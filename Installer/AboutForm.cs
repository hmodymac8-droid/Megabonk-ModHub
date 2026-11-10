using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ModHubInstaller;

/// <summary>"About the developer": who makes Mod Hub, the credits, and links to its GitHub and Nexus pages.</summary>
internal static class AboutForm
{
    public const string GitHub = "https://github.com/hmodymac8-droid/Megabonk-ModHub";
    public const string Nexus = "https://www.nexusmods.com/megabonk/mods/194";

    private static string T(string en, string ar) => Text.T(en, ar);

    /// <param name="shot">Test: draw it into this picture off-screen instead of showing it.</param>
    public static void Show(IWin32Window owner, string shot = null)
    {
        var bg = Color.FromArgb(24, 24, 28);
        var gold = Color.FromArgb(255, 200, 40);
        var grey = Color.FromArgb(170, 170, 180);
        using var f = new Form
        {
            Text = T("About the developer", "عن المطوّر"),
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent, BackColor = bg, ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f), AutoScaleMode = AutoScaleMode.Dpi, ClientSize = new Size(500, 450),
            RightToLeft = Text.Arabic ? RightToLeft.Yes : RightToLeft.No, RightToLeftLayout = Text.Arabic,
        };
        try { f.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var title = new Label { Text = "AhmedHus21", Font = new Font("Segoe UI Semibold", 18f), ForeColor = gold, AutoSize = true, Location = new Point(22, 16) };
        var role = new Label
        {
            Text = T("Makes Mod Hub, Quest Tracker and the Arabic translation for Megabonk.", "صانع Mod Hub و Quest Tracker والترجمة العربية لـ Megabonk."),
            ForeColor = grey, AutoSize = false, Bounds = new Rectangle(24, 58, 452, 44),
        };
        var credits = new Label
        {
            Text = T("Special thanks to sensei, my friend and teacher.\nThanks to pinkpomelo, the BepInEx team, Il2CppInterop and Harmony by Andreas Pardeike. Melon Bridge mirrors the API of MelonLoader by LavaGang and includes Json.NET by James Newton-King (MIT).\nThe code was written with Claude (Anthropic's AI); every version is tested in the game.",
                     "شكر خاص لـ sensei، صديقي ومعلمي.\nوشكرا لـ pinkpomelo، وفريق BepInEx، و Il2CppInterop، و Harmony من Andreas Pardeike. و Melon Bridge يحاكي واجهة MelonLoader من LavaGang، وفيه Json.NET من James Newton-King (MIT).\nالكود مكتوب بمساعدة Claude (ذكاء اصطناعي من Anthropic)، وكل نسخة تنجرب في اللعبة."),
            AutoSize = false, Bounds = new Rectangle(24, 110, 452, 220),
        };
        var github = Link(T("Mod Hub on GitHub (source code and downloads)", "Mod Hub على GitHub (الكود والتحميلات)"), GitHub, new Point(24, 340));
        var nexus = Link(T("Mod Hub on Nexus Mods", "Mod Hub على Nexus Mods"), Nexus, new Point(24, 370));
        var ok = new Button
        {
            Text = T("Close", "سكّر"), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(36, 36, 42), ForeColor = Color.White,
            Bounds = new Rectangle(376, 402, 100, 34), DialogResult = DialogResult.OK,
        };
        ok.FlatAppearance.BorderSize = 0;
        f.AcceptButton = f.CancelButton = ok;
        f.Controls.AddRange(new Control[] { title, role, credits, github, nexus, ok });
        if (shot != null)
        {
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(-10000, -10000);
            f.Shown += (_, __) => { using var bmp = new Bitmap(f.Width, f.Height); f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height)); bmp.Save(shot); f.Close(); };
        }
        f.ShowDialog(owner);
    }

    private static LinkLabel Link(string text, string url, Point at)
    {
        var l = new LinkLabel { Text = text, AutoSize = true, Location = at, LinkColor = Color.FromArgb(255, 200, 40), ActiveLinkColor = Color.White, VisitedLinkColor = Color.FromArgb(255, 200, 40) };
        l.LinkClicked += (_, __) => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
        return l;
    }
}
