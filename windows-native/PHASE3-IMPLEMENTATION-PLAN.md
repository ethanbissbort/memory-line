# Phase 3: Audio Recording & Processing - Implementation Plan

**Status:** 🔵 **PLANNING**
**Estimated Duration:** 4 weeks
**Prerequisites:** Phase 1 ✅ Complete | Phase 2 ✅ Complete
**Branch:** `claude/windows-migration-phase-0-01FPaPzX9vsqV72TgXBRsLmA`

---

## Executive Summary

Phase 3 implements audio capture, queue management, and speech-to-text processing. This phase enables users to record audio entries, process them through STT engines (local or cloud), and manage the processing queue.

### Key Objectives

1. **Audio Recording** - Windows MediaCapture API for recording
2. **Queue Management** - Background processing with status tracking
3. **Speech-to-Text** - Multi-engine support (Windows SR, Whisper, Azure)

### Success Criteria

- ✅ Record audio at 16kHz, 16-bit WAV format
- ✅ Queue system with background processing
- ✅ Local STT with Windows Speech Recognition
- ✅ Cloud STT with OpenAI Whisper API
- ✅ Error handling and retry logic
- ✅ Audio playback and waveform visualization

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                      QueuePage (XAML)                    │
│  ┌────────────────┐  ┌──────────────┐  ┌─────────────┐ │
│  │ Record Button  │  │ Queue List   │  │ Status Info │ │
│  │ Stop Button    │  │ (ListView)   │  │ Progress    │ │
│  └────────────────┘  └──────────────┘  └─────────────┘ │
└─────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────┐
│                   QueueViewModel                         │
│  - ObservableCollection<QueueItemDto>                   │
│  - RecordCommand, StopCommand                            │
│  - PlayCommand, RemoveCommand                            │
│  - ProcessCommand                                        │
└─────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────┐
│              Services (MemoryTimeline.Core)              │
│  ┌──────────────────┐  ┌──────────────┐  ┌───────────┐ │
│  │ IAudioRecording  │  │ IQueueService│  │ ISTTService│ │
│  │ Service          │  │              │  │            │ │
│  └──────────────────┘  └──────────────┘  └───────────┘ │
└─────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────┐
│           Data Layer (MemoryTimeline.Data)               │
│  - RecordingQueue entity                                 │
│  - RecordingQueueRepository                              │
│  - QueueStatus enum (Pending, Processing, Completed)     │
└─────────────────────────────────────────────────────────┘
```

---

## Phase 3.1: Audio Recording (Week 1)

### Objectives
- Implement audio capture using Windows MediaCapture
- Add audio playback functionality
- Create file management system

### Tasks

#### 3.1.1 Create IAudioRecordingService Interface

**File:** `windows-native/src/MemoryTimeline.Core/Services/IAudioRecordingService.cs`

```csharp
public interface IAudioRecordingService
{
    // Recording control
    Task<bool> StartRecordingAsync();
    Task StopRecordingAsync();
    Task PauseRecordingAsync();
    Task ResumeRecordingAsync();
    void CancelRecording();

    // Recording state
    bool IsRecording { get; }
    bool IsPaused { get; }
    TimeSpan RecordingDuration { get; }

    // Events
    event EventHandler<RecordingStateChangedEventArgs> RecordingStateChanged;
    event EventHandler<AudioLevelChangedEventArgs> AudioLevelChanged;

    // Device management
    Task<List<AudioDeviceInfo>> GetAudioDevicesAsync();
    Task SetAudioDeviceAsync(string deviceId);

    // File info
    string GetLastRecordingPath();
}
```

#### 3.1.2 Implement AudioRecordingService

**File:** `windows-native/src/MemoryTimeline.Core/Services/AudioRecordingService.cs`

**Key Components:**
- `MediaCapture` for recording
- `StorageFile` for saving to ApplicationData
- Audio format: 16kHz, 16-bit, mono WAV
- Automatic file naming: `recording_YYYYMMDD_HHMMSS.wav`
- Real-time audio level monitoring
- Pause/resume support

**Dependencies:**
```xml
<PackageReference Include="Microsoft.Windows.SDK.Contracts" Version="10.0.22621.2" />
```

#### 3.1.3 Create IAudioPlaybackService Interface

**File:** `windows-native/src/MemoryTimeline.Core/Services/IAudioPlaybackService.cs`

```csharp
public interface IAudioPlaybackService
{
    Task PlayAsync(string filePath);
    Task PauseAsync();
    Task ResumeAsync();
    Task StopAsync();
    Task SeekAsync(TimeSpan position);

