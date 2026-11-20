using System.Collections.Generic;
using Godot;

/**
 * Trees, scattered deterministically over the terrain. Port of the placement
 * and collision halves of js/trees.js (rendering lives in TreeRender.cs).
 */
public static class TreeGen
{
    /** Silhouette profiles: [height fraction, half-width fraction]. */
    public static readonly (float y, float w)[] SPRUCE =
    {
        (0.18f, 0.34f), (0.31f, 0.27f), (0.28f, 0.32f),
        (0.45f, 0.22f), (0.42f, 0.27f),
        (0.61f, 0.16f), (0.58f, 0.21f),
        (0.77f, 0.10f), (0.74f, 0.14f),
        (0.91f, 0.04f), (1.00f, 0.00f),
    };

    public static readonly (float y, float w)[] PINE =
    {
        (0.34f, 0.24f), (0.48f, 0.18f), (0.46f, 0.22f),
        (0.62f, 0.15f), (0.60f, 0.19f),
        (0.76f, 0.11f), (0.74f, 0.14f),
        (0.88f, 0.07f), (0.86f, 0.10f),
        (1.00f, 0.00f),
    };

    public static readonly (float y, float w)[] OAK =
    {
        (0.30f, 0.10f), (0.34f, 0.21f), (0.43f, 0.30f), (0.55f, 0.35f),
        (0.68f, 0.33f), (0.80f, 0.26f), (0.91f, 0.15f), (1.00f, 0.00f),
    };

    public static readonly (float y, float w)[] POPLAR =
    {
        (0.22f, 0.13f), (0.30f, 0.23f), (0.42f, 0.29f), (0.57f, 0.30f),
        (0.71f, 0.26f), (0.83f, 0.19f), (0.92f, 0.11f), (1.00f, 0.00f),
    };

    public static readonly (float y, float w)[] BIRCH =
    {
        (0.36f, 0.06f), (0.43f, 0.14f), (0.52f, 0.20f), (0.63f, 0.21f),
        (0.74f, 0.18f), (0.85f, 0.12f), (0.93f, 0.06f), (1.00f, 0.00f),
    };

    /** Conifer tiers as closed frustums: [bottom y, bottom r, top y, top r]. */
    public static readonly (float y0, float r0, float y1, float r1)[] SPRUCE_TIERS =
    {
        (0.18f, 0.34f, 0.31f, 0.26f),
        (0.28f, 0.32f, 0.45f, 0.21f),
        (0.42f, 0.27f, 0.61f, 0.15f),
        (0.58f, 0.21f, 0.77f, 0.10f),
        (0.74f, 0.14f, 0.91f, 0.04f),
        (0.91f, 0.04f, 1.00f, 0.00f),
    };

    public static readonly (float y0, float r0, float y1, float r1)[] PINE_TIERS =
    {
        (0.34f, 0.24f, 0.48f, 0.17f),
        (0.46f, 0.22f, 0.62f, 0.14f),
        (0.60f, 0.19f, 0.76f, 0.10f),
        (0.74f, 0.14f, 0.88f, 0.06f),
        (0.86f, 0.10f, 1.00f, 0.00f),
    };

    public class Species
    {
        public (float y, float w)[] Profile;
        public (float y0, float r0, float y1, float r1)[] Tiers; // null for broadleaves
        public float CanopyBase;
        public float HeightMin, HeightMax;
        public float WidthMin, WidthMax;
        public Vector3 Tint;
        public float Crown;
    }

    public static readonly Species[] KINDS =
    {
        new() { Profile = SPRUCE, Tiers = SPRUCE_TIERS, CanopyBase = 0.18f,
                HeightMin = 0.85f, HeightMax = 1.30f, WidthMin = 0.80f, WidthMax = 1.00f,
                Tint = new Vector3(46, 108, 56) },
        new() { Profile = PINE, Tiers = PINE_TIERS, CanopyBase = 0.34f,
                HeightMin = 0.95f, HeightMax = 1.40f, WidthMin = 0.62f, WidthMax = 0.80f,
                Tint = new Vector3(54, 116, 46) },
        new() { Profile = OAK, Tiers = null, CanopyBase = 0.30f,
                HeightMin = 0.62f, HeightMax = 1.00f, WidthMin = 0.95f, WidthMax = 1.22f,
                Tint = new Vector3(88, 140, 46) },
        new() { Profile = POPLAR, Tiers = null, CanopyBase = 0.22f,
                HeightMin = 0.90f, HeightMax = 1.28f, WidthMin = 0.55f, WidthMax = 0.72f,
                Tint = new Vector3(78, 148, 54) },
        new() { Profile = BIRCH, Tiers = null, CanopyBase = 0.36f,
                HeightMin = 0.52f, HeightMax = 0.84f, WidthMin = 0.60f, WidthMax = 0.80f,
                Tint = new Vector3(120, 160, 60) },
    };

    static TreeGen()
    {
        foreach (Species kind in KINDS)
        {
            float crown = 0;
            foreach ((float _, float w) in kind.Profile) crown = Mathf.Max(crown, w);
            kind.Crown = crown;
        }
    }

    public class Tree
    {
        public float X, Z, Ground;
        public int Kind;
        public float Height, Width;
        public float TrunkWidth;
        public float Shade;
        public float Spin;
        public float CanopyBase;
        public Vector3 Tint;
        public float Radius; // cylinder used by the swept strike test
    }

    /** Species choice, weighted towards conifers and round-crowned broadleaves. */
    private static int PickKind(double r)
    {
        if (r < 0.30) return 0;   // spruce
        if (r < 0.56) return 2;   // oak
        if (r < 0.78) return 1;   // pine
        if (r < 0.90) return 3;   // poplar
        return 4;                 // birch
    }

