using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace ModHub;

internal sealed class StorePackage
{
    public string Owner, Name, FullName, Version, Description, DownloadUrl, PageUrl, IconUrl;
    public bool IsNexus; public int NexusId; public string PictureUrl; // Nexus mods shown in the same list
    public int NexusLink;  // a Thunderstore package whose website is its Nexus page: that mod's id (0 = none)
    public long Downloads;
    public DateTime Created, Updated;
    public List<string> Dependencies = new();
    public bool NeedsMelon => Dependencies.Any(d => d.Contains("MelonLoader", StringComparison.OrdinalIgnoreCase));
    public string DisplayName => Name.Replace('_', ' ');
}

internal sealed class NexusMod
{
    public int Id;
    public string Name, Version, Summary, Author, PictureUrl, ThumbnailUrl;
    public long Downloads;
    public DateTime Created, Updated;

    private StorePackage _package;
    /// <summary>The same mod shaped like a Thunderstore package, so the list and details page can show it.</summary>
    public StorePackage Package => _package ??= new StorePackage
    {
        IsNexus = true, NexusId = Id, Owner = Author, Name = Name, FullName = "Nexus:" + Id, Version = Version,
        Description = Summary, IconUrl = ThumbnailUrl ?? PictureUrl, PictureUrl = PictureUrl, Downloads = Downloads,
        Created = Created, Updated = Updated, PageUrl = $"https://www.nexusmods.com/megabonk/mods/{Id}",
    };
}

/// <summary>
/// Everything that talks to the internet, always on background threads so the game never waits:
/// the Thunderstore catalog (free, direct downloads), Nexus mod versions (public API, no key), downloads.
/// Results are handed to the game thread through <see cref="Pending"/>. Without internet it just stays empty.
/// </summary>
internal static class Online
{
    private const int MegabonkNexusId = 7674;
    internal static readonly HttpClient Http = MakeClient();
    private static readonly HashSet<string> ManagerApps = new(StringComparer.OrdinalIgnoreCase) { "r2modman", "GaleModManager", "ThunderstoreModManager" };

    public static volatile List<StorePackage> Store;          // null = not loaded yet
    public static volatile List<NexusMod> Nexus;
    public static volatile string StoreError;
    private static int _storeLoading, _nexusLoading;
    private static long _storeRetryAt, _nexusRetryAt; // after a failure, wait before trying again (no request spam offline)
    private static bool TooSoon(long at) => DateTime.UtcNow.Ticks < Interlocked.Read(ref at);
    private static long In30s => DateTime.UtcNow.AddSeconds(30).Ticks;

    private static readonly Queue<Action> Pending = new();
    public static void RunPending()
    {
        while (true)
        {
            Action a;
            lock (Pending) { if (Pending.Count == 0) return; a = Pending.Dequeue(); }
            try { a(); } catch (Exception e) { Plugin.Logger.LogWarning($"Mod Hub: {e.Message}"); }
        }
    }
    internal static void OnMain(Action a) { lock (Pending) Pending.Enqueue(a); }

