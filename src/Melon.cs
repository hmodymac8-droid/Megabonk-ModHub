using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;

namespace ModHub;

/// <summary>
/// Mod Hub's side of Melon Bridge (the plugin that runs MelonLoader mods inside BepInEx). MelonLoader mods live in
/// BepInEx\melon-mods\&lt;Folder&gt;\. The Hub reads the bridge's status file and calls its check by reflection,
/// so the Hub keeps working when the bridge is missing or switched off.
/// </summary>
internal static class Melon
{
    public const string BridgeGuid = "modhub.melonbridge";
    public const string IdPrefix = "melon-";
    public static string ModsDir => Path.Combine(Paths.BepInExRootPath, "melon-mods");
    // Normally plugins\MelonBridge. Installed by r2modman / Gale it sits inside the package folder
    // (plugins\AhmedHus21-ModHub\MelonBridge). Looked up once and remembered (rows ask every frame).
    private static string _bridgeDir;
    private static DateTime _bridgeLookedAt;
    private static string BridgeDir
    {
        get
        {
            string normal = Path.Combine(Paths.PluginPath, "MelonBridge");
            if (Directory.Exists(normal)) return normal;
            if (_bridgeDir != null && Directory.Exists(_bridgeDir)) return _bridgeDir;
            if (DateTime.UtcNow - _bridgeLookedAt > TimeSpan.FromSeconds(10))
            {
                _bridgeLookedAt = DateTime.UtcNow;
                try
                {
                    _bridgeDir = Directory.GetFiles(Paths.PluginPath, "MelonBridge.dll*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith("MelonBridge.dll", StringComparison.OrdinalIgnoreCase)
                                 || f.EndsWith("MelonBridge.dll" + Catalog.OffSuffix, StringComparison.OrdinalIgnoreCase))
                        .Select(Path.GetDirectoryName).FirstOrDefault();
                }
                catch { _bridgeDir = null; }
            }
            return _bridgeDir ?? normal;
        }
    }
    private static string StatusFile => Path.Combine(Paths.ConfigPath, "MelonBridge.status");

    /// <summary>The bridge's files are there (switched on or off).</summary>
    public static bool BridgeInstalled =>
        File.Exists(Path.Combine(BridgeDir, "MelonBridge.dll")) || File.Exists(Path.Combine(BridgeDir, "MelonBridge.dll" + Catalog.OffSuffix));

    /// <summary>The bridge will run at the next start.</summary>
    public static bool BridgeOn => File.Exists(Path.Combine(BridgeDir, "MelonBridge.dll"));

    public static string IdFor(string folder) => IdPrefix + folder;
    public static string FolderOf(string id) => id.StartsWith(IdPrefix) ? id.Substring(IdPrefix.Length) : id;

    // ------------------------------------------------------------------ status of this session

    public sealed class ModStatus { public string State, Detail; }

    /// <summary>Folder (or loose file) name → what the bridge did with it this session. Empty before the bridge has run.</summary>
    public static Dictionary<string, ModStatus> ReadStatus(out string bridgeState, out string bridgeNote)
    {
        var map = new Dictionary<string, ModStatus>(StringComparer.OrdinalIgnoreCase);
        bridgeState = null; bridgeNote = null;
        try
        {
            var fi = new FileInfo(StatusFile);
            // Only this session's file counts (an old one would show mods as running that aren't).
            using (var me = Process.GetCurrentProcess())
                if (!fi.Exists || fi.LastWriteTime < me.StartTime) return map;
            foreach (var line in File.ReadAllLines(StatusFile))
            {
                if (line.StartsWith("state=")) bridgeState = line.Substring(6);
                else if (line.StartsWith("note=")) bridgeNote = line.Substring(5);
                else if (line.StartsWith("mod="))
                {
                    var p = line.Substring(4).Split('|');
                    if (p.Length < 5) continue;
                    string top = p[0].Split('\\', '/')[0];
                    if (top.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) top = Path.GetFileNameWithoutExtension(top);
                    // A folder can hold several DLLs: a problem wins over "running".
                    if (!map.TryGetValue(top, out var s) || s.State == "running") map[top] = new ModStatus { State = p[3], Detail = p[4] };
                }
            }
        }
        catch { }
        return map;
    }

    // ------------------------------------------------------------------ labels in Get mods

    // Nexus has no "which loader" field, so these come from the Melon Bridge survey of every Megabonk MelonLoader
    // mod (2026-10-21). Anything newer is still checked by the bridge when it's installed.
    private static readonly HashSet<int> NexusMelonOnly = new()
    {
        1, 2, 4, 6, 11, 15, 16, 17, 19, 20, 22, 24, 25, 28, 30, 33, 35, 40, 41, 43, 49, 50, 52, 53, 69, 72, 78, 87,
        114, 115, 125, 145, 146, 153, 156, 164, 166, 171, 174, 178, 182, 189,
    };
    private static readonly HashSet<int> NexusBoth = new() { 5, 12, 13, 23, 29, 45, 47, 80, 88, 89, 90, 94, 96, 97, 98, 101, 183 };
    private static readonly Dictionary<int, (string En, string Ar)> NexusBroken = new()
    {
        [1] = ("Doesn't work: made for the Megabonk Demo, not the full game.", "ما يشتغل: مصنوع لنسخة Megabonk التجريبية، مو للعبة الكاملة."),
        [47] = ("Its MelonLoader file doesn't work in this game version: use its BepInEx file.", "ملف MelonLoader حقه ما يشتغل في نسخة اللعبة هذي: استخدم ملف BepInEx."),
        [87] = ("Doesn't work in this game version: try \"Chaos Stat Blacklist Revived\".", "ما يشتغل في نسخة اللعبة هذي: جرّب \"Chaos Stat Blacklist Revived\"."),
    };

    /// <summary>Small tag after a mod's name in Get mods: "Melon", "BepInEx + Melon", or null.</summary>
    public static string TagFor(StorePackage p)
    {
        if (p.IsNexus) return NexusMelonOnly.Contains(p.NexusId) ? "Melon" : NexusBoth.Contains(p.NexusId) ? "BepInEx + Melon" : null;
        return p.NeedsMelon ? "Melon" : null;
    }

    /// <summary>Why a mod is known not to work (shown in red instead of its description), or null.</summary>
    public static string BrokenReason(StorePackage p) =>
        p.IsNexus && NexusBroken.TryGetValue(p.NexusId, out var r) ? L.T(r.En, r.Ar) : null;

    // ------------------------------------------------------------------ checking before install

    public sealed class CheckResult { public string Verdict, Reason; }

    private static MethodInfo _check;

    /// <summary>Asks the running bridge whether a MelonLoader DLL will work. Null when the bridge isn't running.</summary>
    public static CheckResult Check(string dll, IEnumerable<string> libraryDirs)
    {
        try
        {
            _check ??= AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "MelonBridge")
                ?.GetType("MelonBridge.Api")?.GetMethod("Check", BindingFlags.Public | BindingFlags.Static);
            if (_check == null) return null;
            var r = (string[])_check.Invoke(null, new object[] { dll, libraryDirs?.ToArray() ?? Array.Empty<string>() });
            return new CheckResult { Verdict = r[0], Reason = r[1] };
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning("Melon Bridge check failed: " + e.Message);
            return null;
        }
    }
}
