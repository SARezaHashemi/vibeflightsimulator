/**
 * Flight model.
 *
 * Translation is force-driven: thrust along the nose, gravity, and the
 * aerodynamic lift, drag and side force from `aero.js`. Rotation is a proper
 * rigid body: control, damping and stability moments change an angular
 * momentum vector about the body axes, and the attitude follows the resulting
 * angular velocity. With no moment, angular momentum therefore stays fixed.
 *
 * Energy is audited rather than invented. The point mass integrates with
 * velocity Verlet, so gravity conserves mechanical energy exactly, while
 * thrust and drag change it only by the work they physically do. Engine power
 * spools towards the throttle lever and lapses with air density and speed, so
 * opening the throttle adds energy at a finite rate. On the ground the same
 * body carries surface-dependent grip, rolling resistance and slope gravity,
 * while the nosewheel steers a bicycle model.
 *
 * The aircraft is a small state machine: AIR → GROUND on a good touchdown,
 * AIR → CRASHED on a bad one, GROUND → AIR when you rotate again.
 */

import {
  MASS, GRAVITY, TRIM_ALPHA,
  PITCH_RATE, ROLL_RATE, YAW_RATE, THROTTLE_RATE,
  ENGINE_SPOOL_UP, ENGINE_SPOOL_DOWN,
  INERTIA, ELEVATOR_AUTH, AILERON_AUTH, RUDDER_AUTH,
  PITCH_DAMP, ROLL_DAMP, YAW_DAMP, STABILITY_PITCH, STABILITY_YAW,
  CONTROL_SMOOTHING, START, START_SPEED, START_THROTTLE,
  GEAR_RATE, GEAR_HEIGHT,
  ROTATE_SPEED, WHEELBASE, MAX_STEER, STEER_RATE,
  SURFACE_HANDLING, MAX_GROUND_SLOPE, MAX_TAXI_SLOPE, TAXI_SLOPE_SPEED,
  TAKEOFF_GRACE,
} from './config.js';
import {
  clamp, cross, dot, length, normalise, qaxis, qconj, qfromBasis, qmul, qnorm,
  qrot, qspin, scale,
} from './math.js';
import { Body, applyGravity, mechanicalEnergy, rotationalEnergy } from './physics.js';
import { applyAerodynamics, applyGroundDrag, flowAngles } from './aero.js';
import { thrustForce } from './propulsion.js';
import { AIRPORT, SURFACE, surfaceAt, surfaceHeight } from './world.js';
import { treeStrike } from './trees.js';

export const STATE = Object.freeze({ AIR: 'air', GROUND: 'ground', CRASHED: 'crashed' });

/** Telemetry used when the wing is not doing anything (parked, crashed). */
const AERO_IDLE = Object.freeze({
  alpha: 0, beta: 0, speed: 0, qbar: 0,
  cl: 0, cd: 0, lift: 0, drag: 0, gLoad: 1, stalled: false,
});

/** Principal moments of inertia about the body x (pitch), y (yaw), z (roll) axes. */
const INERTIA_BODY = Object.freeze({
  x: INERTIA.pitch,
  y: INERTIA.yaw,
  z: INERTIA.roll,
});

/** Shortest signed angle between two headings. */
const angleDelta = (a, b) => Math.atan2(Math.sin(a - b), Math.cos(a - b));

export class Aircraft {
  constructor() {
    this.reset();
  }

  reset() {
    this.body = new Body({
      mass: MASS,
      position: START,
      velocity: { x: 0, y: 0, z: -START_SPEED },
    });
    this.position = this.body.position;

    this.orientation = qaxis(1, 0, 0, TRIM_ALPHA);
    this.throttle = START_THROTTLE;                   // lever position, 0..1
    this.engine = START_THROTTLE;                     // spooled power, 0..1

    // Angular momentum is kept in world axes so it is conserved exactly when
    // no moment acts. Angular velocity is the body-frame view of it.
    this.angularMomentum = { x: 0, y: 0, z: 0 };
    this.angularVelocity = { x: 0, y: 0, z: 0 };
    this.omega = { pitch: 0, roll: 0, yaw: 0 };       // body rates, rad/s
    this.surfaces = { pitch: 0, roll: 0, yaw: 0 };    // smoothed control deflections
    this.elapsed = 0;

    this.state = STATE.AIR;
    this.crashReason = '';
    this.gearDown = true;
    this.gearPosition = 1;
    this.groundHeading = 0;
    this.steerAngle = 0;
    this.groundIgnoreUntil = 0;
    this.landedAt = -1;

    this.surface = surfaceAt(START.x, START.z);
    this.lastLanding = null;
    this.crashImpact = null;

    this.aero = AERO_IDLE;
    this.#refreshBasis();
  }

