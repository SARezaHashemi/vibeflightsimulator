/**
 * Scenery that is not part of the terrain mesh — currently the runway.
 *
 * Everything here is flat geometry on the graded airfield plateau, so it is
 * drawn as world-space quads that are projected (and near-plane clipped)
 * through the camera, then interleaved into the terrain's painter order.
 */

import { AIRPORT, RUNWAY_ALONG, RUNWAY_ACROSS } from './world.js';
import { fogFactor } from './terrain.js';
import { HAZE } from './config.js';

const LIFT = 0.35;              // metres above the graded ground
const MARK_LIFT = LIFT + 0.02;  // markings sit on top of the asphalt

const ASPHALT = [56, 58, 62];
const MARKING = [226, 226, 216];

const HALF_LENGTH = AIRPORT.length / 2;
const HALF_WIDTH = AIRPORT.width / 2;

/** Runway-local (along, across) → world position. */
function point(along, across, lift = LIFT) {
  return {
    x: AIRPORT.x + RUNWAY_ALONG.x * along + RUNWAY_ACROSS.x * across,
    y: AIRPORT.elevation + lift,
    z: AIRPORT.z + RUNWAY_ALONG.z * along + RUNWAY_ACROSS.z * across,
  };
}

function tint(base, fog) {
  const r = base[0] * (1 - fog) + HAZE[0] * fog;
  const g = base[1] * (1 - fog) + HAZE[1] * fog;
  const b = base[2] * (1 - fog) + HAZE[2] * fog;
  return `rgb(${r | 0},${g | 0},${b | 0})`;
}

/** A rectangle specified in runway coordinates. */
function quad(ctx, camera, along0, along1, across0, across1, base, lift) {
  const centre = point((along0 + along1) * 0.5, (across0 + across1) * 0.5, lift);
  const fog = fogFactor(camera.toView(centre).z, camera);

  const poly = camera.projectPolygon([
    point(along0, across0, lift),
    point(along0, across1, lift),
    point(along1, across1, lift),
    point(along1, across0, lift),
  ]);
  if (!poly) return;

  ctx.beginPath();
  ctx.moveTo(poly[0].x, poly[0].y);
  for (let i = 1; i < poly.length; i++) ctx.lineTo(poly[i].x, poly[i].y);
  ctx.closePath();
  ctx.fillStyle = tint(base, fog);
  ctx.fill();
}

function drawRunway(ctx, camera) {
  /* ---- paved surface ---- */
  quad(ctx, camera, -HALF_LENGTH, HALF_LENGTH, -HALF_WIDTH, HALF_WIDTH, ASPHALT, LIFT);

  /* ---- edge lines ---- */
  quad(ctx, camera, -HALF_LENGTH, HALF_LENGTH, -HALF_WIDTH + 2, -HALF_WIDTH + 3.4, MARKING, MARK_LIFT);
  quad(ctx, camera, -HALF_LENGTH, HALF_LENGTH, HALF_WIDTH - 3.4, HALF_WIDTH - 2, MARKING, MARK_LIFT);

  /* ---- centreline dashes ---- */
  const dash = 30;
  const gap = 26;
  for (let a = -HALF_LENGTH + 110; a + dash <= HALF_LENGTH - 110; a += dash + gap) {
    quad(ctx, camera, a, a + dash, -0.7, 0.7, MARKING, MARK_LIFT);
  }

  /* ---- threshold bars at both ends ---- */
  const bars = 6;
  const barWidth = 3;
  const barGap = 3;
  const span = bars * barWidth + (bars - 1) * barGap;
  for (let i = 0; i < bars; i++) {
    const c0 = -span / 2 + i * (barWidth + barGap);
    quad(ctx, camera, -HALF_LENGTH + 22, -HALF_LENGTH + 52, c0, c0 + barWidth, MARKING, MARK_LIFT);
    quad(ctx, camera, HALF_LENGTH - 52, HALF_LENGTH - 22, c0, c0 + barWidth, MARKING, MARK_LIFT);
  }
}

/**
 * Depth for the painter order: the nearest corner of the runway. Cells at a
 * greater depth (the ground underneath it) are painted first, cells nearer
 * than the threshold come after.
 */
export function runwayLayer(camera) {
  let nearest = Infinity;
  for (const corner of [
    point(-HALF_LENGTH, -HALF_WIDTH),
    point(-HALF_LENGTH, HALF_WIDTH),
    point(HALF_LENGTH, HALF_WIDTH),
    point(HALF_LENGTH, -HALF_WIDTH),
  ]) {
    nearest = Math.min(nearest, camera.toView(corner).z);
  }

  return {
    depth: Math.max(nearest, 1),
    draw: (ctx) => drawRunway(ctx, camera),
  };
}
