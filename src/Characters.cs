using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace ModHub;

/// <summary>
/// Custom characters and skins (Yuzu, Vergil, ...) aren't BepInEx mods: they are a "Name.custom.json" plus a Unity
/// asset bundle that the CustomCharacterCreator mod loads from its own folder. Dropped into the drop folder
/// (loose or zipped), the Hub installs the loader if needed and puts the character next to it.
/// </summary>
internal static class Characters
{
    public const string LoaderPackage = "MegabonkModders-CustomCharacterCreator";

    /// <summary>The mod's name as players see it in Get mods (the original and the fixes share it).</summary>
    public const string LoaderName = "CustomCharacterCreator";
    private const string JsonEnd = ".custom.json";
    private const string OffTag = ".off";                 // a switched-off character: "X.custom.json.off"
    private static bool _snapshotTaken;
    private static readonly HashSet<string> _onAtStart = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaderRequested;

    /// <summary>The folder the loader reads characters from. It also scans every *.custom.json under plugins, so an
    /// older install kept next to the loader still works; new ones all go here.</summary>
    public const string FolderName = "CustomCharacters";

    public static string Dir => Path.Combine(Catalog.PluginsDir, FolderName);

    /// <summary>Every Thunderstore package that is a character loader — the original plus community fixes — newest first.</summary>
    public static List<StorePackage> LoaderPackages() =>
        (Online.Store ?? new List<StorePackage>())
            .Where(p => p.FullName.IndexOf("CustomCharacterCreator", StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderByDescending(p => p.Updated)
            .ToList();

    /// <summary>The loader to install: the newest one. The original stops loading characters after a game update, and someone
    /// usually publishes a fixed copy under a new package name with the same plugin inside.
    /// </summary>
    public static StorePackage PreferredLoader() => LoaderPackages().FirstOrDefault();

    /// <summary>The loader version the game needs (the newest one on Thunderstore), for messages.</summary>
    public static string LoaderVersion() => PreferredLoader()?.Version ?? "1.4.1";

    /// <summary>Must the installed loader be replaced? Missing, the original package (broken after the game update),
    /// or failing in the log are all "yes".</summary>
    public static bool LoaderNeedsUpdate(Mod loader)
    {
        if (loader == null) return true;
        if (string.Equals(loader.Source, "Thunderstore:" + LoaderPackage, StringComparison.OrdinalIgnoreCase)) return true;
        return LoaderProblem() != null;
    }

    private static bool _loaderPending;

    /// <summary>Thunderstore's list has arrived: finish a loader install that was waiting for it (offline at install time).</summary>
    public static void LoaderListArrived()
    {
        if (!_loaderPending) return;
        _loaderPending = false;
        EnsureLoader(out _);
    }

    private static readonly string[] PackRoots = { "character", "skin", "soloSkin", "weapon" };

    /// <summary>
    /// Checks a character/skin pack before anything is written. A pack with a broken json, or one whose 3D model is
    /// missing, makes the game's loader throw — and then NO character loads. Refuse it here, with the reason.
    /// </summary>
    public static string Validate(ZipArchive zip, ZipArchiveEntry json)
    {
        string text;
        try { using var s = json.Open(); using var r = new StreamReader(s); text = r.ReadToEnd(); }
        catch { return L.T("Could not read this character file.", "تعذرت قراءة ملف الشخصية."); }

        JsonElement root;
        try { using var doc = JsonDocument.Parse(text); root = doc.RootElement.Clone(); }
        catch { return L.T("This is not a valid character file (the json is broken).", "هذا ليس ملف شخصية صالح (الـ json تالف)."); }

        string stem = json.Name.Substring(0, json.Name.Length - JsonEnd.Length);
        string folder = json.FullName.Substring(0, json.FullName.Length - json.Name.Length);
        string bundle = null;
        bool known = false;
        foreach (var key in PackRoots)
        {
            if (!root.TryGetProperty(key, out var part) || part.ValueKind != JsonValueKind.Object) continue;
            known = true;
            if (part.TryGetProperty("assetBundleName", out var b) && b.ValueKind == JsonValueKind.String) bundle = b.GetString();
            if (string.IsNullOrEmpty(stem) && part.TryGetProperty("characterName", out var n) && n.ValueKind == JsonValueKind.String) stem = n.GetString();
            break;
        }
        if (!known) return L.T("This file has no character or skin inside.", "هذا الملف ما فيه شخصية ولا شكل.");

        // The 3D model is the bundle the json names, or (for skins, which don't name one) the file named like the json.
        string wanted = string.IsNullOrEmpty(bundle) ? stem : bundle;
        if (string.IsNullOrEmpty(wanted)) return null;
        bool present = zip.Entries.Any(e => e.Name.Length > 0
                      && e.FullName.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                      && (e.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                          || Path.GetFileNameWithoutExtension(e.Name).Equals(wanted, StringComparison.OrdinalIgnoreCase)));
        return present ? null
            : L.T($"This pack is incomplete: it needs the model file \"{wanted}\" next to its json, and the zip does not have it.",
                  $"هذه الحزمة ناقصة: تحتاج ملف المجسم \"{wanted}\" جنب الـ json، وهو غير موجود في الملف.");
    }

    public static bool IsCharacterJson(string file) => file.EndsWith(JsonEnd, StringComparison.OrdinalIgnoreCase);

    /// <summary>The "version" field of a character json ("1.4.0"), or null.</summary>
    public static string VersionOf(string jsonText)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            if (doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String) return v.GetString();
        }
        catch { }
        return null;
    }

