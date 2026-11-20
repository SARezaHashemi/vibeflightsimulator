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
        // Sky: deep blue zenith easing into a warm, light horizon.
        var skyMaterial = new ProceduralSkyMaterial
        {
            SkyTopColor = Config.SKY_TOP,
            SkyHorizonColor = Config.SKY_HORIZON,
            GroundHorizonColor = Config.SKY_HORIZON,
            GroundBottomColor = Config.GROUND_BOTTOM,
            SunAngleMax = Config.SUN_DISC_ANGLE,
            SunCurve = Config.SUN_DISC_CURVE,
        };
        var sky = new Sky { SkyMaterial = skyMaterial };

        var environment = new Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,

            // Lighting: one warm directional sun plus a cool blue ambient
            // fill, so lit faces read warm and shade falls cool.
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Config.AMBIENT_COLOR,
            AmbientLightEnergy = Config.AMBIENT_ENERGY,
            AmbientLightSkyContribution = 0f,

            // Distance haze for depth only: starts far out, tinted toward the
            // horizon sky rather than washing the world white.
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogLightColor = Config.FOG_COLOR,
            FogDepthBegin = Config.FOG_BEGIN,
            FogDepthEnd = Config.FOG_END,
            FogDepthCurve = Config.FOG_DEPTH_CURVE,
            FogSkyAffect = Config.FOG_SKY_AFFECT,

            // Mild filmic tone mapping plus a touch of contrast/saturation.
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapWhite = Config.TONEMAP_WHITE,
            AdjustmentEnabled = true,
            AdjustmentSaturation = Config.ADJUSTMENT_SATURATION,
            AdjustmentContrast = Config.ADJUSTMENT_CONTRAST,
        };
        AddChild(new WorldEnvironment { Environment = environment });

        // The sun itself, matching the direction the shading assumes
        // (Config.SUN_DIR). Soft shadows out to the shadow distance.
        var sun = new DirectionalLight3D
        {
            LightColor = Config.SUN_COLOR,
            LightEnergy = Config.SUN_ENERGY,
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            DirectionalShadowMaxDistance = Config.SUN_SHADOW_MAX_DISTANCE,
            ShadowNormalBias = 2f,
        };
        AddChild(sun);
        sun.LookAt(-Config.SUN_DIR);  // shine from the sun into the scene

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

        _lightTest = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--lighttest") >= 0;
        if (_lightTest) BuildLightTestRig();

        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shot="))
                _shotPath = arg["--shot=".Length..];
            if (arg == "--look=sun")
                _lookSun = true;
        }

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

    // Debug: --shot=/abs/path.png saves one screenshot then quits.
    private string _shotPath;
    private int _shotFrame = 12;
    private bool _lookSun;
    private bool _lightTest;
    private Vector3 _rigCentre;

    private void BuildLightTestRig()
    {
        // One grey box on a grey slab, photographed from the north-east.
        // The sun sits north-west-up (Config.SUN_DIR), so the box's north
        // face must render brighter than its east face, and the shadow must
        // fall to the south-west. Anything else is a reversed light.
        Vector3 rig = new(
            _aircraft.Position.X,
            WorldSim.SurfaceHeight(_aircraft.Position.X, _aircraft.Position.Z) + 0.2f,
            _aircraft.Position.Z);
        _rigCentre = rig;

        var mb = new MeshBuilder();
        Color grey = new(0.5f, 0.5f, 0.5f);

        Vector3 Q(float x, float y, float z) => rig + new Vector3(x, y, z);

        // slab
        mb.AddQuad(new[] { Q(-100, 0, -100), Q(-100, 0, 100), Q(100, 0, 100), Q(100, 0, -100) }, grey);
        // box, half extents 2 × 12 × 2, base on the slab, every face wound
        // so its normal points outward
        float hx = 2, hy = 12, hz = 2;
        mb.AddQuad(new[] { Q(-hx, hy, -hz), Q(-hx, hy, hz), Q(hx, hy, hz), Q(hx, hy, -hz) }, grey); // top
        mb.AddQuad(new[] { Q(hx, 0, -hz), Q(-hx, 0, -hz), Q(-hx, hy, -hz), Q(hx, hy, -hz) }, grey); // north
        mb.AddQuad(new[] { Q(hx, 0, hz), Q(hx, 0, -hz), Q(hx, hy, -hz), Q(hx, hy, hz) }, grey);      // east
        mb.AddQuad(new[] { Q(-hx, 0, hz), Q(hx, 0, hz), Q(hx, hy, hz), Q(-hx, hy, hz) }, grey);      // south
        mb.AddQuad(new[] { Q(-hx, 0, -hz), Q(-hx, 0, hz), Q(-hx, hy, hz), Q(-hx, hy, -hz) }, grey);  // west

        var node = new MeshInstance3D { Mesh = mb.Build() };
        AddChild(node);
    }

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

        if (_lookSun)
        {
            // Debug view: face the sun's azimuth with the horizon in frame.
            Vector3 f = new Vector3(-0.55f, 0.20f, -0.69f).Normalized();
            Vector3 r = new Vector3(-f.Z, 0, f.X).Normalized();
            Vector3 u = r.Cross(f).Normalized();
            eye = _aircraft.Position + new Vector3(0, 2, 0);
            _camera.GlobalTransform = new Transform3D(new Basis(r, u, -f), eye);
        }

        if (_lightTest)
        {
            // Debug view: the rig from the north-east, close up.
            eye = _rigCentre + new Vector3(14, 18, -16);
            Vector3 centre = _rigCentre + new Vector3(0, 5, 0);
            Vector3 f = (centre - eye).Normalized();
            Vector3 r = new Vector3(-f.Z, 0, f.X).Normalized();
            Vector3 u = r.Cross(f).Normalized();
            _camera.GlobalTransform = new Transform3D(new Basis(r, u, -f), eye);
        }

        _terrain.Draw(eye, _aircraft.Position.Y, time);
        _scenery.Update(eye, TerrainMesh.DrawDistanceFor(_aircraft.Position.Y));
        _city.Update(eye, Config.CITY_MAX_DISTANCE);
        _trees.Update(eye);
        _clouds.Update(eye);

        _model.BodyNode.GlobalTransform = new Transform3D(new Basis(_aircraft.Orientation), _aircraft.Position);
        _model.UpdateGear(_aircraft.GearPosition);
        _model.HideGearWhenUp(_aircraft.GearPosition);

        _hud.QueueRedraw();

        if (_shotPath != null && --_shotFrame <= 0)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);
            GD.Print($"shot saved: {_shotPath}");
            GetTree().Quit();
        }
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
