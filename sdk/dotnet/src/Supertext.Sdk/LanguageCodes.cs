using System.Text.RegularExpressions;

namespace Supertext.Sdk;

/// <summary>
/// Language codes for the Supertext AI file API. Supertext wants the source as a bare language
/// (<c>en</c>) and the target as a regional code (<c>de-DE</c>, <c>en-US</c>): a bare target or a
/// regional source is rejected with <c>INVALID_LANGUAGE_PAIR</c>. <see cref="SupertextClient"/>
/// applies <see cref="Source"/> and <see cref="Target"/> itself; call them with overrides when your
/// CMS locales need a different mapping (e.g. <c>de</c> → <c>de-CH</c>).
/// </summary>
public static partial class LanguageCodes
{
    /// <summary>The region a bare target language gets (<c>de</c> → <c>de-DE</c>).</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultRegions = new Dictionary<string, string>
    {
        ["ar"] = "ar-SA", ["bg"] = "bg-BG", ["cs"] = "cs-CZ", ["da"] = "da-DK", ["de"] = "de-DE", ["el"] = "el-GR",
        ["en"] = "en-US", ["es"] = "es-ES", ["et"] = "et-EE", ["fi"] = "fi-FI", ["fr"] = "fr-FR", ["hr"] = "hr-HR",
        ["hu"] = "hu-HU", ["it"] = "it-IT", ["ja"] = "ja-JP", ["ko"] = "ko-KR", ["lt"] = "lt-LT", ["lv"] = "lv-LV",
        ["nb"] = "nb-NO", ["nl"] = "nl-NL", ["pl"] = "pl-PL", ["pt"] = "pt-PT", ["ro"] = "ro-RO", ["ru"] = "ru-RU",
        ["sk"] = "sk-SK", ["sl"] = "sl-SI", ["sr"] = "sr-RS", ["sv"] = "sv-SE", ["th"] = "th-TH", ["tr"] = "tr-TR",
        ["uk"] = "uk-UA", ["vi"] = "vi-VN", ["zh"] = "zh-CN",
    };

    /// <summary>
    /// Canonical spelling: <c>" de_ch "</c> → <c>de-CH</c>, <c>zh-hant</c> → <c>zh-Hant</c>,
    /// <c>es-419</c> stays, <c>DE</c> → <c>de</c>. Blank → empty string. Variants and extensions after a
    /// script or region are dropped (<c>de_DE_formal</c> → <c>de-DE</c>, <c>en-US-x-twain</c> → <c>en-US</c>);
    /// a code whose second subtag is neither a script nor a region is kept (<c>de-CHE</c>) and isn't valid.
    /// </summary>
    public static string Normalize(string? code)
    {
        var trimmed = (code ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }
        // Empty subtags are kept on purpose, so that "de-" stays invalid in IsValid.
        var parts = trimmed.Replace('_', '-').Split('-');
        parts[0] = parts[0].ToLowerInvariant();
        var next = 1;
        if (next < parts.Length && IsAsciiLetters(parts[next], 4))
        {
            parts[next] = TitleCase(parts[next]);
            next++;
        }
        if (next < parts.Length && (IsAsciiLetters(parts[next], 2) || IsAsciiDigits(parts[next], 3)))
        {
            parts[next] = parts[next].ToUpperInvariant();
            next++;
        }
        if (next > 1)
        {
            // Variants and extensions after a script or region are dropped (de_DE_formal → de-DE).
            return string.Join('-', parts, 0, next);
        }
        for (var i = 1; i < parts.Length; i++)
        {
            var part = parts[i];
            parts[i] = part.Length switch
            {
                2 => part.ToUpperInvariant(),
                4 => TitleCase(part),
                _ => part,
            };
        }
        return string.Join('-', parts);
    }

    private static string TitleCase(string part) => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant();

    private static bool IsAsciiLetters(string part, int length) => part.Length == length && part.All(char.IsAsciiLetter);

    private static bool IsAsciiDigits(string part, int length) => part.Length == length && part.All(char.IsAsciiDigit);

    /// <summary>
    /// Source code: the bare language of the matching override, else of <paramref name="code"/>
    /// (<c>en-US</c> → <c>en</c>). Empty for an empty code (Supertext then detects the language).
    /// </summary>
    /// <param name="code">The CMS locale.</param>
    /// <param name="overrides">CMS locale → Supertext code; a key matches when it normalizes to the same code.</param>
    public static string Source(string? code, IReadOnlyDictionary<string, string>? overrides = null)
    {
        var chosen = Normalize(Override(code, overrides) ?? code);
        var dash = chosen.IndexOf('-');
        return dash < 0 ? chosen : chosen[..dash];
    }

    /// <summary>
    /// Target code: the matching non-blank override, else <paramref name="code"/>, normalized; a bare
    /// language gets its <see cref="DefaultRegions">default region</see>. Unknown bare codes stay as they are.
    /// </summary>
    /// <param name="code">The CMS locale.</param>
    /// <param name="overrides">CMS locale → Supertext code; a key matches when it normalizes to the same code.</param>
    public static string Target(string? code, IReadOnlyDictionary<string, string>? overrides = null)
    {
        var chosen = Normalize(Override(code, overrides) ?? code);
        if (chosen.Contains('-'))
        {
            return chosen;
        }
        return DefaultRegions.TryGetValue(chosen, out var regional) ? regional : chosen;
    }

    /// <summary>Whether the code (after <see cref="Normalize"/>) looks like a code Supertext can accept.</summary>
    public static bool IsValid(string? code) => ValidCode().IsMatch(Normalize(code));

    private static string? Override(string? code, IReadOnlyDictionary<string, string>? overrides)
    {
        if (overrides is null)
        {
            return null;
        }
        var normalized = Normalize(code);
        foreach (var (key, value) in overrides)
        {
            if (Normalize(key) == normalized && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        return null;
    }

    [GeneratedRegex("^[a-z]{2,3}(-[A-Z][a-z]{3})?(-([A-Z]{2}|[0-9]{3}))?$")]
    private static partial Regex ValidCode();
}
