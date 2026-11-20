using System.Collections.Generic;
using Godot;

/**
 * Small helper for building flat-shaded, vertex-coloured ArrayMeshes.
 * All world geometry (terrain, scenery, cities, trees, clouds) goes through
 * here. Faces get a flat normal per triangle — that is what gives the
 * low-poly facets their crisp read under the directional sun.
 */
public class MeshBuilder
{
    private readonly List<Vector3> _vertices = new();
    private readonly List<Vector3> _normals = new();
    private readonly List<Color> _colors = new();

    public int VertexCount => _vertices.Count;

    public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color colour)
    {
        Vector3 n = FaceNormal(new List<Vector3> { a, b, c });
        _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
        _normals.Add(n); _normals.Add(n); _normals.Add(n);
        _colors.Add(colour); _colors.Add(colour); _colors.Add(colour);
    }

    public void AddQuad(IReadOnlyList<Vector3> p, Color colour)
    {
        // Two triangles: p0 p1 p2 and p0 p2 p3.
        AddTriangle(p[0], p[1], p[2], colour);
        AddTriangle(p[0], p[2], p[3], colour);
    }

    /** A polygon fan (used to cap rings), optionally reversed. */
    public void AddFan(Vector3[] ring, Color colour, bool reverse = false)
    {
        for (int i = 1; i < ring.Length - 1; i++)
        {
            if (reverse) AddTriangle(ring[i], ring[i + 1], ring[0], colour);
            else AddTriangle(ring[0], ring[i], ring[i + 1], colour);
        }
    }

    public ArrayMesh Build()
    {
        if (_vertices.Count == 0) return null;

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = _colors.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /**
     * The one material for all low-poly world geometry: albedo from vertex
     * colours, lit by the sun + ambient fill in Main, flat facets from the
     * per-face normals.
     */
    public static StandardMaterial3D ShadedMaterial()
    {
        return new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 1f,
            MetallicSpecular = 0.2f,   // keep the plasticky highlight off the paint
        };
    }

    /** Newell's method: a stable normal for any (almost) planar polygon. */
    public static Vector3 FaceNormal(IReadOnlyList<Vector3> points)
    {
        float nx = 0, ny = 0, nz = 0;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 a = points[i];
            Vector3 b = points[(i + 1) % points.Count];
            nx += (a.Y - b.Y) * (a.Z + b.Z);
            ny += (a.Z - b.Z) * (a.X + b.X);
            nz += (a.X - b.X) * (a.Y + b.Y);
        }
        float len = new Vector3(nx, ny, nz).Length();
        if (len == 0) return Vector3.Up;
        return new Vector3(nx / len, ny / len, nz / len);
    }
}
