/**
 * Trees, scattered deterministically over the terrain.
 *
 * Placement is grouped by terrain cell so a tree is always painted after the
 * ground it stands on and before the nearer cells in front of it. Each cell
 * that holds trees becomes one layer for the terrain's painter's sort, using
 * the cell's own sorting depth. Woodland comes from a coarse hash mask, so the
 * landscape has copses and open fields rather than a uniform sprinkle.
 *
 * Near trees are low-poly solids: a trunk prism and a canopy lofted through a
 * stack of polygonal rings. The faces are back-face culled, lit from the sun's
 * direction and painted back to front, so a tree shows real depth as the view
 * moves around it. Far trees collapse to a single flat silhouette, where the
 * facets would be smaller than a pixel anyway. Everything blends into the
 * same haze as the terrain.
 *
 * Placement lives in `cellTrees()` and is shared with the flight model, so a
 * tree the renderer draws is exactly the tree the aircraft can hit. Collision
 * uses a vertical cylinder around the crown and is swept along the frame's
 * movement, which stops a fast aircraft tunnelling through a narrow trunk.
 */

import {
  TREE_RANGE, TREE_MAX_DISTANCE, TREE_FADE, TREE_MIN_PIXELS, TREE_NEAR,
  TREE_DETAIL_DISTANCE, TREE_COLLISION_MARGIN,
  TREE_PATCH, TREE_WOODS_DENSITY, TREE_SCATTER_DENSITY,
  TREE_MIN_GROUND, TREE_MAX_GROUND, TREE_MIN_HEIGHT, TREE_MAX_HEIGHT,
  TREE_RUNWAY_CLEARANCE, TREE_APPROACH_CLEARANCE,
  CELL_SIZE, SUN_DIR, HAZE,
} from './config.js';
import { fogFactor } from './terrain.js';
import { AIRPORT, terrainHeight, runwayLocal } from './world.js';
import { clamp } from './math.js';

/* Silhouette profiles: [height fraction, half-width fraction]. The first
 * entry is the bottom of the canopy; the last is the tip. Broadleaves loft
 * these rings into a solid crown, and every species uses the same profile for
 * its distant silhouette, so the low-poly trees keep one consistent shape. */
const SPRUCE = [
  [0.18, 0.34], [0.31, 0.27], [0.28, 0.32],
  [0.45, 0.22], [0.42, 0.27],
  [0.61, 0.16], [0.58, 0.21],
  [0.77, 0.10], [0.74, 0.14],
  [0.91, 0.04], [1.00, 0.00],
];

const PINE = [
  [0.34, 0.24], [0.48, 0.18], [0.46, 0.22],
  [0.62, 0.15], [0.60, 0.19],
  [0.76, 0.11], [0.74, 0.14],
  [0.88, 0.07], [0.86, 0.10],
  [1.00, 0.00],
];

const OAK = [
  [0.30, 0.10], [0.34, 0.21], [0.43, 0.30], [0.55, 0.35],
  [0.68, 0.33], [0.80, 0.26], [0.91, 0.15], [1.00, 0.00],
];

const POPLAR = [
  [0.22, 0.13], [0.30, 0.23], [0.42, 0.29], [0.57, 0.30],
  [0.71, 0.26], [0.83, 0.19], [0.92, 0.11], [1.00, 0.00],
];

const BIRCH = [
  [0.36, 0.06], [0.43, 0.14], [0.52, 0.20], [0.63, 0.21],
  [0.74, 0.18], [0.85, 0.12], [0.93, 0.06], [1.00, 0.00],
];

// Conifer tiers as closed frustums: [bottom height, bottom radius,
// top height, top radius], in fractions of the tree. The tiers overlap so
// their skirts read as branches, the way a spruce or pine actually grows.
const SPRUCE_TIERS = [
  [0.18, 0.34, 0.31, 0.26],
  [0.28, 0.32, 0.45, 0.21],
  [0.42, 0.27, 0.61, 0.15],
  [0.58, 0.21, 0.77, 0.10],
  [0.74, 0.14, 0.91, 0.04],
  [0.91, 0.04, 1.00, 0.00],
];

const PINE_TIERS = [
  [0.34, 0.24, 0.48, 0.17],
  [0.46, 0.22, 0.62, 0.14],
  [0.60, 0.19, 0.76, 0.10],
  [0.74, 0.14, 0.88, 0.06],
  [0.86, 0.10, 1.00, 0.00],
];

