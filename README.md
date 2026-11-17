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
attack gets too high — the HUD shows AoA and wing loading, and the wing starts
to give up around 15° — so unload the stick and let the nose drop when the
stall warning blinks. Air density falls with altitude, so the wing needs a
little more speed up high.

The runway marker at the top of the HUD gives distance and a turn arrow. Line
up with the centreline, gear **down** (three green lamps), and touch down at
a shallow angle. A touchdown is a landing only if all of these hold:

| Condition | Fail message |
| --- | --- |
| On the paved surface | `TERRAIN` |
| Gear locked down | `GEAR UP` |
| Under 360 km/h | `TOO FAST` |
| Sink rate under 6 m/s | `HARD LANDING` |
| Wings within ~11° of level | `WING DOWN` |
| Aligned within ~20° of the runway | `CRABBED` |

Once down you can brake with `X`, steer with the rudder, and take off again by
holding `S` above ~198 km/h. Rolling off the pavement faster than 65 km/h
breaks the aircraft (`RAN OFF RUNWAY`). The nosewheel steers like a bicycle:
it needs forward speed to turn, is tight at taxi pace, and is limited by tyre
grip at speed, so a hard turn will slide a little.

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
    ├── aero.js         air density, lift curve, drag polar, aerodynamic forces
    ├── input.js        keyboard → normalised control axes
    ├── world.js        heightfield, airfield plateau, surface colours
    ├── aircraft.js     flight model, gear, landing, crash reasons
    ├── camera.js       cockpit camera, projection and near-plane clipping
    ├── sky.js          horizon, sky gradient and sun
    ├── terrain.js      grid mesh, painter's sort, shading, haze
    ├── scenery.js      the runway and its markings
    ├── clouds.js       cloud puffs, depth-sorted with the terrain
    └── hud.js          head-up display
```

### How it fits together

`main.js` owns the loop. Each frame it asks `Input` for the four control
axes, hands them to `Aircraft.update()` (the only place that mutates the
simulation), points `Camera` at the aircraft, and then draws: sky → terrain
(+ runway) → HUD.

### Physics

In the air the aircraft is a point mass. Each frame `Aircraft` clears its
body's forces, adds gravity and thrust, then lets `aero.js` add the wing's
lift, the drag polar and the side force from sideslip, before `Body.step()`
integrates velocity and position.

Lift follows a real curve: the coefficient rises linearly with angle of attack
until `CL_MAX`, then the flow separates and lift drops away while separated-flow
drag climbs. Lift is perpendicular to the relative wind and lies in the
aircraft's plane of symmetry, so it banks with the wings and disappears in a
vertical climb. Drag is parasite drag plus induced drag (which grows with the
square of lift, so pulling g costs speed) plus the post-stall term. Air density
falls exponentially with altitude. There is no artificial "wing holds the
weight up" force any more; the aircraft stays up because the wing is producing
enough lift, and it sinks when it is not.

Attitude is still kinematic: the control axes rotate the orientation
quaternion directly, with no torques or moments from the aero forces. The
pitch stability moment holds a trim angle of attack, and the yaw stability
moment weathervanes the nose into the relative wind. On the ground the same
body is used again: thrust, parasite drag, rolling resistance, brakes and
lateral tyre grip act as forces, while a bicycle model steers the nosewheel.
Yaw rate comes from forward speed and wheel angle, capped by available grip, so
taxi turns are tight, high-speed turns are gentle, and the aircraft keeps its
momentum through a turn instead of snapping onto a new heading. Rotational
dynamics driven by the aero moments, and spring/damper landing gear, are the
natural next steps.

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