    bool IsPlaying { get; }
    bool IsPaused { get; }
    TimeSpan Duration { get; }
    TimeSpan Position { get; }

    event EventHandler<PlaybackStateChangedEventArgs> PlaybackStateChanged;
    event EventHandler<TimeSpan> PositionChanged;
}
```

#### 3.1.4 Implement AudioPlaybackService

**File:** `windows-native/src/MemoryTimeline.Core/Services/AudioPlaybackService.cs`

**Key Components:**
- `MediaPlayer` for playback
- Progress tracking
- Playback state management

#### 3.1.5 Create Supporting Models

**RecordingState.cs:**
```csharp
public enum RecordingState
{
    Idle,
    Recording,
    Paused,
    Stopped
}
```

**AudioDeviceInfo.cs:**
```csharp
public class AudioDeviceInfo
{
    public string DeviceId { get; set; }
    public string DeviceName { get; set; }
    public bool IsDefault { get; set; }
}
```

#### 3.1.6 Update DTOs

**File:** `windows-native/src/MemoryTimeline.Core/DTOs/QueueItemDto.cs`

```csharp
public class QueueItemDto
{
    public string QueueId { get; set; }
    public string AudioFilePath { get; set; }
    public string Transcript { get; set; }
    public QueueStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public TimeSpan? AudioDuration { get; set; }
    public long? AudioFileSize { get; set; }
    public string ErrorMessage { get; set; }
    public int RetryCount { get; set; }

    public static QueueItemDto FromRecordingQueue(RecordingQueue queue) { ... }
}
```

### Testing Strategy

**Unit Tests:**
- [ ] AudioRecordingService_StartRecording_CreatesFile
- [ ] AudioRecordingService_PauseResume_WorksCorrectly
- [ ] AudioPlaybackService_Play_LoadsAndPlaysFile
- [ ] AudioPlaybackService_Seek_UpdatesPosition

---

## Phase 3.2: Queue System (Week 2)

### Objectives
- Create queue management service
- Implement background processing
- Add retry logic with exponential backoff

### Tasks

#### 3.2.1 Create IQueueService Interface

**File:** `windows-native/src/MemoryTimeline.Core/Services/IQueueService.cs`

```csharp
public interface IQueueService
{
    // Queue operations
    Task<QueueItemDto> AddToQueueAsync(string audioFilePath);
    Task<QueueItemDto> GetQueueItemAsync(string queueId);
    Task<List<QueueItemDto>> GetAllQueueItemsAsync();
    Task<List<QueueItemDto>> GetQueueItemsByStatusAsync(QueueStatus status);
    Task RemoveFromQueueAsync(string queueId);
    Task ClearCompletedAsync();

    // Processing
    Task StartProcessingAsync();
    Task StopProcessingAsync();
    Task ProcessItemAsync(string queueId);
    Task<int> GetPendingCountAsync();

