namespace MedicalManager.Data.Notifications;

public sealed record GuidePerson(string Kind, string Name, string Phone, string? Detail = null);

public sealed record GuideTurn(string Prompt, bool Listen, bool Slow, bool OfferKind, bool OfferYesNo, GuidePerson? Call);

public sealed class CallGuide
{
    private readonly IReadOnlyList<GuidePerson> _family;
    private readonly IReadOnlyList<GuidePerson> _doctors;
    private readonly string _firstName;
    private string _phase = "open";
    private string? _listKind;
    private int _index = -1;
    private GuidePerson? _pending;

    public CallGuide(string? userName, IEnumerable<GuidePerson> family, IEnumerable<GuidePerson> doctors)
    {
        _firstName = FirstName(userName);
        _family = family.Where(person => !string.IsNullOrWhiteSpace(person.Name) && !string.IsNullOrWhiteSpace(person.Phone)).ToList();
        _doctors = doctors.Where(person => !string.IsNullOrWhiteSpace(person.Name) && !string.IsNullOrWhiteSpace(person.Phone)).ToList();
    }

    public static void SelfCheck()
    {
        var family = new[] { new GuidePerson("family", "Jordan Rivera", "9175550100") };
        var doctors = new[]
        {
            new GuidePerson("doctor", "Dr. Morgan Chen", "5125550101"),
            new GuidePerson("doctor", "Dr. Sam Patel", "5125550102")
        };
        var guide = new CallGuide("Alex Rivera", family, doctors);
        var hello = guide.Opening();
        if (!hello.Contains("Hello, Alex", StringComparison.Ordinal) || !hello.Contains("I am here to help", StringComparison.Ordinal))
            throw new InvalidOperationException("The call guide does not greet the user.");

        var unsure = guide.Hear("I don't know");
        if (!unsure.Prompt.Contains("family", StringComparison.OrdinalIgnoreCase) || !unsure.Prompt.Contains("doctor", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A confused answer should ask about family or a doctor.");

        var byName = new CallGuide("Alex Rivera", family, doctors);
        byName.Opening();
        var named = byName.Hear("Jordan");
        if (!named.Prompt.Contains("Jordan Rivera", StringComparison.Ordinal) || !named.OfferYesNo)
            throw new InvalidOperationException("Saying a saved name should offer that person.");

        var familyTurn = guide.Hear("family");
        if (!familyTurn.Prompt.Contains("Jordan Rivera", StringComparison.Ordinal) || !familyTurn.OfferYesNo)
            throw new InvalidOperationException("The family choice should offer the profile name.");

        var calling = guide.Hear("yes");
        if (calling.Call?.Name != "Jordan Rivera")
            throw new InvalidOperationException("Yes should call the family name that was just offered.");

        var shortAnswer = new CallGuide("Alex Rivera", family, doctors);
        shortAnswer.Opening();
        var yesFamily = shortAnswer.Hear("yes family");
        if (!yesFamily.Prompt.Contains("Jordan Rivera", StringComparison.Ordinal) || yesFamily.Call is not null)
            throw new InvalidOperationException("Yes family should offer the family name without a full sentence.");
        if (shortAnswer.Hear("yes").Call?.Name != "Jordan Rivera")
            throw new InvalidOperationException("Yes alone should call the family name that was just offered.");

        var yesOnly = new CallGuide("Alex Rivera", family, doctors);
        yesOnly.Opening();
        if (yesOnly.Hear("yes").Call is not null)
            throw new InvalidOperationException("Yes alone should not place a call before a person is chosen.");

        var many = new CallGuide("Alex Rivera", family, doctors);
        many.Opening();
        var firstDoctor = many.Hear("doctor");
        if (!firstDoctor.Slow || !firstDoctor.Prompt.Contains("Morgan Chen", StringComparison.Ordinal))
            throw new InvalidOperationException("Multiple doctors should be offered one name at a time.");
        var secondDoctor = many.Hear("no");
        if (!secondDoctor.Prompt.Contains("Sam Patel", StringComparison.Ordinal))
            throw new InvalidOperationException("No should move to the next doctor.");
        if (many.Hear("yes").Call?.Name != "Dr. Sam Patel")
            throw new InvalidOperationException("Yes should call the doctor who was just named.");
    }

    public string Opening()
    {
        _phase = "open";
        _pending = null;
        return $"Hello, {_firstName}. I am here to help. Family, or doctor?";
    }

    public GuideTurn StillHere()
    {
        if (_pending is not null)
            return new GuideTurn($"{_pending.Name}. Say yes.", true, true, false, true, null);

        return new GuideTurn($"I am here, {_firstName}. Family, or doctor?", true, true, true, false, null);
    }

    public GuideTurn Hear(string? transcript)
    {
        var options = (transcript ?? "")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (options.Length == 0)
            return HearOne("");

        return HearOne(options.OrderByDescending(Score).First());
    }

    private int Score(string heard)
    {
        if (SingleNameMatch(heard) is not null)
            return 6;
        if (WantsFamily(heard) || WantsDoctor(heard))
            return 5;
        if (IsNo(heard))
            return 4;
        if (IsYes(heard) && _pending is not null)
            return 3;
        if (IsYes(heard))
            return 1;
        return 0;
    }

    private GuideTurn HearOne(string heard)
    {
        if (WantsFamily(heard) && _listKind != "family")
            return StartList("family");
        if (WantsDoctor(heard) && _listKind != "doctor")
            return StartList("doctor");

        var named = SingleNameMatch(heard);
        if (named is not null)
        {
            if (IsYes(heard) || (_pending is not null && NameMatches(named.Name, _pending.Name) && IsMostlyName(heard, named)))
                return Call(named);
            return Offer(named);
        }

        if (_phase is "list" or "confirm" && IsNo(heard))
            return NextInList();

        if (_phase is "list" or "confirm" && IsYes(heard) && _pending is not null)
            return Call(_pending);

        if (_phase is "list" or "confirm" && _pending is not null)
            return new GuideTurn($"{_pending.Name}. Say yes, or no.", true, true, false, true, null);

        return AskKind();
    }

    private GuideTurn StartList(string kind)
    {
        var people = kind == "family" ? _family : _doctors;
        if (people.Count == 0)
        {
            var missing = kind == "family"
                ? "No family number saved."
                : "No doctor number saved.";
            _phase = "narrow";
            _pending = null;
            return new GuideTurn($"{missing} Family, or doctor?", true, true, true, false, null);
        }

        _listKind = kind;
        _index = 0;
        _phase = "list";
        _pending = people[0];
        var who = Introduce(_pending);
        if (people.Count == 1)
            return new GuideTurn($"{who}. Say yes.", true, true, false, true, null);

        return new GuideTurn($"{who}. Say yes, or no.", true, true, false, true, null);
    }

    private GuideTurn NextInList()
    {
        var people = _listKind == "doctor" ? _doctors : _family;
        _index++;
        if (_index < 0 || _index >= people.Count)
            return AskKind("That's everyone.");

        _phase = "list";
        _pending = people[_index];
        return new GuideTurn($"{Introduce(_pending)}. Say yes, or no.", true, true, false, true, null);
    }

    private GuideTurn Offer(GuidePerson person)
    {
        _pending = person;
        _phase = "confirm";
        _listKind = person.Kind;
        return new GuideTurn($"{Introduce(person)}. Say yes.", true, true, false, true, null);
    }

    private GuideTurn AskKind(string? lead = null)
    {
        _phase = "narrow";
        _pending = null;
        _listKind = null;
        var question = "Family, or doctor?";
        var prompt = string.IsNullOrWhiteSpace(lead)
            ? $"That's all right, {_firstName}. {question}"
            : $"{lead} {question}";
        return new GuideTurn(prompt, true, true, true, false, null);
    }

    private static GuideTurn Call(GuidePerson person) =>
        new($"Calling {person.Name}.", false, false, false, false, person);

    private static string Introduce(GuidePerson person)
    {
        if (string.IsNullOrWhiteSpace(person.Detail))
            return person.Name;

        var detail = person.Detail.Trim().TrimEnd('.');
        return $"{person.Name}, your {detail.ToLowerInvariant()}";
    }

    private GuidePerson? SingleNameMatch(string heard)
    {
        if (heard.Length < 2)
            return null;

        var matches = _family.Concat(_doctors).Where(person => NameMatches(heard, person.Name)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    public static bool NameMatches(string heard, string name)
    {
        var spoken = Normalize(heard);
        var target = Normalize(name);
        if (spoken.Length < 2 || target.Length == 0)
            return false;
        if (spoken.Contains(target, StringComparison.Ordinal) || target.Contains(spoken, StringComparison.Ordinal))
            return true;

        return Tokens(name).Any(token => spoken.Contains(token, StringComparison.Ordinal));
    }

    private static bool WantsFamily(string heard) =>
        Mentions(heard, "family", "relative", "mom", "dad", "mother", "father", "wife", "husband", "sister", "brother", "son", "daughter", "spouse", "partner");

    private static bool WantsDoctor(string heard) =>
        Mentions(heard, "doctor", "doc", "physician", "clinic", "specialist");

    private static bool IsYes(string heard)
    {
        var words = Normalize(heard).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Any(word => word is "yes" or "yeah" or "yep" or "sure" or "please" or "ok" or "okay" or "correct" or "call");
    }

    private static bool IsMostlyName(string heard, GuidePerson person)
    {
        var spoken = Tokens(heard).ToList();
        if (spoken.Count == 0)
            return false;

        var name = Tokens(person.Name).ToHashSet(StringComparer.Ordinal);
        return spoken.All(name.Contains);
    }

    private static bool IsNo(string heard)
    {
        var text = Normalize(heard);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Any(word => word is "no" or "nope" or "next" or "nah")
            || text.Contains("someone else", StringComparison.Ordinal);
    }

    private static bool Mentions(string heard, params string[] words)
    {
        var text = Normalize(heard);
        return words.Any(word => text.Contains(word, StringComparison.Ordinal));
    }

    private static IEnumerable<string> Tokens(string name) =>
        Normalize(name)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 2 && token is not "doctor" and not "family");

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var letters = value.ToLowerInvariant().Where(character => char.IsLetter(character) || character == ' ').ToArray();
        return string.Join(' ', new string(letters).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string FirstName(string? userName)
    {
        var first = userName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? "there" : first.Trim();
    }
}