  /* ------------------------------------------------------------ readings */

  get isAirborne()  { return this.state === STATE.AIR; }
  get isGrounded()  { return this.state === STATE.GROUND; }
  get isCrashed()   { return this.state === STATE.CRASHED; }

  /** Airspeed, from the integrated velocity vector. */
  get speed() {
    return this.body.speed;
  }

  /** Positive when climbing, m/s. */
  get verticalSpeed() {
    return this.state === STATE.AIR ? this.body.velocity.y : 0;
  }

  /** Angle of attack in radians; positive means nose above the flight path. */
  get angleOfAttack() {
    return this.aero.alpha;
  }

  /** Wing lift in g. One g means the wing is carrying the aircraft's weight. */
  get gLoad() {
    return this.aero.gLoad;
  }

  /** Acceleration along the nose in m/s², from the last integrated step. */
  get longitudinalAcceleration() {
    const a = this.body.acceleration;
    return a.x * this.forward.x + a.y * this.forward.y + a.z * this.forward.z;
  }

  get isStalled() {
    return this.state === STATE.AIR && this.aero.stalled;
  }

  /** Total mechanical energy in joules: altitude, translation and rotation. */
  get totalEnergy() {
    return mechanicalEnergy(this.body, GRAVITY) + this.rotationalKineticEnergy;
  }

  /** Rotational kinetic energy in joules, from the angular momentum state. */
  get rotationalKineticEnergy() {
    const local = qrot(qconj(this.orientation), this.angularMomentum);
    return rotationalEnergy(local, this.angularVelocity);
  }

  get altitudeAgl() {
    // The origin is the centre of gravity; the wheels are GEAR_HEIGHT below it,
    // so this reads zero when the aircraft is sitting on the ground.
    return this.position.y - GEAR_HEIGHT - surfaceHeight(this.position.x, this.position.z);
  }

  get headingRad() {
    return Math.atan2(this.forward.x, -this.forward.z);
  }

  /** Compass heading in degrees, 0 = north (-Z). */
  get heading() {
    return (this.headingRad * 180 / Math.PI + 360) % 360;
  }

  /** Nose up is positive, in radians. */
  get pitchAngle() {
    return Math.asin(clamp(this.forward.y, -1, 1));
  }

  /** Wings level is 0; rolling right is negative, in radians. */
  get bankAngle() {
    return Math.atan2(this.right.y, this.up.y);
  }

  /** How far off the runway centreline heading we are, in radians. */
  get runwayAlignment() {
    const heading = this.headingRad;
    return Math.min(
      Math.abs(angleDelta(heading, AIRPORT.heading)),
      Math.abs(angleDelta(heading, AIRPORT.heading + Math.PI)),
    );
  }

  /** Angle between where the nose points and where the aircraft is going, radians. */
  get driftAngle() {
    const v = this.body.velocity;
    if (Math.hypot(v.x, v.z) < 1) return 0;
    const track = Math.atan2(v.x, -v.z);
    return Math.abs(angleDelta(track, this.headingRad));
  }

  /* -------------------------------------------------------------- actions */

  toggleGear() {
    if (this.state === STATE.AIR) this.gearDown = !this.gearDown;
  }

  /* --------------------------------------------------------------- update */

  update(dt, axes) {
    if (this.state === STATE.CRASHED) return;
    this.elapsed += dt;

    // Gear travel, whether we are flying or rolling.
    const target = this.gearDown ? 1 : 0;
    const step = GEAR_RATE * dt;
    this.gearPosition += clamp(target - this.gearPosition, -step, step);

    if (this.state === STATE.GROUND) this.#updateGround(dt, axes);
    else this.#updateAir(dt, axes);
  }

