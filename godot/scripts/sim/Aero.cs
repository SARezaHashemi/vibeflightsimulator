using Godot;

/**
 * Aerodynamics: air density, the wing's lift curve, the drag polar and the
 * force generator that turns them into forces on a `Body`.
 * Port of js/aero.js.
 */
public static class Aero
{
    private const float ASPECT_RATIO = (Config.WING_SPAN * Config.WING_SPAN) / Config.WING_AREA;
    private const float INDUCED_FACTOR = 1f / (Mathf.Pi * ASPECT_RATIO * Config.OSWALD);

    private const float HALF_PI = Mathf.Pi / 2f;
    private const float ALPHA_LIMIT = Mathf.Pi / 2f - 1e-3f;

    /**
     * Angle of attack where the linear lift slope reaches CL_MAX. ALPHA_STALL
     * is the aerodynamic stall angle; this is the earlier of the two, so lift
     * is capped without a discontinuity.
     */
    public static readonly float STALL_ALPHA = Mathf.Min(Config.ALPHA_STALL, Config.CL_MAX / Config.CL_ALPHA);

    /** Exponential atmosphere: density at an altitude above sea level, kg/m³. */
    public static float AirDensity(float altitude)
    {
        return Config.RHO_SEA * Mathf.Exp(-Mathf.Max(0f, altitude) / Config.SCALE_HEIGHT);
    }

    /**
     * Lift coefficient as a function of angle of attack.
     * Linear through STALL_ALPHA, then the flow separates and lift falls off
     * smoothly to roughly a quarter of its peak at 90°.
     */
    public static float LiftCoefficient(float alpha)
    {
        float a = Mathf.Abs(alpha);
        if (a <= STALL_ALPHA) return Config.CL_ALPHA * alpha;

        float peak = Config.CL_ALPHA * STALL_ALPHA;
        float t = QMath.Clamp((a - STALL_ALPHA) / (HALF_PI - STALL_ALPHA), 0f, 1f);
        return Mathf.Sign(alpha) * peak * (1f - 0.72f * t * t);
    }

    /**
     * Drag coefficient from the polar: parasitic + induced + separated flow.
     * Induced drag grows with the square of lift, so pulling g costs speed.
     */
    public static float DragCoefficient(float cl, float alpha)
    {
        float induced = INDUCED_FACTOR * cl * cl;

        float a = Mathf.Abs(alpha);
        float separated = 0;
        if (a > STALL_ALPHA)
        {
            float t = QMath.Clamp((a - STALL_ALPHA) / (HALF_PI - STALL_ALPHA), 0f, 1f);
            separated = Config.CD_STALL * Mathf.Sin(t * HALF_PI) * Mathf.Sin(t * HALF_PI);
        }

        return Config.CD0 + induced + separated;
    }

    public struct Flow
    {
        public float Alpha, Beta, Speed, Qbar;
    }

    /**
     * Resolve the body's velocity into the air frame.
     * `alpha` is positive when the relative wind comes from below (nose above
     * the flight path); `beta` is positive when it comes from the right.
     */
    public static Flow FlowAngles(Body body, Vector3 forward, Vector3 up, Vector3 right, float altitude)
    {
        Vector3 v = body.Velocity;
        float speed = v.Length();
        if (speed < 1e-4f) return new Flow { Alpha = 0, Beta = 0, Speed = speed, Qbar = 0 };

        float u = v.Dot(forward);
        float w = v.Dot(up);
        float side = v.Dot(right);

        return new Flow
        {
            Alpha = QMath.Clamp(Mathf.Atan2(-w, u), -ALPHA_LIMIT, ALPHA_LIMIT),
            Beta = QMath.Clamp(Mathf.Atan2(side, Mathf.Abs(u)), -HALF_PI, HALF_PI),
            Speed = speed,
            Qbar = 0.5f * AirDensity(altitude) * speed * speed,
        };
    }

    public struct AeroData
    {
        public float Alpha, Beta, Speed, Qbar;
        public float Cl, Cd, Lift, Drag, GLoad;
        public bool Stalled;

        public static readonly AeroData Idle = new()
        {
            Alpha = 0, Beta = 0, Speed = 0, Qbar = 0,
            Cl = 0, Cd = 0, Lift = 0, Drag = 0, GLoad = 1, Stalled = false,
        };
    }

    /**
     * Turn lift, drag and side force into forces on `body`.
     * Lift is perpendicular to the relative wind and lies in the aircraft's
     * plane of symmetry, so it banks with the wings and vanishes in a vertical
     * climb. Drag opposes the motion.
     */
    public static AeroData ApplyAerodynamics(Body body, Vector3 forward, Vector3 up, Vector3 right, float altitude, float gear)
    {
        Flow flow = FlowAngles(body, forward, up, right, altitude);
        float speed = flow.Speed, qbar = flow.Qbar, alpha = flow.Alpha;

        float cl = LiftCoefficient(alpha);
        float cd = DragCoefficient(cl, alpha) + Config.GEAR_DRAG * gear;
        float qS = qbar * Config.WING_AREA;

        float liftForce = 0;

        if (speed > 1e-4f && qS > 0)
        {
            Vector3 vhat = body.Velocity / speed;

            // Remove the component of "up" along the flight path: what is left
            // is the lift direction. It is zero in a true vertical dive or
            // climb, where a wing makes no lift.
            Vector3 dir = up - vhat * up.Dot(vhat);
            if (dir.Length() > 1e-4f)
            {
                liftForce = qS * cl;
                body.ApplyForce(dir.Normalized() * liftForce);
            }

            // Parasitic + induced + separated drag, against the relative wind.
            body.ApplyForce(vhat * (-qS * cd));

            // Fuselage and fin side force: pushes the velocity back towards
            // the nose.
            body.ApplyForce(right * (-qS * Config.CY_BETA * flow.Beta));
        }

        return new AeroData
        {
            Alpha = alpha, Beta = flow.Beta, Speed = speed, Qbar = qbar,
            Cl = cl, Cd = cd,
            Lift = Mathf.Abs(liftForce),
            Drag = qS * cd,
            GLoad = liftForce / (body.Mass * Config.GRAVITY),
            Stalled = Mathf.Abs(alpha) > STALL_ALPHA,
        };
    }

    /**
     * Rolling drag only. On the ground the wheels carry the weight, so the
     * wing's lift does not matter; parasite and gear drag still slow the
     * aircraft down.
     */
    public static float ApplyGroundDrag(Body body, float altitude, float gear)
    {
        float speed = body.Speed;
        if (speed < 1e-4f) return 0;

        float q = 0.5f * AirDensity(altitude) * speed * speed;
        float force = q * Config.WING_AREA * (Config.CD0 + Config.GEAR_DRAG * gear);
        body.ApplyForce(body.Velocity * (-force / speed));
        return force;
    }
}
