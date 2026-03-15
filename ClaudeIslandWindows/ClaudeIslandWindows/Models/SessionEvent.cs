namespace ClaudeIslandWindows.Models;

public abstract class SessionEvent
{
    public sealed class HookReceived(HookEvent hookEvent) : SessionEvent
    {
        public HookEvent HookEvent { get; } = hookEvent;
    }

    public sealed class PermissionApproved(string sessionId, string toolUseId) : SessionEvent
    {
        public string SessionId { get; } = sessionId;
        public string ToolUseId { get; } = toolUseId;
    }

    public sealed class PermissionDenied(string sessionId, string toolUseId, string reason) : SessionEvent
    {
        public string SessionId { get; } = sessionId;
        public string ToolUseId { get; } = toolUseId;
        public string Reason { get; } = reason;
    }

    public sealed class SessionEnded(string sessionId) : SessionEvent
    {
        public string SessionId { get; } = sessionId;
    }
}
