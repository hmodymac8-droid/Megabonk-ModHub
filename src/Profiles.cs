using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ModHub;

/// <summary>
/// Mod lists you can share: "MODHUB-..." codes with the Thunderstore names of your mods.
/// A friend pastes the code into their Hub and gets the same mods in one click.
/// </summary>
internal static class ShareCode
{
    private const string Prefix = "MODHUB-";

    /// <summary>
    /// Thunderstore names of the mods that are on. Mods only on Nexus are listed by name ("~Name") so the friend knows
    /// to get them there; private mods that aren't published anywhere are left out entirely.
    /// </summary>
    public static string Make()
    {
        static string K(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var entries = Catalog.Mods.Where(m => m.On && !m.IsHub && !m.IsCharacter && !m.Duplicate)
            .Select(m => Updates.StorePackageFor(m)?.FullName
                         ?? (Online.Nexus?.Any(n => K(n.Name) == K(m.Name)) == true ? "~" + m.Name : null))
            .Where(e => e != null).Distinct().ToList();
        var raw = Encoding.UTF8.GetBytes(string.Join(";", entries));
        using var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal)) z.Write(raw, 0, raw.Length);
        return Prefix + Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>The mod entries in a code, or null if it isn't a Mod Hub code.</summary>
    public static List<string> Read(string code)
    {
        try
        {
            code = (code ?? "").Trim();
            int at = code.IndexOf(Prefix, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;
            string b64 = new string(code.Substring(at + Prefix.Length).TakeWhile(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_').ToArray())
                .Replace('-', '+').Replace('_', '/');
            b64 += new string('=', (4 - b64.Length % 4) % 4);
            using var z = new DeflateStream(new MemoryStream(Convert.FromBase64String(b64)), CompressionMode.Decompress);
            using var r = new StreamReader(z, Encoding.UTF8);
            return r.ReadToEnd().Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        catch { return null; }
    }
}

/// <summary>A one-line text box (the game's own text fields are stripped from this build).</summary>
internal sealed class TextBox
{
    public string Text = "";
    public bool Focused;
    public int Max = 30;
}

internal static partial class Panel
{
    private static readonly TextBox _profileName = new();
    private static string _confirmDelete;
    private static List<string> _friendCode;           // entries of the code the player pasted
    private static readonly Queue<StorePackage> _installQueue = new();
    private static int _installedFromCode;

    private static void DrawProfiles(Event e, Vector2 mouse, Rect body)
    {
        float x = body.x, y = body.y, w = body.width;

        // ---------------- profiles
        Label(new Rect(x, y, w, 24f), L.T("Profiles", "المجموعات"), _row, Gold);
        Label(new Rect(x, y + 3f, w, 20f), L.T("Save which mods are on, then switch sets in one click.", "احفظ المودات الشغالة، وبدّل بين المجموعات بضغطة."), _small, Grey, right: !L.Arabic, left: L.Arabic);
        y += 30f;
        DrawTextBox(_profileName, new Rect(x, y, w - 236f, 32f), e, mouse, L.T("Name, e.g. Co-op with friends", "اسم، مثلا: لعب مع الربع"));
        if (Btn(new Rect(x + w - 226f, y, 226f, 32f), L.T("Save current mods", "احفظ المودات الحالية"), _buttonOn))
        {
            string name = _profileName.Text.Trim();
            if (name.Length == 0) name = L.T($"Profile {Catalog.Profiles.Count + 1}", $"مجموعة {Catalog.Profiles.Count + 1}");
            int n = Catalog.SaveProfile(name);
            _profileName.Text = ""; _profileName.Focused = false;
            Toast(L.T($"Saved \"{name}\" ({n} mods on).", $"تم حفظ \"{name}\" ({n} مود شغال)."));
        }
        y += 40f;
        var names = Catalog.Profiles.Keys.ToList();
        if (names.Count == 0) { Label(new Rect(x, y, w, 22f), L.T("No profiles yet.", "ما فيه مجموعات للحين."), _small, Grey); y += 26f; }
        foreach (var name in names.Take(5))
        {
            var r = new Rect(x, y, w, 36f);
            Card(r, CardBg);
            int count = Catalog.Profiles[name].Count;
            Label(new Rect(x + 10f, y + 7f, w - 330f, 22f), $"{name}   ·   " + L.T($"{count} mods", $"{count} مود"), _row, Color.white, left: true);
            if (Btn(new Rect(r.xMax - 314f, y + 3f, 150f, 30f), L.T("Use this set", "استخدمها"), _buttonOn))
            {
                int changed = Catalog.ApplyProfile(name);
                Catalog.Scan(); Updates.Refresh();
                Toast(changed == 0 ? L.T($"\"{name}\" is already what you have.", $"\"{name}\" هي نفس الحالية.")
                                   : L.T($"\"{name}\": {changed} change(s) after restart.", $"\"{name}\": {changed} تغيير بعد إعادة التشغيل."));
            }
            bool sure = _confirmDelete == name && Time.unscaledTime < _confirmUntil && Time.unscaledTime > _confirmUntil - 5.5f;
            if (Btn(new Rect(r.xMax - 156f, y + 3f, 150f, 30f), sure ? L.T("Sure?", "متأكد؟") : L.T("Delete", "حذف"), _button))
            {
                if (sure) { Catalog.DeleteProfile(name); _confirmDelete = null; }
                else { _confirmDelete = name; _confirmUntil = Time.unscaledTime + 6f; }
            }
            y += 40f;
        }
        y += 8f;

        // ---------------- vanilla
        Fill(new Rect(x, y, w, 1f), new Color(1f, 1f, 1f, 0.12f));
        y += 10f;
        Label(new Rect(x, y, w - 300f, 24f), L.T("Vanilla", "اللعبة الأصلية"), _row, Gold);
        Label(new Rect(x, y + 26f, w - 300f, 22f), Catalog.Vanilla
            ? L.T("Vanilla mode is on. Bring back the mods you had.", "وضع اللعبة الأصلية شغال. رجّع موداتك.")
            : L.T("Turn every mod off (Mod Hub stays so you can come back).", "اطفي كل المودات (الـ Mod Hub يبقى عشان ترجعها)."), _small, Grey, left: true);
        if (!Catalog.Vanilla)
        {
            if (Btn(new Rect(x + w - 290f, y + 8f, 290f, 34f), L.T("Play vanilla (all mods off)", "العب الأصلية (اطفي الكل)"), _buttonOn))
            { Catalog.EnterVanilla(); Catalog.Scan(); Updates.Refresh(); Toast(L.T("All mods will be off after restart.", "كل المودات بتنطفي بعد إعادة التشغيل.")); }
        }
        else if (Btn(new Rect(x + w - 290f, y + 8f, 290f, 34f), L.T("Bring my mods back", "رجّع موداتي"), _buttonOn))
        { Catalog.LeaveVanilla(); Catalog.Scan(); Updates.Refresh(); Toast(L.T("Your mods will be back after restart.", "موداتك بترجع بعد إعادة التشغيل.")); }
        y += 58f;

        // ---------------- share
        Fill(new Rect(x, y, w, 1f), new Color(1f, 1f, 1f, 0.12f));
        y += 10f;
        Label(new Rect(x, y, w, 24f), L.T("Play with friends", "العب مع الربع"), _row, Gold);
        Label(new Rect(x, y + 26f, w, 22f), L.T("Send your code to a friend: they paste it here and get the same mods.", "أرسل الكود لربعك: يلصقونه هنا ويجيهم نفس المودات."), _small, Grey, left: true);
        y += 54f;
        float bw = (w - 8f) / 2f;
        if (Btn(new Rect(x, y, bw, 34f), L.T("Copy my mod code", "انسخ كود موداتي"), _buttonOn))
        {
            string code = ShareCode.Make();
            Toast(SetClipboard(code) ? L.T("Code copied. Paste it to your friend (Ctrl+V).", "تم نسخ الكود. الصقه لربعك (Ctrl+V).")
                                     : L.T("Could not reach the clipboard.", "تعذر الوصول للحافظة."));
        }
        if (Btn(new Rect(x + bw + 8f, y, bw, 34f), L.T("Paste a friend's code", "الصق كود صديق"), _button))
        {
            _friendCode = ShareCode.Read(GetClipboard());
            if (_friendCode == null) Toast(L.T("No Mod Hub code found. Copy your friend's code first.", "ما لقيت كود Mod Hub. انسخ كود صديقك أول."));
        }
        y += 42f;

        if (_friendCode != null) DrawFriendCode(new Rect(x, y, w, body.yMax - y));
    }

    private static void DrawFriendCode(Rect area)
    {
        float x = area.x, y = area.y, w = area.width;
        var store = Online.Store;
        var missing = new List<StorePackage>();
        var notOnStore = new List<string>();
        int have = 0;
        foreach (var entry in _friendCode)
        {
            if (entry.StartsWith("~")) { if (!Catalog.Mods.Any(m => Same(m.Name, entry.Substring(1)))) notOnStore.Add(entry.Substring(1)); else have++; continue; }
            var pkg = store?.FirstOrDefault(p => p.FullName.Equals(entry, StringComparison.OrdinalIgnoreCase));
            bool installed = Catalog.Mods.Any(m => m.Source == "Thunderstore:" + entry || (pkg != null && Same(m.Name, pkg.Name)));
            if (installed) have++;
            else if (pkg != null) missing.Add(pkg);
            else notOnStore.Add(entry.Contains('-') ? entry.Substring(entry.IndexOf('-') + 1) : entry);
        }

        Card(area, new Color(1f, 1f, 1f, 0.04f));
        Label(new Rect(x + 10f, y + 6f, w - 20f, 22f),
            L.T($"Friend's code: {_friendCode.Count} mods  ·  {have} you already have  ·  {missing.Count} to install",
                $"كود الصديق: {_friendCode.Count} مود  ·  {have} عندك  ·  {missing.Count} للتثبيت"), _small, Color.white, left: true);
        if (missing.Count > 0)
            Label(new Rect(x + 10f, y + 28f, w - 250f, 22f), string.Join(", ", missing.Select(p => p.DisplayName)), _small, Green, left: true);
        if (notOnStore.Count > 0)
            Label(new Rect(x + 10f, y + 50f, w - 250f, 22f), L.T("Get these yourself (not on Thunderstore): ", "حمّل هذي بنفسك (مو على Thunderstore): ") + string.Join(", ", notOnStore), _small, Gold, left: true);

        bool busy = _installQueue.Count > 0 || Installing.Count > 0;
        if (missing.Count > 0 && Btn(new Rect(x + w - 230f, y + 26f, 220f, 34f),
                busy ? L.T("Installing...", "جاري التثبيت...") : L.T($"Install {missing.Count} missing", $"ثبّت {missing.Count} ناقص"), _buttonOn) && !busy)
        {
            foreach (var p in missing) _installQueue.Enqueue(p);
            _installedFromCode = 0;
            InstallNextFromCode();
        }
        if (missing.Count == 0 && notOnStore.Count == 0)
            Label(new Rect(x + 10f, y + 28f, w - 20f, 22f), L.T("You have all of these. Restart if you just installed them.", "عندك كلها. أعد التشغيل لو توك ثبتها."), _small, Green, left: true);
    }

    private static void InstallNextFromCode()
    {
        if (_installQueue.Count == 0)
        {
            Toast(L.T($"Installed {_installedFromCode} mod(s) from your friend's code. Restart the game to play.", $"تم تثبيت {_installedFromCode} مود من كود صديقك. أعد تشغيل اللعبة."));
            return;
        }
        var p = _installQueue.Dequeue();
        Installing.Add(p.FullName);
        Online.InstallFromStore(p, msg => { Installing.Remove(p.FullName); if (Online.LastInstallOk) _installedFromCode++; InstallNextFromCode(); });
    }

    // ------------------------------------------------------------------ text box and clipboard

    private static TextBox _typing; // the text box that has the keyboard (search has its own)

    private static void DrawTextBox(TextBox tb, Rect box, Event e, Vector2 mouse, string placeholder)
    {
        if (NavItem(box)) { tb.Focused = true; _typing = tb; _searchFocused = false; }
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (box.Contains(mouse)) { tb.Focused = true; _typing = tb; _searchFocused = false; e.Use(); }
            else if (tb.Focused) { tb.Focused = false; if (_typing == tb) _typing = null; }
        }
        if (tb.Focused && e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Backspace) { if (tb.Text.Length > 0) tb.Text = tb.Text.Substring(0, tb.Text.Length - 1); e.Use(); }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape) { tb.Focused = false; _typing = null; e.Use(); }
            else if (e.character != '\0' && !char.IsControl(e.character)) { if (tb.Text.Length < tb.Max) tb.Text += e.character; e.Use(); }
        }
        BorderedCard(box, tb.Focused ? new Color(0.13f, 0.15f, 0.18f, 1f) : new Color(0.1f, 0.115f, 0.14f, 1f), tb.Focused ? Gold : new Color(1f, 1f, 1f, 0.16f), 8f);
        var text = new Rect(box.x + 10f, box.y + 6f, box.width - 20f, box.height - 8f);
        if (tb.Text.Length == 0 && !tb.Focused) { Label(text, placeholder, _small, Grey, left: true); return; }
        bool caret = tb.Focused && (int)(Time.unscaledTime * 2f) % 2 == 0;
        Label(text, tb.Text + (caret ? "|" : ""), _row, Color.white, left: true);
    }

    /// <summary>True while one of our text boxes has the keyboard (so F4/Esc don't close the Hub mid-typing).</summary>
    private static bool TypingInBox => _typing != null && _typing.Focused;

    private static bool SetClipboard(string text)
    {
        try { GUIUtility.systemCopyBuffer = text; return true; }
        catch (Exception e) { LogOnce("Clipboard", e); return false; }
    }

    private static string GetClipboard()
    {
        try { return GUIUtility.systemCopyBuffer ?? ""; }
        catch (Exception e) { LogOnce("Clipboard", e); return ""; }
    }
}
