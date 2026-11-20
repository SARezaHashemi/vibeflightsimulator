using System.Collections.Generic;
using Godot;

/**
 * Scenery that is not part of the terrain mesh: runways, taxiways, aprons and
 * the buildings that go with every airfield. Port of js/scenery.js +
 * structures.js onto real 3D meshes, cached per airfield.
 */
public class SceneryGen
{
    private const float LIFT = 0.35f;

    private static readonly Vector3 ASPHALT = new(56, 58, 62);
    private static readonly Vector3 CONCRETE = new(104, 104, 100);
    private static readonly Vector3 MARKING = new(226, 226, 216);
    private static readonly Vector3 HANGAR = new(150, 146, 136);
    private static readonly Vector3 TOWER = new(176, 174, 166);
    private static readonly Vector3 GLASS = new(112, 138, 154);

    private static readonly StandardMaterial3D Material = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private readonly Node3D _root;
    private readonly Dictionary<long, MeshInstance3D> _instances = new();

    public SceneryGen(Node3D root)
    {
        _root = root;
    }

    private static Vector3 Point(WorldSim.Airport airport, float along, float across, float lift = LIFT)
    {
        return new Vector3(
            airport.X + airport.Along.X * along + airport.Across.X * across,
            airport.Elevation + lift,
            airport.Z + airport.Along.Y * along + airport.Across.Y * across);
    }

    /** A low-poly box with its sun shading baked into the vertex colours. */
    private static void AddBox(MeshBuilder mb, float x, float ground, float z,
        float width, float depth, float height, float angle, Vector3 tint, float shade, float foundation)
    {
        float baseY = ground - foundation;
        float topY = ground + height;

        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);
        float hw = width * 0.5f;
        float hd = depth * 0.5f;

        // Local width axis (cos, sin) and depth axis (-sin, cos) in X/Z.
        float ux = cos * hw, uz = sin * hw;
        float vx = -sin * hd, vz = cos * hd;

