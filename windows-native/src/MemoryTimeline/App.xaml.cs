using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private IHost? _host;
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MemoryTimeline",
        "error.log"
    );

    /// <summary>
    /// Writes a message to the error log file.
    /// </summary>
    private static void WriteToLog(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch
        {
            // Ignore logging failures
        }
    }

    /// <summary>
    /// Writes an exception to the error log file.
    /// </summary>
    private static void LogException(string context, Exception ex)
    {
        var message = $"=== {context} ===\n{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";

        if (ex.InnerException != null)
        {
            message += $"\n\nInner Exception:\n{ex.InnerException.GetType().Name}: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}";
        }

        WriteToLog(message);
    }

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        WriteToLog("App constructor starting...");

        try
        {
            InitializeComponent();
            WriteToLog("InitializeComponent completed");

            // Set up global exception handlers
            UnhandledException += App_UnhandledException;
            System.AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

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
                    services.AddScoped<ITagRepository, TagRepository>();
                    services.AddScoped<IPersonRepository, PersonRepository>();
                    services.AddScoped<ILocationRepository, LocationRepository>();
                    services.AddScoped<ICrossReferenceRepository, CrossReferenceRepository>();
                    services.AddScoped<IEventEmbeddingRepository, EventEmbeddingRepository>();
                    services.AddScoped<IAppSettingRepository, AppSettingRepository>();
                    services.AddScoped<IPendingEventRepository, PendingEventRepository>();

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
                    services.AddSingleton<ILlmService, AnthropicLlmService>();
                    services.AddScoped<IEventExtractionService, EventExtractionService>();

                    // Phase 5: RAG & Embedding services
                    services.AddHttpClient<IEmbeddingService, OpenAIEmbeddingService>();
                    services.AddScoped<IRagService, RagService>();

                    // Phase 6: Export/Import & Windows Integration services
                    services.AddScoped<IExportService, ExportService>();
                    services.AddScoped<IImportService, ImportService>();
                    services.AddSingleton<Services.INotificationService, Services.NotificationService>();
                    services.AddSingleton<IWindowsTimelineService, WindowsTimelineService>();
                    services.AddSingleton<IJumpListService, JumpListService>();

                    // Phase 7: Advanced Search, Analytics, & Audio Import services
                    services.AddScoped<IAdvancedSearchService, AdvancedSearchService>();
                    services.AddScoped<IAnalyticsService, AnalyticsService>();
                    services.AddScoped<IAudioImportService, AudioImportService>();

                    // Register app services
                    services.AddSingleton<INavigationService, NavigationService>();
                    services.AddSingleton<IThemeService, ThemeService>();

                    // Register ViewModels
                    services.AddTransient<MainViewModel>();
                    services.AddSingleton<TimelineViewModel>(); // Singleton to preserve state across navigation
                    services.AddTransient<SettingsViewModel>();
                    services.AddTransient<QueueViewModel>();
                    services.AddTransient<ReviewViewModel>();
                    services.AddTransient<ConnectionsViewModel>();
                    services.AddTransient<SearchViewModel>();
                    services.AddTransient<AnalyticsViewModel>();

                    // Register Views
                    services.AddTransient<MainWindow>();
                    services.AddTransient<TimelinePage>();
                    services.AddTransient<QueuePage>();
                    services.AddTransient<ReviewPage>();
                    services.AddTransient<SearchPage>();
                    services.AddTransient<AnalyticsPage>();
                    services.AddTransient<SettingsPage>();
                    services.AddTransient<ConnectionsPage>();
                })
                .Build();

            WriteToLog("Host built successfully");
        }
        catch (Exception ex)
        {
            LogException("App Constructor Failed", ex);
            throw; // Re-throw to let the app crash (but now we have a log)
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogException("Unhandled UI Exception", e.Exception);
        e.Handled = false; // Let it crash, but we have the log
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogException("AppDomain Unhandled Exception", ex);
        }
        else
        {
            WriteToLog($"AppDomain Unhandled Exception (non-Exception): {e.ExceptionObject}");
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("Unobserved Task Exception", e.Exception);
    }

    /// <summary>
    /// Gets the current <see cref="App"/> instance in use
    /// </summary>
    public new static App Current => (App)Application.Current;

    /// <summary>
    /// Gets the <see cref="IServiceProvider"/> instance to resolve application services.
    /// </summary>
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not initialized");

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
        WriteToLog("OnLaunched starting...");

        try
        {
            if (_host == null)
            {
                throw new InvalidOperationException("Host was not initialized in constructor");
            }

            WriteToLog("Starting host...");
            await _host.StartAsync();
            WriteToLog("Host started");

            // Ensure database is created and migrated
            WriteToLog("Creating database scope...");
            using (var scope = Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                WriteToLog("Ensuring database created...");
                await dbContext.Database.EnsureCreatedAsync();
                WriteToLog("Database ready");
            }

            WriteToLog("Creating MainWindow...");
            _mainWindow = Services.GetRequiredService<MainWindow>();
            WriteToLog("Activating MainWindow...");
            _mainWindow.Activate();
            WriteToLog("MainWindow activated");

            // Initialize theme
            WriteToLog("Initializing theme...");
            var themeService = Services.GetRequiredService<IThemeService>();
            await themeService.InitializeAsync();
            WriteToLog("Theme initialized - startup complete!");
        }
        catch (Exception ex)
        {
            LogException("OnLaunched Failed", ex);

            // Try to show an error dialog if possible
            try
            {
                // Create a minimal window with content for the dialog
                if (_mainWindow == null)
                {
                    _mainWindow = new Window();
                    _mainWindow.Content = new Grid();
                    _mainWindow.Activate();
                }

                if (_mainWindow.Content?.XamlRoot != null)
                {
                    var errorMessage = $"Failed to start Memory Timeline:\n\n{ex.Message}";
                    if (ex.InnerException != null)
                    {
                        errorMessage += $"\n\nInner: {ex.InnerException.Message}";
                    }
                    errorMessage += $"\n\nSee log file for details:\n{LogPath}";

                    var dialog = new ContentDialog
                    {
                        Title = "Startup Error",
                        Content = new ScrollViewer
                        {
                            Content = new TextBlock
                            {
                                Text = errorMessage,
                                TextWrapping = TextWrapping.Wrap,
                                IsTextSelectionEnabled = true
                            },
                            MaxHeight = 400
                        },
                        CloseButtonText = "Exit",
                        XamlRoot = _mainWindow.Content.XamlRoot
                    };

                    await dialog.ShowAsync();
                }
            }
            catch (Exception dialogEx)
            {
                LogException("Error Dialog Failed", dialogEx);
            }

            // Exit the application
            Environment.Exit(1);
        }
    }
}
