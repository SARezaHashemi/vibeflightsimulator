/**
 * Clouds.
 *
 * Each cloud is a cluster of soft puffs scattered over a jittered grid around
 * the aircraft. Every puff is its own layer for the terrain's painter's sort,
 * so a hill in front of a cloud hides it, and a cloud in front of a hill
 * hides the hill. Clusters are generated from a hash of their grid cell, so
 * they stay put as the aircraft moves.
 *
 * Puffs are pre-rendered once into two soft sprites (sunlit top and shaded
 * underside) and then scaled with perspective. Distance haze is applied with
 * the same fog curve as the terrain so clouds fade into the sky.
 */

import {
  CLOUD_CELL, CLOUD_RANGE, CLOUD_DENSITY, CLOUD_BASE_MIN, CLOUD_BASE_MAX,
  CLOUD_MAX_DISTANCE, CLOUD_OPACITY,
} from './config.js';
import { fogFactor } from './terrain.js';

/** Deterministic hash of integers → [0, 1). */
function hash(a, b, c) {
  let h = Math.imul(a | 0, 374761393) ^ Math.imul(b | 0, 668265263) ^ Math.imul(c | 0, 2147483647);
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  h ^= h >>> 16;
  return (h >>> 0) / 4294967296;
}

/** Soft circular sprite with a bright core fading to nothing at the rim. */
function makeSprite(rgb) {
  const size = 128;
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = size;
  const g = canvas.getContext('2d');
  const [r, gg, b] = rgb;
  const grad = g.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
  grad.addColorStop(0.00, `rgba(${r},${gg},${b},1)`);
  grad.addColorStop(0.55, `rgba(${r},${gg},${b},0.9)`);
  grad.addColorStop(0.85, `rgba(${r},${gg},${b},0.35)`);
  grad.addColorStop(1.00, `rgba(${r},${gg},${b},0)`);
  g.fillStyle = grad;
  g.fillRect(0, 0, size, size);
  return canvas;
}

const LIT = makeSprite([255, 255, 255]);
const SHADE = makeSprite([160, 176, 196]);

/** Puffs for the cluster in grid cell (i, k), in world coordinates. */
function clusterPuffs(i, k) {
  const cx = (i + hash(i, k, 1)) * CLOUD_CELL;
  const cz = (k + hash(i, k, 2)) * CLOUD_CELL;
  const baseY = CLOUD_BASE_MIN + hash(i, k, 3) * (CLOUD_BASE_MAX - CLOUD_BASE_MIN);
  const size = 500 + hash(i, k, 4) * 700;     // m, footprint of the cloud
  const count = 6 + Math.floor(hash(i, k, 5) * 8);

  const puffs = [];
  for (let n = 0; n < count; n++) {
    const s = 10 + n * 7;                     // a different hash per puff
    const u = hash(i, k, s) - 0.5;
    const v = hash(i, k, s + 1) - 0.5;
    const h = hash(i, k, s + 2);
    // Puffs are biggest near the middle and rise into a dome; the base is flat.
    const centreness = 1 - Math.hypot(u, v) * 1.6;
    const r = size * (0.22 + 0.22 * h) * Math.max(0.45, centreness);
    puffs.push({
      x: cx + u * size,
      y: baseY + Math.max(0, centreness) * size * 0.22 + r * 0.25,
      z: cz + v * size,
      r,
    });
  }
  return puffs;
}

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
        const p = camera.project(puff);
        if (!p || p.depth > CLOUD_MAX_DISTANCE || p.depth < puff.r * 1.5) continue;

        const radius = camera.focal * puff.r / p.depth;
        if (radius < 0.5) continue;

        const fog = fogFactor(p.depth, camera);
        const alpha = CLOUD_OPACITY * (1 - fog * 0.9);
        if (alpha <= 0.01) continue;

        layers.push({
          depth: p.depth,
          draw(ctx) {
            ctx.globalAlpha = alpha;
            // Underside first, then the sunlit top sits over it.
            ctx.drawImage(SHADE, p.x - radius, p.y - radius + radius * 0.25, radius * 2, radius * 2);
            ctx.drawImage(LIT, p.x - radius, p.y - radius - radius * 0.1, radius * 2, radius * 2);
            ctx.globalAlpha = 1;
          },
        });
      }
    }
  }
  return layers;
}
