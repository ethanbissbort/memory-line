using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Tests;
using Xunit;

namespace MemoryTimeline.Tests.Integration;

/// <summary>
/// Integration tests for the 2026-08 F10 geographic-data schema upgrade on
/// locations against a real file-based SQLite database: an old-shape database
/// (locations without latitude/longitude/place_type/canonical_name/geocoded_at)
/// is built via raw DDL, upgraded with
/// <see cref="SchemaUpgrader.EnsureSchemaAsync"/>, and inspected via PRAGMA.
/// </summary>
public class GeoSchemaTests : IDisposable
{
    private static readonly string[] ExpectedLocationColumns =
    {
        "location_id", "name", "created_at",
        "latitude", "longitude", "place_type", "canonical_name", "geocoded_at"
    };

    private readonly List<(TestDbContextFactory Factory, string DatabasePath)> _databases = new();

    [Fact]
    public async Task EnsureSchemaAsync_OldShapeLocations_AddsGeoColumns_Idempotently()
    {
        // Arrange - build an OLD-shape database: locations with only the
        // original three columns (plus its unique name index) and a legacy row
        var factory = CreateFactory();
        await using (var setupContext = factory.CreateDbContext())
        {
            var connection = setupContext.Database.GetDbConnection();
            await connection.OpenAsync();
            await ExecuteAsync(connection,
                """
                CREATE TABLE "locations" (
                    "location_id" TEXT NOT NULL CONSTRAINT "PK_locations" PRIMARY KEY,
                    "name" TEXT NOT NULL,
                    "created_at" TEXT NOT NULL
                );
                """);
            await ExecuteAsync(connection,
                "CREATE UNIQUE INDEX \"IX_locations_name\" ON \"locations\" (\"name\");");
            await ExecuteAsync(connection,
                "INSERT INTO \"locations\" (\"location_id\", \"name\", \"created_at\") " +
                "VALUES ('legacy-1', 'Paris', '2024-01-01 00:00:00');");

            // Old-shape app_settings so the seed-parity block runs: the three
            // F10 keys must arrive with their defaults (geocoding OFF).
            await ExecuteAsync(connection,
                """
                CREATE TABLE "app_settings" (
                    "setting_key" TEXT NOT NULL CONSTRAINT "PK_app_settings" PRIMARY KEY,
                    "setting_value" TEXT NOT NULL,
                    "updated_at" TEXT NOT NULL
                );
                """);
        }

        // Act - run the upgrade (EnsureCreated is a no-op because a table exists)
        await using (var upgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(upgradeContext);
        }

        // Assert - the five new columns exist
        await using (var verifyContext = factory.CreateDbContext())
        {
            var connection = verifyContext.Database.GetDbConnection();
            await connection.OpenAsync();
            var columns = await GetColumnNamesAsync(connection, "locations");
            columns.Should().BeEquivalentTo(ExpectedLocationColumns);
        }

        // Assert - seed parity: the F10 setting keys arrived with geocoding OFF
        await using (var settingsContext = factory.CreateDbContext())
        {
            var settings = await settingsContext.AppSettings.AsNoTracking()
                .ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue);
            settings.Should().ContainKey("geocoding_enabled").WhoseValue.Should().Be("false");
            settings.Should().ContainKey("geocoding_provider").WhoseValue.Should().Be("nominatim");
            settings.Should().ContainKey("map_default_zoom").WhoseValue.Should().Be("1.0");
        }

        // Act again - a second upgrade must be a safe no-op
        await using (var secondUpgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(secondUpgradeContext);
        }

        // Assert - schema unchanged; the legacy row reads through EF with all
        // geo fields absent (nullable columns default NULL)
        await using (var readContext = factory.CreateDbContext())
        {
            var connection = readContext.Database.GetDbConnection();
            await connection.OpenAsync();
            var columns = await GetColumnNamesAsync(connection, "locations");
            columns.Should().BeEquivalentTo(ExpectedLocationColumns);

            var legacy = await readContext.Locations.AsNoTracking().SingleAsync();
            legacy.LocationId.Should().Be("legacy-1");
            legacy.Name.Should().Be("Paris");
            legacy.Latitude.Should().BeNull();
            legacy.Longitude.Should().BeNull();
            legacy.PlaceType.Should().BeNull();
            legacy.CanonicalName.Should().BeNull();
            legacy.GeocodedAt.Should().BeNull();
            legacy.HasCoordinates.Should().BeFalse();
        }
    }

    [Fact]
    public async Task EnsureSchemaAsync_UpgradedDatabase_RoundTripsCoordinatesThroughEf()
    {
        // Arrange - old-shape database, then upgrade
        var factory = CreateFactory();
        await using (var setupContext = factory.CreateDbContext())
        {
            var connection = setupContext.Database.GetDbConnection();
            await connection.OpenAsync();
            await ExecuteAsync(connection,
                """
                CREATE TABLE "locations" (
                    "location_id" TEXT NOT NULL CONSTRAINT "PK_locations" PRIMARY KEY,
                    "name" TEXT NOT NULL,
                    "created_at" TEXT NOT NULL
                );
                """);
        }

        await using (var upgradeContext = factory.CreateDbContext())
        {
            await SchemaUpgrader.EnsureSchemaAsync(upgradeContext);
        }

        // Act - the upgraded schema must accept an EF write of a located place
        var geocodedAt = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        await using (var writeContext = factory.CreateDbContext())
        {
            writeContext.Locations.Add(new Location
            {
                LocationId = "geo-1",
                Name = "Sydney",
                Latitude = -33.8688,
                Longitude = 151.2093,
                PlaceType = "city",
                CanonicalName = "Sydney, New South Wales, Australia",
                GeocodedAt = geocodedAt
            });
            await writeContext.SaveChangesAsync();
        }

        // Assert - round-trips intact through a fresh context (REAL affinity
        // for coordinates, TEXT for the timestamp)
        await using (var readContext = factory.CreateDbContext())
        {
            var stored = await readContext.Locations.AsNoTracking().SingleAsync();
            stored.Latitude.Should().BeApproximately(-33.8688, 1e-9);
            stored.Longitude.Should().BeApproximately(151.2093, 1e-9);
            stored.PlaceType.Should().Be("city");
            stored.CanonicalName.Should().Be("Sydney, New South Wales, Australia");
            stored.GeocodedAt.Should().Be(geocodedAt);
            stored.HasCoordinates.Should().BeTrue();
        }
    }

    // ---- SQLite helpers ----

    private TestDbContextFactory CreateFactory()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"GeoSchemaTests_{Guid.NewGuid()}.db");
        var factory = TestDbContextFactory.CreateSqliteFile(databasePath);
        _databases.Add((factory, databasePath));
        return factory;
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> GetColumnNamesAsync(DbConnection connection, string tableName)
    {
        // Table names cannot be parameterized in PRAGMA statements; the names
        // used here are compile-time constants.
        var names = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\")";
        await using var reader = await command.ExecuteReaderAsync();
        var ordinal = reader.GetOrdinal("name");
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(ordinal));
        }

        return names;
    }

    public void Dispose()
    {
        foreach (var (factory, databasePath) in _databases)
        {
            using (var context = factory.CreateDbContext())
            {
                context.Database.EnsureDeleted();
            }

            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var path = databasePath + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
