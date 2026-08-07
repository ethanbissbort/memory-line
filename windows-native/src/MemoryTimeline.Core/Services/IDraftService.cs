using CommunityToolkit.Mvvm.Messaging;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Data.Models;
using MemoryTimeline.Data.Repositories;
using Microsoft.Extensions.Logging;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for saving, listing, and resuming editor drafts.
/// </summary>
public interface IDraftService
{
    /// <summary>
    /// Upserts a draft. When <paramref name="draftId"/> is null or unknown a
    /// new draft (with a new id) is created; otherwise the existing draft's
    /// title and payload are updated.
    /// </summary>
    Task<DraftDto> SaveDraftAsync(string draftType, string title, string payloadJson, string? draftId = null);

    /// <summary>
    /// Gets drafts (optionally filtered by type), most recently updated first.
    /// </summary>
    Task<List<DraftDto>> GetDraftsAsync(string? draftType = null);

    /// <summary>Gets one draft by id, or null when it does not exist.</summary>
    Task<DraftDto?> GetDraftAsync(string draftId);

    /// <summary>Deletes a draft; missing ids are ignored.</summary>
    Task DeleteDraftAsync(string draftId);

    /// <summary>Gets the total number of drafts.</summary>
    Task<int> GetDraftCountAsync();
}

/// <summary>
/// Draft service implementation over <see cref="IDraftRepository"/>. Publishes
/// <see cref="DraftsChangedMessage"/> after successful saves and deletes.
/// </summary>
public class DraftService : IDraftService
{
    private readonly IDraftRepository _draftRepository;
    private readonly ILogger<DraftService> _logger;

    public DraftService(IDraftRepository draftRepository, ILogger<DraftService> logger)
    {
        _draftRepository = draftRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DraftDto> SaveDraftAsync(
        string draftType,
        string title,
        string payloadJson,
        string? draftId = null)
    {
        try
        {
            var effectiveTitle = NormalizeTitle(title, draftType);
            var effectivePayload = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson;

            Draft? existing = null;
            if (!string.IsNullOrWhiteSpace(draftId))
            {
                existing = await _draftRepository.GetByIdAsync(draftId);
            }

            Draft entity;
            if (existing == null)
            {
                entity = new Draft
                {
                    DraftId = Guid.NewGuid().ToString(),
                    DraftType = draftType,
                    Title = effectiveTitle,
                    PayloadJson = effectivePayload,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _draftRepository.AddAsync(entity);
                _logger.LogInformation(
                    "Draft created: {DraftId} ({DraftType}) - {Title}",
                    entity.DraftId, entity.DraftType, entity.Title);
            }
            else
            {
                existing.Title = effectiveTitle;
                existing.PayloadJson = effectivePayload;
                existing.UpdatedAt = DateTime.UtcNow;
                await _draftRepository.UpdateAsync(existing);
                entity = existing;
                _logger.LogInformation(
                    "Draft updated: {DraftId} ({DraftType}) - {Title}",
                    entity.DraftId, entity.DraftType, entity.Title);
            }

            SendDraftsChanged();
            return DraftDto.FromDraft(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving draft ({DraftType}): {Title}", draftType, title);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<DraftDto>> GetDraftsAsync(string? draftType = null)
    {
        try
        {
            var drafts = draftType == null
                ? await _draftRepository.GetAllOrderedAsync()
                : await _draftRepository.GetByTypeAsync(draftType);

            return drafts.Select(DraftDto.FromDraft).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting drafts (type: {DraftType})", draftType ?? "all");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<DraftDto?> GetDraftAsync(string draftId)
    {
        try
        {
            var draft = await _draftRepository.GetByIdAsync(draftId);
            return draft == null ? null : DraftDto.FromDraft(draft);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting draft: {DraftId}", draftId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteDraftAsync(string draftId)
    {
        try
        {
            var draft = await _draftRepository.GetByIdAsync(draftId);
            if (draft == null)
            {
                _logger.LogDebug("Draft not found for delete, ignoring: {DraftId}", draftId);
                return;
            }

            await _draftRepository.DeleteAsync(draft);
            _logger.LogInformation("Draft deleted: {DraftId}", draftId);
            SendDraftsChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting draft: {DraftId}", draftId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> GetDraftCountAsync()
    {
        try
        {
            return await _draftRepository.CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting draft count");
            throw;
        }
    }

    /// <summary>
    /// Replaces an empty title with "Untitled event/era/person" (falls back to
    /// "Untitled draft" for unknown types).
    /// </summary>
    private static string NormalizeTitle(string title, string draftType)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title.Trim();
        }

        return draftType switch
        {
            DraftTypes.Event => "Untitled event",
            DraftTypes.Era => "Untitled era",
            DraftTypes.Person => "Untitled person",
            _ => "Untitled draft"
        };
    }

    /// <summary>
    /// Publishes <see cref="DraftsChangedMessage"/>; a subscriber failure must
    /// not turn a successful write into an error.
    /// </summary>
    private void SendDraftsChanged()
    {
        try
        {
            WeakReferenceMessenger.Default.Send(new DraftsChangedMessage());
        }
        catch (Exception messengerEx)
        {
            _logger.LogWarning(messengerEx, "Error publishing DraftsChangedMessage");
        }
    }
}
