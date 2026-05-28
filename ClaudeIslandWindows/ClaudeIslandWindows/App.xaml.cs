using System.Windows;
using ClaudeIslandWindows.BuddyBridge;
using ClaudeIslandWindows.Services;
using ClaudeIslandWindows.ViewModels;
using ClaudeIslandWindows.Views;

namespace ClaudeIslandWindows;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    public static SessionStore SessionStore { get; } = new();
    public static NamedPipeServer PipeServer { get; } = new();
    public static NotchViewModel NotchViewModel { get; } = new();
    public static BuddyBridgeService BuddyBridge { get; } = new(SessionStore, PipeServer);

    private NotchWindow? _window;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _singleInstanceMutex = new Mutex(true, "ClaudeIslandWindows_SingleInstance", out bool isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        StartupManager.SyncIfEnabled();
        HookInstaller.InstallIfNeeded();
        PipeServer.Start(OnHookEvent);
        BuddyBridge.Start();

        _window = new NotchWindow();
        _window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { BuddyBridge.Stop(); } catch { }
        try { PipeServer.Stop(); } catch { }
        base.OnExit(e);
    }

    private static string CrashLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeIsland", "crash.log");

    private static void LogException(string source, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath,
                $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] ===\n{ex}\n\n");
        }
        catch { }
    }

    private void OnDispatcherUnhandledException(object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("Dispatcher", e.Exception);
        e.Handled = true; // keep the app alive
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogException("AppDomain", ex);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("Task", e.Exception);
        e.SetObserved();
    }

    private void OnHookEvent(Models.HookEvent hookEvent)
    {
        SessionStore.ProcessEvent(new Models.SessionEvent.HookReceived(hookEvent));
        try { BuddyBridge.NotifyHookEvent(hookEvent); } catch { }

        _window?.Dispatcher.BeginInvoke(() =>
        {
            NotchViewModel.RefreshSessions(SessionStore);

            // DEBUG — log current state of ALL sessions after refresh
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ClaudeIsland", "events.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                var snapshot = string.Join(", ", SessionStore.GetSessions()
                    .Select(s => $"{s.SessionId[..8]}={s.Phase.Kind}(last={s.LastActivity:HH:mm:ss})"));
                File.AppendAllText(logPath,
                    $"{DateTime.Now:HH:mm:ss} evt={hookEvent.Event}/{hookEvent.Status} " +
                    $"from={hookEvent.SessionId[..8]} → processing={NotchViewModel.IsProcessing} " +
                    $"[{snapshot}]\n");
            }
            catch { }
        });
    }
}
