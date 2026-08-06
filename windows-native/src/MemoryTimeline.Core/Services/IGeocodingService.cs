namespace MemoryTimeline.Core.Services;

/// <summary>
/// Optional, OFF-by-default geocoding (F10): resolves place names to
/// coordinates via an online provider. Implementations MUST check
/// <see cref="SettingKeys.GeocodingEnabled"/> before ANY network activity and
/// throw <see cref="InvalidOperationException"/> while it is off - with
/// geocoding disabled, no place name ever leaves the machine.
/// </summary>
public interface IGeocodingService
{
    /// <summary>
    /// Looks up a place name and returns its best-match coordinates and the
    /// provider's canonical display name, or null when the provider found no
    /// match. Throws <see cref="InvalidOperationException"/> when geocoding is
    /// disabled (checked before any request is sent).
    /// </summary>
    Task<(double Latitude, double Longitude, string DisplayName)?> GeocodeAsync(string placeName, CancellationToken ct = default);

    /// <summary>
    /// Geocodes one stored location by its id and persists the result
    /// (coordinates, canonical name, geocoded-at stamp). Returns true when a
    /// match was found and saved, false when the provider had no match.
    /// Throws <see cref="InvalidOperationException"/> when geocoding is
    /// disabled or the location does not exist.
    /// </summary>
    Task<bool> GeocodeLocationAsync(string locationId, CancellationToken ct = default);

    /// <summary>
    /// Geocodes every location that is still missing coordinates, reporting
    /// (done, total) progress after each attempt. Individual lookup failures
    /// are logged and skipped; returns the number of locations successfully
    /// geocoded and persisted. Throws <see cref="InvalidOperationException"/>
    /// up front when geocoding is disabled.
    /// </summary>
    Task<int> GeocodeMissingAsync(IProgress<(int done, int total)>? progress = null, CancellationToken ct = default);
}
