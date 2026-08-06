using MemoryTimeline.Core.Models;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Brings archived memories back to the user's attention: On This Day
/// matching, neglected-memory random picks, the weekly digest, and upcoming
/// anniversaries. All matching honors each event's <c>DatePrecision</c> - a
/// coarse date is never presented as if the exact day were known.
/// </summary>
public interface IResurfacingService
{
    /// <summary>
    /// Events from past years whose anchor month+day match
    /// <paramref name="forDate"/> (default: today). Only Exact/Day precision
    /// events participate - coarser precisions have no meaningful day-of-year.
    /// A Feb 29 anchor surfaces on Feb 28 in non-leap years.
    /// </summary>
    Task<List<ResurfacedMemory>> GetOnThisDayAsync(DateTime? forDate = null);

    /// <summary>
    /// One random memory from the archive (any precision), or null when the
    /// archive is empty. <see cref="RandomBias.Neglected"/> weights the pick
    /// toward rarely- and not-recently-viewed events.
    /// </summary>
    Task<ResurfacedMemory?> GetRandomAsync(RandomBias bias = RandomBias.Neglected);

    /// <summary>
    /// "This week across the years": Exact/Day events whose month+day fall in
    /// the week containing <paramref name="weekOf"/> (default: today) in any
    /// past year, plus Month-precision events whose anchor month is covered by
    /// that week.
    /// </summary>
    Task<WeeklyDigest> GetWeeklyDigestAsync(DateTime? weekOf = null);

    /// <summary>
    /// Milestone anniversaries falling within the next
    /// <paramref name="daysAhead"/> days (any number of years since), plus
    /// round-number (1/5/10/20/25 year) anniversaries of Exact/Day-precision
    /// events. Ordered by occurrence date.
    /// </summary>
    Task<List<UpcomingAnniversary>> GetUpcomingAnniversariesAsync(int daysAhead = 14);

    /// <summary>
    /// Records that the user opened an event from a resurfacing surface:
    /// increments its view count and stamps last_viewed_at.
    /// </summary>
    Task RecordViewAsync(string eventId);
}