  #updateAir(dt, axes) {
    /* --- control surfaces, smoothed so taps don't jolt --- */
    const slew = 1 - Math.exp(-CONTROL_SMOOTHING * dt);
    this.surfaces.pitch += (axes.pitch - this.surfaces.pitch) * slew;
    this.surfaces.roll  += (axes.roll  - this.surfaces.roll)  * slew;
    this.surfaces.yaw   += (axes.yaw   - this.surfaces.yaw)   * slew;

    /* --- rigid-body rotation: moments → angular momentum → angular velocity --- */
    const altitude = Math.max(0, this.position.y);
    const flow = flowAngles(this.body, this, altitude);
    const qbar = clamp(flow.qbar / 1000, 0.2, 15); // kPa

    // Body axes: x pitches, y yaws, z rolls. The control conventions in the
    // input and HUD are positive nose-up / right-roll / right-yaw, which is
    // why the y and z signs are inverted here.
    const w = this.angularVelocity;
    const omegaPitch = w.x;
    const omegaYaw = -w.y;
    const omegaRoll = -w.z;

    const momentPitch =
      qbar * PITCH_DAMP * (PITCH_RATE * ELEVATOR_AUTH * this.surfaces.pitch - omegaPitch)
      - STABILITY_PITCH * qbar * (flow.alpha - TRIM_ALPHA);
    const momentRoll =
      qbar * ROLL_DAMP * (ROLL_RATE * AILERON_AUTH * this.surfaces.roll - omegaRoll);
    const momentYaw =
      qbar * YAW_DAMP * (YAW_RATE * RUDDER_AUTH * this.surfaces.yaw - omegaYaw)
      + STABILITY_YAW * qbar * flow.beta;

    let q = this.orientation;
    const torqueWorld = qrot(q, { x: momentPitch, y: -momentYaw, z: -momentRoll });
    const L = this.angularMomentum;
    L.x += torqueWorld.x * dt;
    L.y += torqueWorld.y * dt;
    L.z += torqueWorld.z * dt;

    // ω = I⁻¹ L in the body frame. With no torque L is untouched, so angular
    // momentum is conserved; the gyroscopic coupling falls out of carrying it
    // in world axes and viewing it in body axes.
    const localL = qrot(qconj(q), L);
    w.x = localL.x / INERTIA_BODY.x;
    w.y = localL.y / INERTIA_BODY.y;
    w.z = localL.z / INERTIA_BODY.z;

    this.orientation = qspin(q, w, dt);
    this.#refreshBasis();

    // Keep the old scalar rates in sync for telemetry and any outside readers.
    this.omega.pitch = w.x;
    this.omega.roll = -w.z;
    this.omega.yaw = -w.y;

    /* --- throttle lever and engine spool --- */
    if (axes.throttle !== 0) {
      this.throttle = clamp(this.throttle + axes.throttle * THROTTLE_RATE * dt, 0, 1);
    }
    const spool = this.throttle > this.engine ? ENGINE_SPOOL_UP : ENGINE_SPOOL_DOWN;
    this.engine += (this.throttle - this.engine) * (1 - Math.exp(-spool * dt));

    /* --- physics: forces in newtons, integrated over the frame --- */
    const body = this.body;
    body.clearForces();

    applyGravity(body, GRAVITY);

