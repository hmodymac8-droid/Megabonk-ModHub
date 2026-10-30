using UnityEngine;

namespace ModHub;

/// <summary>
/// The Hub's look: rounded cards and buttons, a soft dark palette, colored ON/OFF pills.
/// Rounded corners are drawn from four small generated corner images plus plain rectangles, because this game's
/// build strips GUI style borders (9-slicing) and texture-region drawing.
/// </summary>
internal static partial class Panel
{
    // Palette
    private static readonly Color PanelBg = new(0.075f, 0.085f, 0.105f, 0.97f);
    private static readonly Color CardBg = new(1f, 1f, 1f, 0.045f);
    private static readonly Color CardHover = new(1f, 1f, 1f, 0.075f);
    private static readonly Color SecondaryBtn = new(0.19f, 0.21f, 0.25f, 1f);
    private static readonly Color SecondaryHover = new(0.24f, 0.27f, 0.32f, 1f);
    private static readonly Color PrimaryHover = new(1f, 0.88f, 0.45f, 1f);
    private static readonly Color OnPill = new(0.2f, 0.62f, 0.36f, 1f);
    private static readonly Color OnPillHover = new(0.25f, 0.7f, 0.42f, 1f);
    private static readonly Color OffPill = new(0.36f, 0.19f, 0.2f, 1f);
    private static readonly Color OffPillHover = new(0.44f, 0.23f, 0.24f, 1f);
    private static readonly Color DarkText = new(0.1f, 0.09f, 0.07f, 1f);
    private static readonly Color LightText = new(0.92f, 0.93f, 0.96f, 1f);

    private const float CardRadius = 10f, ButtonRadius = 7f;
    private static Texture2D _cTL, _cTR, _cBL, _cBR;
    private static GUIStyle _pillOn, _pillOff, _segment, _textDark, _textLight;

    private static void EnsureTheme()
    {
        if (_cTL != null) return;
        const int R = 32;
        _cTL = Corner(R, R, 0f);  // circle centre at the corner's inner side (texture y=0 is the bottom row)
        _cTR = Corner(R, 0f, 0f);
        _cBL = Corner(R, R, R);
        _cBR = Corner(R, 0f, R);

        _pillOn = Make(GUI.skin.label, 14, FontStyle.Bold);
        _pillOff = Make(GUI.skin.label, 14, FontStyle.Bold);
        _segment = Make(GUI.skin.label, 14, FontStyle.Bold);
        _textDark = Make(GUI.skin.label, 14, FontStyle.Bold);
        _textLight = Make(GUI.skin.label, 14, FontStyle.Bold);
        foreach (var s in new[] { _pillOn, _pillOff, _segment, _textDark, _textLight }) s.alignment = TextAnchor.MiddleCenter;
        _textDark.normal.textColor = DarkText;
        _textLight.normal.textColor = LightText;
        _pillOn.normal.textColor = Color.white;
        _pillOff.normal.textColor = new Color(1f, 0.78f, 0.78f);
    }

    /// <summary>A white quarter circle (smooth edge) whose centre is at (cx, cy) in texture pixels.</summary>
    private static Texture2D Corner(int size, float cx, float cy)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float a = Mathf.Clamp01(size - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return tex;
    }

    /// <summary>A filled rectangle with rounded corners.</summary>
    private static void Card(Rect r, Color c, float radius = CardRadius)
    {
        if (r.width <= 0f || r.height <= 0f) return;
        EnsureTheme();
        float rad = Mathf.Min(radius, r.width / 2f, r.height / 2f);
        // The Hub is drawn scaled; snap every edge to a whole screen pixel so the pieces meet without hairline gaps.
        float s = Mathf.Max(0.01f, GUI.matrix.m00);
        float Snap(float v) => Mathf.Round(v * s) / s;
        float x0 = Snap(r.x), x1 = Snap(r.x + rad), x2 = Snap(r.xMax - rad), x3 = Snap(r.xMax);
        float y0 = Snap(r.y), y1 = Snap(r.y + rad), y2 = Snap(r.yMax - rad), y3 = Snap(r.yMax);
        // Make the color solid (mixed over the panel background, so it looks the same) and let the middle piece
        // overlap its neighbours by a pixel: with see-through colors any seam showed as a thin dark line.
        if (c.a < 1f) c = new Color(PanelBg.r + (c.r - PanelBg.r) * c.a, PanelBg.g + (c.g - PanelBg.g) * c.a, PanelBg.b + (c.b - PanelBg.b) * c.a, 1f);
        float px = 1f / s;
        var old = GUI.color; GUI.color = c;
        GUI.DrawTexture(new Rect(x1 - px, y0, x2 - x1 + 2f * px, y3 - y0), _white);
        GUI.DrawTexture(new Rect(x0, y1, x1 - x0, y2 - y1), _white);
        GUI.DrawTexture(new Rect(x2, y1, x3 - x2, y2 - y1), _white);
        GUI.DrawTexture(new Rect(x0, y0, x1 - x0, y1 - y0), _cTL);
        GUI.DrawTexture(new Rect(x2, y0, x3 - x2, y1 - y0), _cTR);
        GUI.DrawTexture(new Rect(x0, y2, x1 - x0, y3 - y2), _cBL);
        GUI.DrawTexture(new Rect(x2, y2, x3 - x2, y3 - y2), _cBR);
        GUI.color = old;
    }

    /// <summary>A rounded card with a thin border (fill should be opaque, or the border shows through).</summary>
    private static void BorderedCard(Rect r, Color fill, Color border, float radius = CardRadius, float thickness = 1.5f)
    {
        Card(r, border, radius);
        Card(new Rect(r.x + thickness, r.y + thickness, r.width - 2f * thickness, r.height - 2f * thickness), fill, Mathf.Max(0f, radius - thickness));
    }

    private static bool Hovering(Rect r) => Event.current != null && r.Contains(Event.current.mousePosition);

    /// <summary>Draws a button's rounded background and returns the style to draw its text with.</summary>
    private static GUIStyle ButtonLook(Rect r, GUIStyle kind)
    {
        EnsureTheme();
        bool hover = Hovering(r);
        if (kind == _buttonOn) { Card(r, hover ? PrimaryHover : Gold, ButtonRadius); return _textDark; }
        if (kind == _pillOn) { Card(r, hover ? OnPillHover : OnPill, ButtonRadius); return _pillOn; }
        if (kind == _pillOff) { Card(r, hover ? OffPillHover : OffPill, ButtonRadius); return _pillOff; }
        if (kind == _segment)
        {
            // Selected tab / filter: lighter card, gold text, gold underline.
            Card(r, hover ? SecondaryHover : SecondaryBtn, ButtonRadius);
            Fill(new Rect(r.x + 10f, r.yMax - 3f, r.width - 20f, 3f), Gold);
            _segment.normal.textColor = Gold;
            return _segment;
        }
        // Secondary (everything else): flat dark grey that lights up under the mouse.
        Card(r, hover ? SecondaryHover : new Color(1f, 1f, 1f, 0.1f), ButtonRadius); // a touch lighter than a row card
        return _textLight;
    }
}
