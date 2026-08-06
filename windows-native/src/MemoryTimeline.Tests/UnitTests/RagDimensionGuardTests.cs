using FluentAssertions;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using MemoryTimeline.Tests;
using Moq;
using System.Text.Json;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// F11 dimension-guard tests for RagService: similarity math must never mix
/// embeddings of different dimensions (e.g. OpenAI 1536-dim rows next to local
/// 384-dim rows after a provider switch).
/// </summary>
public class RagDimensionGuardTests : IDisposable
{
    private readonly TestDbContextFactory _contextFactory;
    private readonly AppDbContext _context;
    private readonly Mock<IEmbeddingService> _embeddingServiceMock;
    private readonly Mock<IEventService> _eventServiceMock;
    private readonly Mock<ICrossReferenceRepository> _crossReferenceRepositoryMock;
    private readonly RagService _ragService;

    public RagDimensionGuardTests()
    {
        _contextFactory = TestDbContextFactory.CreateInMemory();
        _context = _contextFactory.CreateDbContext();

        _embeddingServiceMock = new Mock<IEmbeddingService>();
        _eventServiceMock = new Mock<IEventService>();
        _crossReferenceRepositoryMock = new Mock<ICrossReferenceRepository>();

        _ragService = new RagService(
            _embeddingServiceMock.Object,
            _eventServiceMock.Object,
            _contextFactory,
            _crossReferenceRepositoryMock.Object,
            new Mock<ILogger<RagService>>().Object);
    }

    private void SeedEvent(string eventId, string title)
    {
        _context.Events.Add(new Event
        {
            EventId = eventId,
            Title = title,
            StartDate = new DateTime(2024, 1, 15),
            Category = EventCategory.Work,
            CreatedAt = DateTime.UtcNow
        });
    }

    private void SeedEmbedding(string eventId, int dimension, string provider)
    {
        _context.EventEmbeddings.Add(new EventEmbedding
        {
            EmbeddingId = Guid.NewGuid().ToString(),
            EventId = eventId,
            // A small vector is fine: the guard reads the embedding_dimension
            // COLUMN, and kNN is mocked in these tests.
            EmbeddingVector = JsonSerializer.Serialize(new float[] { 0.1f, 0.2f, 0.3f }),
            EmbeddingProvider = provider,
            EmbeddingModel = provider == "local" ? "all-MiniLM-L6-v2" : "text-embedding-3-small",
            EmbeddingDimension = dimension,
            CreatedAt = DateTime.UtcNow
        });
    }

    #region FindSimilarEventsAsync skips mismatched candidates

    [Fact]
    public async Task FindSimilarEventsAsync_SkipsCandidatesWithMismatchedDimension()
    {
        // Arrange - source row is 384-dim; one candidate matches, one is a
        // stale 1536-dim row from the previous provider
        SeedEvent("source", "Source event");
        SeedEvent("match", "Matching-dimension event");
        SeedEvent("stale", "Stale-dimension event");
        SeedEmbedding("source", 384, "local");
        SeedEmbedding("match", 384, "local");
        SeedEmbedding("stale", 1536, "openai");
        _context.SaveChanges();

        List<string>? offeredCandidateIds = null;
        _embeddingServiceMock.Setup(e => e.FindKNearestNeighbors(
                It.IsAny<float[]>(),
                It.IsAny<IEnumerable<(string id, float[] embedding)>>(),
                It.IsAny<int>(),
                It.IsAny<double>()))
            .Callback<float[], IEnumerable<(string id, float[] embedding)>, int, double>(
                (_, candidates, _, _) => offeredCandidateIds = candidates.Select(c => c.id).ToList())
            .Returns(new List<SimilarityResult>());

        // Act
        await _ragService.FindSimilarEventsAsync("source", topK: 10, threshold: 0.0);

        // Assert - only the same-dimension candidate reached the kNN math
        offeredCandidateIds.Should().NotBeNull();
        offeredCandidateIds.Should().Contain("match");
        offeredCandidateIds.Should().NotContain("stale");
    }

    [Fact]
    public async Task FindSimilarEventsAsync_AllCandidatesMatch_NothingSkipped()
    {
        // Arrange
        SeedEvent("source", "Source event");
        SeedEvent("other", "Other event");
        SeedEmbedding("source", 384, "local");
        SeedEmbedding("other", 384, "local");
        _context.SaveChanges();

        List<string>? offeredCandidateIds = null;
        _embeddingServiceMock.Setup(e => e.FindKNearestNeighbors(
                It.IsAny<float[]>(),
                It.IsAny<IEnumerable<(string id, float[] embedding)>>(),
                It.IsAny<int>(),
                It.IsAny<double>()))
            .Callback<float[], IEnumerable<(string id, float[] embedding)>, int, double>(
                (_, candidates, _, _) => offeredCandidateIds = candidates.Select(c => c.id).ToList())
            .Returns(new List<SimilarityResult>());

        // Act
        await _ragService.FindSimilarEventsAsync("source");

        // Assert
        offeredCandidateIds.Should().BeEquivalentTo(new[] { "other" });
    }

