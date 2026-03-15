using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeIslandWindows.Models;

public sealed class HookEvent
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = "";

    [JsonPropertyName("event")]
    public string Event { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("pid")]
    public int? Pid { get; set; }

    [JsonPropertyName("tty")]
    public string? Tty { get; set; }

    [JsonPropertyName("tool")]
    public string? Tool { get; set; }

    [JsonPropertyName("tool_input")]
    public JsonElement? ToolInput { get; set; }

    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; set; }

    [JsonPropertyName("notification_type")]
    public string? NotificationType { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    public SessionPhase DeterminePhase()
    {
        if (Event == "PreCompact")
            return SessionPhase.Compacting;

        return Status switch
        {
            "waiting_for_approval" => SessionPhase.WaitingForApproval(new PermissionContext
            {
                ToolUseId = ToolUseId ?? "",
                ToolName = Tool ?? "unknown",
                ToolInput = DeserializeToolInput(),
                ReceivedAt = DateTime.UtcNow
            }),
            "waiting_for_input" => SessionPhase.WaitingForInput,
            "running_tool" or "processing" or "starting" => SessionPhase.Processing,
            "compacting" => SessionPhase.Compacting,
            "ended" => SessionPhase.Ended,
            _ => SessionPhase.Idle
        };
    }

    public bool ExpectsResponse => Event == "PermissionRequest" && Status == "waiting_for_approval";

    private Dictionary<string, object>? DeserializeToolInput()
    {
        if (ToolInput is not { } element || element.ValueKind == JsonValueKind.Undefined)
            return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(element.GetRawText());
        }
        catch { return null; }
    }
}

public sealed class HookResponse
{
    [JsonPropertyName("decision")]
    public string Decision { get; set; } = "ask";

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}
