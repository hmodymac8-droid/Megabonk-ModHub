using System;
using UnityEngine;

namespace ModHub;

/// <summary>
/// The Hub's look: rounded cards and buttons, a soft dark palette, colored ON/OFF pills.
/// Rounded corners are drawn from four small generated corner images plus plain rectangles, because this game's
/// build strips GUI style borders (9-slicing) and texture-region drawing.
/// </summary>
internal static partial class Panel
{
    // Palette: filled in by ApplyTheme.
    private static Color PanelBg, CardBg, CardHover, SecondaryBtn, SecondaryHover, PrimaryHover, OnPill, OnPillHover,
        OffPill, OffPillHover, DarkText, LightText, BorderCol, SearchBg, SearchBgFocus, SecondaryFlat;
    private static Color InkCol, BandA, BandB;
    private static bool Chunky, _titleFontTried;
    private static Texture2D _band;
    private static Font _titleFont;
    private static float CardRadius = 10f, ButtonRadius = 7f, PanelRadius = 13f, BorderThickness = 1.5f;

    internal static readonly string[] ThemeNames = { "Classic", "Ember", "Moss", "Abyss", "Plum", "Iron", "Crimson", "Flat" };
    private static int _themeIndex = -1;

    private static Color C(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);

    private static void ApplyTheme(int i)
    {
        if (i == _themeIndex) return;
        _themeIndex = i;
        Chunky = false;
        Green = C(0.45f, 0.9f, 0.5f); Red = C(1f, 0.45f, 0.4f); Blue = C(0.45f, 0.75f, 1f);
        if (_band != null) { UnityEngine.Object.Destroy(_band); _band = null; }
        // The text styles hold colors from the old palette, so rebuild them (and the corner textures) next frame.
        _stylesReady = false;
        _cTL = null;
        switch (i)
        {
            case 0: SetChunky(0x262b35, 0x5c4f26, 0x13161b, 0xffd140, 0x303540, 0x339e5c, 0x5c3133, 0xebeef5, 0xadb3bf); return; // Classic: the original colors
            case 1: SetChunky(0x7a3a1c, 0xa8702c, 0x261510, 0xd9a441, 0x4a2c1c, 0x6f8f3a, 0x55403a, 0xf3e7d6, 0xb3a08c); return; // Ember
            case 2: SetChunky(0x2c4f37, 0x65753a, 0x131d17, 0xb9c46a, 0x2c3f30, 0x5f8f4d, 0x3f4a42, 0xe9efdf, 0x9fae9f); return; // Moss
            case 3: SetChunky(0x1b3a5c, 0x2a6c78, 0x0f1825, 0x7cc4cf, 0x22384d, 0x3f8f8a, 0x39465a, 0xe5f0f5, 0x93a9bb); return; // Abyss
            case 4: SetChunky(0x432660, 0x7c3a60, 0x1a1224, 0xd6a0c4, 0x372749, 0x5f8f80, 0x4a3a55, 0xf0e6f2, 0xaa9cb6); return; // Plum
            case 5: SetChunky(0x2e3138, 0x565c66, 0x141619, 0xc9cdd4, 0x2b2e34, 0x5f8a6a, 0x45474d, 0xeaeaec, 0xa0a3a9); return; // Iron
            case 6: SetChunky(0x55202a, 0x84392a, 0x1c1012, 0xd98a6a, 0x41222a, 0x6e8a4f, 0x4a3a3a, 0xf2e4df, 0xb09a98); return; // Crimson
            default: // Flat: the original look, before the chunky design
                PanelBg = C(0.075f, 0.085f, 0.105f, 0.97f); Bg = C(0.07f, 0.08f, 0.1f, 0.96f); Gold = C(1f, 0.82f, 0.25f); Grey = C(0.68f, 0.7f, 0.75f);
                BorderCol = C(1f, 0.82f, 0.25f, 0.55f); BorderThickness = 1.5f;
                CardBg = C(1f, 1f, 1f, 0.045f); CardHover = C(1f, 1f, 1f, 0.075f);
                SecondaryBtn = C(0.19f, 0.21f, 0.25f); SecondaryHover = C(0.24f, 0.27f, 0.32f); SecondaryFlat = C(1f, 1f, 1f, 0.1f);
                PrimaryHover = C(1f, 0.88f, 0.45f);
                OnPill = C(0.2f, 0.62f, 0.36f); OnPillHover = C(0.25f, 0.7f, 0.42f);
                OffPill = C(0.36f, 0.19f, 0.2f); OffPillHover = C(0.44f, 0.23f, 0.24f);
                DarkText = C(0.1f, 0.09f, 0.07f); LightText = C(0.92f, 0.93f, 0.96f);
                SearchBg = C(0.1f, 0.115f, 0.14f); SearchBgFocus = C(0.13f, 0.15f, 0.18f);
                CardRadius = 10f; ButtonRadius = 7f; PanelRadius = 13f;
                break;
        }
    }

