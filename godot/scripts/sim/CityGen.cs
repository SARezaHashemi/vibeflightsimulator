using System.Collections.Generic;
using Godot;

/**
 * Cities: street grids and low-poly buildings scattered over the world.
 * Port of the generation and collision halves of js/city.js (rendering in
 * CityRender.cs).
 */
public static class CityGen
{
    private const int STREET_MIN = 13;
    private const int STREET_MAX = 20;
    private const int AVENUE_EXTRA = 9;
    private const int AVENUE_EVERY = 3;
    private const double PARK_CHANCE = 0.09;
    private const float STREET_LIFT = 0.22f;
    private const float FOUNDATION = 2.5f;
    private const float STRIKE_MARGIN = 2.0f;

    public static readonly Vector3 ASPHALT = new(78, 80, 84);
    public static readonly Vector3 PARK = new(60, 104, 60);

    public static readonly Vector3[] BUILDING_TINTS =
    {
        new(148, 148, 146),  // concrete
        new(126, 132, 136),  // grey stone
        new(158, 150, 134),  // sandstone
        new(112, 122, 130),  // blue-grey glass
        new(138, 126, 114),  // brick
        new(168, 168, 162),  // pale render
        new(104, 112, 120),  // dark steel
    };

    public class City
    {
        public float X, Z, Radius, Angle, Block, Street, Ground;
        public int I, K;
    }

    public class StreetQuad
    {
        public Vector3[] Corners;
        public Vector3 Tint;
    }

    public class Building
    {
        public float X, Z, Ground, Width, Depth, Height, Angle;
        public Vector3 Tint;
        public float Shade;
        public float Foundation;
        public float Radius; // rough plan radius for the swept strike test
    }

    public class Geometry
    {
        public List<StreetQuad> Grounds = new();
        public List<Building> Buildings = new();
    }

    private static readonly Dictionary<long, City> CityCache = new();
    private static readonly Dictionary<long, Geometry> GeometryCache = new();

    /** One deterministic city from a grid cell, or null if the cell is empty. */
    public static City CityCell(int i, int k)
    {
        long key = (long)i * 1000003 + k;
        if (CityCache.TryGetValue(key, out City cached)) return cached;

        City city = null;
        if (WorldSim.Hash(i, k, 201) <= Config.CITY_CHANCE)
        {
            float x = (float)((i + 0.10 + 0.80 * WorldSim.Hash(i, k, 202)) * Config.CITY_SPACING);
            float z = (float)((k + 0.10 + 0.80 * WorldSim.Hash(i, k, 203)) * Config.CITY_SPACING);
            float radius = Config.CITY_MIN_RADIUS +
                (float)WorldSim.Hash(i, k, 204) * (Config.CITY_MAX_RADIUS - Config.CITY_MIN_RADIUS);

            // Give airfield plateaus room, but allow a city to sit alongside.
            bool clear = true;
            foreach (WorldSim.Airport airport in WorldSim.AirportsNear(x, z, radius + 4000))
            {
                if (Mathf.Sqrt((airport.X - x) * (airport.X - x) + (airport.Z - z) * (airport.Z - z)) <
                    radius + airport.FlattenInner + Config.CITY_AIRPORT_GAP)
                {
                    clear = false;
                    break;
                }
            }

            if (clear)
            {
                // Coastal sites are fine, but a city needs mostly dry ground
                // and a gradient its streets can follow.
                int dry = 0, samples = 0;
                float lowest = float.PositiveInfinity, highest = float.NegativeInfinity;
                for (int a = -2; a <= 2; a++)
                {
                    for (int b = -2; b <= 2; b++)
                    {
                        float ground = WorldSim.TerrainHeight(x + a * radius * 0.42f, z + b * radius * 0.42f);
                        samples++;
                        if (ground > Config.SEA_LEVEL + 2)
                        {
                            dry++;
                            lowest = Mathf.Min(lowest, ground);
                            highest = Mathf.Max(highest, ground);
                        }
                    }
                }

                bool gentle = dry >= samples * 0.6 &&
                    highest - lowest < 70 + radius * 0.15f;
                if (gentle)
                {
                    city = new City
                    {
                        X = x, Z = z, Radius = radius,
                        Angle = (float)WorldSim.Hash(i, k, 205) * Mathf.Pi,
                        Block = 125 + (float)WorldSim.Hash(i, k, 206) * 90,
                        Street = STREET_MIN + (float)WorldSim.Hash(i, k, 207) * (STREET_MAX - STREET_MIN),
                        Ground = WorldSim.TerrainHeight(x, z),
                        I = i, K = k,
                    };
                }
            }
        }

        CityCache[key] = city;
        return city;
    }

