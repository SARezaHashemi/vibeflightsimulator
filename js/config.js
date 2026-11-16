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
export const MAX_THRUST = 26;    // m/s² at full throttle
export const DRAG = 0.0030;      // quadratic drag coefficient
export const GRAVITY = 9.8;
export const CLIMB_PENALTY = 1.6; // extra speed loss while climbing
export const STALL_SPEED = 62;   // below this the wing stops carrying the weight
export const SAG_RATE = 0.55;
export const LIFT_CAP = 2.5;     // max wing support, in g, in a steep bank
export const PATH_ALIGN = 3.0;   // 1/s — how fast the flight path follows the nose
export const MAX_PATH_ACCEL = 60; // m/s² cap on that aerodynamic alignment
export const PITCH_RATE = 1.15;  // rad/s
export const ROLL_RATE = 2.40;   // rad/s
export const YAW_RATE = 0.70;    // rad/s
export const THROTTLE_RATE = 0.55; // per second
export const CONTROL_SMOOTHING = 9; // input ramp, higher = snappier
export const EYE_HEIGHT = 1.15;  // cockpit eye, above the aircraft's origin
export const EYE_FORWARD = 2.9;  // ... and this far ahead, inside the canopy

/* ---- cameras -------------------------------------------------------- */
export const CHASE_BACK = 18;    // metres behind the aircraft
export const CHASE_LIFT = 4.5;   // metres above it
export const CHASE_AIM = 8;      // aim this far ahead so the nose stays centred
export const COCKPIT_CULL = 1.6; // hide model faces closer than this to the eye

/* ---- landing gear and ground handling ------------------------------ */
export const GEAR_RATE = 2.2;         // extension travel per second
export const GEAR_DRAG = 0.0006;      // extra drag while the gear is down
export const GEAR_HEIGHT = 1.6;       // contact point above the ground
export const LANDING_MAX_DESCENT = 6; // m/s — harder than this and you break
export const LANDING_MAX_SPEED = 100; // m/s
export const LANDING_MAX_BANK = 0.20; // rad (~11°)
export const LANDING_MAX_ALIGN = 0.35;// rad (~20°) of runway alignment
export const ROTATE_SPEED = 55;       // m/s needed to lift off again
export const WHEELBASE = 7;           // m — nosewheel to main gear
export const MAX_STEER = 0.50;        // rad (~29°) of nosewheel deflection
export const STEER_RATE = 2.2;        // rad/s — how fast the nosewheel turns
export const TIRE_GRIP = 8.0;         // m/s² — lateral grip before the tyres slide
export const TIRE_SLIP = 6.0;         // 1/s — how firmly the tyres pull velocity into line
export const ROLL_RESISTANCE = 0.9;   // m/s² while rolling
export const BRAKE_FORCE = 5.5;       // m/s² with the brake held
export const TAKEOFF_GRACE = 0.7;     // s of immunity from ground contact

/* ---- start state --------------------------------------------------- */
export const START = { x: 0, y: 400, z: -300 };
export const START_SPEED = 85;
export const START_THROTTLE = 0.55;

/* ---- palette ------------------------------------------------------- */
export const HAZE = [198, 220, 236];
export const WATER = [26, 78, 118];
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
