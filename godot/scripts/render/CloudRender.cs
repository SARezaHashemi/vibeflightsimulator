using System.Collections.Generic;
using Godot;

/**
 * Cloud renderer. Port of the drawing half of js/clouds.js: each cloud is a
 * cluster of puffs scattered over a jittered world grid. In Godot the puffs
 * become low-poly domes whose facets carry a vertical gradient and a sunward
 * lambert, keeping the chunky, crystalline look of the original.
 */
public class CloudRender
{
    private const float BASE_FLOOR = 0.45f;

    private static readonly StandardMaterial3D Material = MeshBuilder.ShadedMaterial();

    private readonly Node3D _root;
    private readonly Dictionary<long, ArrayMesh> _clusterMeshes = new();
    private readonly Dictionary<long, MeshInstance3D> _instances = new();

    public CloudRender(Node3D root)
    {
        _root = root;
    }

    /** Low-poly dome: a lat/long sphere with a vertical albedo gradient. */
    private static void AddDome(MeshBuilder mb, Vector3 centre, float radius)
    {
        const int lon = 8, lat = 4;
        Vector3 Point(int i, int j)
        {
            float theta = (i / (float)lon) * Mathf.Pi * 2f;
            float phi = (j / (float)lat) * Mathf.Pi - Mathf.Pi / 2f;
            return centre + new Vector3(
                Mathf.Cos(theta) * Mathf.Cos(phi) * radius,
                Mathf.Sin(phi) * radius * 0.78f,   // slightly oblate
                Mathf.Sin(theta) * Mathf.Cos(phi) * radius);
        }

        for (int i = 0; i < lon; i++)
        {
            for (int j = 0; j < lat; j++)
            {
                Vector3 a = Point(i, j);
                Vector3 b = Point(i + 1, j);
                Vector3 c = Point(i + 1, j + 1);
                Vector3 d = Point(i, j + 1);

                foreach (var tri in new[] { (a, d, c), (a, c, b) })
                {
                    // Pure white where the sun hits, cool blue-grey under the
                    // base: the gradient is albedo, the sun does the shading,
                    // so the puffs separate cleanly from the hazy horizon.
                    // (Wound θ↑φ↑ the normals would point inward, lighting the
                    // clouds from underneath — hence the reversal.)
                    float meanY = (tri.Item1.Y + tri.Item2.Y + tri.Item3.Y) / 3f;
                    float vertical = QMath.Clamp(
                        0.25f + (meanY - (centre.Y - radius)) / (2 * radius) * 0.75f, 0f, 1f);
                    mb.AddTriangle(tri.Item1, tri.Item2, tri.Item3,
                        Config.CLOUD_SHADE.Lerp(Config.CLOUD_LIT, vertical));
                }
            }
        }
    }

    private static ArrayMesh BuildClusterMesh(long key)
    {
        int i = (int)(key >> 21);
        int k = (int)(key & 0x1FFFFF);
        float cx = (float)((i + WorldSim.Hash(i, k, 1)) * Config.CLOUD_CELL);
        float cz = (float)((k + WorldSim.Hash(i, k, 2)) * Config.CLOUD_CELL);
        float baseY = Config.CLOUD_BASE_MIN + (float)WorldSim.Hash(i, k, 3) * (Config.CLOUD_BASE_MAX - Config.CLOUD_BASE_MIN);
        float size = 520 + (float)WorldSim.Hash(i, k, 4) * 720;
        int count = 10 + Mathf.FloorToInt((float)WorldSim.Hash(i, k, 5) * 6);

        var mb = new MeshBuilder();
        for (int n = 0; n < count; n++)
        {
            int s = 10 + n * 17;
            float u = (float)WorldSim.Hash(i, k, s) - 0.5f;
            float v = (float)WorldSim.Hash(i, k, s + 1) - 0.5f;
            float centreness = Mathf.Max(0, 1 - Mathf.Sqrt(u * u + v * v) * 1.7f);
            float r = size * (0.16f + 0.12f * (float)WorldSim.Hash(i, k, s + 2) + 0.09f * centreness);
            float base_ = baseY + ((float)WorldSim.Hash(i, k, s + 3) - 0.5f) * size * 0.03f;

            // Lift the centre so the flat rim lands on the shared cloud base.
            Vector3 centre = new(cx + u * size * 0.90f - cx, base_ + BASE_FLOOR * r, cz + v * size * 0.85f - cz);
            AddDome(mb, centre, r);
        }

        return mb.Build();
    }

    private static long KeyFor(int i, int k) => ((long)i << 21) ^ (uint)k;

    /** Show every cloud cluster within range of the eye, hide the rest. */
    public void Update(Vector3 eye)
    {
        var seen = new HashSet<long>();
        int ci = Mathf.FloorToInt(eye.X / Config.CLOUD_CELL);
        int ck = Mathf.FloorToInt(eye.Z / Config.CLOUD_CELL);

        for (int i = ci - Config.CLOUD_RANGE; i <= ci + Config.CLOUD_RANGE; i++)
        {
            for (int k = ck - Config.CLOUD_RANGE; k <= ck + Config.CLOUD_RANGE; k++)
            {
                if (WorldSim.Hash(i, k, 0) > Config.CLOUD_DENSITY) continue;

                long key = KeyFor(i, k);
                if (!_instances.TryGetValue(key, out MeshInstance3D node))
                {
                    if (!_clusterMeshes.TryGetValue(key, out ArrayMesh mesh))
                    {
                        mesh = BuildClusterMesh(key);
                        _clusterMeshes[key] = mesh;
                    }

                    node = new MeshInstance3D
                    {
                        Mesh = mesh,
                        MaterialOverride = Material,
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    };
                    float cx = (float)((i + WorldSim.Hash(i, k, 1)) * Config.CLOUD_CELL);
                    float cz = (float)((k + WorldSim.Hash(i, k, 2)) * Config.CLOUD_CELL);
                    node.Position = new Vector3(cx, 0, cz);
                    _root.AddChild(node);
                    _instances[key] = node;
                }

                // Cull clusters wholly outside the draw distance.
                float dx = node.Position.X - eye.X, dz = node.Position.Z - eye.Z;
                node.Visible = dx * dx + dz * dz <= Config.CLOUD_MAX_DISTANCE * Config.CLOUD_MAX_DISTANCE;
                seen.Add(key);
            }
        }

        foreach (var pair in _instances)
        {
            if (!seen.Contains(pair.Key)) pair.Value.Visible = false;
        }
    }
}
