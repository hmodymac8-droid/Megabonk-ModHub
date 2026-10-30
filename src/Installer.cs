using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using BepInEx;

namespace ModHub;

/// <summary>What a zip or DLL would install, worked out before anything is written.</summary>
internal sealed class InstallCheck
{
    public bool Ok;
    public string Problem;            // why it can't be installed (shown to the player)
    public string ModName;            // display name
    public string Guid;               // BepInEx plugin GUID (used to replace an older copy wherever it is)
    public string Version;
    public string FolderName;         // folder inside BepInEx\plugins
    public Loader Loader;
    public List<string> ThunderstoreDeps = new();
    public string Note;               // a warning that doesn't stop the install ("Might not work: ...")
    public bool IsCharacter;          // a custom character/skin: nothing to place here, Characters does the install
    // zip entry -> path relative to the BepInEx folder ("plugins/<folder>/...", "melon-mods/<folder>/...", "config/...")
    // or "@userdata/..." for a MelonLoader mod's default settings in the game's UserData folder
    public List<(string Entry, string Target)> Files = new();

    /// <summary>Where this kind of mod lives, relative to BepInEx.</summary>
    public string Base => Loader == Loader.MelonLoader ? "melon-mods/" : "plugins/";
}

/// <summary>
/// Installs BepInEx mods from zips (Nexus, Thunderstore, All-in-One packs, plain zips) or single DLLs.
/// Only BepInEx plugins are accepted; BepInEx itself (core, winhttp.dll) is never overwritten.
/// An existing copy is moved to BepInEx\_hub_removed first, so an update can be undone by hand.
/// </summary>
internal static class Installer
{
    private static readonly string[] SkipNames = { "manifest.json", "icon.png", "readme.md", "changelog.md", "license", "license.txt", "license.md" };

