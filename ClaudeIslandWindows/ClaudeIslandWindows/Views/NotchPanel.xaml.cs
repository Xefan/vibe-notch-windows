using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ClaudeIslandWindows.Services;

namespace ClaudeIslandWindows.Views;

public partial class NotchPanel : UserControl
{
    private static readonly Duration OpenDuration = new(TimeSpan.FromMilliseconds(420));
    private static readonly Duration CloseDuration = new(TimeSpan.FromMilliseconds(350));
    private static readonly IEasingFunction OpenEase = new QuadraticEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction CloseEase = new QuadraticEase { EasingMode = EasingMode.EaseInOut };

    private static readonly string[] SpinnerSymbols = ["·", "✳", "∗", "✶", "✳", "∗"];
    private int _spinnerPhase;
    private System.Windows.Threading.DispatcherTimer? _spinnerTimer;
    private System.Windows.Threading.DispatcherTimer? _chatRefreshTimer;

    // Current clip dimensions (animated)
    private double _clipWidth = ViewModels.NotchViewModel.ClosedWidth;
    private double _clipHeight = ViewModels.NotchViewModel.ClosedHeight;

    // Content grid dimensions (change on content switch)
    private double _gridWidth = 480;
    private double _gridHeight = 320;

    private ViewModels.NotchViewModel ViewModel => App.NotchViewModel;

    public NotchPanel()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelChanged;

        _spinnerTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _spinnerTimer.Tick += (_, _) =>
        {
            _spinnerPhase = (_spinnerPhase + 1) % SpinnerSymbols.Length;
            SpinnerText.Text = SpinnerSymbols[_spinnerPhase];
        };

        _chatRefreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _chatRefreshTimer.Tick += (_, _) =>
        {
            if (ViewModel.CurrentContent == ViewModels.ContentType.Chat &&
                ViewModel.CurrentChatSession is { } session &&
                ViewModel.Status == ViewModels.NotchStatus.Opened)
                RefreshChatMessages(session);
        };

        ContentGrid.MouseEnter += (_, _) =>
        {
            ViewModel.OnMouseEnter();
            AnimateShadow(1);
        };
        ContentGrid.MouseLeave += (_, _) =>
        {
            ViewModel.OnMouseLeave();
            if (ViewModel.Status != ViewModels.NotchStatus.Opened)
                AnimateShadow(0);
        };

