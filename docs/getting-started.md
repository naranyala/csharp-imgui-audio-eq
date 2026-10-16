# Getting started

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`dotnet --version` should report 10.x)
- A desktop session with OpenGL 3.3 core profile support
  - Linux: any recent Mesa or NVIDIA/AMD/Intel driver
  - Windows 10+: built in
  - macOS: works via the forward-compatible context flag (legacy profile is not used)
- Optional: an IDE — Rider, VS Code (with C# Dev Kit), or Visual Studio 2026+

No native libraries need to be installed manually: GLFW ships as a NuGet dependency (`Ultz.Native.GLFW`).

## Build

```bash
dotnet build
```

Expected output: `Build succeeded, 0 Warning(s), 0 Error(s)`.

## Run

```bash
dotnet run --project src/ImGuiApp
```

A 1280x720 window titled "ImGui Audio EQ - Starter" opens showing:

- the ImGui demo window (toggleable)
- the "Audio EQ (starter)" panel with gain/cutoff/band sliders
- FPS readout

## Run a single project / different config

```bash
dotnet run --project src/ImGuiApp -c Release
```

## Publish a self-contained binary

```bash
dotnet publish src/ImGuiApp -c Release -r linux-x64 --self-contained true
```

Output lands in `src/ImGuiApp/bin/Release/net10.0/<rid>/publish/`.

## Solution contents

The solution (`CsharpImGuiAudioEq.slnx`) currently contains one project:

| Project          | Path                | Purpose                          |
| ---------------- | ------------------- | -------------------------------- |
| `ImGuiApp`       | `src/ImGuiApp`      | the desktop application          |

When the audio engine arrives it should be added as a separate class library
(e.g. `src/AudioEq.Core`) — see [roadmap](roadmap.md).

## Suggested workflow

1. Read [architecture](architecture.md) to understand the frame loop.
2. Tweak the starter UI in `Application.DrawUI()`.
3. Rebuild and rerun; changes are a ~2 second iteration.
