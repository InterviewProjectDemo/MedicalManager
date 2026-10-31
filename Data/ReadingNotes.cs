namespace MedicalManager.Data;

public static class ReadingNotes
{
    public const int MaxSuggestions = 10;
    public const int MaxPhraseLength = 80;
    public static readonly TimeSpan HistoryWindow = TimeSpan.FromDays(7);

    public readonly record struct NoteSample(int Id, string UserId, DateTime RecordedAt, string? Notes);

    public static IReadOnlyList<string> Suggest(
        IEnumerable<NoteSample> samples,
        string userId,
        DateTime now,
        int? excludeReadingId = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return [];

        var start = now - HistoryWindow;
        var clusters = new List<Cluster>();
        foreach (var sample in samples.OrderByDescending(sample => sample.RecordedAt))
        {
            if (!string.Equals(sample.UserId, userId, StringComparison.Ordinal))
                continue;
            if (excludeReadingId is int skip && sample.Id == skip)
                continue;
            if (sample.RecordedAt < start || sample.RecordedAt > now)
                continue;

            var text = Clean(sample.Notes);
            if (text is null)
                continue;

            var phrase = new Phrase(text, Tokenize(text), sample.RecordedAt);
            if (phrase.Tokens.Length == 0)
                continue;

            var cluster = clusters.FirstOrDefault(existing => NearDuplicate(existing.Tokens, phrase.Tokens));
            if (cluster is null)
            {
                cluster = new Cluster();
                clusters.Add(cluster);
            }

            cluster.Add(phrase);
        }

        return clusters
            .OrderByDescending(cluster => cluster.Latest)
            .ThenByDescending(cluster => cluster.Count)
            .ThenBy(cluster => cluster.Display, StringComparer.Ordinal)
            .Take(MaxSuggestions)
            .Select(cluster => cluster.Display)
            .ToList();
    }

