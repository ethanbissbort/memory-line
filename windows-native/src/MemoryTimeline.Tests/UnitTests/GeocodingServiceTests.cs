using System.Net;
using System.Text;
using FluentAssertions;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Tests;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Tests for <see cref="NominatimGeocodingService"/>: the privacy gate (with
/// geocoding disabled the service throws BEFORE any request - the stub
/// handler proves ZERO network calls), fixture-JSON parsing on the enabled
/// path, persistence of geocode hits, and the batch entry point.
/// </summary>
public class GeocodingServiceTests : IDisposable
{
    /// <summary>A real Nominatim jsonv2 response shape for "Paris".</summary>
    private const string ParisJson =
        """
        [{"place_id":88053,"licence":"Data © OpenStreetMap contributors, ODbL 1.0","osm_type":"relation","osm_id":71525,"lat":"48.8588897","lon":"2.3200410217200766","category":"boundary","type":"administrative","place_rank":15,"importance":0.884,"addresstype":"suburb","name":"Paris","display_name":"Paris, Île-de-France, Metropolitan France, France","boundingbox":["48.8155755","48.9021560","2.2241220","2.4697602"]}]
        """;

    private readonly TestDbContextFactory _contextFactory;
    private readonly Mock<ISettingsService> _settingsMock;
    private readonly Mock<ILogger<NominatimGeocodingService>> _loggerMock;
    private StubHttpMessageHandler _handler = null!;

    public GeocodingServiceTests()
    {
        _contextFactory = TestDbContextFactory.CreateInMemory();
        _settingsMock = new Mock<ISettingsService>();
        _loggerMock = new Mock<ILogger<NominatimGeocodingService>>();
    }

    private NominatimGeocodingService CreateService(bool geocodingEnabled, string responseJson = "[]")
    {
        _settingsMock
            .Setup(s => s.GetSettingAsync<string>(SettingKeys.GeocodingEnabled, It.IsAny<string>()))
            .ReturnsAsync(geocodingEnabled ? "true" : "false");

        _handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });

        return new NominatimGeocodingService(
            new HttpClient(_handler),
            _settingsMock.Object,
            _contextFactory,
            _loggerMock.Object);
    }

    #region Disabled: the privacy gate

    [Fact]
    public async Task GeocodeAsync_Disabled_ThrowsAndSendsZeroRequests()
    {
        // Arrange
        var service = CreateService(geocodingEnabled: false, ParisJson);

        // Act
        Func<Task> act = () => service.GeocodeAsync("Paris");

        // Assert - throws BEFORE any request is built or sent
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Geocoding is disabled*");
        _handler.Requests.Should().BeEmpty("no place name may leave the machine while geocoding is off");
    }

    [Fact]
    public async Task GeocodeMissingAsync_Disabled_ThrowsAndSendsZeroRequests()
    {
        // Arrange - an unlocated location exists, tempting the batch to run
        await SeedLocationAsync("loc-1", "Paris");
        var service = CreateService(geocodingEnabled: false, ParisJson);

        // Act
        Func<Task> act = () => service.GeocodeMissingAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Geocoding is disabled*");
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GeocodeLocationAsync_Disabled_ThrowsAndSendsZeroRequests()
    {
        // Arrange
        await SeedLocationAsync("loc-1", "Paris");
        var service = CreateService(geocodingEnabled: false, ParisJson);

        // Act
        Func<Task> act = () => service.GeocodeLocationAsync("loc-1");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Geocoding is disabled*");
        _handler.Requests.Should().BeEmpty();
    }

    #endregion

    #region Enabled: parsing + persistence

    [Fact]
    public async Task GeocodeAsync_Enabled_ParsesTheNominatimFixture()
    {
        // Arrange
        var service = CreateService(geocodingEnabled: true, ParisJson);

        // Act
        var result = await service.GeocodeAsync("Paris, France");

        // Assert - lat/lon are strings in Nominatim JSON, parsed invariant
        result.Should().NotBeNull();
        result!.Value.Latitude.Should().BeApproximately(48.8588897, 1e-9);
        result.Value.Longitude.Should().BeApproximately(2.3200410217200766, 1e-9);
        result.Value.DisplayName.Should().Be("Paris, Île-de-France, Metropolitan France, France");

        // Assert - the single request carries the jsonv2 query and the
        // User-Agent the Nominatim usage policy requires
        _handler.Requests.Should().HaveCount(1);
        var url = _handler.Requests[0].RequestUri!.OriginalString;
        url.Should().StartWith("https://nominatim.openstreetmap.org/search");
        url.Should().Contain("format=jsonv2");
        url.Should().Contain("limit=1");
        url.Should().Contain(Uri.EscapeDataString("Paris, France"));
        _handler.Requests[0].Headers.UserAgent.ToString().Should().Contain("MemoryTimeline/1.0");
    }

    [Fact]
    public async Task GeocodeAsync_EmptyResultArray_ReturnsNull()
    {
        // Arrange
        var service = CreateService(geocodingEnabled: true, "[]");

        // Act
        var result = await service.GeocodeAsync("A place nobody has heard of");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GeocodeAsync_BlankName_ReturnsNullWithoutARequest()
    {
        // Arrange
        var service = CreateService(geocodingEnabled: true, ParisJson);

        // Act
        var result = await service.GeocodeAsync("   ");

        // Assert
        result.Should().BeNull();
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GeocodeLocationAsync_Hit_PersistsCoordinatesCanonicalNameAndStamp()
    {
        // Arrange
        await SeedLocationAsync("loc-1", "Paris");
        var service = CreateService(geocodingEnabled: true, ParisJson);
        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        var found = await service.GeocodeLocationAsync("loc-1");

        // Assert
        found.Should().BeTrue();
        var stored = await GetLocationAsync("loc-1");
        stored.Latitude.Should().BeApproximately(48.8588897, 1e-9);
        stored.Longitude.Should().BeApproximately(2.3200410217200766, 1e-9);
        stored.CanonicalName.Should().Be("Paris, Île-de-France, Metropolitan France, France");
        stored.GeocodedAt.Should().NotBeNull("a geocoder produced these coordinates");
        stored.GeocodedAt!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task GeocodeLocationAsync_NoMatch_ReturnsFalseAndPersistsNothing()
    {
        // Arrange
        await SeedLocationAsync("loc-1", "Grandma's kitchen");
        var service = CreateService(geocodingEnabled: true, "[]");

        // Act
        var found = await service.GeocodeLocationAsync("loc-1");

        // Assert
        found.Should().BeFalse();
        var stored = await GetLocationAsync("loc-1");
        stored.Latitude.Should().BeNull();
        stored.GeocodedAt.Should().BeNull();
    }

    [Fact]
    public async Task GeocodeMissingAsync_GeocodesOnlyUnlocatedAndReportsProgress()
    {
        // Arrange - one located (must be skipped), one unlocated
        await SeedLocationAsync("loc-located", "Home", latitude: 1.0, longitude: 2.0);
        await SeedLocationAsync("loc-missing", "Paris");
        var service = CreateService(geocodingEnabled: true, ParisJson);
        var reports = new List<(int done, int total)>();
        var progress = new ImmediateProgress<(int done, int total)>(reports);

        // Act
        var succeeded = await service.GeocodeMissingAsync(progress);

        // Assert
        succeeded.Should().Be(1);
        _handler.Requests.Should().HaveCount(1, "the located place must not be sent anywhere");
        reports.Should().Equal((1, 1));

        var located = await GetLocationAsync("loc-located");
        located.Latitude.Should().Be(1.0, "already-located places are untouched");
        located.GeocodedAt.Should().BeNull();

        var resolved = await GetLocationAsync("loc-missing");
        resolved.Latitude.Should().BeApproximately(48.8588897, 1e-9);
        resolved.GeocodedAt.Should().NotBeNull();
    }

    #endregion

    #region Helpers

    private async Task SeedLocationAsync(
        string locationId,
        string name,
        double? latitude = null,
        double? longitude = null)
    {
        await using var context = _contextFactory.CreateDbContext();
        context.Locations.Add(new Location
        {
            LocationId = locationId,
            Name = name,
            Latitude = latitude,
            Longitude = longitude
        });
        await context.SaveChangesAsync();
    }

    private async Task<Location> GetLocationAsync(string locationId)
    {
        await using var context = _contextFactory.CreateDbContext();
        return context.Locations.Single(l => l.LocationId == locationId);
    }

    /// <summary>
    /// Records every request and answers from a canned responder - proving
    /// "zero network calls" is a simple count assertion.
    /// </summary>
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    /// <summary>Synchronous IProgress capture (no sync-context race).</summary>
    private sealed class ImmediateProgress<T> : IProgress<T>
    {
        private readonly List<T> _reports;

        public ImmediateProgress(List<T> reports)
        {
            _reports = reports;
        }

        public void Report(T value) => _reports.Add(value);
    }

    #endregion

    public void Dispose()
    {
        using var context = _contextFactory.CreateDbContext();
        context.Database.EnsureDeleted();
    }
}
