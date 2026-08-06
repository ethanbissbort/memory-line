namespace MemoryTimeline.Core.Models;

/// <summary>
/// Pure last-request-wins debounce decision for viewport reloads.
///
/// Every pan/zoom tick calls <see cref="RecordTick"/> and receives a ticket.
/// The caller then waits out <see cref="QuietPeriod"/> and reloads only when
/// its ticket is still the newest (<see cref="IsCurrent"/>). A burst of N
/// ticks therefore produces exactly ONE reload - the one belonging to the
/// LAST tick - and it runs against the viewport's final position, because the
/// caller reads the live viewport at fire time. Intermediate tickets are
/// superseded, never the final one, so the settled position is never dropped
/// (unlike the old IsLoading bail-out, which discarded ticks mid-load).
///
/// The class is deliberately clock-free: the ticket comparison is the entire
/// decision, which makes it trivially unit-testable (a simulated burst checks
/// each ticket and exactly one fires), while the real timing lives in the
/// caller's awaited delay. Thread-safe via Interlocked, although the timeline
/// only ticks from the UI thread.
/// </summary>
public sealed class ReloadCoalescer
{
    /// <summary>
    /// Default quiet period: reload ~150ms after the last pan/zoom tick.
    /// </summary>
    public static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromMilliseconds(150);

    private long _latestTicket;

    public ReloadCoalescer()
        : this(DefaultQuietPeriod)
    {
    }

    public ReloadCoalescer(TimeSpan quietPeriod)
    {
        if (quietPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(quietPeriod), "The quiet period must be positive.");
        }

        QuietPeriod = quietPeriod;
    }

    /// <summary>How long after the last tick the reload should fire.</summary>
    public TimeSpan QuietPeriod { get; }

    /// <summary>The most recently issued ticket (0 before the first tick).</summary>
    public long LatestTicket => Interlocked.Read(ref _latestTicket);

    /// <summary>
    /// Records a pan/zoom tick and returns its ticket. Issuing a new ticket
    /// supersedes every earlier one.
    /// </summary>
    public long RecordTick() => Interlocked.Increment(ref _latestTicket);

    /// <summary>
    /// True when <paramref name="ticket"/> is still the newest tick - i.e. the
    /// burst has settled and this caller owns the single coalesced reload.
    /// </summary>
    public bool IsCurrent(long ticket) => Interlocked.Read(ref _latestTicket) == ticket;
}
