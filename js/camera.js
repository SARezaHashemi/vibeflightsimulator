/**
 * Camera: basis vectors, focal length, and projection to screen space.
 * Everything renderer-side projects through this object.
 *
 * Two views are supported. `chase` sits behind and above the aircraft and
 * looks at it; `cockpit` puts the eye inside the canopy and looks down the
 * nose. Both produce the same basis + focal length, so the renderers and the
 * HUD do not need to know which one is active.
 */

import {
  FOV_DEG, EYE_HEIGHT, EYE_FORWARD,
  CHASE_BACK, CHASE_LIFT, CHASE_AIM,
} from './config.js';
import { cross, normalise } from './math.js';
import { terrainHeight } from './world.js';

const NEAR = 0.5;
const WORLD_UP = Object.freeze({ x: 0, y: 1, z: 0 });
const GROUND_CLEARANCE = 2.5;   // keep the chase eye out of the hillside

export class Camera {
  constructor() {
    this.position = { x: 0, y: 0, z: 0 };
    this.forward = { x: 0, y: 0, z: -1 };
    this.up = { x: 0, y: 1, z: 0 };
    this.right = { x: 1, y: 0, z: 0 };
    this.width = 1;
    this.height = 1;
    this.focal = 1;
  }

  /**
   * Point the camera at `aircraft` and fit the lens to the viewport.
   *
   * @param {import('./aircraft.js').Aircraft} aircraft
   * @param {number} width
   * @param {number} height
   * @param {'chase'|'cockpit'} [view]
   */
  update(aircraft, width, height, view = 'chase') {
    this.width = width;
    this.height = height;
    this.focal = (height * 0.5) / Math.tan((FOV_DEG * Math.PI / 180) * 0.5);

    if (view === 'cockpit') this.#cockpit(aircraft);
    else this.#chase(aircraft);
  }

  /** Eye inside the canopy, looking straight down the nose. */
  #cockpit(aircraft) {
    const { position: p, up, forward } = aircraft;

    this.forward = forward;
    this.up = up;
    this.right = aircraft.right;
    this.position = {
      x: p.x + up.x * EYE_HEIGHT + forward.x * EYE_FORWARD,
      y: p.y + up.y * EYE_HEIGHT + forward.y * EYE_FORWARD,
      z: p.z + up.z * EYE_HEIGHT + forward.z * EYE_FORWARD,
    };
  }

  /**
   * Behind and above the aircraft. The reference "up" is mostly the aircraft's
   * own roof with a dash of world up, so the view banks with the aircraft but
   * the horizon does not spin wildly in a steep turn.
   */
  #chase(aircraft) {
    const { position: p, forward: f, up: aircraftUp } = aircraft;

    const upRef = normalise({
      x: aircraftUp.x * 0.65,
      y: aircraftUp.y * 0.65 + 0.35,
      z: aircraftUp.z * 0.65,
    });

    const eye = {
      x: p.x - f.x * CHASE_BACK + upRef.x * CHASE_LIFT,
      y: p.y - f.y * CHASE_BACK + upRef.y * CHASE_LIFT,
      z: p.z - f.z * CHASE_BACK + upRef.z * CHASE_LIFT,
    };

    const floor = terrainHeight(eye.x, eye.z) + GROUND_CLEARANCE;
    if (eye.y < floor) eye.y = floor;

    const aim = {
      x: p.x + f.x * CHASE_AIM,
      y: p.y + f.y * CHASE_AIM,
      z: p.z + f.z * CHASE_AIM,
    };

    this.position = eye;
    this.forward = normalise({
      x: aim.x - eye.x,
      y: aim.y - eye.y,
      z: aim.z - eye.z,
    });

    // A basis built from the view direction keeps the horizon level; it falls
    // back to world up when the nose points straight up or down.
    let right = cross(this.forward, upRef);
    if (Math.hypot(right.x, right.y, right.z) < 0.2) {
      right = cross(this.forward, WORLD_UP);
    }
    this.right = normalise(right);
    this.up = cross(this.right, this.forward);
  }

  /** World point → camera space (x right, y up, z forward/depth). */
  toView(point) {
    const dx = point.x - this.position.x;
    const dy = point.y - this.position.y;
    const dz = point.z - this.position.z;
    return {
      x: dx * this.right.x + dy * this.right.y + dz * this.right.z,
      y: dx * this.up.x + dy * this.up.y + dz * this.up.z,
      z: dx * this.forward.x + dy * this.forward.y + dz * this.forward.z,
    };
  }

  /** Camera space → pixels. Only valid for z > 0. */
  projectView(v) {
    return {
      x: this.width * 0.5 + this.focal * v.x / v.z,
      y: this.height * 0.5 - this.focal * v.y / v.z,
    };
  }

  /** Screen position of a world point, or null when it is behind the eye. */
  project(point) {
    const v = this.toView(point);
    if (v.z <= 0.01) return null;
    return { ...this.projectView(v), depth: v.z };
  }

  /** Screen position of a direction at infinity (no translation applied). */
  projectDirection(dir) {
    const depth = dir.x * this.forward.x + dir.y * this.forward.y + dir.z * this.forward.z;
    if (depth <= 0.06) return null;

    const cx = dir.x * this.right.x + dir.y * this.right.y + dir.z * this.right.z;
    const cy = dir.x * this.up.x + dir.y * this.up.y + dir.z * this.up.z;

    return {
      x: this.width * 0.5 + this.focal * cx / depth,
      y: this.height * 0.5 - this.focal * cy / depth,
    };
  }

  /**
   * Project a convex world-space polygon, clipping it against the near plane
   * so shapes that straddle the camera (a runway under your wheels) still
   * draw correctly. Returns screen points, or null if nothing survives.
   */
  projectPolygon(points) {
    const view = points.map((p) => this.toView(p));
    const clipped = [];

    for (let i = 0; i < view.length; i++) {
      const a = view[i];
      const b = view[(i + 1) % view.length];
      const aIn = a.z >= NEAR;
      const bIn = b.z >= NEAR;

      if (aIn) clipped.push(a);
      if (aIn !== bIn) {
        const t = (NEAR - a.z) / (b.z - a.z);
        clipped.push({
          x: a.x + (b.x - a.x) * t,
          y: a.y + (b.y - a.y) * t,
          z: NEAR,
        });
      }
    }

    if (clipped.length < 3) return null;
    return clipped.map((v) => this.projectView(v));
  }
}
