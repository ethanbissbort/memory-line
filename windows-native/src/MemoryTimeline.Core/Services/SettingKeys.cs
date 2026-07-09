namespace MemoryTimeline.Core.Services;

/// <summary>
/// Single source of truth for application setting keys.
/// Writers (SettingsViewModel) and readers (SettingsService, LLM/embedding services)
/// must both use these constants — never string literals — to prevent key drift.
/// Values are snake_case to match the AppDbContext seed rows, except
/// <see cref="AnthropicApiKey"/> which preserves existing user rows.
/// </summary>
public static class SettingKeys
{
    public const string AnthropicApiKey = "ApiKey";            // preserves existing user rows
    public const string LlmProvider = "llm_provider";
    public const string LlmModel = "llm_model";
    public const string DefaultZoomLevel = "default_zoom_level";
    public const string EmbeddingApiKey = "embedding_api_key"; // matches DB seed
    public const string EmbeddingModel = "embedding_model";    // matches DB seed
    public const string Theme = "theme";                       // matches DB seed
}
