/**
 * Flight model.
 *
 * The aircraft is a point mass driven by forces: thrust along the nose, drag
 * against the airflow, wing lift, gravity, and an aerodynamic term that swings
 * the flight path towards the nose. Attitude is still flown directly with the
 * control rates, and ground handling is still kinematic, so physics is taking
 * over the simulation one part at a time.
 *
 * The aircraft is a small state machine: AIR → GROUND on a good touchdown,
 * AIR → CRASHED on a bad one, GROUND → AIR when you rotate again.
 */

import {
  MASS, MAX_THRUST, DRAG, GRAVITY, CLIMB_PENALTY, STALL_SPEED, SAG_RATE,
  LIFT_CAP, PATH_ALIGN, MAX_PATH_ACCEL,
  PITCH_RATE, ROLL_RATE, YAW_RATE, THROTTLE_RATE,
  RHO_SEA, SCALE_HEIGHT, INERTIA, ELEVATOR_AUTH, AILERON_AUTH, RUDDER_AUTH,
  PITCH_DAMP, ROLL_DAMP, YAW_DAMP, STABILITY_PITCH, STABILITY_YAW,
  CONTROL_SMOOTHING, START, START_SPEED, START_THROTTLE,
  GEAR_RATE, GEAR_DRAG, GEAR_HEIGHT,
  LANDING_MAX_DESCENT, LANDING_MAX_SPEED, LANDING_MAX_BANK, LANDING_MAX_ALIGN,
  ROTATE_SPEED, WHEELBASE, MAX_STEER, STEER_RATE, TIRE_GRIP, TIRE_SLIP,
  ROLL_RESISTANCE, BRAKE_FORCE, TAKEOFF_GRACE,
} from './config.js';
import { clamp, qaxis, qmul, qnorm, qrot, scale, sub } from './math.js';
import { Body, applyDrag, applyGravity } from './physics.js';
import { AIRPORT, terrainHeight, isOnRunway } from './world.js';