const crownWidth = (profile) => Math.max(...profile.map((p) => p[1]));

const TRUNK = [66, 50, 34];

/** Species table: shape, how tall and wide it grows, and its foliage tint. */
const KINDS = [
  { profile: SPRUCE, tiers: SPRUCE_TIERS, canopyBase: 0.18,
    heightMin: 0.85, heightMax: 1.30,
    widthMin: 0.80, widthMax: 1.00, tint: [58, 100, 54] },
  { profile: PINE, tiers: PINE_TIERS, canopyBase: 0.34,
    heightMin: 0.95, heightMax: 1.40,
    widthMin: 0.62, widthMax: 0.80, tint: [64, 106, 50] },
  { profile: OAK, canopyBase: 0.30, heightMin: 0.62, heightMax: 1.00,
    widthMin: 0.95, widthMax: 1.22, tint: [90, 130, 56] },
  { profile: POPLAR, canopyBase: 0.22, heightMin: 0.90, heightMax: 1.28,
    widthMin: 0.55, widthMax: 0.72, tint: [84, 136, 58] },
  { profile: BIRCH, canopyBase: 0.36, heightMin: 0.52, heightMax: 0.84,
    widthMin: 0.60, widthMax: 0.80, tint: [114, 148, 68] },
].map((kind) => ({ ...kind, crown: crownWidth(kind.profile) }));

const EMPTY = Object.freeze([]);

/** Deterministic hash of integers → [0, 1). */
function hash(a, b, c) {
  let h = Math.imul(a | 0, 374761393) ^ Math.imul(b | 0, 668265263) ^ Math.imul(c | 0, 2147483647);
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  h ^= h >>> 16;
  return (h >>> 0) / 4294967296;
}

/** Species choice, weighted towards conifers and round-crowned broadleaves. */
function pickKind(r) {
  if (r < 0.30) return 0;   // spruce
  if (r < 0.56) return 2;   // oak
  if (r < 0.78) return 1;   // pine
  if (r < 0.90) return 3;   // poplar
  return 4;                 // birch
}

const mix = (base, fog, shade = 1) =>
  `rgb(${(base[0] * shade * (1 - fog) + HAZE[0] * fog) | 0},` +
  `${(base[1] * shade * (1 - fog) + HAZE[1] * fog) | 0},` +
  `${(base[2] * shade * (1 - fog) + HAZE[2] * fog) | 0})`;

function fillPolygon(ctx, points) {
  ctx.beginPath();
  ctx.moveTo(points[0].x, points[0].y);
  for (let i = 1; i < points.length; i++) ctx.lineTo(points[i].x, points[i].y);
  ctx.closePath();
  ctx.fill();
}

/**
 * Every tree rooted in cell (gi, gj), in world coordinates.
 *
 * This is the single source of truth for tree placement: the renderer uses it
 * to draw the woods and the flight model uses it to detect a strike. The list
 * is empty for open fields, for cells over water or high rock, and near the
 * runway and its approach corridors.
 */
export function cellTrees(gi, gj) {
  // Coarse woodland mask: copses, hedgerows and open fields.
  const patch = hash(Math.floor(gi / 6), Math.floor(gj / 6), 11);
  const woods = patch > 1 - TREE_PATCH;
  const density = woods ? TREE_WOODS_DENSITY : TREE_SCATTER_DENSITY;
  if (hash(gi, gj, 1) > density) return EMPTY;

  const count = woods ? 2 + Math.floor(hash(gi, gj, 2) * 3) : 1;
  const trees = [];

  for (let n = 0; n < count; n++) {
    const s = n * 7;
    const x = (gi + 0.12 + 0.76 * hash(gi, gj, 20 + s)) * CELL_SIZE;
    const z = (gj + 0.12 + 0.76 * hash(gi, gj, 21 + s)) * CELL_SIZE;

    const ground = terrainHeight(x, z);
    if (ground < TREE_MIN_GROUND || ground > TREE_MAX_GROUND) continue;

    // Keep the runway and its approach corridors clear.
    const local = runwayLocal(x, z);
    if (Math.abs(local.across) < TREE_RUNWAY_CLEARANCE &&
        Math.abs(local.along) < AIRPORT.length / 2 + TREE_APPROACH_CLEARANCE) {
      continue;
    }

    const kind = pickKind(hash(gi, gj, 22 + s));
    const spec = KINDS[kind];

    const scale = TREE_MIN_HEIGHT +
      hash(gi, gj, 23 + s) * (TREE_MAX_HEIGHT - TREE_MIN_HEIGHT);
    const height = scale * (spec.heightMin + hash(gi, gj, 27 + s) * (spec.heightMax - spec.heightMin));
    const width = spec.widthMin + hash(gi, gj, 24 + s) * (spec.widthMax - spec.widthMin);

    trees.push({
      x,
      z,
      ground,
      kind,
      height,
      width,
      trunkWidth: 0.010 + 0.010 * hash(gi, gj, 25 + s),
      shade: 0.82 + 0.36 * hash(gi, gj, 26 + s),
      spin: hash(gi, gj, 28 + s) * Math.PI * 2,
      profile: spec.profile,
      tiers: spec.tiers,
      canopyBase: spec.canopyBase,
      tint: spec.tint,
      // Cylinder used by the flight model's swept strike test.
      radius: spec.crown * height * width,
    });
  }

  return trees;
}

