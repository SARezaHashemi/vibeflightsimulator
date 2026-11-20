using Godot;

/**
 * Port of js/math.js — tiny vector / quaternion helpers.
 * A quaternion rotates a vector from body space into world space.
 * The exact formulas are kept so the flight model behaves identically.
 */
public static class QMath
{
    public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /** Hamilton product: the result applies `b` first, then `a`. */
    public static Quaternion QMul(Quaternion a, Quaternion b)
    {
        return new Quaternion(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
    }

    /** Rotation of `angle` radians about the (normalised) axis x,y,z. */
    public static Quaternion QAxis(float x, float y, float z, float angle)
    {
        float s = Mathf.Sin(angle * 0.5f);
        return new Quaternion(x * s, y * s, z * s, Mathf.Cos(angle * 0.5f));
    }

    /**
     * Orientation from an orthonormal aircraft basis.
     * Body +x is right, +y is up and +z points backwards, so the nose (-z)
     * maps to `forward`.
     */
    public static Quaternion QFromBasis(Vector3 right, Vector3 up, Vector3 forward)
    {
        float m00 = right.X, m01 = up.X, m02 = -forward.X;
        float m10 = right.Y, m11 = up.Y, m12 = -forward.Y;
        float m20 = right.Z, m21 = up.Z, m22 = -forward.Z;
        float trace = m00 + m11 + m22;

        float x, y, z, w;
        if (trace > 0)
        {
            float s = Mathf.Sqrt(trace + 1) * 2;
            w = 0.25f * s;
            x = (m21 - m12) / s;
            y = (m02 - m20) / s;
            z = (m10 - m01) / s;
        }
        else if (m00 > m11 && m00 > m22)
        {
            float s = Mathf.Sqrt(1 + m00 - m11 - m22) * 2;
            w = (m21 - m12) / s;
            x = 0.25f * s;
            y = (m01 + m10) / s;
            z = (m02 + m20) / s;
        }
        else if (m11 > m22)
        {
            float s = Mathf.Sqrt(1 + m11 - m00 - m22) * 2;
            w = (m02 - m20) / s;
            x = (m01 + m10) / s;
            y = 0.25f * s;
            z = (m12 + m21) / s;
        }
        else
        {
            float s = Mathf.Sqrt(1 + m22 - m00 - m11) * 2;
            w = (m10 - m01) / s;
            x = (m02 + m20) / s;
            y = (m12 + m21) / s;
            z = 0.25f * s;
        }

        return QNorm(new Quaternion(x, y, z, w));
    }

    public static Quaternion QNorm(Quaternion q)
    {
        float len = Mathf.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
        if (len == 0) len = 1;
        return new Quaternion(q.X / len, q.Y / len, q.Z / len, q.W / len);
    }

    /** Conjugate; the inverse rotation for a unit quaternion. */
    public static Quaternion QConj(Quaternion q) => new(-q.X, -q.Y, -q.Z, q.W);

    /**
     * Advance an orientation by a body-frame angular velocity over `dt`.
     * Rotating about the instantaneous axis is exact and keeps the quaternion
     * normalised without leaking rotational energy.
     */
    public static Quaternion QSpin(Quaternion q, Vector3 w, float dt)
    {
        float speed = w.Length();
        float angle = speed * dt;
        if (angle < 1e-9f) return q;

        float s = Mathf.Sin(angle * 0.5f) / speed;
        return QNorm(QMul(q, new Quaternion(w.X * s, w.Y * s, w.Z * s, Mathf.Cos(angle * 0.5f))));
    }

    /** Rotate vector `v` by quaternion `q`. */
    public static Vector3 QRot(Quaternion q, Vector3 v)
    {
        float tx = 2 * (q.Y * v.Z - q.Z * v.Y);
        float ty = 2 * (q.Z * v.X - q.X * v.Z);
        float tz = 2 * (q.X * v.Y - q.Y * v.X);
        return new Vector3(
            v.X + q.W * tx + (q.Y * tz - q.Z * ty),
            v.Y + q.W * ty + (q.Z * tx - q.X * tz),
            v.Z + q.W * tz + (q.X * ty - q.Y * tx));
    }

    /** Shortest signed angle between two headings. */
    public static float AngleDelta(float a, float b) => Mathf.Atan2(Mathf.Sin(a - b), Mathf.Cos(a - b));
}