    // Events
    event EventHandler<QueueItemProcessedEventArgs> ItemProcessed;
    event EventHandler<QueueItemFailedEventArgs> ItemFailed;
}
```

#### 3.2.2 Implement QueueService

**File:** `windows-native/src/MemoryTimeline.Core/Services/QueueService.cs`

**Key Components:**
- Background Task for processing
- SemaphoreSlim for concurrent processing control
- Retry logic with exponential backoff (max 3 retries)
- Error handling and logging
- Status updates

**Processing Algorithm:**
```csharp
while (isProcessing)
{
    var pendingItems = await GetPendingItemsAsync();

    foreach (var item in pendingItems)
    {
        try
        {
            await ProcessItemAsync(item);
        }
        catch (Exception ex)
        {
            await HandleProcessingErrorAsync(item, ex);
        }
    }

    await Task.Delay(5000); // Check every 5 seconds
}
```

**Retry Logic:**
```csharp
var retryDelay = TimeSpan.FromSeconds(Math.Pow(2, retryCount)); // 2, 4, 8 seconds
await Task.Delay(retryDelay);
```

#### 3.2.3 Update QueuePage XAML

**File:** `windows-native/src/MemoryTimeline/Views/QueuePage.xaml`

**UI Layout:**
```xml
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/> <!-- Controls -->
        <RowDefinition Height="*"/>     <!-- Queue List -->
        <RowDefinition Height="Auto"/> <!-- Status Bar -->
    </Grid.RowDefinitions>

    <!-- Recording Controls -->
    <StackPanel Grid.Row="0" Orientation="Horizontal">
        <Button Command="{x:Bind ViewModel.StartRecordingCommand}">
            <StackPanel Orientation="Horizontal">
                <FontIcon Glyph="&#xE7C8;"/>
                <TextBlock Text="Record"/>
            </StackPanel>
        </Button>
        <Button Command="{x:Bind ViewModel.StopRecordingCommand}">
            <FontIcon Glyph="&#xE71A;"/>
        </Button>
        <TextBlock Text="{x:Bind ViewModel.RecordingDuration}"/>
        <ProgressBar IsIndeterminate="True"
                     Visibility="{x:Bind ViewModel.IsRecording}"/>
    </StackPanel>

    <!-- Queue ListView -->
    <ListView Grid.Row="1" ItemsSource="{x:Bind ViewModel.QueueItems}">
        <ListView.ItemTemplate>
            <DataTemplate x:DataType="dtos:QueueItemDto">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>

                    <!-- Status Icon -->
                    <FontIcon Grid.Column="0"
                              Glyph="{x:Bind StatusGlyph}"/>

                    <!-- Info -->
                    <StackPanel Grid.Column="1">
                        <TextBlock Text="{x:Bind CreatedAt}"/>
                        <TextBlock Text="{x:Bind StatusText}"/>
                        <ProgressBar Value="{x:Bind Progress}"
                                     Visibility="{x:Bind IsProcessing}"/>
                    </StackPanel>

                    <!-- Actions -->
                    <StackPanel Grid.Column="2" Orientation="Horizontal">
                        <Button Command="{x:Bind PlayCommand}">
                            <FontIcon Glyph="&#xE768;"/>
                        </Button>
                        <Button Command="{x:Bind RemoveCommand}">
                            <FontIcon Glyph="&#xE74D;"/>
                        </Button>
                    </StackPanel>
                </Grid>
            </DataTemplate>
        </ListView.ItemTemplate>
    </ListView>

    <!-- Status Bar -->
    <Grid Grid.Row="2">
        <TextBlock Text="{x:Bind ViewModel.StatusMessage}"/>
        <TextBlock Text="{x:Bind ViewModel.PendingCount}"
                   HorizontalAlignment="Right"/>
    </Grid>
</Grid>
```

#### 3.2.4 Implement QueueViewModel

**File:** `windows-native/src/MemoryTimeline/ViewModels/QueueViewModel.cs`

**Properties:**
- `ObservableCollection<QueueItemDto> QueueItems`
- `bool IsRecording`
- `string RecordingDuration`
- `string StatusMessage`
- `int PendingCount`

**Commands:**
- `StartRecordingCommand`
- `StopRecordingCommand`
- `PlayCommand`
- `RemoveCommand`
- `ProcessCommand`
- `ClearCompletedCommand`

**Service Integration:**
```csharp
private readonly IAudioRecordingService _audioService;
private readonly IQueueService _queueService;
private readonly IAudioPlaybackService _playbackService;
private readonly ILogger<QueueViewModel> _logger;
```

### Testing Strategy

**Unit Tests:**
- [ ] QueueService_AddToQueue_CreatesQueueItem
- [ ] QueueService_ProcessItem_UpdatesStatus
- [ ] QueueService_RetryLogic_ExponentialBackoff
- [ ] QueueViewModel_StartRecording_UpdatesUI

---

## Phase 3.3: Speech-to-Text (Weeks 3-4)

### Objectives
- Create STT service abstraction
- Implement Windows Speech Recognition
- Implement OpenAI Whisper API
- Add Azure Speech Services support (optional)

### Tasks

#### 3.3.1 Create ISpeechToTextService Interface

**File:** `windows-native/src/MemoryTimeline.Core/Services/ISpeechToTextService.cs`

```csharp
public interface ISpeechToTextService
{
    string ProviderName { get; }
    bool IsAvailable { get; }

