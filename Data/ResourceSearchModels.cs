namespace MedicalManager.Data;

public sealed record ResourceSection(string Heading, IReadOnlyList<string> Bullets);

public sealed record ResourceAnswer(
    string Query,
    string Title,
    string Summary,
    IReadOnlyList<ResourceSection> Sections,
    IReadOnlyList<string> RelatedTopics,
    IReadOnlyList<ResourceLink> Sources,
    IReadOnlyList<ResourceLink> OfficialResults,
    string ProviderName,
    bool UsedLlm);
