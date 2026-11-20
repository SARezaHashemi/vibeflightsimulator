using System.Collections.Generic;
using Godot;

/**
 * The world: a procedural heightfield, graded airfield plateaus, and a colour
 * ramp for the surface. Port of js/world.js.
 */
public static class WorldSim
{
    // ---------------------------------------------------------------- relief

    /**
     * Raw terrain height at a world position, in metres.
     * Rolling sines give the general landscape; gaussian bumps make
     * snow-capped landmarks to navigate by.
     */
    public static float RawTerrainHeight(float x, float z)
    {
        return 100
            + 170 * Mathf.Sin(x * 0.00070f) * Mathf.Cos(z * 0.00082f)
            + 90 * Mathf.Sin(x * 0.0019f + 1.7f) * Mathf.Cos(z * 0.0021f + 0.4f)
            + 30 * Mathf.Sin(x * 0.0061f + 0.9f) * Mathf.Cos(z * 0.0072f + 2.1f)
            + 9 * Mathf.Sin(x * 0.021f + 4.3f) * Mathf.Cos(z * 0.017f + 1.2f)
            + 760 * Mathf.Exp(-(((x - 1700) * (x - 1700)) + ((z + 3100) * (z + 3100))) / 1200000f)
            + 520 * Mathf.Exp(-(((x + 2400) * (x + 2400)) + ((z + 1500) * (z + 1500))) / 700000f)
            + 880 * Mathf.Exp(-(((x + 7300) * (x + 7300)) + ((z + 5400) * (z + 5400))) / 1900000f)
            + 640 * Mathf.Exp(-(((x - 8600) * (x - 8600)) + ((z - 2400) * (z - 2400))) / 1500000f)
            + 450 * Mathf.Exp(-(((x - 4200) * (x - 4200)) + ((z + 9800) * (z + 9800))) / 1600000f);
    }

    // --------------------------------------------------------------- airport

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        float t = QMath.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    /** Deterministic hash of integers → [0, 1). Shared with every generator. */
    public static double Hash(int a, int b, int c)
    {
        uint h = (uint)a * 374761393u ^ (uint)b * 668265263u ^ (uint)c * 2147483647u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return h / 4294967296.0;
    }

    /** Deterministic hash of two integers → [0, 1). */
    public static double Hash2(int ix, int iz)
    {
        uint h = (uint)ix * 374761393u ^ (uint)iz * 668265263u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return h / 4294967296.0;
    }

    public class Airport
    {
        public float X, Z;
        public float Heading;   // radians, 0 = runway pointing north (-Z)
        public float Length;    // metres, threshold to threshold
        public float Width;
        public float Elevation; // metres above sea level
        public float FlattenInner;
        public float FlattenOuter;
        public bool Home;
        public Vector2 Along;   // down the centreline
        public Vector2 Across;  // across the width
    }

    /** The home airfield, due north of the start. */
    public static readonly Airport AIRPORT = new()
    {
        X = 0, Z = -3200, Heading = 0, Length = 1700, Width = 60,
        Elevation = 180, FlattenInner = 1000, FlattenOuter = 2600, Home = true,
        Along = new Vector2(0, -1), Across = new Vector2(1, 0),
    };

    private static readonly Dictionary<long, Airport> AirportCache = new();

