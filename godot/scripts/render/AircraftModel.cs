using System.Collections.Generic;
using Godot;

/**
 * A low-poly aircraft. Port of js/model.js onto a real ArrayMesh.
 *
 * Geometry is authored in body space: +x right, +y up, -z forward (the nose
 * points down -z) and the origin sits at the centre of gravity. The wheels
 * are modelled with their bottoms at y = -GEAR_HEIGHT, so a level aircraft
 * sitting GEAR_HEIGHT above the ground rests exactly on them. Gear faces are
 * rebuilt while the extension animates.
 */
public class AircraftModel
{
    private static readonly Color UPPER = Config.FUSELAGE_TOP;
    private static readonly Color SIDE = Config.FUSELAGE_SIDE;
    private static readonly Color BELLY = Config.FUSELAGE_BELLY;
    private static readonly Color ACCENT = Config.FUSELAGE_ACCENT;
    private static readonly Color WING = Config.WING_COLOUR;
    private static readonly Color GLASS = Config.CANOPY_GLASS;
    private static readonly Color METAL = Config.METAL;
    private static readonly Color TYRE = Config.TYRE;

    private const float WHEEL_BOTTOM = -1.6f; // matches GEAR_HEIGHT

    private static readonly StandardMaterial3D Material = MeshBuilder.ShadedMaterial();

    private readonly MeshBuilder _body = new();
    private readonly MeshBuilder _gear = new();

    public readonly MeshInstance3D BodyNode;
    public readonly MeshInstance3D GearNode;
    private float _gearBuiltAt = -1;

    public AircraftModel(Node3D parent)
    {
        BuildAirframe(_body);

        BodyNode = new MeshInstance3D { Mesh = _body.Build(), MaterialOverride = Material };
        // The gear is authored in body space, so it rides on the body node
        // and inherits the aircraft's transform every frame.
        GearNode = new MeshInstance3D { Mesh = null, MaterialOverride = Material };
        parent.AddChild(BodyNode);
        BodyNode.AddChild(GearNode);
    }

    // ------------------------------------------------------------- geometry

    private static Vector3 Vec(float x, float y, float z) => new(x, y, z);

    private static Vector3 Centroid(List<Vector3> points)
    {
        Vector3 sum = Vector3.Zero;
        foreach (Vector3 p in points) sum += p;
        return points.Count == 0 ? sum : sum / points.Count;
    }

    /** Newell's method face normal (delegates to MeshBuilder). */
    private static Vector3 FaceNormal(List<Vector3> points) => MeshBuilder.FaceNormal(points);

    /// <summary>Wound so that its normal points away from `outward`.</summary>
    private static (List<Vector3> points, bool) Oriented(List<Vector3> points, Vector3 outward)
    {
        Vector3 n = FaceNormal(points);
        bool flip = n.Dot(outward) < 0;
        if (flip) points.Reverse();
        return (points, flip);
    }

    /**
     * Close the hull between two matching rings. The outward direction of
     * every face is the face centre minus the hull centre, which is correct
     * for any convex shape.
     */
    private static void MakeSolid(MeshBuilder mb, List<Vector3> front, List<Vector3> back, Color colour)
    {
        var all = new List<Vector3>(front);
        all.AddRange(back);
        Vector3 mid = Centroid(all);

        void Face(List<Vector3> points)
        {
            Vector3 outward = Centroid(points) - mid;
            var oriented = new List<Vector3>(points);
            Vector3 n = FaceNormal(oriented);
            if (n.Dot(outward) < 0) oriented.Reverse();

            if (oriented.Count == 4) mb.AddQuad(oriented, colour);
            else if (oriented.Count == 3) mb.AddTriangle(oriented[0], oriented[1], oriented[2], colour);
        }

        Face(new List<Vector3>(front));
        Face(new List<Vector3>(back));
        for (int i = 0; i < front.Count; i++)
        {
            int j = (i + 1) % front.Count;
            Face(new List<Vector3> { front[i], front[j], back[j], back[i] });
        }
    }

