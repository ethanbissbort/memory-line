using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// What kind of change produced a revision row. Stored as INTEGER in SQLite -
/// the numeric values are part of the schema contract and must never be
/// reordered.
/// </summary>
public enum RevisionKind
{
    /// <summary>The event was created by hand (snapshot = state at creation).</summary>
    Created = 0,

    /// <summary>
    /// The event was edited. The snapshot holds the PRE-edit state, so the
    /// version being replaced is always inspectable/restorable.
    /// </summary>
    Edited = 1,

    /// <summary>The event was created by approving a pending extraction (snapshot = approved state).</summary>
    Approved = 2,

    /// <summary>The event was created by a data import (snapshot = imported state).</summary>
    Imported = 3,

    /// <summary>
    /// Reserved for merge operations that rewrite an event. Currently unused:
    /// person merges (PersonService.MergeAsync) only repoint event_people
    /// junction rows - they never modify event fields - so no revision is
    /// written for them by design.
    /// </summary>
    Merged = 4,

    /// <summary>
    /// A prior revision was restored. The snapshot holds the PRE-restore
    /// state (the state being overwritten), keeping history append-only and
    /// complete: without it, the overwritten state would be lost forever.
    /// </summary>
    Restored = 5
}

/// <summary>
/// An immutable, append-only snapshot of an event at a moment in time:
/// every scalar field plus the tag/person/location NAMES linked at that
/// moment, serialized as JSON.
///
/// Deliberately has NO foreign key to events (and no navigation property,
/// which would make EF create one by convention): revision history must
/// outlive event deletion so a deleted event's past remains inspectable.
/// Lookups go through the (event_id, revised_at DESC) composite index.
/// </summary>
[Table("event_revisions")]
public class EventRevision
{
    [Key]
    [Column("revision_id")]
    public string RevisionId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The event this revision belongs to. Not a FK - see class remarks.</summary>
    [Required]
    [Column("event_id")]
    public string EventId { get; set; } = string.Empty;

    /// <summary>When the revision was written (UTC).</summary>
    [Column("revised_at")]
    public DateTime RevisedAt { get; set; } = DateTime.UtcNow;

    /// <summary>What kind of change produced this revision (INTEGER enum).</summary>
    [Column("revision_kind")]
    public RevisionKind RevisionKind { get; set; }

    /// <summary>
    /// JSON snapshot of the event's fields and tag/person/location names at
    /// this moment (see Core's EventSnapshot for the shape).
    /// </summary>
    [Required]
    [Column("snapshot_json")]
    public string SnapshotJson { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated canonical tokens of the fields that changed in the
    /// operation this revision records (e.g. "title,start_date"), or null
    /// when unknown / not applicable (Created/Approved/Imported).
    /// </summary>
    [Column("changed_fields_csv")]
    public string? ChangedFieldsCsv { get; set; }
}