/**
 * The first tree struck by moving from `from` to `to`, or null.
 *
 * The whole frame's movement is tested, not just the final position, so a
 * fast aircraft cannot skip over a narrow crown between frames. Horizontal
 * overlap is a segment/circle test; the portion of the segment inside that
 * circle is then checked against the tree's ground-to-tip height.
 */
export function treeStrike(from, to) {
  // Trees overlap cell borders, so widen the cell range by the largest crown.
  const pad = TREE_MAX_HEIGHT + TREE_COLLISION_MARGIN;
  const gi0 = Math.floor((Math.min(from.x, to.x) - pad) / CELL_SIZE);
  const gi1 = Math.floor((Math.max(from.x, to.x) + pad) / CELL_SIZE);
  const gj0 = Math.floor((Math.min(from.z, to.z) - pad) / CELL_SIZE);
  const gj1 = Math.floor((Math.max(from.z, to.z) + pad) / CELL_SIZE);

  const dx = to.x - from.x;
  const dz = to.z - from.z;
  const travelSq = dx * dx + dz * dz;

  for (let gi = gi0; gi <= gi1; gi++) {
    for (let gj = gj0; gj <= gj1; gj++) {
      for (const tree of cellTrees(gi, gj)) {
        const radius = tree.radius + TREE_COLLISION_MARGIN;
        const fx = from.x - tree.x;
        const fz = from.z - tree.z;

        let t0;
        let t1;
        if (travelSq < 1e-9) {
          if (fx * fx + fz * fz > radius * radius) continue;
          t0 = 0;
          t1 = 0;
        } else {
          const b = 2 * (fx * dx + fz * dz);
          const c = fx * fx + fz * fz - radius * radius;
          const disc = b * b - 4 * travelSq * c;
          if (disc < 0) continue;
          const root = Math.sqrt(disc);
          t0 = Math.max(0, (-b - root) / (2 * travelSq));
          t1 = Math.min(1, (-b + root) / (2 * travelSq));
          if (t0 > t1) continue;
        }

        const ya = from.y + (to.y - from.y) * t0;
        const yb = from.y + (to.y - from.y) * t1;
        const top = tree.ground + tree.height;
        if (Math.max(ya, yb) >= tree.ground && Math.min(ya, yb) <= top) {
          return tree;
        }
      }
    }
  }

  return null;
}

/** A horizontal ring of world points around the tree's vertical axis. */
function makeRing(tree, y, radius, sides) {
  const points = [];
  const step = (Math.PI * 2) / sides;
  for (let i = 0; i < sides; i++) {
    const angle = tree.spin + i * step;
    points.push({
      x: tree.x + Math.cos(angle) * radius,
      y: tree.ground + y * tree.height,
      z: tree.z + Math.sin(angle) * radius,
    });
  }
  return points;
}

/**
 * Back-face cull, light and project one world-space face.
 *
 * Faces are wound outwards, so one dot product against the eye direction is
 * enough to drop the half of the solid pointing away from us. Projecting here
 * (rather than at draw time) means each face is clipped once per frame.
 */
