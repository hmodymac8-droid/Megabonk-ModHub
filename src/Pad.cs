using System.Collections.Generic;
using Rewired;
using UnityEngine;

namespace ModHub;

internal enum PadKey { A, B, LB, RB, L3, R3, Up, Down, Left, Right, Count }

/// <summary>
/// Reads controllers straight from Rewired (the game's input library), not through the game's own actions, so it
/// keeps working while the Hub pauses the game's controls. Buttons are found by name, so Xbox, PlayStation and
/// Steam Deck pads all work. If no pad has known names, Unity's own joystick buttons (Xbox layout) are used.
/// </summary>
internal static class Pad
{
    private static readonly string[][] Names =
    {
        new[] { "A", "Cross" },
        new[] { "B", "Circle" },
        new[] { "Left Shoulder", "L1", "Left Bumper" },
        new[] { "Right Shoulder", "R1", "Right Bumper" },
        new[] { "Left Stick Button", "L3" },
        new[] { "Right Stick Button", "R3" },
        new[] { "D-Pad Up" },
        new[] { "D-Pad Down" },
        new[] { "D-Pad Left" },
        new[] { "D-Pad Right" },
    };
    private static readonly int[] UnityButton = { 0, 1, 4, 5, 8, 9, -1, -1, -1, -1 }; // Xbox layout

    private static readonly bool[] Now = new bool[(int)PadKey.Count], Prev = new bool[(int)PadKey.Count];
    private static readonly Dictionary<int, int[]> ButtonMaps = new();   // joystick id -> button index per PadKey
    private static readonly Dictionary<int, int[]> StickMaps = new();    // joystick id -> axis index: LX, LY, RX, RY
    private static bool _unityBroken;

    public static Vector2 LeftStick { get; private set; }
    public static Vector2 RightStick { get; private set; }  // up is +y
    public static bool Connected { get; private set; }

    public static bool Down(PadKey k) => Now[(int)k] && !Prev[(int)k];
    public static bool Held(PadKey k) => Now[(int)k];
    public static bool AnyHeld => System.Array.IndexOf(Now, true) >= 0;

    /// <summary>Once per frame, from Update.</summary>
    public static void Poll()
    {
        for (int i = 0; i < Now.Length; i++) { Prev[i] = Now[i]; Now[i] = false; }
        Vector2 left = default, right = default;
        bool named = false;
        Connected = false;
        if (ReInput.isReady)
        {
            foreach (var j in ReInput.controllers.GetJoysticks())
            {
                if (j == null || !j.isConnected) continue;
                Connected = true;
                var map = ButtonMap(j);
                for (int k = 0; k < map.Length; k++)
                    if (map[k] >= 0) { named = true; if (j.GetButton(map[k])) Now[k] = true; }
                var sticks = StickMap(j);
                var l = new Vector2(Axis(j, sticks[0]), Axis(j, sticks[1]));
                var r = new Vector2(Axis(j, sticks[2]), Axis(j, sticks[3]));
                if (l.sqrMagnitude > left.sqrMagnitude) left = l;
                if (r.sqrMagnitude > right.sqrMagnitude) right = r;
            }
        }
        if (!named && !_unityBroken)
        {
            try
            {
                for (int k = 0; k < UnityButton.Length; k++)
                    if (UnityButton[k] >= 0 && Input.GetKey(KeyCode.JoystickButton0 + UnityButton[k])) Now[k] = true;
            }
            catch { _unityBroken = true; }
        }
        LeftStick = left;
        RightStick = right;
    }

    private static float Axis(Joystick j, int index) => index >= 0 ? j.GetAxis(index) : 0f;

    private static int[] ButtonMap(Joystick j)
    {
        if (ButtonMaps.TryGetValue(j.id, out var map)) return map;
        map = new int[Names.Length];
        for (int k = 0; k < map.Length; k++) map[k] = -1;
        var buttons = j.Buttons;
        for (int i = 0; i < j.buttonCount; i++)
        {
            string name;
            try { name = buttons[i].elementIdentifier.name; } catch { continue; }
            for (int k = 0; k < Names.Length; k++)
                if (map[k] < 0 && System.Array.IndexOf(Names[k], name) >= 0) map[k] = i;
        }
        return ButtonMaps[j.id] = map;
    }

    private static int[] StickMap(Joystick j)
    {
        if (StickMaps.TryGetValue(j.id, out var map)) return map;
        string[] want = { "Left Stick X", "Left Stick Y", "Right Stick X", "Right Stick Y" };
        map = new[] { -1, -1, -1, -1 };
        var axes = j.Axes;
        for (int i = 0; i < j.axisCount; i++)
        {
            string name;
            try { name = axes[i].elementIdentifier.name; } catch { continue; }
            int k = System.Array.IndexOf(want, name);
            if (k >= 0) map[k] = i;
        }
        return StickMaps[j.id] = map;
    }
}
