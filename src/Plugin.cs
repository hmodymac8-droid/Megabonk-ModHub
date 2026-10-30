using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace ModHub;

/// <summary>
/// Mod Hub: one place (F4) to see every installed mod, switch any of them on or off, play vanilla,
/// and install new mods from Thunderstore, your Downloads folder or the "drop mods here" folder.
/// It never patches the game: it only reads it and manages files in BepInEx\plugins.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "modhub.megabonk";
    public const string Name = "Mod Hub";
    public const string Version = "1.2.3";

    internal static ManualLogSource Logger;
    internal static ConfigEntry<KeyCode> OpenKey;
    internal static ConfigEntry<string> Language;

    public override void Load()
    {
        Logger = Log;
        OpenKey = Config.Bind("General", "OpenHubKey", KeyCode.F4, "Key that opens and closes Mod Hub (F10 is taken by the game's console).");
        Language = Config.Bind("General", "Language", "Auto",
            new ConfigDescription("Language of Mod Hub. Auto = Arabic when Megabonk Arabic has the game in Arabic.",
                new AcceptableValueList<string>("Auto", "English", "Arabic")));

        Catalog.Load();
        Safety.Begin();
        LocalFiles.EnsureDropDir();
        AddComponent<HubBehaviour>();
        Log.LogInfo($"{Name} {Version} loaded. Press {OpenKey.Value} to open it.");
    }

    /// <summary>
    /// Closes the game cleanly so changes apply on the next start. It deliberately doesn't relaunch itself: that needs a
    /// hidden helper program, which antivirus scanners flag, and starting through Steam keeps Steam in sync.
    /// </summary>
    internal static void CloseGame()
    {
        Logger.LogInfo("Closing the game so mod changes apply on the next start.");
        Application.Quit();
    }

    /// <summary>
    /// Starts a fresh copy of Megabonk, then closes this one. No helper programs or scripts: just the game's own exe
    /// (helpers like cmd or PowerShell get mods flagged by virus scanners).
    /// </summary>
    internal static void Restart()
    {
        // Started by Steam (it gives the game its app id): ask Steam to start it again. Steam keeps tracking the game's
        // processes, and a copy we start ourselves is one it never sees exit, so it stays on "Stopping" and Play says
        // "Game already running" until Steam restarts. Steam waits for this copy to close before starting the new one.
        string appId = Environment.GetEnvironmentVariable("SteamGameId") ?? Environment.GetEnvironmentVariable("SteamAppId");
        if (uint.TryParse(appId, out _))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("steam://rungameid/" + appId) { UseShellExecute = true });
                Logger.LogInfo("Restarting the game through Steam.");
                Application.Quit();
                return;
            }
            catch (Exception e) { Logger.LogWarning($"Steam restart failed, starting the game directly: {e.Message}"); }
        }
        try
        {
            string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            var psi = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Paths.GameRootPath };
            // The mod loader marks this process as already started in its environment; the new copy must not inherit
            // that, or it would skip loading mods.
            foreach (var key in psi.Environment.Keys.Where(k => k.StartsWith("DOORSTOP", StringComparison.OrdinalIgnoreCase)).ToList())
                psi.Environment.Remove(key);
            System.Diagnostics.Process.Start(psi);
            Logger.LogInfo("Restarting the game.");
        }
        catch (Exception e) { Logger.LogWarning($"Could not restart: {e.Message}"); }
        Application.Quit();
    }
}

public class HubBehaviour : MonoBehaviour
{
    public HubBehaviour(IntPtr ptr) : base(ptr) { }

    private bool _scanned;
    private bool _safetyShown;
    private bool _whatsNewShown;

    public void Update()
    {
        L.Refresh();
        if (!_scanned)
        {
            _scanned = true;
            try { Catalog.Scan(); } catch (Exception e) { Plugin.Logger.LogWarning($"Mod scan: {e.Message}"); }
            try { WhatsNew.CheckAtStart(); } catch (Exception e) { Plugin.Logger.LogWarning($"What's new: {e.Message}"); }
        }
        Online.RunPending();
        Online.Tick();
        Panel.Tick();
        Safety.Tick();
        if (!_safetyShown && _scanned && Safety.NeedsAttention && Time.realtimeSinceStartup > 20f) { _safetyShown = true; Panel.ShowSafety(); }
        // Mods updated since the last start: show their notes once, right when the game starts (after the crash-safety message, if any).
        if (!_whatsNewShown && _scanned && !Safety.NeedsAttention && WhatsNew.Unseen.Count > 0)
        { _whatsNewShown = true; Panel.ShowWhatsNew(); }
    }

    public void OnApplicationQuit() => Safety.MarkGood(); // closed normally: this start was fine

    public void OnGUI()
    {
        try { Panel.OnGUI(); }
        catch (Exception e) { Panel.LogOnce("Mod Hub panel", e); }
    }
}

/// <summary>Mod Hub's own labels in English or Arabic (safe to call from any thread).</summary>
internal static class L
{
    private static volatile bool _arabic;
    private static bool _looked;
    private static System.Reflection.PropertyInfo _arabicMode; // MegabonkArabic.Plugin.ArabicOn

    public static bool Arabic => _arabic;

    /// <summary>Called every frame on the game thread.</summary>
    public static void Refresh()
    {
        string mode = Plugin.Language.Value;
        if (mode == "Arabic") { _arabic = true; return; }
        if (mode == "English") { _arabic = false; return; }
        try
        {
            if (!_looked)
            {
                _looked = true;
                if (IL2CPPChainloader.Instance.Plugins.TryGetValue("arabic.megabonk", out var info))
                {
                    var type = info.Instance?.GetType().Assembly.GetType("MegabonkArabic.Plugin");
                    _arabicMode = type?.GetProperty("ArabicOn",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                }
            }
            _arabic = _arabicMode != null && (bool)_arabicMode.GetValue(null);
        }
        catch { _arabic = false; }
    }

    public static string T(string english, string arabic) => _arabic ? arabic : english;
}
