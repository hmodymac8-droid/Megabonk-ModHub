using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModHub;

/// <summary>
/// "Suggest / request": players send an idea for Mod Hub or ask for a mod. Nothing is sent from the game and no key
/// is built in: "Send on GitHub" opens a new GitHub issue on the Hub's repo (Online.HubRepo) in the browser with the text
/// filled in, and the player presses Submit there. Players without a GitHub account copy the text and post it in
/// the Nexus comments instead. Only the Hub and game versions are added, nothing personal.
/// </summary>
internal static partial class Panel
{
    private const string IssuesUrl = "https://github.com/" + Online.HubRepo + "/issues/new";
    private const string NexusPostsUrl = "https://www.nexusmods.com/megabonk/mods/194?tab=posts";

    private static bool _suggest;                   // the page is open
    private static bool _suggestRequest;            // true: "Request a mod", false: "Idea for Mod Hub"
    private static readonly TextBox _sgText = new() { Max = 800 };
    private static readonly TextBox _sgLink = new() { Max = 200 };

    private static void OpenSuggest(bool request)
    {
        _details = null;
        _whatsNew = null;
        _history = false;
        _suggest = true;
        _suggestRequest = request;
    }

    private static void DrawSuggest(Event e, Vector2 mouse, Rect body)
    {
        float x = body.x, y = body.y, w = body.width;
        if (Btn(new Rect(L.Arabic ? x + w - 110f : x, y, 110f, 30f), L.T("< Back", "رجوع >"), _button)) { CloseDetails(); return; }
        Label(new Rect(L.Arabic ? x : x + 124f, y + 2f, w - 124f, 28f), L.T("Suggest or request", "اقتراح أو طلب"), _title, Gold, right: L.Arabic);
        y += 42f;

        // What kind: an idea for the Hub itself, or a mod someone should make / the Hub should list.
        float half = (w - 8f) / 2f;
        var ideaRect = new Rect(L.Arabic ? x + half + 8f : x, y, half, 32f);
        var reqRect = new Rect(L.Arabic ? x : x + half + 8f, y, half, 32f);
        if (Btn(ideaRect, L.T("Idea for Mod Hub", "فكرة لـ Mod Hub"), !_suggestRequest ? _segment : _button)) _suggestRequest = false;
        if (Btn(reqRect, L.T("Request a mod", "اطلب مود"), _suggestRequest ? _segment : _button)) _suggestRequest = true;
        y += 44f;

        Label(new Rect(x, y, w, 22f), _suggestRequest
            ? L.T("What mod do you want? What should it do?", "وش المود اللي تبيه؟ وش يسوي؟")
            : L.T("What should Mod Hub do better, or do next?", "وش تبي Mod Hub يسوي أحسن، أو يضيف؟"), _row, Color.white, left: !L.Arabic, right: L.Arabic);
        y += 26f;
        DrawNoteBox(_sgText, new Rect(x, y, w, 150f), e, mouse, _suggestRequest
            ? L.T("Click here and type. Ctrl+V pastes.", "اضغط هنا واكتب. Ctrl+V للصق.")
            : L.T("Click here and type. Ctrl+V pastes.", "اضغط هنا واكتب. Ctrl+V للصق."));
        y += 160f;

        if (_suggestRequest)
        {
            Label(new Rect(x, y, w, 22f), L.T("A link or name, if it exists somewhere (optional):", "رابط أو اسم، لو موجود في مكان (اختياري):"), _small, Grey, left: !L.Arabic, right: L.Arabic);
            y += 24f;
            DrawNoteBox(_sgLink, new Rect(x, y, w, 32f), e, mouse, L.T("e.g. a Nexus or Thunderstore link", "مثلا رابط Nexus أو Thunderstore"), oneLine: true);
            y += 42f;
        }

        // How it's sent, in plain words, so nobody thinks the game sends it by itself.
        foreach (var line in Wrap(Edition.Nexus
                     ? L.T("\"Send on GitHub\" opens GitHub in your browser with your text filled in. Press \"Create\" there to send it (a free GitHub account is needed). No account? Press \"Copy text\" and paste it in the Nexus comments.",
                           "\"أرسل على GitHub\" يفتح GitHub في المتصفح ونصك مكتوب. اضغط \"Create\" هناك عشان ينرسل (تحتاج حساب GitHub مجاني). ما عندك حساب؟ اضغط \"انسخ النص\" والصقه في تعليقات Nexus.")
                     : L.T("\"Send on GitHub\" opens GitHub in your browser with your text filled in. Press \"Create\" there to send it (a free GitHub account is needed).",
                           "\"أرسل على GitHub\" يفتح GitHub في المتصفح ونصك مكتوب. اضغط \"Create\" هناك عشان ينرسل (تحتاج حساب GitHub مجاني)."),
                     _small, w))
        {
            Label(new Rect(x, y, w, 21f), line, _small, Grey, left: !L.Arabic, right: L.Arabic);
            y += 21f;
        }
        Label(new Rect(x, y, w, 21f), L.T($"Only your text and the versions are added (Mod Hub {Plugin.Version}, game {GameVersion.Number}). Nothing personal.",
                                          $"ينضاف نصك والإصدارات بس (Mod Hub {Plugin.Version}، اللعبة {GameVersion.Number}). ولا شيء شخصي."), _small, Grey, left: !L.Arabic, right: L.Arabic);
        y += 32f;

        bool empty = _sgText.Text.Trim().Length == 0 && _sgLink.Text.Trim().Length == 0;
        int n = Edition.Nexus ? 3 : 2;
        float bw = (w - (n - 1) * 8f) / n;
        float Bx(int i) => L.Arabic ? x + w - (i + 1) * bw - i * 8f : x + i * (bw + 8f);
        if (Btn(new Rect(Bx(0), y, bw, 34f), L.T("Send on GitHub", "أرسل على GitHub"), _buttonOn))
        {
            if (empty) Toast(L.T("Write something first.", "اكتب شيء أول."));
            else
            {
                Application.OpenURL(IssueUrl());
                Toast(L.T("GitHub opened in your browser. Press \"Create\" there to send it. Thanks!", "انفتح GitHub في المتصفح. اضغط \"Create\" هناك عشان ينرسل. شكرا!"));
            }
        }
        if (Btn(new Rect(Bx(1), y, bw, 34f), L.T("Copy text", "انسخ النص"), _button))
        {
            if (empty) Toast(L.T("Write something first.", "اكتب شيء أول."));
            else Toast(SetClipboard(Title() + "\n\n" + Body()) ? L.T("Copied. Paste it where you want to post it (Ctrl+V).", "تم النسخ. الصقه وين ما تبي تنشره (Ctrl+V).")
                                                               : L.T("Could not reach the clipboard.", "تعذر الوصول للحافظة."));
        }
        if (Edition.Nexus && Btn(new Rect(Bx(2), y, bw, 34f), L.T("Open Nexus comments", "افتح تعليقات Nexus"), _button))
            Application.OpenURL(NexusPostsUrl);
    }

