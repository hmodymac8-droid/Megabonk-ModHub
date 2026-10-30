using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace ModHub;

/// <summary>
/// Tells the player when a key they pick to open the Hub is already used: by the game's main controls,
/// by the game's console (F10), or by another mod (read from every mod's BepInEx config file).
/// </summary>
internal static class KeyConflicts
{
    // Megabonk's default keyboard controls (movement, jump, slide, interact, menus).
    private static readonly Dictionary<KeyCode, string> GameKeys = new()
    {
        [KeyCode.W] = "the game (move)", [KeyCode.A] = "the game (move)", [KeyCode.S] = "the game (move)", [KeyCode.D] = "the game (move)",
        [KeyCode.UpArrow] = "the game (move)", [KeyCode.DownArrow] = "the game (move)", [KeyCode.LeftArrow] = "the game (move)", [KeyCode.RightArrow] = "the game (move)",
        [KeyCode.Space] = "the game (jump)", [KeyCode.LeftShift] = "the game (slide)", [KeyCode.LeftControl] = "the game",
        [KeyCode.E] = "the game (interact)", [KeyCode.Tab] = "the game (stats)", [KeyCode.Return] = "the game (menus)",
        [KeyCode.Mouse0] = "the game (mouse)", [KeyCode.Mouse1] = "the game (mouse)", [KeyCode.Mouse2] = "the game (mouse)",
        [KeyCode.F10] = "the game's console",
    };

    /// <summary>Who else uses this key, or null if nobody we know of.</summary>
    public static string UsedBy(KeyCode key)
    {
        if (GameKeys.TryGetValue(key, out var game)) return game;
        try
        {
            foreach (var file in Directory.GetFiles(Paths.ConfigPath, "*.cfg"))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                if (id.Equals(Plugin.Guid, StringComparison.OrdinalIgnoreCase) || id.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)) continue;
                string type = null;
                foreach (var raw in File.ReadLines(file))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("# Setting type:")) { type = line.Substring(15).Trim(); continue; }
                    if (line.StartsWith("#") || line.StartsWith("[") || !line.Contains('=')) { if (line.Length == 0) type = null; continue; }
                    if (type != "KeyCode" && type != "KeyboardShortcut") continue;
                    // "OpenKey = F7" or, for shortcuts, "Toggle = LeftControl + F7": the last part is the main key.
                    string value = line.Substring(line.IndexOf('=') + 1).Trim();
                    string main = value.Split('+').Last().Trim();
                    if (Enum.TryParse<KeyCode>(main, true, out var k) && k == key)
                    {
                        string mod = Catalog.Mods.FirstOrDefault(m => string.Equals(m.Guid, id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;
                        return mod;
                    }
                    type = null;
                }
            }
        }
        catch { /* unreadable config: just don't warn */ }
        return null;
    }
}
