using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeIslandWindows.Services;

public static class HookInstaller
{
    private const string ScriptName = "claude-island-state.ps1";
    private const string HookIdentifier = "claude-island-state";

    private static string ClaudeDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    private static string HooksDir => Path.Combine(ClaudeDir, "hooks");
    private static string ScriptPath => Path.Combine(HooksDir, ScriptName);
    private static string SettingsPath => Path.Combine(ClaudeDir, "settings.json");

    public static void InstallIfNeeded()
    {
        try
        {
            // Clean up old Python hook if present
            var oldScript = Path.Combine(HooksDir, "claude-island-state.py");
            if (File.Exists(oldScript)) File.Delete(oldScript);

            Directory.CreateDirectory(HooksDir);
            var bundled = Path.Combine(AppContext.BaseDirectory, "Resources", ScriptName);
            if (File.Exists(bundled))
                File.Copy(bundled, ScriptPath, overwrite: true);
            UpdateSettings();
        }
        catch { }
    }

    public static bool IsInstalled()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return false;
            return File.ReadAllText(SettingsPath).Contains(HookIdentifier);
        }
        catch { return false; }
    }

    public static void Uninstall()
    {
        try
        {
            if (File.Exists(ScriptPath)) File.Delete(ScriptPath);
            // Also clean up old Python script
            var oldScript = Path.Combine(HooksDir, "claude-island-state.py");
            if (File.Exists(oldScript)) File.Delete(oldScript);

            if (!File.Exists(SettingsPath)) return;

            var root = JsonNode.Parse(File.ReadAllText(SettingsPath))?.AsObject();
            if (root?["hooks"] is not JsonObject hooks) return;

            var toRemove = new List<string>();
            foreach (var (name, value) in hooks)
            {
                if (value is JsonArray entries)
                {
                    RemoveOurEntries(entries);
                    if (entries.Count == 0) toRemove.Add(name);
                }
            }
            foreach (var key in toRemove) hooks.Remove(key);
            if (hooks.Count == 0) root.Remove("hooks");

            File.WriteAllText(SettingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static void UpdateSettings()
    {
        // First remove any old Python-based hooks
        RemoveOldPythonHooks();

        JsonObject root;
        if (File.Exists(SettingsPath))
        {
            try { root = JsonNode.Parse(File.ReadAllText(SettingsPath))?.AsObject() ?? new JsonObject(); }
            catch { root = new JsonObject(); }
        }
        else { root = new JsonObject(); }

        var command = $"powershell -ExecutionPolicy Bypass -NoProfile -File \"{ScriptPath}\"";
        var hooks = root["hooks"]?.AsObject() ?? new JsonObject();

        // Baseline events supported by every Claude Code version with hooks
        var hookList = new List<(string name, JsonArray config)>
        {
            ("UserPromptSubmit", MakeConfig(command, null, null)),
            ("PreToolUse", MakeConfig(command, "*", null)),
            ("PostToolUse", MakeConfig(command, "*", null)),
            ("PermissionRequest", MakeConfig(command, "*", 86400)),
            ("Notification", MakeConfig(command, "*", null)),
            ("Stop", MakeConfig(command, null, null)),
            ("SubagentStop", MakeConfig(command, null, null)),
            ("SessionStart", MakeConfig(command, null, null)),
            ("SessionEnd", MakeConfig(command, null, null)),
            ("PreCompact", MakePreCompactConfig(command)),
        };

        // Register newer events only if the installed Claude Code supports them.
        // Claude Code rejects unknown keys, so we gate on version.
        var version = DetectClaudeCodeVersion();
        if (version is { } v)
        {
            if (v >= new Version(2, 0, 0))
                hookList.Add(("PostToolUseFailure", MakeConfig(command, "*", null)));
            if (v >= new Version(2, 0, 43))
                hookList.Add(("SubagentStart", MakeConfig(command, null, null)));
            if (v >= new Version(2, 1, 76))
                hookList.Add(("PostCompact", MakePreCompactConfig(command)));
            if (v >= new Version(2, 1, 78))
                hookList.Add(("StopFailure", MakeConfig(command, null, null)));
            if (v >= new Version(2, 1, 88))
                hookList.Add(("PermissionDenied", MakeConfig(command, "*", null)));
        }

        var hookEvents = hookList.ToArray();

        foreach (var (name, config) in hookEvents)
        {
            if (hooks[name] is JsonArray existing)
            {
                if (!ContainsOurHook(existing))
                    foreach (var item in config)
                        existing.Add(item!.DeepClone());
            }
            else { hooks[name] = config; }
        }

        root["hooks"] = hooks;
        File.WriteAllText(SettingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void RemoveOldPythonHooks()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var root = JsonNode.Parse(File.ReadAllText(SettingsPath))?.AsObject();
            if (root?["hooks"] is not JsonObject hooks) return;

            bool changed = false;
            var toRemove = new List<string>();
            foreach (var (name, value) in hooks)
            {
                if (value is JsonArray entries)
                {
                    for (int i = entries.Count - 1; i >= 0; i--)
                    {
                        if (entries[i]?["hooks"] is JsonArray hookArr)
                        {
                            if (hookArr.Any(h => (h?["command"]?.GetValue<string>() ?? "").Contains("claude-island-state.py")))
                            {
                                entries.RemoveAt(i);
                                changed = true;
                            }
                        }
                    }
                    if (entries.Count == 0) toRemove.Add(name);
                }
            }
            foreach (var key in toRemove) { hooks.Remove(key); changed = true; }

            if (changed)
                File.WriteAllText(SettingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static Version? DetectClaudeCodeVersion()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "claude",
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return null;
            if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } return null; }
            var output = p.StandardOutput.ReadToEnd();
            // Match X.Y.Z anywhere in the output
            var m = System.Text.RegularExpressions.Regex.Match(output, @"(\d+)\.(\d+)\.(\d+)");
            if (!m.Success) return null;
            return new Version(
                int.Parse(m.Groups[1].Value),
                int.Parse(m.Groups[2].Value),
                int.Parse(m.Groups[3].Value));
        }
        catch { return null; }
    }

    private static JsonArray MakeConfig(string command, string? matcher, int? timeout)
    {
        var hookObj = new JsonObject { ["type"] = "command", ["command"] = command };
        if (timeout.HasValue) hookObj["timeout"] = timeout.Value;
        var entry = new JsonObject { ["hooks"] = new JsonArray { hookObj } };
        if (matcher != null) entry["matcher"] = matcher;
        return new JsonArray { entry };
    }

    private static JsonArray MakePreCompactConfig(string command)
    {
        var hookObj = new JsonObject { ["type"] = "command", ["command"] = command };
        return new JsonArray
        {
            new JsonObject { ["matcher"] = "auto", ["hooks"] = new JsonArray { hookObj } },
            new JsonObject { ["matcher"] = "manual", ["hooks"] = new JsonArray { hookObj.DeepClone() } }
        };
    }

    private static bool ContainsOurHook(JsonArray entries)
    {
        foreach (var entry in entries)
            if (entry?["hooks"] is JsonArray hooks)
                foreach (var hook in hooks)
                    if ((hook?["command"]?.GetValue<string>() ?? "").Contains(HookIdentifier))
                        return true;
        return false;
    }

    private static void RemoveOurEntries(JsonArray entries)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i]?["hooks"] is JsonArray hooks)
                if (hooks.Any(h => (h?["command"]?.GetValue<string>() ?? "").Contains(HookIdentifier)))
                    entries.RemoveAt(i);
    }
}
