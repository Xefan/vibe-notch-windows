using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ClaudeIslandWindows.Models;

namespace ClaudeIslandWindows.Services;

public sealed class NamedPipeServer
{
    public const string PipeName = "claude-island";

    private CancellationTokenSource? _cts;
    private Action<HookEvent>? _eventHandler;

    private readonly Dictionary<string, PendingPermission> _pendingPermissions = new();
    private readonly object _permissionsLock = new();

    private readonly Dictionary<string, Queue<string>> _toolUseIdCache = new();
    private readonly object _cacheLock = new();

    public void Start(Action<HookEvent> onEvent)
    {
        _eventHandler = onEvent;
        _cts = new CancellationTokenSource();
        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        lock (_permissionsLock)
        {
            foreach (var pending in _pendingPermissions.Values)
                pending.PipeStream.Dispose();
            _pendingPermissions.Clear();
        }
    }

    public void RespondToPermission(string toolUseId, string decision, string? reason = null)
    {
        PendingPermission? pending;
        lock (_permissionsLock)
        {
            if (!_pendingPermissions.Remove(toolUseId, out pending))
                return;
        }

        var response = new HookResponse { Decision = decision, Reason = reason };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response));

        try
        {
            pending.PipeStream.Write(bytes, 0, bytes.Length);
            pending.PipeStream.Flush();
            pending.PipeStream.WaitForPipeDrain();
        }
        catch { }
        finally { pending.PipeStream.Dispose(); }
    }

    public void RespondToPermissionBySession(string sessionId, string decision, string? reason = null)
    {
        string? toolUseId = null;
        lock (_permissionsLock)
        {
            var match = _pendingPermissions
                .Where(kv => kv.Value.SessionId == sessionId)
                .OrderByDescending(kv => kv.Value.ReceivedAt)
                .FirstOrDefault();
            if (match.Value != null)
                toolUseId = match.Key;
        }
        if (toolUseId != null)
            RespondToPermission(toolUseId, decision, reason);
    }

    public void CancelPendingPermissions(string sessionId)
    {
        lock (_permissionsLock)
        {
            var toRemove = _pendingPermissions
                .Where(kv => kv.Value.SessionId == sessionId)
                .Select(kv => kv.Key).ToList();
            foreach (var key in toRemove)
                if (_pendingPermissions.Remove(key, out var p))
                    p.PipeStream.Dispose();
        }
    }

    public bool HasPendingPermission(string sessionId)
    {
        lock (_permissionsLock)
            return _pendingPermissions.Values.Any(p => p.SessionId == sessionId);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        // Lock the pipe DACL to just the current user. The default Windows DACL on
        // an unspecified-security NamedPipeServerStream is permissive enough that
        // another local process can write a fake hook event — including a forged
        // approval response that allows a tool call we never authorized. Granting
        // FullControl only to the running user's SID closes that vector.
        var ps = new PipeSecurity();
        var currentUser = WindowsIdentity.GetCurrent().User!;
        ps.AddAccessRule(new PipeAccessRule(
            currentUser, PipeAccessRights.FullControl, AccessControlType.Allow));

        while (!ct.IsCancellationRequested)
        {
            // Use Message mode so reads return complete messages
            var pipe = NamedPipeServerStreamAcl.Create(
                PipeName, PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Message, PipeOptions.Asynchronous,
                inBufferSize: 0, outBufferSize: 0, pipeSecurity: ps);
            try
            {
                await pipe.WaitForConnectionAsync(ct);
                _ = Task.Run(() => HandleClientAsync(pipe), ct);
            }
            catch (OperationCanceledException) { pipe.Dispose(); break; }
            catch { pipe.Dispose(); }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe)
    {
        try
        {
            // Read the complete message
            var buffer = new byte[131072];
            using var ms = new MemoryStream();

            do
            {
                var bytesRead = await pipe.ReadAsync(buffer);
                if (bytesRead == 0) break;
                ms.Write(buffer, 0, bytesRead);
            } while (!pipe.IsMessageComplete);

            if (ms.Length == 0) { pipe.Dispose(); return; }

            var evt = JsonSerializer.Deserialize<HookEvent>(
                Encoding.UTF8.GetString(ms.ToArray()));
            if (evt == null) { pipe.Dispose(); return; }

            if (evt.Event == "PreToolUse") CacheToolUseId(evt);
            if (evt.Event == "SessionEnd") CleanupCache(evt.SessionId);

            if (evt.ExpectsResponse)
            {
                var toolUseId = evt.ToolUseId ?? PopCachedToolUseId(evt);
                if (toolUseId == null) { pipe.Dispose(); _eventHandler?.Invoke(evt); return; }

                evt.ToolUseId = toolUseId;
                var pending = new PendingPermission(evt.SessionId, toolUseId, pipe, evt, DateTime.UtcNow);
                lock (_permissionsLock) { _pendingPermissions[toolUseId] = pending; }
                _eventHandler?.Invoke(evt);
                // Pipe stays open — response written later via RespondToPermission
            }
            else
            {
                pipe.Dispose();
                _eventHandler?.Invoke(evt);
            }
        }
        catch { pipe.Dispose(); }
    }

    private string CacheKey(string sessionId, string? toolName, JsonElement? toolInput)
    {
        string inputStr = "{}";
        if (toolInput is { } el && el.ValueKind != JsonValueKind.Undefined)
        {
            try
            {
                var dict = JsonSerializer.Deserialize<SortedDictionary<string, object>>(el.GetRawText());
                inputStr = JsonSerializer.Serialize(dict);
            }
            catch { inputStr = el.GetRawText(); }
        }
        return $"{sessionId}:{toolName ?? "unknown"}:{inputStr}";
    }

    private void CacheToolUseId(HookEvent evt)
    {
        if (evt.ToolUseId == null) return;
        var key = CacheKey(evt.SessionId, evt.Tool, evt.ToolInput);
        lock (_cacheLock)
        {
            if (!_toolUseIdCache.TryGetValue(key, out var queue))
                _toolUseIdCache[key] = queue = new Queue<string>();
            queue.Enqueue(evt.ToolUseId);
        }
    }

    private string? PopCachedToolUseId(HookEvent evt)
    {
        var key = CacheKey(evt.SessionId, evt.Tool, evt.ToolInput);
        lock (_cacheLock)
        {
            if (!_toolUseIdCache.TryGetValue(key, out var queue) || queue.Count == 0)
                return null;
            var id = queue.Dequeue();
            if (queue.Count == 0) _toolUseIdCache.Remove(key);
            return id;
        }
    }

    private void CleanupCache(string sessionId)
    {
        lock (_cacheLock)
        {
            var prefix = $"{sessionId}:";
            foreach (var key in _toolUseIdCache.Keys.Where(k => k.StartsWith(prefix)).ToList())
                _toolUseIdCache.Remove(key);
        }
    }

    private sealed record PendingPermission(
        string SessionId, string ToolUseId, NamedPipeServerStream PipeStream,
        HookEvent Event, DateTime ReceivedAt);
}
