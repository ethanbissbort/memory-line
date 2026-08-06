using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Guided-recall implementation. Registered as a SINGLETON: stateless over the
/// repositories/factory, so all dependencies must be singleton-safe.
///
/// Generation runs in priority order - (a) density gaps, (b) dangling people,
/// (c) era edges, (d) thin events, (e) anniversary anchors - and every
/// candidate is deduped through <see cref="IRecallPromptRepository.ExistsForAsync"/>
/// against ALL existing rows (including Dismissed/Answered ones), so a skipped
/// question is never asked again. Question text is produced by the configured
/// LLM when a key is present; on <see cref="ConfigurationException"/> or any
/// other LLM failure the per-kind deterministic templates below are the
/// no-key path and are written to stand on their own.
/// </summary>
public class RecallPromptService : IRecallPromptService
{
    /// <summary>Hard cap on new prompt rows created per GetPromptsAsync call.</summary>
    private const int MaxGeneratedPerCall = 10;

    /// <summary>How long a "Later" dismissal snoozes a prompt.</summary>
    private const int SnoozeDays = 14;

    /// <summary>Archives smaller than this skip density-gap detection entirely.</summary>
    private const int MinEventsForGapDetection = 10;

    /// <summary>A year is a gap when its count is at most this fraction of the median.</summary>
    private const double GapDensityFraction = 0.2;

    /// <summary>Descriptions shorter than this count as "thin".</summary>
    private const int ThinDescriptionLength = 40;

    /// <summary>SourceLabel / entity_name column limit.</summary>
    private const int MaxLabelLength = 200;

    private static readonly int[] AnniversaryYearsAgo = { 10, 20, 25 };

    private const string QuestionSystemPrompt =
        "You help someone rebuild their personal memory archive. Given a description of a gap in " +
        "their archive, reply with exactly one specific, warm, answerable question that invites " +
        "them to fill that gap. At most 200 characters. No preamble, no quotation marks, no " +
        "explanation - just the question.";

    private readonly IRecallPromptRepository _promptRepository;
    private readonly IRecordingQueueRepository _queueRepository;
    private readonly IQueueService _queueService;
    private readonly IAnalyticsService _analyticsService;
    private readonly ILlmService _llmService;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<RecallPromptService> _logger;