    public static InstallCheck Check(string path)
    {
        try
        {
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return CheckDll(path);
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(path);
                return CheckZip(zip, Path.GetFileNameWithoutExtension(path));
            }
            return Fail(L.T("Only .zip and .dll files can be installed.", "يمكن تثبيت ملفات .zip و .dll فقط."));
        }
        catch (Exception e) { return Fail(L.T($"Could not read the file: {e.Message}", $"تعذرت قراءة الملف: {e.Message}")); }
    }

    private static InstallCheck Fail(string why) => new() { Ok = false, Problem = why };

    private static InstallCheck CheckDll(string path)
    {
        var meta = ModInfo.Read(path);
        var c = Classify(meta == null ? new List<PluginMeta>() : new List<PluginMeta> { meta });
        if (!c.Ok) return c;
        c.FolderName = SafeName(c.ModName ?? Path.GetFileNameWithoutExtension(path));
        c.Files.Add((null, $"{c.Base}{c.FolderName}/{Path.GetFileName(path)}"));
        if (c.Loader == Loader.MelonLoader) PreCheckMelon(c, null, path);
        return c;
    }

    private static InstallCheck CheckZip(ZipArchive zip, string zipName)
    {
        var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        string Norm(ZipArchiveEntry e) => e.FullName.Replace('\\', '/');

        // Custom characters and skins aren't plugins: a "X.custom.json" plus its asset bundle and no DLL at all.
        // They belong in BepInEx\plugins\CustomCharacters, which Characters installs (and whose loader it makes sure is there).
        if (!entries.Any(e => e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            var character = entries.FirstOrDefault(e => Characters.IsCharacterJson(e.Name));
            if (character != null) return CharacterCheck(zip, character);
        }

        // Read every .NET DLL's metadata.
        var metas = new List<PluginMeta>();
        foreach (var e in entries.Where(e => e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            ms.Position = 0;
            var m = ModInfo.Read(ms);
            if (m != null) { m.Entry = Norm(e); metas.Add(m); }
        }
        var c = Classify(metas);
        if (!c.Ok) return c;

        // Thunderstore package?
        var manifestEntry = entries.FirstOrDefault(e => Norm(e).Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        if (manifestEntry != null)
        {
            try
            {
                using var s = manifestEntry.Open();
                using var doc = JsonDocument.Parse(s);
                var root = doc.RootElement;
                if (root.TryGetProperty("name", out var n)) c.ModName ??= n.GetString()?.Replace('_', ' ');
                if (root.TryGetProperty("version_number", out var v)) c.Version ??= v.GetString();
                if (root.TryGetProperty("dependencies", out var deps))
                    foreach (var d in deps.EnumerateArray()) c.ThunderstoreDeps.Add(d.GetString());
            }
            catch { /* not a real manifest */ }
        }

        c.FolderName = SafeName(c.ModName ?? zipName);
        if (c.Loader == Loader.MelonLoader)
        {
            MelonLayout(entries.Select(e => (e.FullName, Norm(e))).ToList(), c);
            PreCheckMelon(c, zip, null);
            return c;
        }
        string pluginsRoot = $"plugins/{c.FolderName}/";

        // 1) Zip already laid out like the game folder (BepInEx/plugins/...): take plugins and new config files only.
        var bep = entries.Where(e => Norm(e).Contains("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase)).ToList();
        if (bep.Count > 0)
        {
            foreach (var e in bep)
            {
                string rel = Norm(e);
                rel = rel.Substring(rel.IndexOf("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase) + "BepInEx/".Length);
                // A loose DLL right in plugins/ goes into its own folder so it can be switched and removed cleanly.
                if (rel.Count(ch => ch == '/') == 1) rel = pluginsRoot + rel.Substring("plugins/".Length);
                c.Files.Add((e.FullName, rel));
            }
            foreach (var e in entries.Where(e => Norm(e).Contains("BepInEx/config/", StringComparison.OrdinalIgnoreCase)))
            {
                string rel = Norm(e);
                c.Files.Add((e.FullName, rel.Substring(rel.IndexOf("BepInEx/config/", StringComparison.OrdinalIgnoreCase) + "BepInEx/".Length)));
            }
            // The folder the mod itself sits in (a zip can hold more than one, e.g. Mod Hub + Melon Bridge).
            string own = metas.FirstOrDefault(m => m.Guid != null && m.Guid == c.Guid)?.Entry;
            int at = own?.IndexOf("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase) ?? -1;
            string ownFolder = null;
            if (at >= 0)
            {
                var parts = own.Substring(at + "BepInEx/plugins/".Length).Split('/');
                if (parts.Length > 1) ownFolder = parts[0];
            }
            c.FolderName = TopFolders(c).FirstOrDefault(f => f.Equals(ownFolder, StringComparison.OrdinalIgnoreCase))
                           ?? TopFolders(c).FirstOrDefault() ?? c.FolderName;
            return c;
        }

        // 2) Thunderstore style: a "plugins/" folder, or files at the root.
        var tsPlugins = entries.Where(e => Norm(e).StartsWith("plugins/", StringComparison.OrdinalIgnoreCase)).ToList();
        if (tsPlugins.Count > 0)
        {
            foreach (var e in tsPlugins) c.Files.Add((e.FullName, pluginsRoot + Norm(e).Substring("plugins/".Length)));
            foreach (var e in entries.Where(e => Norm(e).StartsWith("config/", StringComparison.OrdinalIgnoreCase)))
                c.Files.Add((e.FullName, Norm(e)));
            return c;
        }

        // 3) Anything else: keep the zip's layout under one folder (drop a single wrapping folder).
        var paths = entries.Select(Norm).ToList();
        string common = CommonFolder(paths);
        foreach (var e in entries)
        {
            string rel = Norm(e).Substring(common.Length);
            if (SkipNames.Contains(Path.GetFileName(rel).ToLowerInvariant()) && !rel.Contains('/')) continue;
            if (rel.EndsWith("winhttp.dll", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("dotnet/") || rel.Contains("/core/")) continue;
            c.Files.Add((e.FullName, pluginsRoot + rel));
        }
        return c;
    }

    /// <summary>A character/skin pack: there is no plugin to install, so the Hub only checks it and lets Characters place the files.</summary>
    private static InstallCheck CharacterCheck(ZipArchive zip, ZipArchiveEntry json)
    {
        // A broken pack makes the game's loader throw, which stops every other character from loading: refuse it here.
        if (Characters.Validate(zip, json) is string problem) return Fail(problem);
        string text = null;
        try { using var s = json.Open(); using var r = new StreamReader(s); text = r.ReadToEnd(); }
        catch { /* unreadable json: still a character pack */ }
        string fallback = json.Name.Substring(0, Math.Max(0, json.Name.Length - ".custom.json".Length));
        if (string.IsNullOrEmpty(fallback)) fallback = "Character";
        return new InstallCheck
        {
            Ok = true,
            IsCharacter = true,
            ModName = text == null ? fallback : Characters.NameOf(text, fallback),
            Version = text == null ? null : Characters.VersionOf(text),
            FolderName = fallback,
        };
    }

    /// <summary>Decides from the DLLs whether this is something the Hub can install.</summary>
    private static InstallCheck Classify(List<PluginMeta> metas)
    {
        // Mod Hub's own zip also holds Melon Bridge: it's Mod Hub, whichever DLL comes first.
        var plugin = metas.FirstOrDefault(m => m.Loader == Loader.BepInEx && m.Guid == Plugin.Guid)
                     ?? metas.FirstOrDefault(m => m.Loader == Loader.BepInEx && m.Guid != null)
                     ?? metas.FirstOrDefault(m => m.Loader == Loader.BepInEx);
        if (plugin != null && !metas.Any(m => m.Loader == Loader.BepInEx && m.ForIl2Cpp))
            return Fail(L.T("This mod is made for a different game or an older BepInEx, not for Megabonk.",
                            "هذا المود مصنوع للعبة ثانية أو لنسخة BepInEx قديمة، مو لـ Megabonk."));
        if (plugin != null)
            return new InstallCheck { Ok = true, Loader = Loader.BepInEx, ModName = plugin.Name, Version = plugin.Version, Guid = plugin.Guid };
        var melon = metas.FirstOrDefault(m => m.Loader == Loader.MelonLoader && m.Name != null) ?? metas.FirstOrDefault(m => m.Loader == Loader.MelonLoader);
        if (melon != null)
        {
            if (!Melon.BridgeInstalled)
                return new InstallCheck
                {
                    Ok = false, Loader = Loader.MelonLoader,
                    Problem = L.T("This is a MelonLoader mod. It needs Melon Bridge, which comes with Mod Hub: reinstall Mod Hub to get it.",
                                  "هذا مود MelonLoader. يحتاج Melon Bridge اللي يجي مع Mod Hub: ثبّت Mod Hub من جديد عشان يجيك."),
                };
            return new InstallCheck { Ok = true, Loader = Loader.MelonLoader, ModName = melon.Name, Version = melon.Version, Guid = melon.Name != null ? "melon:" + melon.Name : null };
        }
        return Fail(L.T("No BepInEx mod found in this file.", "ما فيه مود BepInEx في هذا الملف."));
    }

    private const string UserData = "@userdata/";
    private static readonly string[] MelonFolders = { "mods/", "plugins/", "userlibs/", "userdata/" };

    /// <summary>
    /// A MelonLoader mod keeps its own layout (Mods/, Plugins/, UserLibs/ or DLLs at the top) inside melon-mods/&lt;folder&gt;/.
    /// Its UserData files are default settings: they go to the game's UserData folder, but never over existing ones.
    /// MelonLoader itself is never installed.
    /// </summary>
    private static void MelonLayout(List<(string Entry, string Path)> entries, InstallCheck c)
    {
        string common = CommonFolder(entries.Select(e => e.Path).ToList());
        if (MelonFolders.Contains(common.ToLowerInvariant())) common = ""; // "Mods/x.dll" alone: keep the folder meaning
        string root = $"melon-mods/{c.FolderName}/";
        foreach (var (entry, path) in entries)
        {
            string rel = path.Substring(common.Length);
            string low = rel.ToLowerInvariant();
            if (!rel.Contains('/') && SkipNames.Contains(low)) continue;
            if (low.StartsWith("melonloader/") || low == "version.dll" || low == "winmm.dll" || low == "winhttp.dll" || low.StartsWith("dotnet/")) continue;
            if (low.StartsWith("userdata/")) { c.Files.Add((entry, UserData + rel.Substring("userdata/".Length))); continue; }
            c.Files.Add((entry, root + rel));
        }
    }

    /// <summary>
    /// Asks Melon Bridge to convert and check the mod before it is installed: "won't work" stops the install
    /// with the reason, "might not work" installs with a warning. Without a running bridge nothing is decided here
    /// (the bridge still checks at the next start and Mod Hub shows its answer).
    /// </summary>
    private static void PreCheckMelon(InstallCheck c, ZipArchive zip, string dllPath)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "ModHub-melon-" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            var melonDlls = new List<string>();
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (zip == null) melonDlls.Add(dllPath);
            else
            {
                string full = Path.GetFullPath(tmp) + Path.DirectorySeparatorChar;
                foreach (var e in zip.Entries.Where(e => e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                {
                    string dest = Path.GetFullPath(Path.Combine(tmp, e.FullName.Replace('\\', '/')));
                    if (!dest.StartsWith(full, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                    dirs.Add(Path.GetDirectoryName(dest));
                    if (ModInfo.Read(dest)?.Loader == Loader.MelonLoader) melonDlls.Add(dest);
                }
            }
            string worst = "Works", reason = null;
            foreach (var dll in melonDlls)
            {
                var r = Melon.Check(dll, dirs);
                if (r == null) return;
                if (Rank(r.Verdict) > Rank(worst)) { worst = r.Verdict; reason = r.Reason; }
            }
            if (worst == "NotSupported")
            {
                c.Ok = false;
                c.Problem = L.T($"This MelonLoader mod won't work in this game version: {reason}", $"مود MelonLoader هذا ما راح يشتغل في نسخة اللعبة هذي: {reason}");
            }
            else if (worst == "MightNotWork") c.Note = L.T($"Might not work: {reason}", $"ممكن ما يشتغل: {reason}");
        }
        catch (Exception e) { Plugin.Logger.LogWarning("Melon pre-check failed: " + e.Message); }
        finally { try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { } }
    }

    private static int Rank(string verdict) => verdict == "NotSupported" ? 2 : verdict == "MightNotWork" ? 1 : 0;

    /// <summary>Installs into a different folder (e.g. Thunderstore's Owner-Name).</summary>
    public static void Retarget(InstallCheck c, string folder)
    {
        string from = $"{c.Base}{c.FolderName}/", to = $"{c.Base}{SafeName(folder)}/";
        for (int i = 0; i < c.Files.Count; i++)
            if (c.Files[i].Target.StartsWith(from)) c.Files[i] = (c.Files[i].Entry, to + c.Files[i].Target.Substring(from.Length));
        c.FolderName = SafeName(folder);
    }

    /// <summary>The folders inside plugins/ (or melon-mods/) this install writes to.</summary>
    private static IEnumerable<string> TopFolders(InstallCheck c)
    {
        string b = c.Base;
        return c.Files.Select(f => f.Target).Where(t => t.StartsWith(b) && t.IndexOf('/', b.Length) > b.Length)
            .Select(t => t.Substring(b.Length, t.IndexOf('/', b.Length) - b.Length)).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string CommonFolder(List<string> paths)
    {
        if (paths.Count == 0) return "";
        int slash = paths[0].IndexOf('/');
        if (slash < 0) return "";
        string first = paths[0].Substring(0, slash + 1);
        return paths.All(p => p.StartsWith(first, StringComparison.OrdinalIgnoreCase)) ? first : "";
    }

    private static string SafeName(string s)
    {
        foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
        s = s.Trim().Trim('.');
        return string.IsNullOrEmpty(s) ? "Mod" : s;
    }

    /// <summary>Installs what <see cref="Check"/> found. Returns the plugins folder name, or null.</summary>
    public static string Install(string path, InstallCheck c, string source, string packageVersion = null)
    {
        if (!c.Ok) return null;
        bool melon = c.Loader == Loader.MelonLoader;
        string baseDir = melon ? Melon.ModsDir : Catalog.PluginsDir;
        string id = melon ? Melon.IdFor(c.FolderName) : c.FolderName;
        string target = Path.Combine(baseDir, c.FolderName);
        try
        {
            // Updating: move old copies aside first (renaming works even while the game has them loaded).
            // The copy of this same mod is remembered, so the player can go back to it ("Back to vX").
            string previousCopy = null, previousVersion = null;
            var before = Catalog.Mods.FirstOrDefault(m => m.Id == id);
            foreach (var folder in TopFolders(c).DefaultIfEmpty(c.FolderName))
            {
                string old = Path.Combine(baseDir, folder);
                if (!Directory.Exists(old)) continue;
                Directory.CreateDirectory(Catalog.RemovedDir);
                string moved = Path.Combine(Catalog.RemovedDir, $"{(melon ? Melon.IdFor(folder) : folder)}-{DateTime.Now:yyyyMMdd-HHmmss}");
                Directory.Move(old, moved);
                if (folder.Equals(c.FolderName, StringComparison.OrdinalIgnoreCase)) { previousCopy = moved; previousVersion = before?.PackageVersion ?? before?.Version; }
            }
            // The same mod installed under another folder name (e.g. from Nexus, then updated from Thunderstore):
            // move that copy aside too, so updating never leaves two copies behind.
            if (c.Guid != null)
                foreach (var other in Catalog.Mods.ToList())
                    if (!other.IsHub && other.Guid == c.Guid && (Directory.Exists(other.Path) || File.Exists(other.Path)))
                        Catalog.Uninstall(other);

            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(target);
                File.Copy(path, Path.Combine(target, Path.GetFileName(path)), true);
            }
            else
            {
                using var zip = ZipFile.OpenRead(path);
                string userData = Path.GetFullPath(Path.Combine(Paths.GameRootPath, "UserData"));
                foreach (var (entry, rel) in c.Files)
                {
                    bool settings = rel.StartsWith(UserData);
                    string root = settings ? userData : Path.GetFullPath(Paths.BepInExRootPath);
                    var dest = Path.GetFullPath(Path.Combine(root, settings ? rel.Substring(UserData.Length) : rel));
                    // Never write outside BepInEx (or UserData for settings), and never overwrite the player's existing settings.
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    if ((settings || rel.StartsWith("config/")) && File.Exists(dest)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    zip.GetEntry(entry)?.ExtractToFile(dest, true);
                }
            }
            if (source != null) Catalog.SetSource(id, source, packageVersion);
            if (previousCopy != null && previousVersion != (packageVersion ?? c.Version)) Catalog.RememberUpdate(id, previousCopy, previousVersion);
            Catalog.NoteInstalled(id);
            Plugin.Logger.LogInfo($"Installed {c.ModName} {c.Version} into {c.Base}{c.FolderName} (from {source ?? path}).");
            return id;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Install failed for {path}: {e.Message}");
            return null;
        }
    }
}
