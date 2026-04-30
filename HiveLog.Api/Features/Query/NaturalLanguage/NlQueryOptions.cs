namespace HiveLog.Api.Features.Query.NaturalLanguage;

/// <summary>
/// Configuration for the NL-to-SQL pipeline (Stufe 2 — OpenAI Chat Completions).
/// Bind from configuration section "HiveLog:NlQuery".
/// </summary>
public class NlQueryOptions
{
    public const string SectionName = "HiveLog:NlQuery";

    /// <summary>When false, the LLM fallback is disabled entirely. Template-matcher only.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>OpenAI API key.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>OpenAI-compatible base URL. Default: https://api.openai.com</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com";

    /// <summary>
    /// Model to use for SQL generation. Admin-only, rare calls — use a capable model.
    /// Default: gpt-4.1
    /// </summary>
    public string Model { get; set; } = "gpt-4.1";

    /// <summary>Request timeout in seconds. Default: 30</summary>
    public int TimeoutSeconds { get; set; } = 30;
}