    public static void SelfCheck()
    {
        var now = new DateTime(2026, 10, 31, 12, 0, 0);
        const string user = "patient-a";

        var capped = new List<NoteSample>();
        for (var i = 0; i < 15; i++)
            capped.Add(new NoteSample(i + 1, user, now.AddMinutes(-i), $"Note {i:00}"));
        var top = Suggest(capped, user, now);
        Expect(top.Count == MaxSuggestions, "Suggestions must cap at 10.");
        Expect(top[0] == "Note 00", "The most recent note should be first.");
        Expect(top[9] == "Note 09", "The tenth suggestion should be the tenth most recent.");
        Expect(!top.Contains("Note 14"), "Notes past the cap should be left out.");

        var morning = Suggest(
        [
            new NoteSample(1, user, now.AddHours(-1), "Morning reading"),
            new NoteSample(2, user, now.AddHours(-2), "morning reading."),
            new NoteSample(3, user, now.AddHours(-3), "Morning  reading")
        ], user, now);
        Expect(morning.Count == 1 && morning[0] == "Morning reading",
            "Case, spacing, and punctuation should collapse to one choice.");

        var dotted = Suggest([new NoteSample(1, user, now, "morning reading.")], user, now);
        Expect(dotted.Count == 1 && dotted[0] == "morning reading",
            "Trailing punctuation should not remain on the suggestion.");

        var line = OverwriteNote.LockLine("Jane Doe", "Nurse");
        var overwritten = Suggest(
        [
            new NoteSample(1, user, now.AddMinutes(-30), OverwriteNote.Compose(line, "After lunch")),
            new NoteSample(2, user, now.AddMinutes(-20), line),
            new NoteSample(3, user, now, "   "),
            new NoteSample(4, user, now, null),
            new NoteSample(5, user, now, ""),
            new NoteSample(6, user, now.AddMinutes(-10), "\n\t  ")
        ], user, now);
        Expect(overwritten.Count == 1 && overwritten[0] == "After lunch",
            "Only the extra overwrite text should be suggested.");
        Expect(overwritten.All(item => !item.StartsWith(OverwriteNote.Lead, StringComparison.Ordinal)),
            "The overwrite line must not be suggested.");
        Expect(overwritten.All(item => !string.IsNullOrWhiteSpace(item)),
            "Blank notes should be dropped.");

        var exercise = Suggest(
        [
            new NoteSample(1, user, now.AddHours(-4), "After exercise"),
            new NoteSample(2, user, now.AddHours(-5), "After exercise today"),
            new NoteSample(3, user, now.AddHours(-6), "after   exercise.")
        ], user, now);
        Expect(exercise.Count == 1 && exercise[0] == "After exercise",
            "Near-duplicate notes should collapse to the short phrasing.");

        var distinct = Suggest(
        [
            new NoteSample(1, user, now.AddHours(-1), "Left arm"),
            new NoteSample(2, user, now.AddHours(-2), "Right arm"),
            new NoteSample(3, user, now.AddHours(-3), "Before medication"),
            new NoteSample(4, user, now.AddHours(-4), "After medication")
        ], user, now);
        Expect(distinct.Count == 4, "Distinct phrases should stay separate.");

        var reordered = Suggest(
        [
            new NoteSample(1, user, now.AddHours(-2), "arm left"),
            new NoteSample(2, user, now.AddHours(-1), "Left arm")
        ], user, now);
        Expect(reordered.Count == 1 && reordered[0] == "Left arm",
            "The same words in a different order should collapse to the latest phrasing.");

        var scoped = Suggest(
        [
            new NoteSample(1, user, now.AddHours(-2), "My morning note"),
            new NoteSample(2, "patient-b", now.AddMinutes(-1), "Secret other patient note")
        ], user, now);
        Expect(scoped.Count == 1 && scoped[0] == "My morning note",
            "Another patient's notes must not be suggested when the call is scoped by user id.");

        var windowed = Suggest(
        [
            new NoteSample(1, user, now.AddDays(-7), "Edge of window"),
            new NoteSample(2, user, now.AddDays(-7).AddTicks(-1), "Too old note"),
            new NoteSample(3, user, now, "Right now"),
            new NoteSample(4, user, now.AddTicks(1), "Future note")
        ], user, now);
        Expect(windowed.Contains("Edge of window") && windowed.Contains("Right now"),
            "The window includes now and exactly 7 days earlier.");
        Expect(!windowed.Contains("Too old note") && !windowed.Contains("Future note"),
            "Notes outside the last 7 days should be left out.");

        var history = new List<NoteSample>
        {
            new(7, user, now.AddHours(-1), "Only on this reading"),
            new(8, user, now.AddHours(-2), "Shared phrase"),
            new(9, user, now.AddHours(-3), "Shared phrase")
        };
        var editing = Suggest(history, user, now, excludeReadingId: 7);
        Expect(!editing.Contains("Only on this reading"),
            "The reading being edited should not supply its own unique note.");
        Expect(editing.Contains("Shared phrase"),
            "Other readings in the window still count.");
        Expect(Suggest(history, user, now, excludeReadingId: 9).Contains("Shared phrase"),
            "A shared phrase remains when one of its readings is excluded.");

        var shortened = Suggest(
        [
            new NoteSample(1, user, now, "Felt dizzy after standing. Then I sat down for a long time and waited until it passed.")
        ], user, now);
        Expect(shortened.Count == 1 && shortened[0] == "Felt dizzy after standing",
            "A long note should start from its first sentence.");

        const string paragraph = "Checked this reading after a slow walk around the block before breakfast and before taking the morning medicine at the kitchen table with a glass of water";
        var cut = Suggest([new NoteSample(1, user, now, paragraph)], user, now);
        Expect(cut.Count == 1 && cut[0].Length <= MaxPhraseLength && paragraph.StartsWith(cut[0], StringComparison.Ordinal),
            "A long sentence should be shortened to the user's own words.");

        var ranked = Suggest(
        [
            new NoteSample(1, user, now, "Alpha"),
            new NoteSample(2, user, now.AddHours(-2), "Beta"),
            new NoteSample(3, user, now.AddHours(-3), "Beta"),
            new NoteSample(4, user, now.AddHours(-4), "Beta"),
            new NoteSample(5, user, now.AddHours(-5), "Beta"),
            new NoteSample(6, user, now.AddHours(-6), "Beta")
        ], user, now);
        Expect(ranked[0] == "Alpha" && ranked[1] == "Beta",
            "A newer phrase should rank ahead of an older one used more often.");

        var tied = Suggest(
        [
            new NoteSample(1, user, now, "Alpha"),
            new NoteSample(2, user, now.AddHours(-1), "Alpha"),
            new NoteSample(3, user, now, "Beta")
        ], user, now);
        Expect(tied[0] == "Alpha" && tied[1] == "Beta",
            "When the latest use ties, the phrase used more often should come first.");

        Expect(Suggest([], user, now).Count == 0, "Empty history should not invent suggestions.");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static string? Clean(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return null;

        var text = notes.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (OverwriteNote.IsOverwrite(text))
        {
            if (!OverwriteNote.TrySplit(text, out _, out var extra))
                return null;
            text = extra.Trim();
        }

        if (string.IsNullOrWhiteSpace(text) || text.StartsWith(OverwriteNote.Lead, StringComparison.Ordinal))
            return null;

        var lineBreak = text.IndexOf('\n');
        if (lineBreak >= 0)
            text = text[..lineBreak];

        text = string.Join(' ', text.Split([' ', '\t', '\u00A0'], StringSplitOptions.RemoveEmptyEntries));
        text = FirstClause(text);
        text = TrimEdgePunctuation(text);
        if (text.Length == 0)
            return null;
        if (text.Length > MaxPhraseLength)
            text = TrimEdgePunctuation(CutAtWord(text, MaxPhraseLength));
        if (text.Length == 0 || text.StartsWith(OverwriteNote.Lead, StringComparison.Ordinal))
            return null;

        return text;
    }

    private static string FirstClause(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
            if (character == '\n')
                return text[..i];
            if (i >= 8 && character is '.' or '!' or '?' && (i == text.Length - 1 || char.IsWhiteSpace(text[i + 1])))
                return text[..i];
        }

        return text;
    }

