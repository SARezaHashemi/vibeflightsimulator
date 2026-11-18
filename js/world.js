/**
 * The world: a procedural heightfield, a flattened airfield, and a colour
 * ramp for the surface.
 */

import { WATER, SEA_LEVEL } from './config.js';

/* ------------------------------------------------------------------ relief */

/**
 * Raw terrain height at a world position, in metres.
 * Four octaves of sines give rolling hills; two gaussian bumps make
 * snow-capped landmarks to navigate by.
 */
export function rawTerrainHeight(x, z) {
  return 100
    + 170 * Math.sin(x * 0.00070)       * Math.cos(z * 0.00082)
    +  90 * Math.sin(x * 0.0019  + 1.7) * Math.cos(z * 0.0021  + 0.4)
    +  30 * Math.sin(x * 0.0061  + 0.9) * Math.cos(z * 0.0072  + 2.1)
    +   9 * Math.sin(x * 0.021   + 4.3) * Math.cos(z * 0.017   + 1.2)
    + 760 * Math.exp(-((x - 1700) ** 2 + (z + 3100) ** 2) / 1200000)
    + 520 * Math.exp(-((x + 2400) ** 2 + (z + 1500) ** 2) / 700000);
}

/* ----------------------------------------------------------------- airport */

const smoothstep = (edge0, edge1, x) => {
  const t = Math.min(1, Math.max(0, (x - edge0) / (edge1 - edge0)));
  return t * t * (3 - 2 * t);
};

/**
 * The airfield. It sits due north of the start, on a plateau that is
 * graded out of the hills, so the approach is clear from any direction.
 */
export const AIRPORT = Object.freeze({
  x: 0,
  z: -3200,
  heading: 0,          // radians, 0 = runway pointing north (-Z)
  length: 1700,        // metres, threshold to threshold
  width: 60,
  elevation: 180,      // metres above sea level
  flattenInner: 1000,  // fully level inside this radius
  flattenOuter: 2600,  // blended back into the hills by here
});

/** Ground height including the graded airfield plateau. */
export function terrainHeight(x, z) {
  const raw = rawTerrainHeight(x, z);
  const distance = Math.hypot(x - AIRPORT.x, z - AIRPORT.z);
  if (distance >= AIRPORT.flattenOuter) return raw;

  const blend = smoothstep(AIRPORT.flattenInner, AIRPORT.flattenOuter, distance);
  return AIRPORT.elevation + (raw - AIRPORT.elevation) * blend;
}

/* Runway frame: `along` runs down the centreline, `across` is the width. */
export const RUNWAY_ALONG = { x: Math.sin(AIRPORT.heading), z: -Math.cos(AIRPORT.heading) };
export const RUNWAY_ACROSS = { x: Math.cos(AIRPORT.heading), z: Math.sin(AIRPORT.heading) };

/** Position relative to the runway centre, in runway coordinates. */
export function runwayLocal(x, z) {
  const dx = x - AIRPORT.x;
  const dz = z - AIRPORT.z;
  return {
    along: dx * RUNWAY_ALONG.x + dz * RUNWAY_ALONG.z,
    across: dx * RUNWAY_ACROSS.x + dz * RUNWAY_ACROSS.z,
  };
}

/** Is this point on the paved surface (with an optional margin)? */
export function isOnRunway(x, z, margin = 0) {
  const { along, across } = runwayLocal(x, z);
  return Math.abs(along) <= AIRPORT.length / 2 + margin &&
         Math.abs(across) <= AIRPORT.width / 2 + margin;
}

/** True where the ground is under water. */
export const isWater = (h) => h <= SEA_LEVEL;

/**
 * Height of the solid surface at a world position: the ground where it is dry,
 * or the sea surface where the seabed lies below it.
 *
 * `terrainHeight` still describes the seabed, which is what the water mesh is
 * cut from; anything that physically touches the world (collision, altitude,
 * camera clearance) should use this instead so the flat sea is solid water
 * rather than a hole down to the sea floor.
 */
export function surfaceHeight(x, z) {
  return Math.max(terrainHeight(x, z), SEA_LEVEL);
}

