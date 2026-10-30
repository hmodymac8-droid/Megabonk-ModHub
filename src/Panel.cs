using System;
using System.Collections.Generic;
using System.Linq;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ModHub;

/// <summary>
/// The Mod Hub window (F10). Drawn with Unity's immediate GUI using only calls that exist in Megabonk's build
/// (no text fields, scroll views or toggles: those are stripped), so the search box and scrolling are hand-made.
/// </summary>
internal static partial class Panel
{
    public static bool Open { get; private set; }

    private const float W = 860f, H = 680f, RowH = 62f, ScrollbarW = 10f;
    private static int _tab;                       // 0 installed, 1 get mods, 2 vanilla
    private static int _scroll;
    private static bool _dragBar;
    private static string _search = "";
    private static bool _searchFocused;
    private static string _toast;
    private static float _toastUntil;
    private static string _confirmRemove;          // mod id waiting for a second click
    private static float _confirmUntil;
    private static readonly HashSet<string> Installing = new();
    private static float _nextDropCheck;
    private static readonly HashSet<string> Logged = new();

    private static Rect _panel;
    private static bool _stylesReady;
    private static Texture2D _white;
    private static GUIStyle _title, _sub, _row, _small, _button, _buttonOn, _badge;

    private static readonly Color Bg = new(0.07f, 0.08f, 0.1f, 0.96f);
    private static readonly Color Gold = new(1f, 0.82f, 0.25f);
    private static readonly Color Grey = new(0.68f, 0.7f, 0.75f);
    private static readonly Color Green = new(0.45f, 0.9f, 0.5f);
    private static readonly Color Red = new(1f, 0.45f, 0.4f);
    private static readonly Color Blue = new(0.45f, 0.75f, 1f);

    public static void LogOnce(string what, Exception e)
    {
        if (Logged.Add(what + e.Message)) Plugin.Logger.LogWarning($"{what}: {e.Message}");
    }

    private static void Toast(string text) { _toast = text; _toastUntil = Time.unscaledTime + 12f; }

    // ------------------------------------------------------------------ open / close

    private static void Toggle()
    {
        Open = !Open;
        _pickingKey = false;
        _details = null;
        _whatsNew = null;
        _history = false;
        _searchFocused = false;
        _dragBar = false;
        if (Open)
        {
            CursorGuard.Hold();
            Catalog.Scan();
            Online.LoadStore();
            Safety.CountErrors();
            Catalog.MeasureRemoved();
            Online.LoadNexus();
            Online.LoadHubRelease();
            LocalFiles.ScanDownloads();
            Updates.Refresh();
            _nextDropCheck = 0f;
            // Mods updated since the last start: opening the Hub shows their notes first (if it didn't open by itself yet).
            if (WhatsNew.Unseen.Count > 0 && !Safety.NeedsAttention)
            {
                var updated = WhatsNew.Unseen.ToList();
                WhatsNew.MarkSeen();
                _tab = 0;
                OpenWhatsNew(updated, false);
            }
        }
        else
        {
            CursorGuard.Release();
            // Closed with the controller: the game gets its controls back once the button is let go, so the B that
            // closed the Hub doesn't also make the player slide (or back out of a game menu).
            if (!Pad.AnyHeld) InputGuard.Update(false, false);
            else _guardUntilPadUp = true;
        }
        _openedAt = Time.unscaledTime;
    }

    /// <summary>Opens the Hub on the Installed tab to show the crash-safety message.</summary>
    public static void ShowSafety() { if (!Open) Toggle(); _tab = 0; CloseDetails(); }

    private static float _watchDownloadsUntil, _nextDownloadsScan;
    private static bool _pickingKey;
    private static KeyCode _warnedKey = KeyCode.None;

    public static void Tick()
    {
        try { PadTick(); } catch (Exception e) { LogOnce("Controller", e); }
        if (Open && Catalog.Cleaned.Count > 0)
        {
            string names = string.Join(", ", Catalog.Cleaned);
            Catalog.Cleaned.Clear();
            Toast(L.T($@"Removed an old duplicate copy: {names} (moved to BepInEx\_hub_removed).", $@"تم حذف نسخة قديمة مكررة: {names} (نُقلت إلى BepInEx\_hub_removed)."));
        }
        // After "Update Mod Hub" sent the player to Nexus, keep looking in Downloads for the new zip for a while.
        if (Time.unscaledTime < _watchDownloadsUntil && Time.unscaledTime > _nextDownloadsScan)
        {
            _nextDownloadsScan = Time.unscaledTime + 5f;
            LocalFiles.ScanDownloads();
        }
        if (!Open || Time.unscaledTime < _nextDropCheck) return;
        _nextDropCheck = Time.unscaledTime + 2f;
        try
        {
            var messages = LocalFiles.ProcessDropFolder();
            if (messages.Count > 0) { Catalog.Scan(); Updates.Refresh(); Toast(string.Join("   ", messages)); }
        }
        catch (Exception e) { LogOnce("Drop folder", e); }
    }

    // ------------------------------------------------------------------ drawing

    public static void OnGUI()
    {
        var e = Event.current;
        // Waiting for the new open-key: the next key pressed becomes it (Esc cancels).
        if (_pickingKey && e != null && e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
        {
            if (e.keyCode != KeyCode.Escape)
            {
                // Already used by the game or another mod? Warn, and only take it if the same key is pressed again.
                string user = e.keyCode == Plugin.OpenKey.Value ? null : KeyConflicts.UsedBy(e.keyCode);
                if (user != null && _warnedKey != e.keyCode)
                {
                    _warnedKey = e.keyCode;
                    Toast(L.T($"{e.keyCode} is used by {user}. Press {e.keyCode} again to use it anyway, or press another key.",
                              $"{e.keyCode} مستخدم في {user}. اضغطه مرة ثانية لو تبيه، أو اضغط زر ثاني."));
                    e.Use();
                    return; // still waiting for a key
                }
                Plugin.OpenKey.Value = e.keyCode; // saved to modhub.megabonk.cfg by BepInEx
                Toast(L.T($"Mod Hub now opens with {e.keyCode}.", $"الـ Mod Hub صار يفتح بزر {e.keyCode}."));
            }
            _pickingKey = false;
            _warnedKey = KeyCode.None;
            e.Use();
            return;
        }
        if (e != null && e.type == EventType.KeyDown && !_searchFocused && !TypingInBox)
        {
            if (e.keyCode == Plugin.OpenKey.Value) { Toggle(); e.Use(); return; }
            if (Open && e.keyCode == KeyCode.Escape) { if (!CloseDetails()) Toggle(); e.Use(); return; }
        }
        if (!Open) return;

        EnsureStyles();
        float scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 2.5f);
        var old = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
        try { Draw(e, Screen.width / scale, Screen.height / scale); NavEndFrame(e); }
        finally { GUI.matrix = old; }

        InputGuard.Update(true, true);
        CursorGuard.Hold();
    }

