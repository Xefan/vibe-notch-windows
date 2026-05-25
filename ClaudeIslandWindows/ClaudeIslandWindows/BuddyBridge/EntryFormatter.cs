using System.Text.Json;

namespace ClaudeIslandWindows.BuddyBridge;

/// Turns a (toolName, toolInput JSON) pair into a one-line activity entry per HOOK_MAPPING.md.
/// Format: "HH:MM <verb> <target>", ≤79 chars total. Local time HH:MM prefix.
public static class EntryFormatter
{
    public static string Format(DateTime timestampLocal, string? toolName, JsonElement? toolInput)
    {
        var hhmm = timestampLocal.ToString("HH:mm");
        var body = BuildBody(toolName ?? "tool", toolInput);
        return Truncate($"{hhmm} {body}", 79);
    }

    private static string BuildBody(string toolName, JsonElement? input)
    {
        try
        {
            return toolName switch
            {
                "Bash" => $"$ {Truncate(StringFrom(input, "command"), 60)}",
                "Read" => $"read {Basename(StringFrom(input, "file_path"))}",
                "Write" => $"write {Basename(StringFrom(input, "file_path"))}",
                "Edit" or "MultiEdit" => $"edit {Basename(StringFrom(input, "file_path"))}",
                "Grep" => $"grep {Truncate(StringFrom(input, "pattern"), 50)}",
                "Glob" => $"glob {Truncate(StringFrom(input, "pattern"), 50)}",
                "WebFetch" => $"fetch {Hostname(StringFrom(input, "url"))}",
                "WebSearch" => "search",
                "Task" or "Agent" => $"agent {Truncate(StringFrom(input, "description"), 50)}",
                _ when toolName.StartsWith("mcp__", StringComparison.Ordinal) => McpLastSegment(toolName),
                _ => toolName
            };
        }
        catch { return toolName; }
    }

    private static string StringFrom(JsonElement? input, string key)
    {
        if (input is not { } el || el.ValueKind != JsonValueKind.Object) return "";
        if (!el.TryGetProperty(key, out var v)) return "";
        return v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }

    private static string Basename(string path)
    {
        if (string.IsNullOrEmpty(path)) return "?";
        var sep = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return sep >= 0 && sep + 1 < path.Length ? path[(sep + 1)..] : path;
    }

    private static string Hostname(string url)
    {
        if (string.IsNullOrEmpty(url)) return "?";
        try { return new Uri(url).Host; } catch { return url; }
    }

    private static string McpLastSegment(string toolName)
    {
        var stripped = toolName.StartsWith("mcp__", StringComparison.Ordinal) ? toolName[5..] : toolName;
        var idx = stripped.LastIndexOf("__", StringComparison.Ordinal);
        return idx >= 0 && idx + 2 < stripped.Length ? stripped[(idx + 2)..] : stripped;
    }

    public static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        if (s.Length <= max) return s;
        return max <= 1 ? s[..max] : s[..(max - 1)] + "…";
    }
}
