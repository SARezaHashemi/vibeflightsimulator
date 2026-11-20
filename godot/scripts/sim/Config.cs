using Godot;

/**
 * Central tuning table — every magic number the game uses lives here.
 * Units are metres and seconds. Ported from js/config.js.
 */
public static class Config
{
    /* ---- view ---------------------------------------------------------- */
    public const float FOV_DEG = 62.0f;

    /* ---- terrain ------------------------------------------------------- */
    public const int GRID_RADIUS = 34;   // cells between the aircraft and the edge of the world grid
    public const float CELL_SIZE = 110f; // fixed cell size in metres
    public const float CELL_MIN = 110f;
    public const float CELL_MAX = 300f;
    public const float FOG_START = 0.50f; // fraction of the draw distance where haze begins
    public const float FOG_CURVE = 1.4f;

    /* ---- world generation ---------------------------------------------- */
    public const int AIRPORT_SPACING = 10000;
    public const float AIRPORT_CHANCE = 0.78f;
    public const float AIRPORT_MIN_LENGTH = 950f;
    public const float AIRPORT_MAX_LENGTH = 1950f;
    public const float AIRPORT_MIN_WIDTH = 34f;
    public const float AIRPORT_MAX_WIDTH = 64f;
    public const float AIRPORT_MIN_ELEVATION = 25f;
    public const float AIRPORT_MAX_ELEVATION = 280f;
    public const float AIRPORT_CLEARANCE = 6000f;

    public const int CITY_SPACING = 8000;
    public const float CITY_CHANCE = 0.62f;
    public const float CITY_MIN_RADIUS = 750f;
    public const float CITY_MAX_RADIUS = 1500f;
    public const float CITY_MAX_DISTANCE = 9500f;
    public const float CITY_DETAIL_DISTANCE = 2200f;
    public const float CITY_TREE_CLEARANCE = 80f;
    public const float CITY_AIRPORT_GAP = 700f;

    /* ---- flight model -------------------------------------------------- */
    public const float MASS = 1100f;      // kg
    public const float GRAVITY = 9.8f;
    public const float TRIM_ALPHA = 0.038f; // rad (~2.2°) hands-off angle of attack
    public const float PITCH_RATE = 1.15f;
    public const float ROLL_RATE = 2.40f;
    public const float YAW_RATE = 0.70f;

    /* ---- aerodynamics --------------------------------------------------- */
    public const float RHO_SEA = 1.225f;
    public const float SCALE_HEIGHT = 8500f;
    public const float WING_AREA = 16.2f;
    public const float WING_SPAN = 10.9f;
    public const float CL_ALPHA = 5.2f;
    public const float CL_MAX = 1.35f;
    public const float ALPHA_STALL = 0.30f;
    public const float CD0 = 0.030f;
    public const float CD_STALL = 0.90f;
    public const float CY_BETA = 0.40f;
    public const float OSWALD = 0.78f;

    /* ---- rotational dynamics -------------------------------------------- */
    public static readonly (float pitch, float roll, float yaw) INERTIA = (9000f, 6000f, 11000f);
    public const float PITCH_DAMP = 6500f;
    public const float ROLL_DAMP = 4000f;
    public const float YAW_DAMP = 7000f;
    public const float STABILITY_PITCH = 16000f;
    public const float STABILITY_YAW = 10000f;
    public const float THROTTLE_RATE = 0.55f;
    public const float CONTROL_SMOOTHING = 9f;

    /* ---- engine and propeller ------------------------------------------- */
    public const float ENGINE_POWER = 180000f;
    public const float ENGINE_IDLE = 0.04f;
    public const float ENGINE_SPOOL_UP = 1.3f;
    public const float ENGINE_SPOOL_DOWN = 2.1f;
    public const float PROP_DIAMETER = 1.9f;
    public const float PROP_EFFICIENCY = 0.82f;
    public const float PROP_STATIC_EFFICIENCY = 0.80f;
    public const float EYE_HEIGHT = 1.15f;
    public const float EYE_FORWARD = 2.9f;

    /* ---- cameras --------------------------------------------------------- */
    public const float CHASE_BACK = 18f;
    public const float CHASE_LIFT = 4.5f;
    public const float CHASE_AIM = 8f;
    public const float COCKPIT_CULL = 1.6f;

    /* ---- landing gear and ground handling -------------------------------- */
    public const float GEAR_RATE = 2.2f;
    public const float GEAR_DRAG = 0.012f;
    public const float GEAR_HEIGHT = 1.6f;
    public const float ROTATE_SPEED = 42f;
    public const float WHEELBASE = 7f;
    public const float MAX_STEER = 0.50f;
    public const float STEER_RATE = 2.2f;
    public const float TAKEOFF_GRACE = 0.7f;
    public const float MAX_GROUND_SLOPE = 0.55f;
    public const float MAX_TAXI_SLOPE = 0.28f;
    public const float TAXI_SLOPE_SPEED = 2.0f;

    /* ---- start state ------------------------------------------------------ */
    public static readonly Vector3 START = new(0f, 400f, -300f);
    public const float START_SPEED = 85f;
    public const float START_THROTTLE = 0.65f;

    /* ---- palette ----------------------------------------------------------- */
    public static readonly Color HAZE = new(198f / 255f, 220f / 255f, 236f / 255f);
    public static readonly Vector3 WATER = new(26f, 78f, 118f);
    public static readonly Vector3 WATER_CREST = new(74f, 132f, 158f);
    public const float SEA_LEVEL = 0f;

    public static readonly Vector3 SUN_DIR = new(-0.55f, 0.47f, -0.69f);

    /* ---- clouds ------------------------------------------------------------ */
    public const float CLOUD_CELL = 1600f;
    public const int CLOUD_RANGE = 7;
    public const float CLOUD_DENSITY = 0.48f;
    public const float CLOUD_BASE_MIN = 850f;
    public const float CLOUD_BASE_MAX = 1900f;
    public const float CLOUD_MAX_DISTANCE = 11000f;

    /* ---- trees -------------------------------------------------------------- */
    public const int TREE_RANGE = 24;
    public const float TREE_MAX_DISTANCE = 2400f;
    public const float TREE_FADE = 300f;
    public const float TREE_NEAR = 10f;
    public const float TREE_DETAIL_DISTANCE = 320f;
    public const float TREE_PATCH = 0.52f;
    public const float TREE_WOODS_DENSITY = 0.55f;
    public const float TREE_SCATTER_DENSITY = 0.08f;
    public const float TREE_MIN_GROUND = 5f;
    public const float TREE_MAX_GROUND = 290f;
    public const float TREE_MIN_HEIGHT = 7f;
    public const float TREE_MAX_HEIGHT = 30f;
    public const float TREE_RUNWAY_CLEARANCE = 80f;
    public const float TREE_APPROACH_CLEARANCE = 500f;
    public const float TREE_COLLISION_MARGIN = 2.5f;
}
