using Microsoft.Extensions.Logging;

namespace MemoryTimeline.Sync;

/// <summary>
/// <see cref="IArtifactTransferClient"/> over
/// <c>GET /api/v1/artifacts/{artifactId}/download</c> (design §7.4). Bytes are
/// streamed to a temp sibling file and atomically moved into place, so a torn
/// download never leaves a partial file at the destination path. Content
/// integrity (byte length + SHA-256) is re-verified downstream by capture
/// ingestion, not here.
/// </summary>
public class ArtifactTransferClient : IArtifactTransferClient
{
    private readonly ILogger<ArtifactTransferClient> _logger;
    private readonly SyncApiHttp _http;

    public ArtifactTransferClient(
        HttpClient httpClient, ISyncSettingsStore settingsStore, ILogger<ArtifactTransferClient> logger)
    {
        _logger = logger;
        _http = new SyncApiHttp(httpClient, settingsStore, logger);
    }

    /// <inheritdoc />
    public async Task DownloadArtifactAsync(
        string artifactId, string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var baseUrl = await _http.GetRequiredBaseUrlAsync();
        using var response = await _http.SendAsync(
            baseUrl,
            () => new HttpRequestMessage(
                HttpMethod.Get,
                $"{baseUrl}/api/v1/artifacts/{Uri.EscapeDataString(artifactId)}/download"),
            authenticated: true,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var fullDestination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullDestination);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Stream to a temp sibling in the same directory, then move into place,
        // so the destination path only ever holds a complete file.
        var tempPath = $"{fullDestination}.{Guid.NewGuid():N}.partial";
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            File.Move(tempPath, fullDestination, overwrite: true);
        }
        catch (HttpRequestException ex)
        {
            TryDeleteTempFile(tempPath);
            throw new SyncApiException(
                $"The download of artifact {artifactId} was interrupted.", null, null, retryable: true, ex);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }

        _logger.LogInformation("Downloaded artifact {ArtifactId} to the local sync cache", artifactId);
    }

    private void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            // Cleanup only — the .partial file is inert and never read.
            _logger.LogWarning(ex, "Could not delete partial download file");
        }
    }
}
