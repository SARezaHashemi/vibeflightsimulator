/**
 * Clouds, drawn as filled polygons.
 *
 * Each cloud is a cluster of puffs scattered over a jittered grid around the
 * aircraft. A puff is a jagged outline in a vertical plane fixed in the world
 * (each puff has its own heading, chosen once from its hash), rounded on top,
 * flattened along the base, with a small lit cap drawn over a shaded body.
 * The outline never looks at the camera, so clouds stay put as you turn. Every puff is its own layer for the terrain's painter's sort, so hills
 * hide clouds and clouds hide hills. Clusters come from a hash of their grid
 * cell, so they stay put as the aircraft moves.
 *
 * Outlines are built in world space and projected through the camera's
 * near-plane clipping, which keeps puffs right next to the wing from breaking.
 * Haze blends colour and opacity with the terrain's fog curve.
 */

import {
  CLOUD_CELL, CLOUD_RANGE, CLOUD_DENSITY, CLOUD_BASE_MIN, CLOUD_BASE_MAX,
  CLOUD_MAX_DISTANCE, CLOUD_OPACITY, HAZE,
} from './config.js';
import { fogFactor } from './terrain.js';

const OUTLINE_SIDES = 11;        // vertices per puff outline
const BASE_SQUASH = 0.35;        // how flat the underside is (1 = round)
const SHADE = [168, 182, 200];   // underside colour
const LIT = [255, 255, 255];     // sunlit top colour

/** Deterministic hash of integers → [0, 1). */
function hash(a, b, c) {
  let h = Math.imul(a | 0, 374761393) ^ Math.imul(b | 0, 668265263) ^ Math.imul(c | 0, 2147483647);
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  h ^= h >>> 16;
  return (h >>> 0) / 4294967296;
}

/** Puffs for the cluster in grid cell (i, k), in world coordinates. */
function clusterPuffs(i, k) {
  const cx = (i + hash(i, k, 1)) * CLOUD_CELL;
  const cz = (k + hash(i, k, 2)) * CLOUD_CELL;
  const baseY = CLOUD_BASE_MIN + hash(i, k, 3) * (CLOUD_BASE_MAX - CLOUD_BASE_MIN);
  const size = 500 + hash(i, k, 4) * 700;
  const count = 6 + Math.floor(hash(i, k, 5) * 8);

  const puffs = [];
  for (let n = 0; n < count; n++) {
    const s = 10 + n * 7;
    const u = hash(i, k, s) - 0.5;
    const v = hash(i, k, s + 1) - 0.5;
    const r = size * (0.20 + 0.22 * hash(i, k, s + 2));
    const centreness = Math.max(0, 1 - Math.hypot(u, v) * 1.6);
    puffs.push({
      x: cx + u * size,
      y: baseY + centreness * size * 0.22 + r * 0.25,
      z: cz + v * size,
      r,
      seed: s,
      angle: hash(i, k, s + 3) * Math.PI * 2,
    });
  }
  return puffs;
}

/**
 * Outline of one puff as world-space points, in the puff's own vertical plane.
 * Radius jitter per vertex gives the ragged cumulus edge; the lower half is
 * squashed so the base reads as flat. The plane is fixed in the world, so the
 * shape only changes with viewing angle, the way a real cloud does.
 */
function outline(puff) {
  const across = { x: Math.cos(puff.angle), y: 0, z: Math.sin(puff.angle) };
  const points = [];
  for (let j = 0; j < OUTLINE_SIDES; j++) {
    const t = (j / OUTLINE_SIDES) * Math.PI * 2;
    const jitter = 0.82 + 0.36 * hash(puff.x | 0, puff.z | 0, puff.seed + j);
    const cs = Math.cos(t) * jitter;
    const sn = Math.sin(t) * jitter * (Math.sin(t) < 0 ? BASE_SQUASH : 1);
    points.push({
      x: puff.x + (across.x * cs) * puff.r,
      y: puff.y + sn * puff.r,
      z: puff.z + (across.z * cs) * puff.r,
    });
  }
  return points;
}

/** Fill a screen polygon, if it survives near-plane clipping. */
function fillPolygon(ctx, poly) {
  if (!poly) return;
  ctx.beginPath();
  ctx.moveTo(poly[0].x, poly[0].y);
  for (let i = 1; i < poly.length; i++) ctx.lineTo(poly[i].x, poly[i].y);
  ctx.closePath();
  ctx.fill();
}

const mix = (base, fog) =>
  `rgb(${(base[0] * (1 - fog) + HAZE[0] * fog) | 0},` +
  `${(base[1] * (1 - fog) + HAZE[1] * fog) | 0},` +
  `${(base[2] * (1 - fog) + HAZE[2] * fog) | 0})`;

/**
 * Build depth-sorted layers for the clouds around the camera. Each layer has
 * `depth` and `draw(ctx)`, the same contract as the runway.
 */
export function cloudLayers(camera) {
  const eye = camera.position;
  const ci = Math.floor(eye.x / CLOUD_CELL);
  const ck = Math.floor(eye.z / CLOUD_CELL);
  const layers = [];

  for (let i = ci - CLOUD_RANGE; i <= ci + CLOUD_RANGE; i++) {
    for (let k = ck - CLOUD_RANGE; k <= ck + CLOUD_RANGE; k++) {
      if (hash(i, k, 0) > CLOUD_DENSITY) continue;

      for (const puff of clusterPuffs(i, k)) {
        const depth = camera.toView(puff).z;
        if (depth < puff.r * 1.5 || depth > CLOUD_MAX_DISTANCE) continue;

        const fog = fogFactor(depth, camera);
        const alpha = CLOUD_OPACITY * (1 - fog * 0.5);   // haze thins, never erases
        const body = outline(puff);

        // Lit cap: the same outline lifted straight up, so it sits on top.
        const cap = body.map((p) => ({ x: p.x, y: p.y + puff.r * 0.12, z: p.z }));

        layers.push({
          depth,
          draw(ctx) {
            ctx.globalAlpha = alpha;
            ctx.fillStyle = mix(SHADE, fog);
            fillPolygon(ctx, camera.projectPolygon(body));
            ctx.fillStyle = mix(LIT, fog);
            fillPolygon(ctx, camera.projectPolygon(cap));
            ctx.globalAlpha = 1;
          },
        });
      }
    }
  }
  return layers;
}
