using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ModHub;

/// <summary>
/// The page that opens when you click a mod in the Get mods tab: big icon, who made it, Install,
/// and its full description with screenshots (from the mod's Thunderstore README).
/// </summary>
internal static partial class Panel
{
    private static StorePackage _details;          // mod whose page is open (null = the list)
    private static int _detailScroll;              // first block shown
    private static List<Block> _blocks;
    private static string _blocksKey;

    private sealed class Block
    {
        public string Text, Image;
        public GUIStyle Style;
        public Color Color;
        public float Height;
        public Action Click;   // a button (Text is its label)
    }

    private static void OpenDetails(StorePackage p) { _details = p; _detailScroll = 0; _blocks = null; }
    private static bool CloseDetails()
    {
        if (_whatsNew != null) { _whatsNew = null; return true; }
        if (_details == null) return false;
        _details = null;
        return true;
    }

    private static void DrawDetails(Event e, Vector2 mouse, Rect body)
    {
        var p = _details;
        float x = body.x, y = body.y, w = body.width;

        if (Btn(new Rect(x, y, 110f, 30f), L.T("< Back", "رجوع >"), _button)) { CloseDetails(); return; }
        if (Btn(new Rect(x + w - 200f, y, 200f, 30f), p.IsNexus ? L.T("Open on Nexus", "افتح في Nexus") : L.T("Open on Thunderstore", "افتح في Thunderstore"), _button))
            Application.OpenURL(p.PageUrl);
        y += 40f;

        // Header: icon, name, author, install
        var iconRect = new Rect(x, y, 112f, 112f);
        DrawIcon(iconRect, p.IconUrl);
        float tx = x + 128f, tw = w - 128f;
        Label(new Rect(tx, y + 4f, tw, 30f), $"{p.DisplayName}  v{p.Version}", _title, Color.white, left: true);
        string tag = Melon.TagFor(p);
        Label(new Rect(tx, y + 40f, tw, 22f), (L.Arabic ? $"{p.Owner}  ·  {p.Downloads:N0} تحميل" : $"by {p.Owner}  ·  {p.Downloads:N0} downloads")
                                              + (tag != null ? "  ·  " + tag : ""), _small, Grey, left: true);
        StoreButton(p, new Rect(tx, y + 72f, 160f, 34f));
        if (Melon.BrokenReason(p) is string broken)
            Label(new Rect(tx + 172f, y + 78f, tw - 172f, 22f), Fit(broken, _small, tw - 172f), _small, Red, left: true);
        // Character/skin packs list CustomCharacterCreator as a dependency: say which version is required.
        else if (p.Dependencies.Any(d => d.IndexOf("CustomCharacterCreator", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            string want = Characters.LoaderVersion();
            string name = Characters.LoaderName;
            Label(new Rect(tx + 172f, y + 78f, tw - 172f, 22f),
                Fit(L.T($"Needs {name} {want} (installed for you) — the old 1.4.0 does not work with this game version.",
                        $"تحتاج {name} {want} (يتثبت معها) — النسخة القديمة 1.4.0 ما تشتغل مع نسخة اللعبة هذي."), _small, tw - 172f),
                _small, Gold, left: true);
        }
        y += 124f;

        // Description
        var view = new Rect(x, y, w, body.yMax - y);
        Card(view, new Color(1f, 1f, 1f, 0.03f));
        DrawBlocks(e, mouse, view, BuildBlocks(p, w - ScrollbarW - 32f), ref _detailScroll);
    }

    /// <summary>
    /// Shows text lines, pictures and buttons in a box, from the first block at <paramref name="scroll"/>, with a
    /// scrollbar that can be dragged (or clicked to jump). Shared by the details page and What's new.
    /// </summary>
    private static void DrawBlocks(Event e, Vector2 mouse, Rect view, List<Block> blocks, ref int scroll)
    {
        if (e.type == EventType.ScrollWheel && view.Contains(mouse)) { scroll += e.delta.y > 0 ? 3 : -3; e.Use(); }
        var bar = new Rect(view.xMax - ScrollbarW - 4f, view.y + 4f, ScrollbarW, view.height - 8f);
        var grab = new Rect(bar.x - 8f, bar.y, bar.width + 12f, bar.height);
        if (e.type == EventType.MouseDown && e.button == 0 && grab.Contains(mouse)) _dragBar = true;
        if (e.rawType == EventType.MouseUp) _dragBar = false;
        if (_dragBar && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
        { scroll = Mathf.RoundToInt(Mathf.InverseLerp(bar.y, bar.yMax, mouse.y) * (blocks.Count - 1)); e.Use(); }
        scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, blocks.Count - 1));
        int first = scroll;
        // The scroll callback can't capture a ref parameter: it reports how far to move, applied on the next frame.
        NavArea(view, first > 0, first < blocks.Count - 1, false, d => _blockScrollBy += d);
        if (_blockScrollBy != 0) { scroll = Mathf.Clamp(scroll + _blockScrollBy, 0, Mathf.Max(0, blocks.Count - 1)); _blockScrollBy = 0; first = scroll; }

        float cy = view.y + 10f, cx = view.x + 12f, cw = view.width - ScrollbarW - 32f;
        int last = first;
        for (int i = first; i < blocks.Count; i++)
        {
            var b = blocks[i];
            float h = b.Height;
            Texture2D img = null;
            float room = view.yMax - 6f - cy;
            float maxImageH = 300f;
            if (b.Image != null)
            {
                img = Images.Get(b.Image);
                if (img == null && Images.IsFailed(b.Image)) continue;
                // Shrink a screenshot into the space that's left (rather than leaving the page blank below it).
                if (img != null && i > first && room >= 128f) maxImageH = Mathf.Min(maxImageH, room - 8f);
                h = img == null ? 40f : Mathf.Min(maxImageH, img.height * Mathf.Min(1f, cw / img.width)) + 8f;
            }
            if (h > room && i > first) break;
            if (b.Image != null)
            {
                if (img == null) Label(new Rect(cx, cy + 10f, cw, 20f), L.T("Loading image...", "جاري تحميل الصورة..."), _small, Grey, left: true);
                else
                {
                    float s = Mathf.Min(cw / img.width, maxImageH / img.height, 1f);
                    GUI.DrawTexture(new Rect(cx, cy, img.width * s, img.height * s), img);
                }
            }
            else if (b.Click != null)
            {
                float bw = Mathf.Min(cw, _small.CalcSize(new GUIContent(b.Text)).x + 40f);
                if (Btn(new Rect(L.Arabic ? cx + cw - bw : cx, cy + 3f, bw, h - 8f), b.Text, _button)) b.Click();
            }
            else if (!string.IsNullOrEmpty(b.Text)) Label(new Rect(cx, cy, cw, h), b.Text, b.Style, b.Color);
            cy += h;
            last = i;
        }

        // Scrollbar
        if (blocks.Count > 0 && (first > 0 || last < blocks.Count - 1) && e.type == EventType.Repaint)
        {
            Card(bar, new Color(1f, 1f, 1f, 0.06f), 5f);
            float shown = Mathf.Max(1, last - first + 1);
            float thumbH = Mathf.Max(30f, bar.height * shown / blocks.Count);
            float thumbY = bar.y + (bar.height - thumbH) * (first / (float)Mathf.Max(1, blocks.Count - 1));
            Card(new Rect(bar.x, Mathf.Min(thumbY, bar.yMax - thumbH), bar.width, thumbH), _dragBar ? Gold : new Color(1f, 1f, 1f, 0.45f), 5f);
        }
    }
    private static int _blockScrollBy;

    /// <summary>A mod picture fitted into the box without stretching (Nexus pictures are wide, Thunderstore icons square).</summary>
    private static void DrawIcon(Rect box, string url)
    {
        var tex = Images.Get(url);
        if (tex == null) { Card(box, new Color(1f, 1f, 1f, 0.06f)); return; }
        float s = Mathf.Min(box.width / tex.width, box.height / tex.height);
        float w = tex.width * s, h = tex.height * s;
        if (w < box.width || h < box.height) Fill(box, new Color(0f, 0f, 0f, 0.35f));
        GUI.DrawTexture(new Rect(box.x + (box.width - w) / 2f, box.y + (box.height - h) / 2f, w, h), tex);
    }

    /// <summary>Install / Update / Installed for a Thunderstore mod (shared by the list and the details page).</summary>
    private static void StoreButton(StorePackage p, Rect b)
    {
        if (p.IsNexus)
        {
            // Same mod on Thunderstore? Then it installs right here. Otherwise Nexus needs its own page (no direct downloads).
            var ts = Online.Store?.FirstOrDefault(s => Same(s.Name, p.Name));
            if (ts != null) { StoreButton(ts, b); return; }
            var mine = Catalog.Mods.FirstOrDefault(m => Same(m.Name, p.Name));
            if (JustInstalled(mine)) { if (Btn(b, L.T("Cancel install", "ألغِ التثبيت"), _button)) CancelInstall(mine); }
            else if (mine != null && !Updates.Newer(p.Version, Updates.HaveVersion(mine)))
                Label(new Rect(b.x, b.y + 5f, b.width, 22f), L.T("Installed", "مثبت"), _row, Green, right: true);
            else if (Btn(b, mine != null ? L.T("Update on Nexus", "حدّث من Nexus") : L.T("Get on Nexus", "حمّل من Nexus"), _button))
            {
                Application.OpenURL(p.PageUrl + "?tab=files");
                Toast(L.T("Click Manual download. Mod Hub installs it from your Downloads (Get mods tab).", "اضغط Manual download، والـ Mod Hub يثبته من التنزيلات (تبويب تحميل مودات)."));
            }
            return;
        }
        var installed = Catalog.Mods.FirstOrDefault(m => m.Source == "Thunderstore:" + p.FullName)
                        ?? Catalog.Mods.FirstOrDefault(m => Same(m.Name, p.Name));
        bool busy = Installing.Contains(p.FullName);
        if (JustInstalled(installed) && !busy) { if (Btn(b, L.T("Cancel install", "ألغِ التثبيت"), _button)) CancelInstall(installed); }
        else if (p.NeedsMelon && !Melon.BridgeInstalled) Label(new Rect(b.x - 120f, b.y + 5f, b.width + 120f, 22f), L.T("Needs MelonLoader", "يحتاج MelonLoader"), _small, Red, right: true);
        else if (installed != null && !Updates.Newer(p.Version, Updates.HaveVersion(installed)))
            Label(new Rect(b.x, b.y + 5f, b.width, 22f), L.T("Installed", "مثبت"), _row, Green, right: true);
        else if (Btn(b, busy ? L.T("Installing...", "جاري التثبيت...") : installed != null ? L.T("Update", "تحديث") : L.T("Install", "تثبيت"), _buttonOn) && !busy)
        {
            Installing.Add(p.FullName);
            Online.InstallFromStore(p, msg => { Installing.Remove(p.FullName); Toast(msg); }, installed != null && installed.IsFolder ? Melon.FolderOf(installed.Id) : null);
        }
    }

    // ------------------------------------------------------------------ README -> lines and images

    private static readonly Regex ImageMd = new(@"!\[[^\]]*\]\(\s*<?([^)\s>]+)>?[^)]*\)", RegexOptions.Compiled);
    private static readonly Regex ImageHtml = new(@"<img[^>]*?src\s*=\s*[""']([^""']+)[""'][^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LinkMd = new(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex Html = new(@"<[^>]+>", RegexOptions.Compiled);

    private static List<Block> BuildBlocks(StorePackage p, float width)
    {
        string md = Readmes.Get(p);
        string key = $"{p.FullName}|{width}|{L.Arabic}|{md?.Length ?? -1}";
        if (_blocks != null && _blocksKey == key) return _blocks;

        var list = new List<Block>();
        void Text(string s, GUIStyle st, Color c)
        {
            float lineH = st == _row ? 26f : 21f;
            foreach (var line in Wrap(s, st, width)) list.Add(new Block { Text = line, Style = st, Color = c, Height = lineH });
        }
        void Space(float h) { if (list.Count > 0 && list[list.Count - 1].Text == null && list[list.Count - 1].Image == null) return; list.Add(new Block { Height = h }); }

        if (string.IsNullOrWhiteSpace(md))
        {
            Text(p.Description ?? "", _small, Color.white);
            Space(10f);
            Text(md == null ? L.T("Loading details...", "جاري تحميل التفاصيل...") : L.T("No more details from the author.", "ما فيه تفاصيل أكثر من صاحب المود."), _small, Grey);
        }
        else
        {
            AddMarkdown(list, md, width);
        }
        _blocks = list;
        _blocksKey = key;
        return list;
    }

    /// <summary>Turns markdown into lines (and pictures) that fit the width, added to <paramref name="list"/>.</summary>
    private static void AddMarkdown(List<Block> list, string md, float width)
    {
        void Text(string s, GUIStyle st, Color c)
        {
            float lineH = st == _row ? 26f : 21f;
            foreach (var line in Wrap(s, st, width)) list.Add(new Block { Text = line, Style = st, Color = c, Height = lineH });
        }
        void Space(float h) { if (list.Count > 0 && list[list.Count - 1].Text == null && list[list.Count - 1].Image == null) return; list.Add(new Block { Height = h }); }

        int images = 0;
        bool code = false;
        foreach (var raw in md.Replace("\r", "").Split('\n'))
        {
            if (list.Count > 600) break;
            string line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```")) { code = !code; Space(6f); continue; }
            if (code) { Text(line, _small, new Color(0.8f, 0.85f, 0.95f)); continue; }

            // Screenshots (badges, SVGs and relative links can't be shown).
            foreach (Match m in ImageMd.Matches(line).Cast<Match>().Concat(ImageHtml.Matches(line).Cast<Match>()))
            {
                string url = m.Groups[1].Value;
                if (images < 6 && url.StartsWith("http") && !url.Contains("shields.io") && !url.Contains("badge")
                    && !url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) && !url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                { list.Add(new Block { Image = url, Height = 40f }); images++; }
            }
            line = ImageHtml.Replace(ImageMd.Replace(line, ""), "");
            line = LinkMd.Replace(line, "$1");
            line = Regex.Replace(line, @"(?<![*\w])\*(?![*\s])([^*]+?)\*(?![*\w])", "$1"); // *italic* (not bullets or **bold**)
            line = Html.Replace(line, "").Replace("**", "").Replace("__", "").Replace("`", "")
                       .Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">");
            // Entities like &#92; (some READMEs encode them twice).
            for (int n = 0; n < 2 && line.Contains('&'); n++) line = System.Net.WebUtility.HtmlDecode(line);
            string t = line.Trim();

            if (t.Length == 0 || Regex.IsMatch(t, @"^([-*_]\s*){3,}$")) { Space(10f); continue; }
            if (t.StartsWith("|"))
            {
                if (Regex.IsMatch(t, @"^\|[\s:|-]+\|?$")) continue; // table separator row
                Text(string.Join("   ", t.Trim('|').Split('|').Select(c => c.Trim())), _small, Color.white);
                continue;
            }
            var heading = Regex.Match(t, @"^#{1,6}\s*(.*)$");
            if (heading.Success) { Space(8f); Text(heading.Groups[1].Value, _row, Gold); continue; }
            var bullet = Regex.Match(line, @"^(\s*)[-*+]\s+(.*)$");
            if (bullet.Success) { Text((bullet.Groups[1].Value.Length >= 2 ? "      ◦ " : "  • ") + bullet.Groups[2].Value, _small, Color.white); continue; }
            if (t.StartsWith(">")) { Text(t.TrimStart('>', ' '), _small, Grey); continue; }
            Text(t, _small, Color.white);
        }
    }

    /// <summary>Splits text into lines that fit the width (GUI word-wrap isn't available in this game).</summary>
    private static List<string> Wrap(string text, GUIStyle style, float width)
    {
        var lines = new List<string>();
        var sb = new StringBuilder();
        float Measure(string s) => style.CalcSize(new GUIContent(s)).x;
        foreach (var word in text.Split(' '))
        {
            string candidate = sb.Length == 0 ? word : sb + " " + word;
            if (sb.Length > 0 && Measure(candidate) > width) { lines.Add(sb.ToString()); sb.Clear().Append(word); }
            else sb.Clear().Append(candidate);
            while (sb.Length > 1 && Measure(sb.ToString()) > width)
            {
                // A single very long word (like a URL): cut it.
                int cut = sb.Length - 1;
                while (cut > 1 && Measure(sb.ToString(0, cut)) > width) cut--;
                lines.Add(sb.ToString(0, cut));
                sb.Remove(0, cut);
            }
        }
        if (sb.Length > 0 || lines.Count == 0) lines.Add(sb.ToString());
        return lines;
    }
}
