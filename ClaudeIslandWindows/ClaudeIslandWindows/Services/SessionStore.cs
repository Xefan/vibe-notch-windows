using ClaudeIslandWindows.Models;

namespace ClaudeIslandWindows.Services;

public sealed class SessionStore
{
    private readonly Dictionary<string, SessionState> _sessions = new();
    private readonly object _lock = new();

    public event Action? SessionsChanged;

    public List<SessionState> GetSessions()
    {
        lock (_lock)
        {
            return _sessions.Values
                .Where(s => s.Phase.Kind != SessionPhaseKind.Ended)
                .OrderBy(s => s.SortPriority)
                .ThenByDescending(s => s.LastActivity)
                .ToList();
        }
    }

    public SessionState? GetSession(string sessionId)
    {
        lock (_lock)
            return _sessions.GetValueOrDefault(sessionId);
    }

    public void ProcessEvent(SessionEvent evt)
    {
        lock (_lock)
        {
            switch (evt)
            {
                case SessionEvent.HookReceived hr:
                    ProcessHookEvent(hr.HookEvent);
                    break;
                case SessionEvent.PermissionApproved pa:
                    if (_sessions.TryGetValue(pa.SessionId, out var approved))
                    {
                        approved.Phase = SessionPhase.Processing;
                        approved.LastActivity = DateTime.UtcNow;
                    }
                    break;
                case SessionEvent.PermissionDenied pd:
                    if (_sessions.TryGetValue(pd.SessionId, out var denied))
                    {
                        denied.Phase = SessionPhase.Processing;
                        denied.LastActivity = DateTime.UtcNow;
                    }
                    break;
                case SessionEvent.SessionEnded se:
                    if (_sessions.TryGetValue(se.SessionId, out var ended))
                    {
                        ended.Phase = SessionPhase.Ended;
                        ended.LastActivity = DateTime.UtcNow;
                    }
                    break;
            }
        }
        SessionsChanged?.Invoke();
    }

    private void ProcessHookEvent(HookEvent hookEvent)
    {
        if (!_sessions.TryGetValue(hookEvent.SessionId, out var session))
        {
            session = new SessionState
            {
                SessionId = hookEvent.SessionId,
                Cwd = hookEvent.Cwd,
                ProjectName = SessionState.DeriveProjectName(hookEvent.Cwd),
                Pid = hookEvent.Pid,
                CreatedAt = DateTime.UtcNow
            };
            _sessions[hookEvent.SessionId] = session;
        }

        var newPhase = hookEvent.DeterminePhase();
        if (!session.Phase.CanTransition(newPhase)) return;

        session.Phase = newPhase;
        session.LastActivity = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(hookEvent.Cwd))
        {
            session.Cwd = hookEvent.Cwd;
            session.ProjectName = SessionState.DeriveProjectName(hookEvent.Cwd);
        }
        if (hookEvent.Pid.HasValue)
            session.Pid = hookEvent.Pid;

        if (newPhase.Kind == SessionPhaseKind.Ended)
        {
            _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ =>
            {
                lock (_lock) { _sessions.Remove(hookEvent.SessionId); }
                SessionsChanged?.Invoke();
            });
        }
    }
}
