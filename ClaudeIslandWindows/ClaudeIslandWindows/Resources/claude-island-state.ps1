# Claude Island Hook (Windows)
# Sends session state to ClaudeIsland via Named Pipe
# For PermissionRequest: waits for user decision from the app

$PipeName = "claude-island"
$TimeoutSeconds = 300

function Send-Event {
    param([hashtable]$State)

    try {
        $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", $PipeName, [System.IO.Pipes.PipeDirection]::InOut)
        $pipe.Connect(2000)

        $json = $State | ConvertTo-Json -Depth 10 -Compress
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
        $pipe.Write($bytes, 0, $bytes.Length)
        $pipe.Flush()

        # For permission requests, wait for response
        if ($State["status"] -eq "waiting_for_approval") {
            $buffer = [byte[]]::new(4096)
            $pipe.ReadTimeout = $TimeoutSeconds * 1000

            try {
                $bytesRead = $pipe.Read($buffer, 0, $buffer.Length)
                if ($bytesRead -gt 0) {
                    $responseJson = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $bytesRead)
                    $pipe.Close()
                    return ($responseJson | ConvertFrom-Json)
                }
            } catch {
                # Timeout or read error
            }
        }

        $pipe.Close()
        return $null
    } catch {
        return $null
    }
}

# Read event JSON from stdin
$inputJson = [Console]::In.ReadToEnd()
try {
    $data = $inputJson | ConvertFrom-Json
} catch {
    exit 1
}

$sessionId = if ($data.session_id) { $data.session_id } else { "unknown" }
$event = if ($data.hook_event_name) { $data.hook_event_name } else { "" }
$cwd = if ($data.cwd) { $data.cwd } else { "" }
$toolInput = $data.tool_input

# Get parent process info
$claudePid = (Get-Process -Id $PID).Parent.Id
$processInfo = "$claudePid"

# Build state object
$state = @{
    session_id = $sessionId
    cwd        = $cwd
    event      = $event
    pid        = $claudePid
    tty        = $processInfo
}

switch ($event) {
    "UserPromptSubmit" {
        $state["status"] = "processing"
    }
    "PreToolUse" {
        $state["status"] = "running_tool"
        $state["tool"] = $data.tool_name
        $state["tool_input"] = $toolInput
        if ($data.tool_use_id) {
            $state["tool_use_id"] = $data.tool_use_id
        }
    }
    "PostToolUse" {
        $state["status"] = "processing"
        $state["tool"] = $data.tool_name
        $state["tool_input"] = $toolInput
        if ($data.tool_use_id) {
            $state["tool_use_id"] = $data.tool_use_id
        }
    }
    "PermissionRequest" {
        $state["status"] = "waiting_for_approval"
        $state["tool"] = $data.tool_name
        $state["tool_input"] = $toolInput

        $response = Send-Event -State $state

        if ($response) {
            $decision = if ($response.decision) { $response.decision } else { "ask" }
            $reason = if ($response.reason) { $response.reason } else { "" }

            if ($decision -eq "allow") {
                $output = @{
                    hookSpecificOutput = @{
                        hookEventName = "PermissionRequest"
                        decision = @{ behavior = "allow" }
                    }
                }
                $output | ConvertTo-Json -Depth 10 -Compress
                exit 0
            }
            elseif ($decision -eq "deny") {
                $output = @{
                    hookSpecificOutput = @{
                        hookEventName = "PermissionRequest"
                        decision = @{
                            behavior = "deny"
                            message = if ($reason) { $reason } else { "Denied by user via ClaudeIsland" }
                        }
                    }
                }
                $output | ConvertTo-Json -Depth 10 -Compress
                exit 0
            }
        }

        # No response or "ask" - let Claude Code show its normal UI
        exit 0
    }
    "Notification" {
        $notificationType = $data.notification_type
        if ($notificationType -eq "permission_prompt") {
            exit 0
        }
        elseif ($notificationType -eq "idle_prompt") {
            $state["status"] = "waiting_for_input"
        }
        else {
            $state["status"] = "notification"
        }
        $state["notification_type"] = $notificationType
        $state["message"] = $data.message
    }
    "Stop" {
        $state["status"] = "waiting_for_input"
    }
    "SubagentStop" {
        $state["status"] = "waiting_for_input"
    }
    "SessionStart" {
        $state["status"] = "waiting_for_input"
    }
    "SessionEnd" {
        $state["status"] = "ended"
    }
    "PreCompact" {
        $state["status"] = "compacting"
    }
    default {
        $state["status"] = "unknown"
    }
}

# Send to pipe (fire and forget for non-permission events)
Send-Event -State $state | Out-Null
