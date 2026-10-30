using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ModHub;

/// <summary>The What's new page (Installed mods tab): the author's notes for each updated mod, or where to look.</summary>
internal static partial class Panel
{
    private static List<Change> _whatsNew;          // the page is open (null = closed)
    private static bool _whatsNewBefore;            // about an update that isn't installed yet
    private static int _whatsNewScroll;
    private static List<Block> _wnBlocks;
    private static string _wnKey;

    /// <summary>Opens the Hub on What's new for the mods updated since last time (once, after the game starts).</summary>
    public static void ShowWhatsNew()
    {
        var list = WhatsNew.Unseen.ToList();
        WhatsNew.MarkSeen();
        if (list.Count == 0) return;
        if (!Open) Toggle();
        _tab = 0;
        CloseDetails();
        OpenWhatsNew(list, false);
    }

    private static void OpenWhatsNew(List<Change> list, bool before)
    {
        _details = null;
        _whatsNew = list;
        _whatsNewBefore = before;
        _whatsNewScroll = 0;
        _wnBlocks = null;
    }

    /// <summary>"What's new" on a mod row: the update on offer, or the one just installed.</summary>
    private static Change WhatsNewFor(Mod m)
    {
        if (m.Latest != null) return new Change { Id = m.Id, Name = m.Name, From = Updates.HaveVersion(m), To = m.Latest };
        return WhatsNew.RecentFor(m);
    }

    private static void DrawWhatsNew(Event e, Vector2 mouse, Rect body)
    {
        float x = body.x, y = body.y, w = body.width;
        if (Btn(new Rect(L.Arabic ? x + w - 110f : x, y, 110f, 30f), L.T("< Back", "رجوع >"), _button)) { CloseDetails(); return; }
        string title = _whatsNewBefore ? L.T("What's new in this update", "وش الجديد في هذا التحديث") : L.T("What's new", "وش الجديد");
        Label(new Rect(x + 124f, y + 2f, w - 248f, 28f), title, _title, Gold);
        y += 40f;

        var view = new Rect(x, y, w, body.yMax - y);
        Card(view, new Color(1f, 1f, 1f, 0.03f));
        DrawBlocks(e, mouse, view, BuildWhatsNew(w - ScrollbarW - 32f), ref _whatsNewScroll);
    }

    private static List<Block> BuildWhatsNew(float width)
    {
        // Each mod's notes, looked up (and loaded in the background) every frame until they arrive.
        var items = new List<(Change Change, Notes Notes)>();
        foreach (var c in _whatsNew)
        {
            var m = Catalog.Mods.FirstOrDefault(x => x.Id == c.Id);
            if (m != null) items.Add((c, WhatsNew.For(m, c.From, c.To)));
        }
        string key = $"{width}|{L.Arabic}|" + string.Join(";", items.Select(i => $"{i.Change.Id}:{i.Notes.Loading}:{i.Notes.Sections.Count}"));
        if (_wnBlocks != null && _wnKey == key) return _wnBlocks;

        var list = new List<Block>();
        void Text(string s, GUIStyle st, Color c)
        {
            float lineH = st == _row ? 26f : 21f;
            foreach (var line in Wrap(s, st, width)) list.Add(new Block { Text = line, Style = st, Color = c, Height = lineH });
        }
        void Space(float h) => list.Add(new Block { Height = h });

        if (!_whatsNewBefore && items.Count > 1)
        {
            Text(L.T($"{items.Count} mods were updated since you last played.", $"{items.Count} مودات تحدّثت من آخر مرة لعبت."), _small, Grey);
            Space(6f);
        }
        foreach (var (c, notes) in items)
        {
            if (list.Count > 0) Space(12f);
            Text($"{c.Name}   {c.From}  →  {c.To}", _row, Gold);
            Space(4f);
            if (notes.Loading)
            {
                Text(L.T("Looking for the author's notes...", "ندوّر على ملاحظات صاحب المود..."), _small, Grey);
                continue;
            }
            if (notes.Sections.Count == 0)
            {
                Text(L.T("No notes were published by the author for this update.", "صاحب المود ما نشر أي ملاحظات عن هذا التحديث."), _small, Color.white);
                if (notes.SourceUrl != null)
                {
                    Text(L.T($"You may find more on its {notes.SourceName} page:", $"ممكن تلقى تفاصيل أكثر في صفحته على {notes.SourceName}:"), _small, Grey);
                    string url = notes.SourceUrl;
                    list.Add(new Block { Text = L.T($"Open on {notes.SourceName}", $"افتح في {notes.SourceName}"), Height = 40f, Click = () => Application.OpenURL(url) });
                }
                continue;
            }
            foreach (var (version, text) in notes.Sections)
            {
                if (notes.Sections.Count > 1 || version != c.To) Text(L.T($"Version {version}", $"النسخة {version}"), _row, Color.white);
                AddMarkdown(list, text, width);
                Space(6f);
            }
        }
        _wnBlocks = list;
        _wnKey = key;
        return list;
    }
}
