namespace MemoryTimeline.Core.Services;

/// <summary>
/// Thrown when a similarity query would compare embeddings of different
/// dimensions — e.g. after switching the embedding provider (OpenAI 1536-dim ↔
/// local 384-dim) without re-embedding. Cosine similarity across dimensions is
/// meaningless, so the operation is refused instead of returning garbage
/// results. The fix is to re-embed all events (Settings → AI &amp; Language
/// Models → "Re-embed all events").
/// </summary>
public class EmbeddingDimensionMismatchException : Exception
{
    /// <summary>Dimension the CURRENT embedding provider produces.</summary>
    public int QueryDimension { get; }

    /// <summary>Dimension of the stored event embeddings.</summary>
    public int StoredDimension { get; }

    public EmbeddingDimensionMismatchException(int queryDimension, int storedDimension)
        : base($"The current embedding provider produces {queryDimension}-dimensional vectors, " +
               $"but the stored event embeddings are {storedDimension}-dimensional. " +
               "Re-embed all events (Settings → AI & Language Models → \"Re-embed all events\") " +
               "to restore similarity features.")
    {
        QueryDimension = queryDimension;
        StoredDimension = storedDimension;
    }
}
