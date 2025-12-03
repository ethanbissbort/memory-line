using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Embedding service using OpenAI Embeddings API.
/// </summary>
public class OpenAIEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIEmbeddingService> _logger;
    private readonly string _apiKey;
    private readonly string _model;

    private const string OpenAIApiUrl = "https://api.openai.com/v1/embeddings";
    private const string DefaultModel = "text-embedding-3-small"; // 1536 dimensions
    private const int DefaultDimension = 1536;

    public int EmbeddingDimension => DefaultDimension;
    public string ModelName => _model;
    public bool RequiresInternet => true;

    public OpenAIEmbeddingService(
        HttpClient httpClient,
        ILogger<OpenAIEmbeddingService> logger,
        ISettingsService settingsService)
    {
        _httpClient = httpClient;
        _logger = logger;

        // Get API key from settings
        _apiKey = settingsService.GetSettingAsync<string>("OpenAIApiKey", string.Empty).GetAwaiter().GetResult() ?? string.Empty;
        _model = settingsService.GetSettingAsync<string>("OpenAIEmbeddingModel", DefaultModel).GetAwaiter().GetResult() ?? DefaultModel;

        // Configure HttpClient
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }
    }

    /// <summary>
    /// Generates an embedding for a single text.
    /// </summary>
    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        var embeddings = await GenerateEmbeddingsAsync(new[] { text });
        return embeddings.First();
    }

    /// <summary>
    /// Generates embeddings for multiple texts in a batch.
    /// </summary>
    public async Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                throw new InvalidOperationException("OpenAI API key not configured");
            }

            var textList = texts.ToList();
            _logger.LogInformation("Generating embeddings for {Count} texts using {Model}", textList.Count, _model);

            var request = new OpenAIEmbeddingRequest
            {
                Model = _model,
                Input = textList
            };

            var response = await _httpClient.PostAsJsonAsync(OpenAIApiUrl, request);
            response.EnsureSuccessStatusCode();

            var apiResponse = await response.Content.ReadFromJsonAsync<OpenAIEmbeddingResponse>();

            if (apiResponse?.Data == null || apiResponse.Data.Count == 0)
            {
                throw new Exception("Empty response from OpenAI Embeddings API");
            }

            var embeddings = apiResponse.Data
                .OrderBy(d => d.Index)
                .Select(d => d.Embedding)
                .ToList();

            _logger.LogInformation("Successfully generated {Count} embeddings", embeddings.Count);
            return embeddings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embeddings");
            throw;
        }
    }

    /// <summary>
    /// Calculates cosine similarity between two embeddings.
    /// </summary>
    public double CalculateCosineSimilarity(float[] embedding1, float[] embedding2)
    {
        if (embedding1.Length != embedding2.Length)
        {
            throw new ArgumentException("Embeddings must have the same dimension");
        }

        double dotProduct = 0;
        double norm1 = 0;
        double norm2 = 0;

        for (int i = 0; i < embedding1.Length; i++)
        {
            dotProduct += embedding1[i] * embedding2[i];
            norm1 += embedding1[i] * embedding1[i];
            norm2 += embedding2[i] * embedding2[i];
        }

        if (norm1 == 0 || norm2 == 0)
        {
            return 0;
        }

        return dotProduct / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
    }

    /// <summary>
    /// Finds K nearest neighbors using cosine similarity.
    /// </summary>
    public List<SimilarityResult> FindKNearestNeighbors(
        float[] queryEmbedding,
        IEnumerable<(string id, float[] embedding)> candidateEmbeddings,
        int k,
        double threshold = 0.0)
    {
        var similarities = new List<SimilarityResult>();

        foreach (var (id, embedding) in candidateEmbeddings)
        {
            var similarity = CalculateCosineSimilarity(queryEmbedding, embedding);

            if (similarity >= threshold)
            {
                similarities.Add(new SimilarityResult
                {
                    Id = id,
                    Similarity = similarity
                });
            }
        }

        return similarities
            .OrderByDescending(s => s.Similarity)
            .Take(k)
            .ToList();
    }

    #region API DTOs

    private class OpenAIEmbeddingRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("input")]
        public List<string> Input { get; set; } = new();
    }

    private class OpenAIEmbeddingResponse
    {
        [JsonPropertyName("object")]
        public string? Object { get; set; }

        [JsonPropertyName("data")]
        public List<EmbeddingData> Data { get; set; } = new();

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("usage")]
        public Usage? Usage { get; set; }
    }

    private class EmbeddingData
    {
        [JsonPropertyName("object")]
        public string? Object { get; set; }

        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = Array.Empty<float>();

        [JsonPropertyName("index")]
        public int Index { get; set; }
    }

    private class Usage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }

    #endregion
}
