namespace MedicalManager.Data.Notifications;

public sealed record GuidePerson(string Kind, string Name, string Phone, string? Detail = null);

public sealed record GuideTurn(
    string Prompt,
    bool Listen,
    bool Slow,
    bool OfferKind,
    bool OfferYesNo,
    GuidePerson? Call,
    string? YesLabel = null,
    string? NoLabel = null,
    bool End = false);

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
        _doctors = doctors.Where(person => !string.IsNullOrWhiteSpace(person.Name)).ToList();
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
        if (!hello.Contains("Hi, Alex", StringComparison.Ordinal)
            || !hello.Contains("How can I help", StringComparison.Ordinal)
            || !hello.Contains("Jordan Rivera", StringComparison.Ordinal)
            || !hello.Contains("doctor", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The call guide should greet the user and offer the family person.");

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

        var declined = new CallGuide("Alex Rivera", family, doctors);
        declined.Opening();
        var doctorTurn = declined.Hear("no");
        if (!doctorTurn.Prompt.Contains("Morgan Chen", StringComparison.Ordinal) || !doctorTurn.OfferYesNo)
            throw new InvalidOperationException("No to family should offer a doctor.");

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

        var ending = new CallGuide("Alex Rivera", family, doctors);
        ending.Opening();
        var endTurn = ending.Hear("end");
        if (!endTurn.End || endTurn.Call is not null || !CallGuide.WantsToEnd("please end the call") || CallGuide.WantsToEnd("Jordan"))
            throw new InvalidOperationException("End should close the conversation without placing a call.");
    }

    public string Opening() => Begin().Prompt;

    public GuideTurn Begin()
    {
        if (_family.Count > 0)
        {
            _listKind = "family";
            _index = 0;
            _phase = "list";
            _pending = _family[0];
            return AskPerson(
                $"Hi, {_firstName}. How can I help? Who do you want to talk to, your family or a doctor? Should I call your family, {_pending.Name}?",
                doctorNext: true);
        }

        if (_doctors.Count > 0)
            return StartList("doctor", $"Hi, {_firstName}. How can I help? I don't have a family number yet.");

        _phase = "open";
        _pending = null;
        _listKind = null;
        return new GuideTurn($"Hi, {_firstName}. How can I help? I don't have a family or doctor number saved yet. Add one in your profile, and I will be ready.", false, true, false, false, null);
    }

    public GuideTurn StillHere()
    {
        if (_pending is not null)
            return AskPerson($"I'm right here, {_firstName}. Should I call {_pending.Name}?", doctorNext: _listKind == "family" && _index == 0);

        return new GuideTurn($"I'm right here, {_firstName}. Who do you want to talk to, your family or a doctor?", true, true, true, false, null);
    }

    public bool IsClearChoice(string? transcript)
    {
        var heard = Normalize(transcript);
        if (heard.Length == 0 || WordCount(heard) > 4)
            return false;
        if (WantsFamily(heard) && WantsDoctor(heard))
            return false;
        return IsYes(heard) || IsNo(heard) || WantsFamily(heard) || WantsDoctor(heard) || SingleNameMatch(heard) is not null;
    }

    public GuideTurn Hear(string? transcript)
    {
        var options = (transcript ?? "")
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (options.Length == 0)
            return HearOne("");

        if (WantsToEnd(transcript))
            return new GuideTurn("", false, false, false, false, null, End: true);

        var best = options.OrderByDescending(Score).First();
        if (IsEcho(best) || (WordCount(best) > 6 && !IsYes(best) && !IsNo(best)) || (WantsFamily(best) && WantsDoctor(best)))
            return KeepPlace();

        return HearOne(best);
    }

    public static bool WantsToEnd(string? heard)
    {
        var options = (heard ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return options.Any(option =>
        {
            var words = Normalize(option).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length is > 0 and <= 6 && words.Any(word => word is "end" or "ended" or "ending");
        });
    }

    private static bool IsEcho(string heard)
    {
        var text = Normalize(heard);
        return text.Contains("how can i help", StringComparison.Ordinal)
            || text.Contains("who do you want", StringComparison.Ordinal)
            || text.Contains("should i call", StringComparison.Ordinal);
    }

    private GuideTurn KeepPlace()
    {
        if (_phase is "list" or "confirm" && _pending is not null)
            return AskPerson(CurrentAsk(), doctorNext: _listKind == "family" && _index == 0);

        return AskKind();
    }

    private string CurrentAsk() =>
        _listKind == "family" && _index == 0 && _pending is not null
            ? $"That's all right, {_firstName}. Who do you want to talk to, your family or a doctor? Should I call your family, {_pending.Name}?"
            : $"That's all right, {_firstName}. Should I call {_pending?.Name}?";

    private static int WordCount(string heard) =>
        heard.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

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
            if (IsYes(heard))
                return Call(named);
            if (IsNo(heard) && _pending is not null && NameMatches(named.Name, _pending.Name))
                return NextInList();
            return Offer(named);
        }

        if (_phase is "list" or "confirm" && IsNo(heard))
            return NextInList();

        if (_phase is "list" or "confirm" && IsYes(heard) && _pending is not null)
            return Call(_pending);

        if (_phase is "list" or "confirm" && _pending is not null)
            return KeepPlace();

        return AskKind();
    }

    private GuideTurn StartList(string kind, string? lead = null)
    {
        var people = kind == "family" ? _family : _doctors;
        if (people.Count == 0)
        {
            var missing = kind == "family"
                ? "I don't have a family number saved yet."
                : "I don't have a doctor number saved yet.";
            _phase = "narrow";
            _pending = null;
            _listKind = null;
            return new GuideTurn($"That's all right, {_firstName}. {missing} Who do you want to talk to, your family or a doctor?", true, true, true, false, null);
        }

        _listKind = kind;
        _index = 0;
        _phase = "list";
        _pending = people[0];
        var intro = string.IsNullOrWhiteSpace(lead) ? "" : $"{lead.Trim()} ";
        var who = kind == "family" ? $"your family, {_pending.Name}" : FriendName(_pending);
        return AskPerson($"{intro}Should I call {who}?", doctorNext: kind == "family");
    }

    private GuideTurn NextInList()
    {
        if (_listKind == "family" && _index >= _family.Count - 1)
        {
            if (_doctors.Count > 0)
                return StartList("doctor", $"That's okay, {_firstName}. Let's try a doctor.");
            return AskPerson($"That's okay, {_firstName}. I don't have a doctor saved yet. Should I still call {_pending?.Name}?", doctorNext: false);
        }

        var people = _listKind == "doctor" ? _doctors : _family;
        _index++;
        if (_index < 0 || _index >= people.Count)
            return AskKind("Those are the people I can reach.");

        _phase = "list";
        _pending = people[_index];
        return AskPerson($"Okay. How about {FriendName(_pending)}? Should I call {_pending.Name}?", doctorNext: false);
    }

    private GuideTurn Offer(GuidePerson person)
    {
        _pending = person;
        _phase = "confirm";
        _listKind = person.Kind;
        return AskPerson($"I can do that. Should I call {FriendName(person)}?", doctorNext: person.Kind == "family");
    }

    private GuideTurn AskPerson(string prompt, bool doctorNext)
    {
        var yes = _pending is null ? "Yes" : $"Yes, call {_pending.Name}";
        var no = doctorNext && _doctors.Count > 0 ? "No, a doctor" : "No";
        return new GuideTurn(prompt, true, true, false, true, null, yes, no);
    }

    private GuideTurn AskKind(string? lead = null)
    {
        _phase = "narrow";
        _pending = null;
        _listKind = null;
        var question = "Who do you want to talk to, your family or a doctor?";
        var prompt = string.IsNullOrWhiteSpace(lead)
            ? $"That's all right, {_firstName}. {question}"
            : $"{lead} {question}";
        return new GuideTurn(prompt, true, true, true, false, null);
    }

    private GuideTurn Call(GuidePerson person)
    {
        if (PhoneNumbers.ToE164(person.Phone) is null)
        {
            var family = _family.FirstOrDefault();
            if (family is not null)
            {
                _pending = family;
                _listKind = "family";
                _phase = "list";
                _index = 0;
                return AskPerson($"I know {person.Name}. I don't have a phone number for them yet. Should I call your family, {family.Name}?", doctorNext: _doctors.Count > 0);
            }

            return new GuideTurn($"I know {person.Name}. I don't have a phone number saved yet, so I can't place that call.", false, true, false, false, null);
        }

        return new($"Wonderful. I'm calling {person.Name} for you now.", false, false, false, false, person);
    }

    private static string FriendName(GuidePerson person)
    {
        if (person.Kind == "family")
            return person.Name;
        if (string.IsNullOrWhiteSpace(person.Detail))
            return person.Name;
        return $"{person.Name}, your {person.Detail.Trim().TrimEnd('.').ToLowerInvariant()}";
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
        if (spoken.Length < 3 || target.Length == 0)
            return false;
        if (spoken.Contains(target, StringComparison.Ordinal))
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
        if (words.Length == 0)
            return false;
        if (words.Any(word => word is "yes" or "yeah" or "yep" or "sure" or "ok" or "okay"))
            return true;
        if (words.Any(word => word is "should" or "who" or "how"))
            return false;
        return words.Length <= 3 && words.Any(word => word is "please" or "call");
    }

    private static bool IsNo(string heard)
    {
        var text = Normalize(heard);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Any(word => word is "no" or "nope" or "not" or "next" or "nah")
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