    Task<TranscriptionResult> TranscribeAsync(string audioFilePath,
        CancellationToken cancellationToken = default);
    Task<TranscriptionResult> TranscribeAsync(Stream audioStream,
        CancellationToken cancellationToken = default);

    Task<bool> CheckAvailabilityAsync();
}

public class TranscriptionResult
{
    public bool Success { get; set; }
    public string Text { get; set; }
    public double Confidence { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public string ErrorMessage { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
}
```

#### 3.3.2 Implement WindowsSpeechRecognitionService

**File:** `windows-native/src/MemoryTimeline.Core/Services/WindowsSpeechRecognitionService.cs`

**Key Components:**
- `SpeechRecognizer` from Windows.Media.SpeechRecognition
- Continuous recognition mode
- Language detection
- Confidence scores

```csharp
public class WindowsSpeechRecognitionService : ISpeechToTextService
{
    public string ProviderName => "Windows Speech Recognition";

    public async Task<TranscriptionResult> TranscribeAsync(string audioFilePath,
        CancellationToken cancellationToken)
    {
        var file = await StorageFile.GetFileFromPathAsync(audioFilePath);

        using var recognizer = new SpeechRecognizer();
        var result = await recognizer.RecognizeAsync(file);

        return new TranscriptionResult
        {
            Success = result.Status == SpeechRecognitionResultStatus.Success,
            Text = result.Text,
            Confidence = result.Confidence,
            ProcessingTime = stopwatch.Elapsed
        };
    }
}
```

#### 3.3.3 Implement WhisperApiService

**File:** `windows-native/src/MemoryTimeline.Core/Services/WhisperApiService.cs`

**Key Components:**
- HttpClient for OpenAI API calls
- Multipart form data upload
- JSON response parsing
- Retry with Polly

**Dependencies:**
```xml
<PackageReference Include="Microsoft.Extensions.Http.Polly" Version="8.0.0" />
```

```csharp
public class WhisperApiService : ISpeechToTextService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public string ProviderName => "OpenAI Whisper";

    public async Task<TranscriptionResult> TranscribeAsync(string audioFilePath,
        CancellationToken cancellationToken)
    {
        using var fileStream = File.OpenRead(audioFilePath);
        using var content = new MultipartFormDataContent();

        content.Add(new StreamContent(fileStream), "file",
            Path.GetFileName(audioFilePath));
        content.Add(new StringContent("whisper-1"), "model");

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _apiKey);

        var response = await _httpClient.PostAsync(
            "https://api.openai.com/v1/audio/transcriptions",
            content, cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<WhisperResponse>(json);

        return new TranscriptionResult
        {
            Success = true,
            Text = result.Text,
            Confidence = 1.0, // Whisper doesn't return confidence
            ProcessingTime = stopwatch.Elapsed
        };
    }
}
```

#### 3.3.4 Implement AzureSpeechService (Optional)

**File:** `windows-native/src/MemoryTimeline.Core/Services/AzureSpeechService.cs`

**Dependencies:**
```xml
<PackageReference Include="Microsoft.CognitiveServices.Speech" Version="1.35.0" />
```

#### 3.3.5 Create STT Service Factory

**File:** `windows-native/src/MemoryTimeline.Core/Services/SpeechToTextServiceFactory.cs`

```csharp
public class SpeechToTextServiceFactory
{
    private readonly ISettingsService _settingsService;
    private readonly IServiceProvider _serviceProvider;

