namespace ClaudeIslandWindows.Models;

public enum SessionPhaseKind
{
    Idle,
    Processing,
    WaitingForApproval,
    WaitingForInput,
    Compacting,
    Ended
}

public sealed class SessionPhase
{
    public SessionPhaseKind Kind { get; }
    public PermissionContext? Permission { get; }

    private SessionPhase(SessionPhaseKind kind, PermissionContext? permission = null)
    {
        Kind = kind;
        Permission = permission;
    }

    public static SessionPhase Idle { get; } = new(SessionPhaseKind.Idle);
    public static SessionPhase Processing { get; } = new(SessionPhaseKind.Processing);
    public static SessionPhase WaitingForInput { get; } = new(SessionPhaseKind.WaitingForInput);
    public static SessionPhase Compacting { get; } = new(SessionPhaseKind.Compacting);
    public static SessionPhase Ended { get; } = new(SessionPhaseKind.Ended);

    public static SessionPhase WaitingForApproval(PermissionContext ctx) =>
        new(SessionPhaseKind.WaitingForApproval, ctx);

    public bool NeedsAttention => Kind is SessionPhaseKind.WaitingForApproval or SessionPhaseKind.WaitingForInput;
    public bool IsActive => Kind is not (SessionPhaseKind.Idle or SessionPhaseKind.Ended);
    public bool IsWaitingForApproval => Kind == SessionPhaseKind.WaitingForApproval;

    public bool CanTransition(SessionPhase target)
    {
        if (Kind == SessionPhaseKind.Ended) return false;
        if (Kind == target.Kind && Kind == SessionPhaseKind.Idle) return false;
        return true;
    }
}

public sealed class PermissionContext
{
    public string ToolUseId { get; init; } = "";
    public string ToolName { get; init; } = "unknown";
    public Dictionary<string, object>? ToolInput { get; init; }
    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;
}
