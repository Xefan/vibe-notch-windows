using System.Collections.Concurrent;
using ClaudeIslandWindows.Models;

namespace ClaudeIslandWindows.BuddyBridge;

/// Registry of in-flight permission prompts. Each tool_use_id has at most one TCS;
/// whichever side (Buddy reply / Island UI / 60s timeout) resolves it first wins.
///
/// The TCS is *not* what blocks the hook script — that's NamedPipeServer.RespondToPermission.
/// This class just remembers "Buddy was told about this; if Buddy decides, call UI's
/// RespondToPermission for us so the loop is consistent."
public sealed class PendingPrompts
{
    public sealed record Entry(string ToolUseId, string ToolName, string Hint,
                               string SessionId, DateTime CreatedAt);

    private readonly ConcurrentDictionary<string, Entry> _prompts = new();

    /// Currently-pinned prompt that the Buddy should display. The spec says one at a time,
    /// pick the oldest. Returns null when there's no waiting prompt.
    public Entry? Active =>
        _prompts.Values.OrderBy(e => e.CreatedAt).FirstOrDefault();

    public IReadOnlyCollection<Entry> All => _prompts.Values.ToArray();

    public void Add(Entry e) => _prompts[e.ToolUseId] = e;

    public bool Remove(string toolUseId) => _prompts.TryRemove(toolUseId, out _);

    public bool Contains(string toolUseId) => _prompts.ContainsKey(toolUseId);
}