    private static void MakeBox(MeshBuilder mb, float cx, float cy, float cz, float hx, float hy, float hz, Color colour)
    {
        MakeSolid(mb,
            new List<Vector3>
            {
                Vec(cx - hx, cy + hy, cz - hz),
                Vec(cx + hx, cy + hy, cz - hz),
                Vec(cx + hx, cy + hy, cz + hz),
                Vec(cx - hx, cy + hy, cz + hz),
            },
            new List<Vector3>
            {
                Vec(cx - hx, cy - hy, cz - hz),
                Vec(cx + hx, cy - hy, cz - hz),
                Vec(cx + hx, cy - hy, cz + hz),
                Vec(cx - hx, cy - hy, cz + hz),
            },
            colour);
    }

    /** A wheel: a short octagonal cylinder lying along the body x axis. */
    private static void MakeWheel(MeshBuilder mb, float cx, float cy, float cz, float radius, float halfWidth, Color colour)
    {
        var ring = new List<Vector3>();
        for (int i = 0; i < 8; i++)
        {
            float a = (i / 8f) * Mathf.Pi * 2 + Mathf.Pi / 8;
            ring.Add(Vec(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0));
        }
        var front = new List<Vector3>();
        var back = new List<Vector3>();
        foreach (Vector3 p in ring)
        {
            front.Add(Vec(cx - halfWidth, cy + p.X, cz + p.Y));
            back.Add(Vec(cx + halfWidth, cy + p.X, cz + p.Y));
        }
        MakeSolid(mb, front, back, colour);
    }

    // --------------------------------------------------------- the airframe

    /** A fuselage ring: eight points around the tube. */
    private static List<Vector3> Ring(float z, float radius, float centreY)
    {
        var points = new List<Vector3>();
        for (int i = 0; i < 8; i++)
        {
            float a = (i / 8f) * Mathf.Pi * 2 + Mathf.Pi / 8;
            points.Add(Vec(Mathf.Cos(a) * radius, centreY + Mathf.Sin(a) * radius, z));
        }
        return points;
    }

    private static readonly (float z, float r, float y)[] FUSELAGE =
    {
        (-6.30f, 0.18f, 0.00f),  // nose
        (-5.80f, 0.46f, -0.02f),
        (-4.90f, 0.76f, -0.03f),
        (-3.60f, 0.93f, -0.01f),
        (-1.40f, 0.98f, 0.00f),
        (1.60f, 0.92f, 0.02f),
        (3.50f, 0.66f, 0.06f),
        (4.75f, 0.26f, 0.10f),   // tail cone
    };

    /** Panels are numbered from the upper right, going anticlockwise. */
    private static Color FuselageColour(int panel)
    {
        if (panel == 3 || panel == 7) return ACCENT;  // the flank stripe
        if (panel == 1) return UPPER;                 // the roof
        if (panel >= 4 && panel <= 6) return BELLY;   // the underside
        return SIDE;
    }

    private static void BuildAirframe(MeshBuilder mb)
    {
        // Loft the fuselage through its rings.
        var rings = new List<Vector3>[FUSELAGE.Length];
        for (int i = 0; i < FUSELAGE.Length; i++)
        {
            (float z, float r, float y) = FUSELAGE[i];
            rings[i] = Ring(z, r, y);
        }

        for (int i = 0; i < rings.Length - 1; i++)
        {
            List<Vector3> a = rings[i], b = rings[i + 1];
            var all = new List<Vector3>(a);
            all.AddRange(b);
            Vector3 mid = Centroid(all);

            for (int k = 0; k < 8; k++)
            {
                int k2 = (k + 1) % 8;
                var quad = new List<Vector3> { a[k], a[k2], b[k2], b[k] };
                Vector3 outward = Centroid(quad) - mid;
                Vector3 n = FaceNormal(quad);
                if (n.Dot(outward) < 0) quad.Reverse();

                mb.AddQuad(quad, FuselageColour(k));
            }
        }

        // Nose and tail caps.
        {
            var cap = new List<Vector3>(rings[0]);
            if (FaceNormal(cap).Dot(new Vector3(0, -0.2f, -1)) < 0) cap.Reverse();
            mb.AddFan(cap.ToArray(), UPPER);
        }
        {
            var cap = new List<Vector3>(rings[^1]);
            if (FaceNormal(cap).Dot(new Vector3(0, 0.2f, 1)) < 0) cap.Reverse();
            mb.AddFan(cap.ToArray(), SIDE, reverse: true);
        }

        // Wings: tapered and with a little dihedral.
        Wing(mb, 1);
        Wing(mb, -1);

        // Tailplane.
        Tailplane(mb, 1);
        Tailplane(mb, -1);

        // Fin.
        {
            var fin = new List<Vector3>
            {
                Vec(0, 0.35f, 4.10f),
                Vec(0, 0.35f, 5.20f),
                Vec(0, 2.30f, 5.05f),
                Vec(0, 2.30f, 4.70f),
            };
            MakeSolid(mb, fin, fin.ConvertAll(p => Vec(p.X + 0.14f, p.Y, p.Z)), ACCENT);
        }

        // Canopy: sits over the cockpit, and the eye is placed inside it.
        MakeSolid(mb,
            new List<Vector3>
            {
                Vec(-0.62f, 0.72f, -4.40f),
                Vec(0.62f, 0.72f, -4.40f),
                Vec(0.66f, 0.72f, -2.25f),
                Vec(-0.66f, 0.72f, -2.25f),
            },
            new List<Vector3>
            {
                Vec(-0.40f, 1.42f, -3.95f),
                Vec(0.40f, 1.42f, -3.95f),
                Vec(0.48f, 1.42f, -2.45f),
                Vec(-0.48f, 1.42f, -2.45f),
            },
            GLASS);
    }

