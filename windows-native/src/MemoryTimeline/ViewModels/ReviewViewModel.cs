using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Core.Services;
using System.Collections.ObjectModel;

namespace MemoryTimeline.ViewModels;

/// <summary>
/// ViewModel for reviewing and managing pending events extracted from recordings.
/// </summary>
public partial class ReviewViewModel : ObservableObject
{
    private readonly IEventExtractionService _extractionService;
    private readonly ILogger<ReviewViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<PendingEventDto> _pendingEvents = new();

    [ObservableProperty]
    private PendingEventDto? _selectedEvent;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _pendingCount;

    [ObservableProperty]
    private int _approvedCount;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _showApprovedEvents;

    // Computed properties
    public bool HasPendingEvents => PendingEvents.Any();
    public bool IsEmpty => !HasPendingEvents;

    public ReviewViewModel(
        IEventExtractionService extractionService,
        ILogger<ReviewViewModel> logger)
    {
        _extractionService = extractionService;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the view by loading pending events.
    /// </summary>
    public async Task InitializeAsync()
    {
        await RefreshPendingEventsAsync();
    }

    /// <summary>
    /// Refreshes the list of pending events.
    /// </summary>
    [RelayCommand]
    private async Task RefreshPendingEventsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading pending events...";

            var events = await _extractionService.GetAllPendingEventsAsync();

            PendingEvents.Clear();
            foreach (var evt in events)
            {
                var dto = PendingEventDto.FromPendingEvent(evt);

                // Wire up commands
                dto.ApproveCommand = new RelayCommand(() => _ = ApproveEventAsync(dto));
                dto.RejectCommand = new RelayCommand(() => _ = RejectEventAsync(dto));
                dto.EditCommand = new RelayCommand(() => SelectEventForEdit(dto));

                PendingEvents.Add(dto);
            }

            await UpdateCountsAsync();

            OnPropertyChanged(nameof(HasPendingEvents));
            OnPropertyChanged(nameof(IsEmpty));

            StatusMessage = $"Loaded {PendingEvents.Count} pending events";
            _logger.LogInformation("Loaded {Count} pending events", PendingEvents.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing pending events");
            StatusMessage = "Error loading pending events";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Approves a pending event and creates it as a real event.
    /// </summary>
    [RelayCommand]
    private async Task ApproveEventAsync(PendingEventDto? eventDto)
    {
        if (eventDto == null) return;

        try
        {
            StatusMessage = $"Approving: {eventDto.Title}";

            var createdEvent = await _extractionService.ApprovePendingEventAsync(eventDto.PendingEventId);

            PendingEvents.Remove(eventDto);

            OnPropertyChanged(nameof(HasPendingEvents));
            OnPropertyChanged(nameof(IsEmpty));

            await UpdateCountsAsync();

            StatusMessage = $"Approved: {eventDto.Title}";
            _logger.LogInformation("Approved pending event: {EventId}", createdEvent.EventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving pending event");
            StatusMessage = $"Error approving event: {ex.Message}";
        }
    }

    /// <summary>
    /// Rejects and deletes a pending event.
    /// </summary>
    [RelayCommand]
    private async Task RejectEventAsync(PendingEventDto? eventDto)
    {
        if (eventDto == null) return;

        try
        {
            StatusMessage = $"Rejecting: {eventDto.Title}";

            await _extractionService.RejectPendingEventAsync(eventDto.PendingEventId);

            PendingEvents.Remove(eventDto);

            OnPropertyChanged(nameof(HasPendingEvents));
            OnPropertyChanged(nameof(IsEmpty));

            await UpdateCountsAsync();

            StatusMessage = $"Rejected: {eventDto.Title}";
            _logger.LogInformation("Rejected pending event: {EventId}", eventDto.PendingEventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting pending event");
            StatusMessage = $"Error rejecting event: {ex.Message}";
        }
    }

    /// <summary>
    /// Saves changes to a pending event.
    /// </summary>
    [RelayCommand]
    private async Task SaveChangesAsync()
    {
        if (SelectedEvent == null) return;

        try
        {
            StatusMessage = "Saving changes...";

            var pendingEvent = SelectedEvent.ToPendingEvent();
            await _extractionService.UpdatePendingEventAsync(pendingEvent);

            StatusMessage = "Changes saved";
            _logger.LogInformation("Updated pending event: {EventId}", pendingEvent.PendingEventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving changes");
            StatusMessage = $"Error saving changes: {ex.Message}";
        }
    }

    /// <summary>
    /// Approves all pending events.
    /// </summary>
    [RelayCommand]
    private async Task ApproveAllAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Approving all events...";

            var eventsToApprove = PendingEvents.ToList();
            int approvedCount = 0;

            foreach (var evt in eventsToApprove)
            {
                try
                {
                    await _extractionService.ApprovePendingEventAsync(evt.PendingEventId);
                    PendingEvents.Remove(evt);
                    approvedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to approve event: {Title}", evt.Title);
                }
            }

            OnPropertyChanged(nameof(HasPendingEvents));
            OnPropertyChanged(nameof(IsEmpty));

            await UpdateCountsAsync();

            StatusMessage = $"Approved {approvedCount} of {eventsToApprove.Count} events";
            _logger.LogInformation("Bulk approved {Count} events", approvedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in approve all");
            StatusMessage = "Error approving all events";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Rejects all pending events.
    /// </summary>
    [RelayCommand]
    private async Task RejectAllAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Rejecting all events...";

            var eventsToReject = PendingEvents.ToList();
            int rejectedCount = 0;

            foreach (var evt in eventsToReject)
            {
                try
                {
                    await _extractionService.RejectPendingEventAsync(evt.PendingEventId);
                    PendingEvents.Remove(evt);
                    rejectedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to reject event: {Title}", evt.Title);
                }
            }

            OnPropertyChanged(nameof(HasPendingEvents));
            OnPropertyChanged(nameof(IsEmpty));

            await UpdateCountsAsync();

            StatusMessage = $"Rejected {rejectedCount} of {eventsToReject.Count} events";
            _logger.LogInformation("Bulk rejected {Count} events", rejectedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in reject all");
            StatusMessage = "Error rejecting all events";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Selects an event for editing.
    /// </summary>
    private void SelectEventForEdit(PendingEventDto eventDto)
    {
        SelectedEvent = eventDto;
        StatusMessage = $"Editing: {eventDto.Title}";
    }

    /// <summary>
    /// Updates the event counts.
    /// </summary>
    private async Task UpdateCountsAsync()
    {
        try
        {
            PendingCount = await _extractionService.GetPendingEventCountAsync(false);
            ApprovedCount = await _extractionService.GetPendingEventCountAsync(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error updating counts");
        }
    }

    partial void OnFilterTextChanged(string value)
    {
        // Filter events based on search text
        // This would require implementing a filtered view
        StatusMessage = string.IsNullOrEmpty(value) ? "All events" : $"Filtered: {value}";
    }
}
