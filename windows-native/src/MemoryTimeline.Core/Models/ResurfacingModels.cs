using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Core.Models;

/// <summary>
/// A memory brought back to the user's attention by the resurfacing service
/// (On This Day, random memory, weekly digest).
/// </summary>
public class ResurfacedMemory
{
    public string EventId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The event's anchor date (see <see cref="Event.StartDate"/>).</summary>
    public DateTime StartDate { get; set; }

    /// <summary>How coarse the anchor date is; resurfacing honors it.</summary>
    public DatePrecision DatePrecision { get; set; } = DatePrecision.Day;

    /// <summary>
    /// Precision-honest date text (<see cref="DateDisplay.FormatPrecise"/>),
    /// e.g. "14 Mar 2015" or "March 2015".
    /// </summary>
    public string DisplayDate { get; set; } = string.Empty;

    public string? Category { get; set; }

    /// <summary>
    /// Whole years between the anchor year and the reference date's year
    /// ("5 years ago today"). At least 1 for On This Day, the weekly digest,
    /// and anniversaries (those surfaces never resurface same-year events),
    /// but CAN be 0 for <c>GetRandomAsync</c> picks, which may land on a
    /// current-year event (HomeViewModel renders "This year" for 0).
    /// Consumers must handle the 0 case.
    /// </summary>
    public int YearsAgo { get; set; }
}

/// <summary>
/// The "this week across the years" digest: memories from any past year whose
/// dates fall in the given week.
/// </summary>
public class WeeklyDigest
{
    /// <summary>First day (Sunday) of the digest week.</summary>
    public DateTime WeekStart { get; set; }

    public List<ResurfacedMemory> Memories { get; set; } = new();
}

/// <summary>
/// What an <see cref="UpcomingAnniversary"/> is an anniversary of.
/// </summary>
public enum AnniversarySourceKind
{
    Milestone,
    Event
}

/// <summary>
/// An anniversary (of a milestone, or a round-number event anniversary)
/// falling within the upcoming look-ahead window.
/// </summary>
public class UpcomingAnniversary
{
    /// <summary>The upcoming occurrence date (this year or next).</summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Whole years between the source's anchor year and the occurrence year
    /// (0 for a milestone dated in the upcoming window itself).
    /// </summary>
    public int YearsSince { get; set; }

    public string Title { get; set; } = string.Empty;

    public AnniversarySourceKind SourceKind { get; set; }

    /// <summary>MilestoneId or EventId depending on <see cref="SourceKind"/>.</summary>
    public string SourceId { get; set; } = string.Empty;
}

/// <summary>
/// How <c>GetRandomAsync</c> picks a memory.
/// </summary>
public enum RandomBias
{
    /// <summary>Uniformly random over the whole archive.</summary>
    Any,

    /// <summary>
    /// Weighted toward events with few views and no recent view, so the
    /// archive's forgotten corners come back around.
    /// </summary>
    Neglected
}
