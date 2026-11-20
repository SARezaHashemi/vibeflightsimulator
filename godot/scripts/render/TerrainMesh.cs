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
    private const float PATCH_WAVELENGTH = 520f;

    private readonly MeshInstance3D _node;
    private readonly ArrayMesh _mesh = new();
    private readonly StandardMaterial3D _material;

    // Worst case: every cell subdivided 2×2 (near water) = 4 quads of 6
    // vertices.
    private readonly Vector3[] _vertices = new Vector3[CELLS * CELLS * 24];
    private readonly Vector3[] _normals = new Vector3[CELLS * CELLS * 24];
    private readonly Color[] _colors = new Color[CELLS * CELLS * 24];
    private readonly float[] _ground = new float[M * M];
    private readonly float[] _height = new float[M * M];

    private static readonly Vector3[] QuadScratch = new Vector3[4];

    public TerrainMesh(Node3D parent)
    {
        _material = MeshBuilder.ShadedMaterial();

        _node = new MeshInstance3D { Mesh = _mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        _node.MaterialOverride = _material;
        parent.AddChild(_node);
    }

    /**
     * Smooth value noise anchored to world coordinates — cheap per-cell
     * break-up so large flat areas are not dead flat.
     */
    private static float Dither(float x, float z, float wavelength)
    {
        float fx = x / wavelength;
        float fz = z / wavelength;
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

    /** Rebuild the whole mesh around the eye for this frame. */
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
                    float seabed = (_ground[a] + _ground[b] + _ground[c] + _ground[d]) * 0.25f;

                    for (int si = 0; si < segments; si++)
                    {
                        for (int sj = 0; sj < segments; sj++)
                        {
                            float wx = cellX + si * step;
                            float wz = cellZ + sj * step;
                            v = EmitWaterQuad(v, wx, wz, step, time, seabed);
                        }
                    }
                    continue;
                }

                float ha = _height[a], hb = _height[b], hc = _height[c], hd = _height[d];

                // Flat normal of the cell, from two of its edge vectors.
                Vector3 n = new(-(hb - ha) * cell, cell * cell, -(hd - ha) * cell);
                n /= n.Length();

                float avgGround = (_ground[a] + _ground[b] + _ground[c] + _ground[d]) * 0.25f;
                Color colour = WorldSim.SurfaceColor(avgGround);

                // Large-scale meadow variation, so the grass country is a
                // mosaic of lush pasture and yellow-green dry fields instead
                // of one flat green. Fades out towards the rock and snow
                // lines.
                if (avgGround > 2.5f && avgGround < 300f)
                {
                    float grassiness = QMath.Clamp(
                        Mathf.Min((avgGround - 2.5f) / 25f, (300f - avgGround) / 80f), 0f, 1f);
                    float patch = Dither(cellX + cell * 0.5f, cellZ + cell * 0.5f, PATCH_WAVELENGTH);
                    colour = colour.Lerp(
                        Config.PATCH_LUSH.Lerp(Config.PATCH_DRY, patch), grassiness * 0.55f);
                }

                // Steep faces show bare rock, grey under the snow line and
                // blue-grey through the snowpack.
                float slope = 1f - n.Y;
                if (slope > 0.10f)
                {
                    float rockiness = QMath.Clamp((slope - 0.10f) / 0.20f, 0f, 1f) * 0.85f;
                    Color rock = avgGround > 400 ? Config.SLOPE_ROCK_SNOW : Config.SLOPE_ROCK;
                    colour = colour.Lerp(rock, rockiness);
                }

                // Cheap per-cell break-up sampled from the cell's world
                // position, so it stays put while the grid slides underneath.
                float noise = 0.92f + Dither(cellX + cell * 0.5f, cellZ + cell * 0.5f, DITHER_WAVELENGTH) * 0.16f;
                colour = new Color(
                    Mathf.Clamp(colour.R * noise, 0f, 1f),
                    Mathf.Clamp(colour.G * noise, 0f, 1f),
                    Mathf.Clamp(colour.B * noise, 0f, 1f));

                QuadScratch[0] = new Vector3(cellX, ha, cellZ);
                QuadScratch[1] = new Vector3(cellX + cell, hb, cellZ);
                QuadScratch[2] = new Vector3(cellX + cell, hc, cellZ + cell);
                QuadScratch[3] = new Vector3(cellX, hd, cellZ + cell);

                // Two triangles, wound counter-clockwise seen from above so
                // the ground is a front face with its +Y normal: a, c, b and
                // a, d, c.
                for (int t = 0; t < 6; t++)
                {
                    int corner = t == 0 ? 0 : t == 1 ? 2 : t == 2 ? 1 : t == 3 ? 0 : t == 4 ? 3 : 2;
                    _vertices[v] = QuadScratch[corner];
                    _normals[v] = n;
                    _colors[v] = colour;
                    v++;
                }
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _vertices;
        arrays[(int)Mesh.ArrayType.Normal] = _normals;
        arrays[(int)Mesh.ArrayType.Color] = _colors;

        _mesh.ClearSurfaces();
        _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }

    private int EmitWaterQuad(int v, float wx, float wz, float step, float time, float seabed)
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

        // Crests catch a lighter blue; troughs stay deep.
        float average = (ha + hb + hc + hd) * 0.25f;
        float crest = QMath.Clamp((average - Config.SEA_LEVEL) / 0.55f + 0.5f, 0f, 1f);
        Color colour = Config.WATER.Lerp(Config.WATER_CREST, crest);

        // Shallows over sand turn lagoon-green: the shallower the seabed, the
        // more the sea floor shows through.
        float depth = Config.SEA_LEVEL - seabed;
        if (depth < 14)
        {
            float clear = QMath.Clamp(1 - depth / 14, 0f, 1f);
            colour = colour.Lerp(Config.WATER_LAGOON, clear * 0.75f);
        }

        Vector3 p0 = new(wx, ha, wz);
        Vector3 p1 = new(wx1, hb, wz);
        Vector3 p2 = new(wx1, hc, wz1);
        Vector3 p3 = new(wx, hd, wz1);

        // Wound counter-clockwise seen from above, matching the land quads.
        _vertices[v] = p0; _normals[v] = n; _colors[v] = colour; v++;
        _vertices[v] = p2; _normals[v] = n; _colors[v] = colour; v++;
        _vertices[v] = p1; _normals[v] = n; _colors[v] = colour; v++;
        _vertices[v] = p0; _normals[v] = n; _colors[v] = colour; v++;
        _vertices[v] = p3; _normals[v] = n; _colors[v] = colour; v++;
        _vertices[v] = p2; _normals[v] = n; _colors[v] = colour; v++;
        return v;
    }
}
