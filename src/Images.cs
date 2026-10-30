using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using BepInEx;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ModHub;

/// <summary>
/// Mod icons and README screenshots. Downloads happen on background threads (at most 4 at once) and are
/// cached in BepInEx\cache\ModHub; textures are made on the game thread the first time they are drawn.
/// Anything the game can't decode (SVG badges, GIFs, broken links) is simply skipped.
/// </summary>
internal static class Images
{
    private const int MaxBytes = 6 * 1024 * 1024;
    private static readonly ConcurrentDictionary<string, byte[]> Downloaded = new(); // waiting for the game thread
    private static readonly ConcurrentDictionary<string, bool> Requested = new();
    private static readonly Dictionary<string, Texture2D> Textures = new();
    private static readonly HashSet<string> Failed = new();
    private static readonly Dictionary<string, int> Tries = new();
    private static readonly SemaphoreSlim Slots = new(4);
    private static string CacheDir => Path.Combine(Paths.CachePath, "ModHub", "images");

    /// <summary>The texture for this URL, or null while it loads (or if it can't be shown). Game thread only.</summary>
    public static Texture2D Get(string url)
    {
        if (string.IsNullOrEmpty(url) || Failed.Contains(url)) return null;
        if (Textures.TryGetValue(url, out var tex)) return tex;
        if (Pixels.TryRemove(url, out var px))
        {
            tex = FromPixels(px.Rgba, px.W, px.H);
            if (tex == null) { Failed.Add(url); return null; }
            Textures[url] = tex;
            return tex;
        }
        if (Downloaded.TryRemove(url, out var data))
        {
            tex = Decode(data, out bool retry);
            if (tex == null)
            {
                Tries.TryGetValue(url, out int n);
                if (retry && n < 3) { Tries[url] = n + 1; Downloaded[url] = data; } // try again next frame
                else Failed.Add(url);
                return null;
            }
            Textures[url] = tex;
            return tex;
        }
        if (Requested.TryAdd(url, true)) Fetch(url);
        return null;
    }

    /// <summary>True once we know this image can't be shown (so the page can stop leaving room for it).</summary>
    public static bool IsFailed(string url) => string.IsNullOrEmpty(url) || Failed.Contains(url);

    private static void Fetch(string url) => Online.Background("ModHub-Image", () =>
    {
        byte[] data = Array.Empty<byte>();
        string file = Path.Combine(CacheDir, Hash(url));
        try
        {
            if (File.Exists(file)) data = File.ReadAllBytes(file);
            else
            {
                Slots.Wait();
                try { data = Online.Http.GetByteArrayAsync(url).GetAwaiter().GetResult(); }
                finally { Slots.Release(); }
                if (data.Length > MaxBytes) data = Array.Empty<byte>();
                else { Directory.CreateDirectory(CacheDir); File.WriteAllBytes(file, data); }
            }
        }
        catch { data = Array.Empty<byte>(); }
        if (WebP.IsWebP(data))
        {
            // Nexus pictures: decode here with Windows' WebP codec, hand plain pixels to the game thread.
            var rgba = WebP.Decode(file, out int w, out int h);
            if (rgba != null) { Pixels[url] = (rgba, w, h); return; }
            data = Array.Empty<byte>();
        }
        Downloaded[url] = data;
    });

    private static readonly ConcurrentDictionary<string, (byte[] Rgba, int W, int H)> Pixels = new();

