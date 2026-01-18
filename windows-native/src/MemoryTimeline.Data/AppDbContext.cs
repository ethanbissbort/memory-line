using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;
using System.Reflection;

namespace MemoryTimeline.Data;

/// <summary>
/// Entity Framework Core database context for Memory Timeline.
/// </summary>
public class AppDbContext : DbContext
{
    public DbSet<Event> Events { get; set; } = null!;
    public DbSet<Era> Eras { get; set; } = null!;
    public DbSet<EraCategory> EraCategories { get; set; } = null!;
    public DbSet<EraTag> EraTags { get; set; } = null!;
    public DbSet<Milestone> Milestones { get; set; } = null!;
    public DbSet<RecordingQueue> RecordingQueues { get; set; } = null!;
    public DbSet<PendingEvent> PendingEvents { get; set; } = null!;
    public DbSet<Tag> Tags { get; set; } = null!;
    public DbSet<EventTag> EventTags { get; set; } = null!;
    public DbSet<Person> People { get; set; } = null!;
    public DbSet<EventPerson> EventPeople { get; set; } = null!;
    public DbSet<Location> Locations { get; set; } = null!;
    public DbSet<EventLocation> EventLocations { get; set; } = null!;
    public DbSet<CrossReference> CrossReferences { get; set; } = null!;
    public DbSet<EventEmbedding> EventEmbeddings { get; set; } = null!;
    public DbSet<AppSetting> AppSettings { get; set; } = null!;
    public DbSet<SavedSearch> SavedSearches { get; set; } = null!;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            // Get the ApplicationData folder path for the database
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dbPath = Path.Combine(appDataPath, "MemoryTimeline", "memory-timeline.db");

            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Event entity
        modelBuilder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.HasIndex(e => e.StartDate);
            entity.HasIndex(e => e.EndDate);
            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.EraId);

            entity.HasOne(e => e.Era)
                .WithMany(era => era.Events)
                .HasForeignKey(e => e.EraId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Embedding)
                .WithOne(emb => emb.Event)
                .HasForeignKey<EventEmbedding>(emb => emb.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Era entity
        modelBuilder.Entity<Era>(entity =>
        {
            entity.HasKey(e => e.EraId);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => new { e.StartDate, e.EndDate });
            entity.HasIndex(e => e.CategoryId);

            entity.HasOne(e => e.Category)
                .WithMany(c => c.Eras)
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure EraCategory entity
        modelBuilder.Entity<EraCategory>(entity =>
        {
            entity.HasKey(c => c.CategoryId);
            entity.HasIndex(c => c.Name).IsUnique();
            entity.HasIndex(c => c.SortOrder);
        });

        // Configure EraTag entity
        modelBuilder.Entity<EraTag>(entity =>
        {
            entity.HasKey(et => new { et.EraId, et.Tag });

            entity.HasOne(et => et.Era)
                .WithMany(e => e.EraTags)
                .HasForeignKey(et => et.EraId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(et => et.EraId);
            entity.HasIndex(et => et.Tag);
        });

        // Configure Milestone entity
        modelBuilder.Entity<Milestone>(entity =>
        {
            entity.HasKey(m => m.MilestoneId);
            entity.HasIndex(m => m.Date);
            entity.HasIndex(m => m.LinkedEraId);
            entity.HasIndex(m => m.Type);

            entity.HasOne(m => m.LinkedEra)
                .WithMany(e => e.Milestones)
                .HasForeignKey(m => m.LinkedEraId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure RecordingQueue entity
        modelBuilder.Entity<RecordingQueue>(entity =>
        {
            entity.HasKey(q => q.QueueId);
            entity.HasIndex(q => q.Status);
            entity.HasIndex(q => q.CreatedAt);
        });

        // Configure PendingEvent entity
        modelBuilder.Entity<PendingEvent>(entity =>
        {
            entity.HasKey(p => p.PendingId);
            entity.HasIndex(p => p.Status);
            entity.HasIndex(p => p.CreatedAt);

            entity.HasOne(p => p.RecordingQueue)
                .WithMany(q => q.PendingEvents)
                .HasForeignKey(p => p.QueueId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure Tag entity
        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasKey(t => t.TagId);
            entity.HasIndex(t => t.TagName).IsUnique();
        });

        // Configure EventTag junction entity
        modelBuilder.Entity<EventTag>(entity =>
        {
            entity.HasKey(et => new { et.EventId, et.TagId });

            entity.HasOne(et => et.Event)
                .WithMany(e => e.EventTags)
                .HasForeignKey(et => et.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(et => et.Tag)
                .WithMany(t => t.EventTags)
                .HasForeignKey(et => et.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(et => et.EventId);
            entity.HasIndex(et => et.TagId);
        });

        // Configure Person entity
        modelBuilder.Entity<Person>(entity =>
        {
            entity.HasKey(p => p.PersonId);
            entity.HasIndex(p => p.Name).IsUnique();
        });

        // Configure EventPerson junction entity
        modelBuilder.Entity<EventPerson>(entity =>
        {
            entity.HasKey(ep => new { ep.EventId, ep.PersonId });

            entity.HasOne(ep => ep.Event)
                .WithMany(e => e.EventPeople)
                .HasForeignKey(ep => ep.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ep => ep.Person)
                .WithMany(p => p.EventPeople)
                .HasForeignKey(ep => ep.PersonId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(ep => ep.EventId);
            entity.HasIndex(ep => ep.PersonId);
        });

        // Configure Location entity
        modelBuilder.Entity<Location>(entity =>
        {
            entity.HasKey(l => l.LocationId);
            entity.HasIndex(l => l.Name).IsUnique();
        });

        // Configure EventLocation junction entity
        modelBuilder.Entity<EventLocation>(entity =>
        {
            entity.HasKey(el => new { el.EventId, el.LocationId });

            entity.HasOne(el => el.Event)
                .WithMany(e => e.EventLocations)
                .HasForeignKey(el => el.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(el => el.Location)
                .WithMany(l => l.EventLocations)
                .HasForeignKey(el => el.LocationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(el => el.EventId);
            entity.HasIndex(el => el.LocationId);
        });

        // Configure CrossReference entity
        modelBuilder.Entity<CrossReference>(entity =>
        {
            entity.HasKey(cr => cr.ReferenceId);
            entity.HasIndex(cr => cr.EventId1);
            entity.HasIndex(cr => cr.EventId2);
            entity.HasIndex(cr => cr.RelationshipType);

            entity.HasOne(cr => cr.Event1)
                .WithMany()
                .HasForeignKey(cr => cr.EventId1)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(cr => cr.Event2)
                .WithMany()
                .HasForeignKey(cr => cr.EventId2)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure EventEmbedding entity
        modelBuilder.Entity<EventEmbedding>(entity =>
        {
            entity.HasKey(e => e.EmbeddingId);
            entity.HasIndex(e => e.EventId).IsUnique();
            entity.HasIndex(e => e.EmbeddingProvider);
        });

        // Configure AppSetting entity
        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(s => s.SettingKey);
        });

        // Configure SavedSearch entity
        modelBuilder.Entity<SavedSearch>(entity =>
        {
            entity.HasKey(s => s.SavedSearchId);
            entity.HasIndex(s => s.Name);
            entity.HasIndex(s => s.IsFavorite);
            entity.HasIndex(s => s.LastUsedAt);
        });

        // Seed default settings
        SeedDefaultSettings(modelBuilder);
    }

    private void SeedDefaultSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSetting>().HasData(
            new AppSetting { SettingKey = "theme", SettingValue = "light" },
            new AppSetting { SettingKey = "default_zoom_level", SettingValue = "month" },
            new AppSetting { SettingKey = "audio_quality", SettingValue = "high" },
            new AppSetting { SettingKey = "llm_provider", SettingValue = "anthropic" },
            new AppSetting { SettingKey = "llm_model", SettingValue = "claude-sonnet-4-20250514" },
            new AppSetting { SettingKey = "llm_max_tokens", SettingValue = "4000" },
            new AppSetting { SettingKey = "llm_temperature", SettingValue = "0.3" },
            new AppSetting { SettingKey = "stt_engine", SettingValue = "windows" },
            new AppSetting { SettingKey = "stt_config", SettingValue = "{}" },
            new AppSetting { SettingKey = "rag_auto_run_enabled", SettingValue = "false" },
            new AppSetting { SettingKey = "rag_schedule", SettingValue = "weekly" },
            new AppSetting { SettingKey = "rag_similarity_threshold", SettingValue = "0.75" },
            new AppSetting { SettingKey = "embedding_provider", SettingValue = "local" },
            new AppSetting { SettingKey = "embedding_model", SettingValue = "onnx-text-embedding" },
            new AppSetting { SettingKey = "embedding_api_key", SettingValue = "" },
            new AppSetting { SettingKey = "auto_generate_embeddings", SettingValue = "true" },
            new AppSetting { SettingKey = "send_transcripts_only", SettingValue = "true" },
            new AppSetting { SettingKey = "require_confirmation", SettingValue = "true" }
        );

        // Seed default era categories
        SeedDefaultEraCategories(modelBuilder);
    }

    private void SeedDefaultEraCategories(ModelBuilder modelBuilder)
    {
        var now = DateTime.UtcNow;
        modelBuilder.Entity<EraCategory>().HasData(
            new EraCategory { CategoryId = "cat-education", Name = "Education", DefaultColor = "#0078D4", IconGlyph = "\uE7BE", SortOrder = 1, CreatedAt = now, UpdatedAt = now },
            new EraCategory { CategoryId = "cat-employment", Name = "Employment", DefaultColor = "#107C10", IconGlyph = "\uE821", SortOrder = 2, CreatedAt = now, UpdatedAt = now },
            new EraCategory { CategoryId = "cat-relationship", Name = "Relationship", DefaultColor = "#E74856", IconGlyph = "\uEB51", SortOrder = 3, CreatedAt = now, UpdatedAt = now },
            new EraCategory { CategoryId = "cat-residence", Name = "Residence", DefaultColor = "#8764B8", IconGlyph = "\uE80F", SortOrder = 4, CreatedAt = now, UpdatedAt = now },
            new EraCategory { CategoryId = "cat-health", Name = "Health", DefaultColor = "#00B7C3", IconGlyph = "\uE95E", SortOrder = 5, CreatedAt = now, UpdatedAt = now },
            new EraCategory { CategoryId = "cat-project", Name = "Project", DefaultColor = "#FF8C00", IconGlyph = "\uE8F1", SortOrder = 6, CreatedAt = now, UpdatedAt = now },
            new EraCategory { CategoryId = "cat-other", Name = "Other", DefaultColor = "#6B6B6B", IconGlyph = "\uE7C3", SortOrder = 7, CreatedAt = now, UpdatedAt = now }
        );
    }

    /// <summary>
    /// Override SaveChanges to automatically update timestamps.
    /// </summary>
    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    /// <summary>
    /// Override SaveChangesAsync to automatically update timestamps.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.Entity is Event eventEntity)
            {
                eventEntity.UpdatedAt = DateTime.UtcNow;
            }
            else if (entry.Entity is Era era)
            {
                era.UpdatedAt = DateTime.UtcNow;
            }
            else if (entry.Entity is EraCategory category)
            {
                category.UpdatedAt = DateTime.UtcNow;
            }
            else if (entry.Entity is Milestone milestone)
            {
                milestone.UpdatedAt = DateTime.UtcNow;
            }
            else if (entry.Entity is AppSetting setting)
            {
                setting.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
