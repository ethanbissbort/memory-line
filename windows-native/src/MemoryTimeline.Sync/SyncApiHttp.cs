using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MemoryTimeline.SyncContracts;
using Microsoft.Extensions.Logging;

namespace MemoryTimeline.Sync;

/// <summary>
/// Shared HTTP plumbing for <see cref="SyncApiClient"/> and
/// <see cref="ArtifactTransferClient"/>: base-URL resolution from settings,
/// bearer-token attachment, a single transparent token refresh on 401
/// (serialized process-wide so concurrent clients never race the server's
/// refresh-token rotation), and
/// uniform mapping of error responses and transport failures to
/// <see cref="SyncApiException"/>. Log lines never include tokens or payload
/// content (design §14.5).
/// </summary>
internal sealed class SyncApiHttp
{
    // Serializes token refresh across ALL instances: the worker's and the UI's
    // clients share one settings store, and the server rotates the stored
    // refresh token on every refresh — two concurrent refreshes would strand
    // whichever caller loses the race with a dead refresh token.
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    private readonly HttpClient _httpClient;
    private readonly ISyncSettingsStore _settingsStore;
    private readonly ILogger _logger;

    internal SyncApiHttp(HttpClient httpClient, ISyncSettingsStore settingsStore, ILogger logger)
    {
        _httpClient = httpClient;
        _settingsStore = settingsStore;
        _logger = logger;
    }

    /// <summary>Resolves the configured server URL (trailing slashes trimmed) or throws.</summary>
    internal async Task<string> GetRequiredBaseUrlAsync()
    {
        var url = await _settingsStore.GetServerUrlAsync();
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new SyncApiException("Sync server URL is not configured.", null, null, retryable: false);
        }

