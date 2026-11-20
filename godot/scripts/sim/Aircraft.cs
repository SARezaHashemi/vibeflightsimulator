using Godot;

/**
 * Flight model. Port of js/aircraft.js.
 *
 * Translation is force-driven: thrust along the nose, gravity, and the
 * aerodynamic lift, drag and side force from Aero.cs. Rotation is a proper
 * rigid body: control, damping and stability moments change an angular
 * momentum vector about the body axes, and the attitude follows the resulting
 * angular velocity.
 *
 * The aircraft is a small state machine: AIR → GROUND on a good touchdown,
 * AIR → CRASHED on a bad one, GROUND → AIR when you rotate again.
 */
public class Aircraft
{
    public enum State { Air, Ground, Crashed }

    public class LandingRecord
    {
        public WorldSim.SurfaceKind Surface;
        public float Sink;
        public float Speed;
        public bool Firm;
    }

    public class ImpactRecord
    {
        public float Speed, Sink;
        public WorldSim.SurfaceKind Surface;
    }

    public Body Body;
    public Vector3 Position;

    public Quaternion Orientation;
    public float Throttle;     // lever position, 0..1
    public float Engine;       // spooled power, 0..1

    // Angular momentum is kept in world axes so it is conserved exactly when
    // no moment acts. Angular velocity is the body-frame view of it.
    public Vector3 AngularMomentum;
    public Vector3 AngularVelocity;

    public Vector3 Surfaces;   // smoothed control deflections (pitch, roll, yaw)

    public float Elapsed;
    public State state = State.Air;
    public string CrashReason = "";
    public bool GearDown = true;
    public float GearPosition = 1;
    public float GroundHeading;
    public float SteerAngle;
    public float GroundIgnoreUntil;
    public float LandedAt = -1;

    public WorldSim.SurfaceInfo Surface;
    public LandingRecord LastLanding;
    public ImpactRecord CrashImpact;

    public Aero.AeroData AeroData = Aero.AeroData.Idle;

    // Body axes: x pitches, y yaws, z rolls.
    private static readonly Vector3 INERTIA_BODY = new(
        Config.INERTIA.pitch, Config.INERTIA.yaw, Config.INERTIA.roll);

    // Orthonormal aircraft basis, refreshed from the orientation.
    public Vector3 Forward, Up, Right;

    public Aircraft()
    {
        Reset();
    }

    public void Reset()
    {
        Body = new Body(Config.MASS, Config.START, new Vector3(0, 0, -Config.START_SPEED));
        Position = Body.Position;

        Orientation = QMath.QAxis(1, 0, 0, Config.TRIM_ALPHA);
        Throttle = Config.START_THROTTLE;
        Engine = Config.START_THROTTLE;

        AngularMomentum = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        Surfaces = Vector3.Zero;
        Elapsed = 0;

        state = State.Air;
        CrashReason = "";
        GearDown = true;
        GearPosition = 1;
        GroundHeading = 0;
        SteerAngle = 0;
        GroundIgnoreUntil = 0;
        LandedAt = -1;

        Surface = WorldSim.SurfaceAt(Config.START.X, Config.START.Z);
        LastLanding = null;
        CrashImpact = null;

        AeroData = Aero.AeroData.Idle;
        RefreshBasis();
    }

    // ------------------------------------------------------------ readings

    public bool IsAirborne => state == State.Air;
    public bool IsGrounded => state == State.Ground;
    public bool IsCrashed => state == State.Crashed;

    /** Airspeed, from the integrated velocity vector. */
    public float Speed => Body.Speed;

    /** Positive when climbing, m/s. */
    public float VerticalSpeed => state == State.Air ? Body.Velocity.Y : 0;

    /** Angle of attack in radians; positive means nose above the flight path. */
    public float AngleOfAttack => AeroData.Alpha;

    /** Wing lift in g. One g means the wing carries the aircraft's weight. */
    public float GLoad => AeroData.GLoad;

    /** Acceleration along the nose in m/s², from the last integrated step. */
    public float LongitudinalAcceleration =>
        Body.Acceleration.X * Forward.X + Body.Acceleration.Y * Forward.Y + Body.Acceleration.Z * Forward.Z;

    public bool IsStalled => state == State.Air && AeroData.Stalled;