    private static Color Hex(int rgb, float a = 1f) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);

    /// <summary>The chunky look: header band, ink outlines, hard button shadows. Only the colors differ between themes.</summary>
    private static void SetChunky(int bandA, int bandB, int panel, int accent, int button, int on, int off, int text, int grey)
    {
        Chunky = true;
        BandA = Hex(bandA); BandB = Hex(bandB);
        PanelBg = Hex(panel, 0.98f); Bg = PanelBg; Gold = Hex(accent); Grey = Hex(grey); LightText = Hex(text);
        var p = Hex(panel); var b = Hex(button);
        InkCol = Color.Lerp(p, Color.black, 0.8f);
        BorderCol = InkCol; BorderThickness = 4f;
        CardBg = Color.Lerp(p, b, 0.45f); CardHover = Color.Lerp(p, b, 0.7f);
        SecondaryBtn = b; SecondaryFlat = b; SecondaryHover = Color.Lerp(b, Color.white, 0.1f);
        PrimaryHover = Color.Lerp(Gold, Color.white, 0.2f);
        OnPill = Hex(on); OnPillHover = Color.Lerp(OnPill, Color.white, 0.12f);
        OffPill = Hex(off); OffPillHover = Color.Lerp(OffPill, Color.white, 0.1f);
        DarkText = Color.Lerp(p, Color.black, 0.7f);
        SearchBg = Color.Lerp(p, Color.black, 0.35f); SearchBgFocus = Color.Lerp(p, b, 0.4f);
        Green = Color.Lerp(OnPill, Color.white, 0.35f); Red = Color.Lerp(OffPill, C(1f, 0.5f, 0.45f), 0.7f); Blue = Gold;
        CardRadius = 11f; ButtonRadius = 9f; PanelRadius = 14f;
    }

    /// <summary>The colored strip behind the title, rounded to fit the panel's top corners.</summary>
    private static void HeaderBand()
    {
        const int BW = 860, BH = 47;
        if (_band == null)
        {
            _band = new Texture2D(BW, BH, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float rad = PanelRadius;
            for (int y = 0; y < BH; y++)
                for (int x = 0; x < BW; x++)
                {
                    float top = BH - y - 0.5f, side = Mathf.Min(x + 0.5f, BW - x - 0.5f), a = 1f;
                    if (top < rad && side < rad)
                    {
                        float dx = rad - side, dy = rad - top;
                        a = Mathf.Clamp01(rad - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    }
                    var c = Color.Lerp(BandA, BandB, x / (BW - 1f)); c.a = a;
                    _band.SetPixel(x, y, c);
                }
            _band.Apply();
        }
        GUI.DrawTexture(new Rect(_panel.x, _panel.y, W, BH), _band);
        Fill(new Rect(_panel.x, _panel.y + BH, W, 3f), InkCol);
    }

    /// <summary>"[F4] or [ESC] to close" as little key caps, ending at the right edge of r.</summary>
    private static void KeyHint(Rect r, string key)
    {
        float rx = r.xMax;
        void Word(string t)
        {
            float tw = _small.CalcSize(new GUIContent(t)).x;
            rx -= tw;
            Label(new Rect(rx, r.y, tw + 2f, 24f), t, _small, LightText, left: true);
            rx -= 6f;
        }
        void Cap(string t)
        {
            float tw = _badge.CalcSize(new GUIContent(t)).x + 12f;
            rx -= tw;
            var box = new Rect(rx, r.y + 2f, tw, 20f);
            Card(box, InkCol, 6f);
            Card(new Rect(box.x + 1.5f, box.y + 1.5f, box.width - 3f, box.height - 3f), SecondaryBtn, 5f);
            Label(new Rect(box.x + 6f, box.y + 2f, tw, 18f), t, _badge, LightText);
            rx -= 6f;
        }
        Word("to close"); Cap("ESC"); Word("or"); Cap(key);
    }

    private static void ChunkyBase(Rect r)
    {
        Card(new Rect(r.x - 1.5f, r.y + 1.5f, r.width + 3f, r.height + 3f), InkCol, ButtonRadius + 1.5f);
        Card(new Rect(r.x - 1.5f, r.y - 1.5f, r.width + 3f, r.height + 3f), InkCol, ButtonRadius + 1.5f);
    }

    /// <summary>A notice box: tinted normally, outlined in the accent color on the chunky look.</summary>
    private static void BannerBox(Rect r, Color tint)
    {
        if (Chunky) BorderedCard(r, PanelBg, Gold, CardRadius, 2f); else Card(r, tint);
    }

    private static Font TitleFont()
    {
        if (_titleFontTried) return _titleFont;
        _titleFontTried = true;
        try { _titleFont = Font.CreateDynamicFontFromOSFont("Impact", 26); }
        catch (Exception e) { LogOnce("Title font", e); }
        return _titleFont;
    }

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


    // Small button icons drawn from shapes in code (no files); each is a white shape tinted when drawn.
    private const int IconTrash = 0, IconRestart = 1, IconClock = 2;
    private static readonly Texture2D[] _icons = new Texture2D[3];

    private static float SegCov(float x, float y, float x1, float y1, float x2, float y2, float half)
    {
        float dx = x2 - x1, dy = y2 - y1, t = Mathf.Clamp01(((x - x1) * dx + (y - y1) * dy) / (dx * dx + dy * dy));
        float ex = x - x1 - t * dx, ey = y - y1 - t * dy;
        return Mathf.Clamp01(half + 0.5f - Mathf.Sqrt(ex * ex + ey * ey));
    }

    private static Texture2D MakeIcon(int kind)
    {
        const int N = 32;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int py = 0; py < N; py++)
            for (int px = 0; px < N; px++)
            {
                float x = px + 0.5f, y = py + 0.5f, a;
                float ring = Mathf.Clamp01(2.3f - Mathf.Abs(Mathf.Sqrt((x - 16f) * (x - 16f) + (y - 16f) * (y - 16f)) - 10f));
                if (kind == IconTrash)
                {
                    a = Mathf.Max(SegCov(x, y, 6f, 24f, 26f, 24f, 1.6f), SegCov(x, y, 13f, 27f, 19f, 27f, 1.6f), SegCov(x, y, 9f, 21f, 10.5f, 6f, 1.6f));
                    a = Mathf.Max(a, SegCov(x, y, 23f, 21f, 21.5f, 6f, 1.6f), SegCov(x, y, 10.5f, 6f, 21.5f, 6f, 1.6f));
                }
                else if (kind == IconRestart)
                {
                    float deg = Mathf.Atan2(y - 16f, x - 16f) * Mathf.Rad2Deg;
                    a = deg > 30f && deg < 75f ? 0f : ring;
                    a = Mathf.Max(a, SegCov(x, y, 18.6f, 25.7f, 24f, 28.5f, 1.6f), SegCov(x, y, 18.6f, 25.7f, 23.5f, 21f, 1.6f));
                }
                else a = Mathf.Max(ring, SegCov(x, y, 16f, 16f, 16f, 23f, 1.6f), SegCov(x, y, 16f, 16f, 21f, 13f, 1.6f));
                tex.SetPixel(px, py, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return tex;
    }

    private static void IconAt(int kind, Rect r, Color tint)
    {
        if (_icons[kind] == null) _icons[kind] = MakeIcon(kind);
        var old = GUI.color; GUI.color = tint; GUI.DrawTexture(r, _icons[kind]); GUI.color = old;
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
    private static void Card(Rect r, Color c, float radius = -1f)
    {
        if (radius < 0f) radius = CardRadius;
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
    private static void BorderedCard(Rect r, Color fill, Color border, float radius = -1f, float thickness = -1f)
    {
        if (radius < 0f) radius = CardRadius;
        if (thickness < 0f) thickness = BorderThickness;
        Card(r, border, radius);
        Card(new Rect(r.x + thickness, r.y + thickness, r.width - 2f * thickness, r.height - 2f * thickness), fill, Mathf.Max(0f, radius - thickness));
    }

    private static bool Hovering(Rect r) => Event.current != null && r.Contains(Event.current.mousePosition);

    /// <summary>Draws a button's rounded background and returns the style to draw its text with.</summary>
    private static GUIStyle ButtonLook(Rect r, GUIStyle kind)
    {
        EnsureTheme();
        bool hover = Hovering(r);
        if (Chunky) ChunkyBase(r);
        if (kind == _buttonOn) { Card(r, hover ? PrimaryHover : Gold, ButtonRadius); return _textDark; }
        if (kind == _pillOn) { Card(r, hover ? OnPillHover : OnPill, ButtonRadius); return _pillOn; }
        if (kind == _pillOff) { Card(r, hover ? OffPillHover : OffPill, ButtonRadius); return _pillOff; }
        if (kind == _segment)
        {
            if (Chunky) { Card(r, hover ? PrimaryHover : Gold, ButtonRadius); return _textDark; }
            // Selected tab / filter: lighter card, gold text, gold underline.
            Card(r, hover ? SecondaryHover : SecondaryBtn, ButtonRadius);
            Fill(new Rect(r.x + 10f, r.yMax - 3f, r.width - 20f, 3f), Gold);
            _segment.normal.textColor = Gold;
            return _segment;
        }
        // Secondary (everything else): flat dark grey that lights up under the mouse.
        Card(r, hover ? SecondaryHover : SecondaryFlat, ButtonRadius); // a touch lighter than a row card
        return _textLight;
    }
}
