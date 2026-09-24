using System.Net;
using System.Text;

namespace MedicalManager.Data.Notifications;

public sealed record SpokenNameParts(string First, string Middle, string Last, string Full)
{
    public IReadOnlyList<string> Tokens
    {
        get
        {
            var tokens = new List<string>();
            if (!string.IsNullOrWhiteSpace(First))
                tokens.Add(First);
            if (!string.IsNullOrWhiteSpace(Middle))
                tokens.AddRange(Middle.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (!string.IsNullOrWhiteSpace(Last) && !string.Equals(Last, First, StringComparison.Ordinal))
                tokens.Add(Last);
            return tokens;
        }
    }
}

/// <summary>
/// Builds the spoken form of a person's full name so a phone call says the
/// first name and the last name as distinct, correctly pronounced words.
/// </summary>
public static class NameSpeaker
{
    private static readonly HashSet<string> Titles = new(StringComparer.OrdinalIgnoreCase)
    {
        "dr", "mr", "mrs", "ms", "miss", "mx", "prof", "professor", "sir", "dame"
    };

    private static readonly HashSet<string> Suffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "jr", "sr", "ii", "iii", "iv", "md", "do", "phd", "np", "rn", "pa"
    };

    public static SpokenNameParts Split(string? fullName)
    {
        var raw = string.IsNullOrWhiteSpace(fullName) ? "friend" : fullName.Trim();
        var tokens = raw.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().Trim('.', ','))
            .Where(token => token.Length > 0)
            .ToList();

        while (tokens.Count > 1 && Titles.Contains(tokens[0]))
            tokens.RemoveAt(0);

        while (tokens.Count > 1 && Suffixes.Contains(tokens[^1]))
            tokens.RemoveAt(tokens.Count - 1);

        if (tokens.Count == 0)
            return new SpokenNameParts("friend", "", "", "friend");

        if (tokens.Count == 1)
            return new SpokenNameParts(tokens[0], "", "", tokens[0]);

        var first = tokens[0];
        var last = tokens[^1];
        var middle = string.Join(' ', tokens.Skip(1).Take(tokens.Count - 2));
        return new SpokenNameParts(first, middle, last, string.Join(' ', tokens));
    }

    public static string Ssml(string? fullName, string? pronunciation)
    {
        var parts = Split(fullName);
        if (!string.IsNullOrWhiteSpace(pronunciation))
            return CustomSsml(parts, pronunciation.Trim());

        var builder = new StringBuilder();
        foreach (var token in parts.Tokens)
        {
            if (builder.Length > 0)
                builder.Append("""<break time="280ms"/>""");
            builder.Append(SpeakToken(token));
        }

        return builder.ToString();
    }

    public static string SsmlFirst(string? fullName, string? pronunciation)
    {
        var parts = Split(fullName);
        if (string.IsNullOrWhiteSpace(pronunciation))
            return SpeakToken(parts.First);

        var hint = pronunciation.Trim();
        if (hint.StartsWith('/') && hint.EndsWith('/'))
            return SpeakToken(parts.First);

        var hints = hint.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hints.Length == parts.Tokens.Count && hints.Length > 0)
            return Alias(parts.First, SpokenAlias(hints[0]));

        return SpeakToken(parts.First);
    }

    public static string Preview(string? fullName, string? pronunciation)
    {
        var parts = Split(fullName);
        if (!string.IsNullOrWhiteSpace(pronunciation))
        {
            var spoken = SpokenAlias(pronunciation);
            return string.IsNullOrWhiteSpace(spoken) ? parts.Full : spoken;
        }

        return parts.Full;
    }

    public static void SelfCheck()
    {
        EnglishNamePhoneticizer.SelfCheck();
        var ssml = Ssml("Alex Rivera", null);
        if (!ssml.Contains("Alex", StringComparison.Ordinal) || !ssml.Contains("Rivera", StringComparison.Ordinal))
            throw new InvalidOperationException("Full name speech did not include the first and last name.");
        if (!ssml.Contains("phoneme", StringComparison.Ordinal))
            throw new InvalidOperationException("Rivera should use a pronunciation phoneme.");
        if (!ssml.Contains("break", StringComparison.Ordinal))
            throw new InvalidOperationException("First and last name should be separated by a pause.");

        var custom = Ssml("Naveen Sharma", "nah-VEEN SHAR-mah");
        if (!custom.Contains("nah VEEN", StringComparison.Ordinal) || !custom.Contains("SHAR mah", StringComparison.Ordinal))
            throw new InvalidOperationException("Custom pronunciation was not applied to both names.");
    }

    private static string CustomSsml(SpokenNameParts parts, string pronunciation)
    {
        if (pronunciation.StartsWith('/') && pronunciation.EndsWith('/') && pronunciation.Length > 2)
            return Phoneme(parts.Full, pronunciation.Trim('/'));

        var hints = pronunciation.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tokens = parts.Tokens;
        if (hints.Length == tokens.Count && tokens.Count > 0)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < tokens.Count; i++)
            {
                if (i > 0)
                    builder.Append("""<break time="280ms"/>""");
                builder.Append(Alias(tokens[i], SpokenAlias(hints[i])));
            }

            return builder.ToString();
        }

        return Alias(parts.Full, SpokenAlias(pronunciation));
    }

    private static string SpeakToken(string token)
    {
        if (token.Contains('-'))
        {
            var bits = token.Split('-', StringSplitOptions.RemoveEmptyEntries);
            return string.Join("""<break time="120ms"/>""", bits.Select(SpeakToken));
        }

        var ipa = EnglishNamePhoneticizer.TryToIpa(token);
        return string.IsNullOrWhiteSpace(ipa) ? Xml(token) : Phoneme(token, ipa);
    }

    private static string Phoneme(string text, string ipa) =>
        $"""<phoneme alphabet="ipa" ph="{Xml(ipa)}">{Xml(text)}</phoneme>""";

    private static string Alias(string text, string alias) =>
        $"""<sub alias="{Xml(alias)}">{Xml(text)}</sub>""";

    private static string SpokenAlias(string hint) =>
        string.Join(' ', hint.Split(['-', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Xml(string value) => WebUtility.HtmlEncode(value);
}
