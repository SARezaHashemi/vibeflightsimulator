using System.Collections.Generic;
using Godot;

/**
 * Tree renderer. Port of the drawing half of js/trees.js: near trees are
 * solid low-poly volumes — a trunk prism under a canopy of polygonal tiers —
 * drawn through one MultiMesh per species. Placement and collision live in
 * TreeGen.
 */
public class TreeRender
{
    private static readonly StandardMaterial3D Material = MeshBuilder.ShadedMaterial();

    private readonly Node3D _root;
    private readonly MultiMeshInstance3D[] _species = new MultiMeshInstance3D[TreeGen.KINDS.Length];

    public TreeRender(Node3D root)
    {
        _root = root;
        for (int k = 0; k < TreeGen.KINDS.Length; k++)
        {
            var multi = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = BuildSpeciesMesh(k),
            };
            multi.InstanceCount = 0;

            _species[k] = new MultiMeshInstance3D
            {
                Multimesh = multi,
                MaterialOverride = Material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            };
            _root.AddChild(_species[k]);
        }
    }

    /** A horizontal ring of points around the tree's vertical axis. */
    private static Vector3[] MakeRing(float y, float radius, int sides, float spin)
    {
        var points = new Vector3[sides];
        float step = Mathf.Pi * 2 / sides;
        for (int i = 0; i < sides; i++)
        {
            float angle = spin + i * step;
            points[i] = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
        }
        return points;
    }

    /**
     * Build the solid low-poly geometry for one species, authored with unit
     * height and unit width (instance transforms scale and spin it).
     * Conifers are built from closed frustum tiers, broadleaves from one
     * continuous loft.
     */
    private static ArrayMesh BuildSpeciesMesh(int kindIndex)
    {
        TreeGen.Species spec = TreeGen.KINDS[kindIndex];
        var mb = new MeshBuilder();
        int sides = 6;
        float spin = 0; // per-instance spin comes from the instance transform
        Color tint = spec.Tint;
        Color trunk = Config.TRUNK;

        void Push(Vector3[] points, Color colour)
        {
            mb.AddQuad(points, colour);
        }

        void PushTri(Vector3 a, Vector3 b, Vector3 c, Color colour)
        {
            mb.AddTriangle(a, b, c, colour);
        }

        if (spec.Tiers != null)
        {
            int tierCount = spec.Tiers.Length;
            for (int k = 0; k < tierCount; k++)
            {
                (float y0, float r0, float y1, float r1) = spec.Tiers[k];
                Vector3[] bottom = MakeRing(y0, r0, sides, spin);
                Vector3[] top = r1 > 0.001f ? MakeRing(y1, r1, sides, spin) : null;

                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    if (top != null)
                        Push(new[] { bottom[i], top[i], top[j], bottom[j] }, tint);
                    else
                        PushTri(bottom[i], new Vector3(0, y1, 0), bottom[j], tint);
                }

                // Cap the underside of every tier; from below that is what you
                // see. Only the top of the whole tree needs a top cap.
                mb.AddFan(bottom, tint);
                if (top != null && k == tierCount - 1)
                    mb.AddFan(top, tint, reverse: true);
            }
        }
        else
        {
            var profile = spec.Profile;
            Vector3[][] rings = new Vector3[profile.Length][];
            for (int r = 0; r < profile.Length; r++)
                rings[r] = profile[r].w > 0.001f ? MakeRing(profile[r].y, profile[r].w, sides, spin) : null;

            for (int r = 0; r < rings.Length - 1; r++)
            {
                Vector3[] low = rings[r];
                Vector3[] high = rings[r + 1];

                if (high == null)
                {
                    Vector3 tip = new(0, profile[r + 1].y, 0);
                    for (int i = 0; i < sides; i++)
                        PushTri(low[i], tip, low[(i + 1) % sides], tint);
                    continue;
                }

                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    Push(new[] { low[i], high[i], high[j], low[j] }, tint);
                }
            }

            if (rings[0] != null) mb.AddFan(rings[0], tint);
            Vector3[] last = rings[^1];
            if (last != null) mb.AddFan(last, tint, reverse: true);
        }

        // Trunk prism, reaching a little way into the canopy so there is no
        // gap. Authored at the species' typical trunk fraction.
        float trunkRadius = Mathf.Max(0.012f, 0.018f * 1.2f);
        Vector3[] trunkBottom = MakeRing(0, trunkRadius, 5, spin);
        Vector3[] trunkTop = MakeRing(spec.CanopyBase + 0.04f, trunkRadius, 5, spin);
        for (int i = 0; i < 5; i++)
        {
            int j = (i + 1) % 5;
            Push(new[] { trunkBottom[i], trunkTop[i], trunkTop[j], trunkBottom[j] }, trunk);
        }

        var mesh = mb.Build();
        return mesh;
    }

    /** Rebuild the instance buffers with the trees around the eye. */
    public void Update(Vector3 eye)
    {
        int[] counts = new int[_species.Length];
        int ci = Mathf.FloorToInt(eye.X / Config.CELL_SIZE);
        int cj = Mathf.FloorToInt(eye.Z / Config.CELL_SIZE);

        // First pass: count.
        for (int gi = ci - Config.TREE_RANGE; gi <= ci + Config.TREE_RANGE; gi++)
        {
            for (int gj = cj - Config.TREE_RANGE; gj <= cj + Config.TREE_RANGE; gj++)
            {
                foreach (TreeGen.Tree tree in TreeGen.CellTrees(gi, gj))
                {
                    float dx = tree.X - eye.X, dz = tree.Z - eye.Z;
                    if (dx * dx + dz * dz > Config.TREE_MAX_DISTANCE * Config.TREE_MAX_DISTANCE) continue;
                    counts[tree.Kind]++;
                }
            }
        }

        for (int k = 0; k < _species.Length; k++)
            _species[k].Multimesh.InstanceCount = counts[k];

        // Second pass: fill.
        int[] cursor = new int[_species.Length];
        float maxSq = Config.TREE_MAX_DISTANCE * Config.TREE_MAX_DISTANCE;

        for (int gi = ci - Config.TREE_RANGE; gi <= ci + Config.TREE_RANGE; gi++)
        {
            for (int gj = cj - Config.TREE_RANGE; gj <= cj + Config.TREE_RANGE; gj++)
            {
                foreach (TreeGen.Tree tree in TreeGen.CellTrees(gi, gj))
                {
                    float dx = tree.X - eye.X, dz = tree.Z - eye.Z;
                    if (dx * dx + dz * dz > maxSq) continue;

                    var multi = _species[tree.Kind].Multimesh;
                    int i = cursor[tree.Kind]++;

                    // The mesh is authored with unit height and width: the
                    // instance scales it, spins it and plants it in the
                    // ground.
                    var basis = new Basis(new Vector3(0, 1, 0), tree.Spin)
                        * Basis.FromScale(new Vector3(tree.Height * tree.Width, tree.Height, tree.Height * tree.Width));
                    multi.SetInstanceTransform(i, new Transform3D(basis, new Vector3(tree.X, tree.Ground, tree.Z)));
                    multi.SetInstanceColor(i, new Color(tree.Shade, tree.Shade, tree.Shade));
                }
            }
        }
    }
}
