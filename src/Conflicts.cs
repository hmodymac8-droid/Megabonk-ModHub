using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ModHub;

/// <summary>
/// Finds mods that change the same game function in a way that can clash. Mods change the game with Harmony patches,
/// and Harmony can list them all. Two mods patching one function is usually fine (two "after" patches simply both run),
/// so only the risky cases count: two mods rewriting the same code (transpilers), or one mod able to skip the function
/// (a prefix that returns bool) while another changes it too. Only mods running this session are seen, and only
/// Harmony patches (not native hooks). Checked each time the Hub opens, because some mods patch later in the game.
/// </summary>
internal static class Conflicts
{
    public sealed class Clash { public string With; public List<string> Methods = new(); }

    private static Dictionary<string, List<Clash>> _byMod = new(StringComparer.OrdinalIgnoreCase);
    private static string _lastSummary;

    /// <summary>The risky clashes of a mod (by its name), empty if none.</summary>
    public static List<Clash> For(Mod m) => m?.Name != null && _byMod.TryGetValue(m.Name, out var c) ? c : new List<Clash>();

    /// <summary>Every clash once ("A ↔ B: methods"), for the report.</summary>
    public static List<string> Lines()
    {
        var seen = new HashSet<string>();
        var lines = new List<string>();
        foreach (var kv in _byMod)
            foreach (var c in kv.Value)
            {
                string pair = string.CompareOrdinal(kv.Key, c.With) < 0 ? kv.Key + " | " + c.With : c.With + " | " + kv.Key;
                if (seen.Add(pair)) lines.Add($"{kv.Key} <-> {c.With}: {string.Join(", ", c.Methods.Take(5))}{(c.Methods.Count > 5 ? $" (+{c.Methods.Count - 5})" : "")}");
            }
        return lines;
    }

    public static void Check()
    {
        try
        {
            var owners = ModsByAssembly();
            var found = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase); // mod -> other mod -> methods
            int patched = 0, shared = 0;
            foreach (var method in Harmony.GetAllPatchedMethods().ToList())
            {
                var info = Harmony.GetPatchInfo(method);
                if (info == null) continue;
                patched++;
                // Who patches this function, and how.
                var byMod = new Dictionary<string, (bool skip, bool rewrite)>(StringComparer.OrdinalIgnoreCase);
                void Add(IEnumerable<Patch> patches, bool prefix, bool transpiler)
                {
                    foreach (var p in patches)
                    {
                        string mod = OwnerOf(p.PatchMethod, owners);
                        if (mod == null) continue;
                        byMod.TryGetValue(mod, out var k);
                        byMod[mod] = (k.skip || (prefix && p.PatchMethod.ReturnType == typeof(bool)), k.rewrite || transpiler);
                    }
                }
                Add(info.Prefixes, true, false);
                Add(info.Postfixes, false, false);
                Add(info.Transpilers, false, true);
                Add(info.Finalizers, false, false);
                if (byMod.Count < 2) continue;
                shared++;
                string name = $"{method.DeclaringType?.Name}.{method.Name}";
                foreach (var a in byMod)
                    foreach (var b in byMod)
                    {
                        if (a.Key == b.Key) continue;
                        bool risky = (a.Value.rewrite && b.Value.rewrite) || a.Value.skip || b.Value.skip;
                        if (!risky) continue;
                        if (!found.TryGetValue(a.Key, out var others)) found[a.Key] = others = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                        if (!others.TryGetValue(b.Key, out var methods)) others[b.Key] = methods = new HashSet<string>();
                        methods.Add(name);
                    }
            }
            var byMod2 = new Dictionary<string, List<Clash>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in found)
            {
                var list = new List<Clash>();
                foreach (var o in kv.Value)
                {
                    var methods = new List<string>(o.Value);
                    methods.Sort(StringComparer.Ordinal);
                    list.Add(new Clash { With = o.Key, Methods = methods });
                }
                byMod2[kv.Key] = list;
            }
            _byMod = byMod2;
            // One line in the log (only when it changes), so a player's report shows what was found.
            string summary = $"Mod Hub: {patched} game functions patched, {shared} by more than one mod, {Lines().Count} possible clash(es)"
                             + (Lines().Count > 0 ? ": " + string.Join("; ", Lines()) : ".");
            if (summary != _lastSummary) { _lastSummary = summary; Plugin.Logger.LogInfo(summary); }
        }
        catch (Exception e) { Plugin.Logger.LogDebug($"Conflicts: {e.Message}"); }
    }

    /// <summary>Mod name by the full path of each of its DLLs (enabled name; a mod switched off this session still runs).</summary>
    private static Dictionary<string, string> ModsByAssembly()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in Catalog.Mods.Where(m => !m.IsCharacter && !m.IsHub))
        {
            try
            {
                var files = m.IsFolder ? Directory.GetFiles(m.Path, "*.dll*", SearchOption.AllDirectories) : new[] { m.Path };
                foreach (var f in files)
                {
                    string dll = f.EndsWith(Catalog.OffSuffix, StringComparison.OrdinalIgnoreCase) ? f.Substring(0, f.Length - Catalog.OffSuffix.Length) : f;
                    map[Path.GetFullPath(dll)] = m.Name;
                }
            }
            catch { }
        }
        return map;
    }

    /// <summary>The mod a patch belongs to; null for Mod Hub, Melon Bridge and the loaders themselves.</summary>
    private static string OwnerOf(MethodInfo patch, Dictionary<string, string> owners)
    {
        var asm = patch?.DeclaringType?.Assembly;
        if (asm == null || asm == typeof(Plugin).Assembly) return null;
        string name = asm.GetName().Name ?? "";
        if (name == "MelonBridge" || name.StartsWith("BepInEx") || name.StartsWith("Il2CppInterop") || name == "0Harmony" || name.StartsWith("HarmonyX")) return null;
        try
        {
            string location = asm.Location;
            if (!string.IsNullOrEmpty(location) && owners.TryGetValue(Path.GetFullPath(location), out var mod)) return mod;
        }
        catch { /* dynamic assembly */ }
        // Converted MelonLoader mods run from Melon Bridge's cache: their assembly name is the mod's.
        return Catalog.Mods.FirstOrDefault(m => Updates.Same(m.Name, name))?.Name ?? name;
    }
}