function addFace(points, base, camera, eye, faces, tree, fog) {
  let nx = 0;
  let ny = 0;
  let nz = 0;
  let mx = 0;
  let my = 0;
  let mz = 0;

  for (let i = 0; i < points.length; i++) {
    const a = points[i];
    const b = points[(i + 1) % points.length];
    nx += (a.y - b.y) * (a.z + b.z);
    ny += (a.z - b.z) * (a.x + b.x);
    nz += (a.x - b.x) * (a.y + b.y);
    mx += a.x;
    my += a.y;
    mz += a.z;
  }

  const inv = 1 / points.length;
  mx *= inv;
  my *= inv;
  mz *= inv;

  const len = Math.hypot(nx, ny, nz) || 1;
  nx /= len;
  ny /= len;
  nz /= len;

  if (nx * (eye.x - mx) + ny * (eye.y - my) + nz * (eye.z - mz) <= 0) return;

  const depth =
    (mx - eye.x) * camera.forward.x +
    (my - eye.y) * camera.forward.y +
    (mz - eye.z) * camera.forward.z;
  if (depth < 0.6) return;

  const poly = camera.projectPolygon(points);
  if (!poly) return;

  const lambert = Math.max(0, nx * SUN_DIR.x + ny * SUN_DIR.y + nz * SUN_DIR.z);
  const light = (0.54 + 0.56 * lambert) * tree.shade;
  faces.push({ depth, colour: mix(base, fog, light), poly });
}

/**
 * A solid low-poly tree: a five-sided trunk under a canopy of stacked
 * polygonal rings. Conifers are built from closed frustum tiers, broadleaves
 * from one continuous loft, so the silhouette carries through into 3D.
 *
 * At this range the tree is a few dozen screen pixels wide, so eight sides on
 * near crowns and six further out is enough for the facets to read without
 * weighing down the painter's sort.
 */
function buildVolume(tree, camera, alpha, depth) {
  const eye = camera.position;
  const faces = [];
  const sides = 6;
  const scale = tree.height * tree.width;
  const tint = tree.tint;
  const fog = fogFactor(depth, camera);
  const push = (points, base) => addFace(points, base, camera, eye, faces, tree, fog);

  if (tree.tiers) {
    const tierCount = tree.tiers.length;
    for (let k = 0; k < tierCount; k++) {
      const [y0, r0, y1, r1] = tree.tiers[k];
      const bottom = makeRing(tree, y0, r0 * scale, sides);
      const top = r1 > 0.001 ? makeRing(tree, y1, r1 * scale, sides) : null;
      const tip = top
        ? null
        : { x: tree.x, y: tree.ground + y1 * tree.height, z: tree.z };

      for (let i = 0; i < sides; i++) {
        const j = (i + 1) % sides;
        push(
          top
            ? [bottom[i], top[i], top[j], bottom[j]]
            : [bottom[i], tip, bottom[j]],
          tint,
        );
      }

      // Cap the underside of every tier; from below that is what you see.
      // Internal top caps are hidden by the tier above, so only the top of
      // the whole tree needs one.
      push(bottom, tint);
      if (top && k === tierCount - 1) push(top.slice().reverse(), tint);
    }
  } else {
    const profile = tree.profile;
    const rings = profile.map(([y, w]) => ({
      points: w > 0.001 ? makeRing(tree, y, w * scale, sides) : null,
      y,
    }));

    for (let r = 0; r < rings.length - 1; r++) {
      const low = rings[r];
      const high = rings[r + 1];

      if (!high.points) {
        const tip = { x: tree.x, y: tree.ground + high.y * tree.height, z: tree.z };
        for (let i = 0; i < sides; i++) {
          push([low.points[i], tip, low.points[(i + 1) % sides]], tint);
        }
        continue;
      }

      for (let i = 0; i < sides; i++) {
        const j = (i + 1) % sides;
        push([low.points[i], high.points[i], high.points[j], low.points[j]], tint);
      }
    }

    if (rings[0].points) push(rings[0].points, tint);
    const last = rings[rings.length - 1];
    if (last.points) push(last.points.slice().reverse(), tint);
  }

  // Trunk prism, reaching a little way into the canopy so there is no gap.
  const trunkRadius = Math.max(0.12, tree.height * tree.trunkWidth * 1.2);
  const trunkBottom = makeRing(tree, 0, trunkRadius, 5);
  const trunkTop = makeRing(tree, tree.canopyBase + 0.04, trunkRadius, 5);
  for (let i = 0; i < 5; i++) {
    const j = (i + 1) % 5;
    push(
      [trunkBottom[i], trunkTop[i], trunkTop[j], trunkBottom[j]],
      TRUNK,
    );
  }

  faces.sort((a, b) => b.depth - a.depth);
  return { alpha, faces };
}

