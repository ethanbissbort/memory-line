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
/// reordering, probing, and batched counts. Uses EF InMemory + the real
/// repository; the media root is a per-test temp directory injected through
/// the service's mediaRootOverride parameter.
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

    #region Helpers

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
