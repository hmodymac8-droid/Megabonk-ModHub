using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace ModHub;

/// <summary>One installed mod: a top-level folder in BepInEx\plugins, or a single DLL lying there.</summary>
internal sealed class Mod
{
    public string Id;                 // folder or file name inside plugins (without .off)
    public string Path;               // full path of the folder / file
    public bool IsFolder;
    public string Name, Version, Guid;
    public List<string> Dependencies = new();
    public Loader Loader = Loader.Unknown;
    public bool On;                   // files are enabled on disk (takes effect at next launch)
    public bool LoadedNow;            // running in this session
    public bool IsHub;
    public string Source;             // "Thunderstore:Owner-Name" when installed from Thunderstore
    public string PackageVersion;     // Thunderstore package version the Hub installed (null if unknown)
    public string Latest;             // newer version available (null = up to date / unknown)
    public string LatestUrl;
    public bool CanUpdateInHub;
    public bool Duplicate;            // an older second copy of a mod that is installed twice
    public int Errors;                // errors this mod wrote to the BepInEx log this session
    public bool IsMelon;              // a MelonLoader mod in BepInEx\melon-mods, run by Melon Bridge
    public bool IsCharacter;          // a custom character/skin in BepInEx\plugins\CustomCharacters (files, not a plugin)
    public string MelonState;         // this session: running / partial / off / refused (null before the bridge ran)
    public string MelonDetail;        // why it was refused or turned off
    public string Twin;               // the same mod is also installed for the other loader (name of the other copy)

    public bool WillChange => On != LoadedNow && !Duplicate;
}

/// <summary>
/// Finds every BepInEx mod (on or off) and switches them. A mod is switched off by renaming its DLLs to
/// *.dll.off and on by renaming them back: nothing is deleted, and it takes effect at the next launch.
/// (Windows allows renaming a loaded mod's files, so this happens right away.)
/// </summary>
internal static class Catalog
{
    public const string OffSuffix = ".off";
    public static string PluginsDir => Paths.PluginPath;
    public static string RemovedDir => System.IO.Path.Combine(Paths.BepInExRootPath, "_hub_removed");
    private static string StatePath => System.IO.Path.Combine(Paths.ConfigPath, "ModHub.state.json");

    public static List<Mod> Mods { get; private set; } = new();

    private sealed class State
    {
        public List<string> VanillaRestore { get; set; }           // mods to turn back on when leaving vanilla mode
        public Dictionary<string, string> Sources { get; set; } = new(); // mod id -> where it was installed from
        public Dictionary<string, string> PackageVersions { get; set; } = new(); // mod id -> Thunderstore version installed (a mod's own version can lag behind)
        public Dictionary<string, List<string>> Profiles { get; set; } = new(); // profile name -> mod ids that are on
        public Dictionary<string, Swap> Swaps { get; set; } = new();          // mod id -> the other version kept after an update
    }

    /// <summary>After an update the previous copy is kept in _hub_removed, so the player can go back (and redo the update).</summary>
    public sealed class Swap
    {
        public string OtherPath { get; set; }     // the other version's folder (in _hub_removed)
        public string OtherVersion { get; set; }
        public bool OtherIsOlder { get; set; }    // true: "Back to vX", false: "Redo vX"
    }
    private static State _state = new();
    private static readonly object StateLock = new(); // installs run on a background thread too

    public static bool Vanilla => _state.VanillaRestore != null;