    /// <summary>A texture from raw RGBA pixels (bottom row first).</summary>
    private static Texture2D FromPixels(byte[] rgba, int w, int h)
    {
        try
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            IL2CPP.il2cpp_gchandle_new(tex.Pointer, false); // keep it for the whole session
            var pin = GCHandle.Alloc(rgba, GCHandleType.Pinned);
            try { tex.LoadRawTextureData(pin.AddrOfPinnedObject(), rgba.Length); }
            finally { pin.Free(); }
            tex.Apply(false, true);
            return tex;
        }
        catch (Exception e) { if (!_loggedTrace) { _loggedTrace = true; Plugin.Logger.LogWarning("Mod Hub image: " + e); } return null; }
    }

    // Unity's own PNG/JPG loader, called directly. The C# LoadImage wrapper in this game builds a temporary Span
    // object that the game's garbage collector can free mid-call, so every image failed through it.
    [StructLayout(LayoutKind.Sequential)]
    private struct ManagedSpanWrapper { public IntPtr Begin; public int Length; }

    [return: MarshalAs(UnmanagedType.U1)]
    private delegate bool LoadImageICall(IntPtr tex, ref ManagedSpanWrapper data, [MarshalAs(UnmanagedType.U1)] bool markNonReadable);
    private static LoadImageICall _loadImage;

    private static bool _loggedTrace;

    private static Texture2D Decode(byte[] data, out bool retry)
    {
        retry = false;
        if (data == null || data.Length < 8 || data[0] == (byte)'<') return null; // empty, SVG or an HTML error page
        try
        {
            _loadImage ??= IL2CPP.ResolveICall<LoadImageICall>("UnityEngine.ImageConversion::LoadImage_Injected");
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            IntPtr texHandle = IL2CPP.il2cpp_gchandle_new(tex.Pointer, false); // keep the texture for the whole session
            var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            bool ok;
            try
            {
                var span = new ManagedSpanWrapper { Begin = pin.AddrOfPinnedObject(), Length = data.Length };
                ok = _loadImage(tex.m_CachedPtr, ref span, false);
            }
            finally { pin.Free(); }
            if (ok && tex.width > 2) return tex;
            IL2CPP.il2cpp_gchandle_free(texHandle);
            UnityEngine.Object.Destroy(tex);
        }
        catch (Exception e) { if (!_loggedTrace) { _loggedTrace = true; Plugin.Logger.LogWarning("Mod Hub image: " + e); } }
        return null;
    }

    private static string Hash(string s)
    {
        using var sha = SHA1.Create();
        var sb = new StringBuilder();
        foreach (var b in sha.ComputeHash(Encoding.UTF8.GetBytes(s))) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}

/// <summary>The full description (README) of Thunderstore mods, fetched when a mod's details page opens.</summary>
internal static class Readmes
{
    private static readonly ConcurrentDictionary<string, string> Texts = new();   // "" = couldn't load
    private static readonly ConcurrentDictionary<string, bool> Requested = new();

    /// <summary>The README markdown, or null while it loads.</summary>
    public static string Get(StorePackage p)
    {
        string key = p.FullName + "@" + p.Version;
        if (Texts.TryGetValue(key, out var md)) return md;
        if (Requested.TryAdd(key, true))
            Online.Background("ModHub-Readme", () =>
            {
                string text = "";
                try
                {
                    if (p.IsNexus)
                    {
                        string q = "{\"query\":\"{ legacyMods(ids: [{gameId: 7674, modId: " + p.NexusId + "}]) { nodes { description } } }\"}";
                        using var resp = Online.Http.PostAsync("https://api.nexusmods.com/v2/graphql",
                            new System.Net.Http.StringContent(q, Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
                        using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                        var nodes = doc.RootElement.GetProperty("data").GetProperty("legacyMods").GetProperty("nodes");
                        if (nodes.GetArrayLength() > 0) text = BBCodeToMarkdown(nodes[0].GetProperty("description").GetString() ?? "");
                        if (p.PictureUrl != null) text = $"![]({p.PictureUrl})\n\n" + text; // the mod's main picture first
                    }
                    else
                    {
                        var json = Online.Http.GetStringAsync(
                            $"https://thunderstore.io/api/experimental/package/{p.Owner}/{p.Name}/{p.Version}/readme/").GetAwaiter().GetResult();
                        using var doc = JsonDocument.Parse(json);
                        text = doc.RootElement.GetProperty("markdown").GetString() ?? "";
                    }
                }
                catch { /* offline or no description: the page shows the short summary */ }
                Texts[key] = text;
            });
        return null;
    }

    /// <summary>Nexus descriptions are BBCode; turn them into the simple markdown the details page shows.</summary>
    internal static string BBCodeToMarkdown(string s)
    {
        string R(string input, string pattern, string replacement) =>
            System.Text.RegularExpressions.Regex.Replace(input, pattern, replacement,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        s = R(s, @"<br\s*/?>", "\n");
        s = R(s, @"[‪-‮]", "");                                 // bidi marks used on Nexus pages
        s = R(s, @"\[img[^\]]*\]\s*(.*?)\s*\[/img\]", "\n![]($1)\n");
        s = R(s, @"\[url=([^\]]*)\](.*?)\[/url\]", "$2");
        s = R(s, @"\[size=[4-7]\](.*?)\[/size\]", "\n## $1\n");          // big text = heading
        s = R(s, @"\[\*\]", "\n- ");
        s = R(s, @"\[/?(b|i|u|s|center|right|left|size[^\]]*|color[^\]]*|font[^\]]*|list[^\]]*|quote[^\]]*|spoiler[^\]]*|line|heading[^\]]*|youtube)\]", "");
        s = R(s, @"&nbsp;", " ");
        return s;
    }
}
