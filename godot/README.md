# Mini Flight — Godot Mono port

A port of the browser flight simulator (see the README one level up) to
**Godot 4.7 with C# (.NET)**. The whole simulation is a faithful port of the
JavaScript original; the 2D-canvas software renderer is replaced by Godot's
3D renderer.

## Running it

Requirements: Godot 4.7 (.NET / mono edition) and a .NET SDK (installed via
mise). From the `godot/` directory:

```sh
dotnet build            # compiles the C# assembly
godot-mono-rc           # runs the game (or your own Godot binary)
```

Headless smoke test (prints one telemetry line per simulated second):

```sh
godot-mono-rc --headless -- --sim-debug --quit-after 600
```

## Layout

```
godot/
├── project.godot            engine config, main scene
├── scenes/Main.tscn         one-node scene; everything else is built in code
└── scripts/
    ├── Main.cs              bootstrap, frame loop, cameras
    ├── sim/                 the simulation — a 1:1 port of js/
    │   ├── Config.cs        every tunable constant (js/config.js)
    │   ├── QMath.cs         vector / quaternion helpers (js/math.js)
    │   ├── Body.cs          point-mass physics, velocity Verlet (js/physics.js)
    │   ├── Aero.cs          lift curve, drag polar, side force (js/aero.js)
    │   ├── Propulsion.cs    engine power → propeller thrust (js/propulsion.js)
    │   ├── WorldSim.cs      heightfield, airfield grid, surfaces (js/world.js)
    │   ├── TreeGen.cs       tree placement + swept strike test (js/trees.js)
    │   ├── CityGen.cs       city generation + building strikes (js/city.js)
    │   ├── Aircraft.cs      the flight model state machine (js/aircraft.js)
    │   └── InputRig.cs      keyboard → control axes (js/input.js)
    └── render/              Godot-side presentation
        ├── MeshBuilder.cs   flat-shaded, vertex-coloured mesh helper
        ├── TerrainMesh.cs   dynamic grid mesh, water cells (js/terrain.js)
        ├── SceneryGen.cs    runways, aprons, hangars, towers (js/scenery.js)
        ├── CityRender.cs    street meshes + building MultiMesh (js/city.js)
        ├── TreeRender.cs    one MultiMesh per species (js/trees.js)
        ├── CloudRender.cs   cached low-poly cloud clusters (js/clouds.js)
        ├── AircraftModel.cs the low-poly airframe (js/model.js)
        └── Hud.cs           head-up display (js/hud.js)
```

## How the port maps

- **Simulation** is engine-agnostic C# using Godot value types. Every number
  is the same as the original: the lift curve, the drag polar, the propeller
  power model, the rigid-body rotation via angular momentum kept in world
  axes, the bicycle-model nosewheel, and the landing/crash limits.
- **Rendering** replaces the painter's-algorithm canvas with real 3D:
  - terrain is a single dynamic mesh rebuilt each frame around the eye,
    snapped to world coordinates, flat-shaded with per-face normals; every
    colour is plain albedo from the palette in `Config.cs`
  - water cells render from the rippling sea surface, not the seabed;
  - trees and buildings are `MultiMesh` instances (per-species tree meshes,
    unit-box buildings with per-instance tint);
  - runways, aprons and city streets are cached vertex-coloured meshes;
  - lighting is one warm directional sun with soft shadows out to 2.6 km
    plus a cool blue ambient fill; distance haze starts far out and is
    tinted toward the horizon sky; a filmic tone map and a mild
    contrast/saturation grade finish the frame.
- **Cameras**: chase and cockpit views are ported directly (the chase eye is
  clamped above the terrain, the cockpit eye sits inside the canopy).
- **HUD** is drawn with a `Control._Draw()` override, matching the original
  layout: speed, AoA / g, surface, altitude, heading, throttle lever vs
  spooled engine, gear lamps, runway guidance, stall and crash banners.

## Controls

Same as the original: `W/S` or arrows pitch, `A/D` roll, `Q/E` rudder and
nosewheel steering, `Shift`/`X` throttle up/down (brake on the ground),
`G` gear, `C` camera view, `R` restart.
