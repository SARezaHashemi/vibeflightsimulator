using Godot;

/**
 * Keyboard input, reduced to four normalised axes. Port of js/input.js.
 * Pitch follows stick convention: pull back (S / ↓) raises the nose.
 */
public class InputRig
{
    private readonly System.Collections.Generic.HashSet<Key> _down = new();
    private readonly System.Collections.Generic.HashSet<Key> _wasDown = new();

    /** Called once per physics tick to refresh held keys and transitions. */
    public void Update()
    {
        _wasDown.Clear();
        _wasDown.UnionWith(_down);

        _down.Clear();
        Capture(Key.W); Capture(Key.Up); Capture(Key.S); Capture(Key.Down);
        Capture(Key.A); Capture(Key.Left); Capture(Key.D); Capture(Key.Right);
        Capture(Key.Q); Capture(Key.E);
        Capture(Key.X); Capture(Key.Shift);
        Capture(Key.G); Capture(Key.R); Capture(Key.C);
    }

    private void Capture(Key key)
    {
        if (Input.IsPhysicalKeyPressed(key)) _down.Add(key);
    }

    private bool Held(Key key) => _down.Contains(key);

    private bool JustPressed(Key key) => _down.Contains(key) && !_wasDown.Contains(key);

    public Aircraft.Axes Axes => new()
    {
        Pitch = Read(Key.W, Key.Up, Key.S, Key.Down),
        Roll = Read(Key.A, Key.Left, Key.D, Key.Right),
        Yaw = Read(Key.Q, Key.None, Key.E, Key.None),
        Throttle = Read(Key.X, Key.None, Key.Shift, Key.None),
    };

    private float Read(Key negative, Key negativeAlt, Key positive, Key positiveAlt)
    {
        float value = 0;
        if (negative != Key.None && Held(negative)) value -= 1;
        if (negativeAlt != Key.None && Held(negativeAlt)) value -= 1;
        if (positive != Key.None && Held(positive)) value += 1;
        if (positiveAlt != Key.None && Held(positiveAlt)) value += 1;
        return value;
    }

    // Commands (as opposed to held controls) are reported as key events.

    public bool ResetRequested => JustPressed(Key.R);
    public bool GearRequested => JustPressed(Key.G);
    public bool ViewRequested => JustPressed(Key.C);
}