    private static string Title()
    {
        string first = (_suggestRequest && _sgLink.Text.Trim().Length > 0 && _sgText.Text.Trim().Length == 0 ? _sgLink.Text : _sgText.Text).Trim();
        if (first.Length > 70) first = first.Substring(0, 67).TrimEnd() + "...";
        return (_suggestRequest ? "[Mod request] " : "[Suggestion] ") + first;
    }

    private static string Body()
    {
        var parts = new List<string>();
        if (_sgText.Text.Trim().Length > 0) parts.Add(_sgText.Text.Trim());
        if (_suggestRequest && _sgLink.Text.Trim().Length > 0) parts.Add("Link / name: " + _sgLink.Text.Trim());
        parts.Add($"---\nSent from Mod Hub {Plugin.Version}{(Edition.Thunderstore ? " (Thunderstore edition)" : "")}, Megabonk {GameVersion.Number}");
        return string.Join("\n\n", parts);
    }

    private static string IssueUrl()
    {
        string label = _suggestRequest ? "mod request" : "enhancement";
        return $"{IssuesUrl}?title={Uri.EscapeDataString(Title())}&body={Uri.EscapeDataString(Body())}&labels={Uri.EscapeDataString(label)}";
    }

    /// <summary>A text box that wraps its text over several lines, and pastes with Ctrl+V (links are long).</summary>
    private static void DrawNoteBox(TextBox tb, Rect box, Event e, Vector2 mouse, string placeholder, bool oneLine = false)
    {
        if (NavItem(box)) { tb.Focused = true; _typing = tb; _searchFocused = false; }
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (box.Contains(mouse)) { tb.Focused = true; _typing = tb; _searchFocused = false; e.Use(); }
            else if (tb.Focused) { tb.Focused = false; if (_typing == tb) _typing = null; }
        }
        if (tb.Focused && e.type == EventType.KeyDown)
        {
            bool ctrl = e.control || e.command;
            if (ctrl && e.keyCode == KeyCode.V)
            {
                string paste = GetClipboard().Replace("\r", "").Replace('\n', ' ').Replace('\t', ' ');
                tb.Text = (tb.Text + paste).Length > tb.Max ? (tb.Text + paste).Substring(0, tb.Max) : tb.Text + paste;
                e.Use();
            }
            else if (e.keyCode == KeyCode.Backspace) { if (tb.Text.Length > 0) tb.Text = tb.Text.Substring(0, tb.Text.Length - 1); e.Use(); }
            else if (e.keyCode == KeyCode.Escape || ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && oneLine)) { tb.Focused = false; _typing = null; e.Use(); }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { if (tb.Text.Length < tb.Max) tb.Text += " "; e.Use(); }
            else if (!ctrl && e.character != '\0' && !char.IsControl(e.character)) { if (tb.Text.Length < tb.Max) tb.Text += e.character; e.Use(); }
            else if (e.character != '\0') e.Use(); // Ctrl+letter and stray control characters: don't let them reach the game
        }
        BorderedCard(box, tb.Focused ? SearchBgFocus : SearchBg, tb.Focused ? Gold : new Color(1f, 1f, 1f, 0.16f), 8f);
        var inner = new Rect(box.x + 10f, box.y + 6f, box.width - 20f, box.height - 12f);
        if (tb.Text.Length == 0 && !tb.Focused) { Label(inner, placeholder, _small, Grey, left: !L.Arabic, right: L.Arabic); return; }
        bool caret = tb.Focused && (int)(Time.unscaledTime * 2f) % 2 == 0;
        string shown = tb.Text + (caret ? "|" : "");
        if (oneLine) { Label(inner, Fit(shown, _row, inner.width), _row, Color.white, left: true); return; }
        // Show the last lines that fit, so the newest text stays in view.
        var lines = Wrap(shown, _row, inner.width);
        int fit = Mathf.Max(1, (int)(inner.height / 22f));
        int start = Mathf.Max(0, lines.Count - fit);
        for (int i = start; i < lines.Count; i++)
            Label(new Rect(inner.x, inner.y + (i - start) * 22f, inner.width, 22f), lines[i], _row, Color.white, left: true);
        if (tb.Max - tb.Text.Length < 100)
            Label(new Rect(box.xMax - 90f, box.yMax - 22f, 82f, 18f), $"{tb.Text.Length}/{tb.Max}", _small, Grey, right: true);
    }
}
