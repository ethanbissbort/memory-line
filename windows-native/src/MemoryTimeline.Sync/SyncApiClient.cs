using System.Net.Http.Json;
using MemoryTimeline.SyncContracts;
using Microsoft.Extensions.Logging;

namespace MemoryTimeline.Sync;

/// <summary>
/// <see cref="ISyncClient"/> over the Memory Line Sync API v1 (design §11.2,
/// shared-contracts/openapi/memory-line-sync-v1.yaml). The base URL and device
/// tokens come from <see cref="ISyncSettingsStore"/>; a 401 triggers one
/// transparent token refresh before the call fails. All failures surface as
/// <see cref="SyncApiException"/> with the design §16.2 retry classification.
/// </summary>
public class SyncApiClient : ISyncClient
{
    private const string ApiBasePath = "/api/v1";

    private readonly ISyncSettingsStore _settingsStore;
    private readonly ILogger<SyncApiClient> _logger;
    private readonly SyncApiHttp _http;

    public SyncApiClient(HttpClient httpClient, ISyncSettingsStore settingsStore, ILogger<SyncApiClient> logger)
    {
        _settingsStore = settingsStore;
        _logger = logger;
        _http = new SyncApiHttp(httpClient, settingsStore, logger);
    }

    /// <inheritdoc />
    public async Task<DeviceRegisterResponse> RegisterDeviceAsync(
        string serverUrl,
        DeviceRegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverUrl);
        ArgumentNullException.ThrowIfNull(request);

        // Registration is the one unauthenticated call and uses the explicit
        // URL, not stored settings: the caller persists both on success.
        var baseUrl = serverUrl.TrimEnd('/');
        using var response = await _http.SendAsync(
            baseUrl,
            () => new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}{ApiBasePath}/devices/register")
            {
                Content = JsonContent.Create(request, options: SyncJson.Options),
            },
            authenticated: false,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        var registration = await SyncApiHttp.ReadAsync<DeviceRegisterResponse>(response, cancellationToken);
        _logger.LogInformation("Registered this machine as sync device {DeviceId}", registration.DeviceId);
        return registration;
    }

    /// <inheritdoc />
    public async Task<SyncPullResponse> PullChangesAsync(
        long cursor, int limit, CancellationToken cancellationToken = default)
    {
        var baseUrl = await _http.GetRequiredBaseUrlAsync();
        using var response = await _http.SendAsync(
            baseUrl,
            () => new HttpRequestMessage(
                HttpMethod.Get,
                $"{baseUrl}{ApiBasePath}/sync/pull?cursor={cursor}&limit={limit}"),
            authenticated: true,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        return await SyncApiHttp.ReadAsync<SyncPullResponse>(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SyncPushResponse> PushChangesAsync(
        SyncPushRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var baseUrl = await _http.GetRequiredBaseUrlAsync();
        using var response = await _http.SendAsync(
            baseUrl,
            () => new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}{ApiBasePath}/sync/push")
            {
                Content = JsonContent.Create(request, options: SyncJson.Options),
            },
            authenticated: true,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        return await SyncApiHttp.ReadAsync<SyncPushResponse>(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AckAsync(long cursor, CancellationToken cancellationToken = default)
    {
        var baseUrl = await _http.GetRequiredBaseUrlAsync();
        using var response = await _http.SendAsync(
            baseUrl,
            () => new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}{ApiBasePath}/sync/ack")
            {
                Content = JsonContent.Create(new SyncAckRequest { Cursor = cursor }, options: SyncJson.Options),
            },
            authenticated: true,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task RevokeDeviceAsync(CancellationToken cancellationToken = default)
    {
        var baseUrl = await _http.GetRequiredBaseUrlAsync();
        var deviceId = await _settingsStore.GetDeviceIdAsync();
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new SyncApiException("No sync device is registered.", null, null, retryable: false);
        }

        using var response = await _http.SendAsync(
            baseUrl,
            () => new HttpRequestMessage(
                HttpMethod.Delete,
                $"{baseUrl}{ApiBasePath}/devices/{Uri.EscapeDataString(deviceId)}"),
            authenticated: true,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        _logger.LogInformation("Revoked sync device {DeviceId}", deviceId);
    }
}
