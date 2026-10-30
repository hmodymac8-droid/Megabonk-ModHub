using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ModHubInstaller;

/// <summary>Finds Megabonk through Steam: every Steam library (any drive), then the game's own install folder name.</summary>
internal static class GameFinder
{
    public const string AppId = "3405340";
    public const string Exe = "Megabonk.exe";

    public static bool IsGameFolder(string dir) => !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, Exe));

    public static string Find()
    {
        foreach (var lib in SteamLibraries())
        {
            try
            {
                string apps = Path.Combine(lib, "steamapps");
                string manifest = Path.Combine(apps, "appmanifest_" + AppId + ".acf");
                if (File.Exists(manifest))
                {
                    var m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                    if (m.Success)
                    {
                        string dir = Path.Combine(apps, "common", m.Groups[1].Value);
                        if (IsGameFolder(dir)) return dir;
                    }
                }
                string plain = Path.Combine(apps, "common", "Megabonk");
                if (IsGameFolder(plain)) return plain;
            }
            catch { /* unreadable library: try the next one */ }
        }
        return null;
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        void Add(string p) { if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) roots.Add(Path.GetFullPath(p.Replace('/', '\\'))); }
        try { Add(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string); } catch { }
        try { Add(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string); } catch { }
        try { Add(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string); } catch { }
        Add(@"C:\Program Files (x86)\Steam");
        Add(@"C:\Program Files\Steam");

        var libs = new List<string>(roots);
        foreach (var root in roots)
        {
            try
            {
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    if (Directory.Exists(m.Groups[1].Value.Replace(@"\\", @"\"))) libs.Add(Path.GetFullPath(m.Groups[1].Value.Replace(@"\\", @"\")));
            }
            catch { }
        }
        return libs.Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
