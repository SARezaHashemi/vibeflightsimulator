using Godot;

/**
 * A small point-mass physics engine. Port of js/physics.js.
 *
 * A `Body` owns a mass, a position and a velocity. During a frame, force
 * generators push and pull on it; `Step()` turns the accumulated force into
 * an acceleration and integrates with velocity Verlet, which conserves the
 * mechanical energy of a constant-force (gravity) step exactly.
 */
public class Body
{
    public float Mass;
    public Vector3 Position;
    public Vector3 Velocity;
    public Vector3 Force;
    public Vector3 Acceleration;

    public Body(float mass, Vector3 position, Vector3 velocity)
    {
        Mass = mass;
        Position = position;
        Velocity = velocity;
        Force = Vector3.Zero;
        Acceleration = Vector3.Zero;
    }

    /** Magnitude of the velocity vector, m/s. */
    public float Speed => Velocity.Length();

    public void SetVelocity(Vector3 v) => Velocity = v;

    public void ApplyForce(Vector3 force) => Force += force;

    /** Convenience for generators that are written as accelerations. */
    public void ApplyAcceleration(Vector3 acceleration) => Force += acceleration * Mass;

    public void ClearForces() => Force = Vector3.Zero;

    /**
     * Advance one frame with velocity Verlet: move to the half-step using the
     * current velocity plus half the acceleration, then finish the velocity.
     */
    public void Step(float dt)
    {
        float invMass = 1 / Mass;

        Acceleration = Force * invMass;

        Position += Velocity * dt + Acceleration * (0.5f * dt * dt);
        Velocity += Acceleration * dt;

        ClearForces();
    }
}
