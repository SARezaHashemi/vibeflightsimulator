# Mini Flight

A tiny, dependency-free flight simulator that runs in the browser. Everything
is drawn with the 2D canvas API — no WebGL, no libraries, no build step.

You start on final approach to a small airfield, with a graded plateau, a
1.7 km runway and a pair of snow-capped mountains to navigate by.

## Running it

The source is split into ES modules, so it must be served over HTTP (browsers
refuse to load modules from `file://`). Any static server will do:

```sh
cd flight
python3 -m http.server 8123
```

Then open <http://localhost:8123>.

## Controls

| Key | Action |
| --- | --- |
| `S` / `↓` | pitch up (pull back) |
| `W` / `↑` | pitch down (push forward) |
| `A` `D` / `←` `→` | roll left / right |
| `Q` `E` | rudder, and nosewheel steering on the ground |
| `Shift` / `X` | throttle up / down — `X` is the brake on the ground |
| `G` | raise or lower the landing gear |
| `R` | restart after a crash |

## Flying and landing

Climbing costs airspeed, diving builds it. The wing stalls when its angle of
attack gets too high — the HUD shows AoA, wing loading, and acceleration along
the nose, and the wing starts to give up around 15° — so unload the stick and
let the nose drop when the stall warning blinks. Air density falls with
altitude, so the wing needs a little more speed up high.

The runway marker at the top of the HUD gives distance and a turn arrow. Line
up with the centreline, gear **down** (three green lamps), and touch down at a
shallow angle. The surface decides how much the airframe will forgive:

| Surface | Max impact | Max speed | Max bank | Max drift |
| --- | --- | --- | --- | --- |
| Runway | 4.5 m/s | 324 km/h | 11° | 20° |
| Grass | 3.0 m/s | 252 km/h | 10° | 20° |
| Rough / rock | 1.8 m/s | 162 km/h | 7° | 17° |

A landing is accepted when the gear is down, the aircraft is on a dry surface,
and it is inside the limits for that surface. Grass and open fields are
landable, so a gentle arrival beside the runway no longer ends the flight.
Landing on water ditches the aircraft. A slope too steep for the surface is
refused, and flying into rising ground is a terrain impact. Touchdowns in the
top quarter of a surface's impact limit are reported as a **firm landing**.

Impact is measured as closing speed along the surface normal, not only as
vertical speed. That means flying level into rising ground registers as a hard
arrival even when the altimeter is steady, and landing uphill uses a little
more of the available travel than landing on the flat.

Once down you can brake with `X`, steer with the rudder, and take off again by
holding `S` above ~198 km/h. The aircraft keeps its momentum on touchdown; the
tyres pull it straight instead of snapping it onto the runway heading. Surface
handling matters: grass rolls harder and has less grip, and rough ground stops
you quickly. Taxiing off the pavement at taxi pace is harmless — the HUD shows
`SURFACE GRASS` — while leaving it above about 108 km/h is a runway excursion.
Entering rough ground above ~43 km/h breaks the gear, and very steep ground
will dig in. The nosewheel steers like a bicycle: it needs forward speed to
turn, is tight at taxi pace, and is limited by tyre grip at speed, so a hard
turn will slide a little.

Woods are solid. Spruce, pine, oak, poplar and birch stand at different sizes.
Near trees are low-poly volumes — a trunk prism under a canopy of lit, faceted
polygonal tiers — and collapse to a silhouette at distance. Flying into one
ends the flight (`HIT TREE`); the strike test sweeps the whole frame's movement
so a fast aircraft cannot slip between frames.

## Project layout

```
flight/
├── index.html          page shell
├── css/
│   └── style.css       page reset and canvas layout
└── js/
    ├── main.js         bootstrap and frame loop
    ├── config.js       every tunable constant in one place
    ├── math.js         vector / quaternion helpers
    ├── physics.js      point-mass bodies: forces → acceleration → velocity
    ├── propulsion.js   engine power, propeller thrust, density lapse
    ├── aero.js         air density, lift curve, drag polar, aerodynamic forces
    ├── input.js        keyboard → normalised control axes
    ├── world.js        heightfield, airfield plateau, sea surface, surface types, colours
    ├── aircraft.js     flight model, gear, landing, crash reasons
    ├── camera.js       cockpit camera, projection and near-plane clipping
    ├── sky.js          horizon, sky gradient and sun
    ├── terrain.js      grid mesh, painter's sort, shading, haze
    ├── scenery.js      the runway and its markings
    ├── trees.js        procedural woods, species, tree strikes, depth-sorted
    ├── clouds.js       cloud puffs, depth-sorted with the terrain
    └── hud.js          head-up display
```

