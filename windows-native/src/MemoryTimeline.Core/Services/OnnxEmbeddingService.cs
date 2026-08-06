using MemoryTimeline.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Fully local embedding service backed by ONNX Runtime and the
/// sentence-transformers/all-MiniLM-L6-v2 model (384 dimensions). This makes
/// the seeded embedding_provider='local' default REAL: Connections / semantic
/// similarity work with no API key and no text ever leaving the machine.
/// <para>
/// The model (model.onnx, ~90 MB) and its BERT vocabulary (vocab.txt) are
/// downloaded once from Hugging Face into
/// %LOCALAPPDATA%\MemoryTimeline\Models\all-MiniLM-L6-v2\ using the same
/// temp-file+move pattern as the Whisper model download, so a partial download
/// can never be mistaken for a valid model. The local_model_path setting
/// overrides the directory when non-empty (for air-gapped installs that copy
/// the files in manually). The embedding_model setting is intentionally NOT
/// read here — the local model identity is fixed; legacy stored values like
/// 'onnx-text-embedding' are ignored.
/// </para>
/// <para>
/// The expensive <see cref="InferenceSession"/> is created once and cached
/// under a semaphore (the Whisper factory pattern); InferenceSession.Run is
/// thread-safe, so concurrent embedding calls share the cached session. Any
/// native/init failure is reported as a <see cref="ConfigurationException"/>
/// ("Local embedding model unavailable: ...") and flips
/// <see cref="IsAvailableAsync"/> to false — never a crash.
/// </para>
/// </summary>
public class OnnxEmbeddingService : IEmbeddingService, IDisposable
{
    private const string ModelFolderName = "all-MiniLM-L6-v2";
    private const string ModelFileName = "model.onnx";
    private const string VocabFileName = "vocab.txt";
    private const string ModelDownloadUrl =
        "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx";
    private const string VocabDownloadUrl =
        "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt";
    private const int Dimension = 384;
    private const string OutputName = "last_hidden_state";

    /// <summary>
    /// Dedicated client for the two one-time model downloads. Static (not a
    /// typed client) because this service is registered as a plain singleton;
    /// the generous timeout covers the ~90 MB model file on slow connections.
    /// </summary>
    private static readonly HttpClient DownloadClient = new() { Timeout = TimeSpan.FromMinutes(15) };

    private readonly ISettingsService _settingsService;
    private readonly ILogger<OnnxEmbeddingService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private InferenceSession? _session;
    private WordPieceTokenizer? _tokenizer;
    private string? _loadedModelDirectory;
    private string? _initFailureMessage;
    private bool _disposed;

    public int EmbeddingDimension => Dimension;
    public string ModelName => ModelFolderName;
    public bool RequiresInternet => false; // offline after the one-time model download
    public string ProviderKey => EmbeddingProviderKeys.Local;