    public float AltitudeAgl
    {
        get
        {
            // The origin is the centre of gravity; the wheels are GEAR_HEIGHT
            // below it, so this reads zero when sitting on the ground.
            return Position.Y - Config.GEAR_HEIGHT - WorldSim.SurfaceHeight(Position.X, Position.Z);
        }
    }

    public float HeadingRad => Mathf.Atan2(Forward.X, -Forward.Z);

    /** Compass heading in degrees, 0 = north (-Z). */
    public float Heading => (HeadingRad * 180 / Mathf.Pi + 360) % 360;

    /** Nose up is positive, in radians. */
    public float PitchAngle => Mathf.Asin(QMath.Clamp(Forward.Y, -1, 1));

    /** Wings level is 0; rolling right is negative, in radians. */
    public float BankAngle => Mathf.Atan2(Right.Y, Up.Y);

    /** How far off the nearest runway centreline heading we are, in radians. */
    public float RunwayAlignment
    {
        get
        {
            float heading = HeadingRad;
            WorldSim.Airport airport = WorldSim.NearestAirport(Position.X, Position.Z);
            return Mathf.Min(
                Mathf.Abs(QMath.AngleDelta(heading, airport.Heading)),
                Mathf.Abs(QMath.AngleDelta(heading, airport.Heading + Mathf.Pi)));
        }
    }

    /** Angle between where the nose points and where the aircraft is going. */
    public float DriftAngle
    {
        get
        {
            Vector3 v = Body.Velocity;
            if (Mathf.Sqrt(v.X * v.X + v.Z * v.Z) < 1) return 0;
            float track = Mathf.Atan2(v.X, -v.Z);
            return Mathf.Abs(QMath.AngleDelta(track, HeadingRad));
        }
    }

    // -------------------------------------------------------------- actions

    public void ToggleGear()
    {
        if (state == State.Air) GearDown = !GearDown;
    }

    // --------------------------------------------------------------- update

    public struct Axes
    {
        public float Pitch, Roll, Yaw, Throttle;
    }

    public void Update(float dt, Axes axes)
    {
        if (state == State.Crashed) return;
        Elapsed += dt;

        // Gear travel, whether we are flying or rolling.
        float target = GearDown ? 1 : 0;
        float step = Config.GEAR_RATE * dt;
        GearPosition += QMath.Clamp(target - GearPosition, -step, step);

        if (state == State.Ground) UpdateGround(dt, axes);
        else UpdateAir(dt, axes);
    }

