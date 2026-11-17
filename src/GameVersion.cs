using System;
using System.IO;
using BepInEx;
using UnityEngine;

namespace ModHub;

/// <summary>
/// Notices when Megabonk itself was updated: mods made for the old version can stop working, so the Hub says so once
/// (until the player presses OK) and offers updates or vanilla mode. The fingerprint is the game's version plus
/// GameAssembly.dll's size, kept per game folder in BepInEx\config\ModHub.game.txt
/// (line 1: the version the player last confirmed, line 2: the version seen at the last start).
/// </summary>
internal static class GameVersion
{
    private static string FilePath => Path.Combine(Paths.ConfigPath, "ModHub.game.txt");

    /// <summary>"version | size | date" of the running game (null if it couldn't be read).</summary>
    public static string Current { get; private set; }

    /// <summary>The game's own version number (Application.version).</summary>
    public static string Number { get; private set; }

    /// <summary>The game changed since the player last pressed OK on the banner.</summary>
    public static bool Updated { get; private set; }

    /// <summary>This start is the first one on the new version (the Hub opens by itself only then).</summary>
    public static bool JustNoticed { get; private set; }

    public static void CheckAtStart()
    {
        try
        {
            try { Number = Application.version; } catch { Number = "?"; }
            // Version + size of the game's code. Not the file date: Steam's "verify files" or moving the game rewrites it.
            var game = new FileInfo(Path.Combine(Paths.GameRootPath, "GameAssembly.dll"));
            Current = $"{Number} | {(game.Exists ? game.Length : 0)}";
            var lines = File.Exists(FilePath) ? File.ReadAllLines(FilePath) : new string[0];
            string confirmed = lines.Length > 0 ? lines[0].Trim() : null, lastSeen = lines.Length > 1 ? lines[1].Trim() : null;
            if (confirmed == null) { Write(Current, Current); return; } // first start with this Hub: nothing to compare yet
            Updated = confirmed != Current;
            if (Updated && lastSeen != Current)
            {
                JustNoticed = true;
                // Once per game update, not once per start.
                History.Add(History.GameUpdated, "Megabonk", NumberOf(confirmed), Number);
                Plugin.Logger.LogInfo($"Mod Hub: Megabonk was updated ({confirmed} -> {Current}).");
            }
            Write(confirmed, Current);
        }
        catch (Exception e) { Plugin.Logger.LogDebug($"Game version: {e.Message}"); }
    }

    /// <summary>The banner's OK: this version is the normal one from now on.</summary>
    public static void Dismiss()
    {
        Updated = false;
        if (Current != null) Write(Current, Current);
    }

    private static string NumberOf(string fingerprint) => fingerprint.Split('|')[0].Trim();

    private static void Write(string confirmed, string seen)
    {
        try { File.WriteAllLines(FilePath, new[] { confirmed, seen }); } catch (Exception e) { Plugin.Logger.LogDebug($"Game version: {e.Message}"); }
    }
}
