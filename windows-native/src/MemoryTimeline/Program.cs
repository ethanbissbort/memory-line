using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;

namespace MemoryTimeline;

/// <summary>
/// Program entry point for unpackaged WinUI 3 application.
/// Handles Windows App SDK bootstrapper initialization.
/// </summary>
public static class Program
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MemoryTimeline",
        "startup.log"
    );

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        Log("=== Application Starting ===");
        Log($"Args: {string.Join(", ", args)}");
        Log($"Current directory: {Environment.CurrentDirectory}");
        Log($"Process path: {Environment.ProcessPath}");

        try
        {
            Log("Initializing COM...");
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Log("COM initialized");

            Log("Checking if main instance...");
            bool isRedirect = DecideRedirection();

            if (!isRedirect)
            {
                Log("Starting as main instance...");
                Microsoft.UI.Xaml.Application.Start((p) =>
                {
                    Log("Inside Application.Start callback");
                    var context = new DispatcherQueueSynchronizationContext(
                        DispatcherQueue.GetForCurrentThread());
                    SynchronizationContext.SetSynchronizationContext(context);
                    Log("Creating App instance...");
                    new App();
                    Log("App instance created");
                });
                Log("Application.Start completed");
            }
            else
            {
                Log("Redirecting to existing instance");
            }

            Log("Main returning 0");
            return 0;
        }
        catch (Exception ex)
        {
            Log($"FATAL ERROR: {ex.GetType().Name}: {ex.Message}");
            Log($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Log($"Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                Log($"Inner stack trace: {ex.InnerException.StackTrace}");
            }
            return 1;
        }
    }

    private static bool DecideRedirection()
    {
        try
        {
            Log("Getting current activation args...");
            bool isRedirect = false;
            AppActivationArguments args = AppInstance.GetCurrent().GetActivatedEventArgs();
            Log($"Activation kind: {args.Kind}");

            ExtendedActivationKind kind = args.Kind;
            AppInstance keyInstance = AppInstance.FindOrRegisterForKey("MemoryTimelineMainInstance");
            Log($"Key instance found, IsCurrent: {keyInstance.IsCurrent}");

            if (keyInstance.IsCurrent)
            {
                Log("This is the main instance");
            }
            else
            {
                Log("Redirecting activation to main instance");
                isRedirect = true;
                keyInstance.RedirectActivationToAsync(args).AsTask().Wait();
            }

            return isRedirect;
        }
        catch (Exception ex)
        {
            Log($"DecideRedirection error (non-fatal): {ex.Message}");
            // If redirection fails, just continue as main instance
            return false;
        }
    }
}
