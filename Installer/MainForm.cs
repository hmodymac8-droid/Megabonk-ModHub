using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModHubInstaller;

/// <summary>One small window: where the game is, what's in the way, one Install button, then "Done".</summary>
internal sealed class MainForm : Form
{
    private static readonly Color Bg = Color.FromArgb(24, 24, 28), Card = Color.FromArgb(36, 36, 42), Gold = Color.FromArgb(255, 200, 40),
                                   Green = Color.FromArgb(90, 200, 120), Red = Color.FromArgb(240, 90, 90), Grey = Color.FromArgb(170, 170, 180);

    private readonly Label _folder = new(), _status = new(), _detail = new();
    private readonly Button _install = new(), _browse = new(), _admin = new(), _start = new(), _close = new();
    private readonly ProgressBar _bar = new();
    private string _gameDir;
    private bool _done;

    public MainForm(string target)
    {
        Text = "Mod Hub Installer";
        RightToLeft = ModHubInstaller.Text.Arabic ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = ModHubInstaller.Text.Arabic;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 380);

        var title = new Label { Text = "Mod Hub", Font = new Font("Segoe UI Semibold", 22f), ForeColor = Gold, AutoSize = true, Location = new Point(24, 16) };
        var sub = new Label
        {
            Text = T("Installs Mod Hub (with BepInEx) into Megabonk.", "يثبّت Mod Hub (مع BepInEx) في Megabonk."),
            ForeColor = Grey, AutoSize = false, Size = new Size(512, 52), Location = new Point(26, 62),
        };
        var folderTitle = new Label { Text = T("Game folder", "مجلد اللعبة"), ForeColor = Grey, AutoSize = true, Location = new Point(26, 112) };
        _folder.SetBounds(26, 136, 400, 28);
        _folder.RightToLeft = RightToLeft.No; // a path always reads left to right
        _folder.TextAlign = ModHubInstaller.Text.Arabic ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;
        _folder.AutoEllipsis = true;
        Style(_browse, T("Browse...", "اختر..."), false);
        _browse.SetBounds(436, 134, 100, 32);
        _browse.Click += (_, __) => Browse();

        _status.SetBounds(26, 186, 510, 26);
        _status.Font = new Font("Segoe UI Semibold", 11f);
        _detail.SetBounds(26, 214, 510, 64);
        _detail.ForeColor = Grey;
        _bar.SetBounds(26, 284, 510, 10);
        _bar.Visible = false;

        Style(_install, T("Install", "ثبّت"), true);
        _install.SetBounds(26, 312, 200, 44);
        _install.Click += async (_, __) => await DoInstall();
        Style(_admin, T("Install as administrator", "ثبّت كمسؤول"), false);
        _admin.SetBounds(236, 312, 220, 44);
        _admin.Visible = false;
        _admin.Click += (_, __) => RunAsAdmin();
        Style(_start, T("Start Megabonk", "شغّل Megabonk"), true);
        _start.SetBounds(26, 312, 200, 44);
        _start.Visible = false;
        _start.Click += (_, __) => { try { Process.Start(new ProcessStartInfo("steam://rungameid/" + GameFinder.AppId) { UseShellExecute = true }); } catch { } Close(); };
        Style(_close, T("Close", "سكّر"), false);
        _close.SetBounds(236, 312, 120, 44);
        _close.Visible = false;
        _close.Click += (_, __) => Close();

        Controls.AddRange(new Control[] { title, sub, folderTitle, _folder, _browse, _status, _detail, _bar, _install, _admin, _start, _close });

