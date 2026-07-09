using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MemoryTimeline.Data;

/// <summary>
/// Ensures the SQLite database schema matches the current EF Core model.
///
/// This class substitutes for EF Core migrations until a machine with the
/// .NET SDK / dotnet-ef can regenerate a proper migration baseline. The
/// previously checked-in migration was badly stale (it lacked the
/// era_categories, era_tags, milestones and saved_searches tables plus
/// several eras/events/tags columns) and has been deleted.
///
/// Strategy:
///  1. Database.EnsureCreatedAsync creates a brand-new database with the FULL
///     current model (including seed data) when no database file exists yet.
///  2. For databases created by OLDER builds, EnsureCreated is a no-op, so we
///     detect schema drift by inspecting sqlite_master / PRAGMA table_info and
///     apply idempotent, additive DDL (CREATE TABLE IF NOT EXISTS /
///     ALTER TABLE ... ADD COLUMN) for everything the current model needs that
///     older schemas are known to be missing.
///
/// All DDL below is derived from AppDbContext.OnModelCreating and the entity
/// models' snake_case [Table]/[Column] mappings (the source of truth), using
/// EF Core's SQLite type affinities: TEXT for strings/Guids/DateTimes,
/// INTEGER for int/bool/enums, REAL for double.
///
/// A repair failure is logged but never allowed to crash startup.
/// </summary>
public static class SchemaUpgrader
{
    public static async Task EnsureSchemaAsync(AppDbContext context, Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        // Step 1: create the database with the full current model if it does
        // not exist yet. No-op (by design) when the database already exists.
        await context.Database.EnsureCreatedAsync();

        // Step 2: repair drift on databases created by older builds. Never
        // let a repair failure crash startup - the app can still run against
        // the portion of the schema that exists.
        try
        {
            await RepairSchemaDriftAsync(context, logger);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "SchemaUpgrader: schema drift repair failed; continuing startup with existing schema.");
        }
    }

    private static async Task RepairSchemaDriftAsync(AppDbContext context, ILogger? logger)
    {
        var connection = context.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen)
        {
            await connection.OpenAsync();
        }

        try
        {
            var existingTables = await GetTableNamesAsync(connection);

            // ---- Missing tables (known drift from pre-2025-01 builds) ----

            await EnsureTableAsync(connection, existingTables, "era_categories", logger,
                """
                CREATE TABLE IF NOT EXISTS "era_categories" (
                    "category_id" TEXT NOT NULL CONSTRAINT "PK_era_categories" PRIMARY KEY,
                    "name" TEXT NOT NULL,
                    "icon_glyph" TEXT NULL,
                    "default_color" TEXT NOT NULL,
                    "sort_order" INTEGER NOT NULL,
                    "is_visible" INTEGER NOT NULL,
                    "created_at" TEXT NOT NULL,
                    "updated_at" TEXT NOT NULL
                );
                """,
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_era_categories_name" ON "era_categories" ("name");
                CREATE INDEX IF NOT EXISTS "IX_era_categories_sort_order" ON "era_categories" ("sort_order");
                """,
                // Seed parity with AppDbContext.SeedDefaultEraCategories (HasData);
                // INSERT OR IGNORE keeps this idempotent.
                """
                INSERT OR IGNORE INTO "era_categories" ("category_id", "name", "icon_glyph", "default_color", "sort_order", "is_visible", "created_at", "updated_at") VALUES
                    ('cat-education',    'Education',    char(59326), '#0078D4', 1, 1, '2025-01-21 00:00:00', '2025-01-21 00:00:00'),
                    ('cat-employment',   'Employment',   char(59425), '#107C10', 2, 1, '2025-01-21 00:00:00', '2025-01-21 00:00:00'),
                    ('cat-relationship', 'Relationship', char(60241), '#E74856', 3, 1, '2025-01-21 00:00:00', '2025-01-21 00:00:00'),
                    ('cat-residence',    'Residence',    char(59407), '#8764B8', 4, 1, '2025-01-21 00:00:00', '2025-01-21 00:00:00'),
                    ('cat-health',       'Health',       char(59742), '#00B7C3', 5, 1, '2025-01-21 00:00:00', '2025-01-21 00:00:00'),
                    ('cat-project',      'Project',      char(59633), '#FF8C00', 6, 1, '2025-01-21 00:00:00', '2025-01-21 00:00:00'),
                    ('cat-other',        'Other',        char(59331), '#6B6B6B', 7, 1, '2025-01-21 00:00:00', '2025-01-21 00:00:00');
                """);

            await EnsureTableAsync(connection, existingTables, "era_tags", logger,
                """
                CREATE TABLE IF NOT EXISTS "era_tags" (
                    "era_id" TEXT NOT NULL,
                    "tag" TEXT NOT NULL,
                    CONSTRAINT "PK_era_tags" PRIMARY KEY ("era_id", "tag"),
                    CONSTRAINT "FK_era_tags_eras_era_id" FOREIGN KEY ("era_id") REFERENCES "eras" ("era_id") ON DELETE CASCADE
                );
                """,
                """
                CREATE INDEX IF NOT EXISTS "IX_era_tags_era_id" ON "era_tags" ("era_id");
                CREATE INDEX IF NOT EXISTS "IX_era_tags_tag" ON "era_tags" ("tag");
                """);

            await EnsureTableAsync(connection, existingTables, "milestones", logger,
                """
                CREATE TABLE IF NOT EXISTS "milestones" (
                    "milestone_id" TEXT NOT NULL CONSTRAINT "PK_milestones" PRIMARY KEY,
                    "name" TEXT NOT NULL,
                    "date" TEXT NOT NULL,
                    "type" INTEGER NOT NULL,
                    "linked_era_id" TEXT NULL,
                    "color_override" TEXT NULL,
                    "description" TEXT NULL,
                    "created_at" TEXT NOT NULL,
                    "updated_at" TEXT NOT NULL,
                    CONSTRAINT "FK_milestones_eras_linked_era_id" FOREIGN KEY ("linked_era_id") REFERENCES "eras" ("era_id") ON DELETE SET NULL
                );
                """,
                """
                CREATE INDEX IF NOT EXISTS "IX_milestones_date" ON "milestones" ("date");
                CREATE INDEX IF NOT EXISTS "IX_milestones_linked_era_id" ON "milestones" ("linked_era_id");
                CREATE INDEX IF NOT EXISTS "IX_milestones_type" ON "milestones" ("type");
                """);

            await EnsureTableAsync(connection, existingTables, "saved_searches", logger,
                """
                CREATE TABLE IF NOT EXISTS "saved_searches" (
                    "saved_search_id" TEXT NOT NULL CONSTRAINT "PK_saved_searches" PRIMARY KEY,
                    "name" TEXT NOT NULL,
                    "search_term" TEXT NULL,
                    "categories" TEXT NULL,
                    "tag_ids" TEXT NULL,
                    "person_ids" TEXT NULL,
                    "location_ids" TEXT NULL,
                    "era_ids" TEXT NULL,
                    "start_date" TEXT NULL,
                    "end_date" TEXT NULL,
                    "has_audio" INTEGER NULL,
                    "has_transcript" INTEGER NULL,
                    "min_confidence" REAL NULL,
                    "sort_by" TEXT NOT NULL,
                    "page_size" INTEGER NOT NULL,
                    "is_favorite" INTEGER NOT NULL,
                    "use_count" INTEGER NOT NULL,
                    "last_used_at" TEXT NULL,
                    "created_at" TEXT NOT NULL,
                    "updated_at" TEXT NOT NULL
                );
                """,
                """
                CREATE INDEX IF NOT EXISTS "IX_saved_searches_name" ON "saved_searches" ("name");
                CREATE INDEX IF NOT EXISTS "IX_saved_searches_is_favorite" ON "saved_searches" ("is_favorite");
                CREATE INDEX IF NOT EXISTS "IX_saved_searches_last_used_at" ON "saved_searches" ("last_used_at");
                """);

            // ---- Missing columns on pre-existing tables ----
            // Note: SQLite's ALTER TABLE ADD COLUMN cannot add foreign key
            // constraints, so eras.category_id is added without one; the FK is
            // only present on databases created fresh by EnsureCreated. EF
            // does not require the constraint to query/save the relationship.

            await EnsureColumnsAsync(connection, existingTables, "eras", logger, new[]
            {
                ("subtitle",       "\"subtitle\" TEXT NULL"),
                ("category_id",    "\"category_id\" TEXT NULL"),
                ("color_override", "\"color_override\" TEXT NULL"),
                ("display_order",  "\"display_order\" INTEGER NOT NULL DEFAULT 0"),
                ("notes",          "\"notes\" TEXT NULL"),
            });

            await EnsureColumnsAsync(connection, existingTables, "events", logger, new[]
            {
                ("location",   "\"location\" TEXT NULL"),
                ("confidence", "\"confidence\" REAL NULL"),
            });

            await EnsureColumnsAsync(connection, existingTables, "tags", logger, new[]
            {
                ("color", "\"color\" TEXT NULL"),
            });

            // Index on the (possibly just-added) eras.category_id column,
            // mirroring OnModelCreating's HasIndex(e => e.CategoryId).
            if (existingTables.Contains("eras"))
            {
                await ExecuteAsync(connection,
                    "CREATE INDEX IF NOT EXISTS \"IX_eras_category_id\" ON \"eras\" (\"category_id\");");
            }
        }
        finally
        {
            if (!wasOpen)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// Creates a table (plus its indexes and optional seed data) when it is
    /// missing from the database. Failures are logged and swallowed so one
    /// broken repair does not prevent the remaining repairs.
    /// </summary>
    private static async Task EnsureTableAsync(
        DbConnection connection,
        HashSet<string> existingTables,
        string tableName,
        ILogger? logger,
        params string[] ddlBatches)
    {
        if (existingTables.Contains(tableName))
        {
            return;
        }

        try
        {
            foreach (var ddl in ddlBatches)
            {
                await ExecuteAsync(connection, ddl);
            }

            existingTables.Add(tableName);
            logger?.LogInformation("SchemaUpgrader: created missing table '{Table}'.", tableName);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "SchemaUpgrader: failed to create missing table '{Table}'.", tableName);
        }
    }

    /// <summary>
    /// Adds any missing columns to an existing table. Failures are logged and
    /// swallowed so one broken repair does not prevent the remaining repairs.
    /// </summary>
    private static async Task EnsureColumnsAsync(
        DbConnection connection,
        HashSet<string> existingTables,
        string tableName,
        ILogger? logger,
        (string ColumnName, string ColumnDdl)[] columns)
    {
        // If the table itself is missing (very old or corrupted database),
        // there is nothing to alter; EnsureCreated only builds full schemas
        // for brand-new databases.
        if (!existingTables.Contains(tableName))
        {
            logger?.LogWarning("SchemaUpgrader: table '{Table}' not found; skipping column repairs for it.", tableName);
            return;
        }

        HashSet<string> existingColumns;
        try
        {
            existingColumns = await GetColumnNamesAsync(connection, tableName);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "SchemaUpgrader: failed to inspect columns of '{Table}'.", tableName);
            return;
        }

        foreach (var (columnName, columnDdl) in columns)
        {
            if (existingColumns.Contains(columnName))
            {
                continue;
            }

            try
            {
                await ExecuteAsync(connection, $"ALTER TABLE \"{tableName}\" ADD COLUMN {columnDdl};");
                logger?.LogInformation("SchemaUpgrader: added missing column '{Table}.{Column}'.", tableName, columnName);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "SchemaUpgrader: failed to add column '{Table}.{Column}'.", tableName, columnName);
            }
        }
    }

    private static async Task<HashSet<string>> GetTableNamesAsync(DbConnection connection)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task<HashSet<string>> GetColumnNamesAsync(DbConnection connection, string tableName)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var command = connection.CreateCommand();
        // Table names cannot be parameterized in PRAGMA statements; the names
        // used here are compile-time constants, never user input.
        command.CommandText = $"PRAGMA table_info(\"{tableName}\")";
        await using var reader = await command.ExecuteReaderAsync();
        var nameOrdinal = reader.GetOrdinal("name");
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(nameOrdinal));
        }

        return columns;
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