    // Engine thrust acts along the nose. The propeller converts the spooled
    // shaft power into thrust, so acceleration is strong at low speed and
    // fades as the aircraft approaches its top speed.
    body.applyForce(scale(this.forward, this.#thrustForce(altitude)));

    // Wings and airframe: a real lift curve and drag polar. Lift banks with the
    // wings, drag rises with the square of speed and of lift, and the wing
    // stalls when the angle of attack gets too high.
    this.aero = applyAerodynamics(body, this, altitude, this.gearPosition);

    // Sweep the frame's movement through the woods. Testing the segment asks
    // "did we pass through a tree?" rather than only "are we inside one now?",
    // so a fast aircraft cannot tunnel through a crown between frames.
    const previous = { x: this.position.x, y: this.position.y, z: this.position.z };
    body.step(dt);

    if (treeStrike(previous, this.position)) return this.#crash('HIT TREE');

    /* --- ground contact --- */
    const surface = surfaceAt(this.position.x, this.position.z);
    this.surface = surface;
    if (this.position.y <= surface.height + GEAR_HEIGHT && this.elapsed > this.groundIgnoreUntil) {
      this.#touchdown(surface);
    }
  }

  #updateGround(dt, axes) {
    const body = this.body;
    const surface = surfaceAt(this.position.x, this.position.z);

    // Water is not a taxi surface. Rolling into it ends the flight just as
    // surely as landing in it.
    if (surface.kind === SURFACE.WATER) return this.#crash('DITCHED', surface);

    const handling = SURFACE_HANDLING[surface.kind] ?? SURFACE_HANDLING.grass;
    this.surface = surface;
    this.aero = AERO_IDLE;

    /* --- nosewheel steering: a simple bicycle model --- */
    const steerInput = clamp(axes.yaw + axes.roll * 0.6, -1, 1);
    const steerStep = STEER_RATE * handling.steer * dt;
    const steerLimit = MAX_STEER * handling.steer;
    this.steerAngle += clamp(steerInput * steerLimit - this.steerAngle, -steerStep, steerStep);

    // Yaw rate is curvature times forward speed. Tyre grip caps the curvature,
    // so the aircraft can turn tightly at taxi speed but only gently at
    // take-off speed — and it cannot pivot while stationary. Grass and broken
    // ground offer less grip, so turns there are wider and slide more.
    const forwardSpeed = Math.max(
      0,
      body.velocity.x * this.forward.x + body.velocity.z * this.forward.z,
    );
    if (forwardSpeed > 0.05) {
      const gripCurvature = handling.grip / (forwardSpeed * forwardSpeed);
      const curvature = clamp(Math.tan(this.steerAngle) / WHEELBASE, -gripCurvature, gripCurvature);
      this.groundHeading += forwardSpeed * curvature * dt;
    }

    // Orientation follows the nosewheel and the local slope, even though the
    // velocity does not.
    this.#applyGroundPose(surface);

    /* --- throttle lever and engine spool --- */
    if (axes.throttle !== 0) {
      this.throttle = clamp(this.throttle + axes.throttle * THROTTLE_RATE * dt, 0, 1);
    }
    const spool = this.throttle > this.engine ? ENGINE_SPOOL_UP : ENGINE_SPOOL_DOWN;
    this.engine += (this.throttle - this.engine) * (1 - Math.exp(-spool * dt));

    /* --- forces on the rolling airframe --- */
    body.clearForces();

    // Engine thrust acts along the nose, which on a slope is tilted with it.
    body.applyForce(scale(this.forward, this.#thrustForce(this.position.y)));

    const horizontal = Math.hypot(body.velocity.x, body.velocity.z);
    const brakeHeld = axes.throttle < 0 && this.throttle <= 0.001;

    // Gravity down the slope. The flat airfield has no gradient, so this only
    // matters when taxiing over open country. A stationary aircraft with the
    // brake set holds still instead of creeping downhill.
    if (!(brakeHeld && horizontal < 0.3)) {
      body.applyForce({
        x: -surface.gradient.x * GRAVITY * MASS,
        y: 0,
        z: -surface.gradient.z * GRAVITY * MASS,
      });
    }

    // Aerodynamic drag. The wheels carry the weight, so only parasite and gear
    // drag act while the aircraft is rolling.
    applyGroundDrag(body, this.position.y, this.gearPosition);

    if (horizontal > 0.02) {
      // Rolling resistance and brakes oppose the direction of travel. Capping
      // at speed/dt keeps a stopped aircraft from being pushed backwards.
      const rolling = handling.rolling;
      const braking = brakeHeld ? handling.brake : 0;
      const stop = Math.min((rolling + braking) * MASS, (horizontal / dt) * MASS);
      body.applyForce({
        x: -(body.velocity.x / horizontal) * stop,
        y: 0,
        z: -(body.velocity.z / horizontal) * stop,
      });
    }

    // Lateral tyre grip pulls the velocity into line with the nose. It is
    // limited, so a fast turn slides a little instead of snapping to rails.
    const lateral = body.velocity.x * this.right.x + body.velocity.z * this.right.z;
    const grip = clamp(
      -lateral * handling.slip * MASS,
      -handling.grip * MASS,
      handling.grip * MASS,
    );
    body.applyForce({ x: this.right.x * grip, y: 0, z: this.right.z * grip });

    const previous = { x: this.position.x, y: this.position.y, z: this.position.z };
    body.step(dt);
    body.velocity.y = 0;              // the wheels carry the weight

    const after = surfaceAt(this.position.x, this.position.z);
    this.#applyGroundPose(after);

    // Taxiing off the pavement into the woods is just as final as flying into
    // them.
    if (treeStrike(previous, this.position)) return this.#crash('HIT TREE', after);

    if (after.kind === SURFACE.WATER) return this.#crash('DITCHED', after);

    /*
     * Runway excursions are speed-dependent. Trickling off the pavement at
     * taxi pace is harmless; leaving at take-off speed is not. Entering broken
     * ground is more punishing still, because the wheels cannot roll over it.
     */
    if (surface.kind === SURFACE.RUNWAY && after.kind !== SURFACE.RUNWAY &&
        this.speed > SURFACE_HANDLING.grass.taxiLimit) {
      return this.#crash('RAN OFF RUNWAY', after);
    }

    if (after.kind === SURFACE.ROUGH && surface.kind !== SURFACE.ROUGH &&
        this.speed > SURFACE_HANDLING.rough.taxiLimit) {
      return this.#crash('ROUGH GROUND', after);
    }

    if (after.slope > MAX_TAXI_SLOPE && this.speed > TAXI_SLOPE_SPEED) {
      return this.#crash('STEEP GROUND', after);
    }

    /* --- rotate and fly again --- */
    if (this.speed > ROTATE_SPEED && axes.pitch > 0) this.#takeOff();
  }

  /* ---------------------------------------------------------- transitions */

  #touchdown(surface) {
    if (surface.kind === SURFACE.WATER) return this.#crash('DITCHED', surface);

    const handling = SURFACE_HANDLING[surface.kind] ?? SURFACE_HANDLING.grass;
    const impact = this.#impactSpeed(surface);
    const bank = Math.abs(this.#bankRelativeTo(surface.normal));
    const alignment = Math.max(
      this.driftAngle,
      surface.kind === SURFACE.RUNWAY ? this.runwayAlignment : 0,
    );

    // A gear-up arrival is a belly landing, which ends the flight even if it is
    // otherwise gentle. Then judge the arrival against the surface it is on:
    // a grass field forgives less than asphalt, and broken ground less again.
    // Impact comes before speed so flying into a hillside reads as a terrain
    // impact rather than merely an over-fast touchdown.
    if (this.gearPosition < 0.95)            return this.#crash('GEAR UP', surface);
    if (impact > handling.maxDescent) {
      const reason = surface.slope > 0.12 ? 'TERRAIN IMPACT' : 'HARD LANDING';
      return this.#crash(reason, surface);
    }
    if (this.speed > handling.maxSpeed)      return this.#crash('TOO FAST', surface);
    if (bank > handling.maxBank)             return this.#crash('WING DOWN', surface);
    if (alignment > handling.maxAlign)       return this.#crash('CRABBED', surface);
    if (surface.slope > handling.maxSlope)   return this.#crash('STEEP TERRAIN', surface);

    this.#land(surface, impact);
  }

  #land(surface, impact) {
    this.state = STATE.GROUND;
    this.landedAt = this.elapsed;
    this.surface = surface;

    // The wheels follow the nose, but the aircraft keeps its momentum; any
    // sideways drift is left for the tyres to pull straight.
    this.groundHeading = this.headingRad;
    this.steerAngle = 0;
    this.#stopRotation();
    this.body.velocity.y = 0;

    const handling = SURFACE_HANDLING[surface.kind] ?? SURFACE_HANDLING.grass;
    const speed = this.body.speed;
    this.lastLanding = {
      surface: surface.kind,
      sink: impact,
      speed,
      quality: impact > handling.maxDescent * 0.75 ? 'firm' : 'smooth',
    };

    this.#applyGroundPose(surface);
  }

  #takeOff() {
    this.state = STATE.AIR;
    this.landedAt = -1;
    this.steerAngle = 0;
    this.groundIgnoreUntil = this.elapsed + TAKEOFF_GRACE;

    // Rotate to a shallow climb, then hand control back to the flyer.
    const yaw = qaxis(0, 1, 0, -this.groundHeading);
    this.orientation = qnorm(qmul(yaw, qaxis(1, 0, 0, 0.10)));
    this.#refreshBasis();
    this.position.y += 1.2;

    // Keep the rolling velocity level. Rotating the nose away from it gives
    // the wing the angle of attack that starts the climb.
    const speed = this.body.speed;
    this.body.setVelocity(
      Math.sin(this.groundHeading) * speed,
      0,
      -Math.cos(this.groundHeading) * speed,
    );
    this.#stopRotation();
  }