    /// <summary>A character was installed this session: the game has to restart before its loader sees it.</summary>
    public static bool RestartNeeded { get; private set; }

    /// <summary>
    /// Is the character loader failing on this game version? The game says so in the log, not in the plugin's own lines:
    /// an il2cpp trampoline exception names the loader in the stack. Cached, because it is read while drawing.
    /// </summary>
    public static string LoaderProblem()
    {
        if (DateTime.UtcNow.Ticks - _problemAt < 30 * TimeSpan.TicksPerSecond) return _problem;
        _problemAt = DateTime.UtcNow.Ticks;
        _problem = null;
        try
        {
            string log = Path.Combine(Path.GetDirectoryName(Catalog.PluginsDir), "LogOutput.log");
            if (!File.Exists(log)) return _problem;
            using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length > 4_000_000) fs.Seek(-4_000_000, SeekOrigin.End);
            using var reader = new StreamReader(fs);
            string line;
            bool inException = false;
            int window = 0;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Contains("MissingMethodException") || line.Contains("MissingFieldException"))
                { inException = true; window = 0; continue; }
                if (!inException) continue;
                if (line.Contains("CustomCharacterLoader") || line.Contains("CharacterAdder"))
                {
                    // e.g. "Method not found: 'Void CharacterData.set_description(System.String)'"
                    _problem = L.T($"{LoaderName} does not work with this game version", $"{LoaderName} ما يشتغل مع نسخة اللعبة هذي");
                    break;
                }
                if (++window > 25) inException = false;
            }
        }
        catch { }
        return _problem;
    }

    private static string _problem;
    private static long _problemAt;

    /// <summary>What to add after "Restart the game...": a warning when the loader is broken, or nothing.</summary>
    public static string Warning()
    {
        string problem = LoaderProblem();
        return problem == null ? null : L.T($" But it will not show up yet: {problem}.", $" لكنها ما راح تظهر: {problem}.");
    }

    /// <summary>
    /// Same as <see cref="Warning"/>, plus "it needs the loader" when no character loader is installed at all
    /// (installs it in the background when it is on Thunderstore). Used where nothing else pulls the dependency in.
    /// </summary>
    public static string AfterInstall()
    {
        string warning = Warning();
        if (warning != null) return warning;
        if (FindLoader() != null) return null;
        EnsureLoader(out _); // starts installing it if Thunderstore's list is loaded
        return L.T(" It also needs CustomCharacterCreator: Mod Hub is installing it now (or install it from Get mods).",
                   " وتحتاج كمان CustomCharacterCreator: المدير يثبّتها الحين (أو ثبّتها من Get mods).");
    }

    /// <summary>Installs a character zip into BepInEx\plugins\CustomCharacters. Returns the character's name.</summary>
    public static string InstallCharacterZip(string zipPath)
    {
        RestartNeeded = true;
        Directory.CreateDirectory(Dir);
        return InstallZip(zipPath, Dir);
    }

    /// <summary>Is a character (or skin) with this name already installed?</summary>
    public static bool IsInstalled(string name)
    {
        try
        {
            if (string.IsNullOrEmpty(name) || !Directory.Exists(Dir)) return false;
            return Directory.GetFiles(Dir, "*" + JsonEnd, SearchOption.AllDirectories)
                            .Concat(Directory.GetFiles(Dir, "*" + JsonEnd + OffTag, SearchOption.AllDirectories))
                            .Any(f => NameOf(File.ReadAllText(f), "").Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    /// <summary>A zip with a character/skin in it (and no BepInEx mod of its own).</summary>
    public static bool IsCharacterZip(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            return zip.Entries.Any(e => IsCharacterJson(e.Name)) && !zip.Entries.Any(e => e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    /// <summary>The installed character loader, or null.</summary>
    public static Mod FindLoader() => Catalog.Mods.FirstOrDefault(m =>
        m.Source == "Thunderstore:" + LoaderPackage ||
        (m.Name ?? "").Replace(" ", "").IndexOf("CustomCharacter", StringComparison.OrdinalIgnoreCase) >= 0 ||
        (m.Guid ?? "").IndexOf("CustomCharacter", StringComparison.OrdinalIgnoreCase) >= 0);

    /// <summary>The folder the loader's DLL is in (characters go next to it).</summary>
    private static string LoaderDir(Mod loader)
    {
        if (!loader.IsFolder) return Path.GetDirectoryName(loader.Path);
        var dll = Directory.GetFiles(loader.Path, "*.dll", SearchOption.AllDirectories)
                      .Concat(Directory.GetFiles(loader.Path, "*.dll" + Catalog.OffSuffix, SearchOption.AllDirectories))
                      .FirstOrDefault(d => ModInfo.Read(d) != null);
        return dll != null ? Path.GetDirectoryName(dll) : loader.Path;
    }

    /// <summary>
    /// Makes sure the loader is there. Returns its folder, or null while it is being installed / can't be.
    /// "message" is set once when something needs telling.
    /// </summary>
    public static string EnsureLoader(out string message)
    {
        message = null;
        var loader = FindLoader();
        if (loader != null && !LoaderNeedsUpdate(loader)) return LoaderDir(loader);
        if (_loaderRequested) return null;

        var pkg = PreferredLoader();
        if (pkg == null)
        {
            _loaderPending = true;   // no list yet (offline): install it as soon as Thunderstore's list arrives
            Online.LoadStore(); // try again once the Thunderstore list is in
            return null;
        }
        _loaderRequested = true;
        message = L.T("Custom characters need CustomCharacterCreator: installing it first...", "الشخصيات المخصصة تحتاج CustomCharacterCreator: جاري تثبيته أول...");
        Online.InstallFromStore(pkg, msg => { _loaderRequested = false; Plugin.Logger.LogInfo($"Character loader: {msg}"); });
        return null;
    }

    /// <summary>The files that belong to a loose "X.custom.json": the json and the bundle(s) named X / X.*.</summary>
    public static List<string> LooseSet(string json)
    {
        string dir = Path.GetDirectoryName(json);
        string baseName = Path.GetFileName(json).Substring(0, Path.GetFileName(json).Length - JsonEnd.Length);
        var set = new List<string> { json };
        foreach (var f in Directory.GetFiles(dir))
        {
            string name = Path.GetFileName(f);
            if (f == json || IsCharacterJson(name) || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Equals(baseName, StringComparison.OrdinalIgnoreCase) || name.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase)) set.Add(f);
        }
        return set;
    }

    /// <summary>Character name from the json (falls back to the file name).</summary>
    public static string NameOf(string jsonText, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            foreach (var key in new[] { "character", "skin" })
                if (doc.RootElement.TryGetProperty(key, out var c))
                    foreach (var field in new[] { "characterName", "skinName", "name" })
                        if (c.TryGetProperty(field, out var n) && n.ValueKind == JsonValueKind.String) return n.GetString();
        }
        catch { }
        return fallback;
    }

    /// <summary>Installed characters, as rows for the Hub's mod list (they can be switched on/off like a mod).</summary>
    public static List<Mod> Scan()
    {
        var rows = new List<Mod>();
        try
        {
            SnapshotOnce();
            if (!Directory.Exists(Dir)) return rows;
            foreach (var folder in Directory.GetDirectories(Dir))
            {
                var jsons = Directory.GetFiles(folder, "*" + JsonEnd, SearchOption.AllDirectories);
                var offJsons = Directory.GetFiles(folder, "*" + JsonEnd + OffTag, SearchOption.AllDirectories);
                if (jsons.Length == 0 && offJsons.Length == 0) continue;
                rows.Add(Row(folder, jsons.Length > 0 ? jsons[0] : offJsons[0], true, jsons.Length > 0));
            }
            // Loose files left in CustomCharacters itself (older installs, or copied in by hand): one row per json.
            foreach (var json in Directory.GetFiles(Dir, "*" + JsonEnd))
                rows.Add(Row(json, json, false, true));
            foreach (var json in Directory.GetFiles(Dir, "*" + JsonEnd + OffTag))
                rows.Add(Row(json, json, false, false));
        }
        catch (Exception e) { Plugin.Logger.LogDebug($"Characters scan: {e.Message}"); }
        return rows;
    }

    /// <summary>Which characters were enabled when the game started (so a switch shows "after restart").</summary>
    private static void SnapshotOnce()
    {
        if (_snapshotTaken) return;
        _snapshotTaken = true;
        try
        {
            if (!Directory.Exists(Dir)) return;
            foreach (var folder in Directory.GetDirectories(Dir))
                if (Directory.GetFiles(folder, "*" + JsonEnd, SearchOption.AllDirectories).Length > 0) _onAtStart.Add(folder);
            foreach (var json in Directory.GetFiles(Dir, "*" + JsonEnd)) _onAtStart.Add(json);
        }
        catch { }
    }

    private static Mod Row(string path, string json, bool folder, bool on)
    {
        string text = null;
        try { text = File.ReadAllText(json.EndsWith(OffTag, StringComparison.OrdinalIgnoreCase) ? json.Substring(0, json.Length - OffTag.Length) : json); } catch { }
        string fallback = Path.GetFileName(folder ? path : json).Replace(JsonEnd, "").Replace(OffTag, "");
        return new Mod
        {
            Id = "character:" + fallback,
            Path = path, IsFolder = folder, IsCharacter = true,
            Name = text == null ? fallback : NameOf(text, fallback),
            Version = text == null ? null : VersionOf(text),
            On = on, LoadedNow = _onAtStart.Contains(path),
        };
    }

    /// <summary>Switches a character on/off by renaming its json: the game's loader only reads "*.custom.json".</summary>
    public static bool SetEnabled(Mod mod, bool on)
    {
        try
        {
            var files = mod.IsFolder
                ? Directory.GetFiles(mod.Path, "*" + JsonEnd + OffTag, SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(mod.Path, "*" + JsonEnd, SearchOption.AllDirectories)).ToList()
                : new List<string> { mod.Path };
            foreach (var f in files)
            {
                bool isOff = f.EndsWith(OffTag, StringComparison.OrdinalIgnoreCase);
                if (on && isOff) File.Move(f, f.Substring(0, f.Length - OffTag.Length));
                else if (!on && !isOff) File.Move(f, f + OffTag);
            }
            RestartNeeded = true;
            Plugin.Logger.LogInfo($"Character {mod.Name} turned {(on ? "on" : "off")}.");
            return true;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Could not switch character {mod.Name}: {e.Message}");
            return false;
        }
    }

    /// <summary>Moves a character to BepInEx\_hub_removed: its folder, or a loose json together with its bundle.</summary>
    public static bool Remove(Mod mod)
    {
        try
        {
            Directory.CreateDirectory(Catalog.RemovedDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            if (mod.IsFolder)
            {
                Directory.Move(mod.Path, Path.Combine(Catalog.RemovedDir, SafeName(mod.Id).Replace(':', '-') + "-" + stamp));
                return true;
            }
            string json = mod.Path;
            if (json.EndsWith(OffTag, StringComparison.OrdinalIgnoreCase)) json = json.Substring(0, json.Length - OffTag.Length);
            string dir = Path.GetDirectoryName(json);
            string baseName = Path.GetFileName(json).Substring(0, Path.GetFileName(json).Length - JsonEnd.Length);
            string dest = Path.Combine(Catalog.RemovedDir, SafeName(baseName) + "-" + stamp);
            Directory.CreateDirectory(dest);
            File.Move(json, Path.Combine(dest, Path.GetFileName(json)));
            foreach (var f in Directory.GetFiles(dir))
            {
                string n = Path.GetFileName(f);
                if (n.Equals(baseName, StringComparison.OrdinalIgnoreCase) || n.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase))
                    try { File.Move(f, Path.Combine(dest, n)); } catch { }
            }
            return true;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Could not remove character {mod.Name}: {e.Message}");
            return false;
        }
    }

    private static string SafeName(string s)
    {
        foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
        s = s.Trim().Trim('.');
        return string.IsNullOrEmpty(s) ? "Character" : s;
    }

    /// <summary>A folder for this character: the existing one when updating, and never a name a loose file already uses
    /// (a character called "GodOfDeath" next to its own "godofdeath" bundle would collide on Windows).</summary>
    private static string FolderFor(string root, string name)
    {
        string dest = Path.Combine(root, SafeName(name));
        if (Directory.Exists(dest) || !File.Exists(dest)) return dest;
        for (int i = 2; ; i++)
        {
            dest = Path.Combine(root, $"{SafeName(name)} ({i})");
            if (!Directory.Exists(dest) && !File.Exists(dest)) return dest;
        }
    }

    /// <summary>Copies loose character files into their own folder under the characters folder. Returns the character's name.</summary>
    public static string InstallLoose(List<string> files, string rootDir)
    {
        string name = NameOf(File.ReadAllText(files[0]), Path.GetFileName(files[0]).Replace(JsonEnd, ""));
        RestartNeeded = true;
        string dest = FolderFor(rootDir, name);
        Directory.CreateDirectory(dest);
        foreach (var f in files) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
        Plugin.Logger.LogInfo($"Installed custom character {name} into {dest}.");
        return name;
    }

    /// <summary>Extracts each character of a zip into its own folder under rootDir. Returns the character name(s).</summary>
    public static string InstallZip(string zipPath, string rootDir)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        Directory.CreateDirectory(rootDir);
        var names = new List<string>();
        foreach (var json in zip.Entries.Where(e => IsCharacterJson(e.Name)))
        {
            string folder = json.FullName.Substring(0, json.FullName.Length - json.Name.Length);
            string text;
            using (var r = new StreamReader(json.Open())) text = r.ReadToEnd();
            string name = NameOf(text, json.Name.Replace(JsonEnd, ""));
            string dest = FolderFor(rootDir, name);
            Directory.CreateDirectory(dest);
            foreach (var e in zip.Entries.Where(e => e.Name.Length > 0 && e.FullName.StartsWith(folder) && !e.FullName.Substring(folder.Length).Contains('/')))
            {
                string lower = e.Name.ToLowerInvariant();
                if (lower.EndsWith(".txt") || lower.EndsWith(".md") || lower == "manifest.json" || lower == "icon.png") continue; // readme / package files
                e.ExtractToFile(Path.Combine(dest, e.Name), true);
            }
            names.Add(name);
        }
        Plugin.Logger.LogInfo($"Installed custom character(s) {string.Join(", ", names)} into {rootDir}.");
        return string.Join(", ", names);
    }
}
