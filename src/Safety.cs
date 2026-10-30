using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using UnityEngine;

namespace ModHub;

/// <summary>
/// Crash safety. The Hub notes when the game starts and clears the note once the game has run fine for a while
/// (or closed normally). If the note is still there next time, the last start failed:
///  - once: the Hub opens at the menu and offers to turn off the mods that are new since the last good start;
///  - twice in a row: it turns those mods off by itself (the game may never reach the menu) and says so.
/// It also counts the errors each mod wrote to the BepInEx log, for the "errors" badge.
/// </summary>
internal static class Safety
{
    private const float GoodAfterSeconds = 90f;
    private static string LaunchFile => Path.Combine(Paths.ConfigPath, "ModHub.launch");     // failed starts in a row
    private static string GoodFile => Path.Combine(Paths.ConfigPath, "ModHub.lastgood");     // mods of the last good start

    public static List<Mod> Suspects = new();          // offer to turn these off (or go back, if they were just updated)
    public static List<string> AutoDisabled = new();   // turned off by the Hub after two failed starts
    public static List<string> AutoReverted = new();   // updated mods the Hub put back to their previous version instead
    public static bool NeedsAttention => Suspects.Count > 0 || AutoDisabled.Count > 0 || AutoReverted.Count > 0;

    /// <summary>A suspect that was just updated: going back to the version before is the better fix than turning it off.</summary>
    public static bool CanGoBack(Mod m) => Catalog.SwapFor(m) is Catalog.Swap s && !Catalog.SwapIsNewer(m, s);
    private static bool _markedGood;

    /// <summary>Called from Plugin.Load, as early as possible.</summary>
    public static void Begin()
    {
        int failed = 0;
        try { if (File.Exists(LaunchFile)) int.TryParse(File.ReadAllText(LaunchFile).Trim(), out failed); } catch { }
        try { File.WriteAllText(LaunchFile, (failed + 1).ToString()); } catch { }
        if (failed == 0) return;

        var good = ReadGood();
        if (good == null) return; // never had a good start: no way to tell which mods are new
        try { Catalog.Scan(); } catch { return; }
        var newMods = Catalog.Mods.Where(m => !m.IsHub && !m.IsCharacter && m.On && !good.Contains(Key(m))).ToList();
        if (newMods.Count == 0) return;
        Plugin.Logger.LogWarning($"The last {failed} start(s) of the game didn't finish. New since the last good start: {string.Join(", ", newMods.Select(m => m.Name))}");

        if (failed >= 2)
        {
            foreach (var m in newMods)
            {
                // Just updated? Go back to the version that worked instead of losing the mod.
                string back = CanGoBack(m) ? Catalog.SwapFor(m).OtherVersion : null;
                if (back != null && Catalog.SwapVersion(m, out _))
                {
                    AutoReverted.Add($"{m.Name} {back}");
                    History.Add(History.AutoBack, m.Name, m.Version, back);
                    continue;
                }
                Catalog.Set(m, false, withDependencies: false);
                AutoDisabled.Add(m.Name);
                History.Add(History.AutoOff, m.Name);
            }
            Plugin.Logger.LogWarning("Mod Hub fixed them for the next start (went back to the previous version, or turned them off).");
        }
        else Suspects = newMods;
    }

    /// <summary>The player's choice after one failed start: updated mods go back to their previous version, new ones turn off.</summary>
    public static void FixSuspects()
    {
        foreach (var m in Suspects)
        {
            string back = CanGoBack(m) ? Catalog.SwapFor(m).OtherVersion : null;
            if (back != null && Catalog.SwapVersion(m, out _)) { History.Add(History.SafetyBack, m.Name, m.Version, back); continue; }
            Catalog.Set(m, false, withDependencies: false);
            History.Add(History.SafetyOff, m.Name);
        }
        Suspects = new();
    }

    /// <summary>Every frame: once the game has run for a while, this start counts as good.</summary>
    public static void Tick() { if (!_markedGood && Time.realtimeSinceStartup > GoodAfterSeconds) MarkGood(); }

    public static void MarkGood()
    {
        if (_markedGood) return;
        if (Catalog.Mods.Count == 0) return; // not scanned yet: nothing to remember
        _markedGood = true;
        try
        {
            File.Delete(LaunchFile);
            File.WriteAllLines(GoodFile, Catalog.Mods.Where(m => m.LoadedNow && !m.IsCharacter).Select(Key));
        }
        catch (Exception e) { Plugin.Logger.LogDebug($"Mod Hub safety: {e.Message}"); }
    }

