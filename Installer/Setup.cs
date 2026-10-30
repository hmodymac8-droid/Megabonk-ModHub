using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace ModHubInstaller;

/// <summary>What's in the way before installing, if anything.</summary>
internal sealed class Readiness
{
    public bool GameRunning, CanWrite, OtherMelonLoader;
    public bool HasBepInEx, HasModHub; // already modded: then the window talks about keeping mods, and "Update"
    public bool Ready => !GameRunning && CanWrite;
}

/// <summary>
/// Installs the All-in-One that is embedded in this exe into the game folder. It never deletes anything:
/// files it replaces are copied to BepInEx\_installer_backup\&lt;time&gt; first, the player's settings in
/// BepInEx\config are kept, and copies left over from a wrong earlier extraction are moved to the same backup.
/// Afterwards every installed file is compared to the original.
/// </summary>
internal static class Setup
{
    private const string ReadMe = "INSTALL - READ ME.txt";

    public static Readiness Check(string gameDir)
    {
        var r = new Readiness { GameRunning = Process.GetProcessesByName("Megabonk").Length > 0 };
        try
        {
            string probe = Path.Combine(gameDir, ".modhub-installer-write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            r.CanWrite = true;
        }
        catch { r.CanWrite = false; }
        r.HasBepInEx = Directory.Exists(Path.Combine(gameDir, "BepInEx", "plugins")) || File.Exists(Path.Combine(gameDir, "winhttp.dll"));
        r.HasModHub = File.Exists(Path.Combine(gameDir, "BepInEx", "plugins", "ModHub", "ModHub.dll"));
        r.OtherMelonLoader = Directory.Exists(Path.Combine(gameDir, "MelonLoader")) &&
                             (File.Exists(Path.Combine(gameDir, "version.dll")) || File.Exists(Path.Combine(gameDir, "winmm.dll")));
        return r;
    }

    public static Stream OpenPayload() =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip") ?? throw new InvalidOperationException("The installer is damaged (no files inside). Download it again.");

    public sealed class Result
    {
        public int Installed, Unchanged, KeptSettings, BackedUp;
        public List<string> MovedLeftovers = new();
        public string BackupDir;
    }

    /// <summary>Installs everything. progress(0..1, text). Throws with a readable message when something goes wrong.</summary>
    public static Result Install(string gameDir, Action<double, string> progress)
    {
        gameDir = Path.GetFullPath(gameDir).TrimEnd('\\');
        string root = gameDir + "\\";
        var result = new Result { BackupDir = Path.Combine(gameDir, "BepInEx", "_installer_backup", DateTime.Now.ToString("yyyyMMdd-HHmmss")) };

        using var payload = OpenPayload();
        using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
        var files = zip.Entries.Where(e => e.Name.Length > 0 && !e.FullName.Equals(ReadMe, StringComparison.OrdinalIgnoreCase)).ToList();

        // Before changing anything: every file that will be replaced must be free. Otherwise stop now, with nothing
        // changed, instead of leaving a half-finished install.
        progress(0, Text.T("Checking that no file is in use...", "أتأكد إن ما فيه ملف مستخدم..."));
        foreach (var e in files)
        {
            string dest = Path.Combine(gameDir, e.FullName.Replace('/', '\\'));
            if (!File.Exists(dest)) continue;
            try { using (new FileStream(dest, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new InvalidOperationException(Text.T(
                    $"{e.FullName} is being used by another program. Close Megabonk (and any mod manager), then try again. Nothing was changed.",
                    $"{e.FullName} مستخدم من برنامج ثاني. سكّر Megabonk (وأي برنامج مودات)، وجرّب مرة ثانية. ما تغيّر شيء."));
            }
        }

        progress(0, Text.T("Checking for files from an earlier install in the wrong place...", "أدور على ملفات من تثبيت سابق في مكان غلط..."));
        MoveLeftovers(gameDir, result);

        for (int i = 0; i < files.Count; i++)
        {
            var e = files[i];
            string rel = e.FullName.Replace('/', '\\');
            string dest = Path.GetFullPath(Path.Combine(gameDir, rel));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // never outside the game folder
            progress((double)i / files.Count, rel);

            if (File.Exists(dest))
            {
                // The player's own settings stay as they are.
                if (rel.StartsWith(@"BepInEx\config\", StringComparison.OrdinalIgnoreCase)) { result.KeptSettings++; continue; }
                if (SameAsEntry(dest, e)) { result.Unchanged++; continue; }
                string bak = Path.Combine(result.BackupDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(bak));
                File.Copy(dest, bak, true);
                result.BackedUp++;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            string tmp = dest + ".installing";
            try
            {
                e.ExtractToFile(tmp, true);
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(tmp, dest);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                try { File.Delete(tmp); } catch { }
                throw new InvalidOperationException(Text.T($"Couldn't write {rel}. Is Megabonk still open? Close it and try again.\n({ex.Message})",
                                                            $"ما قدرت أكتب {rel}. اللعبة مفتوحة؟ سكّرها وجرّب مرة ثانية.\n({ex.Message})"));
            }
            result.Installed++;
        }

        progress(1, Text.T("Checking the installed files...", "أتأكد من الملفات المثبتة..."));
        Verify(gameDir, files);
        return result;
    }

    /// <summary>Every installed file must match the original exactly (settings files the player already had are skipped).</summary>
    private static void Verify(string gameDir, List<ZipArchiveEntry> files)
    {
        foreach (var e in files)
        {
            string rel = e.FullName.Replace('/', '\\');
            string dest = Path.Combine(gameDir, rel);
            if (rel.StartsWith(@"BepInEx\config\", StringComparison.OrdinalIgnoreCase) && File.Exists(dest)) continue;
            if (!File.Exists(dest) || !SameAsEntry(dest, e))
                throw new InvalidOperationException(Text.T($"A file didn't install correctly: {rel}. Please try again (antivirus programs sometimes block files; check yours).",
                                                            $"ملف ما انثبت صح: {rel}. جرّب مرة ثانية (برامج الحماية أحيانا تمنع ملفات، شيّك على برنامجك)."));
        }
        // The two files the game needs to load mods at all.
        foreach (var must in new[] { "winhttp.dll", @"BepInEx\core\BepInEx.Unity.IL2CPP.dll", @"BepInEx\plugins\ModHub\ModHub.dll" })
            if (!File.Exists(Path.Combine(gameDir, must)))
                throw new InvalidOperationException(Text.T($"Missing after install: {must}. Please try again.", $"ناقص بعد التثبيت: {must}. جرّب مرة ثانية."));
    }

    /// <summary>
    /// A wrong "Extract All" leaves a subfolder (like "ModHub-1.1.0-AllInOne-with-BepInEx") holding its own winhttp.dll
    /// and BepInEx. It does nothing there, but it confuses people: it goes to the backup folder.
    /// </summary>
    private static void MoveLeftovers(string gameDir, Result result)
    {
        foreach (var dir in Directory.GetDirectories(gameDir))
        {
            string name = Path.GetFileName(dir);
            if (name.Equals("BepInEx", StringComparison.OrdinalIgnoreCase) || name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(Path.Combine(dir, "winhttp.dll")) || !Directory.Exists(Path.Combine(dir, "BepInEx"))) continue;
            string target = Path.Combine(result.BackupDir, "leftovers", name);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            Directory.Move(dir, target);
            result.MovedLeftovers.Add(name);
        }
    }

    private static bool SameAsEntry(string path, ZipArchiveEntry e)
    {
        var fi = new FileInfo(path);
        if (fi.Length != e.Length) return false;
        using var sha = SHA256.Create();
        byte[] a, b;
        using (var f = File.OpenRead(path)) a = sha.ComputeHash(f);
        using (var s = e.Open()) b = sha.ComputeHash(s);
        return a.SequenceEqual(b);
    }
}
