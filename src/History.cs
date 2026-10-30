using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using BepInEx;

namespace ModHub;

/// <summary>One thing Mod Hub did. Stored without words, so it reads in the player's language at any time.</summary>
internal sealed class HistoryEntry
{
    public DateTime TimeUtc { get; set; }
    public string Kind { get; set; }      // one of the History.* kinds
    public string Name { get; set; }      // mod, character or profile name
    public string From { get; set; }      // version before (updates, going back)
    public string To { get; set; }        // version after
    public string Source { get; set; }    // "Thunderstore:Owner-Name", "Nexus:194", "GitHub:...", "Downloads", "Drop folder"
    public string Detail { get; set; }    // kind-specific: flags ("kept", "loader"), a count, or names that changed along
}

/// <summary>
/// A plain-language log of what Mod Hub did (installs, updates, removals, switches, vanilla mode, profiles, crash fixes),
/// so players can see what changed and paste it when asking for help. Kept in BepInEx\config\ModHub.history.json,
/// newest last, capped at <see cref="Max"/> entries. Saved on a background thread so the game never waits.
/// Entries are added where the player (or the Hub) decides something, not in the low-level file moves, so that
/// "turn on vanilla mode" is one line and not one line per mod.
/// </summary>
internal static class History
{
    public const string Install = "install", Update = "update", Remove = "remove", Duplicate = "duplicate", CancelInstall = "cancel-install";
    public const string On = "on", Off = "off";
    public const string CharacterInstall = "character-install", CharacterUpdate = "character-update", CharacterRemove = "character-remove";
    public const string CharacterOn = "character-on", CharacterOff = "character-off";
    public const string Back = "back", Redo = "redo", CancelUpdate = "cancel-update";
    public const string AutoBack = "auto-back", AutoOff = "auto-off", SafetyBack = "safety-back", SafetyOff = "safety-off";
    public const string VanillaOn = "vanilla-on", VanillaOff = "vanilla-off", ProfileApply = "profile-apply", ProfileSave = "profile-save";
    public const string Emptied = "emptied";

    private const int Max = 500;
    private static string FilePath => Path.Combine(Paths.ConfigPath, "ModHub.history.json");

    private static readonly object Lock = new();
    private static List<HistoryEntry> _entries;
    private static int _saving;
    private static volatile bool _dirty;

    /// <summary>Goes up with every new entry (the History page rebuilds its lines when it changes).</summary>
    public static int Changes { get; private set; }

    /// <summary>Records one action. Safe to call from any thread (installs run in the background).</summary>
    public static void Add(string kind, string name, string from = null, string to = null, string source = null, string detail = null)
    {
        try
        {
            lock (Lock)
            {
                LoadIfNeeded();
                _entries.Add(new HistoryEntry { TimeUtc = DateTime.UtcNow, Kind = kind, Name = name, From = from, To = to, Source = source, Detail = detail });
                if (_entries.Count > Max) _entries.RemoveRange(0, _entries.Count - Max);
                Changes++;
            }
            _dirty = true;
            SaveSoon();
        }
        catch (Exception e) { Plugin.Logger.LogDebug($"History: {e.Message}"); }
    }

    /// <summary>All entries, newest first.</summary>
    public static List<HistoryEntry> Entries
    {
        get { lock (Lock) { LoadIfNeeded(); return Enumerable.Reverse(_entries).ToList(); } }
    }

