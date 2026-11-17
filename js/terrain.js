/**
 * Terrain renderer.
 *
 * The ground is a single square grid of quads that follows the aircraft,
 * snapped to world coordinates so the mesh never swims. Cells are painted
 * back to front (painter's algorithm) with flat shading and distance haze.
 * Cell size scales with altitude: the higher you fly, the coarser the grid,
 * which keeps the draw distance and the polygon count roughly constant.
 *
 * `layers` lets other scenery (the runway) be interleaved into that painter
 * order by depth instead of being stamped on top of everything.
 */

import {
  GRID_RADIUS, CELL_MIN, CELL_MAX, CELL_PER_METRE, CELL_BIAS,
  FOG_START, FOG_CURVE, HAZE, SUN_DIR,
  SEA_LEVEL, WATER, WATER_CREST,
} from './config.js';
import { terrainHeight, surfaceColor, waterHeight } from './world.js';
import { clamp } from './math.js';

const M = 2 * GRID_RADIUS + 1;      // grid points per axis
const CELLS = M - 1;                // quads per axis

/** Terrain cell size for a given camera altitude. */
export function cellSizeFor(camera) {
  return clamp(camera.position.y * CELL_PER_METRE + CELL_BIAS, CELL_MIN, CELL_MAX);
}

/** Draw distance for a given camera altitude. */
export const drawDistanceFor = (camera) => cellSizeFor(camera) * GRID_RADIUS * 1.02;

/** Haze blend factor, 0 = clear, 1 = fully lost in the murk. */
export function fogFactor(distance, camera) {
  const span = drawDistanceFor(camera);
  return Math.pow(
    clamp((distance - span * FOG_START) / (span * (1 - FOG_START)), 0, 1),
    FOG_CURVE,
  );
}

/** Push a corner outwards by ~1px so neighbours cannot leave hairline seams. */
function expand(px, py, mx, my) {
  const dx = px - mx;
  const dy = py - my;
  const len = Math.hypot(dx, dy);
  if (len < 1e-6) return [px, py];
  const k = 1.1 / len;
  return [px + dx * k, py + dy * k];
}

/**
 * Paint one water cell as a flat plane broken into wave facets.
 *
 * The sea has no terrain relief: every vertex sits on `SEA_LEVEL` plus a small
 * wave offset. Near the camera the cell is split in two so the swell has some
 * shape; far away one quad is enough because the waves are sub-pixel.
 */
function drawWaterCell(ctx, camera, x0, z0, size, time, fog, near) {
  const segments = near ? 2 : 1;
  const step = size / segments;

  for (let si = 0; si < segments; si++) {
    for (let sj = 0; sj < segments; sj++) {
      const wx = x0 + si * step;
      const wz = z0 + sj * step;
      const wx1 = wx + step;
      const wz1 = wz + step;

      const ha = waterHeight(wx, wz, time);
      const hb = waterHeight(wx1, wz, time);
      const hc = waterHeight(wx1, wz1, time);
      const hd = waterHeight(wx, wz1, time);

      const poly = camera.projectPolygon([
        { x: wx, y: ha, z: wz },
        { x: wx1, y: hb, z: wz },
        { x: wx1, y: hc, z: wz1 },
        { x: wx, y: hd, z: wz1 },
      ]);
      if (!poly) continue;

      // Facet normal from the same edge vectors the land uses.
      const nx = -(hb - ha) * step;
      const nz = -(hd - ha) * step;
      const ny = step * step;
      const nl = Math.hypot(nx, ny, nz) || 1;
      const lambert = (nx * SUN_DIR.x + ny * SUN_DIR.y + nz * SUN_DIR.z) / nl;
      const shade = clamp(0.55 + 0.90 * (lambert * 0.5 + 0.5), 0.52, 1.24);

      // Crests catch a lighter blue; troughs stay deep.
      const average = (ha + hb + hc + hd) * 0.25;
      const crest = clamp((average - SEA_LEVEL) / 0.8 + 0.5, 0, 1);
      const r = (WATER[0] + (WATER_CREST[0] - WATER[0]) * crest) * shade;
      const g = (WATER[1] + (WATER_CREST[1] - WATER[1]) * crest) * shade;
      const b = (WATER[2] + (WATER_CREST[2] - WATER[2]) * crest) * shade;

      let mx = 0;
      let my = 0;
      for (const p of poly) { mx += p.x; my += p.y; }
      mx /= poly.length;
      my /= poly.length;

      ctx.beginPath();
      const first = expand(poly[0].x, poly[0].y, mx, my);
      ctx.moveTo(first[0], first[1]);
      for (let i = 1; i < poly.length; i++) {
        const p = expand(poly[i].x, poly[i].y, mx, my);
        ctx.lineTo(p[0], p[1]);
      }
      ctx.closePath();
      ctx.fillStyle =
        `rgb(${(r * (1 - fog) + HAZE[0] * fog) | 0},` +
        `${(g * (1 - fog) + HAZE[1] * fog) | 0},` +
        `${(b * (1 - fog) + HAZE[2] * fog) | 0})`;
      ctx.fill();
    }
  }
}

export class TerrainRenderer {
  constructor() {
    this.height = new Float32Array(M * M);
    this.ground = new Float32Array(M * M);
    this.screenX = new Float32Array(M * M);
    this.screenY = new Float32Array(M * M);
    this.depth = new Float32Array(M * M);
    this.order = new Int32Array(CELLS * CELLS);
    this.colour = [0, 0, 0];
  }