    private static string CutAtWord(string text, int max)
    {
        if (text.Length <= max)
            return text;

        var cut = text.LastIndexOf(' ', max);
        if (cut < 12)
            cut = max;
        return text[..cut];
    }

    private static string TrimEdgePunctuation(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && IsEdgePunctuation(text[start]))
            start++;
        while (end > start && IsEdgePunctuation(text[end - 1]))
            end--;
        return start == 0 && end == text.Length ? text : text[start..end].Trim();
    }

    private static bool IsEdgePunctuation(char character) =>
        character is '.' or ',' or ';' or ':' or '!' or '?' or '"' or '\'' or '…'
            or '“' or '”' or '‘' or '’' or '(' or ')' or '[' or ']'
            or '-' or '–' or '—';

    private static string[] Tokenize(string text)
    {
        var tokens = new List<string>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var word = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (word)
            {
                if (start < 0)
                    start = i;
                continue;
            }

            if (start < 0)
                continue;

            tokens.Add(text[start..i].ToLowerInvariant());
            start = -1;
        }

        return tokens.ToArray();
    }

    private static bool NearDuplicate(string[] left, string[] right)
    {
        if (left.Length == 0 || right.Length == 0)
            return false;
        if (left.SequenceEqual(right))
            return true;

        var shorter = left.Length <= right.Length ? left : right;
        var longer = left.Length <= right.Length ? right : left;
        if (IsTrivialExtension(shorter, longer))
            return true;

        return SameWords(left, right);
    }

    private static bool IsTrivialExtension(string[] shorter, string[] longer)
    {
        if (shorter.Length == 0 || shorter.Length >= longer.Length)
            return false;

        if (StartsWith(longer, shorter))
            return longer[shorter.Length..].All(IsTrivial);

        if (!EndsWith(longer, shorter))
            return false;

        var extra = longer.Length - shorter.Length;
        for (var i = 0; i < extra; i++)
        {
            if (!IsTrivial(longer[i]))
                return false;
        }

        return true;
    }

    private static bool SameWords(string[] left, string[] right)
    {
        var a = Significant(left);
        var b = Significant(right);
        if (a.Length == 0 || a.Length != b.Length)
            return false;

        Array.Sort(a, StringComparer.Ordinal);
        Array.Sort(b, StringComparer.Ordinal);
        return a.SequenceEqual(b);
    }

    private static string[] Significant(string[] tokens)
    {
        var kept = new List<string>(tokens.Length);
        foreach (var token in tokens)
        {
            if (!IsTrivial(token))
                kept.Add(token);
        }

        return kept.ToArray();
    }

    private static bool StartsWith(string[] longer, string[] prefix)
    {
        if (prefix.Length > longer.Length)
            return false;

        for (var i = 0; i < prefix.Length; i++)
        {
            if (!string.Equals(longer[i], prefix[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool EndsWith(string[] longer, string[] suffix)
    {
        if (suffix.Length > longer.Length)
            return false;

        var offset = longer.Length - suffix.Length;
        for (var i = 0; i < suffix.Length; i++)
        {
            if (!string.Equals(longer[offset + i], suffix[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool IsTrivial(string token) => TrivialWords.Contains(token);

    private static readonly HashSet<string> TrivialWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "today", "tonight", "yesterday", "now", "just", "again",
        "please", "my", "this", "that", "and", "with", "last", "ago", "very",
        "really", "so", "too", "also", "at", "of", "for", "to", "in", "on", "it",
        "was", "is", "am", "pm"
    };

    private readonly record struct Phrase(string Text, string[] Tokens, DateTime At);

    private sealed class Cluster
    {
        private readonly List<Phrase> _phrases = [];

        public string[] Tokens => _phrases[0].Tokens;

        public DateTime Latest => _phrases.Max(phrase => phrase.At);

        public int Count => _phrases.Count;

        public string Display => ChooseDisplay();

        public void Add(Phrase phrase) => _phrases.Add(phrase);

        private string ChooseDisplay()
        {
            var groups = new List<Group>();
            foreach (var phrase in _phrases)
            {
                var existing = groups.FirstOrDefault(group =>
                    string.Equals(group.Text, phrase.Text, StringComparison.Ordinal));
                if (existing is null)
                    groups.Add(new Group(phrase.Text, phrase.Tokens, phrase.At));
                else
                    existing.Add(phrase.At);
            }

            var cores = groups.Where(group => groups.All(other =>
                other.Tokens.Length >= group.Tokens.Length && StartsWith(other.Tokens, group.Tokens))).ToList();
            var pool = cores.Count > 0 ? cores : groups;
            pool.Sort(cores.Count > 0 ? CompareCores : CompareRecent);
            return pool[0].Text;
        }

        private static int CompareCores(Group left, Group right)
        {
            var length = left.Text.Length.CompareTo(right.Text.Length);
            if (length != 0)
                return length;
            return CompareRecent(left, right);
        }

        private static int CompareRecent(Group left, Group right)
        {
            var recent = right.Latest.CompareTo(left.Latest);
            if (recent != 0)
                return recent;
            var count = right.Count.CompareTo(left.Count);
            if (count != 0)
                return count;
            return string.Compare(left.Text, right.Text, StringComparison.Ordinal);
        }
    }

    private sealed class Group(string text, string[] tokens, DateTime latest)
    {
        public string Text { get; } = text;
        public string[] Tokens { get; } = tokens;
        public int Count { get; private set; } = 1;
        public DateTime Latest { get; private set; } = latest;

        public void Add(DateTime at)
        {
            Count++;
            if (at > Latest)
                Latest = at;
        }
    }
}
