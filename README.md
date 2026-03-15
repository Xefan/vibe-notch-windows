<div align="center">
  <h3 align="center">Claude Island (for Windows)</h3>
  <p align="center">
    A Windows port of the <a href="https://github.com/MarcinusX/claude-island">original macOS app</a> that brings Dynamic Island-style notifications to Claude Code CLI sessions.
    <br />
    <br />
    <a href="https://github.com/Xefan/claude-island-windows/releases/latest" target="_blank" rel="noopener noreferrer">
      <img src="https://img.shields.io/github/v/release/Xefan/claude-island-windows?style=rounded&color=white&labelColor=000000&label=release" alt="Release Version" />
    </a>
    <a href="#" target="_blank" rel="noopener noreferrer">
      <img alt="GitHub Downloads" src="https://img.shields.io/github/downloads/Xefan/claude-island-windows/total?style=rounded&color=white&labelColor=000000">
    </a>
  </p>
</div>

## Features

- **Notch UI** — Animated overlay at the top-center of your screen that expands on hover or click
- **Live Session Monitoring** — Track multiple Claude Code sessions in real-time
- **Permission Approvals** — Approve or deny tool executions directly from the overlay
- **Auto-Setup** — Hooks install automatically on first launch, no configuration needed
- **No Dependencies** — Uses PowerShell for hooks, no Python or other runtimes required

## Requirements

- Windows 10/11
- .NET 10 Runtime
- Claude Code CLI

## Install

Download the latest release from the [releases page](https://github.com/Xefan/claude-island-windows/releases/latest), or build from source:

```bash
cd claude-island-windows/ClaudeIslandWindows
dotnet build
dotnet run
```

## How It Works

Claude Island installs PowerShell hook scripts into `%HOMEPATH%\.claude\hooks` and registers them in `%HOMEPATH%\.claude\settings.json`. These hooks fire on Claude Code events (tool use, permission requests, session start/end, etc.) and communicate with the app via a Windows Named Pipe (`\\.\pipe\claude-island`).

The app listens for events and displays session state in the overlay panel. When Claude needs permission to run a tool, the overlay expands with approve/deny buttons — no need to switch to the terminal.

### Architecture

```
Claude Code → Hook Event → PowerShell Script → Named Pipe → Claude Island → UI
                                                    ↑
                                    Permission Response (approve/deny)
```

## Analytics

This Windows port does not collect any analytics or telemetry data.

## Credits

Based on [Claude Island](https://github.com/MarcinusX/claude-island) by [Marcin Szalek](https://github.com/MarcinusX), originally built for macOS with Swift/SwiftUI.

This Windows port is built with WPF/.NET and C#.

## License

Apache 2.0
