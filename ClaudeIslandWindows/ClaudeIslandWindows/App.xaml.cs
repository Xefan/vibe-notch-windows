using System.Windows;
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

    private NotchWindow? _window;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, "ClaudeIslandWindows_SingleInstance", out bool isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        HookInstaller.InstallIfNeeded();
        PipeServer.Start(OnHookEvent);

        _window = new NotchWindow();
        _window.Show();
    }

    private void OnHookEvent(Models.HookEvent hookEvent)
    {
        SessionStore.ProcessEvent(new Models.SessionEvent.HookReceived(hookEvent));

        _window?.Dispatcher.BeginInvoke(() =>
        {
            NotchViewModel.RefreshSessions(SessionStore);
        });
    }
}
