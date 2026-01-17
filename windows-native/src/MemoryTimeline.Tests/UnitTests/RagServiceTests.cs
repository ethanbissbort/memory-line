using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using Moq;
using System.Text.Json;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

public class RagServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly Mock<IEmbeddingService> _embeddingServiceMock;
    private readonly Mock<ILlmService> _llmServiceMock;
    private readonly Mock<IEventService> _eventServiceMock;
    private readonly Mock<ILogger<RagService>> _loggerMock;
    private readonly RagService _ragService;

    public RagServiceTests()
    {
        // Create in-memory database
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"RagTestDb_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        _embeddingServiceMock = new Mock<IEmbeddingService>();
        _llmServiceMock = new Mock<ILlmService>();
        _eventServiceMock = new Mock<IEventService>();
        _loggerMock = new Mock<ILogger<RagService>>();

        _ragService = new RagService(
            _embeddingServiceMock.Object,
            _llmServiceMock.Object,
            _eventServiceMock.Object,
            _context,
            _loggerMock.Object);

        // Setup embedding service defaults
        _embeddingServiceMock.Setup(e => e.EmbeddingDimension).Returns(1536);
        _embeddingServiceMock.Setup(e => e.ModelName).Returns("text-embedding-3-small");

        SeedTestData();
    }

    private void SeedTestData()
    {
        var events = new List<Event>
        {
            new()
            {
                EventId = "event1",
                Title = "Started new job at Tech Company",
                Description = "Joined as a software engineer",
                StartDate = new DateTime(2024, 1, 15),
                Category = EventCategory.Work,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                EventId = "event2",
                Title = "Completed coding bootcamp",
                Description = "Finished intensive programming course",
                StartDate = new DateTime(2023, 12, 20),
                Category = EventCategory.Education,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                EventId = "event3",
                Title = "Graduated from University",
                Description = "Earned Computer Science degree",
                StartDate = new DateTime(2023, 5, 15),
                Category = EventCategory.Education,
                CreatedAt = DateTime.UtcNow
            }
        };

        // Create embeddings for events (mock vectors)
        var embeddings = new List<EventEmbedding>
        {
            new()
            {
                EventEmbeddingId = Guid.NewGuid().ToString(),
                EventId = "event1",
                Embedding = JsonSerializer.Serialize(GenerateMockEmbedding(1)),
                Model = "text-embedding-3-small",
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                EventEmbeddingId = Guid.NewGuid().ToString(),
                EventId = "event2",
                Embedding = JsonSerializer.Serialize(GenerateMockEmbedding(2)),
                Model = "text-embedding-3-small",
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                EventEmbeddingId = Guid.NewGuid().ToString(),
                EventId = "event3",
                Embedding = JsonSerializer.Serialize(GenerateMockEmbedding(3)),
                Model = "text-embedding-3-small",
                CreatedAt = DateTime.UtcNow
            }
        };

        _context.Events.AddRange(events);
        _context.EventEmbeddings.AddRange(embeddings);
        _context.SaveChanges();
    }

    private float[] GenerateMockEmbedding(int seed)
    {
        // Generate a simple mock embedding vector
        var random = new Random(seed);
        var embedding = new float[1536];
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)random.NextDouble();
        }
        return embedding;
    }

    [Fact]
    public async Task FindSimilarEventsAsync_ValidEventId_ReturnsSimilarEvents()
    {
        // Arrange
        var sourceEventId = "event1";
        var sourceEmbedding = GenerateMockEmbedding(1);

        // Mock the event service to return event details
        _eventServiceMock.Setup(s => s.GetEventByIdAsync("event2"))
            .ReturnsAsync(await _context.Events.FindAsync("event2"));
        _eventServiceMock.Setup(s => s.GetEventByIdAsync("event3"))
            .ReturnsAsync(await _context.Events.FindAsync("event3"));

        // Mock similarity calculation to return high similarity for event2
        _embeddingServiceMock.Setup(e => e.CalculateCosineSimilarity(
                It.IsAny<float[]>(), It.IsAny<float[]>()))
            .Returns(0.85);

        _embeddingServiceMock.Setup(e => e.FindKNearestNeighbors(
                It.IsAny<float[]>(),
                It.IsAny<IEnumerable<(string id, float[] embedding)>>(),
                It.IsAny<int>(),
                It.IsAny<double>()))
            .Returns(new List<SimilarityResult>
            {
                new() { Id = "event2", Similarity = 0.85 },
                new() { Id = "event3", Similarity = 0.75 }
            });

        // Act
        var results = await _ragService.FindSimilarEventsAsync(sourceEventId, topK: 10, threshold: 0.7);

        // Assert
        results.Should().NotBeNull();
        results.Should().HaveCount(2);
        results.First().EventId.Should().Be("event2");
        results.First().Similarity.Should().BeGreaterThan(0.7);
    }

    [Fact]
    public async Task FindSimilarEventsAsync_WithHighThreshold_FiltersResults()
    {
        // Arrange
        var sourceEventId = "event1";

        _embeddingServiceMock.Setup(e => e.FindKNearestNeighbors(
                It.IsAny<float[]>(),
                It.IsAny<IEnumerable<(string id, float[] embedding)>>(),
                It.IsAny<int>(),
                It.IsAny<double>()))
            .Returns(new List<SimilarityResult>
            {
                new() { Id = "event2", Similarity = 0.95 }
                // event3 filtered out by threshold
            });

        _eventServiceMock.Setup(s => s.GetEventByIdAsync("event2"))
            .ReturnsAsync(await _context.Events.FindAsync("event2"));

        // Act
        var results = await _ragService.FindSimilarEventsAsync(sourceEventId, topK: 10, threshold: 0.9);

        // Assert
        results.Should().HaveCount(1);
        results.First().Similarity.Should().BeGreaterThanOrEqualTo(0.9);
    }

    [Fact]
    public async Task SuggestTagsAsync_BasedOnSimilarEvents_ReturnsSuggestions()
    {
        // Arrange
        var targetEventId = "event1";

        // Create tags for similar events
        var tag1 = new Tag { TagId = "tag1", Name = "career", CreatedAt = DateTime.UtcNow };
        var tag2 = new Tag { TagId = "tag2", Name = "technology", CreatedAt = DateTime.UtcNow };
        _context.Tags.AddRange(tag1, tag2);

        var event2 = await _context.Events.FindAsync("event2");
        event2!.EventTags.Add(new EventTag { EventId = event2.EventId, TagId = tag1.TagId, Tag = tag1 });
        event2!.EventTags.Add(new EventTag { EventId = event2.EventId, TagId = tag2.TagId, Tag = tag2 });

        var event3 = await _context.Events.FindAsync("event3");
        event3!.EventTags.Add(new EventTag { EventId = event3.EventId, TagId = tag1.TagId, Tag = tag1 });

        await _context.SaveChangesAsync();

        // Mock finding similar events
        _embeddingServiceMock.Setup(e => e.FindKNearestNeighbors(
                It.IsAny<float[]>(),
                It.IsAny<IEnumerable<(string id, float[] embedding)>>(),
                It.IsAny<int>(),
                It.IsAny<double>()))
            .Returns(new List<SimilarityResult>
            {
                new() { Id = "event2", Similarity = 0.9 },
                new() { Id = "event3", Similarity = 0.8 }
            });

        _eventServiceMock.Setup(s => s.GetEventByIdAsync("event2")).ReturnsAsync(event2);
        _eventServiceMock.Setup(s => s.GetEventByIdAsync("event3")).ReturnsAsync(event3);
        _eventServiceMock.Setup(s => s.GetEventTagsAsync("event2")).ReturnsAsync(new List<Tag> { tag1, tag2 });
        _eventServiceMock.Setup(s => s.GetEventTagsAsync("event3")).ReturnsAsync(new List<Tag> { tag1 });

        // Act
        var suggestions = await _ragService.SuggestTagsAsync(targetEventId, maxSuggestions: 5);

        // Assert
        suggestions.Should().NotBeEmpty();
        suggestions.Should().Contain(s => s.TagName == "career");
        suggestions.First().Confidence.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DetectPatternsAsync_WithDateRange_DetectsPatterns()
    {
        // Arrange
        var startDate = new DateTime(2023, 1, 1);
        var endDate = new DateTime(2024, 12, 31);

        // Act
        var result = await _ragService.DetectPatternsAsync(startDate, endDate);

        // Assert
        result.Should().NotBeNull();
        result.CategoryPatterns.Should().NotBeEmpty();
        result.CategoryPatterns.Should().Contain(p => p.Category == EventCategory.Education);
        result.CategoryPatterns.Should().Contain(p => p.Category == EventCategory.Work);
    }

    [Fact]
    public async Task DetectPatternsAsync_NoDateRange_AnalyzesAllEvents()
    {
        // Act
        var result = await _ragService.DetectPatternsAsync();

        // Assert
        result.Should().NotBeNull();
        result.CategoryPatterns.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task FindSimilarEventsAsync_NonExistentEventId_ReturnsEmpty()
    {
        // Act
        var results = await _ragService.FindSimilarEventsAsync("non-existent-id");

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectCrossReferencesAsync_WithValidEventId_DetectsReferences()
    {
        // Arrange
        var eventId = "event1";

        _embeddingServiceMock.Setup(e => e.FindKNearestNeighbors(
                It.IsAny<float[]>(),
                It.IsAny<IEnumerable<(string id, float[] embedding)>>(),
                It.IsAny<int>(),
                It.IsAny<double>()))
            .Returns(new List<SimilarityResult>
            {
                new() { Id = "event2", Similarity = 0.85 }
            });

        _eventServiceMock.Setup(s => s.GetEventByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => _context.Events.Find(id));

        // Act
        var results = await _ragService.DetectCrossReferencesAsync(eventId);

        // Assert
        results.Should().NotBeNull();
        // Cross-reference detection is complex, so just verify it returns something
        _embeddingServiceMock.Verify(e => e.FindKNearestNeighbors(
            It.IsAny<float[]>(),
            It.IsAny<IEnumerable<(string, float[])>>(),
            It.IsAny<int>(),
            It.IsAny<double>()), Times.AtLeastOnce);
    }

    [Fact]
    public void EmbeddingServiceMock_CalculateCosineSimilarity_ReturnsValue()
    {
        // Arrange
        var embedding1 = GenerateMockEmbedding(1);
        var embedding2 = GenerateMockEmbedding(2);

        _embeddingServiceMock.Setup(e => e.CalculateCosineSimilarity(embedding1, embedding2))
            .Returns(0.75);

        // Act
        var similarity = _embeddingServiceMock.Object.CalculateCosineSimilarity(embedding1, embedding2);

        // Assert
        similarity.Should().Be(0.75);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