        // Set initial clip with notch shape
        UpdateClipRect(isFinal: true);
    }

    private void UpdateClipRect(bool isFinal = false)
    {
        var x = (_gridWidth - _clipWidth) / 2;
        var w = _clipWidth;
        var h = _clipHeight;

        if (w <= 0 || h <= 0) return;

        const double tr = 6;
        const double br = 16;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(x, 0), true, true);
            ctx.QuadraticBezierTo(new Point(x + tr, 0), new Point(x + tr, tr), false, true);
            ctx.LineTo(new Point(x + tr, h - br), false, true);
            ctx.QuadraticBezierTo(new Point(x + tr, h), new Point(x + tr + br, h), false, true);
            ctx.LineTo(new Point(x + w - tr - br, h), false, true);
            ctx.QuadraticBezierTo(new Point(x + w - tr, h), new Point(x + w - tr, h - br), false, true);
            ctx.LineTo(new Point(x + w - tr, tr), false, true);
            ctx.QuadraticBezierTo(new Point(x + w - tr, 0), new Point(x + w, 0), false, true);
        }
        geo.Freeze();
        ContentGrid.Clip = geo;
    }

    // --- Clip animation via DispatcherTimer ---
    private double _animFromW, _animFromH, _animToW, _animToH;
    private DateTime _animStart;
    private double _animDurationMs;
    private bool _animIsOpen;
    private bool _animRunning;
    private Action? _animCompleted;

    private void AnimateClip(double toW, double toH, Duration duration, bool isOpen, Action? completed = null)
    {
        _animFromW = _clipWidth;
        _animFromH = _clipHeight;
        _animToW = toW;
        _animToH = toH;
        _animDurationMs = duration.TimeSpan.TotalMilliseconds;
        _animIsOpen = isOpen;
        _animCompleted = completed;
        _animStart = DateTime.UtcNow;

        if (!_animRunning)
        {
            _animRunning = true;
            CompositionTarget.Rendering += OnRenderFrame;
        }
    }

    private void OnRenderFrame(object? sender, EventArgs e)
    {
        var elapsed = (DateTime.UtcNow - _animStart).TotalMilliseconds;
        var t = Math.Min(1.0, elapsed / _animDurationMs);

        double eased;
        if (_animIsOpen)
            eased = 1 - (1 - t) * (1 - t);
        else
            eased = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;

        _clipWidth = _animFromW + (_animToW - _animFromW) * eased;
        _clipHeight = _animFromH + (_animToH - _animFromH) * eased;
        UpdateClipRect();

        if (t >= 1.0)
        {
            CompositionTarget.Rendering -= OnRenderFrame;
            _animRunning = false;
            _clipWidth = _animToW;
            _clipHeight = _animToH;
            UpdateClipRect(isFinal: true);
            _animCompleted?.Invoke();
        }
    }

    // --- State management ---

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.NotchViewModel.Status))
        {
            Dispatcher.BeginInvoke(() => AnimateToState(ViewModel.Status));
            Dispatcher.BeginInvoke(UpdateStatusIndicators);
        }
        else if (e.PropertyName == nameof(ViewModels.NotchViewModel.CurrentContent)
                 && ViewModel.Status == ViewModels.NotchStatus.Opened)
        {
            Dispatcher.BeginInvoke(AnimateContentSwitch);
            if (ViewModel.CurrentContent == ViewModels.ContentType.Chat && ViewModel.CurrentChatSession is { } session)
                Dispatcher.BeginInvoke(() => LoadChatMessages(session));
            else
                _chatRefreshTimer?.Stop();
        }
        else if (e.PropertyName is nameof(ViewModels.NotchViewModel.HasActiveSessions)
                 or nameof(ViewModels.NotchViewModel.IsProcessing)
                 or nameof(ViewModels.NotchViewModel.HasWaitingForInput)
                 or nameof(ViewModels.NotchViewModel.PendingApprovalCount))
            Dispatcher.BeginInvoke(UpdateStatusIndicators);
    }

    private void AnimateToState(ViewModels.NotchStatus status)
    {
        switch (status)
        {
            case ViewModels.NotchStatus.Opened: AnimateOpen(); break;
            case ViewModels.NotchStatus.Closed: _chatRefreshTimer?.Stop(); AnimateClose(); break;
            case ViewModels.NotchStatus.Popping: AnimatePop(); break;
        }
    }

    private void AnimateOpen()
    {
        var (targetW, targetH) = GetTargetSize();
        _gridWidth = targetW;
        _gridHeight = targetH;
        ContentGrid.Width = targetW;
        ContentGrid.Height = targetH;

        AnimateClip(targetW, targetH, OpenDuration, true);
        AnimateShadow(1);

        // Slide crab/spinner from activity position to natural position
        CrabTranslate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, OpenDuration) { EasingFunction = OpenEase });
        SpinnerTranslate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, OpenDuration) { EasingFunction = OpenEase });

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

        AnimateShadow(0);

        // If there's activity, slide crab/spinner back to centered position
        if (ViewModel.IsProcessing || ViewModel.PendingApprovalCount > 0)
        {
            CrabTranslate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(ActivityOffset + 3, CloseDuration) { EasingFunction = CloseEase });
            SpinnerTranslate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-ActivityOffset, CloseDuration) { EasingFunction = CloseEase });
        }

        AnimateClip(ViewModels.NotchViewModel.ClosedWidth, ViewModels.NotchViewModel.ClosedHeight,
            CloseDuration, false, () =>
            {
                ContentArea.Visibility = Visibility.Collapsed;
                MenuButton.Visibility = Visibility.Collapsed;
                // Reset grid to expanded size for next open
                _gridWidth = 480;
                _gridHeight = 320;
                ContentGrid.Width = 480;
                ContentGrid.Height = 320;
            });
    }

    private void AnimatePop()
    {
        AnimateClip(ViewModels.NotchViewModel.ClosedWidth + 30,
            ViewModels.NotchViewModel.ClosedHeight + 4,
            new Duration(TimeSpan.FromMilliseconds(200)), true, () =>
            {
                AnimateClip(ViewModels.NotchViewModel.ClosedWidth,
                    ViewModels.NotchViewModel.ClosedHeight,
                    new Duration(TimeSpan.FromMilliseconds(300)), false);
            });
    }

    private void AnimateContentSwitch()
    {
        var (targetW, targetH) = GetTargetSize();
        _gridWidth = targetW;
        _gridHeight = targetH;
        ContentGrid.Width = targetW;
        ContentGrid.Height = targetH;
        AnimateClip(targetW, targetH, new Duration(TimeSpan.FromMilliseconds(250)), true);
    }

    private (double w, double h) GetTargetSize() => ViewModel.CurrentContent switch
    {
        ViewModels.ContentType.Instances => (480.0, 320.0),
        ViewModels.ContentType.Menu => (480.0, 420.0),
        ViewModels.ContentType.Chat => (600.0, 580.0),
        _ => (480.0, 320.0)
    };

    private void AnimateShadow(double opacity)
    {
        NotchShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
            new DoubleAnimation(opacity, new Duration(TimeSpan.FromMilliseconds(200))));
    }

    private void OnNotchSizeChanged(object sender, SizeChangedEventArgs e) { }

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

    // How far to translate the crab/spinner into the collapsed clip area
    private const double ActivityOffset = 140;

    private void UpdateStatusIndicators()
    {
        var isOpened = ViewModel.Status == ViewModels.NotchStatus.Opened;
        var hasActivity = ViewModel.IsProcessing || ViewModel.PendingApprovalCount > 0;

        if (hasActivity)
        {
            CrabIcon.IsAnimating = ViewModel.IsProcessing;
            SpinnerText.Visibility = Visibility.Visible;
            SpinnerText.Foreground = ViewModel.PendingApprovalCount > 0
                ? new SolidColorBrush(Color.FromRgb(255, 180, 0))
                : new SolidColorBrush(Color.FromRgb(217, 120, 87));
            _spinnerTimer?.Start();

            // Position crab/spinner within collapsed clip when not expanded
            if (!isOpened)
            {
                CrabTranslate.X = ActivityOffset + 3;
                SpinnerTranslate.X = -ActivityOffset;
            }
        }
        else
        {
            CrabIcon.IsAnimating = false;
            SpinnerText.Visibility = Visibility.Collapsed;
            _spinnerTimer?.Stop();
        }
    }

    // --- Chat view ---
    private static readonly SolidColorBrush GreenDot = new(Color.FromRgb(100, 200, 100));
    private static readonly SolidColorBrush OrangeDot = new(Color.FromRgb(217, 120, 87));
    private static readonly SolidColorBrush WhiteDot = new(Color.FromRgb(180, 180, 180));
    private int _lastChatItemCount;

    private void LoadChatMessages(Models.SessionState session)
    {
        ChatMessages.Children.Clear();
        var items = ConversationParser.Parse(session.SessionId, session.Cwd);
        _lastChatItemCount = items.Count;
        foreach (var item in items) ChatMessages.Children.Add(CreateMessageElement(item));
        UpdateApprovalBar(session);
        if (session.Phase.Kind is Models.SessionPhaseKind.Processing or Models.SessionPhaseKind.Compacting)
            ChatMessages.Children.Add(CreateProcessingIndicator());
        Dispatcher.BeginInvoke(() => ChatScroller.ScrollToBottom(),
            System.Windows.Threading.DispatcherPriority.Loaded);
        _chatRefreshTimer?.Start();
    }

    private void RefreshChatMessages(Models.SessionState session)
    {
        var items = ConversationParser.Parse(session.SessionId, session.Cwd);
        if (items.Count == _lastChatItemCount) return;
        _lastChatItemCount = items.Count;
        ChatMessages.Children.Clear();
        foreach (var item in items) ChatMessages.Children.Add(CreateMessageElement(item));
        var latest = App.SessionStore.GetSession(session.SessionId);
        if (latest != null)
        {
            UpdateApprovalBar(latest);
            if (latest.Phase.Kind is Models.SessionPhaseKind.Processing or Models.SessionPhaseKind.Compacting)
                ChatMessages.Children.Add(CreateProcessingIndicator());
        }
        Dispatcher.BeginInvoke(() => ChatScroller.ScrollToBottom(),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void UpdateApprovalBar(Models.SessionState session)
    {
        if (session.Phase.IsWaitingForApproval && session.Phase.Permission is { } ctx)
        { ApprovalToolName.Text = ctx.ToolName; ChatApprovalBar.Visibility = Visibility.Visible; }
        else { ChatApprovalBar.Visibility = Visibility.Collapsed; }
    }

    private UIElement CreateProcessingIndicator()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        panel.Children.Add(new Controls.SpinnerText { Foreground = OrangeDot, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = "Processing...", FontSize = 12, FontFamily = new FontFamily("Consolas"), Foreground = OrangeDot, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    private UIElement CreateMessageElement(ConversationParser.ChatItem item) => item.Type switch
    {
        ConversationParser.ItemType.User => CreateUserMessage(item.Content),
        ConversationParser.ItemType.Assistant => CreateAssistantMessage(item.Content),
        ConversationParser.ItemType.ToolCall => CreateToolCallRow(item),
        ConversationParser.ItemType.Thinking => CreateThinkingRow(item.Content),
        _ => new Grid()
    };

    private UIElement CreateUserMessage(string text) => new Border
    {
        Child = new TextBlock { Text = text, FontSize = 12, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White },
        Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(60, 2, 0, 2),
        CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
        HorizontalAlignment = HorizontalAlignment.Right
    };

    private UIElement CreateAssistantMessage(string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Ellipse { Width = 5, Height = 5, Fill = WhiteDot, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0) });
        panel.Children.Add(new TextBlock { Text = text, FontSize = 12, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)) });
        return new Border { Child = panel, Margin = new Thickness(0, 2, 60, 2), HorizontalAlignment = HorizontalAlignment.Left };
    }

    private UIElement CreateToolCallRow(ConversationParser.ChatItem item)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        row.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = item.IsCompleted ? GreenDot : OrangeDot, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        row.Children.Add(new TextBlock { Text = item.ToolName ?? "tool", FontSize = 11, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Consolas"), Foreground = item.IsCompleted ? new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(153, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = item.ToolInput ?? "", FontSize = 11, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        return row;
    }

    private UIElement CreateThinkingRow(string text) => new TextBlock
    {
        Text = $"thinking: {(text.Length > 60 ? text[..60] + "..." : text)}", FontSize = 10, FontFamily = new FontFamily("Consolas"),
        FontStyle = FontStyles.Italic, Foreground = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
        Margin = new Thickness(12, 1, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis
    };

    private void OnSessionRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is Models.SessionState session)
        { ViewModel.ShowChatCommand.Execute(session); LoadChatMessages(session); }
        e.Handled = true;
    }

    private void OnChatApprove(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentChatSession is { } s) { ViewModel.ApprovePermissionCommand.Execute(s); ChatApprovalBar.Visibility = Visibility.Collapsed; }
    }

    private void OnChatDeny(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentChatSession is { } s) { ViewModel.DenyPermissionCommand.Execute(s); ChatApprovalBar.Visibility = Visibility.Collapsed; }
    }
}