    private static void Draw(Event e, float sw, float sh)
    {
        _panel = new Rect((sw - W) / 2f, Mathf.Max(10f, (sh - H) / 2f), W, H);
        var mouse = e.mousePosition;
        Card(new Rect(_panel.x - 1.5f, _panel.y - 1.5f, _panel.width + 3f, _panel.height + 3f), new Color(1f, 0.82f, 0.25f, 0.55f), 14f);
        Card(_panel, PanelBg, 13f);
        float x = _panel.x + 22f, y = _panel.y + 14f, w = W - 44f;

        Label(new Rect(x, y, w, 30f), L.T("Mod Hub", "مركز المودات"), _title, Gold);
        // Restart button, always there (asks "Sure?" first so a stray click never ends a run).
        var restartRect = new Rect(L.Arabic ? x : x + w - 160f, y, 160f, 28f);
        bool sureRestart = _confirmRemove == "<restart>" && Time.unscaledTime < _confirmUntil && Time.unscaledTime > _confirmUntil - 5.5f;
        if (Btn(restartRect, sureRestart ? L.T("Sure? Restart", "متأكد؟ أعد التشغيل") : L.T("Restart game", "أعد تشغيل اللعبة"), _button))
        {
            if (sureRestart) Plugin.Restart();
            else { _confirmRemove = "<restart>"; _confirmUntil = Time.unscaledTime + 6f; }
        }
        // Change the key that opens the Hub: click, then press any key.
        var keyRect = new Rect(L.Arabic ? x + 168f : x + w - 160f - 8f - 150f, y, 150f, 28f);
        if (Btn(keyRect, _pickingKey ? L.T("Press a key...", "اضغط أي زر...") : L.T($"Open key: {Plugin.OpenKey.Value}", $"زر الفتح: {Plugin.OpenKey.Value}"), _pickingKey ? _buttonOn : _button))
            { _pickingKey = !_pickingKey; _warnedKey = KeyCode.None; }
        var hintRect = L.Arabic ? new Rect(x + 330f, y + 4f, w - 330f, 24f) : new Rect(x, y + 4f, w - 330f, 24f);
        Label(hintRect, L.T($"[{Plugin.OpenKey.Value}] or [Esc] to close", $"[{Plugin.OpenKey.Value}] أو [Esc] للإغلاق"), _small, Grey, right: !L.Arabic, left: L.Arabic);
        y += 38f;

        // Tabs
        string[] tabs = { L.T("Installed mods", "المودات المثبتة"), L.T("Get mods", "تحميل مودات"), L.T("Profiles & friends", "المجموعات والربع") };
        float tw = (w - 16f) / 3f;
        for (int i = 0; i < 3; i++)
        {
            _tabRects[i] = new Rect(x + i * (tw + 8f), y, tw, 32f);
            if (Btn(_tabRects[i], tabs[i], _tab == i ? _segment : _button)) SwitchTab(i);
        }
        y += 42f;

        // Restart banner
        // Crash safety message
        if (Safety.NeedsAttention)
        {
            bool auto = Safety.AutoDisabled.Count > 0 || Safety.AutoReverted.Count > 0;
            bool goBack = Safety.Suspects.Any(Safety.CanGoBack);
            string names = auto
                ? string.Join(", ", Safety.AutoReverted.Select(n => L.T($"{n} (went back)", $"{n} (رجع للنسخة السابقة)")).Concat(Safety.AutoDisabled.Select(n => L.T($"{n} (off)", $"{n} (مطفي)"))))
                : string.Join(", ", Safety.Suspects.Select(m => Safety.CanGoBack(m) ? L.T($"{m.Name} {m.Version} (just updated)", $"{m.Name} {m.Version} (تحدّث للتو)") : m.Name));
            var banner = new Rect(x, y, w, 64f);
            Card(banner, new Color(1f, 0.45f, 0.4f, 0.16f));
            Label(new Rect(x + 12f, y + 6f, w - 24f, 24f), auto
                ? L.T("Megabonk didn't start properly twice, so Mod Hub fixed these for the next start:", "اللعبة ما اشتغلت صح مرتين، فالـ Mod Hub صلّح هذي للتشغيل الجاي:")
                : L.T("Megabonk didn't start properly last time. New since the last good start:", "اللعبة ما اشتغلت صح آخر مرة. الجديد من آخر تشغيل سليم:"), _row, Red);
            Label(new Rect(x + 12f, y + 34f, w - 380f, 22f), Fit(names, _small, w - 380f), _small, Color.white);
            if (auto)
            {
                if (Btn(new Rect(banner.xMax - 160f, y + 30f, 150f, 30f), L.T("OK", "تمام"), _button)) Safety.Dismiss();
            }
            else
            {
                if (Btn(new Rect(banner.xMax - 360f, y + 30f, 200f, 30f),
                        goBack ? L.T("Go back & restart", "ارجع للسابقة وأعد التشغيل") : L.T("Turn them off & restart", "اطفيها وأعد التشغيل"), _buttonOn))
                { Safety.FixSuspects(); Plugin.Restart(); }
                if (Btn(new Rect(banner.xMax - 152f, y + 30f, 142f, 30f), L.T("Keep them", "خلها"), _button)) Safety.Dismiss();
            }
            y += 72f;
        }

        // Mods switched on/off, plus running mods that were updated this session. Characters are rows too now, so
        // switching one on/off (or installing one) counts here like any other mod.
        int changes = Catalog.Mods.Count(m => m.WillChange || (m.LoadedNow && Catalog.WasInstalledThisSession(m.Id)));
        // Mod Hub's own update. Its home is Nexus (no direct downloads there): "Update" opens the files page, and once
        // the new zip lands in Downloads the banner turns into a one-click "Install".
        var hub = Catalog.Mods.FirstOrDefault(m => m.IsHub);
        bool hubJustInstalled = hub != null && Catalog.WasInstalledThisSession(hub.Id);
        var hubZip = hubJustInstalled ? null : LocalFiles.Downloads
            .Where(d => d.Check.Ok && d.Check.Guid == Plugin.Guid && Updates.Newer(d.Check.Version, Plugin.Version))
            .OrderByDescending(d => d.When).FirstOrDefault();
        if (changes == 0 && (hubZip != null || (hub?.Latest != null && !hubJustInstalled)))
        {
            var banner = new Rect(x, y, w, 38f);
            Card(banner, new Color(0.45f, 0.75f, 1f, 0.15f));
            var btn = new Rect(banner.xMax - 196f, y + 4f, 190f, 30f);
            if (hubZip != null)
            {
                Label(new Rect(x + 12f, y + 9f, w - 220f, 22f),
                    L.T($"Mod Hub {hubZip.Check.Version} is downloaded and ready.", $"Mod Hub {hubZip.Check.Version} محمّل وجاهز."), _row, Blue);
                if (Btn(btn, L.T($"Install Mod Hub {hubZip.Check.Version}", $"ثبّت Mod Hub {hubZip.Check.Version}"), _buttonOn))
                {
                    // The running copy is moved to BepInEx\_hub_removed (Windows allows that) and the new one takes its place.
                    bool ok = Installer.Install(hubZip.Path, hubZip.Check, "Nexus:" + Updates.HubNexusId) != null;
                    Catalog.Scan(); Updates.Refresh();
                    Toast(ok ? L.T("Mod Hub updated. Restart the game to use the new version.", "تم تحديث Mod Hub. أعد تشغيل اللعبة لاستخدام النسخة الجديدة.")
                             : L.T("Could not update Mod Hub, see the BepInEx log.", "تعذر تحديث Mod Hub، شوف سجل BepInEx."));
                }
            }
            else
            {
                Label(new Rect(x + 12f, y + 9f, w - 220f, 22f),
                    L.T($"Mod Hub {hub.Latest} is available.", $"Mod Hub {hub.Latest} متوفر."), _row, Blue);
                bool busy = Installing.Contains(hub.Id);
                if (Btn(btn, busy ? L.T("Updating...", "جاري التحديث...") : L.T("Update Mod Hub", "حدّث Mod Hub"), _buttonOn) && !busy)
                {
                    if (hub.CanUpdateInHub) UpdateMod(hub);
                    else
                    {
                        Application.OpenURL($"https://www.nexusmods.com/megabonk/mods/{Updates.HubNexusId}?tab=files");
                        _watchDownloadsUntil = Time.unscaledTime + 300f;
                        Toast(L.T("Click \"Manual download\" on the Main file. Mod Hub will offer to install it here.",
                                  "اضغط \"Manual download\" على الملف الرئيسي، والـ Mod Hub بيعرض عليك تثبيته هنا."));
                    }
                }
            }
            y += 46f;
        }
        if (changes > 0)
        {
            var banner = new Rect(x, y, w, 38f);
            Card(banner, new Color(1f, 0.82f, 0.25f, 0.15f));
            Label(new Rect(x + 12f, y + 9f, w - 200f, 22f),
                L.T($"{changes} change(s) apply when the game restarts.", $"{changes} تغيير/تغييرات تنطبق بعد إعادة تشغيل اللعبة."), _row, Gold);
            if (Btn(new Rect(banner.xMax - 176f, y + 4f, 170f, 30f), L.T("Restart game now", "أعد تشغيل اللعبة"), _buttonOn)) Plugin.Restart();
            y += 46f;
        }
        else if (!(changes == 0 && (hubZip != null || (hub?.Latest != null && !hubJustInstalled))))
        {
            // Keep this space even when there's nothing to say, so rows never jump down under the mouse
            // the moment a bar appears (a quick second click would hit the wrong mod).
            var slot = new Rect(x, y, w, 38f);
            Card(slot, new Color(1f, 1f, 1f, 0.03f));
            Label(new Rect(x + 12f, y + 9f, w - 24f, 22f), L.T("Everything is applied. Switch mods on or off below.", "كل شيء مطبّق. شغّل أو طفّي المودات تحت."), _small, Grey, left: true);
            y += 46f;
        }

        var body = new Rect(x, y, w, _panel.yMax - y - 44f);
        switch (_tab)
        {
            case 0: DrawInstalled(e, mouse, body); break;
            case 1: DrawGetMods(e, mouse, body); break;
            default: DrawProfiles(e, mouse, body); break;
        }

        // Footer / toast
        var foot = new Rect(x, _panel.yMax - 34f, w, 24f);
        if (_toast != null && Time.unscaledTime < _toastUntil) Label(foot, _toast, _row, Green);
        else if (_padMode) Label(foot, PadHint(), _small, Gold);
        else Label(foot, L.T("Changes take effect when the game restarts. Nothing is ever deleted: removed mods go to BepInEx\\_hub_removed.",
            "التغييرات تنطبق بعد إعادة تشغيل اللعبة. ما ينحذف شيء: المودات المحذوفة تروح لمجلد BepInEx\\_hub_removed."), _small, Grey);

        // Clicks on the window stay on the window.
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ScrollWheel) && _panel.Contains(mouse)) e.Use();
    }

    // ------------------------------------------------------------------ installed

    private static void DrawInstalled(Event e, Vector2 mouse, Rect body)
    {
        if (_history) { DrawHistory(e, mouse, body); return; }
        if (_whatsNew != null) { DrawWhatsNew(e, mouse, body); return; }
        if (_details != null) { DrawDetails(e, mouse, body); return; }
        var mods = Catalog.Mods;
        Label(new Rect(body.x, body.y + 6f, body.width, 22f),
            L.T($"{mods.Count} mods found", $"تم العثور على {mods.Count} مود"), _small, Grey, left: true);

        // What the Hub did (installs, updates, removals, switches): players paste it when asking for help.
        if (Btn(new Rect(body.xMax - 538f, body.y, 130f, 30f), L.T("History", "السجل"), _button)) { OpenHistory(); return; }

        // Old copies and removed mods pile up in BepInEx\_hub_removed: let the player clear them (after a second click).
        if (Catalog.RemovedCount > 0)
        {
            bool sure = _confirmRemove == "<removed>" && Time.unscaledTime < _confirmUntil && Time.unscaledTime > _confirmUntil - 5.5f;
            string size = Catalog.RemovedBytes >= 1024 * 1024 ? $"{Catalog.RemovedBytes / (1024f * 1024f):0.0} MB" : $"{Catalog.RemovedBytes / 1024f:0} KB";
            if (Btn(new Rect(body.xMax - 400f, body.y, 202f, 30f), sure
                    ? L.T("Delete for good? (no going back)", "حذف نهائي؟ (بدون رجوع)")
                    : L.T($"Empty removed ({size})", $"فرّغ المحذوفات ({size})"), _button))
            {
                if (sure)
                {
                    _confirmRemove = null;
                    bool emptied = Catalog.EmptyRemoved();
                    if (emptied || Catalog.LockedInRemoved > 0) History.Add(History.Emptied, null, detail: size);
                    Toast(emptied ? L.T($"Freed {size}: removed mods and old versions are gone (\"Back to\" buttons too).", $"تم تفريغ {size}: انحذفت المودات والنسخ القديمة (وأزرار \"ارجع لـ\" معها).")
                                                 : Catalog.LockedInRemoved > 0
                                                     ? L.T($"Freed the rest, but {Catalog.LockedInRemoved} file(s) are open in the running game. Close the game and empty it again.", $"تم تفريغ الباقي، لكن {Catalog.LockedInRemoved} ملف مفتوح في اللعبة. سكّر اللعبة وفرّغ مرة ثانية.")
                                                     : L.T("Could not empty it (a file is in use?).", "تعذر التفريغ (ملف مستخدم؟)."));
                }
                else { _confirmRemove = "<removed>"; _confirmUntil = Time.unscaledTime + 6f; }
            }
        }

        // Update all: every mod that can be updated right here (Nexus-only mods still need their page).
        var updatable = mods.Where(m => m.Latest != null && m.CanUpdateInHub && !m.IsHub).ToList();
        if (_updateQueue.Count > 0 || _updatingAll)
            Label(new Rect(body.x, body.y + 6f, body.width, 22f), L.T($"Updating... {_updateQueue.Count} left", $"جاري التحديث... باقي {_updateQueue.Count}"), _small, Gold, right: true);
        else if (updatable.Count > 0 && Btn(new Rect(body.xMax - 190f, body.y, 190f, 30f), L.T($"Update all ({updatable.Count})", $"حدّث الكل ({updatable.Count})"), _buttonOn))
            UpdateAll(updatable);

        var view = new Rect(body.x, body.y + 36f, body.width, body.height - 36f);
        List(e, mouse, view, mods.Count, (i, r) => DrawModRow(mods[i], r));
    }

    // ------------------------------------------------------------------ updating

    private static readonly Queue<Mod> _updateQueue = new();
    private static bool _updatingAll;
    private static int _updatedCount;

    /// <summary>Updates one mod: inside the Hub when it's on Thunderstore, otherwise opens its page.</summary>
    private static void UpdateMod(Mod m, Action<string> after = null)
    {
        if (m.IsHub && !Edition.SelfUpdate)
        {
            // Thunderstore build: r2modman / Gale update the Hub (it doesn't replace itself).
            Application.OpenURL(m.LatestUrl ?? "https://thunderstore.io/c/megabonk/");
            string msg = L.T("Update Mod Hub with your mod manager (r2modman or Gale), then restart the game.", "حدّث Mod Hub من مدير المودات (r2modman أو Gale) وبعدين أعد تشغيل اللعبة.");
            Toast(msg);
            after?.Invoke(msg);
            return;
        }
        if (m.IsHub && m.CanUpdateInHub && Online.Release != null)
        {
            // Downloads and installs itself. If that fails, the Nexus page is the way (as before).
            Installing.Add(m.Id);
            Online.UpdateHub((ok, msg) =>
            {
                Installing.Remove(m.Id);
                Toast(msg);
                if (!ok)
                {
                    Application.OpenURL($"https://www.nexusmods.com/megabonk/mods/{Updates.HubNexusId}?tab=files");
                    _watchDownloadsUntil = Time.unscaledTime + 300f;
                }
                after?.Invoke(msg);
            });
            return;
        }
        if (m.IsHub && !m.CanUpdateInHub)
        {
            // Same as the banner: install the downloaded zip if it's there, otherwise send the player to Nexus.
            var zip = LocalFiles.Downloads.Where(d => d.Check.Ok && d.Check.Guid == Plugin.Guid && Updates.Newer(d.Check.Version, Plugin.Version))
                                          .OrderByDescending(d => d.When).FirstOrDefault();
            if (zip != null)
            {
                bool ok = Installer.Install(zip.Path, zip.Check, "Nexus:" + Updates.HubNexusId) != null;
                Catalog.Scan(); Updates.Refresh();
                Toast(ok ? L.T("Mod Hub updated. Restart the game to use the new version.", "تم تحديث Mod Hub. أعد تشغيل اللعبة لاستخدام النسخة الجديدة.")
                         : L.T("Could not update Mod Hub, see the BepInEx log.", "تعذر تحديث Mod Hub، شوف سجل BepInEx."));
            }
            else
            {
                Application.OpenURL($"https://www.nexusmods.com/megabonk/mods/{Updates.HubNexusId}?tab=files");
                _watchDownloadsUntil = Time.unscaledTime + 300f;
                Toast(L.T("Click \"Manual download\" on the Main file. Mod Hub will offer to install it here.",
                          "اضغط \"Manual download\" على الملف الرئيسي، والـ Mod Hub بيعرض عليك تثبيته هنا."));
            }
            return;
        }
        var pkg = m.CanUpdateInHub ? Updates.StorePackageFor(m) : null;
        if (pkg != null)
        {
            Installing.Add(m.Id);
            // Keep the mod in the folder it already has, so settings and switches stay attached to it.
            Online.InstallFromStore(pkg, msg =>
            {
                Installing.Remove(m.Id);
                if (after != null) after(msg);
                else Toast(m.IsHub ? L.T("Mod Hub updated. Restart the game to use the new version.", "تم تحديث Mod Hub. أعد تشغيل اللعبة لاستخدام النسخة الجديدة.") : msg);
            }, m.IsFolder ? Melon.FolderOf(m.Id) : null);
        }
        else if ((!Online.StoreFresh && Online.StoreError == null) || Online.IsChecking(Updates.StorePackageFor(m)))
        {
            // Thunderstore hasn't answered yet: it may be able to update this mod right here, so don't send the player to a browser.
            Online.LoadStore();
            Toast(L.T("Still checking for updates, try again in a moment.", "لسا نتحقق من التحديثات، جرب بعد لحظة."));
        }
        else if (m.LatestUrl != null)
        {
            Application.OpenURL(m.LatestUrl);
            Toast(L.T("Download the new version, then install it from the Get mods tab.", "حمل النسخة الجديدة ثم ثبتها من تبويب تحميل مودات."));
        }
    }

    /// <summary>Updates the mods one after another (two installs at once could trip over each other).</summary>
    private static void UpdateAll(List<Mod> mods)
    {
        foreach (var m in mods) _updateQueue.Enqueue(m);
        _updatingAll = true;
        _updatedCount = 0;
        UpdateNext();
    }

    private static void UpdateNext()
    {
        if (_updateQueue.Count == 0)
        {
            _updatingAll = false;
            int manual = Catalog.Mods.Count(m => m.Latest != null && !m.CanUpdateInHub);
            Toast(L.T($"Updated {_updatedCount} mod(s). Restart the game to use them.", $"تم تحديث {_updatedCount} مود. أعد تشغيل اللعبة لاستخدامها.")
                  + (manual > 0 ? (Edition.Nexus ? L.T($" {manual} more need a download from Nexus.", $" و{manual} تحتاج تحميل من Nexus.")
                                                 : L.T($" {manual} more need your mod manager.", $" و{manual} تحتاج مدير المودات.")) : ""));
            return;
        }
        var next = _updateQueue.Dequeue();
        UpdateMod(next, msg => { if (Online.LastInstallOk) _updatedCount++; UpdateNext(); });
    }

    /// <summary>Where an installed mod's picture and details come from: its Thunderstore page, else Nexus.</summary>
    private static StorePackage InstalledPackage(Mod m) =>
        Updates.StorePackageFor(m) ?? (m.IsHub ? Online.Nexus?.FirstOrDefault(n => n.Id == Updates.HubNexusId)?.Package
                                                : Online.Nexus?.FirstOrDefault(n => Same(n.Name, m.Name))?.Package);

    /// <summary>Installed a moment ago and not running yet: it can be taken back out with nothing to restart.</summary>
    private static bool JustInstalled(Mod m) =>
        m != null && !m.IsHub && !m.LoadedNow && Catalog.WasInstalledThisSession(m.Id) && Catalog.SwapFor(m) == null;

    private static void CancelInstall(Mod m)
    {
        if (Catalog.Uninstall(m))
        {
            History.Add(History.CancelInstall, m.Name);
            Catalog.ForgetInstalled(m.Id);
            Toast(L.T($"Install cancelled: {m.Name} is gone again. Nothing to restart.", $"تم إلغاء التثبيت: {m.Name} انشال. ما تحتاج تعيد التشغيل."));
        }
        Catalog.Scan();
        Updates.Refresh();
    }

    private static void DrawModRow(Mod m, Rect r)
    {
        Card(r, m.On ? CardBg : new Color(1f, 0.4f, 0.4f, 0.07f));
        float bx = r.xMax - 8f;

        // Buttons (right side): remove, update, on/off
        // Installed a moment ago and not running yet: one press takes it back out (nothing to restart).
        if (JustInstalled(m))
        {
            bx -= 132f;
            if (Btn(new Rect(bx, r.y + 15f, 132f, 32f), L.T("Cancel install", "ألغِ التثبيت"), _button)) { CancelInstall(m); return; }
        }
        else if (!m.IsHub)
        {
            bool confirming = _confirmRemove == m.Id && Time.unscaledTime < _confirmUntil && Time.unscaledTime > _confirmUntil - 5.5f;
            bx -= 92f;
            if (Btn(new Rect(bx, r.y + 15f, 92f, 32f), confirming ? L.T("Sure?", "متأكد؟") : L.T("Remove", "حذف"), _button))
            {
                if (confirming)
                {
                    bool removed = m.IsCharacter ? Characters.Remove(m) : Catalog.Uninstall(m);
                    if (removed) History.Add(m.IsCharacter ? History.CharacterRemove : History.Remove, m.Name, m.Version);
                    if (removed) Toast(L.T($"{m.Name} removed (moved to BepInEx\\_hub_removed).", $"تم حذف {m.Name} (نُقل إلى BepInEx\\_hub_removed)."));
                    else Toast(L.T($"{m.Name} could not be removed: a file is still in use. Close the game and try again.", $"{m.Name}: تعذر الحذف، ملف لا يزال مستخدماً. سكّر اللعبة وحاول مرة ثانية."));
                    _confirmRemove = null;
                    Catalog.Scan();
                    Updates.Refresh();
                    return;
                }
                _confirmRemove = m.Id; _confirmUntil = Time.unscaledTime + 6f;
            }
        }
        // After an update the previous copy is kept: "Back to vX". After going back: "Redo vY" (no download needed,
        // so it replaces the normal Update button when it's the same version).
        var swap = Catalog.SwapFor(m);
        bool swapNewer = swap != null && Catalog.SwapIsNewer(m, swap);
        // A kept copy that is as new as the store's version replaces the download: one "Update" button only.
        bool keptIsUpdate = swapNewer && m.Latest != null && !Updates.Newer(m.Latest, swap.OtherVersion);
        if (m.Latest != null && !keptIsUpdate)
        {
            bx -= 118f;
            bool busy = Installing.Contains(m.Id);
            if (Btn(new Rect(bx, r.y + 15f, 112f, 32f), busy ? L.T("Updating...", "جاري التحديث...") : L.T($"Update {m.Latest}", $"تحديث {m.Latest}"), _buttonOn) && !busy)
                UpdateMod(m);
        }
        if (swap != null)
        {
            // Updated in this session and not restarted yet: the game still runs the old version, so going back
            // just cancels the update (nothing to restart).
            bool cancel = !swapNewer && swap.OtherIsOlder && Catalog.WasInstalledThisSession(m.Id);
            bx -= 128f;
            string label = cancel ? L.T("Cancel update", "ألغِ التحديث")
                         : swapNewer ? L.T($"Update {swap.OtherVersion}", $"تحديث {swap.OtherVersion}")
                         : L.T($"Back to {swap.OtherVersion}", $"ارجع لـ {swap.OtherVersion}");
            if (Btn(new Rect(bx, r.y + 15f, 122f, 32f), label, swapNewer ? _buttonOn : _button))
            {
                string from = m.Version, to = swap.OtherVersion;
                if (!Catalog.SwapVersion(m, out string error))
                    Toast(L.T($"Couldn't switch {m.Name}: {error}", $"ما قدرت أبدّل {m.Name}: {error}"));
                else if (cancel)
                {
                    History.Add(History.CancelUpdate, m.Name, from, to);
                    Catalog.ForgetInstalled(m.Id);
                    Toast(L.T($"Update cancelled: {m.Name} stays on {to}. Nothing to restart.", $"تم إلغاء التحديث: {m.Name} يبقى على {to}. ما تحتاج تعيد التشغيل."));
                }
                else if (swapNewer)
                {
                    History.Add(History.Redo, m.Name, from, to);
                    Toast(L.T($"{m.Name} updates to {to} after restart (from the saved copy, no download).", $"{m.Name} يتحدّث إلى {to} بعد إعادة التشغيل (من النسخة المحفوظة، بدون تحميل)."));
                }
                else
                {
                    History.Add(History.Back, m.Name, from, to);
                    Toast(L.T($"{m.Name} goes back to {to} after restart. You can update again any time.", $"{m.Name} يرجع إلى {to} بعد إعادة التشغيل. تقدر تحدّثه مرة ثانية متى ما تبي."));
                }
                Catalog.Scan();
                Updates.Refresh();
                return;
            }
        }
        if (!m.IsHub)
        {
            bx -= 100f;
            if (Btn(new Rect(bx, r.y + 15f, 94f, 32f), m.On ? L.T("ON", "شغال") : L.T("OFF", "مطفي"), m.On ? _pillOn : _pillOff))
            {
                List<string> also;
                if (m.IsCharacter) { Characters.SetEnabled(m, !m.On); also = new List<string>(); }
                else also = Catalog.Set(m, !m.On);
                History.Add(m.IsCharacter ? (m.On ? History.CharacterOn : History.CharacterOff) : (m.On ? History.On : History.Off),
                            m.Name, detail: also.Count > 0 ? string.Join(", ", also) : null);
                Toast((m.On ? L.T($"{m.Name} will be ON after restart.", $"{m.Name} بيشتغل بعد إعادة التشغيل.")
                            : L.T($"{m.Name} will be OFF after restart.", $"{m.Name} بينطفي بعد إعادة التشغيل."))
                      + (also.Count > 0 ? L.T($" Also changed: {string.Join(", ", also)}.", $" وتغير معه: {string.Join("، ", also)}.") : ""));
                Catalog.Scan();
                Updates.Refresh();
            }
        }

        // What's new: the notes of the update on offer, or of the one just installed.
        if (WhatsNewFor(m) is Change change)
        {
            bx -= 112f;
            if (Btn(new Rect(bx, r.y + 15f, 106f, 32f), L.T("What's new", "وش الجديد"), _button))
            { OpenWhatsNew(new List<Change> { change }, m.Latest != null); return; }
        }

        // Picture (from Thunderstore or Nexus) and, further down, click-for-details on the left part of the row
        var pkg = InstalledPackage(m);
        var ev = Event.current;
        var textArea = new Rect(r.x, r.y, bx - r.x - 8f, r.height);
        bool hoverText = pkg != null && textArea.Contains(ev.mousePosition);
        if (hoverText) Card(textArea, new Color(1f, 1f, 1f, 0.085f));
        var iconRect = new Rect(r.x + 6f, r.y + 5f, r.height - 10f, r.height - 10f);
        DrawIcon(iconRect, pkg?.IconUrl);
        float lx = iconRect.xMax + 10f;

        // Text (left side)
        float tw = bx - lx - 8f;
        string title = m.Name + (string.IsNullOrEmpty(m.Version) ? "" : "  v" + m.Version) + (m.IsHub ? L.T("  (this hub)", "  (هذا المركز)") : "")
                       + (m.IsMelon ? "  ·  Melon" : "")
                       + (m.IsCharacter ? L.T("  ·  Character", "  ·  شخصية") : "");
        Label(new Rect(lx, r.y + 7f, tw, 24f), Fit(title, _row, tw), _row, m.On ? Color.white : Grey, left: true);

        string status; Color sc;
        if (m.IsCharacter)
        {
            var loader = Characters.FindLoader();
            string want = Characters.LoaderVersion();
            string name = Characters.LoaderName;
            if (loader == null) { status = L.T($"Needs {name} {want} (the old 1.4.0 does not work with this game version)", $"تحتاج {name} {want} (النسخة القديمة 1.4.0 ما تشتغل مع نسخة اللعبة هذي)"); sc = Gold; }
            else if (!loader.On) { status = L.T($"Won't load: turn {name} back ON", $"ما راح تشتغل: شغّل {name} من جديد"); sc = Red; }
            else if (Characters.LoaderProblem() != null) { status = L.T($"Won't load: {name} does not work with this game version — install {want}", $"ما راح تشتغل: {name} ما يشتغل مع نسخة اللعبة هذي — ثبّت {want}"); sc = Red; }
            else if (m.On != m.LoadedNow) { status = m.On ? L.T("Turns ON after restart", "يشتغل بعد إعادة التشغيل") : L.T("Turns OFF after restart", "ينطفي بعد إعادة التشغيل"); sc = Gold; }
            else { status = m.On ? L.T("On · character", "شغالة · شخصية") : L.T("Off", "مطفية"); sc = m.On ? Green : Grey; }
        }
        else if (m.Duplicate) { status = L.T("Old duplicate copy: press Remove", "نسخة قديمة مكررة: اضغط حذف"); sc = Red; }
        else if (m.IsMelon && m.On && !Melon.BridgeInstalled) { status = L.T("Needs Melon Bridge: reinstall Mod Hub", "يحتاج Melon Bridge: ثبّت Mod Hub من جديد"); sc = Red; }
        else if (m.IsMelon && m.On && !Melon.BridgeOn) { status = L.T("Needs Melon Bridge: switch it on", "يحتاج Melon Bridge: شغّله"); sc = Red; }
        else if (m.IsMelon && m.On && (m.MelonState == "refused" || m.MelonState == "off") && !Catalog.WasInstalledThisSession(m.Id))
        { status = L.T($"Not loaded: {m.MelonDetail}", $"ما اشتغل: {m.MelonDetail}"); sc = Red; }
        else if (m.IsMelon && m.On && m.MelonState == "partial") { status = L.T("Running, part of it paused after errors", "شغال، وجزء منه موقف بسبب أخطاء"); sc = Gold; }
        else if (m.Twin != null && m.On) { status = L.T($"Also installed as {m.Twin}: keep only one", $"مثبت كمان باسم {m.Twin}: خل واحد بس"); sc = Gold; }
        else if (m.On && !m.LoadedNow) { status = L.T("Turns ON after restart", "يشتغل بعد إعادة التشغيل"); sc = Gold; }
        else if (!m.On && m.LoadedNow) { status = L.T("Turns OFF after restart", "ينطفي بعد إعادة التشغيل"); sc = Gold; }
        else if (m.On) { status = L.T("Running", "شغال الحين"); sc = Green; }
        else { status = L.T("Off", "مطفي"); sc = Grey; }
        if (m.Latest != null) { status += L.T($"  ·  new version {m.Latest}", $"  ·  نسخة جديدة {m.Latest}"); }
        else if (m.LatestUrl != null && m.Version != null) status += L.T("  ·  up to date", "  ·  آخر نسخة");
        Label(new Rect(lx, r.y + 33f, tw, 22f), Fit(status, _small, tw), _small, sc, left: true);
        // Red badge right after the status: this mod wrote errors to the BepInEx log this session.
        if (m.Errors > 0)
        {
            float sw = _small.CalcSize(new GUIContent(status)).x;
            Label(new Rect(lx + sw, r.y + 33f, Mathf.Max(0f, tw - sw), 22f),
                L.T($"  ·  {m.Errors} error(s) in the log", $"  ·  {m.Errors} خطأ في السجل"), _small, Red, left: true);
        }

        // Clicking the row opens the mod's page.
        if (pkg != null && NavItem(textArea)) { OpenDetails(pkg); return; }
        if (hoverText && ev.type == EventType.MouseDown && ev.button == 0) { OpenDetails(pkg); ev.Use(); return; }
    }

    // ------------------------------------------------------------------ get mods

    private static void DrawGetMods(Event e, Vector2 mouse, Rect body)
    {
        if (_details != null) { DrawDetails(e, mouse, body); return; }
        float x = body.x, y = body.y, w = body.width;

        if (Edition.Thunderstore)
        {
            // The Thunderstore build installs from Thunderstore only: no drop folder, Downloads or Nexus.
            Label(new Rect(x, y + 5f, w - 210f, 22f), L.T("Every mod here comes from Thunderstore and installs in one click.",
                "كل المودات هنا من Thunderstore وتتثبت بضغطة وحدة."), _small, Grey, left: true);
            if (Btn(new Rect(x + w - 200f, y, 200f, 32f), L.T("Open Thunderstore", "افتح Thunderstore"), _button))
                Application.OpenURL("https://thunderstore.io/c/megabonk/");
            y += 40f;
        }
        else
        {
            // Add your own
            Label(new Rect(x, y, w, 22f), L.T("Have a mod file? Put its .zip or .dll in the drop folder and it installs by itself:",
                "عندك ملف مود؟ حط الـ .zip أو .dll في مجلد الإسقاط ويتثبت لحاله:"), _small, Grey);
            y += 24f;
            float bw = (w - 16f) / 3f;
            if (Btn(new Rect(x, y, bw, 32f), L.T("Open drop folder", "افتح مجلد الإسقاط"), _buttonOn))
            { LocalFiles.EnsureDropDir(); Application.OpenURL("file:///" + LocalFiles.DropDir.Replace('\\', '/')); }
            if (Btn(new Rect(x + bw + 8f, y, bw, 32f), L.T("Browse Nexus", "تصفح Nexus"), _button))
                Application.OpenURL("https://www.nexusmods.com/games/megabonk/mods");
            if (Btn(new Rect(x + 2 * (bw + 8f), y, bw, 32f), L.T("Check Downloads again", "افحص التنزيلات"), _button))
                LocalFiles.ScanDownloads();
            y += 40f;
        }

        // From Downloads
        // Hide zips of mods you already have at the same or a newer version.
        var downloads = LocalFiles.Downloads.Where(d => !Catalog.Mods.Any(m => Same(m.Name, d.Check.ModName) && !Updates.Newer(d.Check.Version, m.Version))).Take(3).ToList();
        if (downloads.Count > 0)
        {
            Label(new Rect(x, y, w, 22f), L.T("Found in your Downloads:", "لقيت في التنزيلات:"), _row, Gold);
            y += 24f;
            foreach (var d in downloads)
            {
                var r = new Rect(x, y, w, 40f);
                Card(r, CardBg);
                // Right side: Remove (to the Recycle Bin, after "Sure?"), then Install or why it can't be installed.
                bool sure = _confirmRemove == d.Path && Time.unscaledTime < _confirmUntil && Time.unscaledTime > _confirmUntil - 5.5f;
                if (Btn(new Rect(r.xMax - 108f, y + 5f, 100f, 30f), sure ? L.T("Sure?", "متأكد؟") : L.T("Remove", "حذف"), _button))
                {
                    if (sure)
                    {
                        _confirmRemove = null;
                        Toast(LocalFiles.RecycleDownload(d.Path)
                            ? L.T($"{System.IO.Path.GetFileName(d.Path)} moved to the Recycle Bin.", $"{System.IO.Path.GetFileName(d.Path)} انتقل لسلة المحذوفات.")
                            : L.T("Could not remove the file (is it open?).", "تعذر حذف الملف (مفتوح؟)."));
                        y += 44f;
                        continue;
                    }
                    _confirmRemove = d.Path; _confirmUntil = Time.unscaledTime + 6f;
                }
                float right = r.xMax - 116f;
                if (d.Check.Ok)
                {
                    right -= 130f;
                    if (Btn(new Rect(right, y + 5f, 122f, 30f), L.T("Install", "تثبيت"), _buttonOn))
                    {
                        if (d.Check.IsCharacter)
                        {
                            string character = null;
                            try { character = Characters.InstallCharacterZip(d.Path, "Downloads"); }
                            catch (Exception ex) { Plugin.Logger.LogWarning($"Character install failed for {d.Path}: {ex.Message}"); }
                            Toast(character != null
                                ? L.T($"Installed the character {character}. Restart the game to play it.", $"تم تثبيت الشخصية {character}. أعد تشغيل اللعبة.") + (Characters.AfterInstall() ?? "")
                                : L.T("Install failed, see the BepInEx log.", "فشل التثبيت، شوف سجل BepInEx."));
                        }
                        else
                        {
                            string folder = Installer.Install(d.Path, d.Check, "Downloads");
                            Toast(folder != null ? L.T($"Installed {d.Check.ModName}. Restart the game to use it.", $"تم تثبيت {d.Check.ModName}. أعد تشغيل اللعبة.")
                                                   + (d.Check.Note != null ? " " + d.Check.Note : "")
                                                 : L.T("Install failed, see the BepInEx log.", "فشل التثبيت، شوف سجل BepInEx."));
                        }
                        Catalog.Scan(); Updates.Refresh(); LocalFiles.ScanDownloads();
                    }
                }
                else
                {
                    right -= 330f;
                    string why = d.Check.Problem ?? L.T("Can't be installed", "ما ينفع يتثبت");
                    Label(new Rect(right, y + 9f, 322f, 22f), Fit(why, _small, 322f), _small, Red, right: true);
                }

                // Left side: mod name and file name, cut to fit (no running into the buttons).
                // "Mod name  v1.2  (file.zip)", or just the file name when the mod's name couldn't be read.
                string file = System.IO.Path.GetFileName(d.Path);
                string name = d.Check.ModName != null
                    ? d.Check.ModName + (d.Check.Version != null ? "  v" + d.Check.Version : "") + "   (" + file + ")"
                    : file;
                Label(new Rect(x + 10f, y + 9f, right - x - 20f, 22f), Fit(name, _small, right - x - 20f), _small, Color.white, left: true);
                y += 44f;
            }
            y += 4f;
        }

        // Where to browse (Thunderstore installs in one click; Nexus needs its page) and how to sort
        string[] sources = { "Thunderstore", "Nexus" };
        for (int i = 0; i < 2 && Edition.Nexus; i++)
            if (Btn(new Rect(x + i * 136f, y, 130f, 30f), sources[i], _nexusList == (i == 1) ? _segment : _button)) { _nexusList = i == 1; _scroll = 0; }
        string[] sorts = { L.T("Popular", "الأشهر"), L.T("Newest", "الأحدث"), L.T("Updated", "آخر تحديث") };
        for (int i = 0; i < 3; i++)
            if (Btn(new Rect(x + w - (3 - i) * 116f + 6f, y, 110f, 30f), sorts[i], _sort == i ? _segment : _button)) { _sort = i; _scroll = 0; }
        y += 38f;
        DrawSearchBox(new Rect(x, y, w, 32f), e, mouse);
        HandleTyping(e);
        y += 38f;

        List<StorePackage> source;
        if (_nexusList) { Online.LoadNexus(); source = Online.Nexus?.Select(n => n.Package).ToList(); }
        else source = Online.Store;
        var view = new Rect(x, y, w, body.yMax - y);
        if (source == null)
        {
            Label(new Rect(x, y + 10f, w, 24f), !_nexusList && Online.StoreError != null
                ? L.T("Could not reach Thunderstore (no internet?).", "تعذر الوصول لـ Thunderstore (ما فيه نت؟).")
                : L.T("Loading the mod list...", "جاري تحميل قائمة المودات..."), _row, Grey);
            return;
        }
        string s = _search.Trim();
        var shown = source.Where(p => s.Length == 0 || p.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                                      || (p.Description ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                                      || (p.Owner ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
        shown = _sort switch
        {
            1 => shown.OrderByDescending(p => p.Created),
            2 => shown.OrderByDescending(p => p.Updated),
            _ => shown.OrderByDescending(p => p.Downloads),
        };
        var list = shown.ToList();
        Pages(e, mouse, view, list.Count, (i, r) => DrawStoreRow(list[i], r));
    }

    /// <summary>
    /// A list split into pages: as many rows as fit, with "Previous / Page x of y / Next" under them.
    /// _scroll is the page number here (it resets to the first page on search, sort and source changes).
    /// The mouse wheel turns pages too.
    /// </summary>
    private static void Pages(Event e, Vector2 mouse, Rect view, int count, Action<int, Rect> drawRow)
    {
        const float BarH = 38f;
        int perPage = Mathf.Max(1, Mathf.FloorToInt((view.height - BarH) / RowH));
        int pages = Mathf.Max(1, Mathf.CeilToInt(count / (float)perPage));
        if (e.type == EventType.ScrollWheel && view.Contains(mouse)) { _scroll += e.delta.y > 0 ? 1 : -1; e.Use(); }
        _scroll = Mathf.Clamp(_scroll, 0, pages - 1);
        NavArea(view, _scroll > 0, _scroll < pages - 1, false, d => _scroll += d);

        int first = _scroll * perPage;
        for (int i = 0; i < perPage && first + i < count; i++)
            drawRow(first + i, new Rect(view.x, view.y + i * RowH, view.width, RowH - 6f));
        if (count == 0) { Label(new Rect(view.x, view.y + 10f, view.width, 24f), L.T("Nothing here.", "ما فيه شيء هنا."), _row, Grey); return; }

        // Page bar (Arabic reads right to left, so "next" sits on the left there).
        float by = view.yMax - BarH + 6f, mid = view.x + view.width / 2f;
        var prevRect = new Rect(L.Arabic ? view.xMax - 150f : view.x, by, 150f, 30f);
        var nextRect = new Rect(L.Arabic ? view.x : view.xMax - 150f, by, 150f, 30f);
        if (_scroll > 0 && Btn(prevRect, L.T("< Previous", "السابق >"), _button)) _scroll--;
        if (_scroll < pages - 1 && Btn(nextRect, L.T("Next >", "< التالي"), _buttonOn)) _scroll++;
        Label(new Rect(mid - 150f, by + 5f, 300f, 22f),
            L.T($"Page {_scroll + 1} of {pages}   ·   {count} mods", $"صفحة {_scroll + 1} من {pages}   ·   {count} مود"), _small, Grey);
    }

    /// <summary>Shortens text with "..." so it fits the width.</summary>
    private static readonly Dictionary<(string, int, float), string> FitCache = new();
    private static string Fit(string text, GUIStyle style, float width)
    {
        var key = (text, style.fontSize, width);
        if (FitCache.TryGetValue(key, out var cached)) return cached; // measuring letter by letter every frame is wasteful
        string result = text;
        if (style.CalcSize(new GUIContent(text)).x > width)
        {
            int n = text.Length;
            while (n > 4 && style.CalcSize(new GUIContent(text.Substring(0, n) + "...")).x > width) n--;
            result = text.Substring(0, n) + "...";
        }
        if (FitCache.Count > 200) FitCache.Clear();
        FitCache[key] = result;
        return result;
    }

    private static bool _nexusList;   // Get mods: browsing Nexus instead of Thunderstore
    private static int _sort;         // 0 popular, 1 newest, 2 recently updated

    private static void DrawStoreRow(StorePackage p, Rect r)
    {
        var ev = Event.current;
        bool hover = r.Contains(ev.mousePosition);
        Card(r, hover ? CardHover : CardBg);

        var btn = new Rect(r.xMax - 150f, r.y + 12f, 142f, 32f);
        StoreButton(p, btn);
        if (NavItem(new Rect(r.x, r.y, btn.x - r.x - 6f, r.height))) { OpenDetails(p); return; }
        // Anywhere else on the row opens the mod's page.
        if (ev.type == EventType.MouseDown && ev.button == 0 && hover && !btn.Contains(ev.mousePosition)) { OpenDetails(p); ev.Use(); return; }

        var iconRect = new Rect(r.x + 6f, r.y + 5f, r.height - 10f, r.height - 10f);
        DrawIcon(iconRect, p.IconUrl);

        float lx = iconRect.xMax + 10f, tw = btn.x - lx - 12f;
        // MelonLoader mods get a small gold tag after the name (they run through Melon Bridge).
        string title = $"{p.DisplayName}  v{p.Version}", tag = Melon.TagFor(p);
        if (tag != null && L.Arabic) title += "  ·  " + tag;
        Label(new Rect(lx, r.y + 6f, tw, 24f), Fit(title, _row, tw), _row, Color.white);
        if (tag != null && !L.Arabic)
        {
            float titleW = Mathf.Min(_row.CalcSize(new GUIContent(title)).x, tw);
            Label(new Rect(lx + titleW + 10f, r.y + 8f, Mathf.Max(0f, tw - titleW - 10f), 22f), "·  " + tag, _small, Gold, left: true);
        }
        // A mod known not to work in this game version says so in red instead of its description.
        string broken = Melon.BrokenReason(p);
        if (broken != null) { Label(new Rect(lx, r.y + 31f, tw, 22f), Fit(broken, _small, tw), _small, Red, left: true); return; }
        string desc = p.Description ?? "";
        if (desc.Length > 72) desc = desc.Substring(0, 69) + "...";
        Label(new Rect(lx, r.y + 31f, tw, 22f),
            L.Arabic ? $"{p.Owner} · {desc}" : $"by {p.Owner} · {p.Downloads:N0} downloads · {desc}", _small, Grey, left: true);
        if (L.Arabic) Label(new Rect(lx, r.y + 8f, tw, 22f), $"{p.Downloads:N0} تحميل", _small, Grey);
    }

    private static bool Same(string a, string b)
    {
        static string K(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var x = K(a); return x.Length > 2 && x == K(b);
    }

    // ------------------------------------------------------------------ list, search, scrolling

    private static void List(Event e, Vector2 mouse, Rect view, int count, Action<int, Rect> drawRow)
    {
        int visible = Mathf.Max(1, Mathf.FloorToInt(view.height / RowH));
        int maxRow = Mathf.Max(0, count - visible);

        if (e.type == EventType.ScrollWheel && view.Contains(mouse)) { _scroll += e.delta.y > 0 ? 2 : -2; e.Use(); }
        var bar = new Rect(view.xMax - ScrollbarW, view.y, ScrollbarW, view.height);
        if (maxRow > 0)
        {
            var grab = new Rect(bar.x - 6f, bar.y, bar.width + 12f, bar.height);
            if (e.type == EventType.MouseDown && e.button == 0 && grab.Contains(mouse)) _dragBar = true;
            if (e.rawType == EventType.MouseUp) _dragBar = false;
            if (_dragBar && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
            { _scroll = Mathf.RoundToInt(Mathf.InverseLerp(bar.y, bar.yMax, mouse.y) * maxRow); e.Use(); }
        }
        _scroll = Mathf.Clamp(_scroll, 0, maxRow);
        NavArea(view, _scroll > 0, _scroll < maxRow, true, d => _scroll += d);

        float rowW = view.width - (maxRow > 0 ? ScrollbarW + 8f : 0f);
        for (int i = 0; i < visible && _scroll + i < count; i++)
            drawRow(_scroll + i, new Rect(view.x, view.y + i * RowH, rowW, RowH - 6f));
        if (count == 0) Label(new Rect(view.x, view.y + 10f, view.width, 24f), L.T("Nothing here.", "ما فيه شيء هنا."), _row, Grey);

        if (maxRow > 0 && e.type == EventType.Repaint)
        {
            Card(bar, new Color(1f, 1f, 1f, 0.06f), 5f);
            float thumbH = Mathf.Max(30f, bar.height * visible / count);
            float thumbY = bar.y + (bar.height - thumbH) * (_scroll / (float)maxRow);
            Card(new Rect(bar.x, thumbY, bar.width, thumbH), _dragBar ? Gold : new Color(1f, 1f, 1f, 0.45f), 5f);
        }
    }

    private static void DrawSearchBox(Rect box, Event e, Vector2 mouse)
    {
        if (NavItem(box)) _searchFocused = true;
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            var clear = new Rect(box.xMax - 28f, box.y, 28f, box.height);
            if (_search.Length > 0 && clear.Contains(mouse)) { _search = ""; _scroll = 0; _searchFocused = true; e.Use(); }
            else if (box.Contains(mouse)) { _searchFocused = true; e.Use(); }
            else _searchFocused = false;
        }
        BorderedCard(box, _searchFocused ? new Color(0.13f, 0.15f, 0.18f, 1f) : new Color(0.1f, 0.115f, 0.14f, 1f), _searchFocused ? Gold : new Color(1f, 1f, 1f, 0.16f), 8f);
        var text = new Rect(box.x + 10f, box.y + 6f, box.width - 44f, box.height - 8f);
        if (_search.Length == 0 && !_searchFocused) { Label(text, L.T("Click here to search mods...", "اضغط هنا للبحث عن مود..."), _small, Grey); return; }
        bool caret = _searchFocused && (int)(Time.unscaledTime * 2f) % 2 == 0;
        Label(text, _search + (caret ? "|" : ""), _row, Color.white);
        if (_search.Length > 0) Label(new Rect(box.xMax - 22f, box.y + 5f, 18f, 22f), "x", _row, Grey);
    }

    private static void HandleTyping(Event e)
    {
        if (!_searchFocused || e.type != EventType.KeyDown) return;
        switch (e.keyCode)
        {
            case KeyCode.Backspace:
                if (_search.Length > 0) { _search = _search.Substring(0, _search.Length - 1); _scroll = 0; }
                e.Use(); return;
            case KeyCode.Escape: case KeyCode.Return: case KeyCode.KeypadEnter:
                _searchFocused = false; e.Use(); return;
        }
        char c = e.character;
        if (c != '\0' && !char.IsControl(c)) { if (_search.Length < 40) { _search += c; _scroll = 0; } e.Use(); }
    }

    // ------------------------------------------------------------------ helpers

    private static float _lastPress = -10f;

    private static bool Btn(Rect r, string text, GUIStyle style)
    {
        bool pad = NavItem(r);
        var look = ButtonLook(r, style); // rounded background (primary, secondary, pill or selected tab)
        bool hit = GUI.Button(r, new GUIContent(Rtl.Visual(text ?? "", look, 0f)), look) || pad;
        if (!hit) return false;
        // One press at a time: a click that arrives twice (or a stuck mouse event) must never turn many pages
        // or confirm a "Sure?" by itself.
        if (Time.unscaledTime - _lastPress < 0.25f) return false;
        _lastPress = Time.unscaledTime;
        return true;
    }

    private static void Label(Rect r, string text, GUIStyle style, Color c, bool right = false, bool left = false)
    {
        text ??= "";
        var s = style;
        if (Rtl.HasArabic(text)) { text = Rtl.Visual(text, style, r.width); s = Aligned(style, !left); }
        else if (right) s = Aligned(style, true);
        var old = GUI.contentColor;
        GUI.contentColor = c;
        GUI.Label(r, text, s);
        GUI.contentColor = old;
    }

    private static readonly Dictionary<IntPtr, GUIStyle> RightStyles = new();
    private static GUIStyle Aligned(GUIStyle s, bool right)
    {
        if (!right) return s;
        if (RightStyles.TryGetValue(s.Pointer, out var r)) return r;
        r = new GUIStyle(s) { wordWrap = false };
        r.alignment = s.alignment switch
        {
            TextAnchor.UpperLeft => TextAnchor.UpperRight,
            TextAnchor.MiddleLeft => TextAnchor.MiddleRight,
            TextAnchor.LowerLeft => TextAnchor.LowerRight,
            _ => s.alignment,
        };
        return RightStyles[s.Pointer] = r;
    }

    private static void Fill(Rect r, Color c)
    {
        var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = old;
    }

    private static void Outline(Rect r, Color c, float t)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c); Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c); Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    private static void EnsureStyles()
    {
        if (_stylesReady && _white != null) return;
        _cTL = null; // rebuild the theme textures together with the styles
        _white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        _white.SetPixel(0, 0, Color.white);
        _white.Apply();
        _title = Make(GUI.skin.label, 24, FontStyle.Bold);
        _sub = Make(GUI.skin.label, 17, FontStyle.Normal, wrap: true);
        _row = Make(GUI.skin.label, 16, FontStyle.Bold);
        _small = Make(GUI.skin.label, 13, FontStyle.Normal);
        _badge = Make(GUI.skin.label, 12, FontStyle.Bold);
        _button = Make(GUI.skin.button, 14, FontStyle.Bold);
        _buttonOn = Make(GUI.skin.button, 14, FontStyle.Bold);
        _buttonOn.normal.textColor = Gold;
        _buttonOn.hover.textColor = Gold;
        _stylesReady = true;
    }

    private static GUIStyle Make(GUIStyle baseStyle, int size, FontStyle fs, bool wrap = false)
    {
        var s = new GUIStyle(baseStyle) { fontSize = size, fontStyle = fs, wordWrap = wrap, richText = false };
        s.normal.textColor = Color.white;
        return s;
    }
}

/// <summary>Frees the mouse while the Hub is open, then hands it back to the game.</summary>
internal static class CursorGuard
{
    private static bool _held;
    private static CursorLockMode _lock;
    private static bool _visible;

    public static void Hold()
    {
        if (!_held) { _lock = Cursor.lockState; _visible = Cursor.visible; _held = true; }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public static void Release()
    {
        if (!_held) return;
        _held = false;
        Cursor.lockState = _lock;
        Cursor.visible = _visible;
    }
}

/// <summary>While the Hub is open the game ignores the mouse and keys (local input only; co-op is unaffected).</summary>
internal static class InputGuard
{
    private static bool _players, _ui;
    private static readonly List<Player> Paused = new();
    private static BaseInputModule _module;
    private static bool _moduleWasEnabled;

    public static void Update(bool blockPlayers, bool blockUi)
    {
        if (blockPlayers != _players)
        {
            _players = blockPlayers;
            try
            {
                if (blockPlayers && ReInput.isReady)
                {
                    var players = ReInput.players;
                    for (int i = 0; i < players.playerCount; i++)
                    {
                        var p = players.GetPlayer(i);
                        if (p == null || !p.isPlaying) continue;
                        p.isPlaying = false;
                        Paused.Add(p);
                    }
                }
                else { foreach (var p in Paused) { try { p.isPlaying = true; } catch { } } Paused.Clear(); }
            }
            catch (Exception e) { Panel.LogOnce("Input guard (players)", e); }
        }
        if (blockUi != _ui)
        {
            _ui = blockUi;
            try
            {
                if (blockUi)
                {
                    // Switch off only the part that reads mouse and keys. Disabling the whole EventSystem makes the
                    // game's UiFocusManager throw an error every frame (it expects EventSystem.current to exist).
                    _module = EventSystem.current != null ? EventSystem.current.currentInputModule : null;
                    if (_module != null) { _moduleWasEnabled = _module.enabled; _module.enabled = false; }
                }
                else
                {
                    if (_module != null && !_module.WasCollected) _module.enabled = _moduleWasEnabled;
                    _module = null;
                }
            }
            catch (Exception e) { Panel.LogOnce("Input guard (menus)", e); }
        }
    }
}
