using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ClaudeIslandWindows.ViewModels;

namespace ClaudeIslandWindows.Views;

public partial class NotchWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    private NotchViewModel ViewModel => App.NotchViewModel;

    public NotchWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;

        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

        PositionAtTopCenter();
        ViewModel.Initialize();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        PositionAtTopCenter();
    }

    private void PositionAtTopCenter()
    {
        var screen = SystemParameters.WorkArea;
        Left = screen.Left + (screen.Width - ActualWidth) / 2;
        Top = screen.Top;
    }

    private void OnMouseEnter(object sender, MouseEventArgs e) => ViewModel.OnMouseEnter();
    private void OnMouseLeave(object sender, MouseEventArgs e) => ViewModel.OnMouseLeave();

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel.ToggleCommand.Execute(null);
    }
}
