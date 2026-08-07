using System.Security.Cryptography;
using MemoryTimeline.SyncApi.Application;
using MemoryTimeline.SyncApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace MemoryTimeline.SyncApi.Infrastructure;

/// <summary>
/// Startup bootstrap for the sync service: creates the data directory,
/// materializes the token signing key and pairing code (from configuration or
/// generated-and-persisted files), creates the SQLite schema, and ensures the
/// single owner row exists (design §4.2 self-hosted mode).
/// </summary>
public static class SyncApiBootstrapper
{
    private const string SigningKeyFileName = "signing.key";
    private const string PairingCodeFileName = "pairing-code.txt";
    private const int MinimumSigningKeyBytes = 32;

    /// <summary>Idempotency record retention window (design §11.4).</summary>
    private const int IdempotencyRetentionHours = 48;

    /// <summary>
    /// Validates options and prepares the on-disk environment. Runs before the
    /// host is built because the JWT signing key must exist for authentication
    /// setup. Throws <see cref="InvalidOperationException"/> on invalid config.
    /// </summary>
    public static SyncServerEnvironment Initialize(SyncApiOptions options)
    {
        if (options.AccessTokenLifetimeMinutes <= 0)
        {
            throw new InvalidOperationException("SyncApi:AccessTokenLifetimeMinutes must be positive.");
        }

        if (options.RefreshTokenLifetimeDays <= 0)
        {
            throw new InvalidOperationException("SyncApi:RefreshTokenLifetimeDays must be positive.");
        }

        if (options.RefreshTokenGraceSeconds < 0)
        {
            throw new InvalidOperationException("SyncApi:RefreshTokenGraceSeconds must be zero or positive.");
        }

        if (options.MaxPartSizeBytes <= 0)
        {
            throw new InvalidOperationException("SyncApi:MaxPartSizeBytes must be positive.");
        }

        var dataDir = Path.GetFullPath(string.IsNullOrWhiteSpace(options.DataDir) ? "data" : options.DataDir);
        Directory.CreateDirectory(dataDir);
        var artifactsDir = Path.Combine(dataDir, "artifacts");
        Directory.CreateDirectory(artifactsDir);

        return new SyncServerEnvironment
        {
            DataDirectory = dataDir,
            ArtifactsDirectory = artifactsDir,
            DatabasePath = Path.Combine(dataDir, "sync.db"),
            SigningKey = ResolveSigningKey(options, dataDir),
            PairingCode = ResolvePairingCode(options, dataDir),
        };
    }

    /// <summary>
    /// Creates the schema (EnsureCreated — the service owns it) and the single
    /// owner row on first run, and sweeps idempotency records older than the
    /// retention window (design §11.4). Returns the owner ID.
    /// </summary>
    public static async Task<string> InitializeDatabaseAsync(IServiceProvider services)
    {
        var factory = services.GetRequiredService<IDbContextFactory<SyncDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        await EnsureCaptureStatusTableAsync(db);

        var retentionCutoffUtc = DateTime.UtcNow.AddHours(-IdempotencyRetentionHours);
        await db.IdempotencyRecords
            .Where(r => r.CreatedAtUtc < retentionCutoffUtc)
            .ExecuteDeleteAsync();

        var owner = await db.Owners.OrderBy(o => o.CreatedAtUtc).FirstOrDefaultAsync();
        if (owner is null)
        {
            owner = new Owner
            {
                OwnerId = Guid.NewGuid().ToString("D"),
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.Owners.Add(owner);
            await db.SaveChangesAsync();
        }

        return owner.OwnerId;
    }

    /// <summary>
    /// Creates the capture_status projection table (design §19 Phase 3) on
    /// databases created before it existed: EnsureCreated builds the whole
    /// schema on first run but leaves an existing database untouched, so a
    /// later table has to be created explicitly. Both statements are
    /// idempotent, so the fresh-database path is a no-op. Column names and
    /// types mirror the CaptureStatusRow mapping in <see cref="SyncDbContext"/>.
    /// </summary>
    private static async Task EnsureCaptureStatusTableAsync(SyncDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS capture_status (
                capture_id TEXT NOT NULL CONSTRAINT PK_capture_status PRIMARY KEY,
                owner_id TEXT NOT NULL,
                status TEXT NOT NULL,
                processing_stage TEXT NULL,
                updated_at_utc TEXT NOT NULL,
                transcript_available INTEGER NOT NULL,
                transcript_preview TEXT NULL,
                transcript_char_count INTEGER NULL,
                pending_event_count INTEGER NULL,
                approved_event_count INTEGER NULL,
                failure_reason TEXT NULL,
                failure_retryable INTEGER NULL,
                revision INTEGER NOT NULL,
                received_at_utc TEXT NOT NULL
            )
            """);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_capture_status_owner_id ON capture_status (owner_id)");
    }

    private static byte[] ResolveSigningKey(SyncApiOptions options, string dataDir)
    {
        if (!string.IsNullOrWhiteSpace(options.TokenSigningKeyBase64))
        {
            byte[] key;
            try
            {
                key = Convert.FromBase64String(options.TokenSigningKeyBase64.Trim());
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("SyncApi:TokenSigningKeyBase64 is not valid base64.", ex);
            }

            if (key.Length < MinimumSigningKeyBytes)
            {
                throw new InvalidOperationException(
                    $"SyncApi:TokenSigningKeyBase64 must decode to at least {MinimumSigningKeyBytes} bytes.");
            }

            return key;
        }

        var keyPath = Path.Combine(dataDir, SigningKeyFileName);
        if (File.Exists(keyPath))
        {
            byte[]? key = null;
            try
            {
                key = Convert.FromBase64String(File.ReadAllText(keyPath).Trim());
            }
            catch (FormatException)
            {
                // Fall through to the explicit corrupt-file error below.
            }

            if (key is { Length: >= MinimumSigningKeyBytes })
            {
                return key;
            }

            throw new InvalidOperationException(
                $"Signing key file '{keyPath}' is corrupt or too short; delete it to regenerate " +
                "(existing access tokens will be invalidated).");
        }

        var generated = RandomNumberGenerator.GetBytes(64);
        File.WriteAllText(keyPath, Convert.ToBase64String(generated));
        return generated;
    }

    private static string ResolvePairingCode(SyncApiOptions options, string dataDir)
    {
        var codePath = Path.Combine(dataDir, PairingCodeFileName);

        if (!string.IsNullOrWhiteSpace(options.PairingCode))
        {
            var configured = options.PairingCode.Trim();
            File.WriteAllText(codePath, configured);
            return configured;
        }

        if (File.Exists(codePath))
        {
            var existing = File.ReadAllText(codePath).Trim();
            if (existing.Length > 0)
            {
                return existing;
            }
        }

        var generated = GeneratePairingCode();
        File.WriteAllText(codePath, generated);
        return generated;
    }

    private static string GeneratePairingCode()
    {
        var hex = Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
        return $"{hex[..4]}-{hex[4..8]}-{hex[8..]}";
    }
}
