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
        NotchBorder.CornerRadius = new CornerRadius(0, 0, 24, 24);
        AnimateShadow(20, 0.6, OpenDuration);

        // Show content after size animation is underway
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
        // Fade out content quickly
        var fadeOut = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(120)));
        fadeOut.Completed += (_, _) =>
        {
            ContentArea.Visibility = Visibility.Collapsed;
            MenuButton.Visibility = Visibility.Collapsed;
        };
        ContentArea.BeginAnimation(OpacityProperty, fadeOut);
        MenuButton.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(120))));

        // Shrink after content fades
        var widthAnim = new DoubleAnimation(ViewModels.NotchViewModel.ClosedWidth, CloseDuration)
        {
            BeginTime = TimeSpan.FromMilliseconds(80),
            EasingFunction = CloseEase
        };
        var heightAnim = new DoubleAnimation(ViewModels.NotchViewModel.ClosedHeight, CloseDuration)
        {
            BeginTime = TimeSpan.FromMilliseconds(80),
            EasingFunction = CloseEase
        };
        BeginAnimation(WidthProperty, widthAnim);
        BeginAnimation(HeightProperty, heightAnim);

        NotchBorder.CornerRadius = new CornerRadius(0, 0, 14, 14);
        AnimateShadow(12, 0.4, CloseDuration);
    }

    private void AnimatePop()
    {
        var expandW = new DoubleAnimation(ViewModels.NotchViewModel.ClosedWidth + 30,
            new Duration(TimeSpan.FromMilliseconds(200))) { EasingFunction = OpenEase };
        var expandH = new DoubleAnimation(ViewModels.NotchViewModel.ClosedHeight + 4,
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
        BeginAnimation(HeightProperty, expandH);
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

    private void AnimateShadow(double blurRadius, double opacity, Duration duration)
    {
        Shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty,
            new DoubleAnimation(blurRadius, duration));
        Shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
            new DoubleAnimation(opacity, duration));
    }

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        App.PipeServer.Stop();
        Application.Current.Shutdown();
    }

    private void UpdateStatusDot()
    {
        // Match macOS: only show indicator when actively processing or needing approval
        // WaitingForInput sessions do NOT show the header dot
        if (ViewModel.PendingApprovalCount > 0)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(217, 120, 87)); // Amber/coral
            StatusDot.Visibility = Visibility.Visible;
        }
        else if (ViewModel.IsProcessing)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(255, 140, 0)); // Orange for processing
            StatusDot.Visibility = Visibility.Visible;
        }
        else
        {
            StatusDot.Visibility = Visibility.Collapsed;
        }
    }
}
