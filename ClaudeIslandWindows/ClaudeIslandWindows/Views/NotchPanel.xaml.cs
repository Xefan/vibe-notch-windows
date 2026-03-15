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

    // Matching macOS: · ✢ ✳ ∗ ✻ ✽ — star/asterisk variants creating a twinkle effect
    private static readonly string[] SpinnerSymbols = ["·", "✳", "∗", "✶", "✳", "∗"];
    private int _spinnerPhase;
    private System.Windows.Threading.DispatcherTimer? _spinnerTimer;

    private ViewModels.NotchViewModel ViewModel => App.NotchViewModel;

    public NotchPanel()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelChanged;

        _spinnerTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _spinnerTimer.Tick += (_, _) =>
        {
            _spinnerPhase = (_spinnerPhase + 1) % SpinnerSymbols.Length;
            SpinnerText.Text = SpinnerSymbols[_spinnerPhase];
        };
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.NotchViewModel.Status))
            Dispatcher.BeginInvoke(() => AnimateToState(ViewModel.Status));
        else if (e.PropertyName == nameof(ViewModels.NotchViewModel.CurrentContent)
                 && ViewModel.Status == ViewModels.NotchStatus.Opened)
        {
            Dispatcher.BeginInvoke(AnimateContentSwitch);
            if (ViewModel.CurrentContent == ViewModels.ContentType.Chat && ViewModel.CurrentChatSession is { } session)
                Dispatcher.BeginInvoke(() => LoadChatMessages(session));
        }
        else if (e.PropertyName is nameof(ViewModels.NotchViewModel.HasActiveSessions)
                 or nameof(ViewModels.NotchViewModel.IsProcessing)
                 or nameof(ViewModels.NotchViewModel.PendingApprovalCount))
            Dispatcher.BeginInvoke(UpdateStatusIndicators);
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
        ViewModels.ContentType.Chat => (600.0, 580.0),
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

    private void UpdateStatusIndicators()
    {
        if (ViewModel.IsProcessing || ViewModel.PendingApprovalCount > 0)
        {
            CrabIcon.Visibility = Visibility.Visible;
            CrabIcon.IsAnimating = ViewModel.IsProcessing;
            SpinnerText.Visibility = Visibility.Visible;
            SpinnerText.Foreground = ViewModel.PendingApprovalCount > 0
                ? new SolidColorBrush(Color.FromRgb(255, 180, 0))   // Amber for approval
                : new SolidColorBrush(Color.FromRgb(217, 120, 87)); // Claude orange for processing
            _spinnerTimer?.Start();
        }
        else
        {
            CrabIcon.Visibility = Visibility.Collapsed;
            CrabIcon.IsAnimating = false;
            SpinnerText.Visibility = Visibility.Collapsed;
            _spinnerTimer?.Stop();
        }
    }

    private void OnSessionRowClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is Models.SessionState session)
        {
            ViewModel.ShowChatCommand.Execute(session);
            LoadChatMessages(session);
        }
        e.Handled = true;
    }

    private static readonly SolidColorBrush GreenDot = new(Color.FromRgb(100, 200, 100));
    private static readonly SolidColorBrush OrangeDot = new(Color.FromRgb(217, 120, 87));
    private static readonly SolidColorBrush WhiteDot = new(Color.FromRgb(180, 180, 180));

    private void LoadChatMessages(Models.SessionState session)
    {
        ChatMessages.Children.Clear();
        var items = ConversationParser.Parse(session.SessionId, session.Cwd);

        foreach (var item in items)
        {
            ChatMessages.Children.Add(CreateMessageElement(item));
        }

        // Show approval bar if waiting
        if (session.Phase.IsWaitingForApproval && session.Phase.Permission is { } ctx)
        {
            ApprovalToolName.Text = ctx.ToolName;
            ChatApprovalBar.Visibility = Visibility.Visible;
        }
        else
        {
            ChatApprovalBar.Visibility = Visibility.Collapsed;
        }

        Dispatcher.BeginInvoke(() => ChatScroller.ScrollToBottom(),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private UIElement CreateMessageElement(ConversationParser.ChatItem item)
    {
        return item.Type switch
        {
            ConversationParser.ItemType.User => CreateUserMessage(item.Content),
            ConversationParser.ItemType.Assistant => CreateAssistantMessage(item.Content),
            ConversationParser.ItemType.ToolCall => CreateToolCallRow(item),
            ConversationParser.ItemType.Thinking => CreateThinkingRow(item.Content),
            _ => new Grid()
        };
    }

    private UIElement CreateUserMessage(string text)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White
        };

        return new Border
        {
            Child = textBlock,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(60, 2, 0, 2),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)), // white 15%
            HorizontalAlignment = HorizontalAlignment.Right
        };
    }

    private UIElement CreateAssistantMessage(string text)
    {
        var dot = new Ellipse
        {
            Width = 5, Height = 5,
            Fill = WhiteDot,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0)
        };

        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)) // white 90%
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(dot);
        panel.Children.Add(textBlock);

        return new Border
        {
            Child = panel,
            Margin = new Thickness(0, 2, 60, 2),
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    private UIElement CreateToolCallRow(ConversationParser.ChatItem item)
    {
        var dotColor = item.IsCompleted ? GreenDot : OrangeDot;

        var dot = new Ellipse
        {
            Width = 6, Height = 6,
            Fill = dotColor,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };

        var toolName = new TextBlock
        {
            Text = item.ToolName ?? "tool",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Consolas"),
            Foreground = item.IsCompleted
                ? new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)) // white 70%
                : new SolidColorBrush(Color.FromArgb(153, 255, 255, 255)), // white 60%
            VerticalAlignment = VerticalAlignment.Center
        };

        var inputText = new TextBlock
        {
            Text = item.ToolInput ?? "",
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), // white 40%
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 1, 0, 1)
        };
        row.Children.Add(dot);
        row.Children.Add(toolName);
        row.Children.Add(inputText);

        return row;
    }

    private UIElement CreateThinkingRow(string text)
    {
        var preview = text.Length > 60 ? text[..60] + "..." : text;

        var label = new TextBlock
        {
            Text = $"thinking: {preview}",
            FontSize = 10,
            FontFamily = new FontFamily("Consolas"),
            FontStyle = FontStyles.Italic,
            Foreground = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), // white 30%
            Margin = new Thickness(12, 1, 0, 1),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        return label;
    }

    private void OnChatInputKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && !string.IsNullOrWhiteSpace(ChatInput.Text))
        {
            SendChatMessage();
            e.Handled = true;
        }
    }

    private void OnChatSend(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ChatInput.Text))
            SendChatMessage();
    }

    private void SendChatMessage()
    {
        // TODO: Send message to Claude session via named pipe or process stdin
        // For now, just show it in the chat
        var text = ChatInput.Text.Trim();
        ChatInput.Text = "";

        ChatMessages.Children.Add(CreateUserMessage(text));

        Dispatcher.BeginInvoke(() => ChatScroller.ScrollToBottom(),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnChatApprove(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentChatSession is { } session)
        {
            ViewModel.ApprovePermissionCommand.Execute(session);
            ChatApprovalBar.Visibility = Visibility.Collapsed;
        }
    }

    private void OnChatDeny(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentChatSession is { } session)
        {
            ViewModel.DenyPermissionCommand.Execute(session);
            ChatApprovalBar.Visibility = Visibility.Collapsed;
        }
    }
}