    public async Task<ISpeechToTextService> CreateServiceAsync()
    {
        var provider = await _settingsService.GetSettingAsync<string>("stt_provider");

        return provider switch
        {
            "windows" => _serviceProvider.GetService<WindowsSpeechRecognitionService>(),
            "whisper" => _serviceProvider.GetService<WhisperApiService>(),
            "azure" => _serviceProvider.GetService<AzureSpeechService>(),
            _ => throw new InvalidOperationException($"Unknown STT provider: {provider}")
        };
    }
}
```

#### 3.3.6 Integrate STT with QueueService

**Update QueueService.ProcessItemAsync():**

```csharp
private async Task ProcessItemAsync(QueueItemDto item)
{
    // Update status to Processing
    await UpdateQueueStatusAsync(item.QueueId, QueueStatus.Processing);

    try
    {
        // Get STT service
        var sttService = await _sttFactory.CreateServiceAsync();

        // Transcribe audio
        var result = await sttService.TranscribeAsync(item.AudioFilePath);

        if (result.Success)
        {
            // Update queue item with transcript
            await UpdateTranscriptAsync(item.QueueId, result.Text);

            // Update status to Completed
            await UpdateQueueStatusAsync(item.QueueId, QueueStatus.Completed);

            // Raise event
            ItemProcessed?.Invoke(this, new QueueItemProcessedEventArgs(item.QueueId));
        }
        else
        {
            throw new Exception(result.ErrorMessage);
        }
    }
    catch (Exception ex)
    {
        await HandleProcessingErrorAsync(item, ex);
    }
}
```

### Testing Strategy

**Unit Tests:**
- [ ] WindowsSpeechRecognitionService_Transcribe_ReturnsText
- [ ] WhisperApiService_Transcribe_CallsAPI
- [ ] STTServiceFactory_CreateService_ReturnsCorrectProvider
- [ ] QueueService_ProcessWithSTT_UpdatesTranscript

**Integration Tests:**
- [ ] EndToEnd_RecordAudio_TranscribeWithSTT_SavesTranscript

---

## Service Registration (App.xaml.cs)

Add to ConfigureServices():

```csharp
// Audio services
services.AddSingleton<IAudioRecordingService, AudioRecordingService>();
services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();

// Queue service
services.AddSingleton<IQueueService, QueueService>();

// STT services
services.AddSingleton<WindowsSpeechRecognitionService>();
services.AddSingleton<WhisperApiService>();
services.AddSingleton<AzureSpeechService>();
services.AddSingleton<SpeechToTextServiceFactory>();

// ViewModels
services.AddTransient<QueueViewModel>();
```

---

## Dependencies

```xml
<ItemGroup>
  <!-- Windows SDK -->
  <PackageReference Include="Microsoft.Windows.SDK.Contracts" Version="10.0.22621.2" />

  <!-- HTTP with Polly for retries -->
  <PackageReference Include="Microsoft.Extensions.Http.Polly" Version="8.0.0" />

  <!-- Azure Speech (optional) -->
  <PackageReference Include="Microsoft.CognitiveServices.Speech" Version="1.35.0" />
</ItemGroup>
```

---

## Settings Configuration

Add to default AppSettings seed data:

```csharp
new AppSetting { SettingKey = "stt_provider", SettingValue = "windows" },
new AppSetting { SettingKey = "stt_language", SettingValue = "en-US" },
new AppSetting { SettingKey = "audio_quality", SettingValue = "16000" }, // Sample rate
new AppSetting { SettingKey = "openai_api_key", SettingValue = "" },
new AppSetting { SettingKey = "azure_speech_key", SettingValue = "" },
new AppSetting { SettingKey = "azure_speech_region", SettingValue = "" },
```

---

## Implementation Timeline

### Week 1: Audio Recording
- Day 1-2: IAudioRecordingService interface and implementation
- Day 3: IAudioPlaybackService interface and implementation
- Day 4: DTOs and supporting models
- Day 5: Unit tests

### Week 2: Queue System
- Day 1-2: IQueueService interface and implementation
- Day 3: QueuePage XAML design
- Day 4: QueueViewModel implementation
- Day 5: Integration and testing

### Week 3: STT Services
- Day 1-2: ISpeechToTextService interface and WindowsSpeechRecognitionService
- Day 3-4: WhisperApiService implementation
- Day 5: STT Factory and service registration

### Week 4: Integration & Testing
- Day 1-2: Integrate STT with QueueService
- Day 3: End-to-end testing
- Day 4-5: Bug fixes and polish

---

## Success Metrics

- ✅ Audio recording at 16kHz, 16-bit works
- ✅ Queue system processes items in background
- ✅ Windows STT transcribes audio files
- ✅ OpenAI Whisper API integration works
- ✅ Retry logic handles transient failures
- ✅ UI updates reflect queue status changes
- ✅ Audio playback works
- ✅ Error messages are user-friendly

---

## Next Phase

After Phase 3 completion, proceed to **Phase 4: LLM Integration** for event extraction from transcripts.
