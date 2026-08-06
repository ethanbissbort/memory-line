using FluentAssertions;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Unit tests for <see cref="ResurfacingService"/>: On This Day precision
/// rules (the F1 integration), the neglected-random weighting, view
/// recording, the weekly digest, upcoming anniversaries, and the pure
/// launch-toast guard.
/// </summary>
public class ResurfacingServiceTests : IDisposable
{
    private readonly TestDbContextFactory _contextFactory;
    private readonly MilestoneRepository _milestoneRepository;
    private readonly EventRepository _eventRepository;
    private readonly Mock<ILogger<ResurfacingService>> _loggerMock;

    public ResurfacingServiceTests()
    {
        _contextFactory = TestDbContextFactory.CreateInMemory();
        _milestoneRepository = new MilestoneRepository(_contextFactory);
        _eventRepository = new EventRepository(_contextFactory);
        _loggerMock = new Mock<ILogger<ResurfacingService>>();
    }

    /// <summary>
    /// Builds the service under test; the internal constructor overload lets
    /// each test inject a deterministic RNG.
    /// </summary>
    private ResurfacingService CreateService(Random? random = null)
    {
        return new ResurfacingService(
            _contextFactory,
            _milestoneRepository,
            _eventRepository,
            _loggerMock.Object,
            random ?? new Random(42));
    }

    private async Task<Event> SeedEventAsync(
        string title,
        DateTime startDate,
        DatePrecision precision = DatePrecision.Day,
        int viewCount = 0,
        DateTime? lastViewedAt = null,
        string? id = null)
    {
        var evt = new Event
        {
            EventId = id ?? Guid.NewGuid().ToString(),
            Title = title,
            StartDate = startDate,
            DatePrecision = precision,
            ViewCount = viewCount,
            LastViewedAt = lastViewedAt,
            Category = "other"
        };

        await using var context = _contextFactory.CreateDbContext();
        context.Events.Add(evt);
        await context.SaveChangesAsync();
        return evt;
    }

