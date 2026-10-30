using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModHub;

/// <summary>
/// Controller (and arrow-key) control of the Hub. Every button registers its place on screen while it's drawn; the
/// D-pad or left stick moves a gold frame to the nearest button in that direction, A presses it, B goes back,
/// LB / RB switch tabs, the right stick scrolls, and L3 + R3 together open or close the Hub.
/// Moving the mouse hides the frame again.
/// </summary>
internal static partial class Panel
{
    private struct ScrollArea
    {
        public Rect Rect;
        public bool Up, Down;      // can scroll that way
        public bool ByMoving;      // moving the frame past its edge scrolls it (lists); otherwise only the right stick
        public Action<int> Scroll; // +1 down, -1 up
    }

    private static bool _padMode;                  // show the gold frame
    private static Rect? _focus;
    private static List<Rect> _navLast = new(), _navNow = new();
    private static List<ScrollArea> _areasLast = new(), _areasNow = new();
    private static readonly Rect[] _tabRects = new Rect[3];
    private static bool _padPress;                 // A was pressed: the focused button fires on the next draw
    private static Vector2 _lastMouse;
    private static float _openedAt;
    private static bool _guardUntilPadUp;          // closed with the pad: give the game its controls back once buttons are up
    private static Vector2Int _heldDir;
    private static float _nextMove, _nextScroll;
    private static float _navX;                    // the column up / down keeps to

