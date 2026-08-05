using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;

namespace MemoryTimeline.ViewModels;

/// <summary>
/// ViewModel for the Home page: On This Day memories, a (neglected-biased)
/// random memory, recent additions, the pending-review count, the weekly
/// digest, and upcoming anniversaries - plus the first-run empty state when
/// the archive has no events yet.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly IResurfacingService _resurfacingService;
    private readonly IEventService _eventService;
    private readonly IEventExtractionService _eventExtractionService;
    private readonly ISettingsService _settingsService;
    private readonly INavigationService _navigationService;
    private readonly ILogger<HomeViewModel> _logger;

    // Captured on construction (UI thread) so any background completion can
    // marshal observable mutations back onto the dispatcher.
    private readonly DispatcherQueue? _dispatcherQueue;

    [ObservableProperty]
    private string _headerDateLine = DateTime.Now.ToString("dddd, d MMMM yyyy");

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>True when the archive has zero events (first-run empty state).</summary>
    [ObservableProperty]
    private bool _isArchiveEmpty;

    /// <summary>True once the archive has content and the sections are populated.</summary>
    [ObservableProperty]
    private bool _hasContent;

    [ObservableProperty]
    private ObservableCollection<HomeMemoryCardDto> _onThisDayMemories = new();

    [ObservableProperty]
    private bool _hasOnThisDay;

    [ObservableProperty]
    private HomeMemoryCardDto? _randomMemory;

    [ObservableProperty]
    private ObservableCollection<HomeMemoryCardDto> _recentEvents = new();

    [ObservableProperty]
    private bool _hasRecentEvents;

    [ObservableProperty]
    private int _pendingReviewCount;

    [ObservableProperty]
    private bool _hasPendingReview;

    [ObservableProperty]
    private string _pendingReviewText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<HomeMemoryCardDto> _weeklyDigestMemories = new();

    /// <summary>True when the digest is enabled and has at least one memory.</summary>
    [ObservableProperty]
    private bool _showWeeklyDigest;

    [ObservableProperty]
    private ObservableCollection<HomeAnniversaryDto> _upcomingAnniversaries = new();

    [ObservableProperty]
    private bool _hasUpcomingAnniversaries;

    public HomeViewModel(
        IResurfacingService resurfacingService,
        IEventService eventService,
        IEventExtractionService eventExtractionService,
        ISettingsService settingsService,
        INavigationService navigationService,
        ILogger<HomeViewModel> logger)
    {
        _resurfacingService = resurfacingService;
        _eventService = eventService;
        _eventExtractionService = eventExtractionService;
        _settingsService = settingsService;
        _navigationService = navigationService;
        _logger = logger;

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>
    /// Marshals the supplied action onto the UI dispatcher thread (background
    /// work must never mutate bound state directly).
    /// </summary>
    private void RunOnUi(Action action)
    {
        if (_dispatcherQueue == null || _dispatcherQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => action());
        }
    }

    /// <summary>
    /// Loads every Home section. Called from HomePage.OnNavigatedTo; safe to
    /// call again (each navigation refreshes the page).
    /// </summary>
    public async Task InitializeAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            RunOnUi(() => HeaderDateLine = DateTime.Now.ToString("dddd, d MMMM yyyy"));

            var totalEvents = await _eventService.GetTotalEventCountAsync();
            if (totalEvents == 0)
            {
                // First-run empty state: nothing to resurface yet, guide setup.
                RunOnUi(() =>
                {
                    IsArchiveEmpty = true;
                    HasContent = false;
                });
                return;
            }

            var onThisDay = await _resurfacingService.GetOnThisDayAsync();
            var random = await _resurfacingService.GetRandomAsync(RandomBias.Neglected);
            var recent = (await _eventService.GetRecentEventsAsync(5)).ToList();
            var pendingCount = await _eventExtractionService.GetPendingEventCountAsync(false);
            var anniversaries = await _resurfacingService.GetUpcomingAnniversariesAsync();

            var digestEnabledSetting = await _settingsService.GetSettingAsync<string>(SettingKeys.WeeklyDigestEnabled, "true");
            var digestEnabled = !string.Equals(digestEnabledSetting?.Trim(), "false", StringComparison.OrdinalIgnoreCase);
            var digest = digestEnabled ? await _resurfacingService.GetWeeklyDigestAsync() : null;

            RunOnUi(() =>
            {
                IsArchiveEmpty = false;

                OnThisDayMemories.Clear();
                foreach (var memory in onThisDay)
                {
                    OnThisDayMemories.Add(CreateCard(memory, YearsAgoText(memory.YearsAgo) + " today"));
                }
                HasOnThisDay = OnThisDayMemories.Count > 0;

                RandomMemory = random == null ? null : CreateCard(random, YearsAgoText(random.YearsAgo));

                RecentEvents.Clear();
                foreach (var evt in recent)
                {
                    var card = new HomeMemoryCardDto
                    {
                        EventId = evt.EventId,
                        Title = evt.Title,
                        Subtitle = $"Added {evt.CreatedAt:d MMM yyyy}",
                        DisplayDate = DateDisplay.FormatPrecise(evt.StartDate, evt.DatePrecision)
                    };
                    card.OpenCommand = new RelayCommand(() => _ = OpenMemoryAsync(card.EventId));
                    RecentEvents.Add(card);
                }
                HasRecentEvents = RecentEvents.Count > 0;

                PendingReviewCount = pendingCount;
                HasPendingReview = pendingCount > 0;
                PendingReviewText = pendingCount == 1
                    ? "1 extracted event is waiting for review"
                    : $"{pendingCount} extracted events are waiting for review";

                WeeklyDigestMemories.Clear();
                if (digest != null)
                {
                    foreach (var memory in digest.Memories)
                    {
                        WeeklyDigestMemories.Add(CreateCard(memory, YearsAgoText(memory.YearsAgo)));
                    }
                }
                ShowWeeklyDigest = digestEnabled && WeeklyDigestMemories.Count > 0;

                UpcomingAnniversaries.Clear();
                foreach (var anniversary in anniversaries)
                {
                    UpcomingAnniversaries.Add(CreateAnniversaryDto(anniversary));
                }
                HasUpcomingAnniversaries = UpcomingAnniversaries.Count > 0;

                HasContent = true;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the Home page");
            RunOnUi(() => ErrorMessage = $"Could not load the home page: {ex.Message}");
        }
        finally
        {
            RunOnUi(() => IsLoading = false);
        }
    }

    /// <summary>
    /// Replaces the random memory card with a fresh neglected-biased pick.
    /// </summary>
    [RelayCommand]
    private async Task ShowAnotherRandomAsync()
    {
        try
        {
            var random = await _resurfacingService.GetRandomAsync(RandomBias.Neglected);
            RunOnUi(() => RandomMemory = random == null ? null : CreateCard(random, YearsAgoText(random.YearsAgo)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pick another random memory");
            RunOnUi(() => ErrorMessage = $"Could not pick another memory: {ex.Message}");
        }
    }

    /// <summary>Empty-state CTA: start recording on the Queue page.</summary>
    [RelayCommand]
    private void RecordFirstMemory()
    {
        _navigationService.NavigateTo("Queue");
    }

    /// <summary>Empty-state CTA: open the Queue page's paste-text dialog.</summary>
    [RelayCommand]
    private void PasteText()
    {
        // QueuePage.OnNavigatedTo opens the paste-capture dialog for the
        // "paste" parameter (same path as the Ctrl+Shift+V accelerator).
        _navigationService.NavigateTo("Queue", "paste");
    }

    /// <summary>Empty-state CTA: open Settings (Anthropic key setup).</summary>
    [RelayCommand]
    private void OpenSettings()
    {
        _navigationService.NavigateTo("Settings");
    }

    /// <summary>Pending-review card: jump to the Review page.</summary>
    [RelayCommand]
    private void ReviewNow()
    {
        _navigationService.NavigateTo("Review");
    }

    /// <summary>
    /// Opens a memory on the timeline, recording the view first so the
    /// neglected-random bias learns what has been revisited. A failed view
    /// record is logged and surfaced but never blocks the navigation - opening
    /// the memory is what the user asked for.
    /// </summary>
    private async Task OpenMemoryAsync(string eventId)
    {
        try
        {
            await _resurfacingService.RecordViewAsync(eventId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record the view for event {EventId}", eventId);
            RunOnUi(() => ErrorMessage = $"Could not record the view: {ex.Message}");
        }

        RunOnUi(() => _navigationService.NavigateTo("Timeline", eventId));
    }

    private HomeMemoryCardDto CreateCard(ResurfacedMemory memory, string subtitle)
    {
        var card = new HomeMemoryCardDto
        {
            EventId = memory.EventId,
            Title = memory.Title,
            Subtitle = subtitle,
            DisplayDate = memory.DisplayDate
        };
        card.OpenCommand = new RelayCommand(() => _ = OpenMemoryAsync(card.EventId));
        return card;
    }

    private static HomeAnniversaryDto CreateAnniversaryDto(UpcomingAnniversary anniversary)
    {
        var yearsText = anniversary.YearsSince switch
        {
            0 => "coming up",
            1 => "1 year",
            _ => $"{anniversary.YearsSince} years"
        };

        return new HomeAnniversaryDto
        {
            Title = anniversary.Title,
            DateText = anniversary.Date.ToString("ddd, d MMM"),
            YearsText = yearsText
        };
    }

    private static string YearsAgoText(int yearsAgo)
    {
        return yearsAgo switch
        {
            0 => "This year",
            1 => "1 year ago",
            _ => $"{yearsAgo} years ago"
        };
    }
}

/// <summary>
/// A clickable memory card on the Home page. The command is set by the
/// ViewModel before the card is added to a collection (same pattern as the
/// Ask page's citation chips).
/// </summary>
public class HomeMemoryCardDto
{
    public string EventId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>Context line, e.g. "5 years ago today" or "Added 3 Aug 2026".</summary>
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>Precision-honest date text, e.g. "14 Mar 2015" or "Summer 1998".</summary>
    public string DisplayDate { get; set; } = string.Empty;

    /// <summary>Set by the ViewModel before the card is exposed to the view.</summary>
    public IRelayCommand? OpenCommand { get; set; }
}

/// <summary>
/// One "Coming up" row: an upcoming milestone or round-number event anniversary.
/// </summary>
public class HomeAnniversaryDto
{
    public string Title { get; set; } = string.Empty;

    /// <summary>The upcoming occurrence, e.g. "Sat, 15 Aug".</summary>
    public string DateText { get; set; } = string.Empty;

    /// <summary>How many years it marks, e.g. "5 years" (or "coming up").</summary>
    public string YearsText { get; set; } = string.Empty;
}
