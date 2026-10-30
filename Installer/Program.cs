using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ModHubInstaller;

/// <summary>English, or Arabic when Windows is in Arabic.</summary>
internal static class Text
{
    public static bool Arabic = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
    public static string T(string en, string ar) => Arabic ? ar : en;
}

internal static class Program
{
    /// <summary>
    /// Normal use: double-click, the window opens. For testing and the admin retry:
    ///   --target "game folder"   use this folder instead of searching Steam
    ///   --silent --log file      install without a window and write the result to a file (exit code 0 = ok)
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        // A path ending in a backslash before a quote reaches us with a stray quote: clean it either way.
        string target = Arg(args, "--target")?.Trim().Trim('"').TrimEnd('\\');
        if (Arg(args, "--lang") is string lang) Text.Arabic = lang == "ar"; // test: force a language
        if (args.Contains("--find")) { File.WriteAllText(Arg(args, "--log") ?? "find.txt", GameFinder.Find() ?? "NOT FOUND"); return 0; } // test: where is the game?
        if (args.Contains("--silent")) return Silent(target, Arg(args, "--log"));

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var form = new MainForm(target);
        if (Arg(args, "--shot") is string shot)
        {
            // Test: draw the window into a picture off-screen (nothing appears on the screen), then exit.
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-10000, -10000);
            form.ShowInTaskbar = false;
            form.Shown += (_, __) =>
            {
                using var bmp = new System.Drawing.Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(shot);
                form.Close();
            };
        }
        Application.Run(form);
        return 0;
    }

    private static int Silent(string target, string log)
    {
        void Log(string s) { if (log != null) File.AppendAllText(log, s + Environment.NewLine); }
        try
        {
            target ??= GameFinder.Find();
            if (!GameFinder.IsGameFolder(target)) { Log("FAIL no game folder: " + target); return 2; }
            var ready = Setup.Check(target);
            Log($"CHECK running={ready.GameRunning} canWrite={ready.CanWrite} otherMelonLoader={ready.OtherMelonLoader}");
            if (!ready.Ready) { Log("FAIL not ready"); return 3; }
            var r = Setup.Install(target, (_, __) => { });
            Log($"OK installed={r.Installed} unchanged={r.Unchanged} keptSettings={r.KeptSettings} backedUp={r.BackedUp} leftovers={string.Join(",", r.MovedLeftovers)}");
            return 0;
        }
        catch (Exception ex) { Log("FAIL " + ex.Message.Replace('\n', ' ')); return 1; }
    }

    private static string Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