    public RecallPromptService(
        IRecallPromptRepository promptRepository,
        IRecordingQueueRepository queueRepository,
        IQueueService queueService,
        IAnalyticsService analyticsService,
        ILlmService llmService,
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<RecallPromptService> logger)
    {
        _promptRepository = promptRepository;
        _queueRepository = queueRepository;
        _queueService = queueService;
        _analyticsService = analyticsService;
        _llmService = llmService;
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<RecallPrompt>> GetPromptsAsync(int count = 5, CancellationToken ct = default)
    {
        if (count <= 0)
        {
            return new List<RecallPrompt>();
        }

        var now = DateTime.UtcNow;
        var active = await _promptRepository.GetActiveAsync(now);

        if (active.Count < count)
        {
            var created = await GenerateAsync(Math.Min(count - active.Count, MaxGeneratedPerCall), now, ct);
            if (created > 0)
            {
                active = await _promptRepository.GetActiveAsync(now);
            }
        }

        return active.Take(count).ToList();
    }

    /// <inheritdoc />
    public async Task DismissAsync(string promptId, DismissReason reason)
    {
        var prompt = await GetRequiredPromptAsync(promptId);
        var now = DateTime.UtcNow;

        switch (reason)
        {
            case DismissReason.Later:
                // "Later" is a snooze, not a dismissal: the prompt stays Active
                // with snoozed_until 14 days out, so GetActiveAsync hides it
                // until then and it returns to the rotation automatically.
                prompt.DismissReason = DismissReason.Later;
                prompt.SnoozedUntil = now.AddDays(SnoozeDays);
                break;

            case DismissReason.NotInterested:
            case DismissReason.AlreadyCovered:
                // Permanent. The row is kept (never deleted) so the
                // ExistsForAsync dedupe guarantees this question is never
                // generated again.
                prompt.Status = RecallPromptStatus.Dismissed;
                prompt.DismissReason = reason;
                prompt.SnoozedUntil = null;
                break;

            default:
                throw new ArgumentException($"'{reason}' is not a valid dismissal reason.", nameof(reason));
        }

        await _promptRepository.UpdateAsync(prompt);
        _logger.LogInformation("Recall prompt {PromptId} skipped ({Reason})", promptId, reason);
    }

    /// <inheritdoc />
    public async Task<string> StartAnsweringAsync(string promptId, string answerText)
    {
        var prompt = await GetRequiredPromptAsync(promptId);

        // The queue row carries the question as its label so the answer is
        // recognizable in the queue list and in review. EnqueueTextAsync
        // throws ArgumentException for empty text BEFORE the prompt is touched.
        var queueId = await _queueService.EnqueueTextAsync(answerText, Clip(prompt.Question, MaxLabelLength));

        prompt.QueueId = queueId;
        prompt.Status = RecallPromptStatus.Answered;
        await _promptRepository.UpdateAsync(prompt);

        _logger.LogInformation("Recall prompt {PromptId} answered with typed text as queue item {QueueId}",
            promptId, queueId);
        return queueId;
    }

    /// <inheritdoc />
    public async Task MarkAnsweredAsync(string promptId, string queueId)
    {
        var prompt = await GetRequiredPromptAsync(promptId);

        var queueItem = await _queueRepository.GetByIdAsync(queueId);
        if (queueItem == null)
        {
            throw new InvalidOperationException($"Queue item not found: {queueId}");
        }

        // Stamp the question onto the queue row so the recorded answer is
        // recognizable, but never overwrite a label the row already has.
        if (string.IsNullOrWhiteSpace(queueItem.SourceLabel))
        {
            queueItem.SourceLabel = Clip(prompt.Question, MaxLabelLength);
            await _queueRepository.UpdateAsync(queueItem);
        }

        prompt.QueueId = queueId;
        prompt.Status = RecallPromptStatus.Answered;
        await _promptRepository.UpdateAsync(prompt);

        _logger.LogInformation("Recall prompt {PromptId} answered by queue item {QueueId}", promptId, queueId);
    }

    #region Generation

    /// <summary>
    /// A potential prompt found by one of the gap detectors, before dedupe and
    /// question generation. <see cref="DedupeAnchorStart"/> is null for
    /// entity-keyed kinds (the entity id alone identifies the gap) and the
    /// period start for period-keyed kinds.
    /// </summary>
    private sealed record PromptCandidate(
        RecallPromptKind Kind,
        string? EntityId,
        string? EntityName,
        DateTime? AnchorStart,
        DateTime? AnchorEnd,
        DateTime? DedupeAnchorStart,
        string LlmDescription,
        string FallbackQuestion);

    /// <summary>
    /// Creates up to <paramref name="maxNew"/> new prompt rows from the
    /// highest-priority undeduped gap candidates. One broken candidate is
    /// logged and skipped, never allowed to stop the rest.
    /// </summary>
    private async Task<int> GenerateAsync(int maxNew, DateTime now, CancellationToken ct)
    {
        if (maxNew <= 0)
        {
            return 0;
        }

        List<PromptCandidate> candidates;
        try
        {
            candidates = await BuildCandidatesAsync(now);
        }
        catch (Exception ex)
        {
            // Candidate discovery reads several tables; a failure here should
            // degrade to "no new prompts", not break the page showing them.
            _logger.LogError(ex, "Recall prompt candidate discovery failed; generating nothing this call.");
            return 0;
        }

        var created = 0;
        foreach (var candidate in candidates)
        {
            if (created >= maxNew)
            {
                break;
            }

            ct.ThrowIfCancellationRequested();

            try
            {
                if (await _promptRepository.ExistsForAsync(candidate.Kind, candidate.EntityId, candidate.DedupeAnchorStart))
                {
                    continue; // Already asked (possibly dismissed) - never re-ask.
                }

                var question = await BuildQuestionAsync(candidate, ct);

                await _promptRepository.AddAsync(new RecallPrompt
                {
                    Kind = candidate.Kind,
                    Question = question,
                    AnchorStart = candidate.AnchorStart,
                    AnchorEnd = candidate.AnchorEnd,
                    EntityId = candidate.EntityId,
                    EntityName = candidate.EntityName,
                    Status = RecallPromptStatus.Active,
                    DismissReason = DismissReason.None,
                    CreatedAt = now,
                    UpdatedAt = now
                });

                created++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate a recall prompt ({Kind}, entity {EntityId})",
                    candidate.Kind, candidate.EntityId);
            }
        }

        if (created > 0)
        {
            _logger.LogInformation("Generated {Count} new recall prompt(s)", created);
        }

        return created;
    }

