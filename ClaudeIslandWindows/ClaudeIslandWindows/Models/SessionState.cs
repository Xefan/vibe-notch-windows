namespace ClaudeIslandWindows.Models;

public sealed class SessionState
{
    public string SessionId { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public int? Pid { get; set; }
    public SessionPhase Phase { get; set; } = SessionPhase.Idle;
    public string? LastMessage { get; set; }
    public string? LastToolName { get; set; }
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// True once this session has received a UserPromptSubmit event.
    /// Main sessions always have one; subagents don't.
    public bool HasUserPrompt { get; set; }

    public static string DeriveProjectName(string cwd)
    {
        if (string.IsNullOrEmpty(cwd)) return "Unknown";
        return Path.GetFileName(cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "Unknown";
    }

    public int SortPriority => Phase.Kind switch
    {
        SessionPhaseKind.WaitingForApproval => 0,
        SessionPhaseKind.Processing => 1,
        SessionPhaseKind.WaitingForInput => 2,
        SessionPhaseKind.Compacting => 3,
        SessionPhaseKind.Idle => 4,
        SessionPhaseKind.Ended => 5,
        _ => 6
    };
}
