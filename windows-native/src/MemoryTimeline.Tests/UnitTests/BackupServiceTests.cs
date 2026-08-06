using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using MemoryTimeline.Tests;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Backup/restore behavior (F12) over real file-based SQLite databases and
/// temp directories: round-trip fidelity, the WAL-safe online-backup
/// primitive under a concurrent write transaction, manifest contents, .tmp
/// hygiene, the scheduled-backup decision matrix, and backup listing.
/// </summary>
public class BackupServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _sourceDbPath;
    private readonly TestDbContextFactory _sourceFactory;
    private readonly SettingsService _sourceSettings;
    private readonly MediaService _sourceMedia;
    private readonly BackupService _backupService;
    private readonly string _mediaRoot;
    private readonly string _audioRoot;
    private readonly string _backupDir;

    public BackupServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"BackupServiceTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);
        _mediaRoot = Path.Combine(_testRoot, "media");
        _audioRoot = Path.Combine(_testRoot, "audio");
        _backupDir = Path.Combine(_testRoot, "backups");
        Directory.CreateDirectory(_backupDir);

        _sourceDbPath = Path.Combine(_testRoot, "source.db");
        _sourceFactory = TestDbContextFactory.CreateSqliteFile(_sourceDbPath);
        using (var context = _sourceFactory.CreateDbContext())
        {
            context.Database.EnsureCreated();
        }

        _sourceSettings = new SettingsService(_sourceFactory, new Mock<ILogger<SettingsService>>().Object);
        _sourceMedia = new MediaService(
            new EventMediaRepository(_sourceFactory),
            _sourceFactory,
            new Mock<IThumbnailGenerator>().Object,
            new Mock<ILogger<MediaService>>().Object,
            mediaRootOverride: _mediaRoot);
        _backupService = new BackupService(
            _sourceFactory,
            _sourceSettings,
            _sourceMedia,
            new Mock<ILogger<BackupService>>().Object,
            audioRootOverride: _audioRoot);
    }

    #region Backup + preview + restore round trip

    [Fact]
    public async Task CreateBackup_ThenPreview_ManifestAndCountsMatch()
    {
        // Arrange - 3 events, one media file, one audio file
        await SeedEventsAsync(3);
        WriteFile(Path.Combine(_mediaRoot, "2020", "01", "photo.jpg"), "jpeg-bytes");
        WriteFile(Path.Combine(_audioRoot, "rec1.wav"), "wav-bytes");

        var destination = Path.Combine(_backupDir, "roundtrip.mtbak");
        var progressValues = new List<int>();

        // Act
        var result = await _backupService.CreateBackupAsync(
            destination,
            new BackupOptions { IncludeMedia = true, IncludeAudio = true },
            new SyncProgress(progressValues.Add));

        // Assert - result + no torn temp file
        result.FilePath.Should().Be(destination);
        result.EventCount.Should().Be(3);
        result.SizeBytes.Should().BeGreaterThan(0);
        File.Exists(destination).Should().BeTrue();
        File.Exists(destination + ".tmp").Should().BeFalse();
        progressValues.Should().Contain(100);

        // Assert - preview reads the manifest without touching the live db
        var preview = await _backupService.PreviewRestoreAsync(destination);
        preview.FormatVersion.Should().Be(1);
        preview.AppVersion.Should().NotBeNullOrEmpty();
        preview.EventCount.Should().Be(3);
        preview.IncludesMedia.Should().BeTrue();
        preview.IncludesAudio.Should().BeTrue();
        preview.MediaFileCount.Should().Be(1);
        preview.AudioFileCount.Should().Be(1);
        preview.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
        preview.SizeBytes.Should().Be(result.SizeBytes);
    }

    [Fact]
    public async Task RestoreAsync_Replace_ReproducesEventCount_AndRestoresFiles()
    {
        // Arrange - back up the seeded source archive
        await SeedEventsAsync(4);
        WriteFile(Path.Combine(_mediaRoot, "2021", "05", "pic.png"), "png-bytes");
        WriteFile(Path.Combine(_audioRoot, "memo.wav"), "wav-bytes");
        var backupPath = Path.Combine(_backupDir, "full.mtbak");
        await _backupService.CreateBackupAsync(backupPath, new BackupOptions());

        // A SECOND live installation (own db + own media/audio roots)
        var targetDir = Path.Combine(_testRoot, "target");
        Directory.CreateDirectory(targetDir);
        var targetDbPath = Path.Combine(targetDir, "live.db");
        var targetFactory = TestDbContextFactory.CreateSqliteFile(targetDbPath);
        using (var context = targetFactory.CreateDbContext())
        {
            context.Database.EnsureCreated(); // empty archive, zero events
        }

        var targetMediaRoot = Path.Combine(targetDir, "media");
        var targetAudioRoot = Path.Combine(targetDir, "audio");
        var targetSettings = new SettingsService(targetFactory, new Mock<ILogger<SettingsService>>().Object);
        var targetMedia = new MediaService(
            new EventMediaRepository(targetFactory),
            targetFactory,
            new Mock<IThumbnailGenerator>().Object,
            new Mock<ILogger<MediaService>>().Object,
            mediaRootOverride: targetMediaRoot);
        var targetBackupService = new BackupService(
            targetFactory,
            targetSettings,
            targetMedia,
            new Mock<ILogger<BackupService>>().Object,
            audioRootOverride: targetAudioRoot);

        // Act
        await targetBackupService.RestoreAsync(backupPath, RestoreMode.Replace);

        // Assert - the restored archive reproduces the source exactly
        await using (var verifyContext = targetFactory.CreateDbContext())
        {
            (await verifyContext.Events.CountAsync()).Should().Be(4);
        }

        File.Exists(Path.Combine(targetMediaRoot, "2021", "05", "pic.png")).Should().BeTrue();
        File.Exists(Path.Combine(targetAudioRoot, "memo.wav")).Should().BeTrue();

        // The automatic db-only safety backup was written (no destination
        // configured => next to the target database) and is itself readable
        var safetyBackups = Directory.GetFiles(targetDir, "pre-restore-*.mtbak");
        safetyBackups.Should().NotBeEmpty();
        var safetyPreview = await targetBackupService.PreviewRestoreAsync(safetyBackups[0]);
        safetyPreview.EventCount.Should().Be(0);
        safetyPreview.IncludesMedia.Should().BeFalse();
    }

    [Fact]
    public async Task CreateBackup_WhileAnotherContextHoldsWriteTransaction_SnapshotsCommittedState()
    {
        // Arrange - 2 committed events
        await SeedEventsAsync(2);

        // An OPEN, uncommitted write transaction on another factory context
        await using var writerContext = _sourceFactory.CreateDbContext();
        await using var transaction = await writerContext.Database.BeginTransactionAsync();
        writerContext.Events.Add(new Event
        {
            EventId = Guid.NewGuid().ToString(),
            Title = "Uncommitted event",
            StartDate = new DateTime(2024, 1, 1)
        });
        await writerContext.SaveChangesAsync();

        // Act - the online-backup primitive must produce a consistent,
        // uncorrupted snapshot of the COMMITTED state despite the open writer
        var destination = Path.Combine(_backupDir, "under-write.mtbak");
        var result = await _backupService.CreateBackupAsync(
            destination, new BackupOptions { IncludeMedia = false, IncludeAudio = false });

        await transaction.RollbackAsync();

        // Assert
        result.EventCount.Should().Be(2);
        var preview = await _backupService.PreviewRestoreAsync(destination);
        preview.EventCount.Should().Be(2);
    }

    [Fact]
    public async Task CreateBackup_CancelledMidway_LeavesNoTmpOrDestination()
    {
        // Arrange
        await SeedEventsAsync(1);
        WriteFile(Path.Combine(_mediaRoot, "big.jpg"), new string('x', 4096));
        var destination = Path.Combine(_backupDir, "cancelled.mtbak");

        // Cancel synchronously when the post-snapshot progress stage (25)
        // reports - the temp zip exists by the next cancellation check.
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(value =>
        {
            if (value == 25)
            {
                cts.Cancel();
            }
        });

        // Act / Assert
        var act = () => _backupService.CreateBackupAsync(
            destination, new BackupOptions(), progress, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        File.Exists(destination).Should().BeFalse();
        File.Exists(destination + ".tmp").Should().BeFalse();
    }

    [Fact]
    public async Task PreviewRestoreAsync_NotABackup_Throws()
    {
        // Arrange - a zip-less garbage file with the right extension
        var garbagePath = Path.Combine(_backupDir, "garbage.mtbak");
        File.WriteAllText(garbagePath, "this is not a zip");

        // Act / Assert
        var act = () => _backupService.PreviewRestoreAsync(garbagePath);
        await act.Should().ThrowAsync<Exception>();
    }

    #endregion

    #region Scheduled-backup decision

    [Theory]
    // schedule, hours since last run (-1 = never ran), expected
    [InlineData("off", -1, false)]
    [InlineData("off", 200, false)]
    [InlineData("daily", -1, true)]
    [InlineData("daily", 2, false)]
    [InlineData("daily", 25, true)]
    [InlineData("weekly", -1, true)]
    [InlineData("weekly", 144, false)] // 6 days
    [InlineData("weekly", 192, true)]  // 8 days
    [InlineData("", -1, false)]
    [InlineData(null, 999, false)]
    [InlineData("hourly", 999, false)] // unknown cadence never fires
    [InlineData("Daily", 25, true)]    // case-insensitive
    public void ShouldRunScheduledBackup_Matrix(string? schedule, double hoursSinceLastRun, bool expected)
    {
        var now = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        DateTime? lastRunAt = hoursSinceLastRun < 0 ? null : now.AddHours(-hoursSinceLastRun);

        BackupService.ShouldRunScheduledBackup(schedule, lastRunAt, now).Should().Be(expected);
    }

    #endregion

    #region Listing

    [Fact]
    public async Task ListBackupsAsync_ParsesManifests_SkipsUnreadableFiles()
    {
        // Arrange - a configured destination with 2 real backups + 1 garbage file
        await SeedEventsAsync(2);
        await _sourceSettings.SetSettingAsync(SettingKeys.BackupDestination, _backupDir);

        await _backupService.CreateBackupAsync(
            Path.Combine(_backupDir, "first.mtbak"),
            new BackupOptions { IncludeMedia = false, IncludeAudio = false });
        await _backupService.CreateBackupAsync(
            Path.Combine(_backupDir, "second.mtbak"),
            new BackupOptions { IncludeMedia = false, IncludeAudio = false });
        File.WriteAllText(Path.Combine(_backupDir, "corrupt.mtbak"), "not a zip");

        // Act
        var records = await _backupService.ListBackupsAsync();

        // Assert - both healthy backups listed (newest first), garbage skipped
        records.Should().HaveCount(2);
        records.Select(r => r.FileName).Should().BeEquivalentTo(new[] { "first.mtbak", "second.mtbak" });
        records.Should().OnlyContain(r => r.EventCount == 2 && r.SizeBytes > 0);
        records[0].CreatedAtUtc.Should().BeOnOrAfter(records[1].CreatedAtUtc);
    }

    [Fact]
    public async Task ListBackupsAsync_NoDestinationConfigured_ReturnsEmpty()
    {
        var records = await _backupService.ListBackupsAsync();
        records.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// IProgress that invokes its callback synchronously (Progress&lt;T&gt;
    /// marshals via a SynchronizationContext, which would make the
    /// cancellation test racy).
    /// </summary>
    private sealed class SyncProgress : IProgress<int>
    {
        private readonly Action<int> _callback;
        public SyncProgress(Action<int> callback) => _callback = callback;
        public void Report(int value) => _callback(value);
    }

    private async Task SeedEventsAsync(int count)
    {
        await using var context = _sourceFactory.CreateDbContext();
        for (var i = 0; i < count; i++)
        {
            context.Events.Add(new Event
            {
                EventId = Guid.NewGuid().ToString(),
                Title = $"Event {i + 1}",
                StartDate = new DateTime(2020, 1, 1).AddDays(i)
            });
        }

        await context.SaveChangesAsync();
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    #endregion

    public void Dispose()
    {
        using (var context = _sourceFactory.CreateDbContext())
        {
            context.Database.EnsureDeleted();
        }

        // Release pooled handles on every test database before deleting files.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Leftover handles on Windows CI: temp dirs are cleaned by the OS.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
