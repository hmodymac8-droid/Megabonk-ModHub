using System.Collections.Generic;
using System.Text;

namespace ModHub;

/// <summary>
/// Prepares Arabic text for TextMeshPro, which can lay text out right-to-left but cannot join letters.
/// 1. Each letter is replaced by its contextual presentation form (isolated / final / initial / medial),
///    with lam-alef ligatures, so the letters connect.
/// 2. Runs of left-to-right text (numbers, English names) are pre-reversed, because TMP's right-to-left
///    mode lays out every character right-to-left; this makes them read correctly again.
/// 3. Brackets in Arabic context are mirrored.
/// Rich-text tags such as &lt;color=#fff&gt; are left untouched.
/// </summary>
internal static partial class Rtl
{
    // isolated, final, initial, medial ('\0' = the letter has no such form)
    private static readonly Dictionary<char, char[]> Forms = new()
    {
        ['ء'] = new[] { 'ﺀ', '\0', '\0', '\0' },           // ء
        ['آ'] = new[] { 'ﺁ', 'ﺂ', '\0', '\0' },       // آ
        ['أ'] = new[] { 'ﺃ', 'ﺄ', '\0', '\0' },       // أ
        ['ؤ'] = new[] { 'ﺅ', 'ﺆ', '\0', '\0' },       // ؤ
        ['إ'] = new[] { 'ﺇ', 'ﺈ', '\0', '\0' },       // إ
        ['ئ'] = new[] { 'ﺉ', 'ﺊ', 'ﺋ', 'ﺌ' }, // ئ
        ['ا'] = new[] { 'ﺍ', 'ﺎ', '\0', '\0' },       // ا
        ['ب'] = new[] { 'ﺏ', 'ﺐ', 'ﺑ', 'ﺒ' }, // ب
        ['ة'] = new[] { 'ﺓ', 'ﺔ', '\0', '\0' },       // ة
        ['ت'] = new[] { 'ﺕ', 'ﺖ', 'ﺗ', 'ﺘ' }, // ت
        ['ث'] = new[] { 'ﺙ', 'ﺚ', 'ﺛ', 'ﺜ' }, // ث
        ['ج'] = new[] { 'ﺝ', 'ﺞ', 'ﺟ', 'ﺠ' }, // ج
        ['ح'] = new[] { 'ﺡ', 'ﺢ', 'ﺣ', 'ﺤ' }, // ح
        ['خ'] = new[] { 'ﺥ', 'ﺦ', 'ﺧ', 'ﺨ' }, // خ
        ['د'] = new[] { 'ﺩ', 'ﺪ', '\0', '\0' },       // د
        ['ذ'] = new[] { 'ﺫ', 'ﺬ', '\0', '\0' },       // ذ
        ['ر'] = new[] { 'ﺭ', 'ﺮ', '\0', '\0' },       // ر
        ['ز'] = new[] { 'ﺯ', 'ﺰ', '\0', '\0' },       // ز
        ['س'] = new[] { 'ﺱ', 'ﺲ', 'ﺳ', 'ﺴ' }, // س
        ['ش'] = new[] { 'ﺵ', 'ﺶ', 'ﺷ', 'ﺸ' }, // ش
        ['ص'] = new[] { 'ﺹ', 'ﺺ', 'ﺻ', 'ﺼ' }, // ص
        ['ض'] = new[] { 'ﺽ', 'ﺾ', 'ﺿ', 'ﻀ' }, // ض
        ['ط'] = new[] { 'ﻁ', 'ﻂ', 'ﻃ', 'ﻄ' }, // ط
        ['ظ'] = new[] { 'ﻅ', 'ﻆ', 'ﻇ', 'ﻈ' }, // ظ
        ['ع'] = new[] { 'ﻉ', 'ﻊ', 'ﻋ', 'ﻌ' }, // ع
        ['غ'] = new[] { 'ﻍ', 'ﻎ', 'ﻏ', 'ﻐ' }, // غ
        ['ـ'] = new[] { 'ـ', 'ـ', 'ـ', 'ـ' }, // ـ tatweel
        ['ف'] = new[] { 'ﻑ', 'ﻒ', 'ﻓ', 'ﻔ' }, // ف
        ['ق'] = new[] { 'ﻕ', 'ﻖ', 'ﻗ', 'ﻘ' }, // ق
        ['ك'] = new[] { 'ﻙ', 'ﻚ', 'ﻛ', 'ﻜ' }, // ك
        ['ل'] = new[] { 'ﻝ', 'ﻞ', 'ﻟ', 'ﻠ' }, // ل
        ['م'] = new[] { 'ﻡ', 'ﻢ', 'ﻣ', 'ﻤ' }, // م
        ['ن'] = new[] { 'ﻥ', 'ﻦ', 'ﻧ', 'ﻨ' }, // ن
        ['ه'] = new[] { 'ﻩ', 'ﻪ', 'ﻫ', 'ﻬ' }, // ه
        ['و'] = new[] { 'ﻭ', 'ﻮ', '\0', '\0' },       // و
        ['ى'] = new[] { 'ﻯ', 'ﻰ', '\0', '\0' },       // ى
        ['ي'] = new[] { 'ﻱ', 'ﻲ', 'ﻳ', 'ﻴ' }, // ي
    };

    // lam + alef variant -> (isolated, final) ligature
    private static readonly Dictionary<char, (char Isolated, char Final)> LamAlef = new()
    {
        ['آ'] = ('ﻵ', 'ﻶ'),
        ['أ'] = ('ﻷ', 'ﻸ'),
        ['إ'] = ('ﻹ', 'ﻺ'),
        ['ا'] = ('ﻻ', 'ﻼ'),
    };

