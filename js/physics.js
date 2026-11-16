/**
 * A very small point-mass physics engine.
 *
 * A `Body` owns a mass, a position and a velocity. During a frame, force
 * generators push and pull on it; `step()` turns the accumulated force into an
 * acceleration and integrates with semi-implicit Euler. Rotational dynamics
 * are deliberately out of scope for now — the aircraft's attitude is still
 * driven straight from the controls.
 *
 * Units are SI: metres, seconds, kilograms, newtons.
 */

export class Body {
  constructor({
    mass = 1,
    position = { x: 0, y: 0, z: 0 },
    velocity = { x: 0, y: 0, z: 0 },
  } = {}) {
    this.mass = mass;
    this.position = { ...position };
    this.velocity = { ...velocity };
    this.force = { x: 0, y: 0, z: 0 };
    this.acceleration = { x: 0, y: 0, z: 0 };
  }

  /** Magnitude of the velocity vector, m/s. */
  get speed() {
    return Math.hypot(this.velocity.x, this.velocity.y, this.velocity.z);
  }

  setVelocity(x, y, z) {
    this.velocity.x = x;
    this.velocity.y = y;
    this.velocity.z = z;
  }

  applyForce(force) {
    this.force.x += force.x;
    this.force.y += force.y;
    this.force.z += force.z;
  }

  /** Convenience for generators that are written as accelerations. */
  applyAcceleration(acceleration) {
    this.applyForce({
      x: acceleration.x * this.mass,
      y: acceleration.y * this.mass,
      z: acceleration.z * this.mass,
    });
  }

  clearForces() {
    this.force.x = 0;
    this.force.y = 0;
    this.force.z = 0;
  }

  /**
   * Advance one frame. Updating velocity before position (semi-implicit Euler)
   * keeps the integration stable at the frame rates we see in the browser.
   */
  step(dt) {
    const invMass = 1 / this.mass;

    this.acceleration.x = this.force.x * invMass;
    this.acceleration.y = this.force.y * invMass;
    this.acceleration.z = this.force.z * invMass;

    this.velocity.x += this.acceleration.x * dt;
    this.velocity.y += this.acceleration.y * dt;
    this.velocity.z += this.acceleration.z * dt;

    this.position.x += this.velocity.x * dt;
    this.position.y += this.velocity.y * dt;
    this.position.z += this.velocity.z * dt;

    this.clearForces();
  }
}

/** Uniform gravitational field. */
export function applyGravity(body, g = 9.81) {
  body.applyForce({ x: 0, y: -g * body.mass, z: 0 });
}

/**
 * Quadratic drag, opposing the direction of travel.
 * `coefficient` is the same value the old arcade model used for its
 * acceleration term, so force = mass * coefficient * speed².
 */
export function applyDrag(body, coefficient) {
  const speed = body.speed;
  if (speed < 1e-6) return;

  const deceleration = coefficient * speed * speed;
  const scale = (deceleration * body.mass) / speed;

  body.applyForce({
    x: -body.velocity.x * scale,
    y: -body.velocity.y * scale,
    z: -body.velocity.z * scale,
  });
}
