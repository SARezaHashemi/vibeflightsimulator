/**
 * A small point-mass physics engine.
 *
 * A `Body` owns a mass, a position and a velocity. During a frame, force
 * generators push and pull on it; `step()` turns the accumulated force into an
 * acceleration and integrates with velocity Verlet, which conserves the
 * mechanical energy of a constant-force (gravity) step exactly. Angular state
 * lives on the aircraft itself, so this module only advances translation.
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
   * Advance one frame with velocity Verlet: move to the half-step using the
   * current velocity plus half the acceleration, then finish the velocity.
   *
   * For a constant force this integrates the trajectory and the mechanical
   * energy exactly — gravity neither gains nor loses energy from the scheme.
   * The old semi-implicit update lost ½·g²·dt² of energy every step under
   * gravity. Drag, thrust and the other real forces still do their physical
   * work on top, so total energy only changes through those forces.
   */
  step(dt) {
    const invMass = 1 / this.mass;

    this.acceleration.x = this.force.x * invMass;
    this.acceleration.y = this.force.y * invMass;
    this.acceleration.z = this.force.z * invMass;

    this.position.x += this.velocity.x * dt + 0.5 * this.acceleration.x * dt * dt;
    this.position.y += this.velocity.y * dt + 0.5 * this.acceleration.y * dt * dt;
    this.position.z += this.velocity.z * dt + 0.5 * this.acceleration.z * dt * dt;

    this.velocity.x += this.acceleration.x * dt;
    this.velocity.y += this.acceleration.y * dt;
    this.velocity.z += this.acceleration.z * dt;

    this.clearForces();
  }
}

/** Translational mechanical energy: kinetic + gravitational potential. */
export function mechanicalEnergy(body, gravity = 9.81) {
  const v = body.velocity;
  return 0.5 * body.mass * (v.x * v.x + v.y * v.y + v.z * v.z)
    + body.mass * gravity * body.position.y;
}

/**
 * Rotational kinetic energy, ½·L·ω. Both vectors must be expressed in the
 * same frame; the dot product is not invariant under a rotation of only one.
 */
export function rotationalEnergy(angularMomentum, angularVelocity) {
  return 0.5 * (
    angularMomentum.x * angularVelocity.x +
    angularMomentum.y * angularVelocity.y +
    angularMomentum.z * angularVelocity.z
  );
}

/** Uniform gravitational field. */
export function applyGravity(body, g = 9.81) {
  body.applyForce({ x: 0, y: -g * body.mass, z: 0 });
}
