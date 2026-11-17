/**
 * Aerodynamics: air density, the wing's lift curve, the drag polar and the
 * force generator that turns them into forces on a `Body`.
 *
 * The model is still point-mass — attitude is not driven by the aero moments —
 * but the forces are the real ones. Lift rises with the square of airspeed and
 * with angle of attack until the wing stalls; drag is parasitic plus induced
 * plus a large separated-flow term once the wing gives up. Side force from
 * sideslip weathervanes the velocity into line with the nose.
 *
 * Units are SI. Coefficients are dimensionless, forces newtons.
 */

import {
  RHO_SEA, SCALE_HEIGHT, WING_AREA, WING_SPAN, CL_ALPHA, CL_MAX, ALPHA_STALL,
  CD0, CD_STALL, CY_BETA, OSWALD, GEAR_DRAG, GRAVITY,
} from './config.js';
import { clamp, dot, length, normalise, scale, sub } from './math.js';

const ASPECT_RATIO = (WING_SPAN * WING_SPAN) / WING_AREA;
const INDUCED_FACTOR = 1 / (Math.PI * ASPECT_RATIO * OSWALD);

const HALF_PI = Math.PI / 2;
const ALPHA_LIMIT = HALF_PI - 1e-3;

/**
 * Angle of attack where the linear lift slope reaches `CL_MAX`. `ALPHA_STALL`
 * is the aerodynamic stall angle; this is the earlier of the two, so lift is
 * capped without a discontinuity.
 */
export const STALL_ALPHA = Math.min(ALPHA_STALL, CL_MAX / CL_ALPHA);

/** Exponential atmosphere: density at an altitude above sea level, kg/m³. */
export function airDensity(altitude) {
  return RHO_SEA * Math.exp(-Math.max(0, altitude) / SCALE_HEIGHT);
}

/**
 * Lift coefficient as a function of angle of attack.
 *
 * Linear through `STALL_ALPHA`, then the flow separates and lift falls off
 * smoothly to roughly a quarter of its peak at 90° — the wing becomes a flat
 * plate facing the wind, which mostly makes drag.
 */
export function liftCoefficient(alpha) {
  const a = Math.abs(alpha);
  if (a <= STALL_ALPHA) return CL_ALPHA * alpha;

  const peak = CL_ALPHA * STALL_ALPHA;
  const t = clamp((a - STALL_ALPHA) / (HALF_PI - STALL_ALPHA), 0, 1);
  return Math.sign(alpha) * peak * (1 - 0.72 * t * t);
}

/**
 * Drag coefficient from the polar: parasitic + induced + separated flow.
 * Induced drag grows with the square of lift, so pulling g costs speed.
 */
export function dragCoefficient(cl, alpha) {
  const induced = INDUCED_FACTOR * cl * cl;

  const a = Math.abs(alpha);
  let separated = 0;
  if (a > STALL_ALPHA) {
    const t = clamp((a - STALL_ALPHA) / (HALF_PI - STALL_ALPHA), 0, 1);
    separated = CD_STALL * Math.sin(t * HALF_PI) ** 2;
  }

  return CD0 + induced + separated;
}

/**
 * Resolve the body's velocity into the air frame.
 *
 * `alpha` is positive when the relative wind comes from below (nose above the
 * flight path); `beta` is positive when it comes from the right. Both are
 * clamped just short of 90° so the force directions stay well defined.
 */
export function flowAngles(body, frame, altitude) {
  const v = body.velocity;
  const speed = Math.hypot(v.x, v.y, v.z);
  if (speed < 1e-4) return { alpha: 0, beta: 0, speed, qbar: 0 };

  const u = dot(v, frame.forward);
  const w = dot(v, frame.up);
  const side = dot(v, frame.right);

  return {
    alpha: clamp(Math.atan2(-w, u), -ALPHA_LIMIT, ALPHA_LIMIT),
    beta: clamp(Math.atan2(side, Math.abs(u)), -HALF_PI, HALF_PI),
    speed,
    qbar: 0.5 * airDensity(altitude) * speed * speed,
  };
}

/**
 * Turn lift, drag and side force into forces on `body`.
 *
 * Lift is perpendicular to the relative wind and lies in the aircraft's plane
 * of symmetry, so it banks with the wings and vanishes in a vertical climb.
 * Drag opposes the motion. The return value is the telemetry the HUD reads.
 */
export function applyAerodynamics(body, frame, altitude, gear = 0) {
  const flow = flowAngles(body, frame, altitude);
  const { speed, qbar, alpha } = flow;

  const cl = liftCoefficient(alpha);
  const cd = dragCoefficient(cl, alpha) + GEAR_DRAG * gear;
  const qS = qbar * WING_AREA;

  let liftForce = 0;

  if (speed > 1e-4 && qS > 0) {
    const vhat = scale(body.velocity, 1 / speed);

    // Remove the component of "up" along the flight path: what is left is the
    // lift direction. It is zero when the wind comes straight along the wing's
    // normal (a true vertical dive or climb), where a wing makes no lift.
    const dir = sub(frame.up, scale(vhat, dot(frame.up, vhat)));
    if (length(dir) > 1e-4) {
      liftForce = qS * cl;
      body.applyForce(scale(normalise(dir), liftForce));
    }

    // Parasitic + induced + separated drag, against the relative wind.
    body.applyForce(scale(vhat, -qS * cd));

    // Fuselage and fin side force: pushes the velocity back towards the nose.
    body.applyForce(scale(frame.right, -qS * CY_BETA * flow.beta));
  }

  return {
    ...flow,
    cl,
    cd,
    lift: Math.abs(liftForce),
    drag: qS * cd,
    gLoad: liftForce / (body.mass * GRAVITY),
    stalled: Math.abs(alpha) > STALL_ALPHA,
  };
}

/**
 * Rolling drag only. On the ground the wheels carry the weight, so the wing's
 * lift does not matter; parasite and gear drag still slow the aircraft down.
 */
export function applyGroundDrag(body, altitude, gear = 0) {
  const speed = body.speed;
  if (speed < 1e-4) return 0;

  const q = 0.5 * airDensity(altitude) * speed * speed;
  const force = q * WING_AREA * (CD0 + GEAR_DRAG * gear);
  body.applyForce(scale(body.velocity, -force / speed));
  return force;
}
