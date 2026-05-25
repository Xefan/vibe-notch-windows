using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeIslandWindows.Models;

namespace ClaudeIslandWindows.BuddyBridge;

public sealed class PromptDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("tool")] public string Tool { get; set; } = "";
    [JsonPropertyName("hint")] public string Hint { get; set; } = "";
}

public sealed class HeartbeatDto
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("running")] public int Running { get; set; }
    [JsonPropertyName("waiting")] public int Waiting { get; set; }
    [JsonPropertyName("completed")] public bool Completed { get; set; }
    [JsonPropertyName("msg")] public string Msg { get; set; } = "idle";
    [JsonPropertyName("entries")] public string[] Entries { get; set; } = Array.Empty<string>();
    // tokens / tokens_today are always serialized (even when 0) so the firmware can
    // baseline its delta accounting on the first heartbeat. See BUG_TOKEN_OVERCOUNTING.md.
    [JsonPropertyName("tokens")] public uint Tokens { get; set; }
    [JsonPropertyName("tokens_today")] public uint TokensToday { get; set; }
    [JsonPropertyName("prompt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PromptDto? Prompt { get; set; }
}

/// Aggregates SessionState[] + side state into a heartbeat object per PROTOCOL.md.
public static class HeartbeatBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static HeartbeatDto Build(
        IReadOnlyList<SessionState> sessions,
        IReadOnlyList<string> entries,
        bool completedFlag,
        uint tokens,
        uint tokensToday,
        PromptDto? prompt)
    {
        int total = sessions.Count;
        int running = sessions.Count(s => s.Phase.Kind is SessionPhaseKind.Processing
                                                       or SessionPhaseKind.Compacting);
        int waiting = sessions.Count(s => s.Phase.Kind == SessionPhaseKind.WaitingForApproval);

        var currentTool = sessions
            .FirstOrDefault(s => s.Phase.Kind == SessionPhaseKind.Processing && s.LastToolName != null)
            ?.LastToolName;
        var approveTool = sessions
            .FirstOrDefault(s => s.Phase.Kind == SessionPhaseKind.WaitingForApproval)
            ?.LastToolName ?? prompt?.Tool;
        var anyCompacting = sessions.Any(s => s.Phase.Kind == SessionPhaseKind.Compacting);
        var anyWaitingInput = sessions.Any(s => s.Phase.Kind == SessionPhaseKind.WaitingForInput);

        string msg;
        if (waiting > 0)         msg = $"approve: {approveTool ?? "tool"}";
        else if (anyCompacting)  msg = "compacting";
        else if (running > 0)    msg = currentTool != null ? $"running: {currentTool}" : "thinking";
        else if (anyWaitingInput)msg = "waiting";
        else if (total > 0)      msg = "ready";
        else                     msg = "idle";

        return new HeartbeatDto
        {
            Total = total,
            Running = running,
            Waiting = waiting,
            Completed = completedFlag,
            Msg = EntryFormatter.Truncate(msg, 63),
            Entries = entries.Take(6).ToArray(),
            Tokens = tokens,
            TokensToday = tokensToday,
            Prompt = prompt
        };
    }

    public static string Serialize(HeartbeatDto hb) =>
        JsonSerializer.Serialize(hb, JsonOpts);
}