    private static void LoadIfNeeded()
    {
        if (_entries != null) return;
        _entries = new List<HistoryEntry>();
        try
        {
            if (File.Exists(FilePath)) _entries = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath)) ?? new List<HistoryEntry>();
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"Could not read the Mod Hub history (starting a new one): {e.Message}"); }
    }

    /// <summary>Writes the file on a background thread; several entries in a row are written together.</summary>
    private static void SaveSoon()
    {
        if (Interlocked.Exchange(ref _saving, 1) == 1) return;
        Online.Background("ModHub-History", () =>
        {
            try { while (_dirty) { _dirty = false; Write(); } }
            finally { Interlocked.Exchange(ref _saving, 0); }
            if (_dirty) SaveSoon(); // an entry came in while the last write was finishing
        });
    }

    /// <summary>Called when the game closes: writes what the background thread may not have written yet.</summary>
    public static void Flush()
    {
        if (!_dirty) return;
        _dirty = false;
        Write();
    }

    private static readonly object WriteLock = new(); // the background save and Flush at quit must not write at once

    private static void Write()
    {
        lock (WriteLock)
        {
            try
            {
                string json;
                lock (Lock) json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
                // Write next to it first, so a crash halfway never leaves a broken file behind.
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, FilePath, true);
            }
            catch (Exception e) { Plugin.Logger.LogDebug($"Could not save the Mod Hub history: {e.Message}"); }
        }
    }

    // ------------------------------------------------------------------ words

    /// <summary>One sentence for an entry, in the player's language.</summary>
    public static string Text(HistoryEntry h)
    {
        string n = h.Name ?? "?";
        string where = SourceName(h.Source);
        string by = where == null ? "" : L.T($" from {where}", $" من {where}");
        bool kept = Has(h, "kept");
        string loader = Has(h, "loader") ? L.T(" (needed for custom characters)", " (تحتاجه الشخصيات المخصصة)") : "";
        string also = h.Kind is On or Off && !string.IsNullOrEmpty(h.Detail)
            ? L.T($" (also changed: {h.Detail})", $" (وتغير معه: {h.Detail})") : "";
        switch (h.Kind)
        {
            case Install: return L.T($"Installed {n} {h.To}{by}", $"تم تثبيت {n} {h.To}{by}") + loader;
            case Update:
                if (h.From == null || h.From == h.To) return L.T($"Reinstalled {n} {h.To}{by}", $"تمت إعادة تثبيت {n} {h.To}{by}") + loader;
                return L.T($"Updated {n} {h.From} → {h.To}{by}", $"تم تحديث {n} من {h.From} إلى {h.To}{by}")
                       + (kept ? L.T(" (old copy kept)", " (النسخة القديمة محفوظة)") : "") + loader;
            case Remove: return L.T($@"Removed {n} (moved to BepInEx\_hub_removed)", $@"تم حذف {n} (نُقل إلى BepInEx\_hub_removed)");
            case Duplicate: return L.T($@"Mod Hub moved out an old duplicate copy of {n} {h.From} (to BepInEx\_hub_removed)",
                                       $@"المدير نقل نسخة قديمة مكررة من {n} {h.From} (إلى BepInEx\_hub_removed)");
            case CancelInstall: return L.T($"Cancelled the install of {n}", $"تم إلغاء تثبيت {n}");
            case On: return L.T($"Turned ON {n}", $"تم تشغيل {n}") + also;
            case Off: return L.T($"Turned OFF {n}", $"تم إطفاء {n}") + also;
            case CharacterInstall: return L.T($"Installed the character {n} {h.To}{by}", $"تم تثبيت الشخصية {n} {h.To}{by}");
            case CharacterUpdate:
                return h.From != null && h.From != h.To
                    ? L.T($"Updated the character {n} {h.From} → {h.To}{by}", $"تم تحديث الشخصية {n} من {h.From} إلى {h.To}{by}")
                    : L.T($"Reinstalled the character {n} {h.To}{by}", $"تمت إعادة تثبيت الشخصية {n} {h.To}{by}");
            case CharacterRemove: return L.T($@"Removed the character {n} (moved to BepInEx\_hub_removed)", $@"تم حذف الشخصية {n} (نُقلت إلى BepInEx\_hub_removed)");
            case CharacterOn: return L.T($"Turned ON the character {n}", $"تم تشغيل الشخصية {n}");
            case CharacterOff: return L.T($"Turned OFF the character {n}", $"تم إطفاء الشخصية {n}");
            case Back: return L.T($"Went back from {n} {h.From} to {h.To}", $"تم الرجوع في {n} من {h.From} إلى {h.To}");
            case Redo: return L.T($"Switched {n} from {h.From} to {h.To} (the saved copy, no download)", $"تم تبديل {n} من {h.From} إلى {h.To} (النسخة المحفوظة، بدون تحميل)");
            case CancelUpdate: return L.T($"Cancelled the update of {n} (stays on {h.To})", $"تم إلغاء تحديث {n} (يبقى على {h.To})");
            case AutoBack: return L.T($"The game didn't start twice: Mod Hub put {n} back from {h.From} to {h.To}",
                                      $"اللعبة ما اشتغلت مرتين: المدير رجّع {n} من {h.From} إلى {h.To}");
            case AutoOff: return L.T($"The game didn't start twice: Mod Hub turned OFF {n}", $"اللعبة ما اشتغلت مرتين: المدير طفّى {n}");
            case SafetyBack: return L.T($"After a failed start: went back from {n} {h.From} to {h.To}", $"بعد تشغيل فاشل: تم الرجوع في {n} من {h.From} إلى {h.To}");
            case SafetyOff: return L.T($"After a failed start: turned OFF {n}", $"بعد تشغيل فاشل: تم إطفاء {n}");
            case VanillaOn: return L.T($"Vanilla mode ON ({h.Detail} mods turned off)", $"وضع اللعبة الأصلية شغال ({h.Detail} مود انطفى)");
            case VanillaOff: return L.T($"Vanilla mode OFF ({h.Detail} mods turned back on)", $"وضع اللعبة الأصلية انطفى ({h.Detail} مود رجع)");
            case ProfileApply: return L.T($"Used the profile \"{n}\" ({h.Detail} changes)", $"تم استخدام المجموعة \"{n}\" ({h.Detail} تغيير)");
            case ProfileSave: return L.T($"Saved the profile \"{n}\" ({h.Detail} mods on)", $"تم حفظ المجموعة \"{n}\" ({h.Detail} مود شغال)");
            case Emptied: return L.T($@"Emptied BepInEx\_hub_removed (freed {h.Detail}): removed mods and old versions are gone for good",
                                     $@"تم تفريغ BepInEx\_hub_removed (تم توفير {h.Detail}): المودات المحذوفة والنسخ القديمة انحذفت نهائيا");
            default: return $"{h.Kind} {n} {h.From} {h.To}".Trim();
        }
    }

    private static bool Has(HistoryEntry h, string flag) =>
        h.Detail != null && h.Detail.Split(',').Any(f => f.Trim() == flag);

    /// <summary>"Thunderstore", "Nexus", "GitHub", "your Downloads", "the drop folder" (null when unknown).</summary>
    private static string SourceName(string source)
    {
        if (string.IsNullOrEmpty(source)) return null;
        if (source.StartsWith("Thunderstore:")) return "Thunderstore";
        if (source.StartsWith("Nexus:")) return "Nexus";
        if (source.StartsWith("GitHub:")) return "GitHub";
        if (source == "Downloads") return L.T("your Downloads", "التنزيلات");
        if (source == "Drop folder") return L.T("the drop folder", "مجلد الإسقاط");
        return null; // a file path: nothing useful to say
    }

    /// <summary>"Today", "Yesterday" or the date, for the day an entry happened (local time).</summary>
    public static string Day(DateTime local)
    {
        var today = DateTime.Now.Date;
        if (local.Date == today) return L.T("Today", "اليوم");
        if (local.Date == today.AddDays(-1)) return L.T("Yesterday", "أمس");
        if (L.Arabic) return $"{local.Day}/{local.Month}/{local.Year}";
        return local.ToString(local.Year == today.Year ? "d MMM" : "d MMM yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>The whole history as text, newest first, for pasting into a help request.</summary>
    public static string AsText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Mod Hub {Plugin.Version} - " + L.T("history", "السجل"));
        foreach (var h in Entries)
            sb.AppendLine($"{h.TimeUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {Text(h)}");
        return sb.ToString();
    }
}
