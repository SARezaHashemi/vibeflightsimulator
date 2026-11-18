/**
 * Central tuning table — every magic number the game uses lives here.
 * Units are metres and seconds.
 */

/* ---- view ---------------------------------------------------------- */
export const FOV_DEG = 62;

/* ---- terrain ------------------------------------------------------- */
export const GRID_RADIUS = 28;   // cells between the aircraft and the edge of the world grid
export const CELL_SIZE = 110;    // fixed cell size in metres: keeps terrain locked to world coordinates
export const CELL_MIN = 110;
export const CELL_MAX = 110;
export const CELL_PER_METRE = 0.42;
export const CELL_BIAS = 45;
export const FOG_START = 0.50;   // fraction of the draw distance where haze begins
export const FOG_CURVE = 1.4;

/* ---- flight model -------------------------------------------------- */
export const MASS = 1100;        // kg — the physics body's mass
export const GRAVITY = 9.8;
export const TRIM_ALPHA = 0.038; // rad (~2.2°) of angle of attack hands-off
export const PITCH_RATE = 1.15;  // rad/s
export const ROLL_RATE = 2.40;   // rad/s
export const YAW_RATE = 0.70;    // rad/s

/* ---- aerodynamics (lift, drag, angle of attack) -------------------- */
export const RHO_SEA = 1.225;     // kg/m³, sea-level air density
export const SCALE_HEIGHT = 8500; // m, exponential decay of air density with altitude
export const WING_AREA = 16.2;    // m², planform area
export const WING_SPAN = 10.9;    // m
export const CL_ALPHA = 5.2;      // 1/rad, lift slope before the stall
export const CL_MAX = 1.35;       // peak lift coefficient
export const ALPHA_STALL = 0.30;  // rad (~17°), stall angle (lift caps at CL_MAX slightly earlier)
// Parasitic drag of the clean airframe. Realistic for a light aircraft; with
// a real propeller thrust curve this gives a cruise near 70 m/s and a top
// speed near 80 m/s instead of the old arcade envelope.
export const CD0 = 0.030;
export const CD_STALL = 0.90;     // extra separated-flow drag past the stall
export const CY_BETA = 0.40;      // side-force coefficient from sideslip
export const OSWALD = 0.78;       // span efficiency for induced drag

/* ---- rotational dynamics ------------------------------------------- */
// Moments are N·m. Damping and stability terms are scaled by dynamic
// pressure in kPa, so control power falls off at low speed and rises with it.
export const INERTIA = { pitch: 9000, roll: 6000, yaw: 11000 }; // kg·m²
export const ELEVATOR_AUTH = 1.0; // elevator effectiveness scale
export const AILERON_AUTH = 1.0;  // aileron effectiveness scale
export const RUDDER_AUTH = 1.0;   // rudder effectiveness scale
export const PITCH_DAMP = 6500;   // N·m·s/rad per kPa — sets the pitch response time
export const ROLL_DAMP = 4000;    // N·m·s/rad per kPa
export const YAW_DAMP = 7000;     // N·m·s/rad per kPa
export const STABILITY_PITCH = 16000; // N·m/rad per kPa — holds the trim angle of attack
export const STABILITY_YAW = 10000;   // N·m/rad per kPa — weathervane into the relative wind
export const THROTTLE_RATE = 0.55; // throttle lever travel per second
export const CONTROL_SMOOTHING = 9; // input ramp, higher = snappier

/* ---- engine and propeller ------------------------------------------ */
// The throttle commands shaft power, not thrust. A 180 kW engine turns a
// 1.9 m propeller; momentum theory sets the static thrust limit, and once the
// aircraft is moving the same power can only do η·P/V work on it. That gives
// realistic acceleration at low speed and a 1/V thrust lapse in the cruise.
export const ENGINE_POWER = 180000;   // W, sea-level shaft power at full throttle
export const ENGINE_IDLE = 0.04;      // fraction of maximum power at idle
export const ENGINE_SPOOL_UP = 1.3;   // 1/s, lag when opening the throttle
export const ENGINE_SPOOL_DOWN = 2.1; // 1/s, lag when closing it
export const PROP_DIAMETER = 1.9;     // m
export const PROP_EFFICIENCY = 0.82;  // propulsive efficiency in the cruise
export const PROP_STATIC_EFFICIENCY = 0.80; // losses at zero forward speed
export const EYE_HEIGHT = 1.15;  // cockpit eye, above the aircraft's origin
export const EYE_FORWARD = 2.9;  // ... and this far ahead, inside the canopy

/* ---- cameras -------------------------------------------------------- */
export const CHASE_BACK = 18;    // metres behind the aircraft
export const CHASE_LIFT = 4.5;   // metres above it
export const CHASE_AIM = 8;      // aim this far ahead so the nose stays centred
export const COCKPIT_CULL = 1.6; // hide model faces closer than this to the eye