    /// <summary>Every frame from Update (also while the Hub is closed, to catch L3 + R3).</summary>
    private static void PadTick()
    {
        Pad.Poll();
        if (_guardUntilPadUp && (Open || !Pad.AnyHeld))
        {
            _guardUntilPadUp = false;
            if (!Open) InputGuard.Update(false, false);
        }
        if (!_pickingKey && Pad.Held(PadKey.L3) && Pad.Held(PadKey.R3) && (Pad.Down(PadKey.L3) || Pad.Down(PadKey.R3)))
        {
            Toggle();
            if (Open) { _padMode = true; _focus = null; }
            return;
        }

        // Moving the mouse hides the frame (not the jump when the Hub frees the cursor, or camera turns while closed).
        var mouse = (Vector2)Input.mousePosition;
        if (Open && Time.unscaledTime > _openedAt + 0.5f && (mouse - _lastMouse).sqrMagnitude > 9f) _padMode = false;
        _lastMouse = mouse;
        if (!Open) { _padMode = false; return; }

        bool typing = _searchFocused || TypingInBox;
        bool keys = !typing && !_pickingKey;
        if (Pad.Down(PadKey.B))
        {
            _padMode = true;
            if (_searchFocused) _searchFocused = false;
            else if (TypingInBox) { _typing.Focused = false; _typing = null; }
            else if (_confirmRemove != null && Time.unscaledTime < _confirmUntil) _confirmRemove = null;
            else if (!CloseDetails()) Toggle();
            return;
        }
        if (Pad.Down(PadKey.LB) || Pad.Down(PadKey.RB))
        {
            _padMode = true;
            SwitchTab((_tab + (Pad.Down(PadKey.RB) ? 1 : 2)) % 3);
            _focus = _tabRects[_tab];
            return;
        }
        if (Pad.Down(PadKey.A) || (keys && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))))
        {
            if (!_padMode || _focus == null) { _padMode = true; _focus ??= _tabRects[_tab]; }
            else _padPress = true;
            return;
        }

        // Direction: D-pad, left stick or arrow keys. Held = repeats after a short pause.
        var dir = Vector2Int.zero;
        if (Pad.Held(PadKey.Up) || Pad.LeftStick.y > 0.5f || (keys && Input.GetKey(KeyCode.UpArrow))) dir.y = -1;
        else if (Pad.Held(PadKey.Down) || Pad.LeftStick.y < -0.5f || (keys && Input.GetKey(KeyCode.DownArrow))) dir.y = 1;
        else if (Pad.Held(PadKey.Left) || Pad.LeftStick.x < -0.5f || (keys && Input.GetKey(KeyCode.LeftArrow))) dir.x = -1;
        else if (Pad.Held(PadKey.Right) || Pad.LeftStick.x > 0.5f || (keys && Input.GetKey(KeyCode.RightArrow))) dir.x = 1;
        if (dir == Vector2Int.zero) _heldDir = dir;
        else if (dir != _heldDir || Time.unscaledTime >= _nextMove)
        {
            _nextMove = Time.unscaledTime + (dir != _heldDir ? 0.4f : 0.12f);
            _heldDir = dir;
            _padMode = true;
            Move(dir);
        }

        // Right stick: scroll the list or the description under the frame.
        float rs = Pad.RightStick.y;
        if (Mathf.Abs(rs) > 0.5f && Time.unscaledTime >= _nextScroll)
        {
            _nextScroll = Time.unscaledTime + 0.1f;
            _padMode = true;
            ScrollAt(_focus?.center, rs > 0 ? -1 : 1, onlyByMoving: false);
        }
    }

    private static void SwitchTab(int tab) { _tab = tab; _scroll = 0; _searchFocused = false; CloseDetails(); }

    private static void Move(Vector2Int dir)
    {
        if (_focus == null) { _focus = _tabRects[_tab]; return; }
        var f = _focus.Value;
        var from = f.center;
        Rect? best = null;
        if (dir.x != 0)
        {
            // Left / right: the nearest button in the same row.
            float bestDist = float.MaxValue;
            foreach (var r in _navLast)
            {
                if (Same(r, f)) continue;
                float along = (r.center.x - from.x) * dir.x;
                // Same row = one of the two covers the other's middle (a box just above the row doesn't count).
                bool sameRow = (from.y >= r.yMin && from.y <= r.yMax) || (r.center.y >= f.yMin && r.center.y <= f.yMax);
                if (along < 4f || !sameRow) continue;
                if (along < bestDist) { bestDist = along; best = r; }
            }
            if (best != null) _navX = best.Value.center.x;
        }
        else
        {
            // Up / down: always the very next row (never jump over one), and in it the button closest to the column
            // the frame has been travelling in.
            if (_navX < f.xMin || _navX > f.xMax) _navX = from.x;
            float nearest = float.MaxValue;
            foreach (var r in _navLast)
            {
                if (Same(r, f) || (r.center.y - from.y) * dir.y < 4f) continue;
                float gap = (r.center.y - from.y) * dir.y;
                nearest = Mathf.Min(nearest, gap);
            }
            float bestDist = float.MaxValue;
            foreach (var r in _navLast)
            {
                if (Same(r, f) || (r.center.y - from.y) * dir.y < 4f) continue;
                float gap = (r.center.y - from.y) * dir.y;
                if (gap > nearest + 12f) continue; // same row: middles within a few pixels
                float dist = _navX < r.xMin ? r.xMin - _navX : _navX > r.xMax ? _navX - r.xMax : 0f;
                dist = dist * 1000f + Mathf.Abs(r.center.x - _navX); // inside the button beats near it
                if (dist < bestDist) { bestDist = dist; best = r; }
            }
        }
        // In a scrolling list, moving past the first or last row scrolls the list instead of leaving it.
        if (dir.y != 0 && ScrollAt(from, dir.y, onlyByMoving: true, unless: best)) return;
        if (best != null) { _focus = best; return; }
        if (dir.y != 0) ScrollAt(null, dir.y, onlyByMoving: false, emptyOnly: true); // nothing that way: scroll the description (details)
    }

    /// <summary>Scrolls the area at a point (or the first one that can). False when nothing scrolled.</summary>
    private static bool ScrollAt(Vector2? at, int dir, bool onlyByMoving, Rect? unless = null, bool emptyOnly = false)
    {
        foreach (var a in _areasLast)
        {
            if (at.HasValue && !a.Rect.Contains(at.Value)) continue;
            if (emptyOnly && _navLast.Exists(r => a.Rect.Contains(r.center))) continue; // has buttons: move to them instead
            if (onlyByMoving && !a.ByMoving) continue;
            if (!(dir > 0 ? a.Down : a.Up)) continue;
            if (unless.HasValue && a.Rect.Contains(unless.Value.center)) continue; // the next button is still inside
            a.Scroll(dir);
            return true;
        }
        return false;
    }

    private static bool Same(Rect a, Rect b) => Mathf.Abs(a.x - b.x) < 2f && Mathf.Abs(a.y - b.y) < 2f && Mathf.Abs(a.width - b.width) < 2f;

    /// <summary>
    /// Called for every pressable thing while it's drawn. True when the controller pressed it (A on the gold frame).
    /// </summary>
    private static bool NavItem(Rect r)
    {
        if (Event.current.type == EventType.Repaint) _navNow.Add(r);
        if (_padPress && _focus.HasValue && Same(_focus.Value, r)) { _padPress = false; return true; }
        return false;
    }

    private static void NavArea(Rect r, bool up, bool down, bool byMoving, Action<int> scroll)
    {
        if (Event.current.type == EventType.Repaint) _areasNow.Add(new ScrollArea { Rect = r, Up = up, Down = down, ByMoving = byMoving, Scroll = scroll });
    }

    /// <summary>End of each drawn frame: keep the frame on something that still exists, and draw it.</summary>
    private static void NavEndFrame(Event e)
    {
        if (e.type != EventType.Repaint) return;
        (_navLast, _navNow) = (_navNow, _navLast);
        (_areasLast, _areasNow) = (_areasNow, _areasLast);
        _navNow.Clear();
        _areasNow.Clear();
        _padPress = false; // a press that hit nothing must not fire later
        if (!_padMode) return;
        if (_focus == null || !_navLast.Exists(r => Same(r, _focus.Value)))
        {
            // The button went away (page changed, mod removed...): take the nearest one.
            var at = _focus?.center ?? _tabRects[_tab].center;
            Rect? near = null;
            float bd = float.MaxValue;
            foreach (var r in _navLast) { float d = (r.center - at).sqrMagnitude; if (d < bd) { bd = d; near = r; } }
            _focus = near;
        }
        if (_focus is Rect f)
        {
            var ring = new Rect(f.x - 3f, f.y - 3f, f.width + 6f, f.height + 6f);
            Outline(ring, Gold, 3f);
        }
    }


    private static string PadHint() => L.T(
        "[A] Select    [B] Back    [LB] [RB] Tabs    [Right stick] Scroll    [L3 + R3] Close",
        "[A] اختيار    [B] رجوع    [LB] [RB] التبويبات    [العصا اليمنى] تمرير    [L3 + R3] إغلاق");
}
