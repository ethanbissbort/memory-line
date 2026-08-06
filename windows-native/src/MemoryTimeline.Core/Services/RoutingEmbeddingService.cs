using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Canonical embedding_provider keys and normalization. These keys are also
/// persisted on event_embeddings rows (<c>embedding_provider</c> column) via
/// <see cref="IEmbeddingService.ProviderKey"/>.
/// </summary>
public static class EmbeddingProviderKeys
{
    public const string Local = "local";
    public const string OpenAi = "openai";

    /// <summary>
    /// Normalizes a stored embedding_provider value to a canonical key.
    /// Unknown/blank values (including any legacy model strings like the old
    /// 'onnx-text-embedding' seed) fall back to Local — the seeded default.
    /// </summary>
    public static string Normalize(string? storedValue)
    {
        return storedValue?.Trim().ToLowerInvariant() switch
        {
            OpenAi or "open-ai" or "openai-cloud" => OpenAi,
            _ => Local
        };
    }
}

/// <summary>
/// Singleton <see cref="IEmbeddingService"/> facade that re-reads
/// <see cref="SettingKeys.EmbeddingProvider"/> on EVERY embedding call and
/// delegates to <see cref="OnnxEmbeddingService"/> ("local", the seeded
/// default) or <see cref="OpenAIEmbeddingService"/> ("openai") — so switching
/// providers in Settings applies without a restart.
/// <para>
/// Inner services are resolved per call: OnnxEmbeddingService is a singleton
/// registered as itself; OpenAIEmbeddingService is an HttpClient typed client
/// (transient), so per-call resolution keeps HttpClient lifetimes healthy.
/// The pure math members (cosine similarity, kNN) are provider-independent and
/// delegate to the last-known provider. EmbeddingDimension/ModelName/
/// RequiresInternet/ProviderKey reflect the provider selected by the most
/// recent settings read (primed at construction).
/// </para>
/// </summary>
public class RoutingEmbeddingService : IEmbeddingService
{
    private readonly Func<string, IEmbeddingService> _resolveProvider;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<RoutingEmbeddingService> _logger;
    private volatile string _lastProviderKey = EmbeddingProviderKeys.Local;

    public RoutingEmbeddingService(
        IServiceProvider serviceProvider,
        ISettingsService settingsService,
        ILogger<RoutingEmbeddingService> logger)
        : this(
            key => key == EmbeddingProviderKeys.OpenAi
                ? serviceProvider.GetRequiredService<OpenAIEmbeddingService>()
                : (IEmbeddingService)serviceProvider.GetRequiredService<OnnxEmbeddingService>(),
            settingsService,
            logger)
    {
    }

    /// <summary>
    /// Delegate-resolving constructor (test seam): <paramref name="resolveProvider"/>
    /// receives a canonical <see cref="EmbeddingProviderKeys"/> key.
    /// </summary>
    public RoutingEmbeddingService(
        Func<string, IEmbeddingService> resolveProvider,
        ISettingsService settingsService,
        ILogger<RoutingEmbeddingService> logger)
    {
        _resolveProvider = resolveProvider;
        _settingsService = settingsService;
        _logger = logger;

        // Prime the display properties with the persisted provider (fire and
        // forget: a failure just leaves the seeded default until a call).
        _ = PrimeProviderKeyAsync();
    }

    public int EmbeddingDimension => ResolveForDisplay().EmbeddingDimension;
    public string ModelName => ResolveForDisplay().ModelName;
    public bool RequiresInternet => ResolveForDisplay().RequiresInternet;
    public string ProviderKey => ResolveForDisplay().ProviderKey;

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        var inner = await ResolveCurrentAsync();
        return await inner.GenerateEmbeddingAsync(text);
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts)
    {
        var inner = await ResolveCurrentAsync();
        return await inner.GenerateEmbeddingsAsync(texts);
    }

    public async Task<bool> IsAvailableAsync()
    {
        var inner = await ResolveCurrentAsync();
        return await inner.IsAvailableAsync();
    }

    /// <summary>Pure math — identical for every provider.</summary>
    public double CalculateCosineSimilarity(float[] embedding1, float[] embedding2)
    {
        return ResolveForDisplay().CalculateCosineSimilarity(embedding1, embedding2);
    }

    /// <summary>Pure math — identical for every provider.</summary>
    public List<SimilarityResult> FindKNearestNeighbors(
        float[] queryEmbedding,
        IEnumerable<(string id, float[] embedding)> candidateEmbeddings,
        int k,
        double threshold = 0.0)
    {
        return ResolveForDisplay().FindKNearestNeighbors(queryEmbedding, candidateEmbeddings, k, threshold);
    }

    /// <summary>
    /// Re-reads embedding_provider and resolves the matching inner service.
    /// </summary>
    private async Task<IEmbeddingService> ResolveCurrentAsync()
    {
        var stored = await _settingsService.GetSettingAsync<string>(
            SettingKeys.EmbeddingProvider, EmbeddingProviderKeys.Local);
        var key = EmbeddingProviderKeys.Normalize(stored);

        if (!string.Equals(key, _lastProviderKey, StringComparison.Ordinal))
        {
            _logger.LogInformation("Embedding provider routing switched to '{Provider}'", key);
            _lastProviderKey = key;
        }

        return _resolveProvider(key);
    }

    private IEmbeddingService ResolveForDisplay()
    {
        return _resolveProvider(_lastProviderKey);
    }

    private async Task PrimeProviderKeyAsync()
    {
        try
        {
            var stored = await _settingsService.GetSettingAsync<string>(
                SettingKeys.EmbeddingProvider, EmbeddingProviderKeys.Local);
            _lastProviderKey = EmbeddingProviderKeys.Normalize(stored);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not prime the embedding provider key from settings");
        }
    }
}