    private static Airport GeneratedAirport(int i, int k)
    {
        long key = (long)i * 1000003 + k;
        if (AirportCache.TryGetValue(key, out Airport cached)) return cached;

        Airport airport = null;
        if (Hash(i, k, 101) <= Config.AIRPORT_CHANCE)
        {
            float x = (float)((i + 0.14 + 0.72 * Hash(i, k, 102)) * Config.AIRPORT_SPACING);
            float z = (float)((k + 0.14 + 0.72 * Hash(i, k, 103)) * Config.AIRPORT_SPACING);
            float raw = RawTerrainHeight(x, z);

            // Keep the home field's approach clear, stay out of the sea and off
            // the high mountains.
            bool awayFromHome = Mathf.Sqrt((x - AIRPORT.X) * (x - AIRPORT.X) + (z - AIRPORT.Z) * (z - AIRPORT.Z)) > Config.AIRPORT_CLEARANCE;
            if (awayFromHome && raw >= Config.AIRPORT_MIN_ELEVATION && raw <= Config.AIRPORT_MAX_ELEVATION)
            {
                float length = Config.AIRPORT_MIN_LENGTH +
                    (float)Hash(i, k, 104) * (Config.AIRPORT_MAX_LENGTH - Config.AIRPORT_MIN_LENGTH);
                float width = Config.AIRPORT_MIN_WIDTH +
                    (float)Hash(i, k, 105) * (Config.AIRPORT_MAX_WIDTH - Config.AIRPORT_MIN_WIDTH);
                float heading = ((float)Hash(i, k, 106) - 0.5f) * Mathf.Pi * 2f;
                float inner = Mathf.Max(650, length * 0.5f + 170 + width);

                // The plateau takes the average of the surrounding ground
                // rather than the single centre sample.
                float reach = inner * 0.6f;
                float elevation = QMath.Clamp((
                    raw +
                    RawTerrainHeight(x + reach, z) +
                    RawTerrainHeight(x - reach, z) +
                    RawTerrainHeight(x, z + reach) +
                    RawTerrainHeight(x, z - reach)) / 5f,
                    8f, Config.AIRPORT_MAX_ELEVATION);

                airport = new Airport
                {
                    X = x, Z = z, Heading = heading, Length = length, Width = width,
                    Elevation = elevation,
                    FlattenInner = inner,
                    FlattenOuter = inner + 1500 + (float)Hash(i, k, 107) * 500,
                    Home = false,
                    Along = new Vector2(Mathf.Sin(heading), -Mathf.Cos(heading)),
                    Across = new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)),
                };
            }
        }

        AirportCache[key] = airport;
        return airport;
    }

    /** Every airfield whose centre lies within `radius` of a point. */
    public static List<Airport> AirportsNear(float x, float z, float radius)
    {
        var airports = new List<Airport>();
        if (Mathf.Sqrt((x - AIRPORT.X) * (x - AIRPORT.X) + (z - AIRPORT.Z) * (z - AIRPORT.Z)) <= radius + AIRPORT.FlattenOuter)
            airports.Add(AIRPORT);

        int i0 = Mathf.FloorToInt((x - radius) / Config.AIRPORT_SPACING) - 1;
        int i1 = Mathf.CeilToInt((x + radius) / Config.AIRPORT_SPACING) + 1;
        int k0 = Mathf.FloorToInt((z - radius) / Config.AIRPORT_SPACING) - 1;
        int k1 = Mathf.CeilToInt((z + radius) / Config.AIRPORT_SPACING) + 1;

        for (int i = i0; i <= i1; i++)
        {
            for (int k = k0; k <= k1; k++)
            {
                Airport airport = GeneratedAirport(i, k);
                if (airport == null) continue;
                if (Mathf.Sqrt((airport.X - x) * (airport.X - x) + (airport.Z - z) * (airport.Z - z)) <= radius + airport.FlattenOuter)
                    airports.Add(airport);
            }
        }
        return airports;
    }

    /** The airfield whose centre is closest to a point (home field by default). */
    public static Airport NearestAirport(float x, float z)
    {
        Airport best = AIRPORT;
        float bestDistance = Mathf.Sqrt((x - AIRPORT.X) * (x - AIRPORT.X) + (z - AIRPORT.Z) * (z - AIRPORT.Z));

        int i0 = Mathf.RoundToInt(x / Config.AIRPORT_SPACING) - 3;
        int i1 = Mathf.RoundToInt(x / Config.AIRPORT_SPACING) + 3;
        int k0 = Mathf.RoundToInt(z / Config.AIRPORT_SPACING) - 3;
        int k1 = Mathf.RoundToInt(z / Config.AIRPORT_SPACING) + 3;

        for (int i = i0; i <= i1; i++)
        {
            for (int k = k0; k <= k1; k++)
            {
                Airport airport = GeneratedAirport(i, k);
                if (airport == null) continue;
                float distance = Mathf.Sqrt((airport.X - x) * (airport.X - x) + (airport.Z - z) * (airport.Z - z));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = airport;
                }
            }
        }
        return best;
    }

    /** Ground height including every graded airfield plateau. */
    public static float TerrainHeight(float x, float z)
    {
        float height = RawTerrainHeight(x, z);
        float strongest = 0;
        float plateau = height;

        // An airfield's plateau can only reach the neighbouring grid cells.
        int i0 = Mathf.RoundToInt(x / Config.AIRPORT_SPACING) - 1;
        int i1 = Mathf.RoundToInt(x / Config.AIRPORT_SPACING) + 1;
        int k0 = Mathf.RoundToInt(z / Config.AIRPORT_SPACING) - 1;
        int k1 = Mathf.RoundToInt(z / Config.AIRPORT_SPACING) + 1;

        void Consider(Airport airport)
        {
            if (airport == null) return;
            float distance = Mathf.Sqrt((x - airport.X) * (x - airport.X) + (z - airport.Z) * (z - airport.Z));
            if (distance >= airport.FlattenOuter) return;
            float weight = 1 - Smoothstep(airport.FlattenInner, airport.FlattenOuter, distance);
            if (weight > strongest)
            {
                strongest = weight;
                plateau = airport.Elevation;
            }
        }

        Consider(AIRPORT);
        for (int i = i0; i <= i1; i++)
            for (int k = k0; k <= k1; k++)
                Consider(GeneratedAirport(i, k));

        if (strongest > 0) height += (plateau - height) * strongest;
        return height;
    }

    /** Position relative to a runway centre, in runway coordinates. */
    public static (float along, float across) RunwayLocal(Airport airport, float x, float z)
    {
        float dx = x - airport.X;
        float dz = z - airport.Z;
        return (dx * airport.Along.X + dz * airport.Along.Y,
                dx * airport.Across.X + dz * airport.Across.Y);
    }

    /** Is this point on an airport's paved surface (with an optional margin)? */
    public static bool IsOnRunway(Airport airport, float x, float z, float margin = 0)
    {
        (float along, float across) = RunwayLocal(airport, x, z);
        return Mathf.Abs(along) <= airport.Length / 2 + margin &&
               Mathf.Abs(across) <= airport.Width / 2 + margin;
    }

    /** True if a point lies on or near any airfield's runway or approach path. */
    public static bool NearAirport(float x, float z, float acrossMargin, float alongMargin)
    {
        foreach (Airport airport in AirportsNear(x, z, 1200))
        {
            (float along, float across) = RunwayLocal(airport, x, z);
            if (Mathf.Abs(across) < acrossMargin &&
                Mathf.Abs(along) < airport.Length / 2 + alongMargin)
                return true;
        }
        return false;
    }

    // -------------------------------------------------------------- surfaces

    public enum SurfaceKind { Runway, Grass, Rough, Water }

    public struct SurfaceInfo
    {
        public SurfaceKind Kind;
        public float Height;
        public float Slope;
        public Vector2 Gradient;
        public Vector3 Normal;
    }

    private const float ROUGH_SLOPE = 0.14f;
    private const float ROUGH_HEIGHT = 200f;

    /** Terrain slope by central difference. */
    public static Vector2 TerrainGradient(float x, float z, float step = 8)
    {
        return new Vector2(
            (TerrainHeight(x + step, z) - TerrainHeight(x - step, z)) / (2 * step),
            (TerrainHeight(x, z + step) - TerrainHeight(x, z - step)) / (2 * step));
    }

    /** Height of the solid surface: the ground where dry, else the sea surface. */
    public static float SurfaceHeight(float x, float z)
    {
        return Mathf.Max(TerrainHeight(x, z), Config.SEA_LEVEL);
    }

    /**
     * Classify the surface under a point and measure its local slope.
     * The returned `normal` points out of the surface; landing impact is
     * measured along it.
     */
    public static SurfaceInfo SurfaceAt(float x, float z)
    {
        float ground = TerrainHeight(x, z);
        if (ground <= Config.SEA_LEVEL)
        {
            return new SurfaceInfo
            {
                Kind = SurfaceKind.Water,
                Height = Config.SEA_LEVEL,
                Slope = 0,
                Gradient = Vector2.Zero,
                Normal = Vector3.Up,
            };
        }

        Vector2 gradient = TerrainGradient(x, z);
        float slope = Mathf.Sqrt(gradient.X * gradient.X + gradient.Y * gradient.Y);
        float inv = 1 / Mathf.Sqrt(gradient.X * gradient.X + 1 + gradient.Y * gradient.Y);

        SurfaceKind kind;
        bool onPavement = false;
        foreach (Airport airport in AirportsNear(x, z, 1200))
        {
            if (IsOnRunway(airport, x, z, 1)) { onPavement = true; break; }
        }
        if (onPavement) kind = SurfaceKind.Runway;
        else if (ground > ROUGH_HEIGHT || slope > ROUGH_SLOPE) kind = SurfaceKind.Rough;
        else kind = SurfaceKind.Grass;

        return new SurfaceInfo
        {
            Kind = kind,
            Height = Mathf.Max(ground, Config.SEA_LEVEL),
            Slope = slope,
            Gradient = gradient,
            Normal = new Vector3(-gradient.X * inv, inv, -gradient.Y * inv),
        };
    }

    /**
     * Height of the sea surface at a world position. Three long, shallow
     * sines anchored to world coordinates and advanced by time.
     */
    public static float WaterHeight(float x, float z, float time)
    {
        return Config.SEA_LEVEL
            + 0.24f * Mathf.Sin(x * 0.028f + z * 0.010f + time * 1.10f)
            + 0.14f * Mathf.Sin(z * 0.041f - x * 0.008f - time * 1.45f)
            + 0.08f * Mathf.Sin((x + z) * 0.075f + time * 2.10f);
    }

    // --------------------------------------------------------------- palette

    /**
     * Base surface colour for a given height.
     * Bands: water → wet sand → beach grass → meadow → hillside → rock → snow.
     */
    public static Color SurfaceColor(float h)
    {
        if (h <= Config.SEA_LEVEL)
            return new Color(Config.WATER.X / 255f, Config.WATER.Y / 255f, Config.WATER.Z / 255f);

        // The strip at the waterline is darker: sand still wet from the tide.
        if (h < 2.5f)
        {
            float t = h / 2.5f;
            return Mix(150 / 255f, 132 / 255f, 92 / 255f,   // wet sand
                       216 / 255f, 196 / 255f, 142 / 255f,  // dry beach
                       t);
        }
        else if (h < 9)
        {
            // Beach grass gives way to proper meadow.
            float t = (h - 2.5f) / 6.5f;
            return Mix(148 / 255f, 168 / 255f, 96 / 255f,   // dune grass
                       62 / 255f, 124 / 255f, 54 / 255f,    // meadow green
                       QMath.Clamp(t * 1.4f, 0f, 1f));
        }
        else if (h < 240)
        {
            // Lush lowlands drying out towards the hill lines.
            float t = (h - 9) / 231;
            return Mix(58 / 255f, 126 / 255f, 52 / 255f,    // rich pasture
                       128 / 255f, 140 / 255f, 72 / 255f,   // dry hillside
                       t * t);
        }
        else if (h < 330)
        {
            float t = (h - 240) / 90;                                       // scree
            return Mix(120 / 255f, 112 / 255f, 84 / 255f,
                       146 / 255f, 134 / 255f, 112 / 255f,
                       t);
        }
        else if (h < 400)
        {
            float t = (h - 330) / 70;                                       // rock
            return Mix(146 / 255f, 134 / 255f, 112 / 255f,
                       168 / 255f, 164 / 255f, 156 / 255f,
                       t);
        }
        else
        {
            float t = Mathf.Min(1, (h - 400) / 120);                        // snow
            return Mix(206 / 255f, 210 / 255f, 216 / 255f,
                       240 / 255f, 244 / 255f, 250 / 255f,
                       t);
        }
    }

    private static Color Mix(float r0, float g0, float b0, float r1, float g1, float b1, float t)
    {
        return new Color(r0 + (r1 - r0) * t, g0 + (g1 - g0) * t, b0 + (b1 - b0) * t);
    }
}
