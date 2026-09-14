using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace MedicalManager.Data;

public sealed class ResourceSearchService(
    IHttpClientFactory httpFactory,
    IOptions<ResourceSearchOptions> options,
    IConfiguration configuration,
    ILogger<ResourceSearchService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<ResourceAnswer> SearchAsync(string question, CancellationToken cancellationToken = default)
    {
        var query = NormalizeQuery(question);
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Enter a question to search.");
        }

        var references = await GatherReferencesAsync(query, cancellationToken);
        var llm = await TryCompleteWithLlmAsync(query, references, cancellationToken);

        if (llm is not null)
        {
            return Merge(query, llm, references, usedLlm: true);
        }

        return BuildFallbackAnswer(query, references);
    }

    private static string NormalizeQuery(string question)
    {
        var trimmed = question.Trim();
        if (trimmed.Length > 500)
        {
            trimmed = trimmed[..500];
        }

        return Regex.Replace(trimmed, @"\s+", " ");
    }

    private async Task<List<ResourceLink>> GatherReferencesAsync(string query, CancellationToken cancellationToken)
    {
        var medlineTask = SearchMedlinePlusAsync(query, cancellationToken);
        var wikiTask = SearchWikipediaAsync(query, cancellationToken);
        await Task.WhenAll(medlineTask, wikiTask);

        var combined = new List<ResourceLink>();
        AddUnique(combined, medlineTask.Result);
        AddUnique(combined, wikiTask.Result);
        return combined;
    }

    private async Task<IReadOnlyList<ResourceLink>> SearchMedlinePlusAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpFactory.CreateClient("ResourceSearch");
            var url = $"https://wsearch.nlm.nih.gov/ws/query?db=healthTopics&term={Uri.EscapeDataString(query)}";
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var xml = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
            var links = new List<ResourceLink>();

            foreach (var document in xml.Descendants("document").Take(6))
            {
                var title = ContentValue(document, "title");
                var href = ContentValue(document, "url");
                var summary = ContentValue(document, "FullSummary")
                    ?? ContentValue(document, "snippet")
                    ?? ContentValue(document, "altTitle");

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                links.Add(new ResourceLink(
                    HtmlPlain(title),
                    href.Trim(),
                    Truncate(HtmlPlain(summary ?? title), 500)));
            }

            return links;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "MedlinePlus search failed.");
            return [];
        }
    }

    private async Task<IReadOnlyList<ResourceLink>> SearchWikipediaAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpFactory.CreateClient("ResourceSearch");
            var url =
                "https://en.wikipedia.org/w/api.php?action=query&list=search&format=json&utf8=1" +
                $"&srlimit=4&srsearch={Uri.EscapeDataString(query)}";
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("query", out var queryEl) ||
                !queryEl.TryGetProperty("search", out var search) ||
                search.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var links = new List<ResourceLink>();
            foreach (var hit in search.EnumerateArray())
            {
                var title = hit.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
                var snippet = hit.TryGetProperty("snippet", out var snippetEl) ? snippetEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var pageUrl = "https://en.wikipedia.org/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_'));
                links.Add(new ResourceLink(
                    title,
                    pageUrl,
                    Truncate(HtmlPlain(snippet ?? title), 400)));
            }

            return links;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Wikipedia search failed.");
            return [];
        }
    }

    private static string? ContentValue(XElement document, string name) =>
        document.Elements("content")
            .FirstOrDefault(e => string.Equals((string?)e.Attribute("name"), name, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    private static void AddUnique(List<ResourceLink> target, IReadOnlyList<ResourceLink> incoming)
    {
        foreach (var link in incoming)
        {
            if (target.Any(existing =>
                    string.Equals(existing.Url, link.Url, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(existing.Title, link.Title, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            target.Add(link);
        }
    }

    private async Task<LlmPayload?> TryCompleteWithLlmAsync(
        string query,
        IReadOnlyList<ResourceLink> references,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var groqKey = FirstNonEmpty(settings.GroqApiKey, configuration["GROQ_API_KEY"]);
        var openAiKey = FirstNonEmpty(settings.OpenAiApiKey, configuration["OPENAI_API_KEY"]);
        var geminiKey = FirstNonEmpty(settings.GeminiApiKey, configuration["GEMINI_API_KEY"]);
        var provider = (settings.Provider ?? "Auto").Trim();

        var attempts = new List<Func<CancellationToken, Task<LlmPayload?>>>();

        void QueueGroq()
        {
            if (!string.IsNullOrWhiteSpace(groqKey))
            {
                attempts.Add(ct => CompleteOpenAiCompatibleAsync(
                    "https://api.groq.com/openai/v1/chat/completions",
                    groqKey,
                    settings.GroqModel,
                    "Groq",
                    query,
                    references,
                    ct));
            }
        }

        void QueueOpenAi()
        {
            if (!string.IsNullOrWhiteSpace(openAiKey))
            {
                attempts.Add(ct => CompleteOpenAiCompatibleAsync(
                    "https://api.openai.com/v1/chat/completions",
                    openAiKey,
                    settings.OpenAiModel,
                    "ChatGPT",
                    query,
                    references,
                    ct));
            }
        }

        void QueueGemini()
        {
            if (!string.IsNullOrWhiteSpace(geminiKey))
            {
                attempts.Add(ct => CompleteGeminiAsync(geminiKey!, settings.GeminiModel, query, references, ct));
            }
        }

        void QueuePollinations() =>
            attempts.Add(ct => CompletePollinationsAsync(query, references, ct));

        if (provider.Equals("Groq", StringComparison.OrdinalIgnoreCase))
        {
            QueueGroq();
        }
        else if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) ||
                 provider.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase))
        {
            QueueOpenAi();
        }
        else if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            QueueGemini();
        }
        else if (provider.Equals("Pollinations", StringComparison.OrdinalIgnoreCase))
        {
            QueuePollinations();
        }
        else
        {
            QueueGroq();
            QueueOpenAi();
            QueueGemini();
            QueuePollinations();
        }

        foreach (var attempt in attempts)
        {
            try
            {
                var result = await attempt(cancellationToken);
                if (result is not null)
                {
                    return result;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "LLM provider attempt failed.");
            }
        }

        return null;
    }

    private async Task<LlmPayload?> CompleteOpenAiCompatibleAsync(
        string endpoint,
        string apiKey,
        string model,
        string providerName,
        string query,
        IReadOnlyList<ResourceLink> references,
        CancellationToken cancellationToken)
    {
        var client = httpFactory.CreateClient("ResourceSearch");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var body = new
        {
            model,
            temperature = 0.2,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt() },
                new { role = "user", content = UserPrompt(query, references) }
            }
        };
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("{Provider} returned {Status}: {Body}", providerName, (int)response.StatusCode, Truncate(text, 200));
            return null;
        }

        using var doc = JsonDocument.Parse(text);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return ParsePayload(content, providerName);
    }

    private async Task<LlmPayload?> CompleteGeminiAsync(
        string apiKey,
        string model,
        string query,
        IReadOnlyList<ResourceLink> references,
        CancellationToken cancellationToken)
    {
        var client = httpFactory.CreateClient("ResourceSearch");
        var endpoint =
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";
        var body = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = SystemPrompt() + "\n\n" + UserPrompt(query, references) }
                    }
                }
            },
            generationConfig = new { temperature = 0.2, responseMimeType = "application/json" }
        };

        using var response = await client.PostAsJsonAsync(endpoint, body, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Gemini returned {Status}: {Body}", (int)response.StatusCode, Truncate(text, 200));
            return null;
        }

        using var doc = JsonDocument.Parse(text);
        var content = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        return ParsePayload(content, "Gemini");
    }

    private async Task<LlmPayload?> CompletePollinationsAsync(
        string query,
        IReadOnlyList<ResourceLink> references,
        CancellationToken cancellationToken)
    {
        var client = httpFactory.CreateClient("ResourceSearch");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://text.pollinations.ai/openai");
        var body = new
        {
            model = "openai",
            jsonMode = true,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt() },
                new { role = "user", content = UserPrompt(query, references) }
            }
        };
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Pollinations returned {Status}", (int)response.StatusCode);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0)
            {
                var content = choices[0].GetProperty("message").GetProperty("content").GetString();
                var parsed = ParsePayload(content, "Pollinations");
                if (parsed is not null)
                {
                    return parsed;
                }
            }
        }
        catch (JsonException)
        {
            // Response may already be the JSON payload.
        }

        return ParsePayload(text, "Pollinations");
    }

    private static string SystemPrompt() =>
        """
        You are a health information assistant for a patient app called MedicalManager.
        Use the provided reference snippets when they are relevant.
        Do not diagnose, prescribe, or invent specific lab values, dosages, or personal medical advice.
        Encourage the reader to confirm details with their own clinician.
        Reply with JSON only, matching this schema:
        {
          "title": "short title",
          "summary": "2-4 sentence overview in plain language",
          "sections": [
            { "heading": "section name", "bullets": ["point"] }
          ],
          "relatedTopics": ["follow-up question"],
          "sources": [{ "title": "name", "url": "https://...", "description": "why it helps" }]
        }
        Include 3-5 sections such as Overview, What it means, Practical tips, and When to talk to a clinician.
        Prefer official sources (CDC, NIH, MedlinePlus, ADA, NKF, USDA) in the sources list.
        """;

    private static string UserPrompt(string query, IReadOnlyList<ResourceLink> references)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Question: {query}");
        sb.AppendLine();
        sb.AppendLine("Reference snippets:");
        if (references.Count == 0)
        {
            sb.AppendLine("(none available — be conservative and say what is generally known.)");
        }
        else
        {
            var index = 1;
            foreach (var item in references.Take(8))
            {
                sb.AppendLine($"{index}. {item.Title} — {item.Url}");
                sb.AppendLine($"   {item.Description}");
                index++;
            }
        }

        return sb.ToString();
    }

    private static LlmPayload? ParsePayload(string? content, string providerName)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var json = ExtractJson(content);
        if (json is null)
        {
            return null;
        }

        var parsed = JsonSerializer.Deserialize<LlmPayload>(json, JsonOptions);
        if (parsed is null || (string.IsNullOrWhiteSpace(parsed.Summary) && string.IsNullOrWhiteSpace(parsed.Title)))
        {
            return null;
        }

        parsed.ProviderName = providerName;
        return parsed;
    }

    private static string? ExtractJson(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNl = trimmed.IndexOf('\n');
            if (firstNl > 0)
            {
                trimmed = trimmed[(firstNl + 1)..];
            }

            var fence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                trimmed = trimmed[..fence];
            }

            trimmed = trimmed.Trim();
        }

        if (trimmed.StartsWith('{'))
        {
            return trimmed;
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return trimmed[start..(end + 1)];
        }

        return null;
    }

    private ResourceAnswer Merge(string query, LlmPayload llm, IReadOnlyList<ResourceLink> references, bool usedLlm)
    {
        var sections = (llm.Sections ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Heading) && s.Bullets is { Count: > 0 })
            .Select(s => new ResourceSection(s.Heading!.Trim(), s.Bullets!.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.Trim()).ToList()))
            .ToList();

        if (sections.Count == 0 && !string.IsNullOrWhiteSpace(llm.Summary))
        {
            sections.Add(new ResourceSection("Overview", [llm.Summary.Trim()]));
        }

        var sources = new List<ResourceLink>();
        if (llm.Sources is not null)
        {
            foreach (var source in llm.Sources)
            {
                if (string.IsNullOrWhiteSpace(source.Title) || !IsHttpsUrl(source.Url))
                {
                    continue;
                }

                sources.Add(new ResourceLink(source.Title.Trim(), source.Url!.Trim(), source.Description?.Trim() ?? source.Title.Trim()));
            }
        }

        AddUnique(sources, references);

        var related = (llm.RelatedTopics ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        return new ResourceAnswer(
            query,
            string.IsNullOrWhiteSpace(llm.Title) ? query : llm.Title.Trim(),
            llm.Summary?.Trim() ?? "",
            sections,
            related,
            sources,
            references,
            usedLlm ? (llm.ProviderName ?? "LLM") : "Library search",
            usedLlm);
    }

    private static ResourceAnswer BuildFallbackAnswer(string query, IReadOnlyList<ResourceLink> references)
    {
        if (references.Count == 0)
        {
            return new ResourceAnswer(
                query,
                query,
                "No live results were returned. Try a shorter question, or open one of the curated diabetes, kidney, or nutrition links on this page.",
                [],
                ResourceCatalog.SuggestedQuestions.ToList(),
                [],
                [],
                "Library search",
                false);
        }

        var lead = references[0];
        var sections = references
            .Take(6)
            .Select(r => new ResourceSection(r.Title, SplitIntoBullets(r.Description)))
            .ToList();

        return new ResourceAnswer(
            query,
            string.IsNullOrWhiteSpace(lead.Title) ? query : lead.Title,
            string.IsNullOrWhiteSpace(lead.Description)
                ? "Here are organized results from MedlinePlus and Wikipedia. This is general information, not a diagnosis."
                : lead.Description,
            sections,
            ResourceCatalog.SuggestedQuestions.Take(4).ToList(),
            references.ToList(),
            references,
            "MedlinePlus + Wikipedia",
            false);
    }

    private static IReadOnlyList<string> SplitIntoBullets(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return ["Open this source for a full explanation."];
        }

        var parts = Regex.Split(description, @"(?<=[.!?])\s+")
            .Where(p => p.Length > 20)
            .Take(4)
            .ToList();

        return parts.Count > 0 ? parts : [description];
    }

    private static bool IsHttpsUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static string HtmlPlain(string value)
    {
        var withoutTags = Regex.Replace(value, "<.*?>", " ");
        return Regex.Replace(System.Net.WebUtility.HtmlDecode(withoutTags), @"\s+", " ").Trim();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)].TrimEnd() + "…";

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private sealed class LlmPayload
    {
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public List<LlmSection>? Sections { get; set; }
        public List<string>? RelatedTopics { get; set; }
        public List<LlmSource>? Sources { get; set; }

        [JsonIgnore]
        public string? ProviderName { get; set; }
    }

    private sealed class LlmSection
    {
        public string? Heading { get; set; }
        public List<string>? Bullets { get; set; }
    }

    private sealed class LlmSource
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Description { get; set; }
    }
}
