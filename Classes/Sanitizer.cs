using System.Text.RegularExpressions;

namespace Gallium2021.Classes;

public static class Sanitizer
{
    private static readonly string[] BannedWords =
    {
        "fuck", "shit", "cum", "cunt", "bitch", "asshole", "nigger", "nigga", "faggot", "wanker", "nig", "butt",
        "booty", "tits", "titty", "sex", "balls", "ballsack", "asshat", "dickhead", "douchebag", "jackass", "kike",
        "chink", "spic", "tranny", "troon", "dyke", "retard", "retarded", "suicide", "doxx", "dox", "rape", "molest",
        "groom", "semen", "coon", "dick", "dickface", "vagina", "penis", "anal", "horny", "masturbate", "orgasm",
        "penetration", "penetrate", "ejaculation", "ejaculate", "hoe", "ass", "whore", "bootysex", "sexpenis",
        "negro", "diddy", "wanking", "fucking", "fucker", "wank", "67",
    };

    private static readonly Dictionary<char, string> LeetMap = new()
    {
        ['a'] = "a4@", ['b'] = "b8", ['c'] = "c(", ['e'] = "e3", ['g'] = "g69", ['i'] = "i1!|",
        ['l'] = "l1|", ['o'] = "o0", ['s'] = "s5$", ['t'] = "t7+", ['u'] = "u4",
    };

    private const string Sep = @"[\s\W_]*";
    private const string Suffix = @"(?:[\s\W_]*(?:ing|ting|ers?|iest|ier|est|ed|s))?";

    public static readonly Regex BannedWordsRegex = Build();

    private static Regex Build()
    {
        var parts = new List<string>();
        foreach (var word in BannedWords)
        {
            var letterPatterns = new List<string>();
            foreach (var ch in word.ToLowerInvariant())
            {
                var variants = LeetMap.GetValueOrDefault(ch, ch.ToString());
                letterPatterns.Add($"[{EscapeForCharClass(variants)}]+");
            }
            parts.Add(string.Join(Sep, letterPatterns) + Suffix);
        }
        var pattern = @"(?<![A-Za-z])(?:" + string.Join("|", parts) + @")(?![A-Za-z])";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private static string EscapeForCharClass(string chars) =>
        chars.Replace("\\", "\\\\").Replace("]", "\\]").Replace("^", "\\^").Replace("-", "\\-");

    public static string SanitizeText(string text) => BannedWordsRegex.Replace(text, m => new string('*', m.Value.Length));

    public static bool IsPure(string text) => !BannedWordsRegex.IsMatch(text);
}
