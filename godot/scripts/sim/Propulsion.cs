using Godot;

/**
 * Engine and propeller. Port of js/propulsion.js.
 *
 * The throttle controls shaft power, not thrust. At rest, momentum theory
 * limits the thrust to T = η_static · ∛(2·ρ·A·P²); in forward flight the same
 * shaft power can only produce T = η · P / V. Taking the smaller of the two
 * gives a constant-thrust region at low speed and a 1/V thrust lapse at
 * cruise.
 */
public static class Propulsion
{
    private static readonly float DISC_AREA = Mathf.Pi * (Config.PROP_DIAMETER / 2f) * (Config.PROP_DIAMETER / 2f);

    /** Shaft power available at an altitude and spooled throttle setting, in W. */
    public static float EnginePower(float altitude, float spool)
    {
        float densityRatio = Aero.AirDensity(altitude) / Config.RHO_SEA;
        float lever = Config.ENGINE_IDLE + (1 - Config.ENGINE_IDLE) * spool;
        return Config.ENGINE_POWER * lever * densityRatio;
    }

    /** Propeller thrust in newtons for a given shaft power and forward speed. */
    public static float PropellerThrust(float power, float speed, float altitude)
    {
        if (power <= 0) return 0;

        float density = Aero.AirDensity(altitude);
        float staticThrust = Config.PROP_STATIC_EFFICIENCY *
            Mathf.Pow(2f * density * DISC_AREA * power * power, 1f / 3f);
        float dynamicThrust = Config.PROP_EFFICIENCY * power / Mathf.Max(speed, 1e-3f);

        return Mathf.Min(staticThrust, dynamicThrust);
    }

    /** Thrust force in newtons for the current engine state and airspeed. */
    public static float ThrustForce(float altitude, float spool, float speed)
    {
        return PropellerThrust(EnginePower(altitude, spool), speed, altitude);
    }
}
