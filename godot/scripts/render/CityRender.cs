using System.Collections.Generic;
using Godot;

/**
 * City renderer. Port of the drawing half of js/city.js: each city becomes a
 * cached street mesh (ground quads following the terrain) plus a MultiMesh of
 * building boxes with per-instance tint. Collision lives in CityGen.
 */
public class CityRender
{
    private static readonly StandardMaterial3D Material = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private static readonly BoxMesh UnitBox = new() { Size = new Vector3(1, 1, 1) };

    private class CityNodes
    {
        public MeshInstance3D Streets;
        public MultiMeshInstance3D Buildings;
    }

    private readonly Node3D _root;
    private readonly Dictionary<long, CityNodes> _instances = new();

    public CityRender(Node3D root)
    {
        _root = root;
    }

    private static long KeyFor(CityGen.City city) => (long)city.I * 1000003 + city.K;

    private static MultiMeshInstance3D BuildBuildings(CityGen.City city)
    {
        CityGen.Building[] buildings = CityGen.GetGeometry(city).Buildings.ToArray();

        var multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = UnitBox,
        };
        multi.InstanceCount = buildings.Length;

        for (int i = 0; i < buildings.Length; i++)
        {
            CityGen.Building b = buildings[i];
            // Scale the unit box, then rotate it to the city's street angle.
            var rotation = new Basis(new Vector3(0, 1, 0), b.Angle)
                * Basis.FromScale(new Vector3(b.Width, b.Height, b.Depth));
            var origin = new Vector3(b.X, b.Ground + b.Height * 0.5f - b.Foundation, b.Z);
            multi.SetInstanceTransform(i, new Transform3D(rotation, origin));

            multi.SetInstanceColor(i, new Color(
                Mathf.Clamp(b.Tint.X * b.Shade / 255f, 0, 1),
                Mathf.Clamp(b.Tint.Y * b.Shade / 255f, 0, 1),
                Mathf.Clamp(b.Tint.Z * b.Shade / 255f, 0, 1)));
        }

        return new MultiMeshInstance3D
        {
            Multimesh = multi,
            MaterialOverride = Material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private CityNodes GetOrCreate(CityGen.City city)
    {
        long key = KeyFor(city);
        if (_instances.TryGetValue(key, out CityNodes existing)) return existing;

        // Streets: ground quads whose corners follow the terrain, so they
        // climb the hills rather than cutting through them.
        var mb = new MeshBuilder();
        foreach (CityGen.StreetQuad ground in CityGen.GetGeometry(city).Grounds)
        {
            mb.AddQuad(ground.Corners, new Color(
                ground.Tint.X / 255f, ground.Tint.Y / 255f, ground.Tint.Z / 255f));
        }

        var nodes = new CityNodes
        {
            Streets = new MeshInstance3D
            {
                Mesh = mb.Build(),
                MaterialOverride = Material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            },
            Buildings = BuildBuildings(city),
        };

        _root.AddChild(nodes.Streets);
        _root.AddChild(nodes.Buildings);
        _instances[key] = nodes;
        return nodes;
    }

    /** Show every city within range of the eye, hide the rest. */
    public void Update(Vector3 eye, float maxDistance)
    {
        var seen = new HashSet<long>();
        foreach (CityGen.City city in CityGen.CitiesNear(eye.X, eye.Z, maxDistance))
        {
            float distance = Mathf.Sqrt((city.X - eye.X) * (city.X - eye.X) + (city.Z - eye.Z) * (city.Z - eye.Z));
            if (distance > maxDistance + city.Radius) continue;

            CityNodes nodes = GetOrCreate(city);
            nodes.Streets.Visible = true;
            nodes.Buildings.Visible = true;
            seen.Add(KeyFor(city));
        }

        foreach (var pair in _instances)
        {
            if (seen.Contains(pair.Key)) continue;
            pair.Value.Streets.Visible = false;
            pair.Value.Buildings.Visible = false;
        }
    }
}
