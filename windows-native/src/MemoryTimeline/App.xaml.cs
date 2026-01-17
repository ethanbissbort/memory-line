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

                // Register app services
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IThemeService, ThemeService>();

                // Register ViewModels
                services.AddTransient<MainViewModel>();
                services.AddTransient<TimelineViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<QueueViewModel>();
                services.AddTransient<ReviewViewModel>();
                services.AddTransient<ConnectionsViewModel>();

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
        try
        {
            await _host.StartAsync();

            // Ensure database is created and migrated
            using (var scope = Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await dbContext.Database.EnsureCreatedAsync();
            }

            _mainWindow = Services.GetRequiredService<MainWindow>();
            _mainWindow.Activate();

            // Initialize theme
            var themeService = Services.GetRequiredService<IThemeService>();
            await themeService.InitializeAsync();
        }
        catch (Exception ex)
        {
            // Log the error and show a message to the user
            var errorMessage = $"Failed to start Memory Timeline:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}";

            if (ex.InnerException != null)
            {
                errorMessage += $"\n\nInner Exception:\n{ex.InnerException.Message}\n{ex.InnerException.StackTrace}";
            }

            // Always write to log file first for debugging
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MemoryTimeline",
                "error.log"
            );

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"\n\n{'='.ToString().PadRight(50, '=')}\n{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{errorMessage}\n");
            }
            catch
            {
                // Ignore logging failures
            }

            // Try to show an error dialog if possible
            try
            {
                // Create a minimal window with content for the dialog
                if (_mainWindow == null)
                {
                    _mainWindow = new Window();
                    _mainWindow.Content = new Grid(); // Must set content before accessing XamlRoot
                    _mainWindow.Activate();
                }

                if (_mainWindow.Content?.XamlRoot != null)
                {
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
            catch
            {
                // Dialog failed - error is already logged
            }

            // Exit the application
            Environment.Exit(1);
        }
    }
}
