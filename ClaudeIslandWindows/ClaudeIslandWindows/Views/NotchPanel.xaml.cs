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

        // Use HitTarget for hover (matches visible notch size, not full ContentGrid)
        HitTarget.MouseEnter += (_, _) =>
        {
            if (_suppressIndicators) return;
            ViewModel.OnMouseEnter();
        };
        HitTarget.MouseLeave += (_, _) =>
        {
            ViewModel.OnMouseLeave();
        };

        // Set initial clip with notch shape
        UpdateClipRect(isFinal: true);

        // Suppress activity indicators at startup
        _suppressIndicators = true;
        var startupTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        startupTimer.Tick += (_, _) =>
        {
            _suppressIndicators = false;
            startupTimer.Stop();
            UpdateStatusIndicators();
        };
        startupTimer.Start();
    }

    private bool _suppressIndicators = true;

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
        HitTarget.Width = _clipWidth;
        HitTarget.Height = _clipHeight;

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

        // Disable HitTarget so clicks pass through to content
        HitTarget.IsHitTestVisible = false;

        // Let UpdateStatusIndicators handle crab/spinner visibility
        UpdateStatusIndicators();
    }

    private void AnimateClose()
    {
        _isClosing = true;

        ContentArea.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(120))));
        MenuButton.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(120))));

        // Slide crab/spinner into activity positions if there's activity
        var hasActivity = ViewModel.IsProcessing || ViewModel.PendingApprovalCount > 0;
        if (hasActivity)
        {
            // Show "?" for approvals during close
            if (ViewModel.PendingApprovalCount > 0)
            {
                _spinnerTimer?.Stop();
                SpinnerText.Text = "?";
                SpinnerText.Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 0));
            }
            SpinnerText.Visibility = Visibility.Visible;
            SetValue(CrabScale, ScaleTransform.ScaleXProperty, 1);
            SetValue(CrabScale, ScaleTransform.ScaleYProperty, 1);

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
                HitTarget.IsHitTestVisible = true;
                _gridWidth = 480;
                _gridHeight = 320;
                ContentGrid.Width = 480;
                ContentGrid.Height = 320;
                _isClosing = false;
                // Sync final state (clears animation holds)
                UpdateStatusIndicators();
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

    // True while the close animation is running — suppresses direct position snapping
    private bool _isClosing;

    /// Helper: clear any WPF animation hold then set a property directly.
    private static void SetValue(DependencyObject obj, DependencyProperty dp, double value)
    {
        if (obj is IAnimatable a)
            a.BeginAnimation(dp, null);
        obj.SetValue(dp, value);
    }

    private void UpdateStatusIndicators()
    {
        if (_suppressIndicators)
        {
            SetValue(CrabScale, ScaleTransform.ScaleXProperty, 0);
            SetValue(CrabScale, ScaleTransform.ScaleYProperty, 0);
            CrabIcon.IsAnimating = false;
            SpinnerText.Visibility = Visibility.Collapsed;
            _spinnerTimer?.Stop();
            return;
        }

        // During close animation, only update non-positional state (colors, text, animation).
        // Positions are handled by AnimateClose's BeginAnimation calls.
        if (_isClosing) return;

        var isOpened = ViewModel.Status == ViewModels.NotchStatus.Opened;
        var hasActivity = ViewModel.IsProcessing || ViewModel.PendingApprovalCount > 0;
        var hasApproval = ViewModel.PendingApprovalCount > 0;

        // Crab visibility
        if (hasActivity || isOpened)
        {
            SetValue(CrabScale, ScaleTransform.ScaleXProperty, 1);
            SetValue(CrabScale, ScaleTransform.ScaleYProperty, 1);
            CrabIcon.IsAnimating = hasActivity && ViewModel.IsProcessing;

            if (hasActivity && !isOpened)
                SetValue(CrabTranslate, TranslateTransform.XProperty, ActivityOffset + 3);
        }
        else
        {
            SetValue(CrabScale, ScaleTransform.ScaleXProperty, 0);
            SetValue(CrabScale, ScaleTransform.ScaleYProperty, 0);
            CrabIcon.IsAnimating = false;
        }

        // Spinner visibility
        if (hasActivity)
        {
            SpinnerText.Visibility = Visibility.Visible;

            if (hasApproval)
            {
                // Waiting for approval: show static "?" in amber
                _spinnerTimer?.Stop();
                SpinnerText.Text = "?";
                SpinnerText.Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 0));
            }
            else
            {
                // Processing: animate spinner symbols
                SpinnerText.Foreground = new SolidColorBrush(Color.FromRgb(217, 120, 87));
                _spinnerTimer?.Start();
            }

            if (!isOpened)
                SetValue(SpinnerTranslate, TranslateTransform.XProperty, -ActivityOffset);
        }
        else
        {
            SpinnerText.Visibility = Visibility.Collapsed;
            _spinnerTimer?.Stop();
        }
    }

    // --- Chat view ---
    private static readonly SolidColorBrush WhiteDot = new(Color.FromArgb(153, 255, 255, 255));
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
        panel.Children.Add(new Controls.SpinnerText { Foreground = TerminalColors.Prompt, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = "Processing...", FontSize = 12, FontFamily = new FontFamily("Consolas"), Foreground = TerminalColors.Prompt, VerticalAlignment = VerticalAlignment.Center });
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

    private UIElement CreateUserMessage(string text)
    {
        var content = MarkdownRenderer.Render(text, 12);
        return new Border
        {
            Child = content,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(60, 3, 0, 3),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
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
            Margin = new Thickness(0, 6, 8, 0)
        };

        var content = MarkdownRenderer.Render(text, 12);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(dot, 0);
        Grid.SetColumn(content, 1);
        grid.Children.Add(dot);
        grid.Children.Add(content);

        return new Border
        {
            Child = grid,
            Margin = new Thickness(0, 3, 40, 3),
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    private UIElement CreateToolCallRow(ConversationParser.ChatItem item)
    {
        var dotColor = item.IsCompleted ? TerminalColors.Green : TerminalColors.Prompt;
        var textColor = item.IsCompleted ? TerminalColors.Text70 : TerminalColors.Text40;

        var container = new StackPanel { Margin = new Thickness(0, 1, 0, 1) };

        // Header row: dot + name + input + chevron
        var header = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Hand };

        header.Children.Add(new Ellipse
        {
            Width = 6, Height = 6,
            Fill = dotColor,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });

        header.Children.Add(new TextBlock
        {
            Text = item.ToolName ?? "tool",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Consolas"),
            Foreground = textColor,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (!string.IsNullOrEmpty(item.ToolInput))
        {
            header.Children.Add(new TextBlock
            {
                Text = item.ToolInput,
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                Foreground = TerminalColors.Dim,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 300
            });
        }

        // Edit tools: always show diff inline
        if (item.ToolName == "Edit" && (item.OldString != null || item.NewString != null))
        {
            container.Children.Add(header);
            container.Children.Add(CreateDiffView(
                item.OldString ?? "", item.NewString ?? "",
                item.FilePath != null ? System.IO.Path.GetFileName(item.FilePath) : null));
            container.MouseEnter += (_, _) => container.Background = new SolidColorBrush(Color.FromArgb(13, 255, 255, 255));
            container.MouseLeave += (_, _) => container.Background = Brushes.Transparent;
            return container;
        }

        // Chevron for expandable tools (only completed, not Edit/Task)
        if (item.IsCompleted && !string.IsNullOrEmpty(item.ToolResult) &&
            item.ToolName is not "Edit" and not "Task")
        {
            var chevron = new TextBlock
            {
                Text = "›",
                FontSize = 11,
                Foreground = TerminalColors.Dimmer,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            header.Children.Add(chevron);

            // Result panel (collapsed by default)
            var resultPanel = new Border
            {
                Background = TerminalColors.CodeBg,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(12, 4, 0, 2),
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text = item.ToolResult.Length > 500 ? item.ToolResult[..500] + "..." : item.ToolResult,
                    FontSize = 11,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = TerminalColors.Text70,
                    TextWrapping = TextWrapping.Wrap
                }
            };

            header.MouseLeftButtonDown += (_, e) =>
            {
                if (resultPanel.Visibility == Visibility.Collapsed)
                {
                    resultPanel.Visibility = Visibility.Visible;
                    chevron.Text = "⌄";
                }
                else
                {
                    resultPanel.Visibility = Visibility.Collapsed;
                    chevron.Text = "›";
                }
                e.Handled = true;
            };

            container.Children.Add(header);
            container.Children.Add(resultPanel);
        }
        else
        {
            container.Children.Add(header);
        }

        // Add hover effect
        container.MouseEnter += (_, _) => container.Background = new SolidColorBrush(Color.FromArgb(13, 255, 255, 255));
        container.MouseLeave += (_, _) => container.Background = Brushes.Transparent;

        return container;
    }

    private UIElement CreateThinkingRow(string text)
    {
        var isExpandable = text.Length > 80;
        var preview = isExpandable ? text[..80] + "..." : text;

        var container = new StackPanel { Margin = new Thickness(0, 1, 0, 1) };

        var header = new StackPanel { Orientation = Orientation.Horizontal };

        var dot = new Ellipse
        {
            Width = 5, Height = 5,
            Fill = TerminalColors.Dimmer,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        header.Children.Add(dot);

        var label = new TextBlock
        {
            Text = $"thinking: {preview}",
            FontSize = 10,
            FontFamily = new FontFamily("Consolas"),
            FontStyle = FontStyles.Italic,
            Foreground = TerminalColors.Dim,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        header.Children.Add(label);

        if (isExpandable)
        {
            var chevron = new TextBlock
            {
                Text = "›",
                FontSize = 10,
                Foreground = TerminalColors.Dimmer,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Children.Add(chevron);
            header.Cursor = Cursors.Hand;

            var fullText = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontFamily = new FontFamily("Consolas"),
                FontStyle = FontStyles.Italic,
                Foreground = TerminalColors.Dim,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(11, 2, 0, 2),
                Visibility = Visibility.Collapsed
            };

            header.MouseLeftButtonDown += (_, e) =>
            {
                if (fullText.Visibility == Visibility.Collapsed)
                {
                    fullText.Visibility = Visibility.Visible;
                    label.Visibility = Visibility.Collapsed;
                    chevron.Text = "⌄";
                }
                else
                {
                    fullText.Visibility = Visibility.Collapsed;
                    label.Visibility = Visibility.Visible;
                    chevron.Text = "›";
                }
                e.Handled = true;
            };

            container.Children.Add(header);
            container.Children.Add(fullText);
        }
        else
        {
            container.Children.Add(header);
        }

        return container;
    }

    private UIElement CreateDiffView(string oldStr, string newStr, string? filename)
    {
        var panel = new StackPanel { Margin = new Thickness(12, 4, 0, 4) };

        // Filename header
        if (filename != null)
        {
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal };
            headerRow.Children.Add(new TextBlock
            {
                Text = "📄 ",
                FontSize = 10,
                Foreground = TerminalColors.Dim,
                VerticalAlignment = VerticalAlignment.Center
            });
            headerRow.Children.Add(new TextBlock
            {
                Text = filename,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Consolas"),
                Foreground = TerminalColors.Text70
            });
            var headerBorder = new Border
            {
                Child = headerRow,
                Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(6, 6, 0, 0)
            };
            panel.Children.Add(headerBorder);
        }

        // Compute diff using LCS
        var oldLines = oldStr.Split('\n');
        var newLines = newStr.Split('\n');
        var diffLines = ComputeDiff(oldLines, newLines);

        var maxLines = 12;
        var count = 0;
        foreach (var (text, type, lineNum) in diffLines)
        {
            if (count >= maxLines) break;

            var isRemoved = type == '-';
            var isAdded = type == '+';

            var bg = isRemoved ? Color.FromArgb(20, 255, 60, 60)    // faint red
                   : isAdded  ? Color.FromArgb(20, 60, 255, 60)    // faint green
                   : Color.FromArgb(0, 0, 0, 0);

            var textColor = isRemoved ? Color.FromRgb(255, 120, 120)  // red text
                          : isAdded  ? Color.FromRgb(120, 255, 120)  // green text
                          : Color.FromRgb(180, 180, 180);

            var lineRow = new StackPanel { Orientation = Orientation.Horizontal };

            // Line number
            lineRow.Children.Add(new TextBlock
            {
                Text = lineNum.ToString(),
                FontSize = 10,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromArgb((byte)(isRemoved || isAdded ? 153 : 80), textColor.R, textColor.G, textColor.B)),
                Width = 28,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 0, 4, 0)
            });

            // +/- indicator
            lineRow.Children.Add(new TextBlock
            {
                Text = type == '+' ? "+" : type == '-' ? "-" : " ",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(textColor),
                Width = 14
            });

            // Line content
            lineRow.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(textColor),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var lineBorder = new Border
            {
                Child = lineRow,
                Background = new SolidColorBrush(bg),
                Padding = new Thickness(0, 1, 0, 1)
            };

            panel.Children.Add(lineBorder);
            count++;
        }

        if (diffLines.Count > maxLines)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "...",
                FontSize = 10,
                FontFamily = new FontFamily("Consolas"),
                Foreground = TerminalColors.Dimmer,
                Margin = new Thickness(46, 2, 0, 2)
            });
        }

        return panel;
    }

    private static List<(string Text, char Type, int LineNum)> ComputeDiff(string[] oldLines, string[] newLines)
    {
        // LCS-based diff
        var m = oldLines.Length;
        var n = newLines.Length;
        var dp = new int[m + 1, n + 1];

        for (int i = 1; i <= m; i++)
            for (int j = 1; j <= n; j++)
                dp[i, j] = oldLines[i - 1] == newLines[j - 1]
                    ? dp[i - 1, j - 1] + 1
                    : Math.Max(dp[i - 1, j], dp[i, j - 1]);

        // Backtrack to find LCS
        var lcs = new List<string>();
        int ii = m, jj = n;
        while (ii > 0 && jj > 0)
        {
            if (oldLines[ii - 1] == newLines[jj - 1])
            {
                lcs.Add(oldLines[ii - 1]);
                ii--; jj--;
            }
            else if (dp[ii - 1, jj] > dp[ii, jj - 1]) ii--;
            else jj--;
        }
        lcs.Reverse();

        // Build diff lines
        var result = new List<(string, char, int)>();
        int oldIdx = 0, newIdx = 0, lcsIdx = 0;

        while (oldIdx < m || newIdx < n)
        {
            var lcsLine = lcsIdx < lcs.Count ? lcs[lcsIdx] : null;

            if (oldIdx < m && (lcsLine == null || oldLines[oldIdx] != lcsLine))
            {
                result.Add((oldLines[oldIdx], '-', oldIdx + 1));
                oldIdx++;
            }
            else if (newIdx < n && (lcsLine == null || newLines[newIdx] != lcsLine))
            {
                result.Add((newLines[newIdx], '+', newIdx + 1));
                newIdx++;
            }
            else
            {
                oldIdx++; newIdx++; lcsIdx++;
            }
        }

        return result;
    }

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