    public static void TurnOffSuspects()
    {
        foreach (var m in Suspects) Catalog.Set(m, false, withDependencies: false);
        Suspects = new();
    }

    public static void Dismiss() { Suspects = new(); AutoDisabled = new(); AutoReverted = new(); }

    private static string Key(Mod m) => $"{m.Id}|{m.Version}";

    private static HashSet<string> ReadGood()
    {
        try { return File.Exists(GoodFile) ? new HashSet<string>(File.ReadAllLines(GoodFile), StringComparer.OrdinalIgnoreCase) : null; }
        catch { return null; }
    }

    // ------------------------------------------------------------------ errors in the log

    private static readonly Regex ErrorLine = new(@"^\[(?:Error|Fatal)\s*:\s*([^\]]+?)\s*\]", RegexOptions.Compiled);

    /// <summary>Counts this session's log errors per mod (by the name a mod logs under).</summary>
    private static volatile Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
    private static int _counting;

    /// <summary>Reads the log on a background thread (a long session's log can be megabytes), then updates the badges.</summary>
    public static void CountErrors()
    {
        if (System.Threading.Interlocked.Exchange(ref _counting, 1) == 1) return;
        Online.Background("ModHub-LogErrors", () =>
        {
            try { _counts = ReadCounts(); Online.OnMain(() => Apply(Catalog.Mods)); }
            finally { System.Threading.Interlocked.Exchange(ref _counting, 0); }
        });
    }

    private static Dictionary<string, int> ReadCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string log = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
            using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length > 8_000_000) fs.Seek(-8_000_000, SeekOrigin.End); // only the end of a huge log
            using var reader = new StreamReader(fs, Encoding.UTF8);
            var lines = new List<string>();
            string line;
            while ((line = reader.ReadLine()) != null) lines.Add(line);
            var mods = Catalog.Mods.Where(m => !m.IsCharacter).ToList();
            for (int i = 0; i < lines.Count; i++)
            {
                line = lines[i];
                var m = ErrorLine.Match(line);
                // "[UnityLog:...]" lines are the game's own errors that a mod merely forwards: not that mod's fault.
                if (!m.Success || line.Contains("[UnityLog")) continue;
                string source = m.Groups[1].Value;
                // An il2cpp trampoline exception is tagged "Il2CppInterop", but the stack names the mod that threw.
                if (source.Equals("Il2CppInterop", StringComparison.OrdinalIgnoreCase))
                {
                    string owner = OwnerInStack(lines, i + 1, mods);
                    if (owner != null) { counts[owner] = counts.TryGetValue(owner, out int k) ? k + 1 : 1; continue; }
                }
                counts[source] = counts.TryGetValue(source, out int n) ? n + 1 : 1;
            }
        }
        catch { /* no log yet */ }
        return counts;
    }

    /// <summary>Finds the installed mod named in an exception's stack lines (until the next log entry).</summary>
    private static string OwnerInStack(List<string> lines, int from, List<Mod> mods)
    {
        int end = Math.Min(lines.Count, from + 25);
        for (int i = from; i < end; i++)
        {
            string l = lines[i];
            if (l.StartsWith("[")) break;
            foreach (var mod in mods)
            {
                string token = (mod.Name ?? "").Replace(" ", "");
                if (token.Length >= 5 && l.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return mod.Name;
                if (!string.IsNullOrEmpty(mod.Guid) && l.IndexOf(mod.Guid, StringComparison.OrdinalIgnoreCase) >= 0) return mod.Name;
            }
        }
        return null;
    }

    /// <summary>Puts the last counts on the mods (also after every rescan, which makes new mod entries).</summary>
    public static void Apply(IEnumerable<Mod> mods)
    {
        var counts = _counts;
        foreach (var mod in mods)
        {
            int n = 0;
            if (mod.Name != null && counts.TryGetValue(mod.Name, out int a)) n += a;
            if (mod.Guid != null && mod.Guid != mod.Name && counts.TryGetValue(mod.Guid, out int b)) n += b;
            mod.Errors = n;
        }
    }
}