    private const char Lam = 'ل';

    /// <summary>True when the text has Arabic letters that still need shaping.</summary>
    public static bool NeedsShaping(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
            if (c >= 'ء' && c <= 'ي') return true;
        return false;
    }

    /// <summary>True when the text contains Arabic in either raw or already-shaped form.</summary>
    public static bool ContainsArabic(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
            if ((c >= '؀' && c <= 'ۿ') || (c >= 'ﹰ' && c <= '﻿')) return true;
        return false;
    }

    public static string Shape(string s)
    {
        bool[] tag = MarkTags(s);
        var shaped = JoinLetters(s, tag);
        return FixDirection(shaped, MarkTags(shaped));
    }

    // ---------------------------------------------------------------- letter joining

    private static string JoinLetters(string s, bool[] tag)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (tag[i] || !Forms.TryGetValue(c, out var f)) { sb.Append(c); continue; }

            int prevIdx = Neighbour(s, tag, i, -1);
            int nextIdx = Neighbour(s, tag, i, +1);
            bool prevJoins = prevIdx >= 0 && JoinsForward(s[prevIdx]);

            // lam-alef ligature (the alef is consumed)
            if (c == Lam && nextIdx >= 0 && LamAlef.TryGetValue(s[nextIdx], out var lig) && OnlyTransparentBetween(s, tag, i, nextIdx))
            {
                sb.Append(prevJoins ? lig.Final : lig.Isolated);
                for (int k = i + 1; k < nextIdx; k++) sb.Append(s[k]); // keep any marks/tags in between
                i = nextIdx;
                continue;
            }

            bool nextJoins = nextIdx >= 0 && JoinsBackward(s[nextIdx]);
            bool joinPrev = prevJoins && f[1] != '\0';
            bool joinNext = nextJoins && f[2] != '\0';

            char form = joinPrev && joinNext && f[3] != '\0' ? f[3]
                : joinPrev ? f[1]
                : joinNext ? f[2]
                : f[0];
            sb.Append(form);
        }
        return sb.ToString();
    }

    private static bool JoinsForward(char c) => Forms.TryGetValue(c, out var f) && f[2] != '\0';
    private static bool JoinsBackward(char c) => Forms.TryGetValue(c, out var f) && f[1] != '\0';
    private static bool IsTransparent(char c) => (c >= 'ً' && c <= 'ٟ') || c == 'ٰ';

    private static int Neighbour(string s, bool[] tag, int i, int step)
    {
        for (int j = i + step; j >= 0 && j < s.Length; j += step)
        {
            if (tag[j] || IsTransparent(s[j])) continue;
            return j;
        }
        return -1;
    }

    private static bool OnlyTransparentBetween(string s, bool[] tag, int a, int b)
    {
        for (int k = a + 1; k < b; k++)
            if (!tag[k] && !IsTransparent(s[k])) return false;
        return true;
    }

    // ---------------------------------------------------------------- direction

    private static string FixDirection(string s, bool[] tag)
    {
        var chars = s.ToCharArray();
        int i = 0;
        while (i < chars.Length)
        {
            if (tag[i]) { i++; continue; }

            if (IsRunChar(chars, i))
            {
                // Grow the run over neutrals only while more left-to-right text follows.
                int end = i, j = i + 1;
                while (j < chars.Length && !tag[j] && !IsRtl(chars[j]) && chars[j] != '\n')
                {
                    if (IsRunChar(chars, j)) end = j;
                    j++;
                }
                System.Array.Reverse(chars, i, end - i + 1);
                i = end + 1;
                continue;
            }

            chars[i] = Mirror(chars[i]);
            i++;
        }
        return new string(chars);
    }

    // Signs and % stuck to a number belong to it ("-69%", "+1%"), so they move with the number.
    private static bool IsNumberAffix(char c) => c is '%' or '+' or '-' or '٪';

    private static bool IsRunChar(char[] chars, int i)
    {
        char c = chars[i];
        if (IsLtrStrong(c)) return true;
        if (!IsNumberAffix(c)) return false;
        bool digitBefore = i > 0 && chars[i - 1] >= '0' && chars[i - 1] <= '9';
        bool digitAfter = i + 1 < chars.Length && chars[i + 1] >= '0' && chars[i + 1] <= '9';
        return digitBefore || digitAfter;
    }

    private static bool IsRtl(char c) => (c >= '؀' && c <= 'ۿ') || (c >= 'ﹰ' && c <= '﻿');

    private static bool IsLtrStrong(char c) =>
        (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= 'À' && c <= 'ɏ');

    private static char Mirror(char c) => c switch
    {
        '(' => ')', ')' => '(',
        '[' => ']', ']' => '[',
        '{' => '}', '}' => '{',
        '«' => '»', '»' => '«',
        _ => c,
    };

    // ---------------------------------------------------------------- rich text tags

    private static bool[] MarkTags(string s)
    {
        var tag = new bool[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '<' || i + 1 >= s.Length) continue;
            char n = s[i + 1];
            if (!(char.IsLetter(n) && n < 128) && n != '/' && n != '#') continue;
            int close = s.IndexOf('>', i + 1);
            if (close < 0 || close - i > 80) continue;
            if (s.IndexOf('<', i + 1, close - i - 1) >= 0) continue;
            for (int k = i; k <= close; k++) tag[k] = true;
            i = close;
        }
        return tag;
    }
}