/* --------------------------------------------------------------- surfaces */

/** What the wheels and the keel are touching. */
export const SURFACE = Object.freeze({
  RUNWAY: 'runway',
  GRASS: 'grass',
  ROUGH: 'rough',
  WATER: 'water',
});

const ROUGH_SLOPE = 0.14;   // rise/run; steeper than this is broken ground
const ROUGH_HEIGHT = 200;   // m above sea level; matches the rocky colour band

/** Terrain slope in metres of rise per metre travelled, by central difference. */
export function terrainGradient(x, z, step = 8) {
  return {
    x: (terrainHeight(x + step, z) - terrainHeight(x - step, z)) / (2 * step),
    z: (terrainHeight(x, z + step) - terrainHeight(x, z - step)) / (2 * step),
  };
}

/**
 * Classify the surface under a point and measure its local slope.
 *
 * The runway is the only paved surface. Dry land is grass on gentle low
 * ground and rough on steep or high rocky ground. Water is flat and solid for
 * collision purposes, but it is not a surface an aircraft can roll on.
 *
 * The returned `normal` is a unit vector pointing out of the surface. Landing
 * impact is measured along it, so a level aircraft flying into a hillside has
 * a large closing speed even when its vertical speed is zero.
 */
export function surfaceAt(x, z) {
  const ground = terrainHeight(x, z);
  if (ground <= SEA_LEVEL) {
    return {
      kind: SURFACE.WATER,
      height: SEA_LEVEL,
      slope: 0,
      gradient: { x: 0, z: 0 },
      normal: { x: 0, y: 1, z: 0 },
    };
  }

  const gradient = terrainGradient(x, z);
  const slope = Math.hypot(gradient.x, gradient.z);
  const inv = 1 / Math.hypot(gradient.x, 1, gradient.z);

  let kind;
  if (isOnRunway(x, z, 1)) kind = SURFACE.RUNWAY;
  else if (ground > ROUGH_HEIGHT || slope > ROUGH_SLOPE) kind = SURFACE.ROUGH;
  else kind = SURFACE.GRASS;

  return {
    kind,
    height: Math.max(ground, SEA_LEVEL),
    slope,
    gradient,
    normal: { x: -gradient.x * inv, y: inv, z: -gradient.z * inv },
  };
}

/**
 * Height of the sea surface at a world position, in metres.
 *
 * The sea is a flat plane at `SEA_LEVEL`; these three long, shallow sines only
 * ripple it by a few tens of centimetres. They are anchored to world
 * coordinates and advance with time, so the waves drift across the surface
 * instead of sliding with the camera.
 */
export function waterHeight(x, z, time = 0) {
  return SEA_LEVEL
    + 0.24 * Math.sin(x * 0.028 + z * 0.010 + time * 1.10)
    + 0.14 * Math.sin(z * 0.041 - x * 0.008 - time * 1.45)
    + 0.08 * Math.sin((x + z) * 0.075 + time * 2.10);
}

/* ---------------------------------------------------------------- palette */

/**
 * Base surface colour for a given height, written into `out` as [r, g, b].
 * Bands: water → sand → grass → rock → snow.
 */
export function surfaceColor(h, out) {
  if (h <= SEA_LEVEL) {
    out[0] = WATER[0]; out[1] = WATER[1]; out[2] = WATER[2];
    return out;
  }
  if (h < 5) {
    out[0] = 188; out[1] = 172; out[2] = 126;            // sand
  } else if (h < 200) {
    const t = (h - 5) / 195;                             // grass
    out[0] = 46 + 40 * t; out[1] = 98 + 26 * t; out[2] = 48 + 18 * t;
  } else if (h < 320) {
    const t = (h - 200) / 120;                           // rock
    out[0] = 108 + 26 * t; out[1] = 102 + 20 * t; out[2] = 88 + 18 * t;
  } else {
    const t = Math.min(1, (h - 320) / 150);              // snow
    out[0] = 174 + 32 * t; out[1] = 178 + 32 * t; out[2] = 184 + 34 * t;
  }
  return out;
}