    private void UpdateAir(float dt, Axes axes)
    {
        // Control surfaces, smoothed so taps don't jolt.
        float slew = 1 - Mathf.Exp(-Config.CONTROL_SMOOTHING * dt);
        Surfaces.X += (axes.Pitch - Surfaces.X) * slew;
        Surfaces.Y += (axes.Roll - Surfaces.Y) * slew;
        Surfaces.Z += (axes.Yaw - Surfaces.Z) * slew;

        // Rigid-body rotation: moments → angular momentum → angular velocity.
        float altitude = Mathf.Max(0, Position.Y);
        Aero.Flow flow = Aero.FlowAngles(Body, Forward, Up, Right, altitude);
        float qbar = QMath.Clamp(flow.Qbar / 1000, 0.2f, 15f); // kPa

        // The control conventions are positive nose-up / right-roll /
        // right-yaw, which is why the y and z signs are inverted here.
        Vector3 w = AngularVelocity;
        float omegaPitch = w.X;
        float omegaYaw = -w.Y;
        float omegaRoll = -w.Z;

        float momentPitch =
            qbar * Config.PITCH_DAMP * (Config.PITCH_RATE * Surfaces.X - omegaPitch)
            - Config.STABILITY_PITCH * qbar * (flow.Alpha - Config.TRIM_ALPHA);
        float momentRoll =
            qbar * Config.ROLL_DAMP * (Config.ROLL_RATE * Surfaces.Y - omegaRoll);
        float momentYaw =
            qbar * Config.YAW_DAMP * (Config.YAW_RATE * Surfaces.Z - omegaYaw)
            + Config.STABILITY_YAW * qbar * flow.Beta;

        Quaternion q = Orientation;
        Vector3 torqueWorld = QMath.QRot(q, new Vector3(momentPitch, -momentYaw, -momentRoll));
        AngularMomentum += torqueWorld * dt;

        // ω = I⁻¹L in the body frame. With no torque L is untouched, so
        // angular momentum is conserved.
        Vector3 localL = QMath.QRot(QMath.QConj(q), AngularMomentum);
        w = new Vector3(
            localL.X / INERTIA_BODY.X,
            localL.Y / INERTIA_BODY.Y,
            localL.Z / INERTIA_BODY.Z);
        AngularVelocity = w;

        Orientation = QMath.QSpin(q, w, dt);
        RefreshBasis();

        // Throttle lever and engine spool.
        if (axes.Throttle != 0)
            Throttle = QMath.Clamp(Throttle + axes.Throttle * Config.THROTTLE_RATE * dt, 0, 1);
        float spool = Throttle > Engine ? Config.ENGINE_SPOOL_UP : Config.ENGINE_SPOOL_DOWN;
        Engine += (Throttle - Engine) * (1 - Mathf.Exp(-spool * dt));

        // Physics: forces in newtons, integrated over the frame.
        Body body = Body;
        body.ClearForces();

        body.ApplyForce(new Vector3(0, -Config.GRAVITY * body.Mass, 0));

        // Engine thrust acts along the nose.
        body.ApplyForce(Forward * ThrustForce(altitude));

        // Wings and airframe: a real lift curve and drag polar.
        AeroData = Aero.ApplyAerodynamics(body, Forward, Up, Right, altitude, GearPosition);

        // Sweep the frame's movement through the woods and the city.
        Vector3 previous = Position;
        body.Step(dt);
        Position = body.Position;

        if (TreeGen.TreeStrike(previous, Position) != null) { Crash("HIT TREE"); return; }
        if (CityGen.CityStrike(previous, Position) != null) { Crash("HIT BUILDING"); return; }

        // Ground contact.
        WorldSim.SurfaceInfo surface = WorldSim.SurfaceAt(Position.X, Position.Z);
        Surface = surface;
        if (Position.Y <= surface.Height + Config.GEAR_HEIGHT && Elapsed > GroundIgnoreUntil)
            Touchdown(surface);
    }

