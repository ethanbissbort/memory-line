using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Guided recall: turns gaps in the archive (quiet years, one-mention people,
/// trailing/empty eras, detail-less events, round anniversaries) into specific,
/// answerable questions, and routes the answers into the normal capture queue
/// so they flow through extraction and review like any other memory.
/// </summary>
public interface IRecallPromptService
{
    /// <summary>
    /// Returns up to <paramref name="count"/> active prompts (oldest first),
    /// topping the pool up by generating new ones from archive gaps when it is
    /// below <paramref name="count"/>. Generation is capped per call and
    /// deduped against every previously created prompt - including Dismissed
    /// and Answered rows - so the app never re-asks a question. Question text
    /// comes from the configured LLM when available and from solid
    /// deterministic per-kind templates when it is not (no-key path).
    /// </summary>
    Task<List<RecallPrompt>> GetPromptsAsync(int count = 5, CancellationToken ct = default);

    /// <summary>
    /// Skips a prompt. <see cref="DismissReason.NotInterested"/> and
    /// <see cref="DismissReason.AlreadyCovered"/> are PERMANENT: the row is
    /// marked Dismissed (and kept, so the dedupe never regenerates it).
    /// <see cref="DismissReason.Later"/> is a snooze: the prompt stays Active
    /// with snoozed_until set 14 days out and returns to the rotation
    /// automatically after that.
    /// </summary>
    Task DismissAsync(string promptId, DismissReason reason);

    /// <summary>
    /// Typed-answer path: enqueues <paramref name="answerText"/> as a Text
    /// queue source labeled with the prompt's question (clipped to 200 chars),
    /// links the new queue row to the prompt, and marks the prompt Answered.
    /// Throws <see cref="ArgumentException"/> for empty text (same contract as
    /// <see cref="IQueueService.EnqueueTextAsync"/>).
    /// </summary>
    /// <returns>The new queue item's id.</returns>
    Task<string> StartAnsweringAsync(string promptId, string answerText);

    /// <summary>
    /// Recorded-answer path: links an externally created queue row (e.g. a
    /// just-stopped recording) to the prompt and marks the prompt Answered.
    /// Stamps the queue row's SourceLabel with the prompt's question (clipped
    /// to 200 chars) when the row does not already carry a label.
    /// </summary>
    Task MarkAnsweredAsync(string promptId, string queueId);
}