  #crash(reason, surface = null) {
    const where = surface ?? surfaceAt(this.position.x, this.position.z);
    this.state = STATE.CRASHED;
    this.crashReason = reason;
    this.surface = where;
    this.crashImpact = {
      speed: this.speed,
      sink: this.#impactSpeed(where),
      surface: where.kind,
    };
    this.aero = AERO_IDLE;
    this.engine = 0;
    this.throttle = 0;
    this.#stopRotation();
    this.position.y = where.height + GEAR_HEIGHT;
  }

  /* -------------------------------------------------------------- helpers */

  #refreshBasis() {
    this.forward = qrot(this.orientation, { x: 0, y: 0, z: -1 });
    this.up = qrot(this.orientation, { x: 0, y: 1, z: 0 });
    this.right = qrot(this.orientation, { x: 1, y: 0, z: 0 });
  }

  /** Clear all rotational momentum, e.g. on the ground or after a crash. */
  #stopRotation() {
    this.angularMomentum.x = 0;
    this.angularMomentum.y = 0;
    this.angularMomentum.z = 0;
    this.angularVelocity.x = 0;
    this.angularVelocity.y = 0;
    this.angularVelocity.z = 0;
    this.omega.pitch = 0;
    this.omega.roll = 0;
    this.omega.yaw = 0;
  }

  /**
   * Thrust force from the spooled engine, in newtons.
   *
   * The propeller, not the throttle, sets the thrust: a 180 kW engine driving
   * a 1.9 m propeller gives roughly 4.5 kN at rest and then falls as 1/V in
   * forward flight. Thin air reduces both shaft power and static thrust.
   *
   * The propeller sees the component of velocity along the nose, not the total
   * speed, so a vertical dive does not unload it the way forward flight does.
   */
  #thrustForce(altitude) {
    const v = this.body.velocity;
    const forwardSpeed = Math.max(0, v.x * this.forward.x + v.y * this.forward.y + v.z * this.forward.z);
    return thrustForce(altitude, this.engine, forwardSpeed);
  }

  /** Sit on the wheels, nose along the ground heading and attitude on the slope. */
  #applyGroundPose(surface = null) {
    const ground = surface ?? surfaceAt(this.position.x, this.position.z);
    this.surface = ground;

    const heading = this.groundHeading;
    const hx = Math.sin(heading);
    const hz = -Math.cos(heading);

    // Clamp the local grade so a cliff edge cannot throw the aircraft into a
    // silly attitude. The pose follows the hill beneath it.
    const gx = clamp(ground.gradient.x, -MAX_GROUND_SLOPE, MAX_GROUND_SLOPE);
    const gz = clamp(ground.gradient.z, -MAX_GROUND_SLOPE, MAX_GROUND_SLOPE);

    const forward = normalise({ x: hx, y: hx * gx + hz * gz, z: hz });
    const normal = normalise({ x: -gx, y: 1, z: -gz });

    let right = cross(forward, normal);
    if (length(right) < 1e-3) right = { x: Math.cos(heading), y: 0, z: Math.sin(heading) };
    right = normalise(right);
    const up = normalise(cross(right, forward));

    this.orientation = qfromBasis(right, up, forward);
    this.#refreshBasis();
    this.#stopRotation();
    this.position.y = ground.height + GEAR_HEIGHT;
  }

  /** Closing speed along the surface normal, m/s. */
  #impactSpeed(surface) {
    const v = this.body.velocity;
    const n = surface.normal;
    return Math.max(0, -(v.x * n.x + v.y * n.y + v.z * n.z));
  }

  /** Bank relative to the surface, in radians; zero means the wings sit level on it. */
  #bankRelativeTo(normal) {
    return Math.atan2(dot(this.right, normal), dot(this.up, normal));
  }
}
