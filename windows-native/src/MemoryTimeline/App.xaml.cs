using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Repositories;
using MemoryTimeline.ViewModels;
using MemoryTimeline.Views;
using MemoryTimeline.Services;

namespace MemoryTimeline;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _mainWindow;
    private readonly IHost _host;

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        InitializeComponent();

        // Configure dependency injection
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Register DbContext
                services.AddDbContext<AppDbContext>();

                // Register repositories
                services.AddScoped<IEventRepository, EventRepository>();
                services.AddScoped<IEraRepository, EraRepository>();
                services.AddScoped<IRecordingQueueRepository, RecordingQueueRepository>();

                // Register core services
                services.AddSingleton<ISettingsService, SettingsService>();
                services.AddScoped<IEventService, EventService>();
                services.AddScoped<ITimelineService, TimelineService>();

                // Phase 3: Audio & Queue services
                services.AddSingleton<IAudioRecordingService, AudioRecordingService>();
                services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();
                services.AddScoped<IQueueService, QueueService>();
                services.AddScoped<ISpeechToTextService, WindowsSpeechRecognitionService>();

                // Phase 4: LLM & Event Extraction services
                services.AddHttpClient<ILlmService, AnthropicClaudeService>();
                services.AddScoped<IEventExtractionService, EventExtractionService>();

                // Phase 5: RAG & Embedding services
                services.AddHttpClient<IEmbeddingService, OpenAIEmbeddingService>();
                services.AddScoped<IRagService, RagService>();

                // TODO: Phase 7: Import/Export services (not yet implemented)
                // services.AddScoped<IExportService, ExportService>();
                // services.AddScoped<IImportService, ImportService>();

                // Register app services
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IThemeService, ThemeService>();

                // Register ViewModels
                services.AddTransient<MainViewModel>();
                services.AddTransient<TimelineViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<QueueViewModel>();

                // Register Views
                services.AddTransient<MainWindow>();
                services.AddTransient<TimelinePage>();
                services.AddTransient<QueuePage>();
                services.AddTransient<SearchPage>();
                services.AddTransient<AnalyticsPage>();
                services.AddTransient<SettingsPage>();
            })
            .Build();
    }

    /// <summary>
    /// Gets the current <see cref="App"/> instance in use
    /// </summary>
    public new static App Current => (App)Application.Current;

    /// <summary>
    /// Gets the <see cref="IServiceProvider"/> instance to resolve application services.
    /// </summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>
    /// Gets the main application window.
    /// </summary>
    public Window? Window => _mainWindow;

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await _host.StartAsync();

        _mainWindow = Services.GetRequiredService<MainWindow>();
        _mainWindow.Activate();

        // Initialize theme
        var themeService = Services.GetRequiredService<IThemeService>();
        await themeService.InitializeAsync();
    }
}
