using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using MemoryTimeline.Tests;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Tests for <see cref="MediaService"/>: managed-tree copies, hash dedupe,
/// thumbnails (mocked <see cref="IThumbnailGenerator"/>), removal, captions,
/// reordering, probing, batched counts, and which of those reach the sync feed
/// as a timeline projection. Uses EF InMemory + the real repository; the media
/// root is a per-test temp directory injected through the service's
/// mediaRootOverride parameter.
///
/// The shared <c>_mediaService</c> fixture is deliberately built WITHOUT a
/// projection publisher - the shape every pre-existing caller uses - so every
/// test outside the projection region doubles as proof that the null publisher
/// is inert.
/// </summary>
public class MediaServiceTests : IDisposable
{
    private readonly TestDbContextFactory _contextFactory;
    private readonly EventMediaRepository _mediaRepository;
    private readonly Mock<IThumbnailGenerator> _thumbnailMock;
    private readonly Mock<ILogger<MediaService>> _loggerMock;
    private readonly MediaService _mediaService;
    private readonly string _mediaRoot;
    private readonly string _sourceDir;

    public MediaServiceTests()
    {
        _contextFactory = TestDbContextFactory.CreateInMemory();
        _mediaRepository = new EventMediaRepository(_contextFactory);
        _thumbnailMock = new Mock<IThumbnailGenerator>();
        // Default: thumbnail generation unavailable (attachments must survive).
        _thumbnailMock
            .Setup(t => t.TryGenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _loggerMock = new Mock<ILogger<MediaService>>();

        _mediaRoot = Path.Combine(Path.GetTempPath(), $"MediaServiceTests_Root_{Guid.NewGuid()}");
        _sourceDir = Path.Combine(Path.GetTempPath(), $"MediaServiceTests_Src_{Guid.NewGuid()}");
        Directory.CreateDirectory(_sourceDir);

        _mediaService = new MediaService(
            _mediaRepository,
            _contextFactory,
            _thumbnailMock.Object,
            _loggerMock.Object,
            _mediaRoot);
    }

    #region AttachAsync

    [Fact]
    public async Task AttachAsync_ValidImage_CopiesFileIntoManagedTreeAndPersistsRelativeRow()
    {
        // Arrange
        await SeedEventAsync("event-1");
        var content = Encoding.UTF8.GetBytes("pretend this is a jpeg");
        var source = CreateSourceFile("beach.jpg", content);
        Directory.Exists(_mediaRoot).Should().BeFalse("the media root is created lazily on first attach");

        // Act
        var media = await _mediaService.AttachAsync("event-1", source);

        // Assert - returned row
        media.EventId.Should().Be("event-1");
        media.MediaType.Should().Be(MediaType.Image);
        media.SortOrder.Should().Be(0);
        media.FileSizeBytes.Should().Be(content.Length);
        media.ContentHash.Should().Be(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
        media.ThumbnailPath.Should().BeNull("the thumbnail generator reported failure");
        media.CapturedAt.Should().BeNull("the fake image has no readable EXIF");
        media.Latitude.Should().BeNull();
        media.Longitude.Should().BeNull();

        // Assert - relative path stored, absolute file exists, source untouched
        Path.IsPathRooted(media.FilePath).Should().BeFalse("the DB stores paths relative to the media root");
        media.FilePath.Should().EndWith(media.MediaId + ".jpg");
        var absolute = _mediaService.GetAbsolutePath(media);
        absolute.Should().Be(Path.Combine(_mediaRoot, media.FilePath));
        File.Exists(absolute).Should().BeTrue("the file is copied into the managed tree");
        File.Exists(source).Should().BeTrue("the source file is copied, never moved");
        File.ReadAllBytes(absolute).Should().Equal(content);

        // Assert - row persisted
        var rows = await _mediaRepository.GetForEventAsync("event-1");
        rows.Should().ContainSingle(m => m.MediaId == media.MediaId);
    }

    [Fact]
    public async Task AttachAsync_DuplicateHashSameEvent_NoOpsAndReturnsExistingRow()
    {
        // Arrange
        await SeedEventAsync("event-1");
        var source = CreateSourceFile("dup.jpg", Encoding.UTF8.GetBytes("same bytes"));

        // Act
        var first = await _mediaService.AttachAsync("event-1", source);
        var second = await _mediaService.AttachAsync("event-1", source);

        // Assert
        second.MediaId.Should().Be(first.MediaId);
        (await _mediaRepository.GetForEventAsync("event-1")).Should().HaveCount(1);
        Directory.GetFiles(_mediaRoot, "*", SearchOption.AllDirectories)
            .Should().HaveCount(1, "the duplicate must not copy a second file");
    }

    [Fact]
    public async Task AttachAsync_SameFileDifferentEvent_AttachesToBoth()
    {
        // Arrange
        await SeedEventAsync("event-1");
        await SeedEventAsync("event-2");
        var source = CreateSourceFile("shared.png", Encoding.UTF8.GetBytes("shared content"));

        // Act
        var first = await _mediaService.AttachAsync("event-1", source);
        var second = await _mediaService.AttachAsync("event-2", source);

        // Assert - dedupe is per event, not global
        second.MediaId.Should().NotBe(first.MediaId);
        (await _mediaRepository.GetForEventAsync("event-1")).Should().HaveCount(1);
        (await _mediaRepository.GetForEventAsync("event-2")).Should().HaveCount(1);
    }

    [Fact]
    public async Task AttachAsync_UnsupportedExtension_ThrowsAndPersistsNothing()
    {
        // Arrange
        await SeedEventAsync("event-1");
        var source = CreateSourceFile("malware.exe", Encoding.UTF8.GetBytes("nope"));

        // Act
        Func<Task> act = () => _mediaService.AttachAsync("event-1", source);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not a supported file type*");
        (await _mediaRepository.GetForEventAsync("event-1")).Should().BeEmpty();
        Directory.Exists(_mediaRoot).Should().BeFalse("nothing may be copied for a rejected file");
    }

    [Fact]
    public async Task AttachAsync_MissingSourceFile_ThrowsWithClearMessage()
    {
        // Arrange
        await SeedEventAsync("event-1");
        var missing = Path.Combine(_sourceDir, "not-there.jpg");

        // Act
        Func<Task> act = () => _mediaService.AttachAsync("event-1", missing);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"*{missing}*");
    }

    [Fact]
    public async Task AttachAsync_UnknownEvent_Throws()
    {
        // Arrange
        var source = CreateSourceFile("orphan.jpg", Encoding.UTF8.GetBytes("content"));

        // Act
        Func<Task> act = () => _mediaService.AttachAsync("no-such-event", source);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Event not found*");
    }

    [Fact]
    public async Task AttachAsync_ThumbnailSucceeds_StoresRelativeThumbnailPath()
    {
        // Arrange - the generator writes the dest file and reports success
        await SeedEventAsync("event-1");
        SetupThumbnailSuccess();
        var source = CreateSourceFile("withthumb.jpg", Encoding.UTF8.GetBytes("image"));

        // Act
        var media = await _mediaService.AttachAsync("event-1", source);

        // Assert
        media.ThumbnailPath.Should().Be(Path.Combine(".thumbs", media.MediaId + ".jpg"));
        var absoluteThumb = _mediaService.GetAbsoluteThumbnailPath(media);
        absoluteThumb.Should().Be(Path.Combine(_mediaRoot, media.ThumbnailPath!));
        File.Exists(absoluteThumb!).Should().BeTrue();
    }

    [Fact]
    public async Task AttachAsync_NonImage_SkipsThumbnailGenerator()
    {
        // Arrange
        await SeedEventAsync("event-1");
        var source = CreateSourceFile("notes.pdf", Encoding.UTF8.GetBytes("%PDF-1.4"));

        // Act
        var media = await _mediaService.AttachAsync("event-1", source);

        // Assert
        media.MediaType.Should().Be(MediaType.Document);
        media.ThumbnailPath.Should().BeNull();
        _thumbnailMock.Verify(
            t => t.TryGenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region AttachManyAsync

    [Fact]
    public async Task AttachManyAsync_MultipleFiles_ReportsProgressAndAssignsSequentialSortOrders()
    {
        // Arrange
        await SeedEventAsync("event-1");
        var pathA = CreateSourceFile("a.jpg", Encoding.UTF8.GetBytes("aaa"));
        var pathB = CreateSourceFile("b.jpg", Encoding.UTF8.GetBytes("bbb"));
        var pathC = CreateSourceFile("c.jpg", Encoding.UTF8.GetBytes("ccc"));
        var progress = new ImmediateProgress<(int done, int total)>();

        // Act
        var attached = await _mediaService.AttachManyAsync(
            "event-1", new[] { pathA, pathB, pathC }, progress);

        // Assert
        attached.Should().HaveCount(3);
        attached.Select(m => m.SortOrder).Should().Equal(0, 1, 2);
        progress.Reports.Should().Equal((1, 3), (2, 3), (3, 3));

        var ordered = await _mediaService.GetForEventAsync("event-1");
        ordered.Select(m => m.MediaId).Should().Equal(attached.Select(m => m.MediaId));
    }

    [Fact]
    public async Task AttachManyAsync_OneBadFile_AttachesTheRestThenThrowsSummary()
    {
        // Arrange
        await SeedEventAsync("event-1");
        var good = CreateSourceFile("good.jpg", Encoding.UTF8.GetBytes("good"));
        var missing = Path.Combine(_sourceDir, "gone.jpg");

        // Act
        Func<Task> act = () => _mediaService.AttachManyAsync("event-1", new[] { good, missing });

        // Assert - the failure is reported, but the good file is attached
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*1 of 2*");
        (await _mediaRepository.GetForEventAsync("event-1")).Should().HaveCount(1);
    }

    #endregion

    #region Remove / Caption / Reorder

    [Fact]
    public async Task RemoveAsync_DeleteFileTrue_RemovesRowFileAndThumbnail()
    {
        // Arrange
        await SeedEventAsync("event-1");
        SetupThumbnailSuccess();
        var source = CreateSourceFile("removeme.jpg", Encoding.UTF8.GetBytes("bytes"));
        var media = await _mediaService.AttachAsync("event-1", source);
        var absoluteFile = _mediaService.GetAbsolutePath(media);
        var absoluteThumb = _mediaService.GetAbsoluteThumbnailPath(media)!;
        File.Exists(absoluteFile).Should().BeTrue();
        File.Exists(absoluteThumb).Should().BeTrue();

        // Act
        await _mediaService.RemoveAsync(media.MediaId);

        // Assert
        (await _mediaRepository.GetByIdAsync(media.MediaId)).Should().BeNull();
        File.Exists(absoluteFile).Should().BeFalse();
        File.Exists(absoluteThumb).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveAsync_DeleteFileFalse_RemovesRowButKeepsFile()
    {
        // Arrange
        await SeedEventAsync("event-1");
        var source = CreateSourceFile("keepfile.jpg", Encoding.UTF8.GetBytes("bytes"));
        var media = await _mediaService.AttachAsync("event-1", source);
        var absoluteFile = _mediaService.GetAbsolutePath(media);

        // Act
        await _mediaService.RemoveAsync(media.MediaId, deleteFile: false);

        // Assert
        (await _mediaRepository.GetByIdAsync(media.MediaId)).Should().BeNull();
        File.Exists(absoluteFile).Should().BeTrue();
    }

    [Fact]
    public async Task RemoveAsync_UnknownId_IsANoOp()
    {
        // Act
        Func<Task> act = () => _mediaService.RemoveAsync("missing-media-id");

        // Assert - idempotent removal never throws
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdateCaptionAsync_TrimsAndPersists_AndBlankClears()
    {
        // Arrange
        await SeedEventAsync("event-1");
        var source = CreateSourceFile("caption.jpg", Encoding.UTF8.GetBytes("bytes"));
        var media = await _mediaService.AttachAsync("event-1", source);

        // Act + Assert - set
        await _mediaService.UpdateCaptionAsync(media.MediaId, "  First day at the lake  ");
        (await _mediaRepository.GetByIdAsync(media.MediaId))!.Caption.Should().Be("First day at the lake");

        // Act + Assert - clear
        await _mediaService.UpdateCaptionAsync(media.MediaId, "   ");
        (await _mediaRepository.GetByIdAsync(media.MediaId))!.Caption.Should().BeNull();
    }

    [Fact]
    public async Task UpdateCaptionAsync_UnknownId_Throws()
    {
        // Act
        Func<Task> act = () => _mediaService.UpdateCaptionAsync("missing-media-id", "caption");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ReorderAsync_PersistsNewOrder()
    {
        // Arrange
        await SeedEventAsync("event-1");
        var a = await _mediaService.AttachAsync("event-1", CreateSourceFile("r-a.jpg", Encoding.UTF8.GetBytes("ra")));
        var b = await _mediaService.AttachAsync("event-1", CreateSourceFile("r-b.jpg", Encoding.UTF8.GetBytes("rb")));
        var c = await _mediaService.AttachAsync("event-1", CreateSourceFile("r-c.jpg", Encoding.UTF8.GetBytes("rc")));

        // Act
        await _mediaService.ReorderAsync("event-1", new[] { c.MediaId, a.MediaId, b.MediaId });

        // Assert - GetForEventAsync returns the new order with dense sort orders
        var ordered = await _mediaService.GetForEventAsync("event-1");
        ordered.Select(m => m.MediaId).Should().Equal(c.MediaId, a.MediaId, b.MediaId);
        ordered.Select(m => m.SortOrder).Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task ReorderAsync_IdsMissingFromList_KeepRelativeOrderAfterListedOnes()
    {
        // Arrange
        await SeedEventAsync("event-1");
        var a = await _mediaService.AttachAsync("event-1", CreateSourceFile("m-a.jpg", Encoding.UTF8.GetBytes("ma")));
        var b = await _mediaService.AttachAsync("event-1", CreateSourceFile("m-b.jpg", Encoding.UTF8.GetBytes("mb")));
        var c = await _mediaService.AttachAsync("event-1", CreateSourceFile("m-c.jpg", Encoding.UTF8.GetBytes("mc")));

        // Act - only c is listed; a and b keep their relative order after it
        await _mediaService.ReorderAsync("event-1", new[] { c.MediaId });

        // Assert
        var ordered = await _mediaService.GetForEventAsync("event-1");
        ordered.Select(m => m.MediaId).Should().Equal(c.MediaId, a.MediaId, b.MediaId);
    }

    #endregion

    #region Probe / Counts

    [Fact]
    public async Task ProbeAsync_SupportedFile_ReturnsTypeAndSizeWithoutImporting()
    {
        // Arrange
        var content = Encoding.UTF8.GetBytes("plain text notes");
        var source = CreateSourceFile("notes.txt", content);

        // Act
        var probe = await _mediaService.ProbeAsync(source);

        // Assert
        probe.MediaType.Should().Be(MediaType.Document);
        probe.FileSizeBytes.Should().Be(content.Length);
        probe.CapturedAt.Should().BeNull();
        probe.Latitude.Should().BeNull();
        probe.Longitude.Should().BeNull();
        Directory.Exists(_mediaRoot).Should().BeFalse("probing must not import anything");
    }

    [Fact]
    public async Task ProbeAsync_UnsupportedExtension_ReturnsNullMediaType()
    {
        // Arrange
        var source = CreateSourceFile("data.xyz", Encoding.UTF8.GetBytes("???"));

        // Act
        var probe = await _mediaService.ProbeAsync(source);

        // Assert
        probe.MediaType.Should().BeNull();
    }

    [Fact]
    public async Task ProbeAsync_MissingFile_Throws()
    {
        // Act
        Func<Task> act = () => _mediaService.ProbeAsync(Path.Combine(_sourceDir, "nope.jpg"));

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task GetCountsForEventsAsync_ManyEvents_ReturnsOneBatchedDictionary()
    {
        // Arrange
        await SeedEventAsync("event-1");
        await SeedEventAsync("event-2");
        await SeedEventAsync("event-3");
        await _mediaService.AttachAsync("event-1", CreateSourceFile("c1.jpg", Encoding.UTF8.GetBytes("c1")));
        await _mediaService.AttachAsync("event-1", CreateSourceFile("c2.jpg", Encoding.UTF8.GetBytes("c2")));
        await _mediaService.AttachAsync("event-2", CreateSourceFile("c3.jpg", Encoding.UTF8.GetBytes("c3")));

        // Act
        var counts = await _mediaRepository.GetCountsForEventsAsync(
            new[] { "event-1", "event-2", "event-3" });

        // Assert - events without media are simply absent
        counts.Should().HaveCount(2);
        counts["event-1"].Should().Be(2);
        counts["event-2"].Should().Be(1);
        counts.Should().NotContainKey("event-3");
    }

    #endregion

    #region Timeline projection publishing

    [Fact]
    public async Task AttachAsync_WithProjectionPublisher_PublishesTheEvent()
    {
        // Arrange - the event projection denormalises a media count, so a photo
        // changes what a companion draws without any event column moving
        await SeedEventAsync("event-1");
        var publisher = CreatePublisherMock();
        var service = CreateServiceWithPublisher(publisher.Object);

        // Act
        await service.AttachAsync("event-1", CreateSourceFile("pub.jpg", Encoding.UTF8.GetBytes("pub")));

        // Assert
        publisher.Verify(
            p => p.PublishEventAsync("event-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AttachAsync_DuplicateHashSameEvent_PublishesNothingForTheNoOp()
    {
        // Arrange
        await SeedEventAsync("event-1");
        var publisher = CreatePublisherMock();
        var service = CreateServiceWithPublisher(publisher.Object);
        var source = CreateSourceFile("dup-pub.jpg", Encoding.UTF8.GetBytes("same bytes"));
        await service.AttachAsync("event-1", source);
        publisher.Invocations.Clear();

        // Act - the duplicate returns the existing row and writes nothing
        await service.AttachAsync("event-1", source);

        // Assert - no row changed, so there is no new state to project
        publisher.Verify(
            p => p.PublishEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AttachAsync_ProjectionPublisherThrows_AttachmentStillCommits()
    {
        // Arrange - an outbox that is full, locked or misconfigured
        await SeedEventAsync("event-1");
        var publisher = new Mock<ITimelineProjectionPublisher>();
        publisher
            .Setup(p => p.PublishEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("outbox unavailable"));
        var service = CreateServiceWithPublisher(publisher.Object);

        // Act - the photo is the user's memory; a sync problem must not cost it
        var media = await service.AttachAsync(
            "event-1", CreateSourceFile("survives.jpg", Encoding.UTF8.GetBytes("survives")));

        // Assert
        (await _mediaRepository.GetByIdAsync(media.MediaId)).Should().NotBeNull();
        File.Exists(_mediaService.GetAbsolutePath(media)).Should().BeTrue();
    }

    [Fact]
    public async Task AttachManyAsync_PublishesOncePerBatch_NotOncePerFile()
    {
        // Arrange
        await SeedEventAsync("event-1");
        var publisher = CreatePublisherMock();
        var service = CreateServiceWithPublisher(publisher.Object);
        var paths = new[]
        {
            CreateSourceFile("batch-a.jpg", Encoding.UTF8.GetBytes("aaa")),
            CreateSourceFile("batch-b.jpg", Encoding.UTF8.GetBytes("bbb")),
            CreateSourceFile("batch-c.jpg", Encoding.UTF8.GetBytes("ccc")),
        };

        // Act
        var attached = await service.AttachManyAsync("event-1", paths);

        // Assert - every row lands on the same event, so three photos are one
        // badge change; per-file payloads would all be obsolete on arrival
        attached.Should().HaveCount(3, "otherwise 'published once' proves nothing");
        publisher.Verify(
            p => p.PublishEventAsync("event-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AttachManyAsync_PartialFailure_PublishesTheRowsThatCommitted()
    {
        // Arrange
        await SeedEventAsync("event-1");
        var publisher = CreatePublisherMock();
        var service = CreateServiceWithPublisher(publisher.Object);
        var good = CreateSourceFile("half-good.jpg", Encoding.UTF8.GetBytes("good"));
        var missing = Path.Combine(_sourceDir, "half-gone.jpg");

        // Act
        Func<Task> act = () => service.AttachManyAsync("event-1", new[] { good, missing });

        // Assert - the batch throws its failure summary, but one row committed
        // and the caller's exception is no reason to leave the count stale
        await act.Should().ThrowAsync<InvalidOperationException>();
        publisher.Verify(
            p => p.PublishEventAsync("event-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AttachManyAsync_CancelledMidBatch_PublishesWhatAlreadyCommitted()
    {
        // Arrange - cancel between files, the one place AttachManyAsync gives up
        // with rows already durable
        await SeedEventAsync("event-1");
        var publisher = CreatePublisherMock();
        var service = CreateServiceWithPublisher(publisher.Object);
        using var cts = new CancellationTokenSource();
        var paths = new[]
        {
            CreateSourceFile("cancel-a.jpg", Encoding.UTF8.GetBytes("a")),
            CreateSourceFile("cancel-b.jpg", Encoding.UTF8.GetBytes("b")),
        };

        // Act
        Func<Task> act = () => service.AttachManyAsync(
            "event-1", paths, new CancellingProgress(cts), cts.Token);

        // Assert - cancellation stops the work still to come, not the report of
        // the work already done
        await act.Should().ThrowAsync<OperationCanceledException>();
        (await _mediaRepository.GetForEventAsync("event-1")).Should().HaveCount(1);
        publisher.Verify(
            p => p.PublishEventAsync("event-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AttachManyAsync_EveryFileFailed_PublishesNothing()
    {
        // Arrange
        await SeedEventAsync("event-1");
        var publisher = CreatePublisherMock();
        var service = CreateServiceWithPublisher(publisher.Object);
        var missingA = Path.Combine(_sourceDir, "none-a.jpg");
        var missingB = Path.Combine(_sourceDir, "none-b.jpg");

        // Act
        Func<Task> act = () => service.AttachManyAsync("event-1", new[] { missingA, missingB });

        // Assert - nothing reached the event, so nothing to say about it
        await act.Should().ThrowAsync<InvalidOperationException>();
        publisher.Verify(
            p => p.PublishEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveAsync_WithProjectionPublisher_PublishesTheEvent()
    {
        // Arrange
        await SeedEventAsync("event-1");
        var publisher = CreatePublisherMock();
        var service = CreateServiceWithPublisher(publisher.Object);
        var media = await service.AttachAsync(
            "event-1", CreateSourceFile("removed.jpg", Encoding.UTF8.GetBytes("bytes")));
        publisher.Invocations.Clear(); // the attach's own publish is not what this asserts

        // Act
        await service.RemoveAsync(media.MediaId);

        // Assert
        publisher.Verify(
            p => p.PublishEventAsync("event-1", It.IsAny<CancellationToken>()), Times.Once);

        // The missing-row no-op removes nothing and must publish nothing.
        await service.RemoveAsync("missing-media-id");
        publisher.Verify(
            p => p.PublishEventAsync("event-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveAsync_KeepingTheFile_StillPublishes()
    {
        // Arrange - deleteFile decides the file's fate, never the row's, and the
        // count a companion badges the event with is a count of rows
        await SeedEventAsync("event-1");
        var publisher = CreatePublisherMock();
        var service = CreateServiceWithPublisher(publisher.Object);
        var media = await service.AttachAsync(
            "event-1", CreateSourceFile("kept.jpg", Encoding.UTF8.GetBytes("bytes")));
        publisher.Invocations.Clear();

        // Act
        await service.RemoveAsync(media.MediaId, deleteFile: false);

        // Assert
        publisher.Verify(
            p => p.PublishEventAsync("event-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateCaptionAndReorder_PublishNothing_TheProjectionCarriesOnlyACount()
    {
        // Arrange
        await SeedEventAsync("event-1");
        var publisher = CreatePublisherMock();
        var service = CreateServiceWithPublisher(publisher.Object);
        var first = await service.AttachAsync("event-1", CreateSourceFile("q-a.jpg", Encoding.UTF8.GetBytes("qa")));
        var second = await service.AttachAsync("event-1", CreateSourceFile("q-b.jpg", Encoding.UTF8.GetBytes("qb")));
        publisher.Invocations.Clear();

        // Act - both are real archive writes
        await service.UpdateCaptionAsync(first.MediaId, "At the lake");
        await service.ReorderAsync("event-1", new[] { second.MediaId, first.MediaId });

        // Assert - and both are invisible to a companion: the event payload
        // carries a media COUNT and no caption, order or thumbnail. Publishing
        // would only rebuild a payload the publisher drops as unchanged.
        publisher.Verify(
            p => p.PublishEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        (await service.GetForEventAsync("event-1")).Select(m => m.MediaId)
            .Should().Equal(
                new[] { second.MediaId, first.MediaId },
                "the reorder itself must still have happened");
    }

    [Fact]
    public async Task CleanupOrphansAsync_SweepsFiles_AndPublishesNothing()
    {
        // Arrange - a live attachment plus an orphaned file, aged past the
        // grace window so the sweep will actually delete it
        await SeedEventAsync("event-1");
        var publisher = CreatePublisherMock();
        var service = CreateServiceWithPublisher(publisher.Object);
        await service.AttachAsync("event-1", CreateSourceFile("live.jpg", Encoding.UTF8.GetBytes("live")));
        var orphan = Path.Combine(_mediaRoot, "2019", "05", "orphan.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(orphan)!);
        File.WriteAllText(orphan, "no row references this");
        File.SetCreationTimeUtc(
            orphan, DateTime.UtcNow - MediaService.CleanupGraceWindow - TimeSpan.FromMinutes(5));
        publisher.Invocations.Clear();

        // Act
        var result = await service.CleanupOrphansAsync();

        // Assert - the sweep deletes FILES and never an event_media row, so no
        // event's projected count moved. Publishing "everything" to be safe
        // would flood the outbox to say nothing, and a deleted orphan has no
        // event id to attribute it to in the first place.
        result.MediaFilesDeleted.Should().Be(1, "otherwise 'published nothing' proves nothing");
        File.Exists(orphan).Should().BeFalse();
        publisher.Verify(
            p => p.PublishEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        publisher.Verify(
            p => p.PublishDeletedAsync(
                It.IsAny<TimelineProjectionEntity>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// The fixture's service plus a projection publisher. Everything else is
    /// shared - same repository, same context factory, same media root - so a
    /// test can read the archive back through either instance.
    /// </summary>
    private MediaService CreateServiceWithPublisher(ITimelineProjectionPublisher publisher) =>
        new(_mediaRepository, _contextFactory, _thumbnailMock.Object, _loggerMock.Object, _mediaRoot, publisher);

    private static Mock<ITimelineProjectionPublisher> CreatePublisherMock()
    {
        var publisher = new Mock<ITimelineProjectionPublisher>();
        publisher
            .Setup(p => p.PublishEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return publisher;
    }

    private async Task SeedEventAsync(string eventId)
    {
        await using var context = _contextFactory.CreateDbContext();
        context.Events.Add(new Event
        {
            EventId = eventId,
            Title = $"Event {eventId}",
            StartDate = new DateTime(2020, 6, 1),
            Category = EventCategory.Other
        });
        await context.SaveChangesAsync();
    }

    private string CreateSourceFile(string fileName, byte[] content)
    {
        var path = Path.Combine(_sourceDir, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    private void SetupThumbnailSuccess()
    {
        _thumbnailMock
            .Setup(t => t.TryGenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((string sourcePath, string destPath, int longestEdge, CancellationToken token) =>
            {
                File.WriteAllBytes(destPath, new byte[] { 0xFF, 0xD8 });
                return Task.FromResult(true);
            });
    }

    /// <summary>
    /// Synchronous IProgress capture - unlike Progress&lt;T&gt;, reports are
    /// recorded immediately, so assertions never race a sync-context post.
    /// </summary>
    private sealed class ImmediateProgress<T> : IProgress<T>
    {
        public List<T> Reports { get; } = new();

        public void Report(T value) => Reports.Add(value);
    }

    /// <summary>
    /// Cancels as soon as the first file is reported done. AttachManyAsync
    /// checks the token between files, so this is the one way to interrupt it
    /// at a point where some rows have committed and some never will.
    /// </summary>
    private sealed class CancellingProgress : IProgress<(int done, int total)>
    {
        private readonly CancellationTokenSource _cts;

        public CancellingProgress(CancellationTokenSource cts) => _cts = cts;

        public void Report((int done, int total) value) => _cts.Cancel();
    }

    #endregion

    public void Dispose()
    {
        using (var context = _contextFactory.CreateDbContext())
        {
            context.Database.EnsureDeleted();
        }

        foreach (var directory in new[] { _mediaRoot, _sourceDir })
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
