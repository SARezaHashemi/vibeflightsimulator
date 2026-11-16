/**
 * A low-poly aircraft, drawn as flat-shaded polygons.
 *
 * Geometry is authored in body space: +x right, +y up, -z forward (the nose
 * points down -z) and the origin sits at the centre of gravity. `drawAircraft`
 * maps it into the world with the aircraft's own basis, sorts the faces back
 * to front and projects them through the camera, so the same model works from
 * the chase camera and from inside the cockpit.
 *
 * The wheels are modelled with their bottoms at y = -GEAR_HEIGHT, so a level
 * aircraft sitting `GEAR_HEIGHT` above the ground rests exactly on them.
 */

import { HAZE, SUN_DIR } from './config.js';
import { dot } from './math.js';
import { fogFactor } from './terrain.js';

/* --------------------------------------------------------------- palette */

const UPPER  = [235, 239, 244];   // top of the fuselage
const SIDE   = [222, 228, 236];   // upper sides
const BELLY  = [188, 196, 206];   // underside
const ACCENT = [196, 74, 62];     // cheat line on both flanks
const WING   = [214, 221, 230];
const GLASS  = [36, 58, 80];
const METAL  = [122, 128, 136];
const TYRE   = [34, 35, 38];

const EMPTY = Object.freeze([]);

/* ------------------------------------------------------------- geometry */

const vec = (x, y, z) => ({ x, y, z });
const sub = (a, b) => ({ x: a.x - b.x, y: a.y - b.y, z: a.z - b.z });

function centroid(points) {
  let x = 0, y = 0, z = 0;
  for (const p of points) { x += p.x; y += p.y; z += p.z; }
  const n = points.length || 1;
  return { x: x / n, y: y / n, z: z / n };
}

/** Newell's method: a stable normal for any (almost) planar polygon. */
function faceNormal(points) {
  let nx = 0, ny = 0, nz = 0;
  for (let i = 0; i < points.length; i++) {
    const a = points[i];
    const b = points[(i + 1) % points.length];
    nx += (a.y - b.y) * (a.z + b.z);
    ny += (a.z - b.z) * (a.x + b.x);
    nz += (a.x - b.x) * (a.y + b.y);
  }
  const len = Math.hypot(nx, ny, nz) || 1;
  return { x: nx / len, y: ny / len, z: nz / len };
}

/** A face wound so that its normal points away from `outward`'s reference. */
function oriented(points, outward, colour) {
  const n = faceNormal(points);
  const flip = n.x * outward.x + n.y * outward.y + n.z * outward.z < 0;
  return { points: flip ? points.slice().reverse() : points, colour };
}

/**
 * Close the hull between two matching rings. The outward direction of every
 * face is the face centre minus the hull centre, which is correct for any
 * convex shape — boxes, wings, fins, canopies, wheels.
 */
function makeSolid(front, back, colour) {
  const mid = centroid(front.concat(back));
  const faces = [];
  const face = (points) => faces.push(oriented(points, sub(centroid(points), mid), colour));

  face(front.slice());
  face(back.slice());
  for (let i = 0; i < front.length; i++) {
    const j = (i + 1) % front.length;
    face([front[i], front[j], back[j], back[i]]);
  }
  return faces;
}

const makeBox = (cx, cy, cz, hx, hy, hz, colour) => makeSolid(
  [
    vec(cx - hx, cy + hy, cz - hz),
    vec(cx + hx, cy + hy, cz - hz),
    vec(cx + hx, cy + hy, cz + hz),
    vec(cx - hx, cy + hy, cz + hz),
  ],
  [
    vec(cx - hx, cy - hy, cz - hz),
    vec(cx + hx, cy - hy, cz - hz),
    vec(cx + hx, cy - hy, cz + hz),
    vec(cx - hx, cy - hy, cz + hz),
  ],
  colour,
);

/** A wheel: a short octagonal cylinder lying along the body x axis. */
function makeWheel(cx, cy, cz, radius, halfWidth, colour) {
  const ring = [];
  for (let i = 0; i < 8; i++) {
    const a = (i / 8) * Math.PI * 2 + Math.PI / 8;
    ring.push(vec(Math.cos(a) * radius, Math.sin(a) * radius, 0));
  }
  return makeSolid(
    ring.map((p) => vec(cx - halfWidth, cy + p.x, cz + p.y)),
    ring.map((p) => vec(cx + halfWidth, cy + p.x, cz + p.y)),
    colour,
  );
}