    private void UpdateGround(float dt, Axes axes)
    {
        Body body = Body;
        WorldSim.SurfaceInfo surface = WorldSim.SurfaceAt(Position.X, Position.Z);

        // Water is not a taxi surface.
        if (surface.Kind == WorldSim.SurfaceKind.Water) { Crash("DITCHED", surface); return; }

        GroundHandling handling = SURFACE_HANDLING[surface.Kind];
        Surface = surface;
        AeroData = Aero.AeroData.Idle;

        // Nosewheel steering: a simple bicycle model.
        float steerInput = QMath.Clamp(axes.Yaw + axes.Roll * 0.6f, -1, 1);
        float steerStep = Config.STEER_RATE * handling.Steer * dt;
        float steerLimit = Config.MAX_STEER * handling.Steer;
        SteerAngle += QMath.Clamp(steerInput * steerLimit - SteerAngle, -steerStep, steerStep);

        // Yaw rate is curvature times forward speed. Tyre grip caps the
        // curvature, so the aircraft can turn tightly at taxi speed but only
        // gently at take-off speed.
        float forwardSpeed = Mathf.Max(0,
            body.Velocity.X * Forward.X + body.Velocity.Z * Forward.Z);
        if (forwardSpeed > 0.05f)
        {
            float gripCurvature = handling.Grip / (forwardSpeed * forwardSpeed);
            float curvature = QMath.Clamp(Mathf.Tan(SteerAngle) / Config.WHEELBASE, -gripCurvature, gripCurvature);
            GroundHeading += forwardSpeed * curvature * dt;
        }

        // Orientation follows the nosewheel and the local slope.
        ApplyGroundPose(surface);

        // Throttle lever and engine spool.
        if (axes.Throttle != 0)
            Throttle = QMath.Clamp(Throttle + axes.Throttle * Config.THROTTLE_RATE * dt, 0, 1);
        float spool = Throttle > Engine ? Config.ENGINE_SPOOL_UP : Config.ENGINE_SPOOL_DOWN;
        Engine += (Throttle - Engine) * (1 - Mathf.Exp(-spool * dt));

        // Forces on the rolling airframe.
        body.ClearForces();

        // Engine thrust acts along the nose, which on a slope is tilted with
        // it.
        body.ApplyForce(Forward * ThrustForce(Position.Y));

        float horizontal = Mathf.Sqrt(body.Velocity.X * body.Velocity.X + body.Velocity.Z * body.Velocity.Z);
        bool brakeHeld = axes.Throttle < 0 && Throttle <= 0.001f;

        // Gravity down the slope. A stationary aircraft with the brake set
        // holds still instead of creeping downhill.
        if (!(brakeHeld && horizontal < 0.3f))
        {
            body.ApplyForce(new Vector3(
                -surface.Gradient.X * Config.GRAVITY * Config.MASS, 0,
                -surface.Gradient.Y * Config.GRAVITY * Config.MASS));
        }

        // Aerodynamic drag. The wheels carry the weight.
        Aero.ApplyGroundDrag(body, Position.Y, GearPosition);

        if (horizontal > 0.02f)
        {
            // Rolling resistance and brakes oppose the direction of travel.
            // Capping at speed/dt keeps a stopped aircraft from being pushed
            // backwards.
            float rolling = handling.Rolling;
            float braking = brakeHeld ? handling.Brake : 0;
            float stop = Mathf.Min((rolling + braking) * Config.MASS, (horizontal / dt) * Config.MASS);
            body.ApplyForce(new Vector3(
                -(body.Velocity.X / horizontal) * stop, 0,
                -(body.Velocity.Z / horizontal) * stop));
        }

        // Lateral tyre grip pulls the velocity into line with the nose.
        float lateral = body.Velocity.X * Right.X + body.Velocity.Z * Right.Z;
        float grip = QMath.Clamp(
            -lateral * handling.Slip * Config.MASS,
            -handling.Grip * Config.MASS,
            handling.Grip * Config.MASS);
        body.ApplyForce(new Vector3(Right.X * grip, 0, Right.Z * grip));

        Vector3 previous = Position;
        body.Step(dt);
        Position = body.Position;
        body.Velocity = new Vector3(body.Velocity.X, 0, body.Velocity.Z); // the wheels carry the weight

        WorldSim.SurfaceInfo after = WorldSim.SurfaceAt(Position.X, Position.Z);
        ApplyGroundPose(after);

        // Taxiing off the pavement into the woods is just as final as flying
        // into them.
        if (TreeGen.TreeStrike(previous, Position) != null) { Crash("HIT TREE", after); return; }
        if (CityGen.CityStrike(previous, Position) != null) { Crash("HIT BUILDING", after); return; }

        if (after.Kind == WorldSim.SurfaceKind.Water) { Crash("DITCHED", after); return; }

        // Runway excursions are speed-dependent.
        if (surface.Kind == WorldSim.SurfaceKind.Runway && after.Kind != WorldSim.SurfaceKind.Runway &&
            Speed > SURFACE_HANDLING[WorldSim.SurfaceKind.Grass].TaxiLimit)
        {
            Crash("RAN OFF RUNWAY", after);
            return;
        }

        if (after.Kind == WorldSim.SurfaceKind.Rough && surface.Kind != WorldSim.SurfaceKind.Rough &&
            Speed > SURFACE_HANDLING[WorldSim.SurfaceKind.Rough].TaxiLimit)
        {
            Crash("ROUGH GROUND", after);
            return;
        }

        if (after.Slope > Config.MAX_TAXI_SLOPE && Speed > Config.TAXI_SLOPE_SPEED)
        {
            Crash("STEEP GROUND", after);
            return;
        }

        // Rotate and fly again.
        if (Speed > Config.ROTATE_SPEED && axes.Pitch > 0) TakeOff();
    }

    // ------------------------------------------------------------ transitions