    public static void Load()
    {
        try
        {
            if (File.Exists(StatePath)) _state = JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) ?? new State();
            _state.Sources ??= new();
            _state.PackageVersions ??= new();
            _state.Profiles ??= new();
            _state.Swaps ??= new();
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"Could not read Mod Hub state: {e.Message}"); }
    }

    private static void SaveState()
    {
        try { lock (StateLock) File.WriteAllText(StatePath, JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception e) { Plugin.Logger.LogWarning($"Could not save Mod Hub state: {e.Message}"); }
    }

    public static void SetSource(string id, string source, string packageVersion = null)
    {
        lock (StateLock)
        {
            _state.Sources[id] = source;
            if (packageVersion != null) _state.PackageVersions[id] = packageVersion; else _state.PackageVersions.Remove(id);
        }
        SaveState();
    }

    private static readonly HashSet<string> InstalledThisSession = new(StringComparer.OrdinalIgnoreCase);
    public static void NoteInstalled(string id)
    {
        lock (InstalledThisSession) InstalledThisSession.Add(id);
        lock (FolderVersions) FolderVersions.Clear(); // a folder's files changed: read versions again
    }
    public static bool WasInstalledThisSession(string id) { lock (InstalledThisSession) return InstalledThisSession.Contains(id); }
    /// <summary>An update cancelled before restarting: the game already runs this version, so nothing is pending.</summary>
    public static void ForgetInstalled(string id) { lock (InstalledThisSession) InstalledThisSession.Remove(id); }

    /// <summary>Old duplicate copies the Hub moved out by itself (shown to the player once, then cleared).</summary>
    public static readonly List<string> Cleaned = new();

    /// <summary>Re-reads the plugins folder. Old duplicate copies of a mod are moved to _hub_removed automatically.</summary>
    public static void Scan() => Scan(true);

    private static void Scan(bool cleanDuplicates)
    {
        var loaded = new Dictionary<string, BepInEx.PluginInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in IL2CPPChainloader.Instance.Plugins.Values)
            if (!string.IsNullOrEmpty(p.Location)) loaded[System.IO.Path.GetFullPath(p.Location)] = p;

        var list = new List<Mod>();
        if (!Directory.Exists(PluginsDir)) { Mods = list; return; }

        foreach (var dir in Directory.GetDirectories(PluginsDir))
        {
            var dlls = Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(dir, "*.dll" + OffSuffix, SearchOption.AllDirectories)).ToList();
            if (dlls.Count == 0) continue;
            list.Add(Build(System.IO.Path.GetFileName(dir), dir, true, dlls, loaded));
        }
        foreach (var file in Directory.GetFiles(PluginsDir, "*.dll").Concat(Directory.GetFiles(PluginsDir, "*.dll" + OffSuffix)))
            list.Add(Build(BaseName(System.IO.Path.GetFileName(file)), file, false, new List<string> { file }, loaded));
        ScanMelon(list, loaded);
        // Custom characters and skins: not plugins, but the player should see what is installed and be able to remove it.
        list.AddRange(Characters.Scan());

        // The same mod installed both as a BepInEx mod and as a MelonLoader mod: both would run.
        foreach (var m in list.Where(m => m.IsMelon && m.On))
        {
            var twin = list.FirstOrDefault(b => !b.IsMelon && b.On && SameName(b.Name, m.Name));
            if (twin != null) { m.Twin = twin.Name; twin.Twin = m.Name; }
        }

        // Same mod installed twice (same GUID, two folders): keep the running / newest one, flag the others.
        foreach (var group in list.Where(m => m.Guid != null).GroupBy(m => m.Guid).Where(g => g.Count() > 1))
        {
            var keep = group.OrderByDescending(m => m.LoadedNow).ThenByDescending(m => ParseVersion(m.Version)).First();
            foreach (var m in group) m.Duplicate = m != keep;
        }

        // A duplicate never runs (BepInEx loads one copy per GUID), so moving it out is safe.
        var stale = list.Where(m => m.Duplicate && !m.LoadedNow).ToList();
        if (cleanDuplicates && stale.Count > 0)
        {
            foreach (var m in stale)
                if (Uninstall(m))
                {
                    Cleaned.Add($"{m.Name} {m.Version}".Trim());
                    History.Add(History.Duplicate, m.Name, m.Version);
                }
            Scan(false); // once only, so a copy that can't be moved just stays flagged
            return;
        }

        Mods = list.OrderBy(m => m.IsHub ? 0 : 1).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Safety.Apply(Mods);
        MeasureRemoved();
    }

    private static Mod Build(string id, string path, bool isFolder, List<string> dlls, Dictionary<string, BepInEx.PluginInfo> loaded)
    {
        var mod = new Mod { Id = id, Path = path, IsFolder = isFolder };
        // On = its .NET DLLs are enabled (DLLs that aren't .NET, like steam_api64.dll, are switched along but don't decide).
        var managed = dlls.Where(d => ModInfo.Read(d) != null).ToList();
        mod.On = (managed.Count > 0 ? managed : dlls).Any(d => d.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

        // Mod Hub's own DLL first: in the r2modman layout its folder also holds Melon Bridge, and the row must be the Hub.
        foreach (var dll in dlls.OrderBy(d => ModInfo.Read(d)?.Guid == Plugin.Guid ? 0 : 1))
        {
            // Match by the enabled name: a running mod that was just switched off is now "x.dll.off".
            if (loaded.TryGetValue(System.IO.Path.GetFullPath(BaseName(dll)), out var info))
            {
                mod.LoadedNow = true;
                mod.Name ??= info.Metadata.Name;
                mod.Version ??= info.Metadata.Version.ToString();
                mod.Guid ??= info.Metadata.GUID;
            }
            var meta = ModInfo.Read(dll);
            if (meta == null) continue;
            if (meta.Loader != Loader.Unknown && mod.Loader == Loader.Unknown) mod.Loader = meta.Loader;
            if (meta.Guid == null && meta.Name == null) continue;
            mod.Name ??= meta.Name;
            mod.Version ??= meta.Version;
            mod.Guid ??= meta.Guid;
            mod.Dependencies.AddRange(meta.Dependencies);
        }
        mod.Name ??= id;
        mod.IsHub = mod.Guid == Plugin.Guid;
        lock (StateLock)
        {
            _state.Sources.TryGetValue(id, out mod.Source);
            _state.PackageVersions.TryGetValue(id, out mod.PackageVersion);
        }
        return mod;
    }

    /// <summary>MelonLoader mods: one per folder in BepInEx\melon-mods (or a loose DLL there), run by Melon Bridge.</summary>
    private static void ScanMelon(List<Mod> list, Dictionary<string, BepInEx.PluginInfo> loaded)
    {
        string root = Melon.ModsDir;
        if (!Directory.Exists(root)) return;
        var status = Melon.ReadStatus(out _, out _);
        void Add(string name, string path, bool isFolder, List<string> dlls)
        {
            // Only folders that actually hold a MelonLoader mod (helpers like UserLibs travel inside a mod's folder).
            if (!dlls.Any(d => ModInfo.Read(d)?.Loader == Loader.MelonLoader)) return;
            var mod = Build(Melon.IdFor(name), path, isFolder, dlls, loaded);
            mod.IsMelon = true;
            mod.Loader = Loader.MelonLoader;
            if (mod.Name == mod.Id) mod.Name = name;
            mod.Guid = "melon:" + mod.Name;
            mod.Dependencies.Add(Melon.BridgeGuid); // turning a Melon mod on turns the bridge on; turning the bridge off turns them off
            if (status.TryGetValue(name, out var s))
            {
                mod.MelonState = s.State;
                mod.MelonDetail = s.Detail;
                mod.LoadedNow = s.State == "running" || s.State == "partial";
            }
            list.Add(mod);
        }
        foreach (var dir in Directory.GetDirectories(root))
        {
            var dlls = Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(dir, "*.dll" + OffSuffix, SearchOption.AllDirectories)).ToList();
            if (dlls.Count > 0) Add(System.IO.Path.GetFileName(dir), dir, true, dlls);
        }
        foreach (var file in Directory.GetFiles(root, "*.dll").Concat(Directory.GetFiles(root, "*.dll" + OffSuffix)))
            Add(System.IO.Path.GetFileNameWithoutExtension(BaseName(file)), file, false, new List<string> { file });
    }

    private static bool SameName(string a, string b)
    {
        string N(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant()
            .Replace("melonloader", "").Replace("bepinex", "").Replace("megabonk", "");
        return N(a).Length > 2 && N(a) == N(b);
    }

    private static Version ParseVersion(string v) => System.Version.TryParse((v ?? "").Split('-', '+', ' ')[0], out var p) ? p : new Version(0, 0);

    private static string BaseName(string file) => file.EndsWith(OffSuffix) ? file.Substring(0, file.Length - OffSuffix.Length) : file;

    // ------------------------------------------------------------------ switching

    /// <summary>Turns a mod on or off (and returns what else had to change because of dependencies).</summary>
    public static List<string> Set(Mod mod, bool on, bool withDependencies = true)
    {
        var also = new List<string>();
        if (mod.IsHub || mod.On == on) return also;
        Rename(mod, on);

        if (withDependencies)
        {
            if (on)
            {
                // Turning on: its hard dependencies must be on too.
                foreach (var dep in mod.Dependencies)
                {
                    var m = Mods.FirstOrDefault(x => x.Guid == dep);
                    if (m != null && !m.On) { Rename(m, true); also.Add(m.Name); }
                }
            }
            else
            {
                // Turning off: mods that need it can't load either.
                foreach (var m in Mods.Where(x => x.On && !x.IsHub && mod.Guid != null && x.Dependencies.Contains(mod.Guid)))
                { Rename(m, false); also.Add(m.Name); }
            }
        }
        return also;
    }

    private static void Rename(Mod mod, bool on)
    {
        var files = mod.IsFolder
            ? Directory.GetFiles(mod.Path, on ? "*.dll" + OffSuffix : "*.dll", SearchOption.AllDirectories)
            : new[] { mod.Path };
        foreach (var f in files)
        {
            try
            {
                string target = on ? BaseName(f) : (f.EndsWith(OffSuffix) ? f : f + OffSuffix);
                if (target == f) continue;
                if (File.Exists(target))
                {
                    // A second copy with the target name: move it aside instead of deleting anything.
                    Directory.CreateDirectory(RemovedDir);
                    File.Move(target, System.IO.Path.Combine(RemovedDir, $"{System.IO.Path.GetFileName(target)}-{DateTime.Now:yyyyMMdd-HHmmss}"));
                }
                File.Move(f, target);
                if (!mod.IsFolder) mod.Path = target;
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"Could not switch {f}: {e.Message}"); }
        }
        mod.On = on;
        Plugin.Logger.LogInfo($"{mod.Name}: turned {(on ? "on" : "off")} (from next launch).");
    }

    // ------------------------------------------------------------------ vanilla mode

    /// <summary>Turns every mod except the Hub off and remembers which were on.</summary>
    public static void EnterVanilla()
    {
        if (Vanilla) return;
        lock (StateLock) _state.VanillaRestore = Mods.Where(m => m.On && !m.IsHub).Select(m => m.Id).ToList();
        foreach (var m in Mods.Where(m => m.On && !m.IsHub))
        {
            if (m.IsCharacter) Characters.SetEnabled(m, false); // characters are files, not DLLs
            else Rename(m, false);
        }
        SaveState();
        History.Add(History.VanillaOn, null, detail: _state.VanillaRestore.Count.ToString());
    }

    /// <summary>Turns back on exactly the mods that were on before vanilla mode.</summary>
    public static void LeaveVanilla()
    {
        if (!Vanilla) return;
        int back = 0;
        foreach (var m in Mods.Where(m => !m.On && _state.VanillaRestore.Contains(m.Id)))
        {
            if (m.IsCharacter) Characters.SetEnabled(m, true);
            else Rename(m, true);
            back++;
        }
        History.Add(History.VanillaOff, null, detail: back.ToString());
        lock (StateLock) _state.VanillaRestore = null;
        SaveState();
    }

    // ------------------------------------------------------------------ profiles

    public static IReadOnlyDictionary<string, List<string>> Profiles => _state.Profiles;

    /// <summary>Saves which mods are on now. Returns how many.</summary>
    public static int SaveProfile(string name)
    {
        var on = Mods.Where(m => m.On && !m.IsHub).Select(m => m.Id).ToList();
        lock (StateLock) _state.Profiles[name] = on;
        SaveState();
        History.Add(History.ProfileSave, name, detail: on.Count.ToString());
        return on.Count;
    }

    public static void DeleteProfile(string name) { lock (StateLock) _state.Profiles.Remove(name); SaveState(); }

    /// <summary>Turns on exactly the mods in the profile and the rest off. Returns how many changed.</summary>
    public static int ApplyProfile(string name)
    {
        if (!_state.Profiles.TryGetValue(name, out var ids)) return 0;
        int changed = 0;
        foreach (var m in Mods.Where(m => !m.IsHub))
        {
            bool want = ids.Contains(m.Id);
            if (m.On == want) continue;
            // Characters are switched by their json, not by DLLs (a character folder has no DLL to rename).
            if (m.IsCharacter) Characters.SetEnabled(m, want);
            else Rename(m, want);
            changed++;
        }
        lock (StateLock) _state.VanillaRestore = null; // a profile replaces vanilla mode
        SaveState();
        History.Add(History.ProfileApply, name, detail: changed.ToString());
        return changed;
    }

    // ------------------------------------------------------------------ removed mods folder

    public static int RemovedCount { get; private set; }
    public static long RemovedBytes { get; private set; }

    /// <summary>Counts what's in BepInEx_hub_removed (old copies and removed mods).</summary>
    public static void MeasureRemoved()
    {
        try
        {
            if (!Directory.Exists(RemovedDir)) { RemovedCount = 0; RemovedBytes = 0; return; }
            var entries = Directory.GetFileSystemEntries(RemovedDir);
            RemovedCount = entries.Length;
            RemovedBytes = Directory.GetFiles(RemovedDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        }
        catch { RemovedCount = 0; RemovedBytes = 0; }
    }

    /// <summary>Files still open in the running game after an "empty" attempt (a loaded DLL cannot be deleted).</summary>
    public static int LockedInRemoved { get; private set; }

    /// <summary>Deletes everything in BepInEx_hub_removed for good (the player confirms first).
    /// A DLL the running game has loaded cannot be deleted, so everything else is removed and the rest is counted.</summary>
    public static bool EmptyRemoved()
    {
        int locked = 0;
        try
        {
            if (Directory.Exists(RemovedDir))
            {
                foreach (var file in Directory.GetFiles(RemovedDir, "*", SearchOption.AllDirectories))
                    try { File.Delete(file); } catch { locked++; }
                foreach (var dir in Directory.GetDirectories(RemovedDir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                    try { Directory.Delete(dir, false); } catch { }
                try { Directory.Delete(RemovedDir, false); } catch { }
            }
            LockedInRemoved = locked;
            MeasureRemoved();
            if (locked > 0)
            {
                Plugin.Logger.LogWarning($"Emptied BepInEx\\_hub_removed except {locked} file(s) the running game still has open.");
                return false;
            }
            Plugin.Logger.LogInfo(@"Emptied BepInEx\_hub_removed.");
            return true;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Could not empty _hub_removed: {e.Message}");
            MeasureRemoved();
            return false;
        }
    }

    // ------------------------------------------------------------------ going back after an update

    /// <summary>Called by the installer after an update moved the previous copy aside.</summary>
    public static void RememberUpdate(string id, string oldPath, string oldVersion)
    {
        if (string.IsNullOrEmpty(oldPath) || !Directory.Exists(oldPath)) return;
        lock (FolderVersions) FolderVersions.Clear(); // files just changed
        lock (StateLock) _state.Swaps[id] = new Swap { OtherPath = oldPath, OtherVersion = FolderVersion(oldPath) ?? oldVersion ?? "?", OtherIsOlder = true };
        SaveState();
    }

    /// <summary>The other version of this mod that can be swapped in, or null (also when its folder was emptied).</summary>
    public static Swap SwapFor(Mod mod)
    {
        if (mod.IsHub || !mod.IsFolder) return null;
        lock (StateLock)
        {
            if (!_state.Swaps.TryGetValue(mod.Id, out var s) || !Directory.Exists(s.OtherPath)) return null;
            // The version shown is read from the saved copy itself, so it is always right.
            s.OtherVersion = FolderVersion(s.OtherPath) ?? s.OtherVersion;
            return s;
        }
    }

    /// <summary>The version of the mod's files on disk (what runs after the next restart).</summary>
    public static string InstalledVersion(Mod mod) => (mod.IsFolder ? FolderVersion(mod.Path) : null) ?? mod.PackageVersion ?? mod.Version;

    /// <summary>The kept copy is newer than the installed one: the swap button is an instant "Update" (no download).</summary>
    public static bool SwapIsNewer(Mod mod, Swap s) => Updates.Newer(s.OtherVersion, InstalledVersion(mod));

    private static readonly Dictionary<string, string> FolderVersions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The version of the mod in a folder, from its own files: Thunderstore's manifest.json first (a mod's DLL version
    /// can lag behind its package version), else the DLL's [BepInPlugin] / [MelonInfo]. Cached: rows ask every frame.
    /// </summary>
    public static string FolderVersion(string dir)
    {
        lock (FolderVersions)
            if (FolderVersions.TryGetValue(dir, out var cached)) return cached;
        string v = null;
        try
        {
            string manifest = System.IO.Path.Combine(dir, "manifest.json");
            if (File.Exists(manifest))
                using (var doc = JsonDocument.Parse(File.ReadAllText(manifest)))
                    if (doc.RootElement.TryGetProperty("version_number", out var n)) v = n.GetString();
            if (v == null)
                foreach (var dll in Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories).Concat(Directory.GetFiles(dir, "*.dll" + OffSuffix, SearchOption.AllDirectories)))
                {
                    var meta = ModInfo.Read(dll);
                    if (meta?.Version != null && (meta.Guid != null || meta.Name != null)) { v = meta.Version; break; }
                }
        }
        catch { }
        lock (FolderVersions) FolderVersions[dir] = v;
        return v;
    }

    /// <summary>
    /// Swaps the installed copy with the kept one ("Back to vX" / "Redo vY"). Nothing is deleted: the current copy is
    /// kept in _hub_removed so the swap can be done again. The mod stays ON or OFF as it was. Takes effect at the next launch.
    /// </summary>
    public static bool SwapVersion(Mod mod, out string error)
    {
        error = null;
        var s = SwapFor(mod);
        if (s == null) { error = "the other version isn't there anymore"; return false; }
        // The copy being put aside: its version from its own files (the running version can be a different one).
        string currentVersion = FolderVersion(mod.Path) ?? mod.PackageVersion ?? mod.Version;
        string stash = System.IO.Path.Combine(RemovedDir, $"{mod.Id}-{currentVersion}-{DateTime.Now:yyyyMMdd-HHmmss}");
        try
        {
            Directory.CreateDirectory(RemovedDir);
            Directory.Move(mod.Path, stash);
            try { Directory.Move(s.OtherPath, mod.Path); }
            catch { Directory.Move(stash, mod.Path); throw; } // put the current copy back if the swap can't finish
        }
        catch (Exception e) { error = e.Message; Plugin.Logger.LogWarning($"Could not swap {mod.Name}: {e.Message}"); return false; }

        // Keep the mod ON or OFF as the player had it (the kept copy may have been saved in the other state).
        Rename(new Mod { Id = mod.Id, Path = mod.Path, IsFolder = true, Name = mod.Name }, mod.On);
        lock (StateLock)
        {
            _state.Swaps[mod.Id] = new Swap { OtherPath = stash, OtherVersion = currentVersion, OtherIsOlder = !s.OtherIsOlder };
            lock (FolderVersions) { FolderVersions.Remove(mod.Path); FolderVersions.Remove(stash); FolderVersions.Remove(s.OtherPath); } // contents moved
            if (_state.PackageVersions.ContainsKey(mod.Id)) _state.PackageVersions[mod.Id] = s.OtherVersion;
        }
        SaveState();
        NoteInstalled(mod.Id);
        Plugin.Logger.LogInfo($"{mod.Name}: switched from {mod.Version} to {s.OtherVersion} (from next launch).");
        return true;
    }

    // ------------------------------------------------------------------ uninstall

    /// <summary>Moves a mod out of plugins (into BepInEx\_hub_removed), so it can be put back by hand.</summary>
    public static bool Uninstall(Mod mod)
    {
        if (mod.IsHub) return false;
        try
        {
            Directory.CreateDirectory(RemovedDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string target = System.IO.Path.Combine(RemovedDir, $"{mod.Id}-{stamp}" + (mod.IsFolder ? "" : System.IO.Path.GetExtension(mod.Path)));
            if (mod.IsFolder) Directory.Move(mod.Path, target); else File.Move(mod.Path, target);
            lock (StateLock) { _state.Sources.Remove(mod.Id); _state.PackageVersions.Remove(mod.Id); _state.Swaps.Remove(mod.Id); }
            SaveState();
            Plugin.Logger.LogInfo($"{mod.Name}: uninstalled (moved to {target}).");
            return true;
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"Could not uninstall {mod.Name}: {e.Message}"); return false; }
    }
}
