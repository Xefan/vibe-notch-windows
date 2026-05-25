using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClaudeIslandWindows.Views;

public partial class NotchWindow : Window
{
    private ViewModels.NotchViewModel ViewModel => App.NotchViewModel;

    public NotchWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;

        Icon = RenderCrabIcon();

        Loaded += OnLoaded;
        Deactivated += OnDeactivated;
    }

    private static ImageSource RenderCrabIcon()
    {
        const int size = 64;
        var crab = new Controls.CrabIcon { Width = size, Height = size * 52.0 / 66.0 };
        crab.Measure(new Size(size, size));
        crab.Arrange(new Rect(0, 0, size, size));
        crab.UpdateLayout();

        // Draw on a square canvas with transparent background, crab centered
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));
            var offsetY = (size - crab.Height) / 2;
            dc.PushTransform(new TranslateTransform(0, offsetY));
            dc.DrawRectangle(new VisualBrush(crab), null, new Rect(0, 0, size, crab.Height));
            dc.Pop();
        }

        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Position centered at top of screen
        var screen = SystemParameters.WorkArea;
        Left = screen.Left + (screen.Width - Width) / 2;
        Top = screen.Top;

        ViewModel.Initialize();

        // Heartbeat: log state every 30s and self-heal if window goes phantom (rendering stops despite IsVisible=true)
        var hb = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        hb.Tick += (_, _) =>
        {
            try
            {
                // Self-heal: re-assert position, topmost, and z-order. Cheap and harmless if window is fine.
                var screen = SystemParameters.WorkArea;
                Left = screen.Left + (screen.Width - Width) / 2;
                Top = screen.Top;
                Topmost = false;
                Topmost = true;
                var helper = new WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                    SetWindowPos(helper.Handle, HWND_TOPMOST, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

                var logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ClaudeIsland", "heartbeat.log");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
                // Gather Win32 truth (vs WPF's view, which can lie)
                var hwnd = new WindowInteropHelper(this).Handle;
                bool win32Visible = false;
                string rect = "n/a";
                bool onScreen = false;
                if (hwnd != IntPtr.Zero)
                {
                    win32Visible = IsWindowVisible(hwnd);
                    if (GetWindowRect(hwnd, out var r))
                    {
                        rect = $"({r.Left},{r.Top})-({r.Right},{r.Bottom})";
                        // Check if any monitor contains the window center
                        var cx = (r.Left + r.Right) / 2;
                        var cy = (r.Top + r.Bottom) / 2;
                        onScreen = MonitorFromPoint(new POINT { x = cx, y = cy }, MONITOR_DEFAULTTONULL) != IntPtr.Zero;
                    }
                }
                var clip = (FindName("ContentGrid") as System.Windows.Controls.Grid)?.Clip;
                var clipDesc = clip?.Bounds.ToString() ?? "null";
                var virtualBounds = $"V({SystemParameters.VirtualScreenLeft},{SystemParameters.VirtualScreenTop},{SystemParameters.VirtualScreenWidth}x{SystemParameters.VirtualScreenHeight})";

                System.IO.File.AppendAllText(logPath,
                    $"{DateTime.Now:HH:mm:ss} WPF[Left={Left} Top={Top} Vis={IsVisible}/{Visibility} State={WindowState}] " +
                    $"Win32[Vis={win32Visible} Rect={rect} OnScreen={onScreen} {virtualBounds}] " +
                    $"App[Status={ViewModel.Status} Content={ViewModel.CurrentContent} Clip={clipDesc}]\n");
            }
            catch { }
        };
        hb.Start();

        // Also log any visibility changes
        IsVisibleChanged += (_, args) =>
        {
            try
            {
                var logPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ClaudeIsland", "heartbeat.log");
                System.IO.File.AppendAllText(logPath,
                    $"{DateTime.Now:HH:mm:ss} ** IsVisibleChanged: {args.NewValue} **\n");
            }
            catch { }
        };
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (ViewModel.Status == ViewModels.NotchStatus.Opened)
            ViewModel.CloseCommand.Execute(null);
    }

    // Win32 — used to re-assert topmost / show window state in the heartbeat
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    private const uint MONITOR_DEFAULTTONULL = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }
}