/* --------------------------------------------------------- the airframe */

/** A fuselage ring: eight points around the tube, flat panels top and sides. */
function ring(z, radius, centreY) {
  const points = [];
  for (let i = 0; i < 8; i++) {
    const a = (i / 8) * Math.PI * 2 + Math.PI / 8;
    points.push(vec(Math.cos(a) * radius, centreY + Math.sin(a) * radius, z));
  }
  return { z, points };
}

const FUSELAGE = [
  ring(-6.30, 0.18,  0.00),   // nose
  ring(-5.80, 0.46, -0.02),
  ring(-4.90, 0.76, -0.03),
  ring(-3.60, 0.93, -0.01),
  ring(-1.40, 0.98,  0.00),
  ring( 1.60, 0.92,  0.02),
  ring( 3.50, 0.66,  0.06),
  ring( 4.75, 0.26,  0.10),   // tail cone
];

/** Panels are numbered from the upper right, going anticlockwise. */
function fuselageColour(panel) {
  if (panel === 3 || panel === 7) return ACCENT;   // the flank stripe
  if (panel === 1) return UPPER;                   // the roof
  if (panel >= 4 && panel <= 6) return BELLY;      // the underside
  return SIDE;
}

const BODY = [];

/** Loft the fuselage through its rings. */
for (let i = 0; i < FUSELAGE.length - 1; i++) {
  const a = FUSELAGE[i].points;
  const b = FUSELAGE[i + 1].points;
  const mid = centroid(a.concat(b));

  for (let k = 0; k < 8; k++) {
    const k2 = (k + 1) % 8;
    const quad = [a[k], a[k2], b[k2], b[k]];
    BODY.push(oriented(quad, sub(centroid(quad), mid), fuselageColour(k)));
  }
}
BODY.push(oriented(FUSELAGE[0].points.slice(), vec(0, -0.2, -1), UPPER));
BODY.push(oriented(FUSELAGE[FUSELAGE.length - 1].points.slice(), vec(0, 0.2, 1), SIDE));

/* ---- wings: tapered and with a little dihedral ---- */
function wing(sign) {
  const top = [
    vec(sign * 0.60, 0.14, -1.85),
    vec(sign * 0.60, 0.14,  1.20),
    vec(sign * 5.60, 0.64,  0.45),
    vec(sign * 5.60, 0.64, -1.05),
  ];
  return makeSolid(top, top.map((p) => vec(p.x, p.y - 0.18, p.z)), WING);
}
BODY.push(...wing(1), ...wing(-1));

/* ---- tailplane ---- */
function tailplane(sign) {
  const top = [
    vec(sign * 0.25, 0.30,  3.85),
    vec(sign * 0.25, 0.30,  5.05),
    vec(sign * 2.35, 0.48,  4.95),
    vec(sign * 2.35, 0.48,  4.20),
  ];
  return makeSolid(top, top.map((p) => vec(p.x, p.y - 0.12, p.z)), WING);
}
BODY.push(...tailplane(1), ...tailplane(-1));

/* ---- fin ---- */
const fin = [
  vec(0, 0.35, 4.10),
  vec(0, 0.35, 5.20),
  vec(0, 2.30, 5.05),
  vec(0, 2.30, 4.70),
];
BODY.push(...makeSolid(fin, fin.map((p) => vec(p.x + 0.14, p.y, p.z)), ACCENT));

/* ---- canopy: sits over the cockpit, and the eye is placed inside it ---- */
const canopyBase = [
  vec(-0.62, 0.72, -4.40),
  vec( 0.62, 0.72, -4.40),
  vec( 0.66, 0.72, -2.25),
  vec(-0.66, 0.72, -2.25),
];
const canopyTop = [
  vec(-0.40, 1.42, -3.95),
  vec( 0.40, 1.42, -3.95),
  vec( 0.48, 1.42, -2.45),
  vec(-0.48, 1.42, -2.45),
];
BODY.push(...makeSolid(canopyBase, canopyTop, GLASS));

/* ------------------------------------------------------------ the gear */

