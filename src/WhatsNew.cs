using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModHub;

/// <summary>One mod that changed version: shown in What's new.</summary>
internal sealed class Change
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string From { get; set; }
    public string To { get; set; }
}

/// <summary>What one update changed, from the author's notes (or nothing, when the author wrote none).</summary>
internal sealed class Notes
{
    public volatile bool Loading = true;
    public List<(string Version, string Text)> Sections = new();
    public string SourceUrl;   // where the player can look for themselves
    public string SourceName;  // "Nexus", "Thunderstore", "GitHub"
}

/// <summary>
/// What's new. At every start the Hub compares each running mod's version with the one it saw last time; a mod that
/// went up (updated in the Hub, from Downloads, by hand, or Mod Hub itself) is listed once, and the Hub opens on
/// What's new with the author's notes for every version in between: the Thunderstore CHANGELOG, a changelog part of
/// the README, the Nexus file changelogs, or the GitHub release text for the Hub. No notes: it says so and links the page.
/// </summary>
internal static class WhatsNew
{
    private static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, "ModHub.whatsnew.json");

    private sealed class Saved
    {
        public Dictionary<string, string> Seen { get; set; } = new();   // mod id -> version that ran last time
        public List<Change> Unseen { get; set; } = new();                // updates not shown yet
        public Dictionary<string, Change> Recent { get; set; } = new();  // mod id -> its last update (for the row button)
    }
    private static Saved _saved;

    /// <summary>Updates the player hasn't seen yet (the Hub opens on them once).</summary>
    public static List<Change> Unseen => _saved?.Unseen ?? new List<Change>();

    /// <summary>
    /// Called once at start, after the first scan. The first start with this feature only remembers the versions,
    /// so nobody gets a page about every mod they already had.
    /// </summary>
    public static void CheckAtStart()
    {
        bool first = !File.Exists(FilePath);
        try { _saved = first ? new Saved() : JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath)) ?? new Saved(); }
        catch (Exception e) { Plugin.Logger.LogDebug($"What's new: {e.Message}"); _saved = new Saved(); }
        _saved.Seen ??= new(); _saved.Unseen ??= new(); _saved.Recent ??= new();

        foreach (var m in Catalog.Mods.Where(m => m.LoadedNow && !m.Duplicate && !m.IsCharacter))
        {
            string now = Updates.HaveVersion(m);
            if (string.IsNullOrEmpty(now)) continue;
            if (!first && _saved.Seen.TryGetValue(m.Id, out var before) && Updates.Newer(now, before))
            {
                var c = new Change { Id = m.Id, Name = m.Name, From = before, To = now };
                _saved.Unseen.RemoveAll(u => u.Id == m.Id);
                _saved.Unseen.Add(c);
                _saved.Recent[m.Id] = c;
                Plugin.Logger.LogInfo($"What's new: {m.Name} {before} -> {now}.");
            }
            _saved.Seen[m.Id] = now;
        }
        Save();
        if (Unseen.Count > 0) { Online.LoadStore(); Online.LoadNexus(); Online.LoadHubRelease(); } // needed to find the notes
    }

    /// <summary>The page was shown: it doesn't open by itself again (the row button still shows it).</summary>
    public static void MarkSeen() { if (_saved == null || _saved.Unseen.Count == 0) return; _saved.Unseen.Clear(); Save(); }

    /// <summary>This mod's last update, while it's still the version installed.</summary>
    public static Change RecentFor(Mod m) =>
        _saved != null && _saved.Recent.TryGetValue(m.Id, out var c) && c.To == Updates.HaveVersion(m) ? c : null;

    private static void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(_saved, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception e) { Plugin.Logger.LogDebug($"What's new: {e.Message}"); }
    }

    // ------------------------------------------------------------------ finding the notes

    private static readonly ConcurrentDictionary<string, Notes> Cache = new();
    private static readonly ConcurrentDictionary<string, DateTime> FirstAsked = new();
    private static readonly Notes Waiting = new();

    /// <summary>The notes for versions after <paramref name="from"/> up to <paramref name="to"/> (loads in the background).</summary>
    public static Notes For(Mod m, string from, string to)
    {
        string key = $"{m.Id}|{from}|{to}";
        if (Cache.TryGetValue(key, out var n)) return n;
        // Wait for the lists (up to 20 s): without them the mod's pages can't be found yet.
        var asked = FirstAsked.GetOrAdd(key, DateTime.UtcNow);
        bool waited = DateTime.UtcNow - asked > TimeSpan.FromSeconds(20);
        bool ready = (Online.Store != null || Online.StoreError != null) && (Online.Nexus != null || !Edition.Nexus) && (!m.IsHub || Online.Release != null || !Edition.SelfUpdate);
        if (!ready && !waited) return Waiting;
        n = new Notes();
        if (!Cache.TryAdd(key, n)) return Cache[key];

        var pkg = Updates.StorePackageFor(m);
        var nexus = Updates.NexusFor(m, pkg);
        if (m.IsHub && Edition.SelfUpdate) { n.SourceUrl = $"https://github.com/{Online.HubRepo}/releases"; n.SourceName = "GitHub"; }
        else if (nexus != null) { n.SourceUrl = $"https://www.nexusmods.com/megabonk/mods/{nexus.Id}?tab=logs"; n.SourceName = "Nexus"; }
        else if (pkg != null) { n.SourceUrl = pkg.PageUrl; n.SourceName = "Thunderstore"; }

        Online.Background("ModHub-WhatsNew", () =>
        {
            try
            {
                if (m.IsHub && Edition.SelfUpdate) n.Sections = FromGitHub(from, to);
                if (n.Sections.Count == 0 && pkg != null) n.Sections = FromThunderstore(pkg, from, to);
                if (n.Sections.Count == 0 && nexus != null) n.Sections = FromNexus(nexus.Id, from, to);
            }
            catch (Exception e) { Plugin.Logger.LogDebug($"What's new for {m.Name}: {e.Message}"); }
            n.Loading = false;
        });
        return n;
    }

    private static string Get(string url)
    {
        using var resp = Online.Http.GetAsync(url).GetAwaiter().GetResult();
        return resp.IsSuccessStatusCode ? resp.Content.ReadAsStringAsync().GetAwaiter().GetResult() : null;
    }

    /// <summary>Compares versions with missing parts as 0, so "1.3" and "1.3.0" are the same version.</summary>
    private static int Cmp(string a, string b)
    {
        static int[] P(string v)
        {
            v = (v ?? "").Trim().TrimStart('v', 'V');
            int cut = v.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut > 0) v = v.Substring(0, cut);
            var n = new int[4];
            var parts = v.Split('.');
            for (int i = 0; i < 4 && i < parts.Length; i++) int.TryParse(parts[i], out n[i]);
            return n;
        }
        var x = P(a); var y = P(b);
        for (int i = 0; i < 4; i++) if (x[i] != y[i]) return x[i].CompareTo(y[i]);
        return 0;
    }

    private static bool InRange(string v, string from, string to) =>
        Cmp(v, to) <= 0 && (from == null ? Cmp(v, to) == 0 : Cmp(v, from) > 0);

    /// <summary>Mod Hub's own releases: the text written for each one.</summary>
    private static List<(string, string)> FromGitHub(string from, string to)
    {
        var list = new List<(string, string)>();
        string json = Get($"https://api.github.com/repos/{Online.HubRepo}/releases?per_page=30");
        if (json == null) return list;
        using var doc = JsonDocument.Parse(json);
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            string v = (r.GetProperty("tag_name").GetString() ?? "").Trim().TrimStart('v', 'V');
            string body = r.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
            if (InRange(v, from, to) && !string.IsNullOrWhiteSpace(body)) list.Add((v, body.Trim()));
        }
        return Sorted(list);
    }

    /// <summary>The package's CHANGELOG.md, else a changelog part of its README.</summary>
    private static List<(string, string)> FromThunderstore(StorePackage p, string from, string to)
    {
        string api = $"https://thunderstore.io/api/experimental/package/{p.Owner}/{p.Name}/";
        foreach (var version in new[] { to, p.Version }.Distinct())
        {
            foreach (var file in new[] { "changelog", "readme" })
            {
                string json = Get($"{api}{version}/{file}/");
                if (json == null) continue;
                using var doc = JsonDocument.Parse(json);
                string md = doc.RootElement.TryGetProperty("markdown", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(md)) continue;
                if (file == "readme") md = ChangelogPart(md);
                var found = Split(md, from, to);
                if (found.Count > 0) return found;
            }
        }
        return new List<(string, string)>();
    }

    /// <summary>
    /// Nexus: each uploaded file can carry a changelog, and the mod page has a Changelogs tab (the mod description
    /// sometimes has one too). Public API, no key.
    /// </summary>
    private static List<(string, string)> FromNexus(int modId, string from, string to)
    {
        var list = new List<(string, string)>();
        try
        {
            string q = "{\"query\":\"{ modFiles(modId: " + modId + ", gameId: 7674) { version changelogText } }\"}";
            using var resp = Online.Http.PostAsync("https://api.nexusmods.com/v2/graphql", new System.Net.Http.StringContent(q, Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("modFiles", out var files) && files.ValueKind == JsonValueKind.Array)
                foreach (var f in files.EnumerateArray())
                {
                    string v = f.TryGetProperty("version", out var fv) ? fv.GetString() : null;
                    if (v == null || !InRange(v, from, to) || list.Any(x => x.Item1 == v)) continue;
                    if (!f.TryGetProperty("changelogText", out var lines) || lines.ValueKind != JsonValueKind.Array) continue;
                    var text = string.Join("\n", lines.EnumerateArray().Select(l => l.GetString()).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => "- " + l.Trim()));
                    if (text.Length > 0) list.Add((v, text));
                }
            else if (doc.RootElement.TryGetProperty("errors", out var errors)) Plugin.Logger.LogDebug($"Nexus changelog: {errors}");
        }
        catch (Exception e) { Plugin.Logger.LogDebug($"Nexus changelog: {e.Message}"); }
        if (list.Count > 0) return Sorted(list);

        // The description: a "Changelog" part written there.
        string dq = "{\"query\":\"{ legacyMods(ids: [{gameId: 7674, modId: " + modId + "}]) { nodes { description } } }\"}";
        using var dresp = Online.Http.PostAsync("https://api.nexusmods.com/v2/graphql", new System.Net.Http.StringContent(dq, Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
        using var ddoc = JsonDocument.Parse(dresp.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        var nodes = ddoc.RootElement.GetProperty("data").GetProperty("legacyMods").GetProperty("nodes");
        if (nodes.GetArrayLength() == 0) return list;
        string md = Readmes.BBCodeToMarkdown(nodes[0].GetProperty("description").GetString() ?? "");
        return Split(ChangelogPart(md), from, to);
    }

    private static List<(string, string)> Sorted(List<(string, string)> list)
    {
        list.Sort((a, b) => Cmp(b.Item1, a.Item1)); // newest first
        return list;
    }

    // ------------------------------------------------------------------ reading changelogs

    private static readonly Regex ChangelogHeading = new(@"^\s*(#{1,6}\s*|\[b\]|\*\*|__)?\s*(change\s*-?\s*log|changes|updates?|patch\s*notes|release\s*notes|what'?s\s*new|version\s*history|history)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // A line that starts a version: "## 1.3.0", "## [1.3.0] - 2026-10-20", "**v1.3.0**", "1.3.0:", "Version 1.3.0", "- v1.3.0 - fixed x".
    private static readonly Regex VersionLine = new(@"^\s*(?:#{1,6}\s*|[-*+]\s+)?(?:\*\*|__)?\s*\[?\s*(?:version\s*|ver\.?\s*|update\s*)?v?(\d+(?:\.\d+){1,3})\b\]?(?:\*\*|__)?\s*(.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The part of a README under a "Changelog" / "Updates" heading (up to the next heading of the same level).</summary>
    internal static string ChangelogPart(string md)
    {
        var lines = md.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (!ChangelogHeading.IsMatch(lines[i]) || lines[i].Trim().Length > 60) continue;
            int level = lines[i].TrimStart().TakeWhile(c => c == '#').Count();
            var part = new List<string>();
            for (int j = i + 1; j < lines.Length; j++)
            {
                string t = lines[j].TrimStart();
                int l = t.TakeWhile(c => c == '#').Count();
                if (level > 0 && l > 0 && l <= level && !VersionLine.IsMatch(t)) break;
                part.Add(lines[j]);
            }
            return string.Join("\n", part);
        }
        return "";
    }

    /// <summary>
    /// Cuts a changelog into versions and keeps the ones after <paramref name="from"/> up to <paramref name="to"/>.
    /// A version line can carry its notes on the same line ("- 1.3.0: fixed x") or have them on the lines below.
    /// </summary>
    internal static List<(string, string)> Split(string md, string from, string to)
    {
        var found = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(md)) return found;
        string version = null;
        var text = new StringBuilder();
        void Flush()
        {
            if (version != null && InRange(version, from, to) && text.ToString().Trim() is { Length: > 0 } t && !found.Any(f => f.Item1 == version))
                found.Add((version, t));
            text.Clear();
        }
        foreach (var raw in md.Replace("\r", "").Split('\n'))
        {
            var m = VersionLine.Match(raw);
            // A version starts a new part when it's a heading/bold line, or a short line that's clearly just a version.
            bool starts = m.Success && (raw.TrimStart().StartsWith("#") || raw.Contains("**") || raw.Contains("__")
                          || Regex.IsMatch(m.Groups[2].Value, @"^([-:–(\[]|$)") || raw.TrimStart().StartsWith("-") || raw.TrimStart().StartsWith("*"));
            if (starts)
            {
                Flush();
                version = m.Groups[1].Value;
                // Notes on the same line ("1.3.0: fixed x"), without a date or dash in front.
                string rest = Regex.Replace(m.Groups[2].Value, @"^[\s\-:–]*(\(?\d{4}[-/.]\d{1,2}[-/.]\d{1,2}\)?|\(?\d{1,2}[-/.]\d{1,2}[-/.]\d{2,4}\)?)?[\s\-:–]*", "").Trim();
                if (rest.Length > 0 && !rest.StartsWith("#")) text.AppendLine("- " + rest);
                continue;
            }
            if (version != null) text.AppendLine(raw);
        }
        Flush();
        return Sorted(found);
    }
}
