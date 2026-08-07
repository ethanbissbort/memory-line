namespace MemoryTimeline.SyncContracts;

/// <summary>
/// Read-only projections of the Windows archive, published onto the sync feed
/// so a companion device can show a timeline it does not own.
///
/// <para><b>Direction and authority.</b> Windows publishes; companions consume.
/// This is the same shape as <c>CaptureStatusChangePayload</c> and rests on the
/// same Phase 3 boundary: Windows holds the archive, runs extraction, and is
/// the only writer. A companion rendering a timeline from these payloads is
/// reading a copy, not sharing ownership, so there is no merge policy here and
/// none is needed.</para>
///
/// <para>The single exception is <see cref="PendingEventDecisionPayload"/>,
/// which travels the other way. Approving or rejecting an extracted event is a
/// decision rather than an edit: it is one bit, it is idempotent, and the same
/// answer applied twice means the same thing. That makes it safe to accept from
/// a companion without opening general multi-writer editing, which would need
/// per-field revisions and a conflict policy the system does not have.</para>
///
/// <para><b>Denormalised on purpose.</b> An event payload carries its tags,
/// people and location names inline rather than referencing junction rows. A
/// consumer of a read-only projection wants to draw a bubble, not reconstruct a
/// relational graph, and syncing junction tables would triple the change volume
/// to reproduce a join the consumer immediately performs anyway. People and
/// eras still get their own entity types, because both are browsed
/// independently of any event.</para>
///
/// <para><b>Privacy (§14.5).</b> These cross devices, so they carry what a user
/// reads on a timeline: titles, dates, categories, names. Full transcripts and
/// file paths stay on Windows; media is referenced by id and fetched through
/// the artifact endpoints if the companion wants it.</para>
/// </summary>
public class EventProjectionPayload
{
    public string EventId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>Trimmed to a readable length by the publisher; not the archive's full text.</summary>
    public string? Description { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    /// <summary>
    /// How much of <see cref="StartDate"/> to believe: exact, day, month,
    /// season, year, decade, or unknown. Carried rather than derived because
    /// getting this wrong is how a companion ends up inventing a precise day
    /// for a memory the user only dated to a summer.
    /// </summary>
    public string DatePrecision { get; set; } = "day";

    /// <summary>
    /// Precision-honest display string ("Summer 2003"), formatted by Windows so
    /// every companion renders exactly what the owning app would rather than
    /// reimplementing the rules and drifting.
    /// </summary>
    public string? DisplayDate { get; set; }

    public DateTime? EarliestPossible { get; set; }

    public DateTime? LatestPossible { get; set; }

    public string? Category { get; set; }

    public string? EraId { get; set; }

    /// <summary>Tag names, already resolved. See the denormalisation note above.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Linked person ids; names come from the person projection.</summary>
    public List<string> PersonIds { get; set; } = new();

    /// <summary>Location display names, already resolved.</summary>
    public List<string> Locations { get; set; } = new();

    /// <summary>Optional coordinates for the map, when the event has a located place.</summary>
    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    /// <summary>How many media items are attached, so a companion can show a badge without fetching any.</summary>
    public int MediaCount { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>A life period, projected for timeline backgrounds and the eras list.</summary>
public class EraProjectionPayload
{
    public string EraId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Subtitle { get; set; }

    public DateTime StartDate { get; set; }

    /// <summary>Null for an ongoing era.</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>"#RRGGBB". Colours cross as strings; the client builds its own brush.</summary>
    public string ColorCode { get; set; } = "#808080";

    public string? CategoryId { get; set; }

    public string? CategoryName { get; set; }

    public int DisplayOrder { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// A person from the contact book, projected for the People hub and for
/// resolving <see cref="EventProjectionPayload.PersonIds"/>.
/// </summary>
public class PersonProjectionPayload
{
    public string PersonId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Nickname { get; set; }

    public string? Relationship { get; set; }

    /// <summary>
    /// When set, this person was merged into another and this id is a
    /// tombstone. Companions must follow it rather than showing a duplicate;
    /// old events keep pointing at the merged-away id on purpose.
    /// </summary>
    public string? MergedIntoId { get; set; }

    public bool IsFavorite { get; set; }

    /// <summary>Events this person appears in, so the hub can sort without a scan.</summary>
    public int EventCount { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// An extracted event awaiting review, projected so a companion can show the
/// queue and act on it. Pairs with <see cref="PendingEventDecisionPayload"/>.
/// </summary>
public class PendingEventProjectionPayload
{
    public string PendingEventId { get; set; } = string.Empty;

    /// <summary>Capture this was extracted from, so the companion can link back.</summary>
    public string? CaptureId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public string DatePrecision { get; set; } = "day";

    public string? DisplayDate { get; set; }

    public string? Category { get; set; }

    /// <summary>Extraction confidence, 0..1, so the reviewer can triage.</summary>
    public double ConfidenceScore { get; set; }

    public List<string> Tags { get; set; } = new();

    /// <summary>People the extraction found, by name — they may not exist yet.</summary>
    public List<string> PeopleNames { get; set; } = new();

    /// <summary>Bounded excerpt of the source transcript (§14.5), never the whole thing.</summary>
    public string? TranscriptPreview { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// A companion's approve/reject decision on a pending event — the one write a
/// non-Windows device may make.
///
/// Safe to accept precisely because it is not an edit: it carries no field
/// values to merge, applying it twice is the same as applying it once, and two
/// devices sending the same verdict agree by construction. Windows still
/// performs the approval — writing the event, its tags, people and locations in
/// one transaction — so the extraction rules stay in one place.
///
/// A device may not decide a pending event that Windows has already resolved;
/// the decision is dropped rather than reversing a completed review.
/// </summary>
public class PendingEventDecisionPayload
{
    public string PendingEventId { get; set; } = string.Empty;

    /// <summary>
    /// One of <see cref="PendingEventDecision"/>.
    ///
    /// Deliberately defaulted to empty rather than to
    /// <see cref="PendingEventDecision.Approve"/>: a defaulted verdict means an
    /// omitted or truncated field deserializes into "approve" and silently
    /// writes an extracted event into the user's timeline. The failure mode of
    /// a missing field must never be a write to the archive, so an empty value
    /// is rejected by the service and the caller has to say what it meant.
    /// </summary>
    public string Decision { get; set; } = string.Empty;

    /// <summary>Device that decided, for the audit trail Windows keeps.</summary>
    public string DecidedByDeviceId { get; set; } = string.Empty;

    public DateTime DecidedAtUtc { get; set; }

    /// <summary>
    /// Optional reviewer corrections applied before approval — title and dates
    /// only, the fields a reviewer realistically fixes on a phone. Null means
    /// approve as extracted. This is deliberately NOT general editing: it rides
    /// the decision because it is part of the same single act.
    /// </summary>
    public PendingEventCorrections? Corrections { get; set; }
}

/// <summary>The narrow set of fields a reviewer may correct while approving.</summary>
public class PendingEventCorrections
{
    public string? Title { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public string? DatePrecision { get; set; }

    public string? Category { get; set; }
}

/// <summary>Verdicts a companion may return for a pending event.</summary>
public static class PendingEventDecision
{
    public const string Approve = "approve";
    public const string Reject = "reject";
}
