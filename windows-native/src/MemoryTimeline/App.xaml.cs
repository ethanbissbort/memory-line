using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data;
using MemoryTimeline.ViewModels;
using MemoryTimeline.Views;

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

                // Register services
                services.AddSingleton<ISettingsService, SettingsService>();
                services.AddScoped<IEventService, EventService>();
                services.AddScoped<IAudioService, AudioService>();
                services.AddScoped<ISpeechToTextService, SpeechToTextService>();
                services.AddScoped<ILlmService, LlmService>();
                services.AddScoped<IEmbeddingService, EmbeddingService>();
                services.AddScoped<IRagService, RagService>();
                services.AddScoped<IExportService, ExportService>();
                services.AddScoped<IImportService, ImportService>();

                // Register ViewModels
                services.AddTransient<MainViewModel>();
                services.AddTransient<TimelineViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<QueueViewModel>();

                // Register Views
                services.AddTransient<MainWindow>();
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
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await _host.StartAsync();

        _mainWindow = Services.GetRequiredService<MainWindow>();
        _mainWindow.Activate();
    }
}
