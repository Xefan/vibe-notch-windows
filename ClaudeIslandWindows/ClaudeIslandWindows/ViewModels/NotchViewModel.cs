using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeIslandWindows.Models;
using ClaudeIslandWindows.Services;

namespace ClaudeIslandWindows.ViewModels;

public enum NotchStatus { Closed, Opened, Popping }
public enum ContentType { Instances, Menu, Chat }

public partial class NotchViewModel : ObservableObject
{
    [ObservableProperty] private NotchStatus _status = NotchStatus.Closed;
    [ObservableProperty] private ContentType _currentContent = ContentType.Instances;
    [ObservableProperty] private bool _isHovered;
    [ObservableProperty] private double _panelWidth = ClosedWidth;
    [ObservableProperty] private double _panelHeight = ClosedHeight;
    [ObservableProperty] private bool _hasActiveSessions;
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private bool _hasWaitingForInput;
    [ObservableProperty] private int _pendingApprovalCount;

    public ObservableCollection<SessionState> Sessions { get; } = new();

    public const double ClosedWidth = 196;
    public const double ClosedHeight = 32;
    private const double InstancesWidth = 480;
    private const double InstancesHeight = 320;
    private const double MenuWidth = 480;
    private const double MenuHeight = 420;
    private const double ChatWidth = 600;
    private const double ChatHeight = 580;

    [ObservableProperty] private SessionState? _currentChatSession;

    private DispatcherTimer? _hoverTimer;
    private DispatcherTimer? _popTimer;

    public void Initialize()
    {
        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _hoverTimer.Tick += (_, _) => { _hoverTimer.Stop(); Open(); };

        _popTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _popTimer.Tick += (_, _) => { _popTimer.Stop(); Close(); };

        // Boot animation
        Status = NotchStatus.Popping;
        var bootTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        bootTimer.Tick += (_, _) => { bootTimer.Stop(); Close(); };
        bootTimer.Start();
    }

    public void RefreshSessions(SessionStore store)
    {
        var updated = store.GetSessions();
        Sessions.Clear();
        foreach (var s in updated) Sessions.Add(s);

        PendingApprovalCount = updated.Count(s => s.Phase.IsWaitingForApproval);
        HasActiveSessions = updated.Any(s => s.Phase.IsActive);
        IsProcessing = updated.Any(s => s.Phase.Kind is SessionPhaseKind.Processing or SessionPhaseKind.Compacting);
        HasWaitingForInput = updated.Any(s => s.Phase.Kind == SessionPhaseKind.WaitingForInput);

        if (PendingApprovalCount > 0 && Status == NotchStatus.Closed)
            Pop();
    }

    public void OnMouseEnter()
    {
        IsHovered = true;
        if (Status == NotchStatus.Closed)
            _hoverTimer?.Start();
    }

    public void OnMouseLeave()
    {
        IsHovered = false;
        _hoverTimer?.Stop();
        if (Status == NotchStatus.Opened)
            Close();
    }

    [RelayCommand]
    private void Toggle()
    {
        if (Status == NotchStatus.Closed || Status == NotchStatus.Popping)
            Open();
        else
            Close();
    }

    [RelayCommand]
    private void Open()
    {
        _hoverTimer?.Stop();
        Status = NotchStatus.Opened;
        CurrentContent = ContentType.Instances;
        UpdateDimensions();
    }

    [RelayCommand]
    private void Close()
    {
        Status = NotchStatus.Closed;
        PanelWidth = ClosedWidth;
        PanelHeight = ClosedHeight;
    }

    [RelayCommand]
    private void ToggleMenu()
    {
        CurrentContent = CurrentContent == ContentType.Menu ? ContentType.Instances : ContentType.Menu;
        UpdateDimensions();
    }

    [RelayCommand]
    private void ShowChat(SessionState session)
    {
        CurrentChatSession = session;
        CurrentContent = ContentType.Chat;
        UpdateDimensions();
    }

    [RelayCommand]
    private void ExitChat()
    {
        CurrentChatSession = null;
        CurrentContent = ContentType.Instances;
        UpdateDimensions();
    }

    [RelayCommand]
    private void ApprovePermission(SessionState session)
    {
        if (session.Phase.Permission is { } ctx)
        {
            App.PipeServer.RespondToPermission(ctx.ToolUseId, "allow");
            App.SessionStore.ProcessEvent(
                new SessionEvent.PermissionApproved(session.SessionId, ctx.ToolUseId));
            RefreshSessions(App.SessionStore);
        }
    }

    [RelayCommand]
    private void DenyPermission(SessionState session)
    {
        if (session.Phase.Permission is { } ctx)
        {
            App.PipeServer.RespondToPermission(ctx.ToolUseId, "deny", "Denied by user via Claude Island");
            App.SessionStore.ProcessEvent(
                new SessionEvent.PermissionDenied(session.SessionId, ctx.ToolUseId, "Denied by user"));
            RefreshSessions(App.SessionStore);
        }
    }

    public void Pop()
    {
        if (Status == NotchStatus.Opened) return;
        Status = NotchStatus.Popping;
        _popTimer?.Start();
    }

    private void UpdateDimensions()
    {
        (PanelWidth, PanelHeight) = CurrentContent switch
        {
            ContentType.Instances => (InstancesWidth, InstancesHeight),
            ContentType.Menu => (MenuWidth, MenuHeight),
            _ => (InstancesWidth, InstancesHeight)
        };
    }
}
