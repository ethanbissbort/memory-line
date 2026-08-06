using FluentAssertions;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using MemoryTimeline.Tests;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Tests for the F10 coordinate plumbing on <see cref="LocationRepository"/>:
/// SetCoordinatesAsync validation and GeocodedAt stamping rules, plus the
/// located/unlocated split queries. EF InMemory + the real repository.
/// </summary>
public class LocationCoordinateTests : IDisposable
{
    private readonly TestDbContextFactory _contextFactory;
    private readonly LocationRepository _repository;

    public LocationCoordinateTests()
    {
        _contextFactory = TestDbContextFactory.CreateInMemory();
        _repository = new LocationRepository(_contextFactory);
    }

    #region SetCoordinatesAsync

    [Fact]
    public async Task SetCoordinatesAsync_ManualSource_SetsCoordinatesAndLeavesGeocodedAtNull()
    {
        // Arrange - the row previously carried a geocoder stamp
        await SeedLocationAsync("loc-1", "Paris", geocodedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // Act
        await _repository.SetCoordinatesAsync("loc-1", 48.8566, 2.3522, "manual");

        // Assert - manual coordinates clear the stale geocoder stamp
        var stored = await GetLocationAsync("loc-1");
        stored.Latitude.Should().Be(48.8566);
        stored.Longitude.Should().Be(2.3522);
        stored.GeocodedAt.Should().BeNull("manual coordinates do not come from a geocoder");
    }

    [Fact]
    public async Task SetCoordinatesAsync_ExifSource_LeavesGeocodedAtNull()
    {
        // Arrange
        await SeedLocationAsync("loc-1", "Lake house");

        // Act
        await _repository.SetCoordinatesAsync("loc-1", -12.5, 130.75, "exif");

        // Assert
        var stored = await GetLocationAsync("loc-1");
        stored.Latitude.Should().Be(-12.5);
        stored.Longitude.Should().Be(130.75);
        stored.GeocodedAt.Should().BeNull();
    }

    [Fact]
    public async Task SetCoordinatesAsync_GeocodedSource_StampsGeocodedAt()
    {
        // Arrange
        await SeedLocationAsync("loc-1", "Berlin");
        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        await _repository.SetCoordinatesAsync("loc-1", 52.52, 13.405, "geocoded");

        // Assert
        var stored = await GetLocationAsync("loc-1");
        stored.GeocodedAt.Should().NotBeNull();
        stored.GeocodedAt!.Value.Should().BeOnOrAfter(before);
    }

    [Theory]
    [InlineData(90.01)]
    [InlineData(-90.01)]
    [InlineData(double.NaN)]
    public async Task SetCoordinatesAsync_LatitudeOutOfRange_ThrowsAndPersistsNothing(double latitude)
    {
        // Arrange
        await SeedLocationAsync("loc-1", "Nowhere");

        // Act
        Func<Task> act = () => _repository.SetCoordinatesAsync("loc-1", latitude, 0, "manual");

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        (await GetLocationAsync("loc-1")).Latitude.Should().BeNull();
    }

    [Theory]
    [InlineData(180.01)]
    [InlineData(-180.01)]
    [InlineData(double.NaN)]
    public async Task SetCoordinatesAsync_LongitudeOutOfRange_ThrowsAndPersistsNothing(double longitude)
    {
        // Arrange
        await SeedLocationAsync("loc-1", "Nowhere");

        // Act
        Func<Task> act = () => _repository.SetCoordinatesAsync("loc-1", 0, longitude, "manual");

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        (await GetLocationAsync("loc-1")).Longitude.Should().BeNull();
    }

    [Fact]
    public async Task SetCoordinatesAsync_BoundaryValues_AreAccepted()
    {
        // Arrange - the poles / antimeridian themselves are valid
        await SeedLocationAsync("loc-1", "Edge of the world");

        // Act
        await _repository.SetCoordinatesAsync("loc-1", -90.0, 180.0, "manual");

        // Assert
        var stored = await GetLocationAsync("loc-1");
        stored.Latitude.Should().Be(-90.0);
        stored.Longitude.Should().Be(180.0);
    }

    [Fact]
    public async Task SetCoordinatesAsync_UnknownLocation_Throws()
    {
        // Act
        Func<Task> act = () => _repository.SetCoordinatesAsync("no-such-location", 10, 10, "manual");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Location not found*");
    }

    #endregion

    #region Located / unlocated split

    [Fact]
    public async Task GetLocatedAsync_And_GetUnlocatedAsync_SplitByCoordinatePresence()
    {
        // Arrange - located, fully unlocated, and half-set (treated as unlocated)
        await SeedLocationAsync("loc-located", "Amsterdam", latitude: 52.37, longitude: 4.89);
        await SeedLocationAsync("loc-none", "Grandma's kitchen");
        await SeedLocationAsync("loc-half", "Half set", latitude: 1.0);

        // Act
        var located = (await _repository.GetLocatedAsync()).ToList();
        var unlocated = (await _repository.GetUnlocatedAsync()).ToList();

        // Assert
        located.Select(l => l.LocationId).Should().Equal("loc-located");
        unlocated.Select(l => l.LocationId).Should().BeEquivalentTo("loc-none", "loc-half");
    }

    #endregion

    #region Helpers

    private async Task SeedLocationAsync(
        string locationId,
        string name,
        double? latitude = null,
        double? longitude = null,
        DateTime? geocodedAt = null)
    {
        await using var context = _contextFactory.CreateDbContext();
        context.Locations.Add(new Location
        {
            LocationId = locationId,
            Name = name,
            Latitude = latitude,
            Longitude = longitude,
            GeocodedAt = geocodedAt
        });
        await context.SaveChangesAsync();
    }

    private async Task<Location> GetLocationAsync(string locationId)
    {
        await using var context = _contextFactory.CreateDbContext();
        return context.Locations.Single(l => l.LocationId == locationId);
    }

    #endregion

    public void Dispose()
    {
        using var context = _contextFactory.CreateDbContext();
        context.Database.EnsureDeleted();
    }
}
