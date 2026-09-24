namespace MedicalManager.Data.Notifications;

/// <summary>
/// Hand-checked IPA for names that English spelling rules mispronounce.
/// Symbols stay inside the set Amazon Polly accepts on a phone call.
/// </summary>
public static class NamePronunciations
{
    private static readonly Dictionary<string, string> Map = Build();

    public static string? TryGet(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var key = new string(name.Trim().ToLowerInvariant().Where(c => c is >= 'a' and <= 'z').ToArray());
        return key.Length == 0 ? null : Map.GetValueOrDefault(key);
    }

    private static Dictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in Entries.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith('#'))
                continue;

            var space = line.IndexOf(' ');
            if (space <= 0)
                continue;

            map[line[..space]] = line[(space + 1)..].Trim();
        }

        return map;
    }

    private const string Entries = """
        aaliyah ɑˈliə
        aisha ˈaɪʃə
        alejandro ɑleˈhɑndro
        alejandro ɑleˈhɑndro
        alicia əˈliʃə
        amit ˈɑmɪt
        anita əˈnitə
        andrea ˈændriə
        angela ˈændʒələ
        angelica ænˈdʒɛlɪkə
        anthony ˈænθəni
        aoife ˈifə
        astrid ˈæstrɪd
        barbara ˈbɑrbərə
        benjamin ˈbɛndʒəmɪn
        bjorn bjɔrn
        carmen ˈkɑrmɛn
        catherine ˈkæθərɪn
        chavez ˈtʃɑvɛz
        charlotte ˈʃɑrlət
        cheryl ˈʃɛrəl
        chelsea ˈtʃɛlsi
        chen tʃɛn
        cheng tʃɛŋ
        choi tʃɔɪ
        christine krɪˈstin
        christopher ˈkrɪstəfər
        cohen ˈkoʊən
        cruz kruz
        cynthia sɪnˈθiə
        deepak ˈdipɑk
        desai dɛˈsaɪ
        diego diˈeɪgo
        diaz ˈdiɑs
        dmitri dmˈitri
        elizabeth ɪˈlɪzəbəθ
        elena ɛˈlɛnə
        eugene ˈjudʒin
        fatima ˈfɑtɪmə
        flores ˈflɔrɛs
        francesca frænˈtʃɛskə
        garcia gɑrˈsiə
        geeta ˈgitə
        gianna dʒiˈɑnə
        gita ˈgitə
        gomez ˈgoʊmɛz
        gonzalez gɑnˈzɑlɛz
        guadalupe gwɑdəˈlupɛ
        gupta ˈgʊptə
        gutierrez guˈtiɛrɛz
        hernandez hɛrˈnɑndɛz
        herrera ɛˈrɛrə
        huang hwɑŋ
        hussain huˈseɪn
        iyer ˈaɪər
        javier hɑviˈɛr
        jesus heˈsus
        jimenez hiˈmɛnɛz
        joaquin hwɑˈkin
        jose hoʊˈzeɪ
        joshi ˈdʒoʊʃi
        juan hwɑn
        katherine ˈkæθərɪn
        kathryn ˈkæθrɪn
        kaur kɑr
        keisha ˈkiʃə
        khan kɑn
        kim kɪm
        knight naɪt
        krishna ˈkrɪʃnə
        lakshmi ˈlɑkʃmi
        lopez ˈloʊpɛz
        lucia ˈluʃə
        luis luˈis
        mahesh məˈheɪʃ
        maria məˈriə
        mario ˈmɑrio
        martinez mɑrˈtinɛz
        mcdonald məkˈdɑnəld
        megan ˈmeɪgən
        meghan ˈmeɪgən
        mehta ˈmɛtə
        michael ˈmaɪkəl
        michelle mɪˈʃɛl
        miguel mɪˈgɛl
        mohammed moʊˈhɑmɛd
        mohammad moʊˈhɑməd
        morales mɔˈrɑlɛs
        muhammad moʊˈhɑməd
        nair ˈnaɪər
        naveen nəˈvin
        navin ˈnɑvɪn
        neha ˈneɪhə
        nguyen nwɪn
        niamh niv
        olsen ˈoʊlsən
        olga ˈoʊlgə
        ortiz ɔrˈtiz
        park pɑrk
        patel pəˈtɛl
        perez pɛˈrɛz
        pham fæm
        philip ˈfɪlɪp
        phillip ˈfɪlɪp
        pooja ˈpuʒə
        priya ˈpriə
        quinn kwɪn
        raj rɑʒ
        ramirez rəˈmɪrɛz
        ramesh rəˈmɛʃ
        rao raʊ
        ravi ˈrɑvi
        reddy ˈrɛdi
        reyes ˈreɪɛs
        rivera rɪˈvɛrə
        rodriguez rɑˈdrigɛz
        rosa ˈroʊsə
        sanchez ˈsɑntʃɛz
        sanjay ˈsɑndʒeɪ
        sean ʃɔn
        sergio ˈsɛrhio
        shah ʃɑ
        sharma ˈʃɑrmə
        shaun ʃɔn
        shawn ʃɔn
        singh sɪŋ
        siobhan ʃɪˈvɔn
        sofia soʊˈfiə
        stephen ˈstivən
        steven ˈstivən
        suresh suˈrɛʃ
        thomas ˈtɑməs
        torres ˈtɔrɛs
        tran trɑn
        vijay ˈvɪdʒeɪ
        wang wɑŋ
        wright raɪt
        xavier ˈzeɪviər
        ximena hiˈmɛnə
        yang jɑŋ
        zhang dʒɑŋ
        zhao dʒaʊ
        """;
}