    private void Touchdown(WorldSim.SurfaceInfo surface)
    {
        if (surface.Kind == WorldSim.SurfaceKind.Water) { Crash("DITCHED", surface); return; }

        GroundHandling handling = SURFACE_HANDLING[surface.Kind];
        float impact = ImpactSpeed(surface);
        float bank = Mathf.Abs(BankRelativeTo(surface.Normal));
        float alignment = Mathf.Max(
            DriftAngle,
            surface.Kind == WorldSim.SurfaceKind.Runway ? RunwayAlignment : 0);

        // A gear-up arrival is a belly landing. Then judge the arrival against
        // the surface it is on. Impact comes before speed so flying into a
        // hillside reads as a terrain impact rather than an over-fast
        // touchdown.
        if (GearPosition < 0.95f) { Crash("GEAR UP", surface); return; }
        if (impact > handling.MaxDescent)
        {
            string reason = surface.Slope > 0.12f ? "TERRAIN IMPACT" : "HARD LANDING";
            Crash(reason, surface);
            return;
        }
        if (Speed > handling.MaxSpeed) { Crash("TOO FAST", surface); return; }
        if (bank > handling.MaxBank) { Crash("WING DOWN", surface); return; }
        if (alignment > handling.MaxAlign) { Crash("CRABBED", surface); return; }
        if (surface.Slope > handling.MaxSlope) { Crash("STEEP TERRAIN", surface); return; }

        Land(surface, impact);
    }

    private void Land(WorldSim.SurfaceInfo surface, float impact)
    {
        state = State.Ground;
        LandedAt = Elapsed;
        Surface = surface;

        // The wheels follow the nose, but the aircraft keeps its momentum.
        GroundHeading = HeadingRad;
        SteerAngle = 0;
        StopRotation();
        Body.Velocity = new Vector3(Body.Velocity.X, 0, Body.Velocity.Z);

        GroundHandling handling = SURFACE_HANDLING[surface.Kind];
        LastLanding = new LandingRecord
        {
            Surface = surface.Kind,
            Sink = impact,
            Speed = Body.Speed,
            Firm = impact > handling.MaxDescent * 0.75f,
        };

        ApplyGroundPose(surface);
    }

    private void TakeOff()
    {
        state = State.Air;
        LandedAt = -1;
        SteerAngle = 0;
        GroundIgnoreUntil = Elapsed + Config.TAKEOFF_GRACE;

        // Rotate to a shallow climb, then hand control back to the flyer.
        Quaternion yaw = QMath.QAxis(0, 1, 0, -GroundHeading);
        Orientation = QMath.QNorm(QMath.QMul(yaw, QMath.QAxis(1, 0, 0, 0.10f)));
        RefreshBasis();
        Position = new Vector3(Position.X, Position.Y + 1.2f, Position.Z);

        // Keep the rolling velocity level. Rotating the nose away from it
        // gives the wing the angle of attack that starts the climb.
        float speed = Body.Speed;
        Body.SetVelocity(new Vector3(
            Mathf.Sin(GroundHeading) * speed, 0,
            -Mathf.Cos(GroundHeading) * speed));
        StopRotation();
        Body.Position = Position;
    }

    private void Crash(string reason, WorldSim.SurfaceInfo? surfaceOpt = null)
    {
        WorldSim.SurfaceInfo where = surfaceOpt ?? WorldSim.SurfaceAt(Position.X, Position.Z);
        state = State.Crashed;
        CrashReason = reason;
        Surface = where;
        CrashImpact = new ImpactRecord
        {
            Speed = Speed,
            Sink = ImpactSpeed(where),
            Surface = where.Kind,
        };
        AeroData = Aero.AeroData.Idle;
        Engine = 0;
        Throttle = 0;
        StopRotation();
        Position = new Vector3(Position.X, where.Height + Config.GEAR_HEIGHT, Position.Z);
        Body.Position = Position;
    }

    // --------------------------------------------------------------- helpers

    private void RefreshBasis()
    {
        Forward = QMath.QRot(Orientation, new Vector3(0, 0, -1));
        Up = QMath.QRot(Orientation, new Vector3(0, 1, 0));
        Right = QMath.QRot(Orientation, new Vector3(1, 0, 0));
    }

    /** Clear all rotational momentum, e.g. on the ground or after a crash. */
    private void StopRotation()
    {
        AngularMomentum = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
    }

    /**
     * Thrust force from the spooled engine, in newtons.
     * The propeller sees the component of velocity along the nose, not the
     * total speed, so a vertical dive does not unload it the way forward
     * flight does.
     */
    private float ThrustForce(float altitude)
    {
        Vector3 v = Body.Velocity;
        float forwardSpeed = Mathf.Max(0, v.Dot(Forward));
        return Propulsion.ThrustForce(altitude, Engine, forwardSpeed);
    }