/* ---- landing gear and ground handling ------------------------------ */
export const GEAR_RATE = 2.2;         // extension travel per second
export const GEAR_DRAG = 0.012;       // extra drag coefficient while the gear is down
export const GEAR_HEIGHT = 1.6;       // contact point above the ground
export const ROTATE_SPEED = 42;       // m/s needed to lift off again (~151 km/h)
export const WHEELBASE = 7;           // m — nosewheel to main gear
export const MAX_STEER = 0.50;        // rad (~29°) of nosewheel deflection
export const STEER_RATE = 2.2;        // rad/s — how fast the nosewheel turns
export const TAKEOFF_GRACE = 0.7;     // s of immunity from ground contact
export const MAX_GROUND_SLOPE = 0.55; // steepest grade the ground pose follows
export const MAX_TAXI_SLOPE = 0.28;   // steeper than this and taxiing digs in
export const TAXI_SLOPE_SPEED = 2.0;  // m/s; below this a slope cannot hurt you

/**
 * Per-surface handling and landing limits.
 *
 * `maxDescent` is the closing speed along the surface normal, not just the
 * vertical speed, so flying level into a hillside still counts as a hard
 * arrival. `taxiLimit` is how fast the aircraft may enter that surface from a
 * smoother one. A runway excursion at taxi pace is harmless; the same
 * excursion at take-off speed is not.
 */
export const SURFACE_HANDLING = {
  runway: {
    rolling: 0.30, brake: 4.8, grip: 8.0, slip: 6.0, steer: 1.0,
    maxDescent: 4.5, maxSpeed: 90, maxBank: 0.20, maxAlign: 0.35,
    maxSlope: 0.05, taxiLimit: Infinity,
  },
  grass: {
    rolling: 1.20, brake: 3.8, grip: 5.5, slip: 4.0, steer: 0.82,
    maxDescent: 3.0, maxSpeed: 70, maxBank: 0.17, maxAlign: 0.35,
    maxSlope: 0.12, taxiLimit: 30,
  },
  rough: {
    rolling: 3.20, brake: 3.0, grip: 4.0, slip: 3.0, steer: 0.55,
    maxDescent: 1.8, maxSpeed: 45, maxBank: 0.12, maxAlign: 0.30,
    maxSlope: 0.20, taxiLimit: 12,
  },
  water: {
    rolling: 20, brake: 20, grip: 0, slip: 0, steer: 0,
    maxDescent: 0, maxSpeed: 0, maxBank: 0, maxAlign: 0,
    maxSlope: 0, taxiLimit: 0,
  },
};

/* ---- start state --------------------------------------------------- */
export const START = { x: 0, y: 400, z: -300 };
export const START_SPEED = 85;
export const START_THROTTLE = 0.65;

/* ---- palette ------------------------------------------------------- */
export const HAZE = [198, 220, 236];
export const WATER = [26, 78, 118];
export const WATER_CREST = [74, 132, 158];
export const SEA_LEVEL = 0;

export const SKY_STOPS = [
  [0.00, '#08213f'],
  [0.45, '#2f6ea8'],
  [0.75, '#79a9ce'],
  [1.00, 'rgb(198,220,236)'],
];

export const SUN_DIR = { x: -0.55, y: 0.47, z: -0.69 };
export const SUN_GLOW = 'rgba(255,244,220,';

export const FONT = 'ui-monospace, SFMono-Regular, Menlo, Consolas, monospace';
export const HUD_TINT = 'rgba(150,215,255,';

/* ---- clouds -------------------------------------------------------- */
export const CLOUD_CELL = 1600;       // m, spacing of the cloud grid
export const CLOUD_RANGE = 6;         // cells around the aircraft that are populated
export const CLOUD_DENSITY = 0.45;    // fraction of cells holding a cloud
export const CLOUD_BASE_MIN = 900;    // m above sea level, lowest cloud base
export const CLOUD_BASE_MAX = 1800;   // m, highest cloud base
export const CLOUD_MAX_DISTANCE = 9000; // m, beyond this clouds are not drawn
export const CLOUD_OPACITY = 0.94;

/* ---- trees --------------------------------------------------------- */
export const TREE_RANGE = 24;          // terrain cells around the aircraft to consider
export const TREE_MAX_DISTANCE = 2400; // m, beyond this trees are not drawn
export const TREE_FADE = 300;          // m, distance over which trees fade out
export const TREE_MIN_PIXELS = 3.0;    // cull trees shorter than this on screen
export const TREE_DETAIL_DISTANCE = 320; // m, nearer trees are drawn as solid low-poly volumes
export const TREE_NEAR = 10;           // m, don't draw trees right on the camera
export const TREE_PATCH = 0.52;        // fraction of the map covered by woodland
export const TREE_WOODS_DENSITY = 0.55; // fraction of wooded cells holding trees
export const TREE_SCATTER_DENSITY = 0.08; // lone trees outside the woods
export const TREE_MIN_GROUND = 5;      // m above sea level, no trees on the beach
export const TREE_MAX_GROUND = 290;    // m, no trees on the high rock and snow
export const TREE_MIN_HEIGHT = 7;      // m, before the species' own size scaling
export const TREE_MAX_HEIGHT = 30;     // m
export const TREE_RUNWAY_CLEARANCE = 80;  // m either side of the centreline
export const TREE_APPROACH_CLEARANCE = 500; // m beyond each threshold
export const TREE_COLLISION_MARGIN = 2.5; // m, airframe radius used for tree strikes
