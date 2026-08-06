using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository interface for <see cref="RecallPrompt"/> rows.
/// </summary>
public interface IRecallPromptRepository
{
    /// <summary>
    /// Active prompts in rotation as of <paramref name="now"/>: status Active
    /// AND (snoozed_until is null OR snoozed_until &lt;= now), oldest first.
    /// The caller supplies 'now' so snooze expiry is testable.
    /// </summary>
    Task<List<RecallPrompt>> GetActiveAsync(DateTime now);

    /// <summary>
    /// Whether ANY prompt row (Active, Dismissed, or Answered) already exists
    /// for this gap. Dismissed/Answered rows count on purpose: once asked, the
    /// app never asks the same question again. When
    /// <paramref name="anchorStart"/> is null the anchor is ignored (entity-
    /// keyed kinds); when set it must match (period-keyed kinds).
    /// </summary>
    Task<bool> ExistsForAsync(RecallPromptKind kind, string? entityId, DateTime? anchorStart);

    Task<RecallPrompt> AddAsync(RecallPrompt prompt);

    Task UpdateAsync(RecallPrompt prompt);

    Task<RecallPrompt?> GetByIdAsync(string promptId);
}
