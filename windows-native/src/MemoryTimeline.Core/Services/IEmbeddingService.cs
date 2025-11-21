namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for vector embedding generation.
/// </summary>
public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text);
    Task<double> CalculateSimilarityAsync(float[] embedding1, float[] embedding2);
}

/// <summary>
/// Embedding service with NPU acceleration support.
/// </summary>
public class EmbeddingService : IEmbeddingService
{
    public Task<float[]> GenerateEmbeddingAsync(string text)
    {
        // TODO: Implement using ONNX Runtime with DirectML
        throw new NotImplementedException();
    }

    public Task<double> CalculateSimilarityAsync(float[] embedding1, float[] embedding2)
    {
        // TODO: Implement cosine similarity
        throw new NotImplementedException();
    }
}
