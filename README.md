# Mini Flight

A tiny flight simulator built for **Godot 4 (.NET / C#)**. It starts on final
approach to a small airfield, with a graded plateau, a
1.7 km runway and a pair of snow-capped mountains to navigate by. The world
keeps going in every direction: more airfields, towns and cities grow on a
deterministic hash grid, so the same skyline is always in the same place.

## Running it

Requires Godot 4.7 (.NET edition) and a .NET SDK:

```sh
cd godot
dotnet build
godot-mono-rc        # or your own Godot 4.7 mono binary
```

A headless smoke test that prints one telemetry line per simulated second:

```sh
godot-mono-rc --headless -- --sim-debug --quit-after 600
```

## Controls

| Key | Action |
| --- | --- |
| `S` / `↓` | pitch up (pull back) |
| `W` / `↑` | pitch down (push forward) |
| `A` `D` / `←` `→` | roll left / right |
| `Q` `E` | rudder, and nosewheel steering on the ground |
| `Shift` / `X` | throttle up / down — `X` is the brake on the ground |
| `G` | raise or lower the landing gear |
| `C` | chase / cockpit view |
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

## Airfields and cities

Airfields repeat every 10 km or so across the world. Each one is graded into a
flat plateau with its own heading, runway length and elevation, and comes with
a taxiway, apron, hangar and control tower. Every runway is a real landing
surface, and the HUD's runway guide points at the nearest one. Trees and cities
keep their distance from the approach paths.

Cities sit on their own 8 km grid, mostly on dry, gently sloping ground. Each
city turns its street grid through a random angle and fills the blocks between
the roads with randomly sized buildings: low-rise blocks on the outskirts,
denser mid-rise towards the centre and the occasional 250 m tower downtown.
Streets are ground polygons that follow the terrain, so they climb the hills
rather than cutting through them; buildings are faceted boxes that sink a
little foundation into the ground. Flying into a building ends the flight
(`HIT BUILDING`). Both the airfields and the cities are seeded from their grid
cell, so they do not change when you fly back.

Clouds are clusters of pale, faceted puffs. Each puff is a low-poly billboard:
an irregular rim of flat-shaded triangles around a raised hub, with the
underside clamped flat so the cluster sits on a common cloud base. Facets are
lit from above and left, giving the chunky, crystalline look of a low-poly
model, while haze thins them towards the edge of the draw distance.

## Project layout

```
flight/
└── godot/                  the Godot 4 (.NET / C#) project
    ├── project.godot       engine config, main scene
    ├── scenes/Main.tscn    one-node scene; everything else is built in code
    └── scripts/
        ├── Main.cs         bootstrap, frame loop, cameras
        ├── sim/            the simulation — engine-agnostic C#
        │   ├── Config.cs   every tunable constant in one place
        │   ├── QMath.cs    vector / quaternion helpers
        │   ├── Body.cs     point-mass physics: forces → acceleration → velocity
        │   ├── Aero.cs     lift curve, drag polar, aerodynamic forces
        │   ├── Propulsion.cs  engine power, propeller thrust, density lapse
        │   ├── WorldSim.cs heightfield, airfield plateaus, sea, surface types
        │   ├── TreeGen.cs  procedural woods and tree strikes
        │   ├── CityGen.cs  street grids, buildings and building strikes
        │   ├── Aircraft.cs flight model, gear, landing, crash reasons
        │   └── InputRig.cs keyboard → normalised control axes
        └── render/         Godot-side presentation
            ├── MeshBuilder.cs   flat-shaded, vertex-coloured mesh helper
            ├── TerrainMesh.cs   dynamic grid mesh and water cells
            ├── SceneryGen.cs    runways, aprons, hangars, towers
            ├── CityRender.cs    street meshes and building MultiMeshes
            ├── TreeRender.cs    one MultiMesh per species
            ├── CloudRender.cs   cached low-poly cloud clusters
            ├── AircraftModel.cs the low-poly airframe
            └── Hud.cs           head-up display
```

### How it fits together

`Main.cs` owns the loop. Each physics tick it asks `InputRig` for the four
control axes and hands them to `Aircraft.Update()` (the only place that
mutates the simulation). Each rendered frame it points the camera at the
aircraft, then refreshes the scene: terrain → airfields, cities, trees and
clouds → the aircraft model → the HUD.

The simulation in `scripts/sim/` is engine-agnostic C# and never touches
Godot nodes; `scripts/render/` reads the aircraft, world and camera and
builds the 3D scene from them. See `godot/README.md` for how each renderer
maps to what it draws.

### Physics

In the air the aircraft's translation is a point mass. Each step `Aircraft`
clears its body's forces, adds gravity and thrust, then lets `Aero` add the
wing's lift, the drag polar and the side force from sideslip, before
`Body.Step()` integrates velocity and position with velocity Verlet. That
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

The sea is flat. `TerrainHeight` still describes the seabed — hills and all —
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

Airfield surfaces are flat geometry sitting on their graded plateaus, and the
sea, streets and runways alike are vertex-coloured meshes drawn by Godot's 3D
renderer, so occlusion falls out of the depth buffer: hills in front of an
airfield hide it correctly, and buildings are sorted by the GPU rather than by
hand. City streets are ground polygons that follow the terrain, bucketed into
a cached mesh per city, while buildings are instanced boxes in one MultiMesh.

Renderers never touch simulation state; they read the aircraft and world only.
Tuning the feel of the aircraft is therefore a matter of editing one table —
`godot/scripts/sim/Config.cs`. Thrust, drag, control rates, landing
tolerances, gear travel and the palette all live there.
# vibeflightsimulator
