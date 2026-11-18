/**
 * Engine and propeller.
 *
 * The throttle controls shaft power, not thrust. The engine makes a power that
 * falls with air density, and the propeller converts that power into thrust in
 * two different ways depending on how fast the aircraft is moving:
 *
 *   - at rest, momentum theory limits the thrust to
 *     T = η_static · ∛(2·ρ·A·P²)
 *   - in forward flight, the same shaft power can only produce
 *     T = η · P / V
 *
 * Taking the smaller of the two gives a realistic constant-thrust region at
 * low speed and a 1/V thrust lapse at cruise. That is why a light aircraft
 * accelerates briskly on the runway and then runs out of acceleration as it
 * approaches its top speed.
 *
 * Units are SI: watts, newtons, metres, seconds.
 */

import {
  ENGINE_POWER, ENGINE_IDLE, PROP_DIAMETER, PROP_EFFICIENCY,
  PROP_STATIC_EFFICIENCY, RHO_SEA,
} from './config.js';
import { airDensity } from './aero.js';

const DISC_AREA = Math.PI * (PROP_DIAMETER / 2) ** 2;

/**
 * Shaft power available at an altitude and spooled throttle setting, in W.
 *
 * A naturally aspirated piston engine makes roughly density-proportional
 * power, so the same throttle setting gives less power high up.
 */
export function enginePower(altitude, spool) {
  const densityRatio = airDensity(altitude) / RHO_SEA;
  const lever = ENGINE_IDLE + (1 - ENGINE_IDLE) * spool;
  return ENGINE_POWER * lever * densityRatio;
}

/**
 * Propeller thrust in newtons for a given shaft power and forward speed.
 *
 * At very low speed the momentum-theory static limit applies; above the
 * crossover speed the propeller is power-limited and thrust falls as 1/V.
 */
export function propellerThrust(power, speed, altitude) {
  if (power <= 0) return 0;

  const density = airDensity(altitude);
  const staticThrust = PROP_STATIC_EFFICIENCY *
    Math.cbrt(2 * density * DISC_AREA * power * power);
  const dynamicThrust = PROP_EFFICIENCY * power / Math.max(speed, 1e-3);

  return Math.min(staticThrust, dynamicThrust);
}

/** Thrust force in newtons for the current engine state and airspeed. */
export function thrustForce(altitude, spool, speed) {
  return propellerThrust(enginePower(altitude, spool), speed, altitude);
}
