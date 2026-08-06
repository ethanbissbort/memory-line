using FluentAssertions;
using MemoryTimeline.Core.Models;
using Xunit;

namespace MemoryTimeline.Tests.Models;

/// <summary>
/// Unit tests for the pure last-request-wins debounce decision used by the
/// timeline's coalesced viewport reloads: a burst of pan/zoom ticks must
/// produce exactly ONE reload, and it must belong to the FINAL tick.
/// </summary>
public class ReloadCoalescerTests
{
    #region Ticket Semantics

    [Fact]
    public void RecordTick_RapidBurst_OnlyLatestTicketIsCurrent()
    {
        // Arrange
        var coalescer = new ReloadCoalescer();

        // Act - a burst of 10 rapid ticks (e.g. a scrollbar drag)
        var tickets = Enumerable.Range(0, 10)
            .Select(_ => coalescer.RecordTick())
            .ToList();

        // Assert - exactly one ticket survives: the last one
        tickets.Count(t => coalescer.IsCurrent(t)).Should().Be(1);
        coalescer.IsCurrent(tickets[^1]).Should().BeTrue();
    }

    [Fact]
    public void IsCurrent_SupersededTicket_ReturnsFalse()
    {
        // Arrange
        var coalescer = new ReloadCoalescer();
        var first = coalescer.RecordTick();

        // Act
        var second = coalescer.RecordTick();

        // Assert
        coalescer.IsCurrent(first).Should().BeFalse();
        coalescer.IsCurrent(second).Should().BeTrue();
    }

    [Fact]
    public void RecordTick_ReturnsMonotonicallyIncreasingTickets()
    {
        // Arrange
        var coalescer = new ReloadCoalescer();

        // Act
        var t1 = coalescer.RecordTick();
        var t2 = coalescer.RecordTick();
        var t3 = coalescer.RecordTick();

        // Assert
        t2.Should().BeGreaterThan(t1);
        t3.Should().BeGreaterThan(t2);
        coalescer.LatestTicket.Should().Be(t3);
    }

    #endregion

    #region Coalescing Simulation

    [Fact]
    public void Simulation_PanBurst_ExactlyOneReloadAndFinalPositionWins()
    {
        // Arrange - simulate the ViewModel's pipeline: every pan tick records
        // a ticket while the viewport moves to a new position; after the burst
        // each pending task checks its own ticket before reloading.
        var coalescer = new ReloadCoalescer();
        var ticketByPosition = new Dictionary<long, double>();
        double viewportPosition = 0;

        for (var i = 1; i <= 25; i++)
        {
            viewportPosition = i * 40.0; // the viewport keeps moving
            var ticket = coalescer.RecordTick();
            ticketByPosition[ticket] = viewportPosition;
        }

        // Act - the quiet period elapses; every scheduled task wakes up and
        // asks whether it still owns the reload.
        var reloadedPositions = ticketByPosition
            .Where(kv => coalescer.IsCurrent(kv.Key))
            .Select(kv => kv.Value)
            .ToList();

        // Assert - one reload, and it is the FINAL position (never an
        // intermediate one, and never zero reloads: the last tick always wins).
        reloadedPositions.Should().HaveCount(1);
        reloadedPositions[0].Should().Be(viewportPosition);
    }

    [Fact]
    public void Simulation_NewGestureAfterSettle_SchedulesItsOwnReload()
    {
        // Arrange - first gesture settles and fires
        var coalescer = new ReloadCoalescer();
        var firstGesture = coalescer.RecordTick();
        coalescer.IsCurrent(firstGesture).Should().BeTrue();

        // Act - a later gesture ticks again
        var secondGesture = coalescer.RecordTick();

        // Assert - the old ticket is dead, the new gesture owns the next reload
        coalescer.IsCurrent(firstGesture).Should().BeFalse();
        coalescer.IsCurrent(secondGesture).Should().BeTrue();
    }

    #endregion

    #region Configuration

    [Fact]
    public void QuietPeriod_Default_Is150Milliseconds()
    {
        // Arrange / Act
        var coalescer = new ReloadCoalescer();

        // Assert
        coalescer.QuietPeriod.Should().Be(TimeSpan.FromMilliseconds(150));
        ReloadCoalescer.DefaultQuietPeriod.Should().Be(TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public void Constructor_CustomQuietPeriod_IsUsed()
    {
        // Arrange / Act
        var coalescer = new ReloadCoalescer(TimeSpan.FromMilliseconds(300));

        // Assert
        coalescer.QuietPeriod.Should().Be(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void Constructor_NonPositiveQuietPeriod_Throws()
    {
        // Act
        var zero = () => new ReloadCoalescer(TimeSpan.Zero);
        var negative = () => new ReloadCoalescer(TimeSpan.FromMilliseconds(-1));

        // Assert
        zero.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion
}
