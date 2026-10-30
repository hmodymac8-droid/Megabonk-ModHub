using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModHub;

/// <summary>The History page (Installed mods tab): what Mod Hub did, newest first, grouped by day, with a Copy button.</summary>
internal static partial class Panel
{
    private static bool _history;                   // the page is open
    private static int _historyScroll;
    private static List<Block> _hBlocks;
    private static string _hKey;

    private static void OpenHistory()
    {
        _details = null;
        _whatsNew = null;
        _history = true;
        _historyScroll = 0;
        _hBlocks = null;
    }

    private static void DrawHistory(Event e, Vector2 mouse, Rect body)
    {
        float x = body.x, y = body.y, w = body.width;
        if (Btn(new Rect(L.Arabic ? x + w - 110f : x, y, 110f, 30f), L.T("< Back", "رجوع >"), _button)) { CloseDetails(); return; }
        Label(new Rect(x + 124f, y + 2f, w - 248f - 60f, 28f), L.T("History", "السجل"), _title, Gold);
        // Copy: players paste this when they ask for help ("what did the Hub change?").
        if (Btn(new Rect(L.Arabic ? x : x + w - 170f, y, 170f, 30f), L.T("Copy history", "انسخ السجل"), _button))
            Toast(SetClipboard(History.AsText()) ? L.T("History copied. Paste it where you ask for help (Ctrl+V).", "تم نسخ السجل. الصقه وين ما تطلب المساعدة (Ctrl+V).")
                                                 : L.T("Could not reach the clipboard.", "تعذر الوصول للحافظة."));
        y += 40f;

        var view = new Rect(x, y, w, body.yMax - y);
        Card(view, new Color(1f, 1f, 1f, 0.03f));
        DrawBlocks(e, mouse, view, BuildHistory(w - ScrollbarW - 32f), ref _historyScroll);
    }

    private static List<Block> BuildHistory(float width)
    {
        // Rebuilt only when something new was recorded, the language or width changed, or the day turned over ("Today").
        string key = $"{width}|{L.Arabic}|{History.Changes}|{DateTime.Now.Date:yyyyMMdd}";
        if (_hBlocks != null && _hKey == key) return _hBlocks;

        var list = new List<Block>();
        var entries = History.Entries;
        if (entries.Count == 0)
            list.Add(new Block { Text = L.T("Nothing yet. Installs, updates, removals and switches will show up here.",
                                            "ما فيه شيء للحين. التثبيت والتحديث والحذف والتشغيل والإطفاء بيظهر هنا."), Style = _small, Color = Grey, Height = 21f });
        DateTime? day = null;
        foreach (var h in entries)
        {
            var local = h.TimeUtc.ToLocalTime();
            if (day != local.Date)
            {
                if (day != null) list.Add(new Block { Height = 10f });
                day = local.Date;
                list.Add(new Block { Text = History.Day(local), Style = _row, Color = Gold, Height = 26f });
            }
            foreach (var line in Wrap($"{local:HH:mm}   {History.Text(h)}", _small, width))
                list.Add(new Block { Text = line, Style = _small, Color = Color.white, Height = 21f });
        }
        _hBlocks = list;
        _hKey = key;
        return list;
    }
}