export const STATE = Object.freeze({ AIR: 'air', GROUND: 'ground', CRASHED: 'crashed' });

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

    this.orientation = { x: 0, y: 0, z: 0, w: 1 };
    this.throttle = START_THROTTLE;
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

    this.forward = { x: 0, y: 0, z: -1 };
    this.up = { x: 0, y: 1, z: 0 };
    this.right = { x: 1, y: 0, z: 0 };
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

  get altitudeAgl() {
    return this.position.y - terrainHeight(this.position.x, this.position.z);
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

    /* --- rotational dynamics: moments → angular acceleration → body rates --- */
    const altitude = Math.max(0, this.position.y);
    const rho = RHO_SEA * Math.exp(-altitude / SCALE_HEIGHT);
    const qbar = clamp((0.5 * rho * this.body.speed ** 2) / 1000, 0.2, 15); // kPa

    const v = this.body.velocity;
    const vSpeed = Math.max(this.body.speed, 1e-3);
    const flightPath = Math.asin(clamp(v.y / vSpeed, -1, 1));
    const sideslip = Math.atan2(
      v.x * this.right.x + v.y * this.right.y + v.z * this.right.z,
      v.x * this.forward.x + v.y * this.forward.y + v.z * this.forward.z,
    );

    const w = this.omega;
    const momentPitch =
      qbar * PITCH_DAMP * (PITCH_RATE * ELEVATOR_AUTH * this.surfaces.pitch - w.pitch)
      - STABILITY_PITCH * qbar * (this.pitchAngle - flightPath);
    const momentRoll =
      qbar * ROLL_DAMP * (ROLL_RATE * AILERON_AUTH * this.surfaces.roll - w.roll);
    const momentYaw =
      qbar * YAW_DAMP * (YAW_RATE * RUDDER_AUTH * this.surfaces.yaw - w.yaw)
      + STABILITY_YAW * qbar * sideslip;

    w.pitch += (momentPitch / INERTIA.pitch) * dt;
    w.roll  += (momentRoll  / INERTIA.roll)  * dt;
    w.yaw   += (momentYaw   / INERTIA.yaw)   * dt;

    /* --- attitude integrated from body rates (X = pitch up, Z = roll right, Y = yaw right) --- */
    let q = this.orientation;
    q = qmul(q, qaxis(1, 0, 0, w.pitch * dt));
    q = qmul(q, qaxis(0, 0, 1, -w.roll * dt));
    q = qmul(q, qaxis(0, 1, 0, -w.yaw * dt));
    this.orientation = qnorm(q);
    this.#refreshBasis();

    /* --- throttle --- */
    if (axes.throttle !== 0) {
      this.throttle = clamp(this.throttle + axes.throttle * THROTTLE_RATE * dt, 0, 1);
    }

    /* --- physics: forces in newtons, integrated over the frame --- */
    const body = this.body;
    body.clearForces();

    const speed = body.speed;

    applyGravity(body, GRAVITY);

    // Engine thrust acts along the nose.
    body.applyForce(scale(this.forward, MAX_THRUST * this.throttle * MASS));

    // Airframe and gear drag.
    applyDrag(body, DRAG + GEAR_DRAG * this.gearPosition);

    // Wings: hold the aircraft's weight up while they are the right way up,
    // and give up below flying speed. Lifting along `up` (rather than straight
    // up) tilts the support vector in a bank and reproduces the climb/descent
    // exchange of energy with gravity.
    const upright = this.up.y > 0.15;
    const support = upright ? Math.min(1 / this.up.y, LIFT_CAP) : 0;
    const wing = clamp((speed / STALL_SPEED) ** 2, 0, 1);
    body.applyForce(scale(this.up, GRAVITY * support * wing * MASS));

    // Below flying speed the wing cannot carry the aircraft any more.
    const sag = Math.max(0, STALL_SPEED - speed) * SAG_RATE;
    body.applyForce({ x: 0, y: -sag * MASS, z: 0 });

    // Climbing costs extra energy (induced drag) on top of the gravity term.
    const climbDrag = -GRAVITY * this.forward.y * (CLIMB_PENALTY - 1) * MASS;
    body.applyForce(scale(this.forward, climbDrag));

    // Aerodynamic stability: swing the flight path towards the nose instead
    // of welding velocity to it. This is what gives the flight path inertia.
    let align = scale(sub(scale(this.forward, speed), body.velocity), PATH_ALIGN);
    const alignLength = Math.hypot(align.x, align.y, align.z);
    if (alignLength > MAX_PATH_ACCEL) {
      align = scale(align, MAX_PATH_ACCEL / alignLength);
    }
    body.applyForce(scale(align, MASS));

    body.step(dt);

    /* --- ground contact --- */
    const ground = terrainHeight(this.position.x, this.position.z);
    if (this.position.y <= ground + GEAR_HEIGHT && this.elapsed > this.groundIgnoreUntil) {
      this.#touchdown();
    }
  }

  #updateGround(dt, axes) {
    const body = this.body;

    /* --- nosewheel steering: a simple bicycle model --- */
    const steerInput = clamp(axes.yaw + axes.roll * 0.6, -1, 1);
    const steerStep = STEER_RATE * dt;
    this.steerAngle += clamp(steerInput * MAX_STEER - this.steerAngle, -steerStep, steerStep);

    // Yaw rate is curvature times forward speed. Tyre grip caps the curvature,
    // so the aircraft can turn tightly at taxi speed but only gently at
    // take-off speed — and it cannot pivot while stationary.
    const forwardSpeed = Math.max(
      0,
      body.velocity.x * this.forward.x + body.velocity.z * this.forward.z,
    );
    if (forwardSpeed > 0.05) {
      const gripCurvature = TIRE_GRIP / (forwardSpeed * forwardSpeed);
      const curvature = clamp(Math.tan(this.steerAngle) / WHEELBASE, -gripCurvature, gripCurvature);
      this.groundHeading += forwardSpeed * curvature * dt;
    }

    // Orientation follows the nosewheel even though the velocity does not.
    this.#applyGroundPose();

    /* --- throttle --- */
    if (axes.throttle !== 0) {
      this.throttle = clamp(this.throttle + axes.throttle * THROTTLE_RATE * dt, 0, 1);
    }

    /* --- forces on the rolling airframe --- */
    body.clearForces();

    // Engine thrust acts along the nose.
    body.applyForce(scale(this.forward, MAX_THRUST * this.throttle * MASS));

    // Aerodynamic drag.
    applyDrag(body, DRAG);

    const horizontal = Math.hypot(body.velocity.x, body.velocity.z);
    if (horizontal > 0.02) {
      // Rolling resistance and brakes oppose the direction of travel. Capping
      // at speed/dt keeps a stopped aircraft from being pushed backwards.
      const rolling = ROLL_RESISTANCE;
      const braking = axes.throttle < 0 && this.throttle <= 0.001 ? BRAKE_FORCE : 0;
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
    const grip = clamp(-lateral * TIRE_SLIP * MASS, -TIRE_GRIP * MASS, TIRE_GRIP * MASS);
    body.applyForce({ x: this.right.x * grip, y: 0, z: this.right.z * grip });

    body.step(dt);
    body.velocity.y = 0;              // the wheels carry the weight
    this.#applyGroundPose();

    /* --- ran off the pavement --- */
    if (!isOnRunway(this.position.x, this.position.z, 6) && this.speed > 18) {
      this.#crash('RAN OFF RUNWAY');
      return;
    }

    /* --- rotate and fly again --- */
    if (this.speed > ROTATE_SPEED && axes.pitch > 0) this.#takeOff();
  }

  /* ---------------------------------------------------------- transitions */

  #touchdown() {
    const onPavement = isOnRunway(this.position.x, this.position.z, 4);
    const descent = -this.verticalSpeed;

    if (!onPavement)                         return this.#crash('TERRAIN');
    if (this.gearPosition < 0.95)            return this.#crash('GEAR UP');
    if (this.speed > LANDING_MAX_SPEED)      return this.#crash('TOO FAST');
    if (descent > LANDING_MAX_DESCENT)       return this.#crash('HARD LANDING');
    if (Math.abs(this.bankAngle) > LANDING_MAX_BANK) return this.#crash('WING DOWN');
    if (this.runwayAlignment > LANDING_MAX_ALIGN)    return this.#crash('CRABBED');

    this.#land();
  }

  #land() {
    // Squared up with whichever runway direction we touched down on.
    const heading = this.headingRad;
    const forward = AIRPORT.heading;
    const backward = AIRPORT.heading + Math.PI;
    this.groundHeading =
      Math.abs(angleDelta(heading, forward)) <= Math.abs(angleDelta(heading, backward))
        ? forward
        : backward;

    this.state = STATE.GROUND;
    this.landedAt = this.elapsed;
    this.steerAngle = 0;
    this.omega = { pitch: 0, roll: 0, yaw: 0 };

    // Rolling speed only; the descent is absorbed by the undercarriage.
    const speed = Math.hypot(this.body.velocity.x, this.body.velocity.z);
    this.body.setVelocity(
      Math.sin(this.groundHeading) * speed,
      0,
      -Math.cos(this.groundHeading) * speed,
    );
    this.#applyGroundPose();
  }

  #takeOff() {
    this.state = STATE.AIR;
    this.landedAt = -1;
    this.steerAngle = 0;
    this.groundIgnoreUntil = this.elapsed + TAKEOFF_GRACE;

    // Rotate to a shallow climb, then hand control back to the flyer.
    const yaw = qaxis(0, 1, 0, -this.groundHeading);
    this.orientation = qnorm(qmul(yaw, qaxis(1, 0, 0, 0.12)));
    this.#refreshBasis();
    this.position.y += 1.2;

    // Hand the rolling speed to the physics body, pointed up the new nose.
    const speed = this.body.speed;
    this.body.setVelocity(
      this.forward.x * speed,
      this.forward.y * speed,
      this.forward.z * speed,
    );
    this.omega = { pitch: 0, roll: 0, yaw: 0 };
  }

  #crash(reason) {
    this.state = STATE.CRASHED;
    this.crashReason = reason;
    this.position.y = terrainHeight(this.position.x, this.position.z) + GEAR_HEIGHT;
  }

  /* -------------------------------------------------------------- helpers */

  #refreshBasis() {
    this.forward = qrot(this.orientation, { x: 0, y: 0, z: -1 });
    this.up = qrot(this.orientation, { x: 0, y: 1, z: 0 });
    this.right = qrot(this.orientation, { x: 1, y: 0, z: 0 });
  }

  /** Sit flat on the wheels, nose along the ground heading. */
  #applyGroundPose() {
    this.orientation = qaxis(0, 1, 0, -this.groundHeading);
    this.#refreshBasis();
    this.position.y = terrainHeight(this.position.x, this.position.z) + GEAR_HEIGHT;
  }
}
