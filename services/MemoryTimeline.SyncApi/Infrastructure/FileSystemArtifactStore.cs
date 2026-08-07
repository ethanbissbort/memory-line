using System.Security.Cryptography;
using MemoryTimeline.SyncApi.Application;

namespace MemoryTimeline.SyncApi.Infrastructure;

/// <summary>
/// <see cref="IArtifactStore"/> over the local filesystem. Layout under
/// {DataDir}/artifacts: parts at {guid}/part_00001..., assembled blob at
/// {guid}.bin. Part writes and assembly go through temp files so a crashed
/// request never leaves a partially written final file.
/// </summary>
public sealed class FileSystemArtifactStore : IArtifactStore
{
    private const int BufferSize = 81920;

    private readonly string _artifactsDirectory;
    private readonly ILogger<FileSystemArtifactStore> _logger;

    public FileSystemArtifactStore(SyncServerEnvironment environment, ILogger<FileSystemArtifactStore> logger)
    {
        _artifactsDirectory = environment.ArtifactsDirectory;
        _logger = logger;
    }

    public string GetAssembledPath(Guid artifactId)
        => Path.Combine(_artifactsDirectory, $"{artifactId:D}.bin");

    public bool AssembledExists(Guid artifactId) => File.Exists(GetAssembledPath(artifactId));

    public bool PartExists(Guid artifactId, int partNumber) => File.Exists(GetPartPath(artifactId, partNumber));

    public int? FirstMissingPart(Guid artifactId, int partCount)
    {
        for (var part = 1; part <= partCount; part++)
        {
            if (!PartExists(artifactId, part))
            {
                return part;
            }
        }

        return null;
    }

    public async Task<long> WritePartAsync(
        Guid artifactId,
        int partNumber,
        Stream content,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var partPath = GetPartPath(artifactId, partNumber);
        Directory.CreateDirectory(Path.GetDirectoryName(partPath)!);
        var tempPath = partPath + ".tmp";

        try
        {
            long total = 0;
            await using (var output = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                    {
                        throw new ArtifactPartTooLargeException(maxBytes);
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await output.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, partPath, overwrite: true);
            return total;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    public async Task<ArtifactAssemblyResult> AssembleToTempAsync(
        Guid artifactId,
        int partCount,
        CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(_artifactsDirectory, $"{artifactId:D}.bin.tmp");

        try
        {
            long total = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                var buffer = new byte[BufferSize];
                for (var part = 1; part <= partCount; part++)
                {
                    await using var input = new FileStream(
                        GetPartPath(artifactId, part), FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
                    int read;
                    while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                    {
                        hash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        total += read;
                    }
                }

                await output.FlushAsync(cancellationToken);
            }

            var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            return new ArtifactAssemblyResult(tempPath, total, sha256);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    public void CommitAssembly(Guid artifactId, string tempPath)
        => File.Move(tempPath, GetAssembledPath(artifactId), overwrite: true);

    public void DiscardAssembly(string tempPath) => TryDelete(tempPath);

    public void DeleteParts(Guid artifactId)
    {
        var partDirectory = Path.Combine(_artifactsDirectory, artifactId.ToString("D"));
        if (!Directory.Exists(partDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(partDirectory, recursive: true);
        }
        catch (IOException ex)
        {
            // Orphaned parts waste disk but do not affect correctness; surfaced for cleanup.
            _logger.LogWarning(ex, "Failed to delete part directory for artifact {ArtifactId}.", artifactId);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Failed to delete part directory for artifact {ArtifactId}.", artifactId);
        }
    }

    private string GetPartPath(Guid artifactId, int partNumber)
        => Path.Combine(_artifactsDirectory, artifactId.ToString("D"), $"part_{partNumber:D5}");

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to delete temporary file {Path}.", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Failed to delete temporary file {Path}.", path);
        }
    }
}
