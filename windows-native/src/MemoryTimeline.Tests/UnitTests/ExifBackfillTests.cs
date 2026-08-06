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
/// Tests for the F10 EXIF GPS backfill (spec: "EXIF GPS on an attached photo
/// populates the event's location coordinates when the location has none").
/// The rule itself is exercised through MediaService's internal
/// BackfillEventLocationCoordinatesAsync (InternalsVisibleTo) - AttachAsync
/// calls it whenever an image carries GPS, but a binary GPS-tagged JPEG
/// fixture would be needed to drive it end-to-end; the wiring is three lines
/// and the no-GPS path is covered below via a full AttachAsync.
/// </summary>
public class ExifBackfillTests : IDisposable
{
    private readonly TestDbContextFactory _contextFactory;
    private readonly MediaService _mediaService;
    private readonly string _mediaRoot;
    private readonly string _sourceDir;

    public ExifBackfillTests()
    {
        _contextFactory = TestDbContextFactory.CreateInMemory();
        var mediaRepository = new EventMediaRepository(_contextFactory);
        var thumbnailMock = new Mock<IThumbnailGenerator>();
        thumbnailMock
            .Setup(t => t.TryGenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mediaRoot = Path.Combine(Path.GetTempPath(), $"ExifBackfillTests_Root_{Guid.NewGuid()}");
        _sourceDir = Path.Combine(Path.GetTempPath(), $"ExifBackfillTests_Src_{Guid.NewGuid()}");
        Directory.CreateDirectory(_sourceDir);

        _mediaService = new MediaService(
            mediaRepository,
            _contextFactory,
            thumbnailMock.Object,
            new Mock<ILogger<MediaService>>().Object,
            _mediaRoot);
    }

    [Fact]
    public async Task Backfill_EventWithLocatedAndUnlocatedLocations_FillsOnlyTheUnlocatedOne()
    {
        // Arrange - one linked location already has coordinates, one has none
        await SeedEventAsync("event-1");
        await SeedLocationAsync("loc-located", "Amsterdam", latitude: 52.37, longitude: 4.89,
            geocodedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await SeedLocationAsync("loc-unlocated", "The beach house");
        await SeedLinkAsync("event-1", "loc-located");
        await SeedLinkAsync("event-1", "loc-unlocated");

        // Act - the photo says (48.8566, 2.3522)
        await _mediaService.BackfillEventLocationCoordinatesAsync("event-1", 48.8566, 2.3522);

        // Assert - only the coordinate-less location was filled
        var located = await GetLocationAsync("loc-located");
        located.Latitude.Should().Be(52.37, "existing coordinates are never overwritten");
        located.Longitude.Should().Be(4.89);
        located.GeocodedAt.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var unlocated = await GetLocationAsync("loc-unlocated");
        unlocated.Latitude.Should().Be(48.8566);
        unlocated.Longitude.Should().Be(2.3522);
        unlocated.GeocodedAt.Should().BeNull("EXIF coordinates do not come from a geocoder");
    }

    [Fact]
    public async Task Backfill_UnlinkedLocation_IsNeverTouched()
    {
        // Arrange - an unlocated location that belongs to a DIFFERENT event
        await SeedEventAsync("event-1");
        await SeedEventAsync("event-2");
        await SeedLocationAsync("loc-other", "Somewhere else");
        await SeedLinkAsync("event-2", "loc-other");

        // Act
        await _mediaService.BackfillEventLocationCoordinatesAsync("event-1", 10.0, 20.0);

        // Assert
        var other = await GetLocationAsync("loc-other");
        other.Latitude.Should().BeNull();
        other.Longitude.Should().BeNull();
    }

    [Fact]
    public async Task Backfill_HalfSetCoordinates_AreTreatedAsMissingAndOverwritten()
    {
        // Arrange - latitude without longitude is unusable; the pair is replaced
        await SeedEventAsync("event-1");
        await SeedLocationAsync("loc-half", "Half set", latitude: 1.0);
        await SeedLinkAsync("event-1", "loc-half");

        // Act
        await _mediaService.BackfillEventLocationCoordinatesAsync("event-1", -33.87, 151.21);

        // Assert
        var half = await GetLocationAsync("loc-half");
        half.Latitude.Should().Be(-33.87);
        half.Longitude.Should().Be(151.21);
    }

    [Fact]
    public async Task AttachAsync_ImageWithoutGps_LeavesLinkedLocationsUntouched()
    {
        // Arrange - a fake JPEG with no readable EXIF at all
        await SeedEventAsync("event-1");
        await SeedLocationAsync("loc-unlocated", "The beach house");
        await SeedLinkAsync("event-1", "loc-unlocated");
        var source = Path.Combine(_sourceDir, "no-gps.jpg");
        File.WriteAllBytes(source, Encoding.UTF8.GetBytes("pretend this is a jpeg"));

        // Act
        await _mediaService.AttachAsync("event-1", source);

        // Assert - no GPS, no backfill
        var location = await GetLocationAsync("loc-unlocated");
        location.Latitude.Should().BeNull();
        location.Longitude.Should().BeNull();
    }

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

    private async Task SeedLinkAsync(string eventId, string locationId)
    {
        await using var context = _contextFactory.CreateDbContext();
        context.EventLocations.Add(new EventLocation
        {
            EventId = eventId,
            LocationId = locationId
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