  draw(ctx, camera, layers = [], time = 0) {
    const W = camera.width;
    const H = camera.height;
    const f = camera.focal;
    const { forward, up, right, position: eye } = camera;

    /* ---- project the grid ---- */
    const cell = cellSizeFor(camera);
    const originX = Math.floor(eye.x / cell) * cell - GRID_RADIUS * cell;
    const originZ = Math.floor(eye.z / cell) * cell - GRID_RADIUS * cell;

    for (let i = 0; i < M; i++) {
      const wx = originX + i * cell;
      for (let j = 0; j < M; j++) {
        const wz = originZ + j * cell;
        const idx = i * M + j;
        const ground = terrainHeight(wx, wz);
        this.ground[idx] = ground;
        // Water cells render at the flat sea surface instead of following the
        // seabed. The terrain height itself is left alone for physics, trees
        // and the camera.
        const h = ground < SEA_LEVEL ? waterHeight(wx, wz, time) : ground;
        this.height[idx] = h;

        const dx = wx - eye.x;
        const dy = h - eye.y;
        const dz = wz - eye.z;

        const depth = dx * forward.x + dy * forward.y + dz * forward.z;
        const cx = dx * right.x + dy * right.y + dz * right.z;
        const cy = dx * up.x + dy * up.y + dz * up.z;
        const safe = Math.max(depth, 0.6);

        this.screenX[idx] = W * 0.5 + f * cx / safe;
        this.screenY[idx] = H * 0.5 - f * cy / safe;
        this.depth[idx] = depth;
      }
    }

    /* ---- collect visible cells, then sort far to near ---- */
    let count = 0;
    for (let i = 0; i < CELLS; i++) {
      for (let j = 0; j < CELLS; j++) {
        const a = i * M + j;
        const b = a + M;
        const c = b + 1;
        const d = a + 1;
        if (this.depth[a] < 0.6 && this.depth[b] < 0.6 &&
            this.depth[c] < 0.6 && this.depth[d] < 0.6) continue;
        this.order[count++] = a;
      }
    }

    const visible = this.order.subarray(0, count);
    visible.sort((a, b) => this.depth[b] - this.depth[a]);

    /* ---- scenery that has to slot into the painter order ---- */
    const merged = layers.filter(Boolean).sort((a, b) => b.depth - a.depth);
    let layerIndex = 0;

    /* ---- paint ---- */
    const maxDistance = drawDistanceFor(camera);
    const { x: lx, y: ly, z: lz } = SUN_DIR;
    const colour = this.colour;

    for (let k = 0; k < count; k++) {
      const a = visible[k];

      while (layerIndex < merged.length && merged[layerIndex].depth >= this.depth[a]) {
        merged[layerIndex++].draw(ctx);
      }

      const b = a + M;
      const c = b + 1;
      const d = a + 1;

      const distance = (this.depth[a] + this.depth[b] + this.depth[c] + this.depth[d]) * 0.25;
      const fog = Math.pow(
        clamp((distance - maxDistance * FOG_START) / (maxDistance * (1 - FOG_START)), 0, 1),
        FOG_CURVE,
      );

      // A cell whose ground is entirely below sea level is water. Draw it as a
      // flat, rippling plane rather than letting the seabed show through the
      // surface. Mixed shore cells keep the land mesh and meet the water at
      // sea level.
      if (this.ground[a] <= SEA_LEVEL && this.ground[b] <= SEA_LEVEL &&
          this.ground[c] <= SEA_LEVEL && this.ground[d] <= SEA_LEVEL) {
        const gi = (a / M) | 0;
        const gj = a - gi * M;
        drawWaterCell(
          ctx, camera,
          originX + gi * cell, originZ + gj * cell,
          cell, time, fog, distance < 1600,
        );
        continue;
      }

      const ha = this.height[a];
      const hb = this.height[b];
      const hc = this.height[c];
      const hd = this.height[d];

      // Flat normal of the cell, from two of its edge vectors.
      const nx = -(hb - ha) * cell;
      const nz = -(hd - ha) * cell;
      const ny = cell * cell;
      const nl = Math.hypot(nx, ny, nz) || 1;
      const lambert = (nx * lx + ny * ly + nz * lz) / nl;
      const shade = clamp(0.64 + 0.50 * (lambert * 0.5 + 0.5), 0.56, 1.16);

      surfaceColor((ha + hb + hc + hd) * 0.25, colour);

      // Cheap per-cell break-up so large flat areas are not dead flat.
      const noise = 0.92 + ((Math.imul(a, 2654435761) >>> 13) & 63) * 0.0025;
      colour[0] *= noise * shade;
      colour[1] *= noise * shade;
      colour[2] *= noise * shade;

      const r = colour[0] * (1 - fog) + HAZE[0] * fog;
      const g = colour[1] * (1 - fog) + HAZE[1] * fog;
      const bl = colour[2] * (1 - fog) + HAZE[2] * fog;

      const x0 = this.screenX[a], y0 = this.screenY[a];
      const x1 = this.screenX[b], y1 = this.screenY[b];
      const x2 = this.screenX[c], y2 = this.screenY[c];
      const x3 = this.screenX[d], y3 = this.screenY[d];
      const mx = (x0 + x1 + x2 + x3) * 0.25;
      const my = (y0 + y1 + y2 + y3) * 0.25;

      const p0 = expand(x0, y0, mx, my);
      const p1 = expand(x1, y1, mx, my);
      const p2 = expand(x2, y2, mx, my);
      const p3 = expand(x3, y3, mx, my);

      ctx.beginPath();
      ctx.moveTo(p0[0], p0[1]);
      ctx.lineTo(p1[0], p1[1]);
      ctx.lineTo(p2[0], p2[1]);
      ctx.lineTo(p3[0], p3[1]);
      ctx.closePath();
      ctx.fillStyle = `rgb(${r | 0},${g | 0},${bl | 0})`;
      ctx.fill();
    }

    while (layerIndex < merged.length) merged[layerIndex++].draw(ctx);
  }
}
