using FluentAssertions;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using MemoryTimeline.Tests;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Clear-Cache orphan sweep (F12): <see cref="MediaService.CleanupOrphansAsync"/>
/// must delete exactly the orphans in a seeded temp media tree - never a file
/// the database references - and report the actual bytes freed.
/// EF InMemory is sufficient here (the sweep only reads event_media rows).
/// </summary>
public class MediaCleanupTests : IDisposable
{
    private readonly TestDbContextFactory _contextFactory;
    private readonly string _mediaRoot;
    private readonly MediaService _mediaService;

    public MediaCleanupTests()
    {
        _contextFactory = TestDbContextFactory.CreateInMemory();
        _mediaRoot = Path.Combine(Path.GetTempPath(), $"MediaCleanupTests_{Guid.NewGuid()}");
        _mediaService = new MediaService(
            new EventMediaRepository(_contextFactory),
            _contextFactory,
            new Mock<IThumbnailGenerator>().Object,
            new Mock<ILogger<MediaService>>().Object,
            mediaRootOverride: _mediaRoot);
    }

    [Fact]
    public async Task CleanupOrphansAsync_DeletesExactlyTheOrphans_AndReportsBytes()
    {
        // Arrange - one referenced media file + thumbnail (must survive)
        var keepRelative = Path.Combine("2020", "01", "keep.jpg");
        var keepThumbRelative = Path.Combine(".thumbs", "keep.jpg");
        WriteFile(Path.Combine(_mediaRoot, keepRelative), "keep-media");
        WriteFile(Path.Combine(_mediaRoot, keepThumbRelative), "keep-thumb");

        await using (var context = await _contextFactory.CreateDbContextAsync())
        {
            context.EventMedia.Add(new EventMedia
            {
                MediaId = "m-keep",
                EventId = "evt-1",
                FilePath = keepRelative,
                ThumbnailPath = keepThumbRelative,
                MediaType = MediaType.Image
            });
            await context.SaveChangesAsync();
        }

        // ...and three orphans (must be deleted): an unreferenced media file,
        // an unreferenced thumbnail, and a .tmp leftover
        var orphanMedia = Path.Combine(_mediaRoot, "2020", "01", "orphan.jpg");
        var orphanThumb = Path.Combine(_mediaRoot, ".thumbs", "orphan.jpg");
        var tempLeftover = Path.Combine(_mediaRoot, "2020", "01", "partial.jpg.tmp");
        WriteFile(orphanMedia, "orphan-media!");
        WriteFile(orphanThumb, "orphan-thumb!!");
        WriteFile(tempLeftover, "tmp");
        var expectedBytes =
            new FileInfo(orphanMedia).Length +
            new FileInfo(orphanThumb).Length +
            new FileInfo(tempLeftover).Length;

        // Act
        var result = await _mediaService.CleanupOrphansAsync();

        // Assert - exactly the orphans went, with real byte accounting
        result.FilesDeleted.Should().Be(3);
        result.MediaFilesDeleted.Should().Be(1);
        result.ThumbnailsDeleted.Should().Be(1);
        result.TempFilesDeleted.Should().Be(1);
        result.BytesFreed.Should().Be(expectedBytes);

        File.Exists(orphanMedia).Should().BeFalse();
        File.Exists(orphanThumb).Should().BeFalse();
        File.Exists(tempLeftover).Should().BeFalse();
        File.Exists(Path.Combine(_mediaRoot, keepRelative)).Should().BeTrue();
        File.Exists(Path.Combine(_mediaRoot, keepThumbRelative)).Should().BeTrue();
    }

    [Fact]
    public async Task CleanupOrphansAsync_NothingToClean_ReportsZero()
    {
        // Arrange - only a referenced file exists
        var keepRelative = Path.Combine("2022", "03", "only.jpg");
        WriteFile(Path.Combine(_mediaRoot, keepRelative), "referenced");
        await using (var context = await _contextFactory.CreateDbContextAsync())
        {
            context.EventMedia.Add(new EventMedia
            {
                MediaId = "m-only",
                EventId = "evt-1",
                FilePath = keepRelative,
                MediaType = MediaType.Image
            });
            await context.SaveChangesAsync();
        }

        // Act
        var result = await _mediaService.CleanupOrphansAsync();

        // Assert - zero deletions => the UI reports "Nothing to clean"
        result.FilesDeleted.Should().Be(0);
        result.BytesFreed.Should().Be(0);
        File.Exists(Path.Combine(_mediaRoot, keepRelative)).Should().BeTrue();
    }

    [Fact]
    public async Task CleanupOrphansAsync_FreshRowlessFile_SurvivesGraceWindow()
    {
        // Arrange - a just-created file with no event_media row: exactly what
        // an in-flight AttachAsync looks like between its file copy and its
        // row insert. The grace window must keep the sweep's hands off it.
        var freshMedia = Path.Combine(_mediaRoot, "2024", "07", "just-attached.jpg");
        var freshThumb = Path.Combine(_mediaRoot, ".thumbs", "just-attached.jpg");
        WriteFreshFile(freshMedia, "fresh-media");
        WriteFreshFile(freshThumb, "fresh-thumb");

        // ...next to a genuinely old orphan that must still be swept.
        var oldOrphan = Path.Combine(_mediaRoot, "2020", "01", "old-orphan.jpg");
        WriteFile(oldOrphan, "old-orphan");

        // Act
        var result = await _mediaService.CleanupOrphansAsync();

        // Assert - the fresh files survived; only the aged orphan went.
        File.Exists(freshMedia).Should().BeTrue();
        File.Exists(freshThumb).Should().BeTrue();
        File.Exists(oldOrphan).Should().BeFalse();
        result.FilesDeleted.Should().Be(1);
        result.MediaFilesDeleted.Should().Be(1);
    }

    [Fact]
    public async Task CleanupOrphansAsync_MissingMediaRoot_IsANoOp()
    {
        // The media root does not exist until the first attachment.
        var result = await _mediaService.CleanupOrphansAsync();

        result.FilesDeleted.Should().Be(0);
        result.BytesFreed.Should().Be(0);
    }

    /// <summary>
    /// Writes a file and ages its creation time past the cleanup grace window
    /// (CleanupOrphansAsync never touches files created within the window, so
    /// just-written fixtures would otherwise always "survive").
    /// </summary>
    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetCreationTimeUtc(
            path, DateTime.UtcNow - MediaService.CleanupGraceWindow - TimeSpan.FromMinutes(5));
    }

    /// <summary>Writes a file WITHOUT aging it - creation time is "now".</summary>
    private static void WriteFreshFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        using (var context = _contextFactory.CreateDbContext())
        {
            context.Database.EnsureDeleted();
        }

        if (Directory.Exists(_mediaRoot))
        {
            Directory.Delete(_mediaRoot, recursive: true);
        }
    }
}
