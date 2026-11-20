using Godot;

/**
 * Terrain renderer. Port of js/terrain.js onto a real 3D mesh.
 *
 * The ground is a single square grid of quads that follows the aircraft,
 * snapped to world coordinates so the mesh never swims. Cells are flat-shaded
 * (one colour per cell, baked into the vertices) and hazy at distance. Water
 * cells are drawn from a flat, rippling plane instead of the seabed.
 */
public class TerrainMesh
{
    private const int M = 2 * Config.GRID_RADIUS + 1;  // grid points per axis
    private const int CELLS = M - 1;                   // quads per axis
    private const float DITHER_WAVELENGTH = 120f;

    private readonly MeshInstance3D _node;
    private readonly ArrayMesh _mesh = new();
    private readonly StandardMaterial3D _material;

    // Worst case: every cell subdivided 2×2 (near water) = 4 quads of 6
    // vertices.
    private readonly Vector3[] _vertices = new Vector3[CELLS * CELLS * 24];
    private readonly Color[] _colors = new Color[CELLS * CELLS * 24];
    private readonly float[] _ground = new float[M * M];
    private readonly float[] _height = new float[M * M];
    private readonly float[] _cellGround = new float[CELLS * CELLS]; // avg raw ground per cell

    private static readonly Vector3[] QuadScratch = new Vector3[4];

