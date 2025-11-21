namespace MemoryTimeline.Core.DTOs;

/// <summary>
/// DTO for audio recording information.
/// </summary>
public class AudioRecordingDto
{
    public string QueueId { get; set; } = string.Empty;
    public string AudioFilePath { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double? DurationSeconds { get; set; }
    public long? FileSizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? TranscriptionText { get; set; }
    public int PendingEventCount { get; set; }

    // Display properties
    public string DurationDisplay => DurationSeconds.HasValue
        ? TimeSpan.FromSeconds(DurationSeconds.Value).ToString(@"mm\:ss")
        : "Unknown";

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

    public string StatusIcon => Status switch
    {
        "pending" => "\uE81E", // Clock
        "processing" => "\uE895", // Sync
        "completed" => "\uE73E", // CheckMark
        "failed" => "\uE783", // Error
        _ => "\uE946" // Help
    };

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