    private async Task SeedMilestoneAsync(string name, DateTime date)
    {
        await using var context = _contextFactory.CreateDbContext();
        context.Milestones.Add(new Milestone { Name = name, Date = date });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// An anchor in <paramref name="year"/> sharing month+day with
    /// <paramref name="target"/>, clamping a Feb 29 target to Feb 28 so the
    /// anchor is constructible in any year.
    /// </summary>
    private static DateTime SafeAnchor(int year, DateTime target)
    {
        var day = target.Month == 2 && target.Day == 29 ? 28 : target.Day;
        return new DateTime(year, target.Month, day);
    }

    /// <summary>
    /// Fully deterministic RNG: NextDouble always returns the configured
    /// fraction, Next(max) always returns the configured index.
    /// </summary>
    private sealed class FixedRandom : Random
    {
        private readonly double _nextDouble;
        private readonly int _nextInt;

        public FixedRandom(double nextDouble, int nextInt = 0)
        {
            _nextDouble = nextDouble;
            _nextInt = nextInt;
        }

        public override double NextDouble() => _nextDouble;

        public override int Next(int maxValue) => Math.Min(_nextInt, maxValue - 1);
    }

    #region GetOnThisDayAsync

    [Fact]
    public async Task GetOnThisDayAsync_DayPrecisionMatchingMonthAndDay_SurfacesWithYearsAgo()
    {
        // Arrange
        await SeedEventAsync("Ran the marathon", new DateTime(2015, 3, 14), DatePrecision.Day);
        var service = CreateService();

        // Act
        var result = await service.GetOnThisDayAsync(new DateTime(2020, 3, 14));

        // Assert
        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Ran the marathon");
        result[0].YearsAgo.Should().Be(5);
        result[0].DatePrecision.Should().Be(DatePrecision.Day);
        result[0].DisplayDate.Should().Be("14 Mar 2015");
    }

    [Fact]
    public async Task GetOnThisDayAsync_DayPrecisionDifferentDay_DoesNotSurface()
    {
        // Arrange
        await SeedEventAsync("Ran the marathon", new DateTime(2015, 3, 14), DatePrecision.Day);
        var service = CreateService();

        // Act
        var result = await service.GetOnThisDayAsync(new DateTime(2020, 3, 15));

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOnThisDayAsync_CoarsePrecisionEvents_NeverSurfaceDaily()
    {
        // Arrange - anchors all share the queried month+day, but no coarse
        // precision has a meaningful day-of-year
        await SeedEventAsync("Month memory", new DateTime(2015, 3, 14), DatePrecision.Month);
        await SeedEventAsync("Season memory", new DateTime(2016, 3, 14), DatePrecision.Season);
        await SeedEventAsync("Year memory", new DateTime(2017, 3, 14), DatePrecision.Year);
        await SeedEventAsync("Decade memory", new DateTime(2011, 3, 14), DatePrecision.Decade);
        await SeedEventAsync("Unknown memory", new DateTime(2018, 3, 14), DatePrecision.Unknown);
        var service = CreateService();

        // Act
        var result = await service.GetOnThisDayAsync(new DateTime(2020, 3, 14));

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOnThisDayAsync_ExactPrecision_Surfaces()
    {
        // Arrange
        await SeedEventAsync("Graduation ceremony", new DateTime(2010, 3, 14, 10, 30, 0), DatePrecision.Exact);
        var service = CreateService();

        // Act
        var result = await service.GetOnThisDayAsync(new DateTime(2020, 3, 14));

        // Assert
        result.Should().HaveCount(1);
        result[0].YearsAgo.Should().Be(10);
        result[0].DisplayDate.Should().Be("14 Mar 2010, 10:30");
    }

    [Fact]
    public async Task GetOnThisDayAsync_Feb29Anchor_FallsBackToFeb28InNonLeapYearsOnly()
    {
        // Arrange
        await SeedEventAsync("Leap day event", new DateTime(2016, 2, 29), DatePrecision.Day);
        var service = CreateService();

        // Act + Assert - non-leap year: surfaces on Feb 28
        var nonLeap = await service.GetOnThisDayAsync(new DateTime(2021, 2, 28));
        nonLeap.Should().HaveCount(1);
        nonLeap[0].YearsAgo.Should().Be(5);

        // Act + Assert - leap year: surfaces on Feb 29, not Feb 28
        var leapDay = await service.GetOnThisDayAsync(new DateTime(2020, 2, 29));
        leapDay.Should().HaveCount(1);
        leapDay[0].YearsAgo.Should().Be(4);

        var leapFeb28 = await service.GetOnThisDayAsync(new DateTime(2020, 2, 28));
        leapFeb28.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOnThisDayAsync_SameYearEvent_DoesNotSurface()
    {
        // Arrange - an event dated today (this year) is not a memory yet
        await SeedEventAsync("Happened today", new DateTime(2020, 3, 14), DatePrecision.Day);
        var service = CreateService();

        // Act
        var result = await service.GetOnThisDayAsync(new DateTime(2020, 3, 14));

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetRandomAsync

    [Fact]
    public async Task GetRandomAsync_EmptyArchive_ReturnsNull()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetRandomAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRandomAsync_NeglectedBias_PrefersUnviewedEvent()
    {
        // Arrange - the unviewed event holds virtually all of the weight:
        // 1.0 vs (1/1001)*0.25 for the heavily viewed, recently seen event.
        // A mid-range roll therefore lands on the unviewed candidate.
        await SeedEventAsync("Forgotten trip", new DateTime(2012, 5, 1), id: "a-unviewed");
        await SeedEventAsync("Favorite story", new DateTime(2013, 6, 2), id: "b-heavily-viewed",
            viewCount: 1000, lastViewedAt: DateTime.UtcNow.AddDays(-1));
        var service = CreateService(new FixedRandom(0.5));

        // Act
        var result = await service.GetRandomAsync(RandomBias.Neglected);

        // Assert
        result.Should().NotBeNull();
        result!.EventId.Should().Be("a-unviewed");
        result.Title.Should().Be("Forgotten trip");
        result.DisplayDate.Should().Be("1 May 2012");
    }

    [Fact]
    public async Task GetRandomAsync_NeglectedBias_HighViewEventRemainsPossible()
    {
        // Arrange - a roll at the very top of the range lands in the heavily
        // viewed event's (tiny) slice: bias, not exclusion.
        await SeedEventAsync("Forgotten trip", new DateTime(2012, 5, 1), id: "a-unviewed");
        await SeedEventAsync("Favorite story", new DateTime(2013, 6, 2), id: "b-heavily-viewed",
            viewCount: 1000, lastViewedAt: DateTime.UtcNow.AddDays(-1));
        var service = CreateService(new FixedRandom(0.9999));

        // Act
        var result = await service.GetRandomAsync(RandomBias.Neglected);

        // Assert
        result.Should().NotBeNull();
        result!.EventId.Should().Be("b-heavily-viewed");
    }

    [Fact]
    public void PickWeighted_RecentlyViewedEvent_GetsPenalizedWeight()
    {
        // Arrange - equal view counts; only the recency penalty differs.
        // With the penalty: weights 0.0625 vs 0.25 (total 0.3125), roll
        // 0.4*0.3125 = 0.125 lands on the not-recent event. Without the
        // penalty the same roll (0.4*0.5 = 0.2 < 0.25) would land on the
        // recently viewed one - so this pins the penalty's existence.
        var utcNow = DateTime.UtcNow;
        var candidates = new List<(string EventId, int ViewCount, DateTime? LastViewedAt)>
        {
            ("a-recently-viewed", 3, utcNow.AddDays(-1)),
            ("b-not-recent", 3, utcNow.AddDays(-60)),
        };

        // Act
        var picked = ResurfacingService.PickWeighted(candidates, new FixedRandom(0.4), utcNow);

        // Assert
        picked.Should().Be("b-not-recent");
    }

    [Fact]
    public async Task GetRandomAsync_AnyBias_PicksUniformlyByIndex()
    {
        // Arrange - Any bias uses Next(count) over id-ordered candidates
        await SeedEventAsync("First", new DateTime(2012, 5, 1), id: "a-first", viewCount: 100);
        await SeedEventAsync("Second", new DateTime(2013, 6, 2), id: "b-second");
        var service = CreateService(new FixedRandom(0.0, nextInt: 1));

        // Act
        var result = await service.GetRandomAsync(RandomBias.Any);

        // Assert
        result.Should().NotBeNull();
        result!.EventId.Should().Be("b-second");
    }

    #endregion

    #region RecordViewAsync

    [Fact]
    public async Task RecordViewAsync_IncrementsViewCountAndStampsLastViewedAt()
    {
        // Arrange
        await SeedEventAsync("Viewed memory", new DateTime(2014, 4, 4), id: "evt-1");
        var service = CreateService();

        // Act
        await service.RecordViewAsync("evt-1");

        // Assert
        await using (var context = _contextFactory.CreateDbContext())
        {
            var stored = context.Events.Single(e => e.EventId == "evt-1");
            stored.ViewCount.Should().Be(1);
            stored.LastViewedAt.Should().NotBeNull();
            stored.LastViewedAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
        }

        // Act again - the count keeps incrementing
        await service.RecordViewAsync("evt-1");

        await using (var context = _contextFactory.CreateDbContext())
        {
            context.Events.Single(e => e.EventId == "evt-1").ViewCount.Should().Be(2);
        }
    }

    [Fact]
    public async Task RecordViewAsync_UnknownEvent_DoesNotThrow()
    {
        // Arrange
        var service = CreateService();

        // Act
        var act = async () => await service.RecordViewAsync("does-not-exist");

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region GetWeeklyDigestAsync

    [Fact]
    public async Task GetWeeklyDigestAsync_IncludesDayEventsInWeekAndMonthPrecisionMatchingWeekMonth()
    {
        // Arrange - week of Wed 15 Jul 2020 = Sun 12 Jul .. Sat 18 Jul
        await SeedEventAsync("In week", new DateTime(2015, 7, 14), DatePrecision.Day);
        await SeedEventAsync("July month", new DateTime(2016, 7, 1), DatePrecision.Month);
        await SeedEventAsync("August month", new DateTime(2016, 8, 1), DatePrecision.Month);
        await SeedEventAsync("Year only", new DateTime(2015, 7, 14), DatePrecision.Year);
        await SeedEventAsync("Out of week", new DateTime(2015, 7, 25), DatePrecision.Day);
        await SeedEventAsync("July this year", new DateTime(2020, 7, 1), DatePrecision.Month);
        var service = CreateService();

        // Act
        var digest = await service.GetWeeklyDigestAsync(new DateTime(2020, 7, 15));

        // Assert
        digest.WeekStart.Should().Be(new DateTime(2020, 7, 12));
        digest.Memories.Select(m => m.Title)
            .Should().BeEquivalentTo(new[] { "In week", "July month" });

        var dayMemory = digest.Memories.Single(m => m.Title == "In week");
        dayMemory.YearsAgo.Should().Be(5);
        dayMemory.DisplayDate.Should().Be("14 Jul 2015");

        var monthMemory = digest.Memories.Single(m => m.Title == "July month");
        monthMemory.YearsAgo.Should().Be(4);
        monthMemory.DisplayDate.Should().Be("July 2016");
    }

    #endregion

    #region GetUpcomingAnniversariesAsync

    [Fact]
    public async Task GetUpcomingAnniversariesAsync_MilestoneWithinWindow_Included()
    {
        // Arrange - a milestone whose month+day fall 5 days ahead (any year mark counts)
        var target = DateTime.Today.AddDays(5);
        var anchor = SafeAnchor(2015, target);
        await SeedMilestoneAsync("Wedding day", anchor);
        var service = CreateService();

        // Act
        var results = await service.GetUpcomingAnniversariesAsync(14);

        // Assert
        results.Should().ContainSingle();
        var anniversary = results[0];
        anniversary.SourceKind.Should().Be(AnniversarySourceKind.Milestone);
        anniversary.Title.Should().Be("Wedding day");

        var expectedOccurrence = new DateTime(target.Year, anchor.Month, anchor.Day);
        anniversary.Date.Should().Be(expectedOccurrence);
        anniversary.YearsSince.Should().Be(expectedOccurrence.Year - 2015);
    }

    [Fact]
    public async Task GetUpcomingAnniversariesAsync_MilestoneOutsideWindow_Excluded()
    {
        // Arrange - month+day 20 days ahead, beyond the 14-day window
        var target = DateTime.Today.AddDays(20);
        await SeedMilestoneAsync("Too far out", SafeAnchor(2015, target));
        var service = CreateService();

        // Act
        var results = await service.GetUpcomingAnniversariesAsync(14);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUpcomingAnniversariesAsync_RoundNumberEventIncluded_NonRoundAndCoarseExcluded()
    {
        // Arrange
        var roundTarget = DateTime.Today.AddDays(3);
        await SeedEventAsync("Round five", SafeAnchor(roundTarget.Year - 5, roundTarget), DatePrecision.Day);

        var nonRoundTarget = DateTime.Today.AddDays(4);
        await SeedEventAsync("Non round", SafeAnchor(nonRoundTarget.Year - 3, nonRoundTarget), DatePrecision.Day);

        var coarseTarget = DateTime.Today.AddDays(2);
        await SeedEventAsync("Coarse date", SafeAnchor(coarseTarget.Year - 5, coarseTarget), DatePrecision.Month);

        var service = CreateService();

        // Act
        var results = await service.GetUpcomingAnniversariesAsync(14);

        // Assert - only the Exact/Day event at a round year mark surfaces
        results.Should().ContainSingle();
        var anniversary = results[0];
        anniversary.SourceKind.Should().Be(AnniversarySourceKind.Event);
        anniversary.Title.Should().Be("Round five");
        anniversary.YearsSince.Should().Be(5);
    }

    #endregion

    #region ShouldShowToast

    [Theory]
    [InlineData(null, "2026-08-05", "true", true)]           // never shown
    [InlineData("", "2026-08-05", "true", true)]             // never shown (empty)
    [InlineData("2026-08-04", "2026-08-05", "true", true)]   // last shown yesterday
    [InlineData("2026-08-05", "2026-08-05", "true", false)]  // already shown today
    [InlineData("2026-08-04", "2026-08-05", "false", false)] // disabled
    [InlineData("2026-08-05", "2026-08-05", "false", false)] // disabled + shown
    [InlineData(null, "2026-08-05", null, true)]             // absent setting defaults enabled
    public void ShouldShowToast_Scenarios_ReturnExpected(
        string? lastToastDate, string today, string? enabledSetting, bool expected)
    {
        ResurfacingService.ShouldShowToast(lastToastDate, today, enabledSetting)
            .Should().Be(expected);
    }

    [Fact]
    public void ShouldShowToast_TwoLaunchesSameDay_ShowsOnlyOnce()
    {
        // Arrange - first launch of the day: nothing stamped yet
        var today = "2026-08-05";

        // Act + Assert - first launch shows
        ResurfacingService.ShouldShowToast(null, today, "true").Should().BeTrue();

        // After showing, the app stamps last_toast_date = today; a second
        // launch the same day must be suppressed.
        ResurfacingService.ShouldShowToast(today, today, "true").Should().BeFalse();
    }

    #endregion

    public void Dispose()
    {
        using var context = _contextFactory.CreateDbContext();
        context.Database.EnsureDeleted();
    }
}