    #endregion

    #region SuggestTagsForTextAsync dimension pre-check

    [Fact]
    public async Task SuggestTagsForTextAsync_CrossDimension_ThrowsTypedException()
    {
        // Arrange - stored rows are all 1536-dim but the CURRENT provider
        // produces 384-dim vectors: comparing would be garbage math
        SeedEvent("e1", "Old event");
        SeedEmbedding("e1", 1536, "openai");
        _context.SaveChanges();

        _embeddingServiceMock.Setup(e => e.EmbeddingDimension).Returns(384);
        _embeddingServiceMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>()))
            .ReturnsAsync(new float[384]);

        // Act
        var act = () => _ragService.SuggestTagsForTextAsync("New title", "New description");

        // Assert - typed exception naming both dimensions and pointing to re-embed
        var exception = (await act.Should().ThrowAsync<EmbeddingDimensionMismatchException>()).Which;
        exception.QueryDimension.Should().Be(384);
        exception.StoredDimension.Should().Be(1536);
        exception.Message.Should().Contain("384").And.Contain("1536").And.ContainEquivalentOf("re-embed");

        // The guard keys on the ACTUAL query vector (generated exactly once) -
        // never on the potentially lagging EmbeddingDimension display property.
        _embeddingServiceMock.Verify(e => e.GenerateEmbeddingAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task SuggestTagsForTextAsync_ProviderSwitchWithLaggingDisplayDimension_StillThrowsTypedException()
    {
        // Arrange - the in-session provider-switch scenario: stored rows are
        // 1536-dim (old provider) and the router's EmbeddingDimension display
        // property STILL reports 1536 (it lags until an embedding call), but
        // per-call routing already produces 384-dim vectors. A guard keyed on
        // the display property would pass and then silently return empty
        // suggestions; the guard must instead throw the typed exception.
        SeedEvent("e1", "Old event");
        SeedEmbedding("e1", 1536, "openai");
        _context.SaveChanges();

        _embeddingServiceMock.Setup(e => e.EmbeddingDimension).Returns(1536); // lagging
        _embeddingServiceMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>()))
            .ReturnsAsync(new float[384]); // what the CURRENT provider really returns

        // Act
        var act = () => _ragService.SuggestTagsForTextAsync("New title", "New description");

        // Assert
        var exception = (await act.Should().ThrowAsync<EmbeddingDimensionMismatchException>()).Which;
        exception.QueryDimension.Should().Be(384);
        exception.StoredDimension.Should().Be(1536);
    }

    [Fact]
    public async Task SuggestTagsForTextAsync_MatchingDimension_DoesNotThrow()
    {
        // Arrange - the query vector's length (384) matches the stored rows'
        // embedding_dimension column
        SeedEvent("e1", "Existing event");
        SeedEmbedding("e1", 384, "local");
        _context.SaveChanges();

        _embeddingServiceMock.Setup(e => e.EmbeddingDimension).Returns(384);
        _embeddingServiceMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>()))
            .ReturnsAsync(new float[384]);
        _embeddingServiceMock.Setup(e => e.FindKNearestNeighbors(
                It.IsAny<float[]>(),
                It.IsAny<IEnumerable<(string id, float[] embedding)>>(),
                It.IsAny<int>(),
                It.IsAny<double>()))
            .Returns(new List<SimilarityResult>());

        // Act
        var suggestions = await _ragService.SuggestTagsForTextAsync("New title", null);

        // Assert
        suggestions.Should().BeEmpty();
        _embeddingServiceMock.Verify(e => e.GenerateEmbeddingAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task SuggestTagsForTextAsync_NoStoredEmbeddings_DoesNotThrow()
    {
        // Arrange - an empty archive must not trip the guard
        _embeddingServiceMock.Setup(e => e.EmbeddingDimension).Returns(384);
        _embeddingServiceMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });
        _embeddingServiceMock.Setup(e => e.FindKNearestNeighbors(
                It.IsAny<float[]>(),
                It.IsAny<IEnumerable<(string id, float[] embedding)>>(),
                It.IsAny<int>(),
                It.IsAny<double>()))
            .Returns(new List<SimilarityResult>());

        // Act
        var suggestions = await _ragService.SuggestTagsForTextAsync("Anything", null);

        // Assert
        suggestions.Should().BeEmpty();
    }

    #endregion

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
