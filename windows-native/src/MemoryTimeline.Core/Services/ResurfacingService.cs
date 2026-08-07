using MemoryTimeline.Core.Models;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Resurfacing implementation. Precision rules (the F1 integration):
///
///  - On This Day (daily): only Exact/Day precision events participate - a
///    Month/Season/Year/Decade/Unknown anchor has no meaningful day-of-year,
///    so those events NEVER appear in the daily list. Feb 29 anchors fall
///    back to Feb 28 in non-leap years.
///  - Weekly digest: Exact/Day events whose month+day fall in the week (any
///    past year) PLUS Month-precision events whose anchor month is covered by
///    the week (month-level matching is honest at month granularity).
///  - Upcoming anniversaries: milestones at any year mark; events only at
///    round-number marks (1/5/10/20/25 years) and only for Exact/Day anchors.
///
/// Stateless over the context factory (registered singleton); every operation
/// opens its own short-lived context.
/// </summary>
public class ResurfacingService : IResurfacingService
{
    /// <summary>Round year marks at which event anniversaries surface.</summary>
    private static readonly int[] RoundYearMarks = { 1, 5, 10, 20, 25 };

    /// <summary>Weight multiplier for events viewed within the last 30 days.</summary>
    private const double RecentViewPenalty = 0.25;

    private static readonly TimeSpan RecentViewWindow = TimeSpan.FromDays(30);

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IMilestoneRepository _milestoneRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ILogger<ResurfacingService> _logger;
    private readonly Random _random;

    public ResurfacingService(
        IDbContextFactory<AppDbContext> contextFactory,
        IMilestoneRepository milestoneRepository,
        IEventRepository eventRepository,
        ILogger<ResurfacingService> logger)
        : this(contextFactory, milestoneRepository, eventRepository, logger, Random.Shared)
    {
    }

    /// <summary>
    /// Test seam: lets tests inject a deterministic <see cref="Random"/> for
    /// the weighted pick (exposed via InternalsVisibleTo).
    /// </summary>
    internal ResurfacingService(
        IDbContextFactory<AppDbContext> contextFactory,
        IMilestoneRepository milestoneRepository,
        IEventRepository eventRepository,
        ILogger<ResurfacingService> logger,
        Random random)
    {
        _contextFactory = contextFactory;
        _milestoneRepository = milestoneRepository;
        _eventRepository = eventRepository;
        _logger = logger;
        _random = random;
    }

