/**
 * Tiny vector / quaternion helpers.
 * A quaternion rotates a vector from body space into world space.
 */

export const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);
export const lerp = (a, b, t) => a + (b - a) * t;

/** Hamilton product: the result applies `b` first, then `a`. */
export function qmul(a, b) {
  return {
    x: a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
    y: a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
    z: a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
    w: a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z,
  };
}

/** Rotation of `angle` radians about the (normalised) axis x,y,z. */
export function qaxis(x, y, z, angle) {
  const s = Math.sin(angle * 0.5);
  return { x: x * s, y: y * s, z: z * s, w: Math.cos(angle * 0.5) };
}

export function qnorm(q) {
  const len = Math.hypot(q.x, q.y, q.z, q.w) || 1;
  return { x: q.x / len, y: q.y / len, z: q.z / len, w: q.w / len };
}

/** Conjugate; the inverse rotation for a unit quaternion. */
export const qconj = (q) => ({ x: -q.x, y: -q.y, z: -q.z, w: q.w });

/**
 * Advance an orientation by a body-frame angular velocity over `dt`.
 *
 * Treating the angular velocity as constant across the step and rotating
 * about its axis is exact, unlike composing three small Euler rotations, and
 * keeps the quaternion normalised without leaking rotational energy.
 */
export function qspin(q, w, dt) {
  const speed = Math.hypot(w.x, w.y, w.z);
  const angle = speed * dt;
  if (angle < 1e-9) return q;

  const s = Math.sin(angle * 0.5) / speed;
  return qnorm(qmul(q, {
    x: w.x * s,
    y: w.y * s,
    z: w.z * s,
    w: Math.cos(angle * 0.5),
  }));
}

/** Rotate vector `v` by quaternion `q`. */
export function qrot(q, v) {
  const tx = 2 * (q.y * v.z - q.z * v.y);
  const ty = 2 * (q.z * v.x - q.x * v.z);
  const tz = 2 * (q.x * v.y - q.y * v.x);
  return {
    x: v.x + q.w * tx + (q.y * tz - q.z * ty),
    y: v.y + q.w * ty + (q.z * tx - q.x * tz),
    z: v.z + q.w * tz + (q.x * ty - q.y * tx),
  };
}

export const dot = (a, b) => a.x * b.x + a.y * b.y + a.z * b.z;
export const length = (v) => Math.hypot(v.x, v.y, v.z);
export const sub = (a, b) => ({ x: a.x - b.x, y: a.y - b.y, z: a.z - b.z });
export const scale = (v, s) => ({ x: v.x * s, y: v.y * s, z: v.z * s });

export function cross(a, b) {
  return {
    x: a.y * b.z - a.z * b.y,
    y: a.z * b.x - a.x * b.z,
    z: a.x * b.y - a.y * b.x,
  };
}

export function normalise(v) {
  const len = Math.hypot(v.x, v.y, v.z) || 1;
  return { x: v.x / len, y: v.y / len, z: v.z / len };
}
