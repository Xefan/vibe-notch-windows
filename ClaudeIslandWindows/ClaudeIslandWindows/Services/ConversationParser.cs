using System.Text.Json;

namespace ClaudeIslandWindows.Services;

public sealed class ConversationParser
{
    public record ChatItem(string Id, string Role, string Content, DateTime Timestamp);

    /// <summary>
    /// Parse a Claude JSONL conversation file and return displayable chat items.
    /// Path format: ~/.claude/projects/{projectDir}/{sessionId}.jsonl
    /// </summary>
    public static List<ChatItem> Parse(string sessionId, string cwd)
    {
        var items = new List<ChatItem>();

        // Claude Code on Windows encodes cwd as: literal:C:\path\to\project → literal:C--path-to-project
        // Colons become '-', backslashes become '-', forward slashes become '-'
        var projectDir = cwd
            .Replace(":\\", "--")  // D:\ → D--
            .Replace(":/", "--")   // D:/ → D--
            .Replace("\\", "-")
            .Replace("/", "-")
            .Replace(".", "-")
            .Replace(":", "-");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var sessionFile = Path.Combine(home, ".claude", "projects", projectDir, sessionId + ".jsonl");

        if (!File.Exists(sessionFile)) return items;

        try
        {
            foreach (var line in File.ReadLines(sessionFile))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    // Skip meta messages
                    if (root.TryGetProperty("isMeta", out var meta) && meta.GetBoolean())
                        continue;

                    var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (type is not ("user" or "assistant")) continue;

                    var uuid = root.TryGetProperty("uuid", out var u) ? u.GetString() ?? "" : "";
                    var timestamp = DateTime.UtcNow;
                    if (root.TryGetProperty("timestamp", out var ts))
                    {
                        if (DateTime.TryParse(ts.GetString(), out var parsed))
                            timestamp = parsed;
                    }

                    // Extract text content from message.content
                    if (!root.TryGetProperty("message", out var message)) continue;
                    if (!message.TryGetProperty("content", out var content)) continue;

                    if (content.ValueKind == JsonValueKind.String)
                    {
                        var text = content.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(text))
                            items.Add(new ChatItem(uuid, type, text, timestamp));
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
                                    items.Add(new ChatItem($"{uuid}-text", type, text, timestamp));
                            }
                            else if (bt == "tool_use" && block.TryGetProperty("name", out var nameEl))
                            {
                                var toolName = nameEl.GetString() ?? "tool";
                                var inputStr = "";
                                if (block.TryGetProperty("input", out var input))
                                {
                                    // Extract a brief description from input
                                    inputStr = SummarizeToolInput(toolName, input);
                                }
                                items.Add(new ChatItem(
                                    block.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? uuid : uuid,
                                    "tool",
                                    $"{toolName}: {inputStr}",
                                    timestamp));
                            }
                            else if (bt == "tool_result" && block.TryGetProperty("content", out var resultContent))
                            {
                                // Skip tool results in display for now — they're verbose
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
                "Bash" => input.TryGetProperty("command", out var cmd) ? Truncate(cmd.GetString() ?? "", 60) : "",
                "Glob" => input.TryGetProperty("pattern", out var pat) ? pat.GetString() ?? "" : "",
                "Grep" => input.TryGetProperty("pattern", out var gp) ? gp.GetString() ?? "" : "",
                _ => ""
            };
        }
        catch { return ""; }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}
