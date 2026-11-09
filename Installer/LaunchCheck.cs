using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace ModHubInstaller;

/// <summary>
/// The proof that an install worked: the game starts, BepInEx writes a fresh LogOutput.log, and in it Mod Hub
/// says "Mod Hub x.y.z loaded". Copying the right files (Setup.Verify) can't show that BepInEx really runs:
/// antivirus programs remove winhttp.dll, Proton needs a launch option, the first start needs internet.
/// </summary>
internal static class LaunchCheck
{
    public enum Outcome { Loaded, NoBepInEx, NoModHub, GameClosed }

    public sealed class Result
    {
        public Outcome Outcome;
        public string Version;   // Mod Hub's version, when it loaded
        public string Errors;    // the last error lines of the log, when Mod Hub didn't load
        public bool Ok => Outcome == Outcome.Loaded;
    }

    private static string LogPath(string gameDir) => Path.Combine(gameDir, "BepInEx", "LogOutput.log");

    /// <summary>
    /// Waits until Mod Hub has loaded, or until it's clear it won't. <paramref name="since"/>: when the game was started
    /// (an older log is from an earlier run). <paramref name="watchGame"/>: also stop when the game closes.
    /// </summary>
    public static Result Wait(string gameDir, DateTime since, TimeSpan timeout, bool watchGame, Action<string> progress = null, CancellationToken cancel = default)
    {
        var end = DateTime.Now + timeout;
        DateTime? gameSeen = null;
        while (DateTime.Now < end && !cancel.IsCancellationRequested)
        {
            string text = ReadFreshLog(gameDir, since);
            if (text != null)
            {
                var line = text.Split('\n').FirstOrDefault(l => l.Contains("Mod Hub") && l.Contains(" loaded"));
                if (line != null)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"Mod Hub (\S+) loaded");
                    return new Result { Outcome = Outcome.Loaded, Version = m.Success ? m.Groups[1].Value : null };
                }
                // BepInEx is up: after the chainloader has finished without Mod Hub, it isn't coming.
                if (text.Contains("Chainloader startup complete")) return new Result { Outcome = Outcome.NoModHub, Errors = LastErrors(text) };
                progress?.Invoke(Text.T("BepInEx is starting (the first start sets itself up, 1-3 minutes)...",
                                        "BepInEx يشتغل (أول تشغيل يجهّز نفسه، 1-3 دقايق)..."));
            }
            if (watchGame)
            {
                bool running = Process.GetProcessesByName("Megabonk").Length > 0;
                if (running) gameSeen ??= DateTime.Now;
                else if (gameSeen != null)
                {
                    // Closed before Mod Hub loaded: read the log one last time.
                    text = ReadFreshLog(gameDir, since);
                    if (text != null && text.Contains("Mod Hub") && text.Contains(" loaded")) continue;
                    return text == null ? new Result { Outcome = Outcome.NoBepInEx } : new Result { Outcome = Outcome.GameClosed, Errors = LastErrors(text) };
                }
                // The game runs, but after 2 minutes BepInEx still hasn't written anything: it isn't loading at all.
                if (running && text == null && DateTime.Now - gameSeen.Value > TimeSpan.FromMinutes(2)) return new Result { Outcome = Outcome.NoBepInEx };
            }
            Thread.Sleep(1000);
        }
        string last = ReadFreshLog(gameDir, since);
        return last == null ? new Result { Outcome = Outcome.NoBepInEx } : new Result { Outcome = Outcome.NoModHub, Errors = LastErrors(last) };
    }

    /// <summary>The log's text if it was written after <paramref name="since"/>, else null. The game keeps it open, so share it.</summary>
    private static string ReadFreshLog(string gameDir, DateTime since)
    {
        try
        {
            var file = new FileInfo(LogPath(gameDir));
            if (!file.Exists || file.LastWriteTime < since.AddSeconds(-2)) return null;
            using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs);
            return r.ReadToEnd();
        }
        catch { return null; }
    }

    private static string LastErrors(string log) =>
        string.Join("\n", log.Split('\n').Where(l => l.Contains("[Error") || l.Contains("[Fatal")).Select(l => l.Trim()).Reverse().Take(3).Reverse());

    /// <summary>One sentence for the player (and what to do).</summary>
    public static string Explain(Result r) => r.Outcome switch
    {
        Outcome.Loaded => Text.T($"It works: Mod Hub {r.Version} loaded. Press F4 in the game's main menu.",
                                 $"شغّال: Mod Hub {r.Version} اشتغل. اضغط F4 في القائمة الرئيسية للعبة."),
        Outcome.NoBepInEx => Text.T("The game started, but BepInEx (the mod loader) didn't. Usually an antivirus removed winhttp.dll: allow it and install again. On Steam Deck / Linux add this launch option: WINEDLLOVERRIDES=\"winhttp=n,b\" %command%",
                                    "اللعبة اشتغلت بس BepInEx (مشغّل المودات) ما اشتغل. غالبا برنامج الحماية شال winhttp.dll: اسمح له وثبّت مرة ثانية. على Steam Deck / لينكس أضف خيار التشغيل: WINEDLLOVERRIDES=\"winhttp=n,b\" %command%"),
        Outcome.NoModHub => Text.T("BepInEx runs, but Mod Hub didn't load." + Tail(r), "BepInEx شغّال، بس Mod Hub ما اشتغل." + Tail(r)),
        _ => Text.T("The game closed before Mod Hub loaded. Start it again and wait for the main menu (the first start takes 1-3 minutes and needs internet)." + Tail(r),
                    "اللعبة تسكّرت قبل ما يشتغل Mod Hub. شغّلها مرة ثانية وانتظر القائمة الرئيسية (أول تشغيل ياخذ 1-3 دقايق ويحتاج نت)." + Tail(r)),
    };

    private static string Tail(Result r) => string.IsNullOrEmpty(r.Errors) ? "" : "\n" + r.Errors;
}
