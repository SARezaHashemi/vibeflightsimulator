using Godot;

/**
 * Entry point: wires the simulation, camera and renderers together and runs
 * the frame loop. Port of js/main.js.
 */
public partial class Main : Node3D
{
    private Aircraft _aircraft;
    private InputRig _input;
    private TerrainMesh _terrain;
    private SceneryGen _scenery;
    private CityRender _city;
    private TreeRender _trees;
    private CloudRender _clouds;
    private AircraftModel _model;
    private Hud _hud;

    private Camera3D _camera;
    private bool _cockpit;

    private const float CHASE_GROUND_CLEARANCE = 2.5f;

    public override void _Ready()
    {
        // Sky, haze fog and sun.
        var skyMaterial = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0x08 / 255f, 0x21 / 255f, 0x3f / 255f),
            SkyHorizonColor = new Color(0x79 / 255f, 0xa9 / 255f, 0xce / 255f),
            GroundHorizonColor = Config.HAZE,
            GroundBottomColor = new Color(0.55f, 0.62f, 0.68f),
            SunAngleMax = 30f,
        };
        var sky = new Sky { SkyMaterial = skyMaterial };

        var environment = new Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            // Distance haze that blends everything into the horizon colour,
            // like the original renderer's fog factor.
            FogEnabled = true,
            FogLightColor = Config.HAZE,
            FogDensity = 0.00045f,
            FogSkyAffect = 0f,
        };
        AddChild(new WorldEnvironment { Environment = environment });

        _camera = new Camera3D
        {
            Fov = Config.FOV_DEG,
            Near = 0.1f,
            Far = 40000f,
            Current = true,
        };
        AddChild(_camera);

        _aircraft = new Aircraft();
        _input = new InputRig();

        _terrain = new TerrainMesh(this);
        _scenery = new SceneryGen(this);
        _city = new CityRender(this);
        _trees = new TreeRender(this);
        _clouds = new CloudRender(this);
        _model = new AircraftModel(this);

        var hudLayer = new CanvasLayer();
        AddChild(hudLayer);
        _hud = new Hud(_aircraft);
        hudLayer.AddChild(_hud);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _input.Update();

        if (_input.ResetRequested) _aircraft.Reset();
        if (_input.GearRequested) _aircraft.ToggleGear();
        if (_input.ViewRequested) _cockpit = !_cockpit;

        // No-op once crashed.
        _aircraft.Update(dt, _input.Axes);

        // Optional headless smoke test: --sim-debug prints one line per
        // second of simulated flight.
        if (_simDebug && SimDebugTick())
            GD.Print($"t={_aircraft.Elapsed:F0}s state={_aircraft.state} " +
                     $"speed={_aircraft.Speed * 3.6f:F0}km/h altAGL={_aircraft.AltitudeAgl:F0}m " +
                     $"vs={_aircraft.VerticalSpeed:F1} aoa={_aircraft.AngleOfAttack * 180 / Mathf.Pi:F1}° " +
                     $"g={_aircraft.GLoad:F2} thr={_aircraft.Throttle:F2} reason={_aircraft.CrashReason}");
    }

    private bool _simDebug = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--sim-debug") >= 0;

    private int _debugSecond;

    private bool SimDebugTick()
    {
        int second = (int)_aircraft.Elapsed;
        if (second == _debugSecond) return false;
        _debugSecond = second;
        return true;
    }

    public override void _Process(double delta)
    {
        float time = Time.GetTicksMsec() * 0.001f;
        Vector3 eye = UpdateCamera();

        _terrain.Draw(eye, _aircraft.Position.Y, time);
        _scenery.Update(eye, TerrainMesh.DrawDistanceFor(_aircraft.Position.Y));
        _city.Update(eye, Config.CITY_MAX_DISTANCE);
        _trees.Update(eye);
        _clouds.Update(eye);

        _model.BodyNode.GlobalTransform = new Transform3D(new Basis(_aircraft.Orientation), _aircraft.Position);
        _model.UpdateGear(_aircraft.GearPosition);
        _model.HideGearWhenUp(_aircraft.GearPosition);

        _hud.QueueRedraw();
    }

    /**
     * Point the camera at the aircraft. Two views: `chase` sits behind and
     * above the aircraft and looks at it; `cockpit` puts the eye inside the
     * canopy and looks down the nose. Port of js/camera.js.
     */
    private Vector3 UpdateCamera()
    {
        Vector3 p = _aircraft.Position;
        Vector3 f = _aircraft.Forward;
        Vector3 up = _aircraft.Up;
        Vector3 right = _aircraft.Right;

        Vector3 eye;
        Vector3 camForward;
        Vector3 camUp;
        Vector3 camRight;

        if (_cockpit)
        {
            camForward = f;
            camUp = up;
            camRight = right;
            eye = p + up * Config.EYE_HEIGHT + f * Config.EYE_FORWARD;
        }
        else
        {
            // The reference "up" is mostly the aircraft's own roof with a dash
            // of world up, so the view banks with the aircraft but the
            // horizon does not spin wildly in a steep turn.
            Vector3 upRef = new Vector3(
                up.X * 0.65f,
                up.Y * 0.65f + 0.35f,
                up.Z * 0.65f).Normalized();

            eye = p - f * Config.CHASE_BACK + upRef * Config.CHASE_LIFT;

            // Keep the chase eye out of the hillside.
            float floor = WorldSim.SurfaceHeight(eye.X, eye.Z) + CHASE_GROUND_CLEARANCE;
            if (eye.Y < floor) eye = new Vector3(eye.X, floor, eye.Z);

            Vector3 aim = p + f * Config.CHASE_AIM;
            camForward = (aim - eye).Normalized();

            // A basis built from the view direction keeps the horizon level.
            Vector3 r = camForward.Cross(upRef);
            if (r.Length() < 0.2f) r = camForward.Cross(Vector3.Up);
            camRight = r.Normalized();
            camUp = camRight.Cross(camForward);
        }

        // Godot cameras look along -Z of their basis.
        var basis = new Basis(camRight, camUp, -camForward);
        _camera.GlobalTransform = new Transform3D(basis, eye);
        return eye;
    }
}
