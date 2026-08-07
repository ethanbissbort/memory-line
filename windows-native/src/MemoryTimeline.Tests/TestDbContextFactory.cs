using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data;

namespace MemoryTimeline.Tests;

/// <summary>
/// Test implementation of <see cref="IDbContextFactory{TContext}"/> that wraps a fixed
/// set of <see cref="DbContextOptions{AppDbContext}"/> so every <see cref="CreateDbContext"/>
/// call returns a NEW context instance targeting the SAME database.
///
/// Works for:
///  - EF InMemory named databases (all contexts built from the same options instance share
///    the same in-memory store root), and
///  - SQLite FILE databases (each context opens its own connection to the same file).
///
/// NOTE: do NOT use this with SQLite "DataSource=:memory:" connection strings — every new
/// connection would get a fresh, empty database. Tests that need SQLite must use a
/// file-based temp database (as the integration/performance tests here do).
/// </summary>
public sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDbContextFactory(DbContextOptions<AppDbContext> options)
    {
        _options = options;
    }

    /// <summary>The options every created context is built from.</summary>
    public DbContextOptions<AppDbContext> Options => _options;

    /// <inheritdoc />
    public AppDbContext CreateDbContext() => new(_options);

    /// <summary>
    /// Creates a factory over a uniquely named EF InMemory database
    /// (or the given name, for tests that need a deterministic name).
    /// </summary>
    public static TestDbContextFactory CreateInMemory(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"TestDb_{Guid.NewGuid()}")
            .Options;
        return new TestDbContextFactory(options);
    }

    /// <summary>
    /// Creates a factory over a SQLite FILE database at the given path.
    /// Each context opens its own connection to the same file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Pooling=False</c> is REQUIRED here, not a preference.
    /// <see cref="Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools"/> is
    /// process-global: it disposes the underlying <c>SQLitePCL.sqlite3</c>
    /// handle of every pooled connection, for every connection string. Three
    /// places call it — <c>BackupService.CreateBackupAsync</c>,
    /// <c>BackupServiceTests</c>, and <c>SchemaUpgraderSyncSchemaTests</c> —
    /// and xUnit runs test classes in parallel, so with pooling ON a clear in
    /// one class can dispose a handle another class is actively using. The
    /// victim then throws <see cref="ObjectDisposedException"/> from wherever
    /// it happened to be, typically the pragma interceptor's command in
    /// <c>ConnectionOpened</c> during <c>EnsureDeleted</c> teardown — which
    /// that interceptor does not catch (it only expects SqliteException).
    /// </para>
    /// <para>
    /// Unpooled connections are not in any pool, so a global clear cannot
    /// touch them. This also makes teardown deletion of the temp .db/-wal/-shm
    /// files reliable, since no pooled handle lingers on the file.
    /// </para>
    /// <para>
    /// Pass <paramref name="pooled"/>: true only for throughput measurement —
    /// see the parameter docs.
    /// </para>
    /// </remarks>
    /// <param name="databaseFilePath">Path to the SQLite file to open.</param>
    /// <param name="pooled">
    /// Opt back into connection pooling. Costs the caller immunity to the
    /// <c>ClearAllPools</c> race described above, so use it only where pooling
    /// is what is being measured: the app pools in production, so a throughput
    /// test that runs unpooled would be timing a code path that never ships
    /// (a fresh file open plus three PRAGMA round-trips per operation, which
    /// on a write-heavy loop dominates the number being asserted on).
    /// </param>
    public static TestDbContextFactory CreateSqliteFile(string databaseFilePath, bool pooled = false)
    {
        var connectionString = pooled
            ? $"Data Source={databaseFilePath}"
            : $"Data Source={databaseFilePath};Pooling=False";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new TestDbContextFactory(options);
    }
}
