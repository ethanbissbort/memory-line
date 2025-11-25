using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace MemoryTimeline.Core.DTOs;

/// <summary>
/// DTO for audio recording information.
/// </summary>
public partial class AudioRecordingDto : ObservableObject
{
    [ObservableProperty]
    private string _queueId = string.Empty;

    [ObservableProperty]
    private string _audioFilePath = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private double? _durationSeconds;

    [ObservableProperty]
    private long? _fileSizeBytes;

    [ObservableProperty]
    private DateTime _createdAt;

    [ObservableProperty]
    private DateTime? _processedAt;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _transcriptionText;

    [ObservableProperty]
    private int _pendingEventCount;

    [ObservableProperty]
    private double _progress;

    // Commands for UI actions
    public IRelayCommand? PlayCommand { get; set; }
    public IRelayCommand? RetryCommand { get; set; }
    public IRelayCommand? RemoveCommand { get; set; }

    // Display properties
    public string DurationDisplay => DurationSeconds.HasValue
        ? TimeSpan.FromSeconds(DurationSeconds.Value).ToString(@"mm\:ss")
        : "Unknown";

    public string DurationText => DurationDisplay;

    public string FileSizeDisplay => FileSizeBytes.HasValue
        ? FormatFileSize(FileSizeBytes.Value)
        : "Unknown";

    public string StatusDisplay => Status switch
    {
        "pending" => "Pending",
        "processing" => "Processing...",
        "completed" => "Completed",
        "failed" => "Failed",
        _ => Status
    };

    public string StatusText => StatusDisplay;

    public string StatusIcon => Status switch
    {
        "pending" => "\uE81E", // Clock
        "processing" => "\uE895", // Sync
        "completed" => "\uE73E", // CheckMark
        "failed" => "\uE783", // Error
        _ => "\uE946" // Help
    };

    public string StatusGlyph => StatusIcon;

    public SolidColorBrush StatusColor => Status switch
    {
        "pending" => new SolidColorBrush(Colors.Orange),
        "processing" => new SolidColorBrush(Colors.DodgerBlue),
        "completed" => new SolidColorBrush(Colors.Green),
        "failed" => new SolidColorBrush(Colors.Red),
        _ => new SolidColorBrush(Colors.Gray)
    };

    public Visibility IsProcessing => Status == "processing" ? Visibility.Visible : Visibility.Collapsed;

    public Visibility HasError => !string.IsNullOrEmpty(ErrorMessage) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CanPlay => Status == "completed" || Status == "failed" ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CanRetry => Status == "failed" ? Visibility.Visible : Visibility.Collapsed;

    partial void OnStatusChanged(string value)
    {
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanRetry));
    }

    partial void OnDurationSecondsChanged(double? value)
    {
        OnPropertyChanged(nameof(DurationDisplay));
        OnPropertyChanged(nameof(DurationText));
    }

    partial void OnFileSizeBytesChanged(long? value)
    {
        OnPropertyChanged(nameof(FileSizeDisplay));
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    public static AudioRecordingDto FromRecordingQueue(Data.Models.RecordingQueue queue)
    {
        return new AudioRecordingDto
        {
            QueueId = queue.QueueId,
            AudioFilePath = queue.AudioFilePath,
            Status = queue.Status,
            DurationSeconds = queue.DurationSeconds,
            FileSizeBytes = queue.FileSizeBytes,
            CreatedAt = queue.CreatedAt,
            ProcessedAt = queue.ProcessedAt,
            ErrorMessage = queue.ErrorMessage,
            PendingEventCount = queue.PendingEvents.Count
        };
    }
}

/// <summary>
/// Audio device information DTO.
/// </summary>
public class AudioDeviceDto
{
    public string DeviceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsEnabled { get; set; }
}

/// <summary>
/// Audio recording settings.
/// </summary>
public class AudioRecordingSettings
{
    public int SampleRate { get; set; } = 16000; // 16kHz
    public int BitsPerSample { get; set; } = 16;
    public int Channels { get; set; } = 1; // Mono
    public string? DeviceId { get; set; }
    public string AudioFormat { get; set; } = "wav";
}
