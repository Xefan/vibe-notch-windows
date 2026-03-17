using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ClaudeIslandWindows.Views;

public partial class NotchWindow : Window
{
    private ViewModels.NotchViewModel ViewModel => App.NotchViewModel;

    public NotchWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;

        Loaded += OnLoaded;
        Deactivated += OnDeactivated;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Position centered at top of screen
        var screen = SystemParameters.WorkArea;
        Left = screen.Left + (screen.Width - Width) / 2;
        Top = screen.Top;

        ViewModel.Initialize();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (ViewModel.Status == ViewModels.NotchStatus.Opened)
            ViewModel.CloseCommand.Execute(null);
    }
}
