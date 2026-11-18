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

/**
 * Orientation from an orthonormal aircraft basis.
 *
 * Body +x is right, +y is up and +z points backwards, so the nose (-z) maps to
 * `forward`. The columns of the rotation matrix are therefore
 * [right, up, -forward].
 */
export function qfromBasis(right, up, forward) {
  const m00 = right.x, m01 = up.x, m02 = -forward.x;
  const m10 = right.y, m11 = up.y, m12 = -forward.y;
  const m20 = right.z, m21 = up.z, m22 = -forward.z;
  const trace = m00 + m11 + m22;

  let x, y, z, w;
  if (trace > 0) {
    const s = Math.sqrt(trace + 1) * 2;
    w = 0.25 * s;
    x = (m21 - m12) / s;
    y = (m02 - m20) / s;
    z = (m10 - m01) / s;
  } else if (m00 > m11 && m00 > m22) {
    const s = Math.sqrt(1 + m00 - m11 - m22) * 2;
    w = (m21 - m12) / s;
    x = 0.25 * s;
    y = (m01 + m10) / s;
    z = (m02 + m20) / s;
  } else if (m11 > m22) {
    const s = Math.sqrt(1 + m11 - m00 - m22) * 2;
    w = (m02 - m20) / s;
    x = (m01 + m10) / s;
    y = 0.25 * s;
    z = (m12 + m21) / s;
  } else {
    const s = Math.sqrt(1 + m22 - m00 - m11) * 2;
    w = (m10 - m01) / s;
    x = (m02 + m20) / s;
    y = (m12 + m21) / s;
    z = 0.25 * s;
  }

  return qnorm({ x, y, z, w });
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