    private static readonly List<Tree> Empty = new();

    /**
     * Every tree rooted in cell (gi, gj), in world coordinates.
     * The single source of truth for tree placement: the renderer uses it to
     * draw the woods and the flight model uses it to detect a strike.
     */
    public static List<Tree> CellTrees(int gi, int gj)
    {
        // Coarse woodland mask: copses, hedgerows and open fields.
        double patch = WorldSim.Hash(Mathf.FloorToInt(gi / 6.0), Mathf.FloorToInt(gj / 6.0), 11);
        bool woods = patch > 1 - Config.TREE_PATCH;
        double density = woods ? Config.TREE_WOODS_DENSITY : Config.TREE_SCATTER_DENSITY;
        if (WorldSim.Hash(gi, gj, 1) > density) return Empty;

        int count = woods ? 2 + Mathf.FloorToInt((float)WorldSim.Hash(gi, gj, 2) * 3) : 1;
        var trees = new List<Tree>();

        for (int n = 0; n < count; n++)
        {
            int s = n * 7;
            float x = (float)((gi + 0.12 + 0.76 * WorldSim.Hash(gi, gj, 20 + s)) * Config.CELL_SIZE);
            float z = (float)((gj + 0.12 + 0.76 * WorldSim.Hash(gi, gj, 21 + s)) * Config.CELL_SIZE);

            float ground = WorldSim.TerrainHeight(x, z);
            if (ground < Config.TREE_MIN_GROUND || ground > Config.TREE_MAX_GROUND) continue;

            // Keep every runway and its approach corridors clear.
            if (WorldSim.NearAirport(x, z, Config.TREE_RUNWAY_CLEARANCE, Config.TREE_APPROACH_CLEARANCE)) continue;

            // Cities have their own planters and parks; keep the woods out.
            if (CityGen.NearCity(x, z, Config.CITY_TREE_CLEARANCE)) continue;

            int kind = PickKind(WorldSim.Hash(gi, gj, 22 + s));
            Species spec = KINDS[kind];

            float scale = Config.TREE_MIN_HEIGHT +
                (float)WorldSim.Hash(gi, gj, 23 + s) * (Config.TREE_MAX_HEIGHT - Config.TREE_MIN_HEIGHT);
            float height = scale * (spec.HeightMin + (float)WorldSim.Hash(gi, gj, 27 + s) * (spec.HeightMax - spec.HeightMin));
            float width = spec.WidthMin + (float)WorldSim.Hash(gi, gj, 24 + s) * (spec.WidthMax - spec.WidthMin);

            trees.Add(new Tree
            {
                X = x, Z = z, Ground = ground, Kind = kind,
                Height = height, Width = width,
                TrunkWidth = 0.010f + 0.010f * (float)WorldSim.Hash(gi, gj, 25 + s),
                Shade = 0.82f + 0.36f * (float)WorldSim.Hash(gi, gj, 26 + s),
                Spin = (float)WorldSim.Hash(gi, gj, 28 + s) * Mathf.Pi * 2f,
                CanopyBase = spec.CanopyBase,
                Tint = spec.Tint,
                Radius = spec.Crown * height * width,
            });
        }

        return trees;
    }

    /**
     * The first tree struck by moving from `from` to `to`, or null.
     * The whole frame's movement is tested, so a fast aircraft cannot skip
     * over a narrow crown between frames.
     */
    public static Tree TreeStrike(Vector3 from, Vector3 to)
    {
        // Trees overlap cell borders, so widen the cell range by the largest
        // crown.
        float pad = Config.TREE_MAX_HEIGHT + Config.TREE_COLLISION_MARGIN;
        int gi0 = Mathf.FloorToInt((Mathf.Min(from.X, to.X) - pad) / Config.CELL_SIZE);
        int gi1 = Mathf.FloorToInt((Mathf.Max(from.X, to.X) + pad) / Config.CELL_SIZE);
        int gj0 = Mathf.FloorToInt((Mathf.Min(from.Z, to.Z) - pad) / Config.CELL_SIZE);
        int gj1 = Mathf.FloorToInt((Mathf.Max(from.Z, to.Z) + pad) / Config.CELL_SIZE);

        float dx = to.X - from.X;
        float dz = to.Z - from.Z;
        float travelSq = dx * dx + dz * dz;

        for (int gi = gi0; gi <= gi1; gi++)
        {
            for (int gj = gj0; gj <= gj1; gj++)
            {
                foreach (Tree tree in CellTrees(gi, gj))
                {
                    float radius = tree.Radius + Config.TREE_COLLISION_MARGIN;
                    float fx = from.X - tree.X;
                    float fz = from.Z - tree.Z;

                    float t0, t1;
                    if (travelSq < 1e-9f)
                    {
                        if (fx * fx + fz * fz > radius * radius) continue;
                        t0 = 0; t1 = 0;
                    }
                    else
                    {
                        float b = 2 * (fx * dx + fz * dz);
                        float c = fx * fx + fz * fz - radius * radius;
                        float disc = b * b - 4 * travelSq * c;
                        if (disc < 0) continue;
                        float root = Mathf.Sqrt(disc);
                        t0 = Mathf.Max(0, (-b - root) / (2 * travelSq));
                        t1 = Mathf.Min(1, (-b + root) / (2 * travelSq));
                        if (t0 > t1) continue;
                    }

                    float ya = from.Y + (to.Y - from.Y) * t0;
                    float yb = from.Y + (to.Y - from.Y) * t1;
                    float top = tree.Ground + tree.Height;
                    if (Mathf.Max(ya, yb) >= tree.Ground && Mathf.Min(ya, yb) <= top)
                        return tree;
                }
            }
        }

        return null;
    }
}
