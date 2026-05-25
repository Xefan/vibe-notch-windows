using System.Collections.Concurrent;
using System.Text.Json;

namespace ClaudeIslandWindows.BuddyBridge;

/// Tails ~/.claude/projects/&lt;encoded-cwd&gt;/&lt;session_id&gt;.jsonl for each known session
/// and sums `usage.output_tokens` from `assistant` lines.
///
/// One stream offset per session file. We re-read tails on a poll cadence rather than
/// using FileSystemWatcher: Claude Code writes are atomic appends but watcher latency
/// on Windows is unreliable; a 2s poll is cheap and bounded.
public sealed class TokenTracker
{
    private readonly Action<string> _log;
    private readonly object _lock = new();

    /// session_id → tracked file info
    private readonly Dictionary<string, FileWatch> _watched = new();

    /// Cumulative output tokens this Claude Island process has seen.
    private long _totalOutput = 0;

    /// Output tokens by local day (yyyy-MM-dd).
    private readonly ConcurrentDictionary<string, long> _byDay = new();

    private CancellationTokenSource? _cts;
    public event Action? TokensChanged;

    public TokenTracker(Action<string>? log = null) => _log = log ?? (_ => { });

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        lock (_lock) _watched.Clear();
    }

    /// Tell the tracker which session_id+cwd pairs are currently known. Pairs not seen
    /// for &gt;30min stay around so we don't lose totals.
    ///
    /// IMPORTANT: per the Buddy bridge spec, `tokens` is cumulative-since-CI-started,
    /// matching Claude Desktop's per-process semantic. The firmware does delta-based
    /// crediting and re-baselines on drops. If we summed historical transcript content
    /// we'd report a huge baseline that the firmware would credit as a single jump.
    /// So on registration we open the file and skip past whatever's already there —
    /// only bytes appended from now on get counted.
    public void RegisterSession(string sessionId, string cwd)
    {
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(cwd)) return;
        lock (_lock)
        {
            if (_watched.ContainsKey(sessionId)) return;
            var path = TranscriptPath(sessionId, cwd);
            long startOffset = 0;
            try { if (File.Exists(path)) startOffset = new FileInfo(path).Length; }
            catch { /* fall through with offset 0 */ }
            _watched[sessionId] = new FileWatch { Path = path, Offset = startOffset };
            _log($"Tokens: watching session {sessionId[..Math.Min(8, sessionId.Length)]} → {path} (skip {startOffset} existing bytes)");
        }
    }

    public uint TotalOutput => (uint)Math.Clamp(_totalOutput, 0, uint.MaxValue);

    public uint TodayOutput
    {
        get
        {
            var key = DateTime.Now.ToString("yyyy-MM-dd");
            return (uint)Math.Clamp(_byDay.GetValueOrDefault(key, 0), 0, uint.MaxValue);
        }
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { ScanAll(); }
            catch (Exception ex) { _log($"Tokens: poll error: {ex.Message}"); }

            try { await Task.Delay(TimeSpan.FromSeconds(2), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void ScanAll()
    {
        List<FileWatch> snapshot;
        lock (_lock) snapshot = _watched.Values.ToList();

        bool changed = false;
        foreach (var w in snapshot)
        {
            try
            {
                if (!File.Exists(w.Path)) continue;
                var fi = new FileInfo(w.Path);
                if (fi.Length <= w.Offset) continue;

                using var fs = new FileStream(w.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fs.Seek(w.Offset, SeekOrigin.Begin);
                using var sr = new StreamReader(fs);
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var delta = ExtractOutputTokens(line);
                    if (delta > 0)
                    {
                        Interlocked.Add(ref _totalOutput, delta);
                        var key = DateTime.Now.ToString("yyyy-MM-dd");
                        _byDay.AddOrUpdate(key, delta, (_, prev) => prev + delta);
                        changed = true;
                    }
                }
                w.Offset = fs.Position;
            }
            catch (Exception ex) { _log($"Tokens: read {w.Path}: {ex.Message}"); }
        }

        if (changed) TokensChanged?.Invoke();
    }

    private static long ExtractOutputTokens(string jsonLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return 0;
            // Only count assistant messages (the format ConversationParser also reads).
            if (!root.TryGetProperty("type", out var tEl) || tEl.GetString() != "assistant") return 0;
            if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return 0;
            if (!msg.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return 0;
            if (!usage.TryGetProperty("output_tokens", out var ot)) return 0;
            return ot.ValueKind switch
            {
                JsonValueKind.Number => ot.TryGetInt64(out var n) ? n : 0,
                _ => 0
            };
        }
        catch { return 0; }
    }

    /// Derives the on-disk transcript path the same way ConversationParser.Parse does.
    public static string TranscriptPath(string sessionId, string cwd)
    {
        var projectDir = cwd
            .Replace(":\\", "--").Replace(":/", "--")
            .Replace("\\", "-").Replace("/", "-")
            .Replace(".", "-").Replace(":", "-");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".claude", "projects", projectDir, sessionId + ".jsonl");
    }

    private sealed class FileWatch
    {
        public string Path { get; set; } = "";
        public long Offset { get; set; }
    }
}