    /** Cities whose disc comes within `radius` of a point. */
    public static List<City> CitiesNear(float x, float z, float radius)
    {
        float pad = radius + Config.CITY_MAX_RADIUS;
        int i0 = Mathf.FloorToInt((x - pad) / Config.CITY_SPACING) - 1;
        int i1 = Mathf.CeilToInt((x + pad) / Config.CITY_SPACING) + 1;
        int k0 = Mathf.FloorToInt((z - pad) / Config.CITY_SPACING) - 1;
        int k1 = Mathf.CeilToInt((z + pad) / Config.CITY_SPACING) + 1;

        var cities = new List<City>();
        for (int i = i0; i <= i1; i++)
        {
            for (int k = k0; k <= k1; k++)
            {
                City city = CityCell(i, k);
                if (city == null) continue;
                if (Mathf.Sqrt((city.X - x) * (city.X - x) + (city.Z - z) * (city.Z - z)) <= radius + city.Radius)
                    cities.Add(city);
            }
        }
        return cities;
    }

    /** True inside (or within `margin` of) any city's footprint. */
    public static bool NearCity(float x, float z, float margin = 0)
    {
        foreach (City city in CitiesNear(x, z, margin))
        {
            if (Mathf.Sqrt((city.X - x) * (city.X - x) + (city.Z - z) * (city.Z - z)) <= city.Radius + margin)
                return true;
        }
        return false;
    }

    // --------------------------------------------------------------- geometry