### How it fits together

`main.js` owns the loop. Each frame it asks `Input` for the four control
axes, hands them to `Aircraft.update()` (the only place that mutates the
simulation), points `Camera` at the aircraft, and then draws: sky → terrain
(+ runway, trees and clouds) → HUD.

### Physics

In the air the aircraft's translation is a point mass. Each frame `Aircraft`
clears its body's forces, adds gravity and thrust, then lets `aero.js` add the
wing's lift, the drag polar and the side force from sideslip, before
`Body.step()` integrates velocity and position with velocity Verlet. That
integrator conserves ballistic mechanical energy exactly, so under gravity the
only energy changes come from the real work done by thrust and drag.

Lift follows a real curve: the coefficient rises linearly with angle of attack
until `CL_MAX`, then the flow separates and lift drops away while separated-flow
drag climbs. Lift is perpendicular to the relative wind and lies in the
aircraft's plane of symmetry, so it banks with the wings and disappears in a
vertical climb. Drag is parasite drag plus induced drag (which grows with the
square of lift, so pulling g costs speed) plus the post-stall term. Air density
falls exponentially with altitude. There is no artificial "wing holds the
weight up" force any more; the aircraft stays up because the wing is producing
enough lift, and it sinks when it is not.

Attitude is a rigid body. Control, damping and stability moments act about
the body axes and update an angular-momentum vector kept in world axes; the
angular velocity is `I⁻¹L` in the body frame, and the orientation quaternion is
advanced about that axis exactly. With no moment the angular momentum stays
put, and the inertia differences produce real gyroscopic coupling between
pitch, roll and yaw. The pitch stability moment holds a trim angle of attack,
and the yaw stability moment weathervanes the nose into the relative wind.

The throttle commands shaft power, not thrust. The engine spools towards the
lever with separate opening and closing lags, and its power falls in proportion
to air density. The propeller turns that power into thrust two ways: at low
speed momentum theory caps it at the static thrust of the disc, while in forward
flight the same power can only do `η·P/V` work. The smaller of the two is used,
which gives strong, near-constant acceleration during the take-off roll and a
natural `1/V` thrust lapse towards cruise. Full throttle in thin air, or opening
it at speed, therefore takes time to show up at the wing. On the ground the same
body is used again: thrust, gravity down the local slope, parasite drag,
surface-dependent rolling resistance, brakes and lateral tyre grip act as
forces, while a bicycle model steers the nosewheel. Yaw rate comes from forward
speed and wheel angle, capped by available grip, so taxi turns are tight,
high-speed turns are gentle, and the aircraft keeps its momentum through a turn
instead of snapping onto a new heading. The ground pose follows the terrain
slope as well as the heading. Spring/damper landing gear is the natural next
step.

The sea is flat. `terrainHeight` still describes the seabed — hills and all —
but cells whose four corners are below `SEA_LEVEL` are drawn from a flat plane
at sea level instead of from the ground beneath. Three long, shallow sines
anchored to world coordinates and advanced by the frame time ripple that plane,
and the near cells are split into wave facets so the swell catches the sun and
mixes crest blue into the deep water. Shore cells are the exception: they keep
the land mesh, with their underwater corners pinned to the same wave surface, so
land meets water at the shoreline. For anything that touches the world —
collision, altitude above ground, camera clearance — `surfaceHeight` takes the
higher of the ground and the sea, so the flat water is solid and not a hole down
to the sea floor.

The runway is flat geometry sitting on the terrain, so it is projected through
the camera as world-space quads (clipped against the near plane, which matters
when it is under your wheels) and handed to the terrain renderer as a *layer*.
The renderer slots it into its back-to-front painter order by depth, so hills
in front of the airfield still occlude it correctly.

Renderers never touch simulation state; they read the aircraft and camera
only. Tuning the feel of the aircraft is therefore a matter of editing
`js/config.js` — thrust, drag, control rates, landing tolerances, gear travel
and the palette all live in that one table.
# vibeflightsimulator
