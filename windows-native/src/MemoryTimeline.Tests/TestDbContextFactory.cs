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
    public static TestDbContextFactory CreateSqliteFile(string databaseFilePath)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databaseFilePath}")
            .Options;
        return new TestDbContextFactory(options);
    }
}
