using Godot;

/**
 * Head-up display, painted onto a full-screen Control. Port of js/hud.js.
 */
public partial class Hud : Control
{
    private static readonly Color TINT = new(150f / 255f, 215f / 255f, 255f / 255f);
    private const int LABEL = 10;
    private const int VALUE = 30;
    private const int SMALL = 11;
    private const int BIG = 34;
    private const int BOLD = 13;

    private readonly Aircraft _aircraft;
    private Font _font;

    public Hud(Aircraft aircraft)
    {
        _aircraft = aircraft;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    private static Color Tint(float alpha) => new(TINT.R, TINT.G, TINT.B, alpha);

    private void Label(string text, float x, float y, int size, Color colour)
    {
        float ascent = _font.GetAscent(size);
        DrawString(_font, new Vector2(x, y + ascent), text, HorizontalAlignment.Left, -1, size, colour);
    }

    private void LabelCentre(string text, float cx, float y, int size, Color colour)
    {
        float ascent = _font.GetAscent(size);
        DrawString(_font, new Vector2(cx - 300, y + ascent), text, HorizontalAlignment.Center, 600, size, colour);
    }

    /** Three gear lamps: green when locked down, amber in transit, dark when up. */
    private void DrawGear()
    {
        float H = Size.Y;
        float y = H - 30;
        float lampW = 16;
        float lampH = 6;
        float gap = 4;
        float total = lampW * 3 + gap * 2;
        float x0 = Size.X / 2 - total / 2;

        Color colour;
        if (_aircraft.GearDown && _aircraft.GearPosition > 0.98f) colour = new Color(0.37f, 0.94f, 0.54f);
        else if (_aircraft.GearPosition > 0.02f) colour = new Color(0.94f, 0.78f, 0.38f);
        else colour = new Color(TINT.R, TINT.G, TINT.B, 0.22f);

        LabelCentre("GEAR", Size.X / 2, H - 46, LABEL, Tint(0.55f));
        for (int i = 0; i < 3; i++)
            DrawRect(new Rect2(x0 + i * (lampW + gap), y, lampW, lampH), colour);
    }

    /** Distance and bearing to the nearest airfield, with a turn arrow. */
    private void DrawRunwayGuidance()
    {
        WorldSim.Airport airport = WorldSim.NearestAirport(_aircraft.Position.X, _aircraft.Position.Z);
        float dx = airport.X - _aircraft.Position.X;
        float dz = airport.Z - _aircraft.Position.Z;
        float distance = Mathf.Sqrt(dx * dx + dz * dz);
        if (distance > 20000) return;

        float bearing = Mathf.Atan2(dx, -dz);
        float offset = QMath.AngleDelta(bearing, _aircraft.HeadingRad);
        string arrow = offset > 0.26 ? ">>" : offset < -0.26 ? "<<" : "^";
        string km = (distance / 1000).ToString(distance < 2000 ? "F2" : "F1");

        LabelCentre($"RWY {km} KM  {arrow}", Size.X / 2, 8, SMALL, Tint(0.7f));
    }

    public override void _Draw()
    {
        _font ??= ThemeDB.FallbackFont;

        float W = Size.X;
        float H = Size.Y;
        Aircraft a = _aircraft;

        // Speed, top left.
        string kmh = Mathf.RoundToInt(a.Speed * 3.6f).ToString();
        Label("SPEED", 22, 20, LABEL, Tint(0.55f));
        Label(kmh, 20, 34, VALUE, new Color(0.92f, 0.97f, 1f));

        float kmhWidth = _font.GetStringSize(kmh, HorizontalAlignment.Left, -1, VALUE).X;
        Label("KM/H", 20 + kmhWidth + 7, 50, LABEL, Tint(0.6f));
        float acc = a.LongitudinalAcceleration;
        Label(
            $"{Mathf.RoundToInt(a.Speed)} M/S   {(acc >= 0 ? "+" : "")}{acc:F1} M/S²",
            20, 72, LABEL, Tint(0.6f));

        // Aerodynamics: angle of attack and wing loading.
        float aoa = a.AngleOfAttack * 180 / Mathf.Pi;
        Label($"AOA {aoa:F1}°   {a.GLoad:F1} G", 20, 88, LABEL,
            a.IsStalled ? new Color(1f, 0.54f, 0.50f) : Tint(0.6f));

        // Surface under the wheels.
        if (a.IsGrounded || a.IsCrashed)
        {
            string kind = a.Surface.Kind.ToString().ToUpperInvariant();
            Color surfaceTint = a.Surface.Kind == WorldSim.SurfaceKind.Rough
                ? new Color(1f, 0.70f, 0.42f) : Tint(0.6f);
            Label($"SURFACE {kind}", 20, 104, LABEL, surfaceTint);
        }

        // Altitude, top right.
        Label("ALTITUDE", W - 60, 20, LABEL, Tint(0.55f));
        Label(Mathf.RoundToInt(a.AltitudeAgl).ToString(), W - 60, 34, VALUE, new Color(0.92f, 0.97f, 1f));
        Label("M AGL", W - 60, 71, LABEL, Tint(0.6f));
        float vs = a.VerticalSpeed;
        Label($"VS {(vs >= 0 ? "+" : "")}{Mathf.RoundToInt(vs)} M/S", W - 60, 87, LABEL, Tint(0.6f));

        // Heading, bottom right.
        Label("HDG", W - 60, H - 46, LABEL, Tint(0.55f));
        Label(Mathf.RoundToInt(a.Heading).ToString("000") + "°", W - 60, H - 32, 18, new Color(0.92f, 0.97f, 1f));

        // Throttle, bottom left: the faint bar is the lever; the bright one is
        // what the engine has actually spooled up to.
        Label("THR", 22, H - 46, LABEL, Tint(0.55f));
        float barX = 22, barY = H - 30, barW = 120, barH = 8;
        DrawRect(new Rect2(barX, barY, barW, barH), Tint(0.18f));
        DrawRect(new Rect2(barX, barY, barW * a.Throttle, barH), Tint(0.30f));
        DrawRect(new Rect2(barX, barY, barW * a.Engine, barH), new Color(0.50f, 0.88f, 0.66f));
        Label(Mathf.RoundToInt(a.Engine * 100) + "%", barX + barW + 10, H - 46, LABEL, Tint(0.75f));

        DrawGear();
        DrawRunwayGuidance();

        // Gun-sight.
        float cx = W / 2, cy = H / 2;
        var sight = new Color(0.75f, 0.94f, 1f, 0.75f);
        DrawLine(new Vector2(cx - 26, cy), new Vector2(cx - 8, cy), sight, 1.5f);
        DrawLine(new Vector2(cx + 8, cy), new Vector2(cx + 26, cy), sight, 1.5f);
        DrawLine(new Vector2(cx, cy - 12), new Vector2(cx, cy - 5), sight, 1.5f);

        // Stall warning.
        if (a.IsStalled)
        {
            float blink = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(a.Elapsed * 7));
            LabelCentre("STALL", cx, cy + 60, BOLD, new Color(1f, 0.35f, 0.31f, blink));
        }

        // Touchdown banner.
        if (a.IsGrounded && a.LastLanding != null)
        {
            float since = a.Elapsed - a.LandedAt;
            if (since < 4)
            {
                float alpha = since < 2.5f ? 1 : 1 - (since - 2.5f) / 1.5f;
                string surface = a.LastLanding.Surface.ToString().ToUpperInvariant();
                bool firm = a.LastLanding.Firm;

                string title = firm
                    ? "FIRM LANDING"
                    : surface == "RUNWAY"
                        ? "LANDED"
                        : $"LANDED ON {surface}";

                Color banner = firm
                    ? new Color(0.94f, 0.78f, 0.38f, alpha)
                    : new Color(0.37f, 0.94f, 0.54f, alpha);
                LabelCentre(title, cx, cy - 64, BOLD, banner);
            }
            LabelCentre($"X to brake      S to rotate above {Mathf.RoundToInt(Config.ROTATE_SPEED * 3.6f)} km/h",
                cx, cy + 68, SMALL, Tint(0.5f));
        }

        // Controls hint, fades out after a few seconds.
        if (a.Elapsed < 14 && !a.IsCrashed)
        {
            float alpha = a.Elapsed < 11 ? 1 : 1 - (a.Elapsed - 11) / 3;
            var hint = new Color(0.82f, 0.94f, 1f, alpha * 0.55f);
            LabelCentre("W / S  pitch  (S = nose up)      A / D  roll      Q / E  rudder", W / 2, H - 84, SMALL, hint);
            LabelCentre("SHIFT  throttle up      X  throttle / brake      G  gear      C  view      R  restart", W / 2, H - 68, SMALL, hint);
        }

        // Crash.
        if (a.IsCrashed)
        {
            DrawRect(new Rect2(0, 0, W, H), new Color(0.04f, 0.08f, 0.13f, 0.6f));

            LabelCentre("CRASHED", cx, H / 2 - 46, BIG, new Color(1f, 0.42f, 0.38f));
            LabelCentre(a.CrashReason, cx, H / 2 - 6, BOLD, new Color(1f, 0.62f, 0.58f));
            LabelCentre("press  R  to fly again", cx, H / 2 + 26, 12, new Color(0.86f, 0.94f, 1f, 0.75f));

            Aircraft.ImpactRecord impact = a.CrashImpact;
            if (impact != null)
            {
                string sink = impact.Sink > 0.05f ? $"  ·  {impact.Sink:F1} m/s" : "";
                LabelCentre(
                    $"impact {Mathf.RoundToInt(impact.Speed * 3.6f)} km/h{sink}",
                    cx, H / 2 + 50, SMALL, new Color(0.86f, 0.94f, 1f, 0.45f));
                if (impact.Surface != WorldSim.SurfaceKind.Runway)
                    LabelCentre("on " + impact.Surface.ToString().ToUpperInvariant(),
                        cx, H / 2 + 66, SMALL, new Color(0.86f, 0.94f, 1f, 0.45f));
            }
        }
    }
}
