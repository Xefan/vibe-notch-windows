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
        bool IsCompleted = false,
        string? ToolResult = null,
        string? OldString = null,
        string? NewString = null,
        string? FilePath = null);

    // Cache: key = sessionFile path, value = (lastModified, items)
    private static readonly Dictionary<string, (DateTime Modified, List<ChatItem> Items)> _cache = new();

    public static List<ChatItem> Parse(string sessionId, string cwd)
    {
        var projectDir = cwd
            .Replace(":\\", "--").Replace(":/", "--")
            .Replace("\\", "-").Replace("/", "-")
            .Replace(".", "-").Replace(":", "-");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var sessionFile = Path.Combine(home, ".claude", "projects", projectDir, sessionId + ".jsonl");

        if (!File.Exists(sessionFile)) return [];

        // Check cache
        var lastWrite = File.GetLastWriteTimeUtc(sessionFile);
        if (_cache.TryGetValue(sessionFile, out var cached) && cached.Modified == lastWrite)
            return cached.Items;

        // Single pass: collect everything
        var rawItems = new List<ChatItem>();
        var completedToolIds = new HashSet<string>();
        var toolResults = new Dictionary<string, string>();

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

                    if (!root.TryGetProperty("message", out var message)) continue;
                    if (!message.TryGetProperty("content", out var content)) continue;

                    var uuid = root.TryGetProperty("uuid", out var u) ? u.GetString() ?? "" : "";
                    var timestamp = DateTime.UtcNow;
                    if (root.TryGetProperty("timestamp", out var ts) &&
                        DateTime.TryParse(ts.GetString(), out var parsed))
                        timestamp = parsed;

                    if (content.ValueKind == JsonValueKind.String)
                    {
                        var text = content.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(text))
                            rawItems.Add(new ChatItem(uuid,
                                type == "user" ? ItemType.User : ItemType.Assistant,
                                text, timestamp));
                    }
                    else if (content.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var block in content.EnumerateArray())
                        {
                            if (!block.TryGetProperty("type", out var blockType)) continue;
                            var bt = blockType.GetString();

                            if (bt == "tool_result" && block.TryGetProperty("tool_use_id", out var tid))
                            {
                                var toolId = tid.GetString() ?? "";
                                completedToolIds.Add(toolId);
                                if (block.TryGetProperty("content", out var rc))
                                {
                                    if (rc.ValueKind == JsonValueKind.String)
                                        toolResults[toolId] = rc.GetString() ?? "";
                                    else if (rc.ValueKind == JsonValueKind.Array)
                                        foreach (var rcItem in rc.EnumerateArray())
                                            if (rcItem.TryGetProperty("text", out var rt))
                                            { toolResults[toolId] = rt.GetString() ?? ""; break; }
                                }
                                continue;
                            }

                            if (bt == "text" && block.TryGetProperty("text", out var textEl))
                            {
                                var text = textEl.GetString() ?? "";
                                if (!string.IsNullOrWhiteSpace(text))
                                    rawItems.Add(new ChatItem($"{uuid}-text",
                                        type == "user" ? ItemType.User : ItemType.Assistant,
                                        text, timestamp));
                            }
                            else if (bt == "tool_use" && block.TryGetProperty("name", out var nameEl))
                            {
                                var toolName = nameEl.GetString() ?? "tool";
                                var toolId = block.TryGetProperty("id", out var idEl)
                                    ? idEl.GetString() ?? "" : "";
                                var inputStr = block.TryGetProperty("input", out var input)
                                    ? SummarizeToolInput(toolName, input) : "";

                                string? oldStr = null, newStr = null, filePath = null;
                                if (toolName == "Edit" && block.TryGetProperty("input", out var editInput))
                                {
                                    if (editInput.TryGetProperty("old_string", out var os)) oldStr = os.GetString();
                                    if (editInput.TryGetProperty("new_string", out var ns)) newStr = ns.GetString();
                                    if (editInput.TryGetProperty("file_path", out var fp2)) filePath = fp2.GetString();
                                }

                                rawItems.Add(new ChatItem(toolId, ItemType.ToolCall,
                                    inputStr, timestamp,
                                    ToolName: toolName, ToolInput: inputStr,
                                    OldString: oldStr, NewString: newStr, FilePath: filePath));
                            }
                            else if (bt == "thinking" && block.TryGetProperty("thinking", out var thinkEl))
                            {
                                var text = thinkEl.GetString() ?? "";
                                if (!string.IsNullOrWhiteSpace(text))
                                    rawItems.Add(new ChatItem($"{uuid}-think", ItemType.Thinking,
                                        text, timestamp));
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        // Apply completion status to tool items
        var items = rawItems.Select(item =>
            item.Type == ItemType.ToolCall && completedToolIds.Contains(item.Id)
                ? item with
                {
                    IsCompleted = true,
                    ToolResult = toolResults.GetValueOrDefault(item.Id)
                }
                : item
        ).ToList();

        _cache[sessionFile] = (lastWrite, items);
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
