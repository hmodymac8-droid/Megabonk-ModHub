using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;

namespace ModHub;

/// <summary>
/// "Copy report": everything a helper needs, as text to paste in Discord or a Nexus comment: Mod Hub, game and
/// BepInEx versions, every mod (on/off, version, where from), possible clashes, recent History and this session's
/// errors grouped by message. Players paste it in public, so personal things are removed first: the Windows user
/// folder and name, the PC name, and Steam IDs (which the game writes into the log).
/// </summary>
internal static class Report
{
    private static readonly Regex ErrorLine = new(@"^\[(Error|Fatal)\s*:\s*([^\]]+)\]\s*(.*)$", RegexOptions.Compiled);
    private static readonly Regex SteamId = new(@"7656119\d{10}", RegexOptions.Compiled);

    public static string Make()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Mod Hub {Plugin.Version}{(Edition.Thunderstore ? " (Thunderstore edition)" : "")} - report {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Game: Megabonk {GameVersion.Number} ({GameVersion.Current}){(GameVersion.Updated ? " - updated since the last start" : "")}");
        sb.AppendLine($"BepInEx: {BepInExVersion()}");
        sb.AppendLine();

        var mods = Catalog.Mods.Where(m => !m.IsHub).ToList();
        sb.AppendLine($"Mods ({mods.Count(m => m.On)} on, {mods.Count(m => !m.On)} off):");
        foreach (var m in mods.OrderBy(m => m.IsCharacter).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
        {
            var notes = new List<string>();
            if (m.IsCharacter) notes.Add("character");
            if (m.IsMelon) notes.Add("MelonLoader");
            if (m.WillChange) notes.Add(m.On ? "turns on after restart" : "turns off after restart");
            if (m.Duplicate) notes.Add("duplicate");
            if (m.Errors > 0) notes.Add($"{m.Errors} error(s)");
            if (m.Latest != null) notes.Add($"update {m.Latest}");
            if (m.Source != null) notes.Add(m.Source);
            sb.AppendLine($"  [{(m.On ? "on " : "off")}] {m.Name} {m.Version}{(notes.Count > 0 ? "  (" + string.Join(", ", notes) + ")" : "")}");
        }

        var clashes = Conflicts.Lines();
        if (clashes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Possible clashes (two mods change the same game function):");
            foreach (var c in clashes) sb.AppendLine("  " + c);
        }

        var recent = History.Entries.Take(15).ToList();
        if (recent.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Recent Mod Hub history:");
            foreach (var h in recent) sb.AppendLine($"  {h.TimeUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {History.Text(h)}");
        }

        var errors = Errors(25);
        sb.AppendLine();
        sb.AppendLine(errors.Count > 0 ? "Errors in this session's log (most recent last):" : "No errors in this session's log.");
        foreach (var e in errors) sb.AppendLine("  " + e);
        return Scrub(sb.ToString());
    }

    private static string BepInExVersion()
    {
        try
        {
            var asm = typeof(Paths).Assembly;
            return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? asm.GetName().Version.ToString();
        }
        catch { return "?"; }
    }

    /// <summary>This session's error lines, the same message once with a count, at most <paramref name="max"/>.</summary>
    private static List<string> Errors(int max)
    {
        var order = new List<string>();
        var counts = new Dictionary<string, int>();
        try
        {
            string log = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
            using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length > 8_000_000) fs.Seek(-8_000_000, SeekOrigin.End); // only the end of a huge log
            using var reader = new StreamReader(fs, Encoding.UTF8);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var m = ErrorLine.Match(line);
                if (!m.Success) continue;
                string msg = m.Groups[3].Value.Trim();
                if (msg.Length > 220) msg = msg.Substring(0, 220) + "...";
                string key = $"[{m.Groups[2].Value.Trim()}] {msg}";
                if (counts.ContainsKey(key)) { counts[key]++; order.Remove(key); }
                else counts[key] = 1;
                order.Add(key);
            }
        }
        catch { return new List<string> { "(the log couldn't be read)" }; }
        return order.Skip(Math.Max(0, order.Count - max)).Select(k => counts[k] > 1 ? $"{counts[k]}x {k}" : k).ToList();
    }

    /// <summary>Removes what identifies the player: user folder, user and PC name, Steam IDs.</summary>
    private static string Scrub(string text)
    {
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (home.Length > 3)
            {
                text = Regex.Replace(text, Regex.Escape(home), "%USERPROFILE%", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, Regex.Escape(home.Replace('\\', '/')), "%USERPROFILE%", RegexOptions.IgnoreCase);
            }
            foreach (var name in new[] { Environment.UserName, Environment.MachineName })
                if (!string.IsNullOrEmpty(name) && name.Length >= 3)
                    text = Regex.Replace(text, @"\b" + Regex.Escape(name) + @"\b", "<hidden>", RegexOptions.IgnoreCase);
        }
        catch { }
        return SteamId.Replace(text, "<steam id>");
    }
}
