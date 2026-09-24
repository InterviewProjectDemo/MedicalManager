using System.Text;

namespace MedicalManager.Data.Notifications;

/// <summary>
/// American English letter-to-sound rules for personal names that are not in the
/// pronunciation dictionary. Output is IPA that Amazon Polly can speak.
/// </summary>
public static class EnglishNamePhoneticizer
{
    private const string Vowels = "aeiou";
    private const string Consonants = "bcdfghjklmnpqrstvwxyz";

    public static string? TryToIpa(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        if (NamePronunciations.TryGet(name) is string known)
            return known;

        var lower = name.Trim().ToLowerInvariant();
        if (lower.Contains('-'))
        {
            var parts = lower.Split('-', StringSplitOptions.RemoveEmptyEntries);
            var spoken = new List<string>();
            foreach (var part in parts)
            {
                var ipa = NamePronunciations.TryGet(part) ?? ToIpaCore(part);
                if (ipa is null)
                    return null;
                spoken.Add(ipa);
            }

            return string.Join(" ", spoken);
        }

        return ToIpaCore(lower);
    }

    public static void SelfCheck()
    {
        Expect("Tim", "ˈtɪm");
        Expect("Kate", "ˈkeɪt");
        Expect("Bob", "ˈbɑb");
        Expect("Ann", "ˈæn");
        Expect("Steve", "ˈstiv");
        Expect("Mike", "ˈmaɪk");
        Expect("Grace", "ˈgreɪs");
        Expect("Chris", "ˈkrɪs");
        Expect("Betty", "ˈbɛti");
        Expect("Joe", "ˈdʒoʊ");
        Expect("Naveen", "nəˈvin");
        Expect("Sharma", "ˈʃɑrmə");
        Expect("Sean", "ʃɔn");
        Expect("Jose", "hoʊˈzeɪ");
        Expect("Rivera", "rɪˈvɛrə");
    }

