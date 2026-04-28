namespace HiveLog.Api.Features.Query.NaturalLanguage;

/// <summary>
/// Configuration for the NL-to-SQL natural language query pipeline (Stufe 2 — Ollama/LLM).
/// Bind from configuration section "HiveLog:NlQuery".
/// </summary>
public class NlQueryOptions
{
    public const string SectionName = "HiveLog:NlQuery";

    /// <summary>
    /// When false, the LLM fallback is disabled entirely. Template-matcher only.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Base URL of the local Ollama instance.
    /// Default: http://localhost:11434
    /// </summary>
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Ollama model to use for SQL generation.
    /// Recommended: "sqlcoder" (Defog SQLCoder, specialised for Text-to-SQL).
    /// Alternative: "deepseek-coder" or "codellama".
    /// Default: "sqlcoder"
    /// </summary>
    public string Model { get; set; } = "sqlcoder";
}
