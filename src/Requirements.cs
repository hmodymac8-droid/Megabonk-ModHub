using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Unity.IL2CPP;

namespace ModHub;

/// <summary>One mod that something from Downloads or the drop folder still needs.</summary>
internal sealed class Need
{
    public string Name;
    public StorePackage Package;   // on Thunderstore: installs in one click
    public string MustHave;        // the plugin GUID the package has to contain (it was found by name)
    public string ModId;           // already installed but off: switching it on is enough
    public string PageUrl;         // only on Nexus: its page
    public string For;             // the mod that needs it
}

/// <summary>
/// A mod from Thunderstore brings what it needs. One from Downloads or the drop folder doesn't, and without those mods
/// it silently fails to load. So after such an install this works out what's missing, and Panel offers to fetch it.
/// A Thunderstore zip names packages exactly; a zip without a manifest only has the plugin GUIDs in its DLLs, and a GUID
/// says nothing about the package: that's only offered when one package has exactly that name, and the install checks
/// the GUID is really inside. Game thread only.
/// </summary>
internal static class Requirements
{
    private sealed class Ask { public string Mod; public List<string> Packages, Guids; }

    private static readonly List<Ask> Asks = new();

    /// <summary>What's missing right now (empty = nothing to ask).</summary>
    public static List<Need> Needs { get; private set; } = new();

    /// <summary>The mods those are for, for the banner.</summary>
    public static string For { get; private set; } = "";

    /// <summary>Remembers a mod just installed from a file. <see cref="Refresh"/> (after the scan) works out what it lacks.</summary>
    public static void Add(InstallCheck c)
    {
        if (c.IsCharacter) return; // characters have their own loader check
        var packages = c.ThunderstoreDeps.ToList();
        var guids = packages.Count > 0 ? new List<string>() : c.Needs; // the same requirements twice otherwise
        if (packages.Count + guids.Count > 0) Asks.Add(new Ask { Mod = c.ModName ?? c.FolderName, Packages = packages, Guids = guids });
    }

    public static void Dismiss() { Asks.Clear(); Needs = new(); For = ""; }

    /// <summary>Called after every scan and list download: what got installed since drops off, and what Thunderstore has fills in.</summary>
    public static void Refresh()
    {
        if (Asks.Count == 0) return;
        var needs = new List<Need>();
        var mods = new List<string>();
        foreach (var ask in Asks)
        {
            var found = ask.Packages.Select(ForPackage).Concat(ask.Guids.Select(ForGuid)).Where(n => n != null).ToList();
            if (found.Count > 0) mods.Add(ask.Mod);
            foreach (var n in found)
                if (!needs.Any(x => x.Name == n.Name)) { n.For = ask.Mod; needs.Add(n); }
        }
        Needs = needs;
        For = string.Join(", ", mods);
        if (needs.Count == 0) Asks.Clear();
    }

    /// <summary>A manifest's "Owner-Name-1.2.3".</summary>
    private static Need ForPackage(string dep)
    {
        int last = dep.LastIndexOf('-');
        string id = last > 0 ? dep.Substring(0, last) : dep;
        if (id.Contains("BepInExPack") || id.EndsWith("-MelonLoader")) return null; // loaders: Melon Bridge stands in for MelonLoader
        // The original character loader stops working after a game update: same rule as a Thunderstore install.
        if (id.IndexOf("CustomCharacterCreator", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var loader = Characters.FindLoader();
            if (Characters.LoaderNeedsUpdate(loader))
                return Characters.PreferredLoader() is { } pkg ? new Need { Name = pkg.DisplayName, Package = pkg } : NotOnStore("CustomCharacterCreator");
            return Off(loader);
        }
        string name = id.Substring(id.IndexOf('-') + 1).Replace('_', ' ');
        var have = Catalog.Mods.FirstOrDefault(m => m.Source == "Thunderstore:" + id || m.Id == id || Updates.Same(m.Name, name));
        if (have != null) return Off(have);
        var store = Online.Store?.FirstOrDefault(p => p.FullName == id);
        return store != null ? new Need { Name = store.DisplayName, Package = store } : NotOnStore(name);
    }

    /// <summary>A [BepInDependency] GUID, like "com.author.somelib".</summary>
    private static Need ForGuid(string guid)
    {
        var have = Catalog.Mods.FirstOrDefault(m => m.Guids.Contains(guid));
        if (have != null) return Off(have);
        if (IL2CPPChainloader.Instance.Plugins.ContainsKey(guid)) return null;
        string name = guid.Substring(guid.LastIndexOf('.') + 1);
        var matches = Online.Store?.Where(p => Updates.Same(p.Name, name) || Updates.Same(p.Name, guid)).Take(2).ToList();
        return matches?.Count == 1 ? new Need { Name = matches[0].DisplayName, Package = matches[0], MustHave = guid } : NotOnStore(name);
    }

    private static Need Off(Mod m) => m.On ? null : new Need { Name = m.Name, ModId = m.Id };

    private static Need NotOnStore(string name)
    {
        var nexus = Online.Nexus?.FirstOrDefault(n => Updates.Same(n.Name, name));
        return new Need { Name = nexus?.Name ?? name, PageUrl = nexus == null ? null : $"https://www.nexusmods.com/megabonk/mods/{nexus.Id}?tab=files" };
    }
}