    /// <inheritdoc />
    public async Task<List<ResurfacedMemory>> GetOnThisDayAsync(DateTime? forDate = null)
    {
        var target = (forDate ?? DateTime.Today).Date;

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            // Only Exact/Day precision events have a trustworthy day-of-year.
            var candidates = await context.Events
                .AsNoTracking()
                .Where(e => e.DatePrecision == DatePrecision.Exact || e.DatePrecision == DatePrecision.Day)
                .Select(e => new { e.EventId, e.Title, e.Description, e.StartDate, e.DatePrecision, e.Category })
                .ToListAsync();

            return candidates
                .Where(e => e.StartDate.Year < target.Year && MatchesAnniversaryDay(e.StartDate, target))
                .Select(e => ToResurfacedMemory(
                    e.EventId, e.Title, e.Description, e.StartDate, e.DatePrecision, e.Category,
                    target.Year - e.StartDate.Year))
                .OrderBy(m => m.YearsAgo)
                .ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing On This Day memories for {Date}", target);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ResurfacedMemory?> GetRandomAsync(RandomBias bias = RandomBias.Neglected)
    {
        try
        {
            List<(string EventId, int ViewCount, DateTime? LastViewedAt)> candidates;
            await using (var context = await _contextFactory.CreateDbContextAsync())
            {
                // Ordered by id so the weighted cumulative walk is deterministic
                // for a given RNG value (test seam).
                candidates = (await context.Events
                        .AsNoTracking()
                        .OrderBy(e => e.EventId)
                        .Select(e => new { e.EventId, e.ViewCount, e.LastViewedAt })
                        .ToListAsync())
                    .Select(e => (e.EventId, e.ViewCount, e.LastViewedAt))
                    .ToList();
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            var pickedId = bias == RandomBias.Any
                ? candidates[_random.Next(candidates.Count)].EventId
                : PickWeighted(candidates, _random, DateTime.UtcNow);

            var evt = await _eventRepository.GetByIdAsync(pickedId);
            if (evt == null)
            {
                // Deleted between the candidate read and the hydration read.
                _logger.LogWarning("Random pick {EventId} no longer exists", pickedId);
                return null;
            }

            var yearsAgo = Math.Max(0, DateTime.Today.Year - evt.StartDate.Year);
            return ToResurfacedMemory(
                evt.EventId, evt.Title, evt.Description, evt.StartDate, evt.DatePrecision, evt.Category, yearsAgo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error picking a random memory (bias {Bias})", bias);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<WeeklyDigest> GetWeeklyDigestAsync(DateTime? weekOf = null)
    {
        var reference = (weekOf ?? DateTime.Today).Date;
        // Sunday-start weeks, matching AnalyticsService's weekly grouping.
        var weekStart = reference.AddDays(-(int)reference.DayOfWeek);
        var weekDays = Enumerable.Range(0, 7).Select(offset => weekStart.AddDays(offset)).ToList();
        var monthsInWeek = weekDays.Select(d => d.Month).Distinct().ToList();

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var candidates = await context.Events
                .AsNoTracking()
                .Where(e => e.DatePrecision == DatePrecision.Exact
                            || e.DatePrecision == DatePrecision.Day
                            || e.DatePrecision == DatePrecision.Month)
                .Select(e => new { e.EventId, e.Title, e.Description, e.StartDate, e.DatePrecision, e.Category })
                .ToListAsync();

            var memories = new List<ResurfacedMemory>();
            foreach (var candidate in candidates)
            {
                int? yearsAgo = null;

                if (candidate.DatePrecision == DatePrecision.Month)
                {
                    // Month precision: include when the anchor month is covered
                    // by the week (a week can span two months).
                    if (monthsInWeek.Contains(candidate.StartDate.Month))
                    {
                        var dayInMonth = weekDays.First(d => d.Month == candidate.StartDate.Month);
                        if (candidate.StartDate.Year < dayInMonth.Year)
                        {
                            yearsAgo = dayInMonth.Year - candidate.StartDate.Year;
                        }
                    }
                }
                else
                {
                    foreach (var day in weekDays)
                    {
                        if (candidate.StartDate.Year < day.Year && MatchesAnniversaryDay(candidate.StartDate, day))
                        {
                            yearsAgo = day.Year - candidate.StartDate.Year;
                            break;
                        }
                    }
                }

                if (yearsAgo.HasValue)
                {
                    memories.Add(ToResurfacedMemory(
                        candidate.EventId, candidate.Title, candidate.Description, candidate.StartDate,
                        candidate.DatePrecision, candidate.Category, yearsAgo.Value));
                }
            }

            return new WeeklyDigest
            {
                WeekStart = weekStart,
                Memories = memories
                    .OrderBy(m => m.YearsAgo)
                    .ThenBy(m => m.StartDate)
                    .ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing weekly digest for week of {WeekStart}", weekStart);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<UpcomingAnniversary>> GetUpcomingAnniversariesAsync(int daysAhead = 14)
    {
        var today = DateTime.Today;
        var windowEnd = today.AddDays(daysAhead);
        var results = new List<UpcomingAnniversary>();

        try
        {
            // Milestones: every anniversary counts, whatever the year mark.
            var milestones = await _milestoneRepository.GetAllAsync();
            foreach (var milestone in milestones)
            {
                if (!TryGetUpcomingOccurrence(milestone.Date, today, windowEnd, out var occurrence))
                {
                    continue;
                }

                if (milestone.Date.Date > occurrence)
                {
                    // The milestone itself is dated beyond this occurrence
                    // (planned for a future year) - not an anniversary yet.
                    continue;
                }

                results.Add(new UpcomingAnniversary
                {
                    Date = occurrence,
                    YearsSince = occurrence.Year - milestone.Date.Year,
                    Title = milestone.Name,
                    SourceKind = AnniversarySourceKind.Milestone,
                    SourceId = milestone.MilestoneId
                });
            }

            // Events: only round-number year marks, and only when the anchor
            // day is actually known (Exact/Day precision).
            await using var context = await _contextFactory.CreateDbContextAsync();
            var events = await context.Events
                .AsNoTracking()
                .Where(e => e.DatePrecision == DatePrecision.Exact || e.DatePrecision == DatePrecision.Day)
                .Select(e => new { e.EventId, e.Title, e.StartDate })
                .ToListAsync();

            foreach (var evt in events)
            {
                if (!TryGetUpcomingOccurrence(evt.StartDate, today, windowEnd, out var occurrence))
                {
                    continue;
                }

                var yearsSince = occurrence.Year - evt.StartDate.Year;
                if (!RoundYearMarks.Contains(yearsSince))
                {
                    continue;
                }

                results.Add(new UpcomingAnniversary
                {
                    Date = occurrence,
                    YearsSince = yearsSince,
                    Title = evt.Title,
                    SourceKind = AnniversarySourceKind.Event,
                    SourceId = evt.EventId
                });
            }

            return results
                .OrderBy(a => a.Date)
                .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing upcoming anniversaries ({DaysAhead} days ahead)", daysAhead);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RecordViewAsync(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return;
        }

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var evt = await context.Events.FirstOrDefaultAsync(e => e.EventId == eventId);
            if (evt == null)
            {
                _logger.LogWarning("RecordViewAsync: event {EventId} not found", eventId);
                return;
            }

            evt.ViewCount++;
            evt.LastViewedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording view for event {EventId}", eventId);
            throw;
        }
    }

    /// <summary>
    /// Pure launch-toast guard: show at most once per day, and only when the
    /// daily toast is enabled. <paramref name="dailyToastEnabledSetting"/> is
    /// the raw setting string - anything except an explicit "false" counts as
    /// enabled (the seeded default is "true", and an absent row must not
    /// silence the toast). <paramref name="lastToastDateSetting"/> is the
    /// stored yyyy-MM-dd of the last toast; null/empty means never shown.
    /// </summary>
    public static bool ShouldShowToast(string? lastToastDateSetting, string todayIsoDate, string? dailyToastEnabledSetting)
    {
        if (string.Equals(dailyToastEnabledSetting?.Trim(), "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.Equals(lastToastDateSetting?.Trim(), todayIsoDate, StringComparison.Ordinal);
    }

    /// <summary>
    /// True when <paramref name="anchor"/>'s month+day fall on
    /// <paramref name="onDate"/>, treating a Feb 29 anchor as Feb 28 in
    /// non-leap years (so leap-day memories still come around annually).
    /// </summary>
    internal static bool MatchesAnniversaryDay(DateTime anchor, DateTime onDate)
    {
        if (anchor.Month == onDate.Month && anchor.Day == onDate.Day)
        {
            return true;
        }

        return anchor.Month == 2 && anchor.Day == 29
            && onDate.Month == 2 && onDate.Day == 28
            && !DateTime.IsLeapYear(onDate.Year);
    }

    /// <summary>
    /// The anchor's occurrence within [windowStart, windowEnd]: this year's
    /// occurrence, or next year's when this year's has already passed.
    /// </summary>
    internal static bool TryGetUpcomingOccurrence(DateTime anchor, DateTime windowStart, DateTime windowEnd, out DateTime occurrence)
    {
        occurrence = GetOccurrenceInYear(anchor, windowStart.Year);
        if (occurrence < windowStart.Date)
        {
            occurrence = GetOccurrenceInYear(anchor, windowStart.Year + 1);
        }

        return occurrence >= windowStart.Date && occurrence <= windowEnd.Date;
    }

    /// <summary>
    /// The anchor's month+day realized in <paramref name="year"/>, with Feb 29
    /// falling back to Feb 28 in non-leap years.
    /// </summary>
    internal static DateTime GetOccurrenceInYear(DateTime anchor, int year)
    {
        var day = anchor.Month == 2 && anchor.Day == 29 && !DateTime.IsLeapYear(year)
            ? 28
            : anchor.Day;
        return new DateTime(year, anchor.Month, day);
    }

    /// <summary>
    /// Weighted random pick for the Neglected bias: weight = 1/(1+viewCount),
    /// quartered again when the event was viewed within the last 30 days.
    /// Candidates must arrive in deterministic order (the cumulative walk's
    /// outcome depends on it for a given RNG value).
    /// </summary>
    internal static string PickWeighted(
        IReadOnlyList<(string EventId, int ViewCount, DateTime? LastViewedAt)> candidates,
        Random random,
        DateTime utcNow)
    {
        var weights = new double[candidates.Count];
        double total = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            var weight = 1.0 / (1 + Math.Max(0, candidates[i].ViewCount));
            var lastViewed = candidates[i].LastViewedAt;
            if (lastViewed.HasValue && utcNow - lastViewed.Value < RecentViewWindow)
            {
                weight *= RecentViewPenalty;
            }

            weights[i] = weight;
            total += weight;
        }

        var roll = random.NextDouble() * total;
        double cumulative = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
            {
                return candidates[i].EventId;
            }
        }

        // Floating-point tail guard (roll == total).
        return candidates[^1].EventId;
    }

    private static ResurfacedMemory ToResurfacedMemory(
        string eventId,
        string title,
        string? description,
        DateTime startDate,
        DatePrecision precision,
        string? category,
        int yearsAgo)
    {
        return new ResurfacedMemory
        {
            EventId = eventId,
            Title = title,
            Description = description,
            StartDate = startDate,
            DatePrecision = precision,
            DisplayDate = DateDisplay.FormatPrecise(startDate, precision),
            Category = category,
            YearsAgo = yearsAgo
        };
    }
}