        return url.TrimEnd('/');
    }

    /// <summary>
    /// Sends a request and returns the SUCCESS response (caller disposes it).
    /// Authenticated requests carry the stored access token; one 401 triggers a
    /// token refresh against <paramref name="baseUrl"/> and a single retry of a
    /// freshly built request. Every failure surfaces as <see cref="SyncApiException"/>.
    /// </summary>
    /// <param name="requestFactory">
    /// Builds the request. Invoked once per attempt because an
    /// <see cref="HttpRequestMessage"/> (and its content) cannot be resent.
    /// </param>
    internal async Task<HttpResponseMessage> SendAsync(
        string baseUrl,
        Func<HttpRequestMessage> requestFactory,
        bool authenticated,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        var response = await SendOnceAsync(requestFactory, authenticated, completionOption, cancellationToken);

        if (authenticated && response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            if (!await TryRefreshTokensAsync(baseUrl, cancellationToken))
            {
                throw new SyncApiException(
                    "The sync service rejected the device credentials and the access token could not be refreshed.",
                    HttpStatusCode.Unauthorized,
                    null,
                    retryable: false);
            }

            response = await SendOnceAsync(requestFactory, authenticated, completionOption, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = await CreateErrorAsync(response, cancellationToken);
            response.Dispose();
            throw error;
        }

        return response;
    }

    /// <summary>Deserializes a success-response body, or throws a non-retryable <see cref="SyncApiException"/>.</summary>
    internal static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        T? value;
        try
        {
            value = await response.Content.ReadFromJsonAsync<T>(SyncJson.Options, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new SyncApiException(
                $"The sync service returned a malformed {typeof(T).Name} body.",
                response.StatusCode,
                null,
                retryable: false,
                ex);
        }

        return value ?? throw new SyncApiException(
            $"The sync service returned an empty {typeof(T).Name} body.",
            response.StatusCode,
            null,
            retryable: false);
    }

    /// <summary>408/429/5xx are retryable; other statuses are not (design §16.2).</summary>
    internal static bool IsRetryableStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private async Task<HttpResponseMessage> SendOnceAsync(
        Func<HttpRequestMessage> requestFactory,
        bool authenticated,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        using var request = requestFactory();
        if (authenticated)
        {
            var accessToken = await _settingsStore.GetAccessTokenAsync();
            if (!string.IsNullOrEmpty(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
        }

        try
        {
            return await _httpClient.SendAsync(request, completionOption, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new SyncApiException("The sync service could not be reached.", null, null, retryable: true, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout surfaces as TaskCanceledException without our token being signalled.
            throw new SyncApiException("The sync service request timed out.", null, null, retryable: true, ex);
        }
    }

    private async Task<bool> TryRefreshTokensAsync(string baseUrl, CancellationToken cancellationToken)
    {
        var deviceId = await _settingsStore.GetDeviceIdAsync();
        var refreshToken = await _settingsStore.GetRefreshTokenAsync();
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(refreshToken))
        {
            _logger.LogWarning("Access token was rejected but no device ID/refresh token is stored; cannot refresh");
            return false;
        }

        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have refreshed while we waited: the stored
            // refresh token differing from the one that triggered this refresh
            // means the pair was already rotated. Replaying our now-invalidated
            // token would fail (and strand this client), so skip the network
            // call — the retried request re-reads the fresh access token.
            var storedRefreshToken = await _settingsStore.GetRefreshTokenAsync();
            if (!string.IsNullOrWhiteSpace(storedRefreshToken) &&
                !string.Equals(storedRefreshToken, refreshToken, StringComparison.Ordinal))
            {
                _logger.LogDebug(
                    "Sync tokens for device {DeviceId} were already refreshed by a concurrent caller", deviceId);
                return true;
            }

            return await RefreshTokensCoreAsync(baseUrl, deviceId, refreshToken, cancellationToken);
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private async Task<bool> RefreshTokensCoreAsync(
        string baseUrl, string deviceId, string refreshToken, CancellationToken cancellationToken)
    {
        using var response = await SendOnceAsync(
            () => new HttpRequestMessage(
                HttpMethod.Post,
                $"{baseUrl}/api/v1/devices/{Uri.EscapeDataString(deviceId)}/refresh")
            {
                Content = JsonContent.Create(
                    new TokenRefreshRequest { RefreshToken = refreshToken },
                    options: SyncJson.Options),
            },
            authenticated: false,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Token refresh for device {DeviceId} failed with status {StatusCode}",
                deviceId, (int)response.StatusCode);
            return false;
        }

        TokenRefreshResponse? tokens;
        try
        {
            tokens = await response.Content.ReadFromJsonAsync<TokenRefreshResponse>(SyncJson.Options, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Token refresh for device {DeviceId} returned a malformed body", deviceId);
            return false;
        }

        if (tokens == null || string.IsNullOrEmpty(tokens.AccessToken) || string.IsNullOrEmpty(tokens.RefreshToken))
        {
            _logger.LogWarning("Token refresh for device {DeviceId} returned an incomplete token pair", deviceId);
            return false;
        }

        await _settingsStore.SaveTokensAsync(tokens.AccessToken, tokens.RefreshToken);
        _logger.LogInformation("Refreshed sync access token for device {DeviceId}", deviceId);
        return true;
    }

    private async Task<SyncApiException> CreateErrorAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ApiError? apiError = null;
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    apiError = JsonSerializer.Deserialize<ApiError>(body, SyncJson.Options);
                }
                catch (JsonException)
                {
                    // Not an ApiError envelope (e.g. a proxy error page); the status code alone classifies it.
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the error response body");
        }

        var statusCode = response.StatusCode;
        var retryable = IsRetryableStatus(statusCode);
        _logger.LogWarning(
            "Sync API request to {Path} failed with status {StatusCode} (code {ErrorCode}, retryable {Retryable})",
            response.RequestMessage?.RequestUri?.AbsolutePath, (int)statusCode, apiError?.Code, retryable);

        var message = apiError == null
            ? $"Sync API request failed with status {(int)statusCode}."
            : $"Sync API request failed with status {(int)statusCode}: {apiError.Code} - {apiError.Message}";
        return new SyncApiException(message, statusCode, apiError, retryable);
    }
}
