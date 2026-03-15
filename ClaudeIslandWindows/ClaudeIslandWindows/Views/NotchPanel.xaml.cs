using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ClaudeIslandWindows.Views;

public partial class NotchPanel : UserControl
{
    private static readonly Duration OpenDuration = new(TimeSpan.FromMilliseconds(420));
    private static readonly Duration CloseDuration = new(TimeSpan.FromMilliseconds(350));
    private static readonly IEasingFunction OpenEase = new QuadraticEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction CloseEase = new QuadraticEase { EasingMode = EasingMode.EaseInOut };

    private ViewModels.NotchViewModel ViewModel => App.NotchViewModel;

    public NotchPanel()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.NotchViewModel.Status))
            Dispatcher.BeginInvoke(() => AnimateToState(ViewModel.Status));
        else if (e.PropertyName == nameof(ViewModels.NotchViewModel.CurrentContent)
                 && ViewModel.Status == ViewModels.NotchStatus.Opened)
            Dispatcher.BeginInvoke(AnimateContentSwitch);
        else if (e.PropertyName is nameof(ViewModels.NotchViewModel.HasActiveSessions)
                 or nameof(ViewModels.NotchViewModel.IsProcessing)
                 or nameof(ViewModels.NotchViewModel.PendingApprovalCount))
            Dispatcher.BeginInvoke(UpdateStatusDot);
    }

    private void AnimateToState(ViewModels.NotchStatus status)
    {
        switch (status)
        {
            case ViewModels.NotchStatus.Opened: AnimateOpen(); break;
            case ViewModels.NotchStatus.Closed: AnimateClose(); break;
            case ViewModels.NotchStatus.Popping: AnimatePop(); break;
        }
    }

    private void AnimateOpen()
    {
        var (targetW, targetH) = GetTargetSize();
        AnimateSize(targetW, targetH, OpenDuration, OpenEase);

        ContentArea.Visibility = Visibility.Visible;
        ContentArea.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200)))
            {
                BeginTime = TimeSpan.FromMilliseconds(200),
                EasingFunction = OpenEase
            });

        MenuButton.Visibility = Visibility.Visible;
        MenuButton.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200)))
            {
                BeginTime = TimeSpan.FromMilliseconds(200)
            });
    }

    private void AnimateClose()
    {
        ContentArea.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(120))));
        MenuButton.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(120))));

        var heightAnim = new DoubleAnimation(ViewModels.NotchViewModel.ClosedHeight, CloseDuration)
        {
            BeginTime = TimeSpan.FromMilliseconds(80),
            EasingFunction = CloseEase
        };
        heightAnim.Completed += (_, _) =>
        {
            ContentArea.Visibility = Visibility.Collapsed;
            MenuButton.Visibility = Visibility.Collapsed;
        };

        BeginAnimation(WidthProperty,
            new DoubleAnimation(ViewModels.NotchViewModel.ClosedWidth, CloseDuration)
            {
                BeginTime = TimeSpan.FromMilliseconds(80),
                EasingFunction = CloseEase
            });
        BeginAnimation(HeightProperty, heightAnim);
    }

    private void AnimatePop()
    {
        var expandW = new DoubleAnimation(ViewModels.NotchViewModel.ClosedWidth + 30,
            new Duration(TimeSpan.FromMilliseconds(200))) { EasingFunction = OpenEase };

        expandW.Completed += (_, _) =>
        {
            BeginAnimation(WidthProperty,
                new DoubleAnimation(ViewModels.NotchViewModel.ClosedWidth,
                    new Duration(TimeSpan.FromMilliseconds(300)))
                {
                    BeginTime = TimeSpan.FromMilliseconds(100),
                    EasingFunction = CloseEase
                });
            BeginAnimation(HeightProperty,
                new DoubleAnimation(ViewModels.NotchViewModel.ClosedHeight,
                    new Duration(TimeSpan.FromMilliseconds(300)))
                {
                    BeginTime = TimeSpan.FromMilliseconds(100),
                    EasingFunction = CloseEase
                });
        };

        BeginAnimation(WidthProperty, expandW);
        BeginAnimation(HeightProperty,
            new DoubleAnimation(ViewModels.NotchViewModel.ClosedHeight + 4,
                new Duration(TimeSpan.FromMilliseconds(200))) { EasingFunction = OpenEase });
    }

    private void AnimateContentSwitch()
    {
        var (targetW, targetH) = GetTargetSize();
        AnimateSize(targetW, targetH, new Duration(TimeSpan.FromMilliseconds(250)), OpenEase);
    }

    private (double w, double h) GetTargetSize() => ViewModel.CurrentContent switch
    {
        ViewModels.ContentType.Instances => (480.0, 320.0),
        ViewModels.ContentType.Menu => (480.0, 420.0),
        _ => (480.0, 320.0)
    };

    private void AnimateSize(double targetWidth, double targetHeight, Duration duration, IEasingFunction ease)
    {
        BeginAnimation(WidthProperty, new DoubleAnimation(targetWidth, duration) { EasingFunction = ease });
        BeginAnimation(HeightProperty, new DoubleAnimation(targetHeight, duration) { EasingFunction = ease });
    }

    private void OnNotchSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateNotchClip(e.NewSize.Width, e.NewSize.Height);
    }

    private void UpdateNotchClip(double w, double h)
    {
        if (w <= 0 || h <= 0) return;

        const double tr = 6;  // top corner inward radius
        const double br = 16; // bottom corner outward radius

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(0, 0), true, true);
            ctx.QuadraticBezierTo(new Point(tr, 0), new Point(tr, tr), false, true);
            ctx.LineTo(new Point(tr, h - br), false, true);
            ctx.QuadraticBezierTo(new Point(tr, h), new Point(tr + br, h), false, true);
            ctx.LineTo(new Point(w - tr - br, h), false, true);
            ctx.QuadraticBezierTo(new Point(w - tr, h), new Point(w - tr, h - br), false, true);
            ctx.LineTo(new Point(w - tr, tr), false, true);
            ctx.QuadraticBezierTo(new Point(w - tr, 0), new Point(w, 0), false, true);
        }
        geo.Freeze();
        NotchBorder.Clip = geo;
    }

    private void OnHeaderClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe && fe.Name == "MenuButton")
            return;

        ViewModel.ToggleCommand.Execute(null);
        e.Handled = true;
    }

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        App.PipeServer.Stop();
        Application.Current.Shutdown();
    }

    private void UpdateStatusDot()
    {
        if (ViewModel.PendingApprovalCount > 0)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(217, 120, 87));
            StatusDot.Visibility = Visibility.Visible;
        }
        else if (ViewModel.IsProcessing)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(255, 140, 0));
            StatusDot.Visibility = Visibility.Visible;
        }
        else
        {
            StatusDot.Visibility = Visibility.Collapsed;
        }
    }
}
