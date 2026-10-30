using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ModHub;

/// <summary>
/// Arabic for Unity's immediate-mode GUI, which only draws left-to-right and never joins letters.
/// Text is shaped (see RtlShaper.cs), wrapped by us in reading order, and each line is reversed into
/// the visual order the GUI then draws as-is.
/// </summary>
internal static partial class Rtl
{
    private static readonly Dictionary<(string, int, int, int), string> Cache = new();

    public static bool HasArabic(string s) => NeedsShaping(s);

    /// <summary>Visual-order text for the GUI. Wraps to <paramref name="width"/> when the style wraps.</summary>
    public static string Visual(string text, GUIStyle style, float width)
    {
        if (!HasArabic(text)) return text;
        bool wrap = style.wordWrap && width > 1f;
        var key = (text, wrap ? (int)width : 0, style.fontSize, (int)style.fontStyle);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        // Shape once in reading order, then lay out words into lines.
        string shaped = Shape(text);
        var lines = new List<string>();
        foreach (var paragraph in shaped.Split('\n'))
        {
            if (!wrap) { lines.Add(paragraph); continue; }

            var nowrap = new GUIStyle(style) { wordWrap = false };
            var words = paragraph.Split(' ');
            var line = new StringBuilder();
            foreach (var word in words)
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && nowrap.CalcSize(new GUIContent(Reverse(candidate))).x > width)
                {
                    lines.Add(line.ToString());
                    line.Clear().Append(word);
                }
                else
                {
                    line.Clear().Append(candidate);
                }
            }
            lines.Add(line.ToString());
        }

        var sb = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(Reverse(lines[i]));
        }

        if (Cache.Count > 2048) Cache.Clear();
        return Cache[key] = sb.ToString();
    }

    /// <summary>
    /// Shape() already reversed left-to-right runs and mirrored brackets for right-to-left layout,
    /// so reversing the whole line yields left-to-right visual order with numbers/English intact.
    /// Rich-text tags are kept as units.
    /// </summary>
    private static string Reverse(string s)
    {
        var tokens = new List<string>();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '<')
            {
                int close = s.IndexOf('>', i + 1);
                if (close > i && close - i < 80) { tokens.Add(s.Substring(i, close - i + 1)); i = close; continue; }
            }
            tokens.Add(s[i].ToString());
        }
        tokens.Reverse();
        // Tags were reversed too: swap each opening/closing pair back by re-pairing them in order.
        var sb = new StringBuilder(s.Length);
        foreach (var t in tokens) sb.Append(t.Length > 1 && t.StartsWith("<") ? FlipTag(t) : t);
        return sb.ToString();
    }

    private static string FlipTag(string tag)
    {
        // After reversal, closing tags come first. Turn "</b>" into "<b>" and the original opener into "</b>".
        // Tags with values (e.g. <color=#fff>) can't be rebuilt from their closer, so drop colour/size tags here.
        bool closing = tag.StartsWith("</");
        int start = closing ? 2 : 1;
        int end = tag.IndexOfAny(new[] { '=', ' ', '>' }, start);
        string name = tag.Substring(start, end - start);
        if (name is not ("b" or "i")) return "";
        return closing ? "<" + name + ">" : "</" + name + ">";
    }
}