        _gameDir = GameFinder.IsGameFolder(target) ? target : GameFinder.Find();
        Activated += (_, __) => { if (!_done && _bar.Visible == false) Refresh2(); }; // re-check when the player comes back (e.g. after closing the game)
        Refresh2();
    }

    private static string T(string en, string ar) => ModHubInstaller.Text.T(en, ar);

    private static void Style(Button b, string text, bool primary)
    {
        b.Text = text;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = primary ? Gold : Card;
        b.ForeColor = primary ? Color.FromArgb(20, 20, 20) : Color.White;
        b.Font = new Font("Segoe UI Semibold", 10.5f);
        b.Cursor = Cursors.Hand;
    }

    /// <summary>Shows where the game is and whether anything is in the way; enables Install only when it can work.</summary>
    private void Refresh2()
    {
        _admin.Visible = false;
        if (!GameFinder.IsGameFolder(_gameDir))
        {
            _folder.Text = T("Megabonk wasn't found automatically.", "ما لقيت Megabonk تلقائيا.");
            Status(Red, T("Click Browse and choose the folder that has Megabonk.exe in it.", "اضغط اختر، وحدد المجلد اللي فيه Megabonk.exe."),
                T("In Steam: right-click Megabonk > Manage > Browse local files shows you that folder.", "في Steam: كليك يمين على Megabonk ثم Manage ثم Browse local files يوريك المجلد."));
            _install.Enabled = false;
            return;
        }
        _folder.Text = _gameDir;
        var r = Setup.Check(_gameDir);
        if (r.GameRunning)
        {
            Status(Red, T("Megabonk is open. Close it first (EXIT in the game's menu).", "Megabonk مفتوحة. سكّرها أول (EXIT من قائمة اللعبة)."),
                T("This window notices by itself when the game is closed.", "هالنافذة تنتبه لحالها لما تتسكّر اللعبة."));
            _install.Enabled = false;
            StartWatchingGame();
            return;
        }
        if (!r.CanWrite)
        {
            Status(Red, T("Windows doesn't allow changes in this folder.", "ويندوز ما يسمح بتعديل هالمجلد."),
                T("Click \"Install as administrator\" and choose Yes.", "اضغط \"ثبّت كمسؤول\" واختر Yes."));
            _install.Enabled = false;
            _admin.Visible = true;
            return;
        }
        if (r.OtherMelonLoader)
            Status(Gold, T("Ready, but MelonLoader is also installed here.", "جاهز، بس MelonLoader مثبت هنا بعد."),
                T("Mod Hub runs MelonLoader mods by itself. With the real MelonLoader also installed, both loaders start and mods can clash. You can still install.",
                  "Mod Hub يشغّل مودات MelonLoader بنفسه. ومع وجود MelonLoader الأصلي، الاثنين يشتغلون والمودات ممكن تتعارض. تقدر تثبّت عادي."));
        else if (r.HasModHub)
            Status(Green, T("Mod Hub is already installed: ready to update.", "Mod Hub مثبّت من قبل: جاهز للتحديث."),
                T("Your mods and settings stay as they are. Files that get replaced are backed up in BepInEx\\_installer_backup.",
                  "موداتك وإعداداتك تبقى زي ما هي. والملفات اللي تتبدّل تنحفظ نسخة منها في BepInEx\\_installer_backup."));
        else if (r.HasBepInEx)
            Status(Green, T("Ready to install. You already have mods: they stay.", "جاهز للتثبيت. عندك مودات من قبل: تبقى زي ما هي."),
                T("Mod Hub is added next to your mods, and your settings are kept. Files that get replaced are backed up in BepInEx\\_installer_backup.",
                  "Mod Hub ينضاف جنب موداتك، وإعداداتك تبقى. والملفات اللي تتبدّل تنحفظ نسخة منها في BepInEx\\_installer_backup."));
        else
            Status(Green, T("Ready to install.", "جاهز للتثبيت."),
                T("This adds BepInEx (the mod loader) and Mod Hub to Megabonk. Your game files and saves aren't changed.",
                  "هذا يضيف BepInEx (مشغّل المودات) و Mod Hub لـ Megabonk. ملفات اللعبة وحفظك ما يتغيّرون."));
        _install.Text = r.HasModHub ? T("Update", "حدّث") : T("Install", "ثبّت");
        _install.Enabled = true;
    }

    private Timer _watch;
    private void StartWatchingGame()
    {
        if (_watch != null) return;
        _watch = new Timer { Interval = 1500 };
        _watch.Tick += (_, __) => { if (Process.GetProcessesByName("Megabonk").Length == 0) { _watch.Stop(); _watch = null; Refresh2(); } };
        _watch.Start();
    }

    private void Status(Color c, string main, string detail)
    {
        _status.ForeColor = c;
        _status.Text = main;
        _detail.Text = detail;
    }

    private void Browse()
    {
        using var dlg = new OpenFileDialog { Title = T("Choose Megabonk.exe", "اختر Megabonk.exe"), Filter = "Megabonk.exe|Megabonk.exe", CheckFileExists = true };
        if (dlg.ShowDialog(this) == DialogResult.OK) { _gameDir = System.IO.Path.GetDirectoryName(dlg.FileName); Refresh2(); }
    }

    private void RunAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--target \"" + _gameDir.TrimEnd('\\') + "\"") { UseShellExecute = true, Verb = "runas" });
            Close();
        }
        catch { /* the player said No to Windows' question: stay here */ }
    }

    private async Task DoInstall()
    {
        var ready = Setup.Check(_gameDir);
        if (!ready.Ready) { Refresh2(); return; }
        _install.Enabled = _browse.Enabled = false;
        _bar.Visible = true;
        Status(Gold, T("Installing...", "جاري التثبيت..."), "");
        try
        {
            string dir = _gameDir;
            var result = await Task.Run(() => Setup.Install(dir, (p, f) => BeginInvoke((Action)(() =>
            {
                _bar.Value = Math.Max(0, Math.Min(100, (int)(p * 100)));
                _detail.Text = f;
            }))));
            _done = true;
            _bar.Visible = false;
            _install.Visible = false;
            _start.Visible = _close.Visible = true;
            string extra = result.MovedLeftovers.Count > 0
                ? T($"\nAlso moved an earlier install from the wrong place to the backup: {string.Join(", ", result.MovedLeftovers)}.",
                    $"\nونقلت تثبيت سابق كان في مكان غلط للنسخة الاحتياطية: {string.Join("، ", result.MovedLeftovers)}.")
                : "";
            Status(Green, T("Done! Mod Hub is installed.", "تم! Mod Hub مثبّت."),
                T("Start Megabonk from Steam. The first start takes 1-3 minutes (a black window shows text; don't close it). Then press F4 in the main menu.",
                  "شغّل Megabonk من Steam. أول تشغيل ياخذ 1-3 دقايق (تطلع نافذة سوداء فيها كلام، لا تسكّرها). بعدها اضغط F4 في القائمة الرئيسية.") + extra);
        }
        catch (Exception ex)
        {
            _bar.Visible = false;
            _install.Enabled = _browse.Enabled = true;
            Status(Red, T("The install didn't finish.", "التثبيت ما كمل."), ex.Message);
        }
    }
}