    /// <summary>All gap candidates, in generation priority order.</summary>
    private async Task<List<PromptCandidate>> BuildCandidatesAsync(DateTime now)
    {
        var candidates = new List<PromptCandidate>();
        candidates.AddRange(await BuildDensityGapCandidatesAsync());
        candidates.AddRange(await BuildDanglingPersonCandidatesAsync());
        candidates.AddRange(await BuildEraEdgeCandidatesAsync(now));
        candidates.AddRange(await BuildThinEventCandidatesAsync());
        candidates.AddRange(await BuildAnniversaryCandidatesAsync(now));
        return candidates;
    }

    /// <summary>
    /// (a) Density gaps: contiguous runs of years strictly inside the archive's
    /// [earliest..latest] span whose event count is at most 20% of the median
    /// of the archive's non-zero years. Archives with fewer than 10 events skip
    /// gap detection - with that little data every year looks like a gap.
    /// </summary>
    private async Task<List<PromptCandidate>> BuildDensityGapCandidatesAsync()
    {
        var result = new List<PromptCandidate>();

        var density = await _analyticsService.GetTimelineDensityAsync(DensityGranularity.Yearly);
        if (density.Count == 0)
        {
            return result;
        }

        var totalEvents = density.Sum(p => p.EventCount);
        if (totalEvents < MinEventsForGapDetection)
        {
            return result;
        }

        var minYear = density.Min(p => p.Date.Year);
        var maxYear = density.Max(p => p.Date.Year);
        if (maxYear - minYear < 2)
        {
            return result; // No strict interior years to inspect.
        }

        // Yearly density only lists years that HAVE events; absent years count 0.
        var countsByYear = density.ToDictionary(p => p.Date.Year, p => p.EventCount);
        var median = Median(countsByYear.Values.Where(c => c > 0).ToList());
        var threshold = GapDensityFraction * median;

        int? runStart = null;
        for (var year = minYear + 1; year <= maxYear; year++)
        {
            var count = countsByYear.TryGetValue(year, out var c) ? c : 0;
            // Boundary years anchor the span; only strictly-interior years can
            // belong to a gap run.
            var isSparse = year < maxYear && count <= threshold;

            if (isSparse)
            {
                runStart ??= year;
                continue;
            }

            if (runStart.HasValue)
            {
                result.Add(CreateGapCandidate(runStart.Value, year - 1, minYear, maxYear));
                runStart = null;
            }
        }

        return result;
    }

    private static PromptCandidate CreateGapCandidate(int startYear, int endYear, int minYear, int maxYear)
    {
        var singleYear = startYear == endYear;
        var periodLabel = singleYear ? startYear.ToString() : $"{startYear}-{endYear}";

        var fallback = singleYear
            ? $"What was happening in your life in {startYear}? That year is almost empty on your timeline."
            : $"What were you doing between {startYear} and {endYear}? Those years are almost empty on your timeline.";

        var description =
            $"Gap type: quiet period on the timeline.\n" +
            $"Period: {startYear} to {endYear}.\n" +
            $"The archive spans {minYear}-{maxYear} but holds almost no memories from this period.";

        return new PromptCandidate(
            RecallPromptKind.DensityGap,
            EntityId: null,
            EntityName: periodLabel,
            AnchorStart: new DateTime(startYear, 1, 1),
            AnchorEnd: new DateTime(endYear, 12, 31),
            DedupeAnchorStart: new DateTime(startYear, 1, 1),
            LlmDescription: description,
            FallbackQuestion: fallback);
    }

    /// <summary>(b) Dangling people: linked to exactly one event.</summary>
    private async Task<List<PromptCandidate>> BuildDanglingPersonCandidatesAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Living people only: a merged-away tombstone can end up with a link
        // (broken-chain approval fallback), and prompting about it would
        // surface a retired name the survivor already covers.
        var dangling = await context.People
            .AsNoTracking()
            .Where(p => p.MergedIntoId == null && p.EventPeople.Count() == 1)
            .Select(p => new
            {
                p.PersonId,
                p.Name,
                EventTitle = p.EventPeople.Select(ep => ep.Event.Title).FirstOrDefault()
            })
            .OrderBy(p => p.Name)
            .Take(10)
            .ToListAsync();