    /** Sit on the wheels, nose along the ground heading, attitude on the slope. */
    private void ApplyGroundPose(WorldSim.SurfaceInfo? surfaceOpt = null)
    {
        WorldSim.SurfaceInfo ground = surfaceOpt ?? WorldSim.SurfaceAt(Position.X, Position.Z);
        Surface = ground;

        float heading = GroundHeading;
        float hx = Mathf.Sin(heading);
        float hz = -Mathf.Cos(heading);

        // Clamp the local grade so a cliff edge cannot throw the aircraft into
        // a silly attitude.
        float gx = QMath.Clamp(ground.Gradient.X, -Config.MAX_GROUND_SLOPE, Config.MAX_GROUND_SLOPE);
        float gz = QMath.Clamp(ground.Gradient.Y, -Config.MAX_GROUND_SLOPE, Config.MAX_GROUND_SLOPE);

        Vector3 forward = new Vector3(hx, hx * gx + hz * gz, hz).Normalized();
        Vector3 normal = new Vector3(-gx, 1, -gz).Normalized();

        Vector3 right = forward.Cross(normal);
        if (right.Length() < 1e-3f)
            right = new Vector3(Mathf.Cos(heading), 0, Mathf.Sin(heading));
        right = right.Normalized();
        Vector3 up = right.Cross(forward).Normalized();

        Orientation = QMath.QFromBasis(right, up, forward);
        RefreshBasis();
        StopRotation();
        Position = new Vector3(Position.X, ground.Height + Config.GEAR_HEIGHT, Position.Z);
        Body.Position = Position;
    }

    /** Closing speed along the surface normal, m/s. */
    private float ImpactSpeed(WorldSim.SurfaceInfo surface)
    {
        Vector3 v = Body.Velocity;
        Vector3 n = surface.Normal;
        return Mathf.Max(0, -(v.X * n.X + v.Y * n.Y + v.Z * n.Z));
    }

    /** Bank relative to the surface; zero means the wings sit level on it. */
    private float BankRelativeTo(Vector3 normal)
    {
        return Mathf.Atan2(Right.Dot(normal), Up.Dot(normal));
    }

    // Per-surface handling and landing limits (js/config.js SURFACE_HANDLING).
    public class GroundHandling
    {
        public float Rolling, Brake, Grip, Slip, Steer;
        public float MaxDescent, MaxSpeed, MaxBank, MaxAlign, MaxSlope, TaxiLimit;
    }

    public static readonly System.Collections.Generic.Dictionary<WorldSim.SurfaceKind, GroundHandling> SURFACE_HANDLING =
        new()
        {
            [WorldSim.SurfaceKind.Runway] = new GroundHandling
            {
                Rolling = 0.30f, Brake = 4.8f, Grip = 8.0f, Slip = 6.0f, Steer = 1.0f,
                MaxDescent = 4.5f, MaxSpeed = 90f, MaxBank = 0.20f, MaxAlign = 0.35f,
                MaxSlope = 0.05f, TaxiLimit = float.PositiveInfinity,
            },
            [WorldSim.SurfaceKind.Grass] = new GroundHandling
            {
                Rolling = 1.20f, Brake = 3.8f, Grip = 5.5f, Slip = 4.0f, Steer = 0.82f,
                MaxDescent = 3.0f, MaxSpeed = 70f, MaxBank = 0.17f, MaxAlign = 0.35f,
                MaxSlope = 0.12f, TaxiLimit = 30f,
            },
            [WorldSim.SurfaceKind.Rough] = new GroundHandling
            {
                Rolling = 3.20f, Brake = 3.0f, Grip = 4.0f, Slip = 3.0f, Steer = 0.55f,
                MaxDescent = 1.8f, MaxSpeed = 45f, MaxBank = 0.12f, MaxAlign = 0.30f,
                MaxSlope = 0.20f, TaxiLimit = 12f,
            },
            [WorldSim.SurfaceKind.Water] = new GroundHandling
            {
                Rolling = 20f, Brake = 20f, Grip = 0, Slip = 0, Steer = 0,
                MaxDescent = 0, MaxSpeed = 0, MaxBank = 0, MaxAlign = 0,
                MaxSlope = 0, TaxiLimit = 0,
            },
        };
}
