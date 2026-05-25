using ClaudeIslandWindows.Models;

namespace ClaudeIslandWindows.Services;

public sealed class SessionStore
{
    private readonly Dictionary<string, SessionState> _sessions = new();
    private readonly object _lock = new();

    public event Action? SessionsChanged;

    // If a session has been in Processing/Compacting without any events for this long,
    // treat it as a zombie (crashed Claude Code instance that never fired SessionEnd).
    private static readonly TimeSpan StaleProcessingTimeout = TimeSpan.FromMinutes(2);

    public List<SessionState> GetSessions()
    {
        lock (_lock)
        {
            // Demote stale processing sessions to Idle so they don't pin IsProcessing=true
            var now = DateTime.UtcNow;
            foreach (var s in _sessions.Values)
            {
                if (s.Phase.Kind is SessionPhaseKind.Processing or SessionPhaseKind.Compacting
                    && now - s.LastActivity > StaleProcessingTimeout)
                {
                    s.Phase = SessionPhase.Idle;
                }
            }

            return _sessions.Values
                .Where(s => s.Phase.Kind != SessionPhaseKind.Ended)
                .Where(s => s.HasUserPrompt) // exclude subagent sessions
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

        // Track whether this session has ever received a UserPromptSubmit
        // (main sessions have; subagents don't — they're spawned via tool calls)
        if (hookEvent.Event == "UserPromptSubmit")
            session.HasUserPrompt = true;

        var oldPhase = session.Phase.Kind;
        session.Phase = newPhase;
        session.LastActivity = DateTime.UtcNow;

        // Play notification sound on transitions that need user attention
        // (only for main sessions — subagent state changes shouldn't notify)
        if (session.HasUserPrompt &&
            oldPhase != newPhase.Kind &&
            newPhase.Kind is SessionPhaseKind.WaitingForApproval or SessionPhaseKind.WaitingForInput)
        {
            SoundPlayer.PlayCurrent();
        }

        if (!string.IsNullOrEmpty(hookEvent.Cwd))
        {
            session.Cwd = hookEvent.Cwd;
            session.ProjectName = SessionState.DeriveProjectName(hookEvent.Cwd);
        }
        if (hookEvent.Pid is > 0)
            session.Pid = hookEvent.Pid;

        // Track last tool for display
        if (hookEvent.Tool != null)
            session.LastToolName = hookEvent.Tool;

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