        var result = new List<PromptCandidate>();
        foreach (var person in dangling)
        {
            var hasEventTitle = !string.IsNullOrWhiteSpace(person.EventTitle);
            var fallback = hasEventTitle
                ? $"{person.Name} appears only once in your archive - around \"{Clip(person.EventTitle!, 80)}\". How did you meet, and what other memories do you share?"
                : $"{person.Name} appears only once in your archive. What other memories do you share with them?";

            var description =
                $"Gap type: person mentioned only once.\n" +
                $"Person: {person.Name}.\n" +
                (hasEventTitle ? $"Their single linked memory: \"{person.EventTitle}\".\n" : string.Empty) +
                "The user has never recorded anything else about this person.";

            result.Add(new PromptCandidate(
                RecallPromptKind.DanglingPerson,
                EntityId: person.PersonId,
                EntityName: Clip(person.Name, MaxLabelLength),
                AnchorStart: null,
                AnchorEnd: null,
                DedupeAnchorStart: null,
                LlmDescription: description,
                FallbackQuestion: fallback));
        }

        return result;
    }

    /// <summary>
    /// (c) Era edges: an ongoing era (no EndDate) with no events in its last
    /// year, or an era containing zero events at all.
    /// </summary>
    private async Task<List<PromptCandidate>> BuildEraEdgeCandidatesAsync(DateTime now)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var cutoff = now.AddYears(-1);
        var eras = await context.Eras
            .AsNoTracking()
            .Select(e => new
            {
                e.EraId,
                e.Name,
                e.StartDate,
                e.EndDate,
                EventCount = e.Events.Count(),
                RecentCount = e.Events.Count(ev => ev.StartDate >= cutoff)
            })
            .OrderBy(e => e.StartDate)
            .Take(10)
            .ToListAsync();

        var result = new List<PromptCandidate>();
        foreach (var era in eras)
        {
            if (era.EventCount == 0)
            {
                result.Add(new PromptCandidate(
                    RecallPromptKind.EraEdge,
                    EntityId: era.EraId,
                    EntityName: Clip(era.Name, MaxLabelLength),
                    AnchorStart: era.StartDate,
                    AnchorEnd: era.EndDate,
                    DedupeAnchorStart: null,
                    LlmDescription:
                        $"Gap type: an era (life chapter) with no memories in it.\n" +
                        $"Era: \"{era.Name}\", starting {era.StartDate:yyyy-MM-dd}" +
                        (era.EndDate.HasValue ? $" and ending {era.EndDate.Value:yyyy-MM-dd}.\n" : " (still ongoing).\n") +
                        "The user defined this chapter of their life but never recorded anything from it.",
                    FallbackQuestion:
                        $"Your era \"{Clip(era.Name, 80)}\" has no memories in it yet. What happened during that chapter?"));
            }
            else if (era.EndDate == null && era.RecentCount == 0)
            {
                // Ongoing era gone quiet: nothing recorded in its last year.
                result.Add(new PromptCandidate(
                    RecallPromptKind.EraEdge,
                    EntityId: era.EraId,
                    EntityName: Clip(era.Name, MaxLabelLength),
                    AnchorStart: cutoff,
                    AnchorEnd: null,
                    DedupeAnchorStart: null,
                    LlmDescription:
                        $"Gap type: an ongoing era that has gone quiet.\n" +
                        $"Era: \"{era.Name}\", started {era.StartDate:yyyy-MM-dd}, still open, but nothing " +
                        "from the past year has been recorded in it.",
                    FallbackQuestion:
                        $"Your \"{Clip(era.Name, 80)}\" era is still open, but nothing from the past year is on the timeline. What has been happening lately?"));
            }
        }

        return result;
    }

    /// <summary>
    /// (d) Thin events: a title but a null/short (&lt;40 chars) description,
    /// preferring often-viewed events (the user clearly cares about them).
    /// </summary>
    private async Task<List<PromptCandidate>> BuildThinEventCandidatesAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var thin = await context.Events
            .AsNoTracking()
            .Where(e => e.Description == null || e.Description.Length < ThinDescriptionLength)
            .OrderByDescending(e => e.ViewCount)
            .ThenByDescending(e => e.StartDate)
            .Select(e => new { e.EventId, e.Title, e.StartDate, e.DatePrecision })
            .Take(10)
            .ToListAsync();

        var result = new List<PromptCandidate>();
        foreach (var evt in thin)
        {
            var dateText = DateDisplay.FormatPrecise(evt.StartDate, evt.DatePrecision);
            var clippedTitle = Clip(evt.Title, 80);

            result.Add(new PromptCandidate(
                RecallPromptKind.ThinEvent,
                EntityId: evt.EventId,
                EntityName: Clip(evt.Title, MaxLabelLength),
                AnchorStart: evt.StartDate,
                AnchorEnd: null,
                DedupeAnchorStart: null,
                LlmDescription:
                    $"Gap type: a memory with a title but almost no detail.\n" +
                    $"Memory: \"{evt.Title}\" ({dateText}).\n" +
                    "The user recorded that it happened but nothing about what it was like.",
                FallbackQuestion:
                    $"\"{clippedTitle}\" ({dateText}) has almost no detail. What do you remember about it - who was there, and how did it feel?"));
        }

        return result;
    }

    /// <summary>
    /// (e) Anniversary anchors: an event exactly 10/20/25 years ago this month,
    /// used as a hook to ask what ELSE was happening back then.
    /// </summary>
    private async Task<List<PromptCandidate>> BuildAnniversaryCandidatesAsync(DateTime now)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var result = new List<PromptCandidate>();
        foreach (var yearsAgo in AnniversaryYearsAgo)
        {
            var year = now.Year - yearsAgo;
            var month = now.Month;

            var evt = await context.Events
                .AsNoTracking()
                .Where(e => e.StartDate.Year == year && e.StartDate.Month == month)
                .OrderBy(e => e.StartDate)
                .Select(e => new { e.EventId, e.Title, e.StartDate })
                .FirstOrDefaultAsync();

            if (evt == null)
            {
                continue;
            }

            var clippedTitle = Clip(evt.Title, 80);
            result.Add(new PromptCandidate(
                RecallPromptKind.AnniversaryAnchor,
                EntityId: evt.EventId,
                EntityName: Clip(evt.Title, MaxLabelLength),
                AnchorStart: new DateTime(year, month, 1),
                AnchorEnd: new DateTime(year, month, DateTime.DaysInMonth(year, month)),
                DedupeAnchorStart: null,
                LlmDescription:
                    $"Gap type: a round-number anniversary as a memory hook.\n" +
                    $"{yearsAgo} years ago this month: \"{evt.Title}\" ({evt.StartDate:yyyy-MM-dd}).\n" +
                    "Ask what ELSE was happening in the user's life around that time.",
                FallbackQuestion:
                    $"{yearsAgo} years ago this month: \"{clippedTitle}\". What else was happening in your life around then?"));
        }

        return result;
    }

    /// <summary>
    /// Asks the configured LLM for the question text; falls back to the
    /// candidate's deterministic template on <see cref="ConfigurationException"/>
    /// (no key - the expected offline path) or any other LLM failure.
    /// </summary>
    private async Task<string> BuildQuestionAsync(PromptCandidate candidate, CancellationToken ct)
    {
        try
        {
            var response = await _llmService.CompleteAsync(QuestionSystemPrompt, candidate.LlmDescription, 300, ct);
            var question = CleanQuestion(response);
            if (!string.IsNullOrWhiteSpace(question))
            {
                return question;
            }

            _logger.LogWarning("LLM returned an empty recall question ({Kind}); using the template fallback.",
                candidate.Kind);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ConfigurationException)
        {
            // No API key: the deterministic templates ARE the product here.
            _logger.LogInformation("No LLM key configured; using the template recall question ({Kind}).",
                candidate.Kind);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM recall-question generation failed ({Kind}); using the template fallback.",
                candidate.Kind);
        }

        return candidate.FallbackQuestion;
    }

    #endregion

    #region Helpers

    private async Task<RecallPrompt> GetRequiredPromptAsync(string promptId)
    {
        var prompt = await _promptRepository.GetByIdAsync(promptId);
        if (prompt == null)
        {
            throw new InvalidOperationException($"Recall prompt not found: {promptId}");
        }

        return prompt;
    }

    /// <summary>
    /// Normalizes an LLM reply into a single-line question: strips code fences
    /// and surrounding quotes, collapses newlines, clips to 200 characters.
    /// </summary>
    private static string CleanQuestion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var text = raw.Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineBreak = text.IndexOf('\n');
            text = firstLineBreak >= 0 ? text[(firstLineBreak + 1)..] : string.Empty;
            var closingFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                text = text[..closingFence];
            }

            text = text.Trim();
        }

        text = text.Trim('"', '“', '”').Trim();
        text = string.Join(' ', text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (text.Length > 200)
        {
            text = text[..200].TrimEnd();
        }

        return text;
    }

    /// <summary>Trims and clips text to at most <paramref name="maxLength"/> chars (ellipsized).</summary>
    private static string Clip(string text, int maxLength)
    {
        text = text.Trim();
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..(maxLength - 1)].TrimEnd() + "…";
    }

    /// <summary>Median of a non-empty list (average of the two middles for even counts).</summary>
    private static double Median(List<int> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    #endregion
}
