using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ClaudeIslandWindows.Models;
using ClaudeIslandWindows.Services;

namespace ClaudeIslandWindows.BuddyBridge;

/// Lifecycle owner for the Buddy bridge. Listens to hook events + session store
/// changes, builds heartbeats, drives the keepalive timer, and translates Buddy
/// permission decisions back into hook responses.
///
/// Threading: methods are safe to call from any thread; internal state is
/// guarded by locks where shared.
public sealed class BuddyBridgeService : INotifyPropertyChanged, IDisposable
{
    private readonly SessionStore _sessions;
    private readonly NamedPipeServer _pipes;
    private readonly BleConnection _ble;
    private readonly PendingPrompts _prompts = new();
    private readonly TokenTracker _tokens;
    private readonly List<string> _entries = new();
    private readonly object _entriesLock = new();

    private NusClient? _nus;
    private CancellationTokenSource? _heartbeatCts;
    private CancellationTokenSource? _debounceCts;
    private bool _completedFlag;
    private string? _lastPromptId; // remembered across heartbeats

    private static readonly TimeSpan Debounce  = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Keepalive = TimeSpan.FromSeconds(10);

    public BridgeStatus Status => _ble.Status;
    public string? DeviceName => _ble.DeviceName;

    public event PropertyChangedEventHandler? PropertyChanged;

    public BuddyBridgeService(SessionStore sessions, NamedPipeServer pipes)
    {
        _sessions = sessions;
        _pipes = pipes;
        _tokens = new TokenTracker(Log);

        var cfg = Settings.Current.BuddyBridge;
        _ble = new BleConnection(cfg.NamePrefix, Log);
        _ble.StatusChanged += OnBleStatusChanged;
        _ble.Connected += OnBleConnected;
        _ble.Disconnected += OnBleDisconnected;

        _sessions.SessionsChanged += OnSessionsChanged;
        _tokens.TokensChanged += () => TriggerHeartbeat();
    }

    public void Start()
    {
        var cfg = Settings.Current.BuddyBridge;
        if (!cfg.Enabled)
        {
            Log("Bridge: disabled in settings");
            return;
        }
        _tokens.Start();
        _ble.Start(cfg.LastDeviceAddress);
        StartHeartbeatLoop();
    }

    public void Stop()
    {
        _heartbeatCts?.Cancel();
        _heartbeatCts = null;
        _ble.Stop();
        _tokens.Stop();
    }

    public void Dispose() => Stop();

    /// User-driven reconnect from the settings menu.
    public void ReconnectNow()
    {
        var cfg = Settings.Current.BuddyBridge;
        _ble.Stop();
        _ble.Start(cfg.LastDeviceAddress);
    }

    /// Called from App.OnHookEvent for every hook arrival. Updates prompts + entries.
    public void NotifyHookEvent(HookEvent evt)
    {
        _tokens.RegisterSession(evt.SessionId, evt.Cwd);

        if (evt.Event == "PreToolUse" && !string.IsNullOrEmpty(evt.Tool))
        {
            var entry = EntryFormatter.Format(DateTime.Now, evt.Tool, evt.ToolInput);
            lock (_entriesLock)
            {
                _entries.Insert(0, entry);
                while (_entries.Count > 6) _entries.RemoveAt(_entries.Count - 1);
            }
        }

        if (evt.Event == "Stop")
            _completedFlag = true;

        if (evt.ExpectsResponse && !string.IsNullOrEmpty(evt.ToolUseId))
        {
            var hint = ExtractHint(evt.Tool, evt.ToolInput);
            _prompts.Add(new PendingPrompts.Entry(
                evt.ToolUseId!, evt.Tool ?? "tool", hint,
                evt.SessionId, DateTime.UtcNow));
        }

        TriggerHeartbeat();
    }

    private void OnSessionsChanged()
    {
        // If a prompt's owning session is no longer waiting for approval, the UI
        // (or the bridge itself) already resolved it — drop from pending.
        foreach (var p in _prompts.All)
        {
            var s = _sessions.GetSession(p.SessionId);
            if (s == null || s.Phase.Kind != SessionPhaseKind.WaitingForApproval)
                _prompts.Remove(p.ToolUseId);
        }
        TriggerHeartbeat();
    }