const LEGS = [
  { x:  0.00, z: -3.55, radius: 0.20, halfWidth: 0.09, inward: 0.00 },
  { x: -1.45, z:  0.55, radius: 0.26, halfWidth: 0.11, inward: 0.70 },
  { x:  1.45, z:  0.55, radius: 0.26, halfWidth: 0.11, inward: 0.70 },
];

const WHEEL_BOTTOM = -1.6;   // matches GEAR_HEIGHT

/**
 * Gear faces for a given extension, 0 = retracted, 1 = locked down. The legs
 * swing inwards and up into the fuselage as they retract, so they disappear
 * inside the bodywork rather than hanging in mid-air.
 */
function buildGear(extension) {
  if (extension <= 0.01) return EMPTY;

  const faces = [];
  const bottom = WHEEL_BOTTOM * extension;

  for (const leg of LEGS) {
    const x = leg.x + Math.sign(leg.x) * leg.inward * (1 - extension);
    const wheelCentre = bottom + leg.radius;
    const wheelTop = bottom + leg.radius * 2;

    faces.push(...makeWheel(x, wheelCentre, leg.z, leg.radius, leg.halfWidth, TYRE));

    if (wheelTop < 0.05) {
      const top = 0.05;
      faces.push(...makeBox(
        x, (top + wheelTop) * 0.5, leg.z,
        0.07, (top - wheelTop) * 0.5, 0.07,
        METAL,
      ));
    }
  }
  return faces;
}

/* -------------------------------------------------------------- drawing */

const clampByte = (v) => (v < 0 ? 0 : v > 255 ? 255 : v | 0);

/**
 * Paint the aircraft into the current camera.
 *
 * @param {CanvasRenderingContext2D} ctx
 * @param {import('./camera.js').Camera} camera
 * @param {import('./aircraft.js').Aircraft} aircraft
 * @param {number} [minDepth] faces closer than this are dropped, which is how
 *   the cockpit view hides the canopy it is sitting inside.
 */
export function drawAircraft(ctx, camera, aircraft, minDepth = 0) {
  const p = aircraft.position;
  const R = aircraft.right;
  const U = aircraft.up;
  const F = aircraft.forward;
  const eye = camera.position;

  const toWorld = (q) => ({
    x: p.x + R.x * q.x + U.x * q.y - F.x * q.z,
    y: p.y + R.y * q.x + U.y * q.y - F.y * q.z,
    z: p.z + R.z * q.x + U.z * q.y - F.z * q.z,
  });

  const near = Math.max(minDepth, 0.6);
  const batch = [];

  const consider = (points, colour) => {
    const world = points.map(toWorld);
    const centre = centroid(world);
    const depth = camera.toView(centre).z;
    if (depth < near) return;

    // Back faces are hidden by the hull in front of them, so the painter's
    // sort only has to get the visible half right.
    const n = faceNormal(world);
    const toEye = sub(eye, centre);
    if (n.x * toEye.x + n.y * toEye.y + n.z * toEye.z <= 0) return;

    batch.push({ world, centre, depth, colour, n });
  };

  for (const face of BODY) consider(face.points, face.colour);
  for (const face of buildGear(aircraft.gearPosition)) consider(face.points, face.colour);

  batch.sort((a, b) => b.depth - a.depth);

  for (const face of batch) {
    const poly = camera.projectPolygon(face.world);
    if (!poly) continue;

    const lambert = Math.max(0, dot(face.n, SUN_DIR));
    const light = 0.64 + 0.46 * lambert;
    const fog = fogFactor(face.depth, camera);
    const [cr, cg, cb] = face.colour;

    const r = (cr * light) * (1 - fog) + HAZE[0] * fog;
    const g = (cg * light) * (1 - fog) + HAZE[1] * fog;
    const b = (cb * light) * (1 - fog) + HAZE[2] * fog;

    ctx.fillStyle = `rgb(${clampByte(r)},${clampByte(g)},${clampByte(b)})`;
    ctx.beginPath();
    ctx.moveTo(poly[0].x, poly[0].y);
    for (let i = 1; i < poly.length; i++) ctx.lineTo(poly[i].x, poly[i].y);
    ctx.closePath();
    ctx.fill();
  }
}