    public static Geometry GetGeometry(City city)
    {
        long key = (long)city.I * 1000003 + city.K;
        if (GeometryCache.TryGetValue(key, out Geometry cached)) return cached;

        var geometry = new Geometry();
        float cos = Mathf.Cos(city.Angle);
        float sin = Mathf.Sin(city.Angle);
        float radius = city.Radius;
        float block = city.Block;
        int lines = Mathf.CeilToInt(radius / block) + 1;

        Vector2 ToWorld(float a, float b) => new(
            city.X + a * cos - b * sin,
            city.Z + a * sin + b * cos);

        void AddQuad(Vector2[] worldCorners, Vector3 tint)
        {
            var corners = new Vector3[4];
            for (int idx = 0; idx < 4; idx++)
            {
                float ground = WorldSim.TerrainHeight(worldCorners[idx].X, worldCorners[idx].Y);
                corners[idx] = new Vector3(worldCorners[idx].X, ground + STREET_LIFT, worldCorners[idx].Y);
            }

            // No asphalt on the water: coastal blocks stop at the shore.
            foreach (Vector3 corner in corners)
            {
                if (corner.Y - STREET_LIFT <= Config.SEA_LEVEL + 0.6f) return;
            }

            geometry.Grounds.Add(new StreetQuad { Corners = corners, Tint = tint });
        }

        void AddGround(float a0, float a1, float b0, float b1, Vector3 tint)
        {
            AddQuad(new[] { ToWorld(a0, b0), ToWorld(a1, b0), ToWorld(a1, b1), ToWorld(a0, b1) }, tint);
        }

        /* ---- street grid ---- */
        for (int m = -lines; m <= lines; m++)
        {
            float offset = m * block;
            if (Mathf.Abs(offset) >= radius) continue;
            float reach = Mathf.Sqrt(radius * radius - offset * offset);
            float width = city.Street + (m % AVENUE_EVERY == 0 ? AVENUE_EXTRA : 0);
            int segments = Mathf.Max(1, Mathf.CeilToInt((2 * reach) / block));
            float step = (2 * reach) / segments;

            for (int s = 0; s < segments; s++)
            {
                float a0 = -reach + s * step;
                float a1 = a0 + step;
                float half = width * 0.5f;

                // Streets running along the city's first axis, and the cross
                // streets along the other.
                AddGround(a0, a1, offset - half, offset + half, ASPHALT);

                AddQuad(new[]
                {
                    ToWorld(offset - half, a0),
                    ToWorld(offset + half, a0),
                    ToWorld(offset + half, a1),
                    ToWorld(offset - half, a1),
                }, ASPHALT);
            }
        }

        /* ---- blocks: parks and buildings ---- */
        int citySeed = (int)(unchecked((uint)(int)city.X * 73856093u) ^ unchecked((uint)(int)city.Z * 19349663u));
        for (int m = -lines; m <= lines; m++)
        {
            for (int q = -lines; q <= lines; q++)
            {
                float a0 = m * block;
                float b0 = q * block;
                Vector2 centre = ToWorld(a0 + block * 0.5f, b0 + block * 0.5f);
                float distance = Mathf.Sqrt((centre.X - city.X) * (centre.X - city.X) + (centre.Y - city.Z) * (centre.Y - city.Z));
                if (distance > radius - block * 0.30f) continue;

                double roll = WorldSim.Hash(m, q, citySeed ^ 301);
                if (roll < PARK_CHANCE)
                {
                    float inset = block * 0.10f;
                    AddGround(a0 + inset, a0 + block - inset, b0 + inset, b0 + block - inset, PARK);
                    continue;
                }

                // Downtown is taller and denser than the outskirts.
                float downtown = QMath.Clamp(1 - distance / radius, 0f, 1f);
                double densityRoll = WorldSim.Hash(m, q, citySeed ^ 302);
                int count = densityRoll < 0.20 ? 1 : densityRoll < 0.55 ? 2 : densityRoll < 0.82 ? 3 : 4;
                int cols = count >= 3 ? 2 : 1;
                int rows = count >= 2 ? 2 : 1;
                float lotWidth = block / cols;
                float lotDepth = block / rows;

                for (int row = 0; row < rows; row++)
                {
                    for (int col = 0; col < cols; col++)
                    {
                        if (count == 3 && row == 1 && col == 1) continue;

                        int seed = m * 977 + q * 613 + row * 37 + col * 11;
                        float margin = Mathf.Min(lotWidth, lotDepth) * 0.10f;
                        float maxWidth = lotWidth - 2 * margin;
                        float maxDepth = lotDepth - 2 * margin;
                        if (maxWidth < 5 || maxDepth < 5) continue;

                        float width = maxWidth * (0.72f + 0.24f * (float)WorldSim.Hash(seed, citySeed, 303));
                        float depth = maxDepth * (0.72f + 0.24f * (float)WorldSim.Hash(seed, citySeed, 304));

                        double towerChance = 0.025 + 0.16 * downtown * downtown;
                        bool tower = WorldSim.Hash(seed, citySeed, 305) < towerChance;

                        float height = 8 + 22 * (float)WorldSim.Hash(seed, citySeed, 306)
                            + Mathf.Pow(downtown, 1.5f) * (25 + 120 * (float)WorldSim.Hash(seed, citySeed, 307));

                        if (tower)
                        {
                            height = 85 + (float)WorldSim.Hash(seed, citySeed, 308) * (95 + 90 * downtown);
                            width *= 0.68f;
                            depth *= 0.68f;
                        }
                        height = Mathf.Min(255, height);

                        // Centre the building in its lot, with a little jitter.
                        float jitterU = ((float)WorldSim.Hash(seed, 7, 309) - 0.5f) * Mathf.Max(0, lotWidth - width - 2 * margin);
                        float jitterV = ((float)WorldSim.Hash(seed, 9, 310) - 0.5f) * Mathf.Max(0, lotDepth - depth - 2 * margin);
                        Vector2 host = ToWorld(
                            a0 + (col + 0.5f) * lotWidth + jitterU,
                            b0 + (row + 0.5f) * lotDepth + jitterV);

                        Vector3 tint = BUILDING_TINTS[
                            Mathf.FloorToInt((float)WorldSim.Hash(seed, citySeed, 311) * BUILDING_TINTS.Length)];
                        float shade = 0.86f + 0.28f * (float)WorldSim.Hash(seed, citySeed, 312);

                        // Sample the footprint corners; skip anything that dips
                        // into water or clings to a cliff.
                        float halfW = width * 0.5f;
                        float halfD = depth * 0.5f;
                        float high = float.NegativeInfinity, low = float.PositiveInfinity;
                        (float ca, float sa) = (Mathf.Cos(city.Angle), Mathf.Sin(city.Angle));
                        foreach ((int su, int sv) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                        {
                            float px = host.X + (su * halfW) * ca - (sv * halfD) * sa;
                            float pz = host.Y + (su * halfW) * sa + (sv * halfD) * ca;
                            float ground = WorldSim.TerrainHeight(px, pz);
                            high = Mathf.Max(high, ground);
                            low = Mathf.Min(low, ground);
                        }

                        if (low <= Config.SEA_LEVEL + 1 || high - low > 24) continue;

                        geometry.Buildings.Add(new Building
                        {
                            X = host.X, Z = host.Y,
                            Ground = high - 0.4f,
                            Width = width, Depth = depth, Height = height,
                            Angle = city.Angle,
                            Tint = tint,
                            Shade = shade,
                            Foundation = FOUNDATION + (high - low) * 0.5f,
                            Radius = Mathf.Sqrt(halfW * halfW + halfD * halfD),
                        });
                    }
                }
            }
        }

        GeometryCache[key] = geometry;
        return geometry;
    }

    // -------------------------------------------------------------- collision

    /** The first building struck by moving from `from` to `to`, or null. */
    public static Building CityStrike(Vector3 from, Vector3 to)
    {
        float dx = to.X - from.X;
        float dz = to.Z - from.Z;
        float travelSq = dx * dx + dz * dz;

        float midX = (from.X + to.X) * 0.5f;
        float midZ = (from.Z + to.Z) * 0.5f;
        float reach = Mathf.Sqrt(travelSq) * 0.5f;

        foreach (City city in CitiesNear(midX, midZ, reach))
        {
            // Cheap reject when the segment misses the city altogether.
            float cityPad = city.Radius + 20;
            if (SegmentCircle(from, to, city.X, city.Z, cityPad) == null) continue;

            foreach (Building building in GetGeometry(city).Buildings)
            {
                float radius = building.Radius + STRIKE_MARGIN;
                float? t = SegmentCircle(from, to, building.X, building.Z, radius);
                if (t == null) continue;

                float y = from.Y + (to.Y - from.Y) * t.Value;
                if (y >= building.Ground && y <= building.Ground + building.Height)
                    return building;
            }
        }

        return null;
    }

    /**
     * Closest approach parameter of a segment to a circle, or null when it
     * misses. Returns the midpoint parameter rather than the entry point;
     * either is inside the building for the height test.
     */
    private static float? SegmentCircle(Vector3 from, Vector3 to, float cx, float cz, float radius)
    {
        float dx = to.X - from.X;
        float dz = to.Z - from.Z;
        float fx = from.X - cx;
        float fz = from.Z - cz;
        float travelSq = dx * dx + dz * dz;

        if (travelSq < 1e-9f)
            return fx * fx + fz * fz <= radius * radius ? 0 : null;

        float t = QMath.Clamp(-(fx * dx + fz * dz) / travelSq, 0f, 1f);
        float px = fx + dx * t;
        float pz = fz + dz * t;
        return px * px + pz * pz <= radius * radius ? t : null;
    }
}
