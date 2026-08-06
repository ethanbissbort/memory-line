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
/// Tests for IEventService.ReEmbedAllAsync (F11): regenerates every event's
/// embedding with the CURRENT provider, deleting stale-dimension rows first so
/// a cancelled run never leaves a cross-dimension mix, and stamps rows with the
/// active provider's real identity (ProviderKey) instead of a hard-coded string.
/// </summary>
public class ReEmbedAllAsyncTests : IDisposable
{
    private readonly TestDbContextFactory _contextFactory;
    private readonly AppDbContext _context;
    private readonly Mock<IEmbeddingService> _embeddingServiceMock;
    private readonly EventService _eventService;

    private static readonly float[] LocalVector = { 0.1f, 0.2f, 0.3f, 0.4f };

    public ReEmbedAllAsyncTests()
    {
        _contextFactory = TestDbContextFactory.CreateInMemory();
        _context = _contextFactory.CreateDbContext();

        // The "current provider" for these tests: local, 4-dim vectors
        _embeddingServiceMock = new Mock<IEmbeddingService>();
        _embeddingServiceMock.Setup(e => e.EmbeddingDimension).Returns(LocalVector.Length);
        _embeddingServiceMock.Setup(e => e.ModelName).Returns("all-MiniLM-L6-v2");
        _embeddingServiceMock.Setup(e => e.ProviderKey).Returns(EmbeddingProviderKeys.Local);
        _embeddingServiceMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>()))
            .ReturnsAsync(LocalVector);

        _eventService = new EventService(
            new EventRepository(_contextFactory),
            _contextFactory,
            new Mock<ILogger<EventService>>().Object,
            _embeddingServiceMock.Object);
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
            EmbeddingVector = JsonSerializer.Serialize(new float[] { 9f, 9f }),
            EmbeddingProvider = provider,
            EmbeddingModel = "text-embedding-3-small",
            EmbeddingDimension = dimension,
            CreatedAt = DateTime.UtcNow
        });
    }

    [Fact]
    public async Task ReEmbedAllAsync_RegeneratesEveryEventWithCurrentProvider()
    {
        // Arrange - three events: one with a stale 1536-dim OpenAI row, one
        // with a same-dimension row, one with no embedding at all
        SeedEvent("e1", "Event one");
        SeedEvent("e2", "Event two");
        SeedEvent("e3", "Event three");
        SeedEmbedding("e1", 1536, "openai");
        SeedEmbedding("e2", LocalVector.Length, "openai");
        _context.SaveChanges();

        // Act
        var count = await _eventService.ReEmbedAllAsync();

        // Assert
        count.Should().Be(3);

        using var verifyContext = _contextFactory.CreateDbContext();
        var rows = verifyContext.EventEmbeddings.ToList();
        rows.Should().HaveCount(3);
        rows.Select(r => r.EventId).Should().BeEquivalentTo(new[] { "e1", "e2", "e3" });
        rows.Should().OnlyContain(r => r.EmbeddingDimension == LocalVector.Length);
        rows.Should().OnlyContain(r => r.EmbeddingProvider == EmbeddingProviderKeys.Local);
        rows.Should().OnlyContain(r => r.EmbeddingModel == "all-MiniLM-L6-v2");

        // Vectors were actually regenerated with the current provider
        var vector = JsonSerializer.Deserialize<float[]>(rows.First().EmbeddingVector);
        vector.Should().Equal(LocalVector);
    }

    [Fact]
    public async Task ReEmbedAllAsync_ReportsProgressFromZeroToTotal()
    {
        // Arrange
        SeedEvent("e1", "Event one");
        SeedEvent("e2", "Event two");
        _context.SaveChanges();

        var reports = new List<(int done, int total)>();
        // A raw IProgress implementation avoids Progress<T>'s SynchronizationContext posting
        var progress = new Mock<IProgress<(int done, int total)>>();
        progress.Setup(p => p.Report(It.IsAny<(int done, int total)>()))
            .Callback<(int done, int total)>(reports.Add);

        // Act
        await _eventService.ReEmbedAllAsync(progress.Object);

        // Assert
        reports.Should().Equal((0, 2), (1, 2), (2, 2));
    }

    [Fact]
    public async Task ReEmbedAllAsync_AlreadyCancelled_ThrowsAndEmbedsNothing()
    {
        // Arrange
        SeedEvent("e1", "Event one");
        _context.SaveChanges();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = () => _eventService.ReEmbedAllAsync(progress: null, ct: cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        _embeddingServiceMock.Verify(e => e.GenerateEmbeddingAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ReEmbedAllAsync_NoEvents_ReturnsZero()
    {
        // Act
        var count = await _eventService.ReEmbedAllAsync();

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_EventWithExistingRow_ReplacesInsteadOfDuplicating()
    {
        // Arrange - event_id carries a unique index, so regenerating must
        // replace the previous row (upsert), not add a second one
        SeedEvent("e1", "Event one");
        SeedEmbedding("e1", 1536, "openai");
        _context.SaveChanges();

        // Act
        await _eventService.GenerateEmbeddingAsync("e1");

        // Assert
        using var verifyContext = _contextFactory.CreateDbContext();
        var rows = verifyContext.EventEmbeddings.Where(ee => ee.EventId == "e1").ToList();
        rows.Should().ContainSingle();
        rows[0].EmbeddingProvider.Should().Be(EmbeddingProviderKeys.Local);
        rows[0].EmbeddingDimension.Should().Be(LocalVector.Length);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