    public TerrainMesh(Node3D parent)
    {
        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        _node = new MeshInstance3D { Mesh = _mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _node.MaterialOverride = _material;
        parent.AddChild(_node);
    }

    /**
     * Smooth value noise anchored to world coordinates — cheap per-cell
     * break-up so large flat areas are not dead flat.
     */
    private static float Dither(float x, float z)
    {
        float fx = x / DITHER_WAVELENGTH;
        float fz = z / DITHER_WAVELENGTH;
        int ix = Mathf.FloorToInt(fx);
        int iz = Mathf.FloorToInt(fz);
        float tx = fx - ix;
        float tz = fz - iz;
        float sx = tx * tx * (3 - 2 * tx);
        float sz = tz * tz * (3 - 2 * tz);

        float a = (float)WorldSim.Hash2(ix, iz);
        float b = (float)WorldSim.Hash2(ix + 1, iz);
        float c = (float)WorldSim.Hash2(ix, iz + 1);
        float d = (float)WorldSim.Hash2(ix + 1, iz + 1);
        float top = a + (b - a) * sx;
        float bottom = c + (d - c) * sx;
        return top + (bottom - top) * sz;
    }

    /** Terrain cell size for a given camera altitude. */
    public static float CellSizeFor(float altitude)
    {
        return QMath.Clamp(altitude * 0.5f + 45f, Config.CELL_MIN, Config.CELL_MAX);
    }

    /** Draw distance for a given camera altitude. */
    public static float DrawDistanceFor(float altitude)
    {
        return CellSizeFor(altitude) * Config.GRID_RADIUS * 1.02f;
    }

    /** Haze blend factor, 0 = clear, 1 = fully lost in the murk. */
    public static float FogFactor(float distance, float drawDistance)
    {
        float t = QMath.Clamp(
            (distance - drawDistance * Config.FOG_START) / (drawDistance * (1 - Config.FOG_START)),
            0f, 1f);
        return Mathf.Pow(t, Config.FOG_CURVE);
    }

    /** Rebuild the whole mesh around the eye for this frame. */
    public void Draw(Vector3 eye, float altitude, float time)
    {
        float cell = 110f; // the grid is locked to a fixed cell size
        float originX = Mathf.Floor(eye.X / cell) * cell - Config.GRID_RADIUS * cell;
        float originZ = Mathf.Floor(eye.Z / cell) * cell - Config.GRID_RADIUS * cell;

        for (int i = 0; i < M; i++)
        {
            float wx = originX + i * cell;
            for (int j = 0; j < M; j++)
            {
                float wz = originZ + j * cell;
                int idx = i * M + j;
                float ground = WorldSim.TerrainHeight(wx, wz);
                _ground[idx] = ground;
                // Water cells render at the flat sea surface instead of
                // following the seabed.
                _height[idx] = ground < Config.SEA_LEVEL ? WorldSim.WaterHeight(wx, wz, time) : ground;
            }
        }

        int v = 0;

        for (int i = 0; i < CELLS; i++)
        {
            float cellX = originX + i * cell;
            for (int j = 0; j < CELLS; j++)
            {
                float cellZ = originZ + j * cell;

                int a = i * M + j;
                int b = a + M;
                int c = b + 1;
                int d = a + 1;

                // A cell whose ground is entirely below sea level is water.
                if (_ground[a] <= Config.SEA_LEVEL && _ground[b] <= Config.SEA_LEVEL &&
                    _ground[c] <= Config.SEA_LEVEL && _ground[d] <= Config.SEA_LEVEL)
                {
                    // Near the camera the cell is split in two so the swell
                    // has some shape; far away one quad is enough.
                    float midX = cellX + cell * 0.5f;
                    float midZ = cellZ + cell * 0.5f;
                    float dist = Mathf.Sqrt((midX - eye.X) * (midX - eye.X) + (midZ - eye.Z) * (midZ - eye.Z));
                    int segments = dist < 1600 ? 2 : 1;
                    float step = cell / segments;

                    for (int si = 0; si < segments; si++)
                    {
                        for (int sj = 0; sj < segments; sj++)
                        {
                            float wx = cellX + si * step;
                            float wz = cellZ + sj * step;
                            v = EmitWaterQuad(v, wx, wz, step, time, eye);
                        }
                    }
                    continue;
                }

                float ha = _height[a], hb = _height[b], hc = _height[c], hd = _height[d];

                // Flat normal of the cell, from two of its edge vectors.
                Vector3 n = new(-(hb - ha) * cell, cell * cell, -(hd - ha) * cell);
                n /= n.Length();
                float lambert = n.Dot(Config.SUN_DIR);
                float shade = QMath.Clamp(0.64f + 0.50f * (lambert * 0.5f + 0.5f), 0.56f, 1.16f);

                float avgGround = (_ground[a] + _ground[b] + _ground[c] + _ground[d]) * 0.25f;
                Color colour = WorldSim.SurfaceColor(avgGround);

                // Cheap per-cell break-up sampled from the cell's world
                // position, so it stays put while the grid slides underneath.
                float noise = 0.92f + Dither(cellX + cell * 0.5f, cellZ + cell * 0.5f) * 0.16f;
                float light = noise * shade;
                colour = new Color(
                    Mathf.Clamp(colour.R * light, 0f, 1f),
                    Mathf.Clamp(colour.G * light, 0f, 1f),
                    Mathf.Clamp(colour.B * light, 0f, 1f));

                QuadScratch[0] = new Vector3(cellX, ha, cellZ);
                QuadScratch[1] = new Vector3(cellX + cell, hb, cellZ);
                QuadScratch[2] = new Vector3(cellX + cell, hc, cellZ + cell);
                QuadScratch[3] = new Vector3(cellX, hd, cellZ + cell);

                // Two triangles: a, b, c and a, c, d.
                for (int t = 0; t < 6; t++)
                {
                    int corner = t == 0 ? 0 : t == 1 ? 1 : t == 2 ? 2 : t == 3 ? 0 : t == 4 ? 2 : 3;
                    _vertices[v] = QuadScratch[corner];
                    _colors[v] = colour;
                    v++;
                }
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _vertices;
        arrays[(int)Mesh.ArrayType.Color] = _colors;

        _mesh.ClearSurfaces();
        _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }

    private int EmitWaterQuad(int v, float wx, float wz, float step, float time, Vector3 eye)
    {
        float wx1 = wx + step;
        float wz1 = wz + step;

        float ha = WorldSim.WaterHeight(wx, wz, time);
        float hb = WorldSim.WaterHeight(wx1, wz, time);
        float hc = WorldSim.WaterHeight(wx1, wz1, time);
        float hd = WorldSim.WaterHeight(wx, wz1, time);

        // Facet normal from the same edge vectors the land uses.
        Vector3 n = new(-(hb - ha) * step, step * step, -(hd - ha) * step);
        n /= n.Length();
        float lambert = n.Dot(Config.SUN_DIR);
        float shade = QMath.Clamp(0.55f + 0.90f * (lambert * 0.5f + 0.5f), 0.52f, 1.24f);

        // Crests catch a lighter blue; troughs stay deep.
        float average = (ha + hb + hc + hd) * 0.25f;
        float crest = QMath.Clamp((average - Config.SEA_LEVEL) / 0.8f + 0.5f, 0f, 1f);
        Vector3 rgb = Config.WATER + (Config.WATER_CREST - Config.WATER) * crest;
        Color colour = new(
            Mathf.Clamp(rgb.X * shade / 255f, 0f, 1f),
            Mathf.Clamp(rgb.Y * shade / 255f, 0f, 1f),
            Mathf.Clamp(rgb.Z * shade / 255f, 0f, 1f));

        Vector3 p0 = new(wx, ha, wz);
        Vector3 p1 = new(wx1, hb, wz);
        Vector3 p2 = new(wx1, hc, wz1);
        Vector3 p3 = new(wx, hd, wz1);

        _vertices[v] = p0; _colors[v] = colour; v++;
        _vertices[v] = p1; _colors[v] = colour; v++;
        _vertices[v] = p2; _colors[v] = colour; v++;
        _vertices[v] = p0; _colors[v] = colour; v++;
        _vertices[v] = p2; _colors[v] = colour; v++;
        _vertices[v] = p3; _colors[v] = colour; v++;
        return v;
    }
}
