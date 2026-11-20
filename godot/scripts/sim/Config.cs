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

    /* ---- look & lighting -----------------------------------------------------
       Every colour in the world is named here so the whole look can be tuned in
       one place. All surface colours are plain albedo — the sun and ambient
       lights (below) do the shading, so what you set is what you get under the
       lit side; the ambient fill lifts the shaded side. */

    /* sky */
    public static readonly Color SKY_TOP = new(24f / 255f, 72f / 255f, 148f / 255f);
    public static readonly Color SKY_HORIZON = new(235f / 255f, 230f / 255f, 219f / 255f);
    public static readonly Color GROUND_BOTTOM = new(107f / 255f, 120f / 255f, 135f / 255f);
    public const float SUN_DISC_ANGLE = 14f;   // angular size of the sun in the sky
    public const float SUN_DISC_CURVE = 0.12f;

    /* haze: distance fog tinted toward the horizon sky, kept for depth only */
    public const float FOG_BEGIN = 2200f;      // m, where the haze starts
    public const float FOG_END = 20000f;       // m, where it is full
    public const float FOG_DEPTH_CURVE = 1.4f; // how quickly it builds
    public static readonly Color FOG_COLOR = new(168f / 255f, 193f / 255f, 220f / 255f);
    public const float FOG_SKY_AFFECT = 0.0f;  // the sky itself is never fogged

    /* lights */
    public static readonly Color SUN_COLOR = new(1.00f, 0.96f, 0.88f);  // slightly warm
    public const float SUN_ENERGY = 1.2f;
    public const float SUN_SHADOW_MAX_DISTANCE = 2600f;                 // m of soft shadows
    public static readonly Color AMBIENT_COLOR = new(148f / 255f, 173f / 255f, 224f / 255f);  // cool blue fill
    public const float AMBIENT_ENERGY = 0.75f;

    /* tone mapping & grading */
    public const float TONEMAP_WHITE = 4.0f;
    public const float ADJUSTMENT_SATURATION = 1.12f;
    public const float ADJUSTMENT_CONTRAST = 1.06f;

    /* terrain height bands */
    public static readonly Color WET_SAND = new(166f / 255f, 140f / 255f, 96f / 255f);
    public static readonly Color BEACH_SAND = new(228f / 255f, 206f / 255f, 148f / 255f);
    public static readonly Color DUNE_GRASS = new(154f / 255f, 172f / 255f, 92f / 255f);
    public static readonly Color MEADOW = new(56f / 255f, 134f / 255f, 48f / 255f);
    public static readonly Color PASTURE = new(48f / 255f, 134f / 255f, 44f / 255f);
    public static readonly Color DRY_FIELD = new(152f / 255f, 150f / 255f, 64f / 255f);
    public static readonly Color SCREE_LOW = new(128f / 255f, 118f / 255f, 92f / 255f);
    public static readonly Color SCREE_HIGH = new(150f / 255f, 140f / 255f, 120f / 255f);
    public static readonly Color ROCK = new(172f / 255f, 168f / 255f, 160f / 255f);
    public static readonly Color SNOW_LOW = new(214f / 255f, 220f / 255f, 230f / 255f);
    public static readonly Color SNOW_HIGH = new(244f / 255f, 248f / 255f, 252f / 255f);

    /* per-face terrain modifiers */
    public static readonly Color PATCH_LUSH = new(36f / 255f, 128f / 255f, 46f / 255f);   // deep green forests/pasture
    public static readonly Color PATCH_DRY = new(170f / 255f, 164f / 255f, 72f / 255f);   // yellow-green fields
    public static readonly Color SLOPE_ROCK = new(138f / 255f, 128f / 255f, 106f / 255f); // bare rock under the snow line
    public static readonly Color SLOPE_ROCK_SNOW = new(180f / 255f, 184f / 255f, 194f / 255f);

    /* water */
    public static readonly Color WATER = new(28f / 255f, 88f / 255f, 134f / 255f);
    public static readonly Color WATER_CREST = new(104f / 255f, 168f / 255f, 188f / 255f);
    public static readonly Color WATER_LAGOON = new(86f / 255f, 178f / 255f, 164f / 255f);

    /* trees */
    public static readonly Color TRUNK = new(82f / 255f, 60f / 255f, 42f / 255f);
    public static readonly Color[] TREE_TINTS =
    {
        new(38f / 255f, 112f / 255f, 50f / 255f),   // spruce
        new(46f / 255f, 120f / 255f, 42f / 255f),   // pine
        new(82f / 255f, 144f / 255f, 40f / 255f),   // oak
        new(68f / 255f, 152f / 255f, 46f / 255f),   // poplar
        new(128f / 255f, 168f / 255f, 54f / 255f),  // birch
    };

    /* clouds */
    public static readonly Color CLOUD_LIT = new(252f / 255f, 253f / 255f, 255f / 255f);
    public static readonly Color CLOUD_SHADE = new(178f / 255f, 192f / 255f, 208f / 255f);

    /* aircraft */
    public static readonly Color FUSELAGE_TOP = new(243f / 255f, 247f / 255f, 251f / 255f);
    public static readonly Color FUSELAGE_SIDE = new(229f / 255f, 235f / 255f, 241f / 255f);
    public static readonly Color FUSELAGE_BELLY = new(198f / 255f, 206f / 255f, 216f / 255f);
    public static readonly Color FUSELAGE_ACCENT = new(214f / 255f, 40f / 255f, 34f / 255f);
    public static readonly Color WING_COLOUR = new(224f / 255f, 231f / 255f, 238f / 255f);
    public static readonly Color CANOPY_GLASS = new(36f / 255f, 58f / 255f, 80f / 255f);
    public static readonly Color METAL = new(122f / 255f, 128f / 255f, 136f / 255f);
    public static readonly Color TYRE = new(34f / 255f, 35f / 255f, 38f / 255f);

    /* city */
    public static readonly Color CITY_ASPHALT = new(78f / 255f, 80f / 255f, 84f / 255f);
    public static readonly Color CITY_PARK = new(52f / 255f, 118f / 255f, 52f / 255f);
    public static readonly Color[] BUILDING_TINTS =
    {
        new(148f / 255f, 148f / 255f, 146f / 255f),  // concrete
        new(126f / 255f, 132f / 255f, 136f / 255f),  // grey stone
        new(158f / 255f, 150f / 255f, 134f / 255f),  // sandstone
        new(112f / 255f, 122f / 255f, 130f / 255f),  // blue-grey glass
        new(138f / 255f, 126f / 255f, 114f / 255f),  // brick
        new(168f / 255f, 168f / 255f, 162f / 255f),  // pale render
        new(104f / 255f, 112f / 255f, 120f / 255f),  // dark steel
    };

    /* airport */
    public static readonly Color RUNWAY_ASPHALT = new(56f / 255f, 58f / 255f, 62f / 255f);
    public static readonly Color CONCRETE = new(104f / 255f, 104f / 255f, 100f / 255f);
    public static readonly Color MARKING = new(240f / 255f, 240f / 255f, 230f / 255f);
    public static readonly Color HANGAR = new(150f / 255f, 146f / 255f, 136f / 255f);
    public static readonly Color TOWER = new(176f / 255f, 174f / 255f, 166f / 255f);
    public static readonly Color TOWER_GLASS = new(112f / 255f, 138f / 255f, 154f / 255f);

    public const float SEA_LEVEL = 0f;

    // Direction the sun sits in the sky (used by the light and any baked shading).
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