        var baseRing = new Vector3[]
        {
            new(x - ux - vx, baseY, z - uz - vz),
            new(x + ux - vx, baseY, z + uz - vz),
            new(x + ux + vx, baseY, z + uz + vz),
            new(x - ux + vx, baseY, z - uz + vz),
        };
        var topRing = new Vector3[4];
        for (int i = 0; i < 4; i++) topRing[i] = new Vector3(baseRing[i].X, topY, baseRing[i].Z);

        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            var wall = new Vector3[] { baseRing[i], topRing[i], topRing[j], baseRing[j] };
            Vector3 n = MeshBuilder.FaceNormal(wall);
            float lambert = Mathf.Max(0, n.Dot(Config.SUN_DIR));
            mb.AddQuad(wall, MeshBuilder.Lit(tint, lambert, 0.68f, 0.52f, shade));
        }

        // Roof, wound upwards.
        mb.AddQuad(new[] { topRing[3], topRing[2], topRing[1], topRing[0] },
            MeshBuilder.Lit(tint, 1, 0.68f, 0.52f, shade));
    }

    private static void BuildRunway(MeshBuilder mb, WorldSim.Airport airport)
    {
        float halfLength = airport.Length / 2;
        float halfWidth = airport.Width / 2;
        var colour = new Color(ASPHALT.X / 255f, ASPHALT.Y / 255f, ASPHALT.Z / 255f);
        var marking = new Color(MARKING.X / 255f, MARKING.Y / 255f, MARKING.Z / 255f);

        void Quad(float along0, float along1, float across0, float across1, Color c, float lift)
        {
            mb.AddQuad(new[]
            {
                Point(airport, along0, across0, lift),
                Point(airport, along0, across1, lift),
                Point(airport, along1, across1, lift),
                Point(airport, along1, across0, lift),
            }, c);
        }

        /* ---- paved surface ---- */
        Quad(-halfLength, halfLength, -halfWidth, halfWidth, colour, LIFT);

        /* ---- edge lines ---- */
        Quad(-halfLength, halfLength, -halfWidth + 2, -halfWidth + 3.4f, marking, LIFT + 0.02f);
        Quad(-halfLength, halfLength, halfWidth - 3.4f, halfWidth - 2, marking, LIFT + 0.02f);

        /* ---- centreline dashes ---- */
        const float dash = 30;
        const float gap = 26;
        float end = halfLength - Mathf.Max(75, halfLength * 0.10f);
        for (float a = -end; a + dash <= end; a += dash + gap)
            Quad(a, a + dash, -0.7f, 0.7f, marking, LIFT + 0.02f);

        /* ---- threshold bars at both ends ---- */
        const int bars = 6;
        float barWidth = Mathf.Min(3, airport.Width / 22);
        float barGap = barWidth * 1.1f;
        float span = bars * barWidth + (bars - 1) * barGap;
        float threshold = Mathf.Min(30, halfLength * 0.05f);
        for (int i = 0; i < bars; i++)
        {
            float c0 = -span / 2 + i * (barWidth + barGap);
            Quad(-halfLength + threshold, -halfLength + threshold * 2.2f, c0, c0 + barWidth, marking, LIFT + 0.02f);
            Quad(halfLength - threshold * 2.2f, halfLength - threshold, c0, c0 + barWidth, marking, LIFT + 0.02f);
        }
    }

    /**
     * Apron, taxiway, hangar and control tower. The side alternates with the
     * airfield's position, so not every field has its buildings on the same
     * hand.
     */
    private static void BuildBuildings(MeshBuilder mb, WorldSim.Airport airport)
    {
        float halfLength = airport.Length / 2;
        float halfWidth = airport.Width / 2;
        float side = Mathf.Sin(airport.X * 0.0013f + airport.Z * 0.0007f) >= 0 ? 1 : -1;

        var asphalt = new Color(ASPHALT.X / 255f, ASPHALT.Y / 255f, ASPHALT.Z / 255f);
        var concrete = new Color(CONCRETE.X / 255f, CONCRETE.Y / 255f, CONCRETE.Z / 255f);

        float taxiAlong = -halfLength * 0.28f;
        float apronNear = halfWidth + 24;
        float apronFar = halfWidth + 150;

        // Taxiway from the runway edge to the apron.
        mb.AddQuad(new[]
        {
            Point(airport, taxiAlong - 16, side > 0 ? halfWidth : -apronNear),
            Point(airport, taxiAlong + 16, side > 0 ? halfWidth : -apronNear),
            Point(airport, taxiAlong + 16, side > 0 ? apronNear : -halfWidth),
            Point(airport, taxiAlong - 16, side > 0 ? apronNear : -halfWidth),
        }, asphalt);

        // Apron slab.
        float along0 = taxiAlong - 130;
        float along1 = taxiAlong + 210;
        float a0 = side > 0 ? apronNear : -apronFar;
        float a1 = side > 0 ? apronFar : -apronNear;
        mb.AddQuad(new[]
        {
            Point(airport, along0, a0),
            Point(airport, along1, a0),
            Point(airport, along1, a1),
            Point(airport, along0, a1),
        }, concrete);

        // Hangar.
        Vector3 hangarCentre = Point(airport, taxiAlong + 80, side * (apronNear + 50));
        AddBox(mb, hangarCentre.X, airport.Elevation + LIFT, hangarCentre.Z,
            Mathf.Min(78, airport.Length * 0.07f), 42, 17,
            airport.Heading + Mathf.Pi * 0.5f, HANGAR, 0.96f,
            Mathf.Min(6, 1.5f + 17 * 0.03f));

        // Control tower: a narrow shaft with a wider glass cab.
        Vector3 towerCentre = Point(airport, taxiAlong - 70, side * (apronNear + 44));
        AddBox(mb, towerCentre.X, airport.Elevation + LIFT, towerCentre.Z,
            9, 9, 30, airport.Heading, TOWER, 0.92f,
            Mathf.Min(6, 1.5f + 30 * 0.03f));
        AddBox(mb, towerCentre.X, airport.Elevation + 30 + LIFT, towerCentre.Z,
            16, 16, 7, airport.Heading, GLASS, 1.02f, 0);
    }

    private MeshInstance3D GetOrCreate(WorldSim.Airport airport)
    {
        long key = airport.Home ? long.MinValue : (long)(airport.X / Config.AIRPORT_SPACING) * 1000003 + (long)(airport.Z / Config.AIRPORT_SPACING);
        if (_instances.TryGetValue(key, out MeshInstance3D existing)) return existing;

        var mb = new MeshBuilder();
        BuildRunway(mb, airport);
        BuildBuildings(mb, airport);

        var node = new MeshInstance3D { Mesh = mb.Build(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        node.MaterialOverride = Material;
        _root.AddChild(node);
        _instances[key] = node;
        return node;
    }

    /** Show every airfield within draw range of the eye, hide the rest. */
    public void Update(Vector3 eye, float drawDistance)
    {
        foreach (var pair in _instances)
            pair.Value.Visible = false;

        foreach (WorldSim.Airport airport in WorldSim.AirportsNear(eye.X, eye.Z, drawDistance))
        {
            float distance = Mathf.Sqrt((airport.X - eye.X) * (airport.X - eye.X) + (airport.Z - eye.Z) * (airport.Z - eye.Z));
            if (distance > drawDistance + airport.Length * 0.6f + 400) continue;

            MeshInstance3D node = GetOrCreate(airport);
            node.Visible = true;
        }
    }
}