    private static void Expect(string name, string ipa)
    {
        var actual = TryToIpa(name);
        if (!string.Equals(actual, ipa, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Name pronunciation for '{name}' was '{actual}', expected '{ipa}'.");
        }
    }

    private static string? ToIpaCore(string lower)
    {
        if (lower.Length == 0 || lower.Any(c => c is < 'a' or > 'z'))
            return null;

        var chars = lower.ToList();
        int longAt = -1;
        var softTail = false;

        if (chars.Count >= 2 && chars[^1] == 'e')
        {
            var before = chars[^2];
            if (Vowels.Contains(before))
            {
                longAt = chars.Count - 2;
                chars.RemoveAt(chars.Count - 1);
            }
            else if (chars.Count >= 3 && Consonants.Contains(before) && before != 'y')
            {
                var doubled = chars.Count >= 4 && chars[^3] == before;
                var hasVowel = chars.Take(chars.Count - 1).Any(c => Vowels.Contains(c));
                if (hasVowel)
                {
                    if (before is 'c' or 'g')
                        softTail = true;

                    chars.RemoveAt(chars.Count - 1);
                    if (!doubled)
                    {
                        for (var i = chars.Count - 2; i >= 0; i--)
                        {
                            if (!Vowels.Contains(chars[i]))
                                continue;
                            longAt = i;
                            break;
                        }
                    }
                }
            }
        }

        var ipa = new StringBuilder();
        var markedLong = false;
        for (var i = 0; i < chars.Count; i++)
        {
            if (i + 1 < chars.Count && chars[i] == chars[i + 1] && Consonants.Contains(chars[i]) && chars[i] != 'y')
                continue;

            var rest = Rest(chars, i);
            if (rest.StartsWith("tion", StringComparison.Ordinal))
            {
                ipa.Append("ʃən");
                i += 3;
                continue;
            }

            if (rest.StartsWith("sion", StringComparison.Ordinal))
            {
                ipa.Append("ʒən");
                i += 3;
                continue;
            }

            if (rest.StartsWith("igh", StringComparison.Ordinal))
            {
                ipa.Append(Mark(ref markedLong, "aɪ"));
                i += 2;
                continue;
            }

            if (rest.StartsWith("eigh", StringComparison.Ordinal))
            {
                ipa.Append(Mark(ref markedLong, "eɪ"));
                i += 3;
                continue;
            }

            if (rest.StartsWith("tch", StringComparison.Ordinal))
            {
                ipa.Append("tʃ");
                i += 2;
                continue;
            }

            if (rest.StartsWith("dge", StringComparison.Ordinal))
            {
                ipa.Append("dʒ");
                i += 2;
                continue;
            }

            if (i == 0 && rest.StartsWith("kn", StringComparison.Ordinal))
            {
                ipa.Append('n');
                i += 1;
                continue;
            }

            if (i == 0 && rest.StartsWith("wr", StringComparison.Ordinal))
            {
                ipa.Append('r');
                i += 1;
                continue;
            }

            if (rest.StartsWith("chr", StringComparison.Ordinal))
            {
                ipa.Append("kr");
                i += 2;
                continue;
            }

            if (rest.StartsWith("ck", StringComparison.Ordinal))
            {
                ipa.Append('k');
                i += 1;
                continue;
            }

            if (rest.StartsWith("ph", StringComparison.Ordinal))
            {
                ipa.Append('f');
                i += 1;
                continue;
            }

            if (rest.StartsWith("sh", StringComparison.Ordinal))
            {
                ipa.Append('ʃ');
                i += 1;
                continue;
            }

            if (rest.StartsWith("ch", StringComparison.Ordinal))
            {
                ipa.Append("tʃ");
                i += 1;
                continue;
            }

            if (rest.StartsWith("th", StringComparison.Ordinal))
            {
                ipa.Append('θ');
                i += 1;
                continue;
            }

            if (rest.StartsWith("wh", StringComparison.Ordinal))
            {
                ipa.Append('w');
                i += 1;
                continue;
            }

            if (rest.StartsWith("qu", StringComparison.Ordinal))
            {
                ipa.Append("kw");
                i += 1;
                continue;
            }

            if (rest.StartsWith("ng", StringComparison.Ordinal))
            {
                ipa.Append('ŋ');
                i += 1;
                continue;
            }

            if (rest.StartsWith("ee", StringComparison.Ordinal) || rest.StartsWith("ea", StringComparison.Ordinal))
            {
                ipa.Append(Mark(ref markedLong, "i"));
                i += 1;
                continue;
            }

            if (rest.StartsWith("oo", StringComparison.Ordinal))
            {
                ipa.Append(Mark(ref markedLong, "u"));
                i += 1;
                continue;
            }

            if (rest.StartsWith("oa", StringComparison.Ordinal) || rest.StartsWith("oe", StringComparison.Ordinal))
            {
                ipa.Append(Mark(ref markedLong, "oʊ"));
                i += 1;
                continue;
            }

            if (rest.StartsWith("ai", StringComparison.Ordinal) || rest.StartsWith("ay", StringComparison.Ordinal))
            {
                ipa.Append(Mark(ref markedLong, "eɪ"));
                i += 1;
                continue;
            }

            if (rest.StartsWith("ei", StringComparison.Ordinal) || rest.StartsWith("ie", StringComparison.Ordinal))
            {
                ipa.Append(Mark(ref markedLong, "i"));
                i += 1;
                continue;
            }

            if (rest.StartsWith("oi", StringComparison.Ordinal) || rest.StartsWith("oy", StringComparison.Ordinal))
            {
                ipa.Append(Mark(ref markedLong, "ɔɪ"));
                i += 1;
                continue;
            }

            if (rest.StartsWith("ou", StringComparison.Ordinal) || rest.StartsWith("ow", StringComparison.Ordinal))
            {
                ipa.Append(Mark(ref markedLong, "aʊ"));
                i += 1;
                continue;
            }

            if (rest.StartsWith("au", StringComparison.Ordinal) || rest.StartsWith("aw", StringComparison.Ordinal))
            {
                ipa.Append(Mark(ref markedLong, "ɔ"));
                i += 1;
                continue;
            }

            if (rest.StartsWith("ar", StringComparison.Ordinal))
            {
                ipa.Append(Mark(ref markedLong, "ɑr"));
                i += 1;
                continue;
            }

            if (rest.StartsWith("er", StringComparison.Ordinal) || rest.StartsWith("ir", StringComparison.Ordinal) || rest.StartsWith("ur", StringComparison.Ordinal))
            {
                ipa.Append(Mark(ref markedLong, "ər"));
                i += 1;
                continue;
            }

            if (rest.StartsWith("or", StringComparison.Ordinal))
            {
                ipa.Append(Mark(ref markedLong, "ɔr"));
                i += 1;
                continue;
            }

            var c = chars[i];
            if (c == 'c')
            {
                var next = i + 1 < chars.Count ? chars[i + 1] : '\0';
                var soft = next is 'e' or 'i' or 'y' || (softTail && i == chars.Count - 1);
                ipa.Append(soft ? 's' : 'k');
                continue;
            }

            if (c == 'g')
            {
                var next = i + 1 < chars.Count ? chars[i + 1] : '\0';
                var soft = next is 'e' or 'i' or 'y' || (softTail && i == chars.Count - 1);
                ipa.Append(soft ? "dʒ" : "g");
                continue;
            }

            if (c == 'j')
            {
                ipa.Append("dʒ");
                continue;
            }

            if (c == 'x')
            {
                ipa.Append("ks");
                continue;
            }

            if (c == 'q')
            {
                ipa.Append('k');
                continue;
            }

            if (c == 'y')
            {
                if (i == 0)
                    ipa.Append('j');
                else
                    ipa.Append(Mark(ref markedLong, "i"));
                continue;
            }

            if (Vowels.Contains(c))
            {
                var vowel = i == longAt ? LongVowel(c, i > 0 ? chars[i - 1] : '\0') : ShortVowel(c);
                ipa.Append(Mark(ref markedLong, vowel));
                continue;
            }

            ipa.Append(c);
        }

        if (ipa.Length == 0)
            return null;

        var text = ipa.ToString();
        if (!markedLong && !text.Contains('ˈ'))
            text = "ˈ" + text;
        else if (!text.Contains('ˈ'))
            text = "ˈ" + text;

        return text;
    }

    private static string Mark(ref bool markedLong, string phoneme)
    {
        markedLong = true;
        return phoneme;
    }

    private static string LongVowel(char vowel, char previous) => vowel switch
    {
        'a' => "eɪ",
        'e' => "i",
        'i' => "aɪ",
        'o' => "oʊ",
        'u' => previous is 'r' or 'l' or 'j' ? "u" : "ju",
        _ => ShortVowel(vowel)
    };

    private static string ShortVowel(char vowel) => vowel switch
    {
        'a' => "æ",
        'e' => "ɛ",
        'i' => "ɪ",
        'o' => "ɑ",
        'u' => "ʌ",
        _ => "ə"
    };

    private static string Rest(List<char> chars, int index)
    {
        var take = Math.Min(4, chars.Count - index);
        return new string(chars.GetRange(index, take).ToArray());
    }
}