    private static HttpClient MakeClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"MegabonkModHub/{Plugin.Version}");
        return h;
    }

    internal static void Background(string name, Action work) =>
        new Thread(() => { try { work(); } catch (Exception e) { Plugin.Logger.LogDebug($"{name}: {e.Message}"); } })
        { IsBackground = true, Name = name }.Start();

    // ------------------------------------------------------------------ Mod Hub's own updates (GitHub releases)

    /// <summary>
    /// Mod Hub updates itself from the releases of its public GitHub repo: GitHub allows direct downloads, Nexus
    /// doesn't (without Premium), and Thunderstore doesn't list the Hub. The Nexus page stays the main home.
    /// </summary>
    public const string HubRepo = "hmodymac8-droid/ModHub";

    internal sealed class HubRelease { public string Version, ZipUrl; }
    public static volatile HubRelease Release;   // the newest Mod Hub on GitHub (null = not known)
    private static int _releaseLoading;
    private static long _releaseRetryAt;
    private static volatile bool _releaseFresh;

    /// <summary>GitHub's latest release. A file BepInEx\config\ModHub.updatefeed with another address replaces it (testing).</summary>
    private static string ReleaseFeed
    {
        get
        {
            try
            {
                string f = Path.Combine(BepInEx.Paths.ConfigPath, "ModHub.updatefeed");
                if (File.Exists(f) && File.ReadAllText(f).Trim() is { Length: > 0 } url) return url;
            }
            catch { }
            return $"https://api.github.com/repos/{HubRepo}/releases/latest";
        }
    }

    public static void LoadHubRelease()
    {
        if (!Edition.SelfUpdate) return; // the Thunderstore build is updated by the mod manager
        if ((_releaseFresh && !Old(_releaseAt)) || TooSoon(_releaseRetryAt) || Interlocked.Exchange(ref _releaseLoading, 1) == 1) return;
        Background("ModHub-Release", () =>
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, ReleaseFeed);
                req.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var resp = Http.SendAsync(req).GetAwaiter().GetResult();
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) { _releaseFresh = true; Interlocked.Exchange(ref _releaseAt, DateTime.UtcNow.Ticks); return; } // no release yet
                resp.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                var r = doc.RootElement;
                string version = r.GetProperty("tag_name").GetString().Trim().TrimStart('v', 'V');
                string zip = null;
                foreach (var a in r.GetProperty("assets").EnumerateArray())
                {
                    string name = a.GetProperty("name").GetString() ?? "";
                    // The small "ModHub-x.y.z.zip" (Mod Hub + Melon Bridge), not the All-in-One with BepInEx.
                    if (name.StartsWith("ModHub-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        && name.IndexOf("AllInOne", StringComparison.OrdinalIgnoreCase) < 0)
                        zip = a.GetProperty("browser_download_url").GetString();
                }
                if (zip != null) { Release = new HubRelease { Version = version, ZipUrl = zip }; OnMain(Updates.Refresh); }
                _releaseFresh = true;
                Interlocked.Exchange(ref _releaseAt, DateTime.UtcNow.Ticks);
            }
            catch (Exception e) { Plugin.Logger.LogDebug($"Mod Hub release check: {e.Message}"); Interlocked.Exchange(ref _releaseRetryAt, In30s); }
            finally { Interlocked.Exchange(ref _releaseLoading, 0); }
        });
    }

    /// <summary>Downloads the newest Mod Hub and installs it over this one (it runs after a restart).</summary>
    public static void UpdateHub(Action<bool, string> done)
    {
        var rel = Release;
        Background("ModHub-SelfUpdate", () =>
        {
            bool ok = false;
            string problem = null;
            string tmp = Path.Combine(Path.GetTempPath(), $"ModHub-{rel.Version}.zip");
            try
            {
                File.WriteAllBytes(tmp, Http.GetByteArrayAsync(rel.ZipUrl).GetAwaiter().GetResult());
                var check = Installer.Check(tmp);
                // Only ever Mod Hub itself, and only a newer one.
                if (!check.Ok || check.Guid != Plugin.Guid || !Updates.Newer(check.Version, Plugin.Version))
                    problem = check.Problem ?? "the download isn't a newer Mod Hub";
                else
                {
                    ok = Installer.Install(tmp, check, "GitHub:" + HubRepo, check.Version) != null;
                    if (!ok) problem = "see the BepInEx log";
                }
            }
            catch (Exception e) { problem = e.Message; }
            try { File.Delete(tmp); } catch { }
            OnMain(() =>
            {
                Catalog.Scan();
                Updates.Refresh();
                done(ok, ok ? L.T($"Mod Hub {rel.Version} is installed. Restart the game to use it.", $"تم تثبيت Mod Hub {rel.Version}. أعد تشغيل اللعبة لاستخدامه.")
                            : L.T($"Couldn't update Mod Hub by itself ({problem}).", $"ما قدر Mod Hub يتحدّث لحاله ({problem})."));
            });
        });
    }

    // ------------------------------------------------------------------ Thunderstore

    public static volatile bool StoreFresh;   // downloaded this session (not just the saved copy)
    private static long _storeAt, _nexusAt, _releaseAt; // when the lists were last downloaded
    private static readonly TimeSpan Stale = TimeSpan.FromMinutes(5);    // opening the Hub re-downloads lists older than this
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(15);   // and while the game stays open, they are checked this often
    private static long _nextCheck;

    /// <summary>
    /// Called every frame: re-checks for updates every <see cref="Every"/> while the game stays open (Hub open or not),
    /// once the Hub has loaded the lists the first time. The work happens on background threads, and the 30 s retry
    /// wait after a failure still applies, so offline it stays quiet.
    /// </summary>
    public static void Tick()
    {
        long now = DateTime.UtcNow.Ticks;
        if (now < _nextCheck) return;
        _nextCheck = now + Every.Ticks;
        if (Interlocked.Read(ref _storeAt) == 0 && Interlocked.Read(ref _nexusAt) == 0) return; // not loaded yet: opening the Hub does that
        LoadStore();
        LoadNexus();
        LoadHubRelease();
    }
    private static bool Old(long at) => DateTime.UtcNow.Ticks - Interlocked.Read(ref at) > Stale.Ticks;
    private static string CacheDir => Path.Combine(BepInEx.Paths.CachePath, "ModHub");

    /// <summary>Thunderstore's list. A file BepInEx\config\ModHub.storefeed with another address or a local file replaces it (testing).</summary>
    private static string StoreJson()
    {
        try
        {
            string f = Path.Combine(BepInEx.Paths.ConfigPath, "ModHub.storefeed");
            if (File.Exists(f) && File.ReadAllText(f).Trim() is { Length: > 0 } feed)
                return File.Exists(feed) ? File.ReadAllText(feed) : Http.GetStringAsync(feed).GetAwaiter().GetResult();
        }
        catch (IOException) { }
        return Http.GetStringAsync("https://thunderstore.io/c/megabonk/api/v1/package/").GetAwaiter().GetResult();
    }

    /// <summary>
    /// Shows the list saved last time right away (Thunderstore can take several seconds to answer),
    /// then downloads the current one in the background and saves it for next time. The saved copy only fills
    /// the gap until that download arrives, and the list is downloaded again when it's older than <see cref="Stale"/>
    /// (a game can stay open for hours while authors release updates).
    /// </summary>
    public static void LoadStore()
    {
        if ((StoreFresh && !Old(_storeAt)) || TooSoon(_storeRetryAt) || Interlocked.Exchange(ref _storeLoading, 1) == 1) return;
        Background("ModHub-Thunderstore", () =>
        {
            string cache = Path.Combine(CacheDir, "thunderstore.json");
            try
            {
                if (Store == null && File.Exists(cache))
                {
                    var saved = ParseStore(File.ReadAllText(cache));
                    if (saved.Count > 0) { Store = WithChecked(saved); OnMain(Updates.Refresh); Characters.LoaderListArrived(); }
                }
            }
            catch { /* damaged copy: the download below replaces it */ }
            try
            {
                var json = StoreJson();
                Store = WithChecked(ParseStore(json));
                Characters.LoaderListArrived(); // a character installed offline can get its loader now
                StoreFresh = true;
                Interlocked.Exchange(ref _storeAt, DateTime.UtcNow.Ticks);
                StoreError = null;
                OnMain(Updates.Refresh);
                try { Directory.CreateDirectory(CacheDir); File.WriteAllText(cache, json); } catch { }
            }
            catch (Exception e) { if (Store == null) StoreError = e.Message; Interlocked.Exchange(ref _storeRetryAt, In30s); }
            finally { Interlocked.Exchange(ref _storeLoading, 0); }
        });
    }

    private static List<StorePackage> ParseStore(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<StorePackage>();
        foreach (var p in doc.RootElement.EnumerateArray())
        {
            if (p.GetProperty("is_deprecated").GetBoolean()) continue;
            var versions = p.GetProperty("versions");
            if (versions.GetArrayLength() == 0) continue;
            var v = versions[0];
            var pkg = new StorePackage
            {
                Owner = p.GetProperty("owner").GetString(),
                Name = p.GetProperty("name").GetString(),
                FullName = p.GetProperty("full_name").GetString(),
                PageUrl = p.GetProperty("package_url").GetString(),
                Created = p.TryGetProperty("date_created", out var dc) && dc.TryGetDateTime(out var c1) ? c1 : DateTime.MinValue,
                Updated = p.TryGetProperty("date_updated", out var du) && du.TryGetDateTime(out var u1) ? u1 : DateTime.MinValue,
            };
            SetVersion(pkg, v);
            foreach (var ver in versions.EnumerateArray()) pkg.Downloads += ver.GetProperty("downloads").GetInt64();
            if (pkg.Name.StartsWith("BepInExPack") || pkg.Name == "MelonLoader") continue; // loaders, not mods
            var cats = p.TryGetProperty("categories", out var c) ? c.EnumerateArray().Select(x => x.GetString()).ToList() : new List<string>();
            bool isMod = cats.Contains("Mods") || cats.Contains("Libraries") || cats.Contains("Misc");
            if ((cats.Count > 0 && !isMod) || ManagerApps.Contains(pkg.Name)) continue; // mod manager apps and modpacks
            list.Add(pkg);
        }
        return list.OrderByDescending(x => x.Downloads).ToList();
    }

    /// <summary>Fills in one version of a package (same shape in the v1 list and the experimental API's "latest").</summary>
    private static void SetVersion(StorePackage pkg, JsonElement v)
    {
        pkg.Version = v.GetProperty("version_number").GetString();
        pkg.Description = v.GetProperty("description").GetString();
        pkg.DownloadUrl = v.GetProperty("download_url").GetString();
        pkg.IconUrl = v.TryGetProperty("icon", out var icon) ? icon.GetString() : null;
        pkg.Dependencies = v.GetProperty("dependencies").EnumerateArray().Select(d => d.GetString()).ToList();
        // Authors who also publish on Nexus usually set it as the website: that ties the two pages together exactly.
        string site = v.TryGetProperty("website_url", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : "";
        var match = System.Text.RegularExpressions.Regex.Match(site, @"nexusmods\.com/megabonk/mods/(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        pkg.NexusLink = match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    // Packages asked about one by one, and the Nexus version that made us ask (so each one is asked once, not every refresh).
    private static readonly Dictionary<string, string> PackageChecks = new();
    // What those single-package answers said: they stay in the list until the full list catches up.
    private static readonly Dictionary<string, StorePackage> Checked = new();
    private static readonly HashSet<string> Asking = new();

    /// <summary>Thunderstore is being asked about this package right now.</summary>
    public static bool IsChecking(StorePackage pkg) { lock (PackageChecks) return pkg != null && Asking.Contains(pkg.FullName); }

    private static List<StorePackage> WithChecked(List<StorePackage> list)
    {
        lock (PackageChecks)
            return Checked.Count == 0 ? list
                : list.Select(p => Checked.TryGetValue(p.FullName, out var c) && Updates.Newer(c.Version, p.Version) ? c : p).ToList();
    }

    /// <summary>
    /// Thunderstore's full list is cached by their CDN and can lag behind a release for a while, while the page of a
    /// single package is current. When Nexus already has a newer version, this asks Thunderstore about that one package,
    /// so the update can still be installed right in the Hub as soon as Thunderstore has it.
    /// </summary>
    public static void CheckPackage(StorePackage pkg, string nexusVersion)
    {
        lock (PackageChecks)
        {
            if (PackageChecks.TryGetValue(pkg.FullName, out var asked) && asked == nexusVersion) return;
            PackageChecks[pkg.FullName] = nexusVersion;
            Asking.Add(pkg.FullName);
        }
        Background("ModHub-Package", () =>
        {
            try
            {
                var json = Http.GetStringAsync($"https://thunderstore.io/api/experimental/package/{pkg.Owner}/{pkg.Name}/").GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                var latest = doc.RootElement.GetProperty("latest");
                if (!Updates.Newer(latest.GetProperty("version_number").GetString(), pkg.Version)) return;
                var fresh = new StorePackage
                {
                    Owner = pkg.Owner, Name = pkg.Name, FullName = pkg.FullName, PageUrl = pkg.PageUrl,
                    Downloads = pkg.Downloads, Created = pkg.Created,
                    Updated = latest.TryGetProperty("date_created", out var dc) && dc.TryGetDateTime(out var d) ? d : pkg.Updated,
                };
                SetVersion(fresh, latest);
                Plugin.Logger.LogInfo($"Mod Hub: Thunderstore's list still says {pkg.FullName} {pkg.Version}, its page says {fresh.Version}.");
                // Swap in a new list (other threads may be reading the old one).
                lock (PackageChecks) Checked[fresh.FullName] = fresh;
                if (Store is { } store) Store = WithChecked(store);
                OnMain(Updates.Refresh);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogDebug($"Thunderstore package {pkg.FullName}: {e.Message}");
                lock (PackageChecks) PackageChecks.Remove(pkg.FullName); // offline: try again at the next refresh
            }
            finally { lock (PackageChecks) Asking.Remove(pkg.FullName); }
        });
    }

    /// <summary>Downloads a Thunderstore package (and its dependencies) and installs it.</summary>
    public static volatile bool LastInstallOk;

    /// <param name="intoFolder">Install into this plugins folder (used for updates, so a mod keeps its folder).</param>
    public static void InstallFromStore(StorePackage pkg, Action<string> done, string intoFolder = null)
    {
        Background("ModHub-Install", () =>
        {
            var installed = new List<string>();
            string error = null;
            void InstallOne(StorePackage p, int depth)
            {
                if (depth > 5) return;
                foreach (var dep in p.Dependencies)
                {
                    // Dependency strings look like "Owner-Name-1.2.3".
                    int last = dep.LastIndexOf('-');
                    string id = last > 0 ? dep.Substring(0, last) : dep;
                    if (id.Contains("BepInExPack") || id.EndsWith("-MelonLoader")) continue; // loaders: Melon Bridge stands in for MelonLoader
                    // Characters list the ORIGINAL character loader as their dependency, and that one stops working after
                    // a game update. Install the newest copy of the loader instead (community fixes ship under a new
                    // package name with the same plugin inside, so the old copy is moved aside by its GUID).
                    if (id.IndexOf("CustomCharacterCreator", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (Characters.LoaderNeedsUpdate(Characters.FindLoader()) && Characters.PreferredLoader() is { } loader) InstallOne(loader, depth + 1);
                        continue;
                    }
                    var depPkg = Store?.FirstOrDefault(x => x.FullName == id);
                    if (depPkg != null && !Catalog.Mods.Any(m => m.Source == "Thunderstore:" + id)) InstallOne(depPkg, depth + 1);
                }
                string tmp = Path.Combine(Path.GetTempPath(), $"ModHub-{p.FullName}-{p.Version}.zip");
                var bytes = Http.GetByteArrayAsync(p.DownloadUrl).GetAwaiter().GetResult();
                File.WriteAllBytes(tmp, bytes);
                var check = Installer.Check(tmp);
                if (check.IsCharacter)
                {
                    // Not a plugin: the character files go into BepInEx\plugins\CustomCharacters.
                    // Its loader came in as a dependency (CustomCharacterCreator), so nothing else to do here.
                    try
                    {
                        string character = Characters.InstallCharacterZip(tmp);
                        installed.Add(character);
                        Plugin.Logger.LogInfo($"Installed custom character {character} (Thunderstore:{p.FullName}).");
                        // A character without the loader dependency in its manifest still needs the loader.
                        if (!p.Dependencies.Any(d => d.IndexOf("CustomCharacterCreator", StringComparison.OrdinalIgnoreCase) >= 0))
                            Characters.EnsureLoader(out _);
                    }
                    catch (Exception e) { error ??= $"{p.DisplayName}: {e.Message}"; }
                    try { File.Delete(tmp); } catch { }
                    return;
                }
                if (!check.Ok) { error ??= $"{p.DisplayName}: {check.Problem}"; return; }
                if (depth == 0 && intoFolder != null) Installer.Retarget(check, intoFolder);
                else if (!check.Files.Exists(f => f.Target.StartsWith(check.Base) && !f.Target.StartsWith($"{check.Base}{check.FolderName}/")))
                    Installer.Retarget(check, p.FullName); // stable folder per package, like r2modman
                if (Installer.Install(tmp, check, "Thunderstore:" + p.FullName, p.Version) != null) installed.Add(p.DisplayName);
                else error ??= p.DisplayName;
                try { File.Delete(tmp); } catch { }
            }
            try { InstallOne(pkg, 0); }
            catch (Exception e) { error = e.Message; }
            OnMain(() =>
            {
                Catalog.Scan();
                Updates.Refresh();
                LastInstallOk = error == null;
                done(error == null
                    ? L.T($"Installed {string.Join(", ", installed)}. Restart the game to use it.", $"تم تثبيت {string.Join("، ", installed)}. أعد تشغيل اللعبة لاستخدامه.")
                    : L.T($"Could not install: {error}", $"تعذر التثبيت: {error}"));
            });
        });
    }

    // ------------------------------------------------------------------ Nexus (public API, no key)

    public static void LoadNexus()
    {
        if (!Edition.Nexus) return; // the Thunderstore build only uses Thunderstore
        if ((Nexus != null && !Old(_nexusAt)) || TooSoon(_nexusRetryAt) || Interlocked.Exchange(ref _nexusLoading, 1) == 1) return;
        Background("ModHub-Nexus", () =>
        {
            try
            {
                var list = new List<NexusMod>();
                for (int offset = 0; offset < 1000; offset += 100)
                {
                    string q = "{\"query\":\"{ mods(filter: { gameId: [{value: \\\"" + MegabonkNexusId + "\\\"}] }, count: 100, offset: " + offset +
                               ") { nodes { modId name version summary author pictureUrl thumbnailUrl downloads createdAt updatedAt adultContent status } } }\"}";
                    using var resp = Http.PostAsync("https://api.nexusmods.com/v2/graphql", new StringContent(q, Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
                    using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    var nodes = doc.RootElement.GetProperty("data").GetProperty("mods").GetProperty("nodes");
                    foreach (var n in nodes.EnumerateArray())
                    {
                        // Adult and hidden/removed mods are left out of the Hub.
                        if (n.TryGetProperty("adultContent", out var adult) && adult.ValueKind == JsonValueKind.True) continue;
                        if (n.TryGetProperty("status", out var st) && st.GetString() != "published") continue;
                        string S(string k) => n.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                        DateTime D(string k) => DateTime.TryParse(S(k), out var d) ? d : DateTime.MinValue;
                        list.Add(new NexusMod
                        {
                            Id = n.GetProperty("modId").GetInt32(), Name = S("name"), Version = S("version"), Summary = S("summary"), Author = S("author"),
                            PictureUrl = S("pictureUrl"), ThumbnailUrl = S("thumbnailUrl"), Created = D("createdAt"), Updated = D("updatedAt"),
                            Downloads = n.TryGetProperty("downloads", out var dl) && dl.ValueKind == JsonValueKind.Number ? dl.GetInt64() : 0,
                        });
                    }
                    if (nodes.GetArrayLength() < 100) break;
                }
                Nexus = list;
                Interlocked.Exchange(ref _nexusAt, DateTime.UtcNow.Ticks);
                OnMain(Updates.Refresh);
            }
            catch (Exception e) { Plugin.Logger.LogDebug($"Nexus list: {e.Message}"); Interlocked.Exchange(ref _nexusRetryAt, In30s); }
            finally { Interlocked.Exchange(ref _nexusLoading, 0); }
        });
    }
}

/// <summary>Works out which installed mods have a newer version on Thunderstore or Nexus.</summary>
internal static class Updates
{
    public static void Refresh()
    {
        foreach (var m in Catalog.Mods)
        {
            if (m.IsCharacter) continue; // characters have no store version to compare
            m.Latest = null; m.LatestUrl = null; m.CanUpdateInHub = false;
            if (m.Duplicate) continue; // the fix for a duplicate is Remove, not Update
            if (m.IsHub && Catalog.WasInstalledThisSession(m.Id)) continue; // already updated: waiting for the restart
            if (m.IsHub && !Edition.SelfUpdate)
            {
                // Thunderstore build: the mod manager updates the Hub, so this only says there's a new one.
                var own = StorePackageFor(m);
                m.LatestUrl = own?.PageUrl;
                if (own != null && Newer(own.Version, m.Version)) m.Latest = own.Version;
                continue;
            }
            if (m.IsHub && Online.Release is { } rel && Newer(rel.Version, m.Version))
            {
                // A newer Mod Hub on GitHub: it downloads and installs right inside the game.
                m.Latest = rel.Version; m.CanUpdateInHub = true;
                m.LatestUrl = $"https://www.nexusmods.com/megabonk/mods/{HubNexusId}?tab=files";
                continue;
            }
            // A mod on both sites gets the newer of the two: either one can be first with a release.
            var pkg = StorePackageFor(m);
            var nexus = NexusFor(m, pkg);
            string have = HaveVersion(m); // trust the package version we installed: some authors bump it without changing the mod's own version
            string nexusUrl = nexus != null ? $"https://www.nexusmods.com/megabonk/mods/{nexus.Id}?tab=files" : null;
            m.LatestUrl = pkg?.PageUrl ?? nexusUrl;
            if (nexus != null && Newer(nexus.Version, have) && (pkg == null || Newer(nexus.Version, pkg.Version)))
            {
                // Nexus is ahead of Thunderstore's (cached) list: point at Nexus, and ask Thunderstore about this package,
                // which turns it into an in-Hub update as soon as Thunderstore has that version too.
                m.Latest = nexus.Version; m.LatestUrl = nexusUrl;
                if (pkg != null) Online.CheckPackage(pkg, nexus.Version);
            }
            else if (pkg != null && Newer(pkg.Version, have))
            {
                m.Latest = pkg.Version; m.CanUpdateInHub = !pkg.NeedsMelon || Melon.BridgeInstalled;
            }
        }
    }

    /// <summary>
    /// The Nexus page of an installed mod. The Hub's is its own page; a Thunderstore package that names its Nexus page
    /// gets exactly that one; otherwise the same name, and when there is a Thunderstore package also the same author,
    /// so someone else's mod with the same name never looks like an update.
    /// </summary>
    public static NexusMod NexusFor(Mod m, StorePackage pkg)
    {
        var nexus = Online.Nexus;
        if (nexus == null) return null;
        if (m.IsHub) return nexus.FirstOrDefault(n => n.Id == HubNexusId);
        if (pkg?.NexusLink > 0) return nexus.FirstOrDefault(n => n.Id == pkg.NexusLink);
        return nexus.FirstOrDefault(n => Same(n.Name, m.Name) && (pkg == null || Key(n.Author) == Key(pkg.Owner)));
    }

    /// <summary>The version we have: the Thunderstore package version we installed if it's newer than the mod's own.</summary>
    public static string HaveVersion(Mod m) => m.PackageVersion != null && Newer(m.PackageVersion, m.Version) ? m.PackageVersion : m.Version;

    public const string HubPackage = "AhmedHus21-ModHub";
    public const int HubNexusId = 194; // https://www.nexusmods.com/megabonk/mods/194

    /// <summary>The Thunderstore package of an installed mod: where it came from, else the same name.</summary>
    public static StorePackage StorePackageFor(Mod m)
    {
        var store = Online.Store;
        if (store == null) return null;
        if (m.IsHub) return store.FirstOrDefault(p => p.FullName == HubPackage); // never someone else's "ModHub"
        if (m.Source != null && m.Source.StartsWith("Thunderstore:"))
            return store.FirstOrDefault(p => "Thunderstore:" + p.FullName == m.Source);
        return store.FirstOrDefault(p => Same(p.Name, m.Name));
    }

    private static string Key(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static bool Same(string a, string b) { var x = Key(a); return x.Length > 2 && x == Key(b); }

    public static bool Newer(string latest, string current)
    {
        static Version Parse(string v)
        {
            v = (v ?? "").Trim().TrimStart('v', 'V');
            int cut = v.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut > 0) v = v.Substring(0, cut);
            if (!v.Contains('.')) v += ".0";
            return System.Version.TryParse(v, out var p) ? p : null;
        }
        var a = Parse(latest); var b = Parse(current);
        return a != null && b != null && a > b;
    }
}

/// <summary>Zips and DLLs the player can install: the "Drop mods here" folder and recent downloads.</summary>
internal static class LocalFiles
{
    public static string DropDir => Path.Combine(BepInEx.Paths.GameRootPath, "Mod Hub - drop mods here");
    private static string DownloadsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public sealed class Candidate { public string Path; public InstallCheck Check; public DateTime When; }

    public static volatile List<Candidate> Downloads = new();
    private static int _scanning;

    public static void EnsureDropDir()
    {
        if (Edition.Thunderstore) return; // no drop folder in the Thunderstore build
        try
        {
            Directory.CreateDirectory(DropDir);
            string help = Path.Combine(DropDir, "README - how to use.txt");
            File.WriteAllText(help,
                "Drop a mod's .zip or .dll file into this folder.\r\n" +
                "Custom characters and skins work too: drop their zip, or their .custom.json file together with its model file.\r\n" +
                "Open Mod Hub (F4 in game): it installs them automatically and moves the files to the \"installed\" folder.\r\n\r\n" +
                "ضع ملف المود (.zip أو .dll) في هذا المجلد. الشخصيات والأشكال المخصصة كمان: ضع ملف الـ zip أو ملف .custom.json مع ملف الموديل.\r\n" +
                "افتح الـ Mod Hub (F4 في اللعبة) ويثبتها تلقائيا.\r\n");
        }
        catch (Exception e) { Plugin.Logger.LogDebug($"Drop folder: {e.Message}"); }
    }

    /// <summary>Installs whatever was dropped into the drop folder. Returns messages for the panel.</summary>
    public static List<string> ProcessDropFolder()
    {
        var messages = new List<string>();
        if (Edition.Thunderstore || !Directory.Exists(DropDir)) return messages;

        // Custom characters / skins: "X.custom.json" + its bundle, loose or in a zip without a mod DLL.
        var looseJsons = Directory.GetFiles(DropDir).Where(Characters.IsCharacterJson).ToList();
        var characterZips = Directory.GetFiles(DropDir, "*.zip").Where(z => ReadyToRead(z) && Characters.IsCharacterZip(z)).ToList();
        if (looseJsons.Count + characterZips.Count > 0)
        {
            string loaderDir = Characters.EnsureLoader(out string note);
            if (note != null) messages.Add(note);
            if (loaderDir != null)
            {
                foreach (var json in looseJsons)
                {
                    var set = Characters.LooseSet(json);
                    if (!set.All(ReadyToRead)) continue;
                    try
                    {
                        string name = Characters.InstallLoose(set, Characters.Dir);
                        messages.Add(L.T($"Installed the character {name}. Restart the game to play it.", $"تم تثبيت الشخصية {name}. أعد تشغيل اللعبة.") + (Characters.Warning() ?? ""));
                        foreach (var f in set) MoveTo(f, "installed");
                    }
                    catch (Exception e) { messages.Add(L.T($"{Path.GetFileName(json)}: {e.Message}", $"{Path.GetFileName(json)}: {e.Message}")); }
                }
                foreach (var zip in characterZips)
                {
                    try
                    {
                        string name = Characters.InstallZip(zip, Characters.Dir);
                        messages.Add(L.T($"Installed the character {name}. Restart the game to play it.", $"تم تثبيت الشخصية {name}. أعد تشغيل اللعبة.") + (Characters.Warning() ?? ""));
                        MoveTo(zip, "installed");
                    }
                    catch (Exception e) { messages.Add(L.T($"{Path.GetFileName(zip)}: {e.Message}", $"{Path.GetFileName(zip)}: {e.Message}")); }
                }
            }
        }

        foreach (var file in Directory.GetFiles(DropDir).Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            if (characterZips.Contains(file)) continue; // handled above (or waiting for the character loader)
            if (!ReadyToRead(file)) continue; // still being copied
            var check = Installer.Check(file);
            string sub = check.Ok && Installer.Install(file, check, "Drop folder") != null ? "installed" : "not installed";
            messages.Add(check.Ok && sub == "installed"
                ? L.T($"Installed {check.ModName} from the drop folder. Restart the game to use it.", $"تم تثبيت {check.ModName} من مجلد الإسقاط. أعد تشغيل اللعبة.")
                : L.T($"{Path.GetFileName(file)}: {check.Problem}", $"{Path.GetFileName(file)}: {check.Problem}"));
            try
            {
                string dest = Path.Combine(DropDir, sub);
                Directory.CreateDirectory(dest);
                string moved = Path.Combine(dest, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Path.GetFileName(file)}");
                File.Move(file, moved);
                if (sub == "not installed") File.WriteAllText(moved + " - why.txt", check.Problem ?? @"Install failed, see BepInEx\LogOutput.log");
            }
            catch { }
        }
        return messages;
    }

    /// <summary>Moves a handled drop-folder file into its "installed" / "not installed" subfolder.</summary>
    private static void MoveTo(string file, string sub)
    {
        try
        {
            string dest = Path.Combine(DropDir, sub);
            Directory.CreateDirectory(dest);
            File.Move(file, Path.Combine(dest, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Path.GetFileName(file)}"));
        }
        catch { }
    }

    private static bool ReadyToRead(string file)
    {
        try { using var _ = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
        catch { return false; }
    }

    /// <summary>Sends a zip found in Downloads to the Recycle Bin (only files inside Downloads) and drops it from the list.</summary>
    public static bool RecycleDownload(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(Path.GetFullPath(DownloadsDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return false;
            // Silent: no confirmation or error dialog may ever open (it would hide behind the fullscreen game and freeze it).
            var op = new ShFileOp { wFunc = 3 /* FO_DELETE */, pFrom = full + "\0\0", fFlags = 0x0040 | 0x0010 | 0x0004 | 0x0400 };
            // FOF_ALLOWUNDO (Recycle Bin) | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
            if (SHFileOperation(ref op) != 0 || op.fAnyOperationsAborted || File.Exists(full)) return false;
            Downloads = Downloads.Where(c => !c.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).ToList();
            Plugin.Logger.LogInfo($"Moved {full} to the Recycle Bin.");
            return true;
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"Could not recycle {path}: {e.Message}"); return false; }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct ShFileOp
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOp op);

    /// <summary>Looks through Downloads (last 14 days) for zips that contain a BepInEx mod.</summary>
    public static void ScanDownloads()
    {
        if (Edition.Thunderstore) return; // the Thunderstore build installs from Thunderstore only (zips in Downloads are usually from Nexus)
        if (Interlocked.Exchange(ref _scanning, 1) == 1) return;
        new Thread(() =>
        {
            try
            {
                var list = new List<Candidate>();
                if (Directory.Exists(DownloadsDir))
                    foreach (var f in new DirectoryInfo(DownloadsDir).GetFiles("*.zip")
                                 .Where(f => f.LastWriteTime > DateTime.Now.AddDays(-14) && f.Length < 200L * 1024 * 1024)
                                 .OrderByDescending(f => f.LastWriteTime).Take(30))
                    {
                        var check = Installer.Check(f.FullName);
                        // Character packs are worth showing unless that character is already installed.
                        bool offered = check.IsCharacter ? !Characters.IsInstalled(check.ModName) : check.Ok || check.Loader == Loader.MelonLoader;
                        if (offered)
                            list.Add(new Candidate { Path = f.FullName, Check = check, When = f.LastWriteTime });
                    }
                Downloads = list;
            }
            catch (Exception e) { Plugin.Logger.LogDebug($"Downloads scan: {e.Message}"); }
            finally { Interlocked.Exchange(ref _scanning, 0); }
        }) { IsBackground = true, Name = "ModHub-Downloads" }.Start();
    }
}