    private void StartHeartbeatLoop()
    {
        _heartbeatCts?.Cancel();
        _heartbeatCts = new CancellationTokenSource();
        var ct = _heartbeatCts.Token;
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(Keepalive);
            while (!ct.IsCancellationRequested)
            {
                try { await timer.WaitForNextTickAsync(ct); }
                catch (OperationCanceledException) { return; }
                // Defensive: a transient exception in the heartbeat path must NOT
                // tear down the keepalive loop. If we let it bubble, Buddy goes
                // 30s without a heartbeat and flips the persona to sleep.
                try { await SendHeartbeatAsync(ct); }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { Log($"Bridge: heartbeat tick swallowed: {ex.Message}"); }
            }
        }, ct);
    }

    private void TriggerHeartbeat()
    {
        // Coalesce bursts inside Debounce window.
        var prev = Interlocked.Exchange(ref _debounceCts, new CancellationTokenSource());
        prev?.Cancel();
        var ctsLocal = _debounceCts!;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Debounce, ctsLocal.Token);
                await SendHeartbeatAsync(ctsLocal.Token);
            }
            catch (OperationCanceledException) { }
        });
    }

    private async Task SendHeartbeatAsync(CancellationToken ct)
    {
        var nus = _nus;
        if (nus == null || !nus.IsConnected) return;

        var sessions = _sessions.GetSessions();
        var active = _prompts.Active;
        PromptDto? promptDto = null;
        if (active != null)
        {
            promptDto = new PromptDto
            {
                Id = active.ToolUseId,
                Tool = EntryFormatter.Truncate(active.ToolName, 23),
                Hint = EntryFormatter.Truncate(active.Hint, 63)
            };
            _lastPromptId = active.ToolUseId;
        }
        else _lastPromptId = null;

        // completed flag is one-shot — capture and clear before sending.
        var completed = _completedFlag;
        _completedFlag = false;

        string[] entriesSnapshot;
        lock (_entriesLock) entriesSnapshot = _entries.ToArray();

        // Always send tokens (even 0). The firmware does delta-based crediting and
        // needs the first heartbeat to baseline at 0 — omitting the field on early
        // heartbeats would lose the first N tokens between baseline and first non-zero.
        var hb = HeartbeatBuilder.Build(
            sessions, entriesSnapshot, completed,
            _tokens.TotalOutput, _tokens.TodayOutput, promptDto);
        var line = HeartbeatBuilder.Serialize(hb);
        await nus.WriteLineAsync(line, ct);
    }

    private void OnBleStatusChanged(BridgeStatus s)
    {
        Raise(nameof(Status));
        Raise(nameof(DeviceName));
    }

    private async void OnBleConnected(NusClient nus)
    {
        _nus = nus;
        nus.LineReceived += OnNusLine;

        // Persist this device so we skip the scan next launch.
        Settings.Current.BuddyBridge.LastDeviceAddress = _ble.DeviceAddress;
        Settings.Current.BuddyBridge.LastDeviceName = _ble.DeviceName;
        Settings.Current.Save();

        // Time sync (one-shot)
        try
        {
            var epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var tz = (long)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalSeconds;
            var timeMsg = $"{{\"time\":[{epoch},{tz}]}}";
            await nus.WriteLineAsync(timeMsg);
        }
        catch (Exception ex) { Log($"Bridge: time-sync send failed: {ex.Message}"); }

        // First heartbeat ASAP so the Buddy flips its `linked` indicator within 30s.
        TriggerHeartbeat();
    }

    private void OnBleDisconnected()
    {
        var nus = _nus;
        _nus = null;
        if (nus != null)
        {
            nus.LineReceived -= OnNusLine;
            nus.Dispose();
        }
    }

    private void OnNusLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (!root.TryGetProperty("cmd", out var cmd) || cmd.GetString() != "permission") return;
            if (!root.TryGetProperty("id", out var idEl) || !root.TryGetProperty("decision", out var decEl))
                return;
            var id = idEl.GetString();
            var dec = decEl.GetString();
            if (id == null || dec == null) return;

            Log($"Bridge: Buddy decided {id} → {dec}");

            // Find session owning this prompt — needed to update SessionStore.
            var entry = _prompts.All.FirstOrDefault(p => p.ToolUseId == id);
            if (entry == null) { Log($"Bridge: ignoring late decision for unknown id {id}"); return; }

            if (dec == "once")
            {
                _pipes.RespondToPermission(id, "allow");
                _sessions.ProcessEvent(new SessionEvent.PermissionApproved(entry.SessionId, id));
            }
            else
            {
                _pipes.RespondToPermission(id, "deny", "Denied via Claude Buddy");
                _sessions.ProcessEvent(new SessionEvent.PermissionDenied(
                    entry.SessionId, id, "Denied via Claude Buddy"));
            }
            _prompts.Remove(id);
            TriggerHeartbeat();
        }
        catch (Exception ex) { Log($"Bridge: parse inbound: {ex.Message}"); }
    }

    private static string ExtractHint(string? toolName, JsonElement? toolInput)
    {
        if (toolInput is not { } el || el.ValueKind != JsonValueKind.Object) return "";
        try
        {
            return toolName switch
            {
                "Bash" => StringProp(el, "command"),
                "Read" or "Write" or "Edit" or "MultiEdit" => StringProp(el, "file_path"),
                "Grep" or "Glob" => StringProp(el, "pattern"),
                "WebFetch" => StringProp(el, "url"),
                "Task" or "Agent" => StringProp(el, "description"),
                _ => ""
            };
        }
        catch { return ""; }
    }

    private static string StringProp(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String) return "";
        return v.GetString() ?? "";
    }

    private static void Log(string msg)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClaudeIsland", "bridge.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss} {msg}\n");
        }
        catch { }
    }

    private void Raise([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
