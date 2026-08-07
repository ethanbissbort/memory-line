using FluentAssertions;
using Microsoft.Data.Sqlite;
using MemoryTimeline.Data;
using MemoryTimeline.Tests;
using Xunit;

namespace MemoryTimeline.Tests.Integration;

/// <summary>
/// Integration tests for the sync-groundwork portion of <see cref="SchemaUpgrader"/>
/// against a real FILE-based SQLite database: fresh databases get the full schema
/// via EnsureCreated, legacy databases (where EnsureCreated is a no-op) get the
/// captures/capture_artifacts/sync_outbox tables plus the recording_queue
/// provenance columns and unique index through idempotent drift repair.
/// Schema is asserted with raw Microsoft.Data.Sqlite queries so the tests verify
/// what is actually in the database file, not what EF believes about it.
/// </summary>
public class SchemaUpgraderSyncSchemaTests : IDisposable
{
    private const string SourceCaptureIdIndexName = "IX_recording_queue_source_capture_id";

    private static readonly string[] ExpectedProvenanceColumns =
    {
        "source_capture_id",
        "source_device_id",
        "source_platform",
        "audio_artifact_id",
        "original_file_name",
        "content_sha256",
        "sync_state",
        "received_at",
        "processing_stage"
    };

    private readonly string _databasePath;

    public SchemaUpgraderSyncSchemaTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"SchemaUpgraderTest_{Guid.NewGuid()}.db");
    }

    /// <summary>
    /// Raw connections must opt OUT of Microsoft.Data.Sqlite's connection pooling:
    /// a pooled setup connection keeps its underlying SQLite handle open after
    /// Dispose, and the schema upgrader's later "PRAGMA journal_mode=WAL" cannot
    /// convert the journal mode while that handle lives — on Windows it fails with
    /// "SQLite Error 8: attempt to write a readonly database".
    /// </summary>
    private string RawConnectionString => $"Data Source={_databasePath};Pooling=False";

    [Fact]
    public async Task EnsureSchemaAsync_FreshDatabase_CreatesSyncTablesColumnsAndUniqueIndex()
    {
        // Act
        await RunSchemaUpgraderAsync();

        // Assert
        await AssertSyncSchemaAsync();
    }

    [Fact]
    public async Task EnsureSchemaAsync_LegacyDatabase_RepairsDriftAndEnforcesUniqueSourceCaptureId()
    {
        // Arrange - build a database exactly as an old build would have left it:
        // ONLY the original recording_queue table, none of the sync schema.
        // EnsureCreated is then a no-op (a table exists), so everything the
        // assertions find must come from the drift repair.
        await using (var connection = new SqliteConnection(RawConnectionString))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE recording_queue (
                    queue_id TEXT PRIMARY KEY,
                    audio_file_path TEXT NOT NULL,
                    status TEXT NOT NULL,
                    duration_seconds REAL,
                    file_size_bytes INTEGER,
                    created_at TEXT NOT NULL,
                    processed_at TEXT,
                    error_message TEXT
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        // Act
        await RunSchemaUpgraderAsync();

        // Assert - same shape as a fresh database
        await AssertSyncSchemaAsync();

        // Assert - the unique index tolerates many legacy NULL rows but rejects
        // a duplicate non-null source_capture_id (idempotent ingestion guarantee).
        await using (var connection = new SqliteConnection(RawConnectionString))
        {
            await connection.OpenAsync();

            await InsertQueueRowAsync(connection, "legacy-row-1", null);
            await InsertQueueRowAsync(connection, "legacy-row-2", null);
            await InsertQueueRowAsync(connection, "synced-row-1", "capture-1");

            var duplicateInsert = () => InsertQueueRowAsync(connection, "synced-row-2", "capture-1");
            await duplicateInsert.Should().ThrowAsync<SqliteException>(
                "the unique index must reject a second queue row for the same capture");
        }
    }

    [Fact]
    public async Task EnsureSchemaAsync_RunTwice_DoesNotThrow()
    {
        // Arrange
        await RunSchemaUpgraderAsync();

        // Act / Assert - all DDL must be idempotent
        var secondRun = RunSchemaUpgraderAsync;
        await secondRun.Should().NotThrowAsync();
    }

    public void Dispose()
    {
        // Both EF and the raw connections here pool by default; clear the pools
        // so no handle keeps the database files alive past the test.
        SqliteConnection.ClearAllPools();

        foreach (var path in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    #region Helpers

    private async Task RunSchemaUpgraderAsync()
    {
        var contextFactory = TestDbContextFactory.CreateSqliteFile(_databasePath);
        await using var context = contextFactory.CreateDbContext();
        await SchemaUpgrader.EnsureSchemaAsync(context);
    }

    /// <summary>
    /// Asserts the sync tables, recording_queue provenance columns, and the
    /// unique source_capture_id index exist, using a raw SQLite connection.
    /// </summary>
    private async Task AssertSyncSchemaAsync()
    {
        await using var connection = new SqliteConnection(RawConnectionString);
        await connection.OpenAsync();

        var tables = await GetTableNamesAsync(connection);
        tables.Should().Contain("captures");
        tables.Should().Contain("capture_artifacts");
        tables.Should().Contain("sync_outbox");

        var columns = await GetColumnNamesAsync(connection, "recording_queue");
        columns.Should().Contain(ExpectedProvenanceColumns);

        var (found, isUnique) = await TryGetIndexAsync(connection, "recording_queue", SourceCaptureIdIndexName);
        found.Should().BeTrue($"index {SourceCaptureIdIndexName} should exist on recording_queue");
        isUnique.Should().BeTrue($"index {SourceCaptureIdIndexName} should be UNIQUE");
    }

    private static async Task<HashSet<string>> GetTableNamesAsync(SqliteConnection connection)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task<HashSet<string>> GetColumnNamesAsync(SqliteConnection connection, string tableName)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\")";
        await using var reader = await command.ExecuteReaderAsync();
        var nameOrdinal = reader.GetOrdinal("name");
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(nameOrdinal));
        }

        return columns;
    }

    private static async Task<(bool Found, bool IsUnique)> TryGetIndexAsync(
        SqliteConnection connection, string tableName, string indexName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_list(\"{tableName}\")";
        await using var reader = await command.ExecuteReaderAsync();
        var nameOrdinal = reader.GetOrdinal("name");
        var uniqueOrdinal = reader.GetOrdinal("unique");
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(nameOrdinal), indexName, StringComparison.OrdinalIgnoreCase))
            {
                return (true, reader.GetInt64(uniqueOrdinal) == 1);
            }
        }

        return (false, false);
    }

    private static async Task InsertQueueRowAsync(
        SqliteConnection connection, string queueId, string? sourceCaptureId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO recording_queue (queue_id, audio_file_path, status, created_at, source_capture_id) " +
            "VALUES ($queueId, '/audio.wav', 'pending', '2026-01-01 00:00:00', $sourceCaptureId);";
        command.Parameters.AddWithValue("$queueId", queueId);
        command.Parameters.AddWithValue("$sourceCaptureId", (object?)sourceCaptureId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    #endregion
}