    private static void Wing(MeshBuilder mb, float sign)
    {
        var top = new List<Vector3>
        {
            Vec(sign * 0.60f, 0.14f, -1.85f),
            Vec(sign * 0.60f, 0.14f, 1.20f),
            Vec(sign * 5.60f, 0.64f, 0.45f),
            Vec(sign * 5.60f, 0.64f, -1.05f),
        };
        MakeSolid(mb, top, top.ConvertAll(p => Vec(p.X, p.Y - 0.18f, p.Z)), WING);
    }

    private static void Tailplane(MeshBuilder mb, float sign)
    {
        var top = new List<Vector3>
        {
            Vec(sign * 0.25f, 0.30f, 3.85f),
            Vec(sign * 0.25f, 0.30f, 5.05f),
            Vec(sign * 2.35f, 0.48f, 4.95f),
            Vec(sign * 2.35f, 0.48f, 4.20f),
        };
        MakeSolid(mb, top, top.ConvertAll(p => Vec(p.X, p.Y - 0.12f, p.Z)), WING);
    }

    // ------------------------------------------------------------ the gear

    private struct Leg
    {
        public float X, Z, Radius, HalfWidth, Inward;
    }

    private static readonly Leg[] LEGS =
    {
        new() { X = 0.00f, Z = -3.55f, Radius = 0.20f, HalfWidth = 0.09f, Inward = 0.00f },
        new() { X = -1.45f, Z = 0.55f, Radius = 0.26f, HalfWidth = 0.11f, Inward = 0.70f },
        new() { X = 1.45f, Z = 0.55f, Radius = 0.26f, HalfWidth = 0.11f, Inward = 0.70f },
    };

    /**
     * Gear faces for a given extension, 0 = retracted, 1 = locked down. The
     * legs swing inwards and up into the fuselage as they retract.
     */
    private static void BuildGear(MeshBuilder mb, float extension)
    {
        if (extension <= 0.01f) return;

        float bottom = WHEEL_BOTTOM * extension;

        foreach (Leg leg in LEGS)
        {
            float x = leg.X + Mathf.Sign(leg.X) * leg.Inward * (1 - extension);
            float wheelCentre = bottom + leg.Radius;
            float wheelTop = bottom + leg.Radius * 2;

            MakeWheel(mb, x, wheelCentre, leg.Z, leg.Radius, leg.HalfWidth, TYRE);

            if (wheelTop < 0.05f)
            {
                float top = 0.05f;
                MakeBox(mb, x, (top + wheelTop) * 0.5f, leg.Z,
                    0.07f, (top - wheelTop) * 0.5f, 0.07f, METAL);
            }
        }
    }

    /** Rebuild the gear mesh when its extension has moved enough to matter. */
    public void UpdateGear(float gearPosition)
    {
        if (Mathf.Abs(gearPosition - _gearBuiltAt) < 0.02f) return;
        _gearBuiltAt = gearPosition;

        var mb = new MeshBuilder();
        BuildGear(mb, gearPosition);
        GearNode.Mesh = mb.Build();
    }

    /** Remove the gear mesh once it is fully retracted. */
    public void HideGearWhenUp(float gearPosition)
    {
        GearNode.Visible = gearPosition > 0.01f;
    }
}