    public OnnxEmbeddingService(
        ISettingsService settingsService,
        ILogger<OnnxEmbeddingService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>
    /// Local embeddings need no API key, so availability only turns false after
    /// an init/native failure has actually been observed (the model download
    /// happens lazily on first use). A later successful init clears the flag.
    /// </summary>
    public Task<bool> IsAvailableAsync()
    {
        return Task.FromResult(!_disposed && _initFailureMessage == null);
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        var embeddings = await GenerateEmbeddingsAsync(new[] { text });
        return embeddings.First();
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts)
    {
        var (session, tokenizer) = await GetOrCreateSessionAsync();

        try
        {
            // DEFERRED: texts are embedded one Run per text (batch size 1, no
            // padding). Padded batched inference would cut per-call overhead
            // for large backfills but complicates the attention mask; the
            // re-embed path is already an explicit, progress-reporting job.
            var results = new List<float[]>();
            foreach (var text in texts)
            {
                results.Add(EmbedSingle(session, tokenizer, text ?? string.Empty));
            }

            _logger.LogInformation("Generated {Count} local embeddings with {Model}", results.Count, ModelName);
            return results;
        }
        catch (Exception ex)
        {
            // Inference failed (native error, unexpected model shape, ...):
            // record the failure so IsAvailableAsync reports it, and surface a
            // clear non-retryable error instead of crashing.
            _initFailureMessage = ex.Message;
            _logger.LogError(ex, "Local ONNX embedding inference failed");
            throw new ConfigurationException($"Local embedding model unavailable: {ex.Message}", ex);
        }
    }

    public double CalculateCosineSimilarity(float[] embedding1, float[] embedding2)
    {
        if (embedding1 == null || embedding2 == null)
        {
            throw new ArgumentNullException(embedding1 == null ? nameof(embedding1) : nameof(embedding2));
        }

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

    public List<SimilarityResult> FindKNearestNeighbors(
        float[] queryEmbedding,
        IEnumerable<(string id, float[] embedding)> candidateEmbeddings,
        int k,
        double threshold = 0.0)
    {
        var similarities = new List<SimilarityResult>();

        if (queryEmbedding == null || queryEmbedding.Length == 0)
        {
            _logger.LogWarning("Query embedding is null or empty; returning no neighbors");
            return similarities;
        }

        foreach (var (id, embedding) in candidateEmbeddings)
        {
            // Skip candidates with missing or dimension-mismatched embeddings rather than
            // aborting the whole search (e.g. corrupt row or an embedding-model change).
            if (embedding == null || embedding.Length != queryEmbedding.Length)
            {
                _logger.LogDebug(
                    "Skipping candidate {Id}: embedding null or dimension mismatch (expected {Expected})",
                    id, queryEmbedding.Length);
                continue;
            }

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

    #region Pooling math (public pure statics — unit-tested without a session)

    /// <summary>
    /// Mean-pools token hidden states over the attention mask: averages the
    /// hidden vectors of the tokens whose mask is 1 (real tokens), ignoring
    /// masked positions. <paramref name="hiddenStates"/> is the flattened
    /// row-major [sequenceLength, hiddenSize] output of the model.
    /// Returns a zero vector when every position is masked.
    /// </summary>
    public static float[] MeanPool(float[] hiddenStates, long[] attentionMask, int sequenceLength, int hiddenSize)
    {
        if (hiddenStates == null) throw new ArgumentNullException(nameof(hiddenStates));
        if (attentionMask == null) throw new ArgumentNullException(nameof(attentionMask));
        if (sequenceLength <= 0 || hiddenSize <= 0)
        {
            throw new ArgumentException("sequenceLength and hiddenSize must be positive");
        }
        if (hiddenStates.Length < sequenceLength * hiddenSize || attentionMask.Length < sequenceLength)
        {
            throw new ArgumentException("hiddenStates/attentionMask are smaller than the declared shape");
        }

        var pooled = new float[hiddenSize];
        var tokenCount = 0;

        for (int t = 0; t < sequenceLength; t++)
        {
            if (attentionMask[t] == 0)
            {
                continue;
            }

            tokenCount++;
            var offset = t * hiddenSize;
            for (int h = 0; h < hiddenSize; h++)
            {
                pooled[h] += hiddenStates[offset + h];
            }
        }

        if (tokenCount == 0)
        {
            return pooled;
        }

        for (int h = 0; h < hiddenSize; h++)
        {
            pooled[h] /= tokenCount;
        }

        return pooled;
    }

    /// <summary>
    /// L2-normalizes the vector in place (unit length), matching the
    /// sentence-transformers post-processing so cosine similarity behaves the
    /// same as with the reference implementation. A zero vector is left as-is.
    /// </summary>
    public static void L2NormalizeInPlace(float[] vector)
    {
        if (vector == null) throw new ArgumentNullException(nameof(vector));

        double sumOfSquares = 0;
        foreach (var value in vector)
        {
            sumOfSquares += (double)value * value;
        }

        if (sumOfSquares <= 0)
        {
            return;
        }

        var norm = Math.Sqrt(sumOfSquares);
        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }
    }

    #endregion

    #region Private Methods

    private float[] EmbedSingle(InferenceSession session, WordPieceTokenizer tokenizer, string text)
    {
        var tokenIds = tokenizer.Encode(text);
        var sequenceLength = tokenIds.Count;

        var idsBuffer = new long[sequenceLength];
        var maskBuffer = new long[sequenceLength];
        var typeBuffer = new long[sequenceLength];
        for (int i = 0; i < sequenceLength; i++)
        {
            idsBuffer[i] = tokenIds[i];
            maskBuffer[i] = 1; // single unpadded sequence: every position is a real token
            typeBuffer[i] = 0;
        }

        var dimensions = new[] { 1, sequenceLength };
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(idsBuffer, dimensions)),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(maskBuffer, dimensions))
        };

        // Some ONNX exports of BERT-family models omit token_type_ids; only
        // feed it when the graph declares the input.
        if (session.InputMetadata.ContainsKey("token_type_ids"))
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(typeBuffer, dimensions)));
        }

        using var outputs = session.Run(inputs);

        var output = outputs.FirstOrDefault(o => o.Name == OutputName) ?? outputs.First();
        var tensor = output.AsTensor<float>();

        if (tensor.Dimensions.Length != 3 || tensor.Dimensions[2] != Dimension)
        {
            throw new InvalidOperationException(
                $"Unexpected model output shape (expected [1, seq, {Dimension}])");
        }

        var flat = output.AsEnumerable<float>().ToArray();
        var pooled = MeanPool(flat, maskBuffer, sequenceLength, Dimension);
        L2NormalizeInPlace(pooled);
        return pooled;
    }

    /// <summary>
    /// Returns the cached session + tokenizer, downloading model files and
    /// loading them on first use (or after local_model_path changed). Follows
    /// the Whisper factory pattern: creation is serialized with a semaphore so
    /// concurrent calls never double-download or double-load.
    /// </summary>
    private async Task<(InferenceSession Session, WordPieceTokenizer Tokenizer)> GetOrCreateSessionAsync()
    {
        var modelDirectory = await ResolveModelDirectoryAsync();

        if (_session != null && _tokenizer != null &&
            string.Equals(_loadedModelDirectory, modelDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return (_session, _tokenizer);
        }

        await _initLock.WaitAsync();
        try
        {
            if (_session != null && _tokenizer != null &&
                string.Equals(_loadedModelDirectory, modelDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return (_session, _tokenizer);
            }

            ObjectDisposedException.ThrowIf(_disposed, this);

            try
            {
                var modelPath = Path.Combine(modelDirectory, ModelFileName);
                var vocabPath = Path.Combine(modelDirectory, VocabFileName);

                await EnsureFileDownloadedAsync(modelPath, ModelDownloadUrl);
                await EnsureFileDownloadedAsync(vocabPath, VocabDownloadUrl);

                var tokenizer = new WordPieceTokenizer(LoadVocabulary(vocabPath));

                _logger.LogInformation("Loading local embedding model from {ModelPath}", modelPath);
                // DEFERRED: default session options = CPU execution provider.
                // Registering the DirectML EP (SessionOptions.AppendExecutionProvider_DML)
                // can throw on machines without DX12 and is not exercised by CI;
                // the 384-dim MiniLM model is fast enough on CPU for this workload.
                var session = new InferenceSession(modelPath);

                _session?.Dispose(); // replaced when local_model_path changed
                _session = session;
                _tokenizer = tokenizer;
                _loadedModelDirectory = modelDirectory;
                _initFailureMessage = null; // recovered (e.g. network is back)

                _logger.LogInformation("Local embedding model loaded and cached");
                return (_session, _tokenizer);
            }
            catch (Exception ex)
            {
                _initFailureMessage = ex.Message;
                _logger.LogError(ex, "Failed to initialize the local ONNX embedding model");
                throw new ConfigurationException($"Local embedding model unavailable: {ex.Message}", ex);
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Resolves the model directory: the local_model_path setting when
    /// non-empty, else %LOCALAPPDATA%\MemoryTimeline\Models\all-MiniLM-L6-v2.
    /// Re-read per call so a settings change applies without a restart.
    /// </summary>
    private async Task<string> ResolveModelDirectoryAsync()
    {
        var overrideDirectory = await _settingsService.GetSettingAsync<string>(SettingKeys.LocalModelPath, string.Empty);
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return overrideDirectory.Trim();
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MemoryTimeline",
            "Models",
            ModelFolderName);
    }

    /// <summary>
    /// Downloads a model file if missing, using EXACTLY the Whisper pattern:
    /// stream to "&lt;target&gt;.download", then move into place, so a
    /// failed/partial download is never mistaken for a valid file on the next run.
    /// </summary>
    private async Task EnsureFileDownloadedAsync(string targetPath, string downloadUrl)
    {
        if (File.Exists(targetPath))
        {
            return;
        }

        _logger.LogInformation("Local embedding model file missing; downloading {Url} to {Path}", downloadUrl, targetPath);

        var tempPath = targetPath + ".download";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            using (var response = await DownloadClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var downloadStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = File.Create(tempPath);
                await downloadStream.CopyToAsync(fileStream);
            }

            // Write to a temp file first, then move into place, so a failed/partial
            // download is never mistaken for a valid model on the next run.
            File.Move(tempPath, targetPath, overwrite: true);

            _logger.LogInformation("Downloaded local embedding model file to {Path}", targetPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download local embedding model file from {Url}", downloadUrl);
            TryDeleteFile(tempPath);
            throw; // wrapped into a ConfigurationException by GetOrCreateSessionAsync
        }
    }

    /// <summary>
    /// Loads vocab.txt (one token per line; the line number IS the token id).
    /// </summary>
    private static Dictionary<string, int> LoadVocabulary(string vocabPath)
    {
        var vocabulary = new Dictionary<string, int>(StringComparer.Ordinal);
        var index = 0;
        foreach (var line in File.ReadLines(vocabPath))
        {
            var token = line.Trim();
            if (token.Length > 0)
            {
                vocabulary[token] = index;
            }
            index++; // count every line (even blanks) to keep ids aligned
        }

        return vocabulary;
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete partial model download at {Path}", path);
        }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session?.Dispose();
        _session = null;
        _initLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
