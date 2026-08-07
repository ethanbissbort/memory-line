using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MemoryTimeline.Data.Models;

/// <summary>
/// A guided-recall question generated from a gap in the archive ("What were
/// you doing in 2004?"). Rows are never deleted: Dismissed and Answered rows
/// stay behind as a dedupe record so the app never asks the same question
/// twice (see the (kind, entity_id) index and the repository's ExistsForAsync).
/// </summary>
[Table("recall_prompts")]
public class RecallPrompt
{
    [Key]
    [Column("prompt_id")]
    public string PromptId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>What kind of archive gap produced this prompt.</summary>
    [Column("kind")]
    public RecallPromptKind Kind { get; set; }

    /// <summary>The question shown to the user.</summary>
    [Required]
    [Column("question")]
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Start of the time period the prompt is about (e.g. Jan 1 of the first
    /// quiet year for a density gap). Doubles as the dedupe anchor for
    /// period-keyed kinds that have no entity id.
    /// </summary>
    [Column("anchor_start")]
    public DateTime? AnchorStart { get; set; }

    /// <summary>End of the time period the prompt is about.</summary>
    [Column("anchor_end")]
    public DateTime? AnchorEnd { get; set; }

    /// <summary>
    /// Id of the entity the prompt is about (person, era, or event), when the
    /// prompt is entity-keyed. Null for density gaps.
    /// </summary>
    [Column("entity_id")]
    public string? EntityId { get; set; }

    /// <summary>Display name of the entity (person name, era name, event title).</summary>
    [MaxLength(200)]
    [Column("entity_name")]
    public string? EntityName { get; set; }

    [Column("status")]
    public RecallPromptStatus Status { get; set; } = RecallPromptStatus.Active;

    /// <summary>
    /// Why the prompt was skipped. <see cref="DismissReason.Later"/> keeps the
    /// prompt Active with <see cref="SnoozedUntil"/> set; the other reasons are
    /// permanent (<see cref="RecallPromptStatus.Dismissed"/>).
    /// </summary>
    [Column("dismiss_reason")]
    public DismissReason DismissReason { get; set; } = DismissReason.None;

    /// <summary>
    /// While set and in the future, an Active prompt is hidden from rotation.
    /// It returns automatically once this moment passes.
    /// </summary>
    [Column("snoozed_until")]
    public DateTime? SnoozedUntil { get; set; }

    /// <summary>
    /// The recording_queue row that answered this prompt (typed text or a
    /// recording), once <see cref="RecallPromptStatus.Answered"/>.
    /// </summary>
    [Column("queue_id")]
    public string? QueueId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// What kind of archive gap a <see cref="RecallPrompt"/> was generated from.
/// Stored as INTEGER in SQLite - the numeric values are part of the schema
/// contract and must never be reordered.
/// </summary>
public enum RecallPromptKind
{
    /// <summary>A year (or run of years) with far fewer events than its surroundings.</summary>
    DensityGap = 0,

    /// <summary>A person linked to exactly one event.</summary>
    DanglingPerson = 1,

    /// <summary>An ongoing era gone quiet, or an era containing zero events.</summary>
    EraEdge = 2,

    /// <summary>An event with a title but little or no description.</summary>
    ThinEvent = 3,

    /// <summary>A round-number anniversary (10/20/25 years) of an event this month.</summary>
    AnniversaryAnchor = 4
}

/// <summary>
/// Lifecycle state of a <see cref="RecallPrompt"/>. Stored as INTEGER.
/// </summary>
public enum RecallPromptStatus
{
    /// <summary>In rotation (possibly snoozed via snoozed_until).</summary>
    Active = 0,

    /// <summary>Permanently skipped; kept as a dedupe record.</summary>
    Dismissed = 1,

    /// <summary>Answered; queue_id links the answer's queue row.</summary>
    Answered = 2
}

/// <summary>
/// Why a <see cref="RecallPrompt"/> was skipped. Stored as INTEGER.
/// </summary>
public enum DismissReason
{
    /// <summary>Not skipped.</summary>
    None = 0,

    /// <summary>The user does not want to answer this - permanent.</summary>
    NotInterested = 1,

    /// <summary>The archive already covers this - permanent.</summary>
    AlreadyCovered = 2,

    /// <summary>Ask again later - snoozes the prompt for 14 days.</summary>
    Later = 3
}