/**
 * Build the drawable geometry for one tree, or null if it is outside the view.
 *
 * Near trees are solid volumes. Past `TREE_DETAIL_DISTANCE` their facets are
 * smaller than a pixel, so the tree collapses to one canopy silhouette and a
 * single fill — which is most of the frame's tree cost at long range.
 */
function buildTree(tree, camera) {
  const baseView = camera.toView({ x: tree.x, y: tree.ground, z: tree.z });
  const depth = baseView.z;
  if (depth < TREE_NEAR || depth > TREE_MAX_DISTANCE) return null;

  const topView = camera.toView({ x: tree.x, y: tree.ground + tree.height, z: tree.z });
  if (topView.z < 0.6) return null;

  const base = camera.projectView(baseView);
  const top = camera.projectView(topView);

  const dx = top.x - base.x;
  const dy = top.y - base.y;
  const height = Math.hypot(dx, dy);
  if (height < TREE_MIN_PIXELS) return null;

  const alpha = clamp((TREE_MAX_DISTANCE - depth) / TREE_FADE, 0, 1);
  if (alpha < 0.02) return null;

  if (depth < TREE_DETAIL_DISTANCE) return buildVolume(tree, camera, alpha, depth);

  const fog = fogFactor(depth, camera);
  const ux = dx / height;
  const uy = dy / height;
  const px = -uy;                 // screen-space perpendicular, unit length
  const py = ux;

  // Start the silhouette at the foot so the distant tree reads as a whole.
  const canopyBase = 0.02;
  const cbx = base.x + ux * canopyBase * height;
  const cby = base.y + uy * canopyBase * height;

  // One flat silhouette: left edge up to the tip, right edge back down.
  const outline = [{ x: cbx, y: cby }];
  for (const [v, w] of tree.profile) {
    outline.push({
      x: base.x + ux * v * height - px * w * height * tree.width,
      y: base.y + uy * v * height - py * w * height * tree.width,
    });
  }
  for (let i = tree.profile.length - 1; i >= 0; i--) {
    const [v, w] = tree.profile[i];
    outline.push({
      x: base.x + ux * v * height + px * w * height * tree.width,
      y: base.y + uy * v * height + py * w * height * tree.width,
    });
  }

  return {
    alpha,
    canopyPoly: outline,
    canopyColour: mix(tree.tint, fog, tree.shade * 0.92),
  };
}

/** All tree layers within draw range of the camera. */
export function treeLayers(camera) {
  const eye = camera.position;
  const ci = Math.floor(eye.x / CELL_SIZE);
  const cj = Math.floor(eye.z / CELL_SIZE);

  const layers = [];

  for (let gi = ci - TREE_RANGE; gi <= ci + TREE_RANGE; gi++) {
    for (let gj = cj - TREE_RANGE; gj <= cj + TREE_RANGE; gj++) {
      const trees = cellTrees(gi, gj);
      if (trees.length === 0) continue;

      // Same corner the terrain uses to sort this cell, pulled a hair nearer
      // so the trees are painted after their own patch of ground.
      const cornerX = gi * CELL_SIZE;
      const cornerZ = gj * CELL_SIZE;
      const cornerDepth = camera.toView({
        x: cornerX,
        y: terrainHeight(cornerX, cornerZ),
        z: cornerZ,
      }).z;
      // Cells wholly behind the eye can't contribute anything.
      if (cornerDepth < -CELL_SIZE * 2) continue;

      const shapes = [];
      for (const tree of trees) {
        const shape = buildTree(tree, camera);
        if (shape) shapes.push(shape);
      }

      if (shapes.length === 0) continue;

      layers.push({
        depth: cornerDepth - 0.1,
        draw(ctx) {
          for (const shape of shapes) {
            ctx.globalAlpha = shape.alpha;
            if (shape.canopyPoly) {
              ctx.fillStyle = shape.canopyColour;
              fillPolygon(ctx, shape.canopyPoly);
              continue;
            }
            for (const face of shape.faces) {
              ctx.fillStyle = face.colour;
              fillPolygon(ctx, face.poly);
            }
          }
          ctx.globalAlpha = 1;
        },
      });
    }
  }

  return layers;
}
