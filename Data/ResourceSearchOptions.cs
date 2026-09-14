namespace MedicalManager.Data;

public sealed class ResourceSearchOptions
{
    public const string SectionName = "ResourceSearch";

    /// <summary>Auto, Groq, OpenAI, Gemini, or Pollinations.</summary>
    public string Provider { get; set; } = "Auto";

    public string? GroqApiKey { get; set; }
    public string GroqModel { get; set; } = "llama-3.1-8b-instant";

    public string? OpenAiApiKey { get; set; }
    public string OpenAiModel { get; set; } = "gpt-4o-mini";

    public string? GeminiApiKey { get; set; }
    public string GeminiModel { get; set; } = "gemini-2.0-flash";
}
