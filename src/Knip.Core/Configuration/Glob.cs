using System.Text;
using System.Text.RegularExpressions;

namespace Knip.Core.Configuration;

/// <summary>
/// Minimal glob matcher supporting <c>**</c> (any path segments), <c>*</c> (any chars except
/// the separator), and <c>?</c> (single char). Matching is case-insensitive and separator-agnostic
/// ('\' is normalized to '/') so the same patterns work for file paths and dotted symbol names.
/// </summary>
public static class Glob
{
    private static readonly Dictionary<string, Regex> Cache = new();

    public static bool IsMatch(string input, string pattern)
    {
        var regex = Compile(pattern);
        return regex.IsMatch(Normalize(input));
    }

    public static bool IsMatchAny(string input, IEnumerable<string> patterns)
    {
        var normalized = Normalize(input);
        foreach (var pattern in patterns)
            if (Compile(pattern).IsMatch(normalized))
                return true;
        return false;
    }

    private static string Normalize(string value) => value.Replace('\\', '/');

    private static Regex Compile(string pattern)
    {
        if (Cache.TryGetValue(pattern, out var cached))
            return cached;

        var regex = new Regex(Translate(Normalize(pattern)),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        Cache[pattern] = regex;
        return regex;
    }

    private static string Translate(string pattern)
    {
        var sb = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                    {
                        // "**" -> any characters including the separator (any depth, files included)
                        i++;
                        sb.Append(".*");
                    }
                    else
                    {
                        // "*" -> any characters within a single path/name segment
                        sb.Append("[^/]*");
                    }
                    break;
                case '?':
                    sb.Append("[^/]");
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        sb.Append('$');
        return sb.ToString();
    }
}
