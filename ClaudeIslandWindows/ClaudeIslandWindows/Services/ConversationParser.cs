using System.Text.Json;

namespace ClaudeIslandWindows.Services;

public sealed class ConversationParser
{
    public enum ItemType { User, Assistant, ToolCall, Thinking }

    public record ChatItem(
        string Id,
        ItemType Type,
        string Content,
        DateTime Timestamp,
        string? ToolName = null,
        string? ToolInput = null,
        bool IsCompleted = false);

    /// <summary>
    /// Parse a Claude JSONL conversation file and return displayable chat items.
    /// </summary>
    public static List<ChatItem> Parse(string sessionId, string cwd)
    {
        var items = new List<ChatItem>();
        var completedToolIds = new HashSet<string>();
        var toolUseIds = new List<string>(); // Track order for completion matching

        var projectDir = cwd
            .Replace(":\\", "--")
            .Replace(":/", "--")
            .Replace("\\", "-")
            .Replace("/", "-")
            .Replace(".", "-")
            .Replace(":", "-");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var sessionFile = Path.Combine(home, ".claude", "projects", projectDir, sessionId + ".jsonl");

        if (!File.Exists(sessionFile)) return items;

        // First pass: collect completed tool IDs
        // Tool results appear in USER messages as tool_result blocks
        try
        {
            foreach (var line in File.ReadLines(sessionFile))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("message", out var msg)) continue;
                    if (!msg.TryGetProperty("content", out var content)) continue;
                    if (content.ValueKind != JsonValueKind.Array) continue;

                    foreach (var block in content.EnumerateArray())
                    {
                        if (block.TryGetProperty("type", out var bt) &&
                            bt.GetString() == "tool_result" &&
                            block.TryGetProperty("tool_use_id", out var tid))
                        {
                            completedToolIds.Add(tid.GetString() ?? "");
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        // Second pass: build display items
        try
        {
            foreach (var line in File.ReadLines(sessionFile))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("isMeta", out var meta) && meta.GetBoolean())
                        continue;

                    var type = root.TryGetProperty("type", out var tp) ? tp.GetString() : null;
                    if (type is not ("user" or "assistant")) continue;

                    var uuid = root.TryGetProperty("uuid", out var u) ? u.GetString() ?? "" : "";
                    var timestamp = DateTime.UtcNow;
                    if (root.TryGetProperty("timestamp", out var ts) &&
                        DateTime.TryParse(ts.GetString(), out var parsed))
                        timestamp = parsed;

                    if (!root.TryGetProperty("message", out var message)) continue;
                    if (!message.TryGetProperty("content", out var content)) continue;

                    if (content.ValueKind == JsonValueKind.String)
                    {
                        var text = content.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(text))
                            items.Add(new ChatItem(uuid, type == "user" ? ItemType.User : ItemType.Assistant,
                                text, timestamp));
                    }
                    else if (content.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var block in content.EnumerateArray())
                        {
                            if (!block.TryGetProperty("type", out var blockType)) continue;
                            var bt = blockType.GetString();

                            if (bt == "text" && block.TryGetProperty("text", out var textEl))
                            {
                                var text = textEl.GetString() ?? "";
                                if (!string.IsNullOrWhiteSpace(text))
                                    items.Add(new ChatItem($"{uuid}-text",
                                        type == "user" ? ItemType.User : ItemType.Assistant,
                                        text, timestamp));
                            }
                            else if (bt == "tool_use" && block.TryGetProperty("name", out var nameEl))
                            {
                                var toolName = nameEl.GetString() ?? "tool";
                                var toolId = block.TryGetProperty("id", out var idEl)
                                    ? idEl.GetString() ?? "" : "";
                                var inputStr = "";
                                if (block.TryGetProperty("input", out var input))
                                    inputStr = SummarizeToolInput(toolName, input);

                                var isCompleted = completedToolIds.Contains(toolId);
                                items.Add(new ChatItem(toolId, ItemType.ToolCall,
                                    inputStr, timestamp,
                                    ToolName: toolName,
                                    ToolInput: inputStr,
                                    IsCompleted: isCompleted));
                            }
                            else if (bt == "thinking" && block.TryGetProperty("thinking", out var thinkEl))
                            {
                                var text = thinkEl.GetString() ?? "";
                                if (!string.IsNullOrWhiteSpace(text))
                                    items.Add(new ChatItem($"{uuid}-think", ItemType.Thinking,
                                        text, timestamp));
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        return items;
    }

    private static string SummarizeToolInput(string toolName, JsonElement input)
    {
        try
        {
            return toolName switch
            {
                "Read" => input.TryGetProperty("file_path", out var fp) ? fp.GetString() ?? "" : "",
                "Write" => input.TryGetProperty("file_path", out var wp) ? wp.GetString() ?? "" : "",
                "Edit" => input.TryGetProperty("file_path", out var ep) ? ep.GetString() ?? "" : "",
                "Bash" => input.TryGetProperty("command", out var cmd) ? Truncate(cmd.GetString() ?? "", 80) : "",
                "Glob" => input.TryGetProperty("pattern", out var pat) ? pat.GetString() ?? "" : "",
                "Grep" => input.TryGetProperty("pattern", out var gp) ? gp.GetString() ?? "" : "",
                "Agent" => input.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                "WebSearch" => input.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "",
                "WebFetch" => input.TryGetProperty("url", out var url) ? url.GetString() ?? "" : "",
                _ => ""
            };
        }
        catch { return ""; }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}
