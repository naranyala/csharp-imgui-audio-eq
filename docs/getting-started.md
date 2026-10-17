# Getting started

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`dotnet --version` should report 10.x)
- A desktop session with OpenGL 3.3 core profile support
  - Linux: any recent Mesa or NVIDIA/AMD/Intel driver
  - Windows 10+: built in
  - macOS: works via the forward-compatible context flag (legacy profile is not used)
- Optional: an audio output device — engine device tests soft-skip without
  one, and `MockEqEngine` always works. OpenAL Soft natives ship inside the
  `Silk.NET.OpenAL.Soft.Native` NuGet package (Linux/Windows/macOS); no
  system audio package is required.
- Optional: an IDE — Rider, VS Code (with C# Dev Kit), or Visual Studio 2026+

No native libraries need to be installed manually: GLFW ships as a NuGet
dependency (`Ultz.Native.GLFW`), OpenAL Soft as above.

## Build

```bash
dotnet build
```

Expected output: `Build succeeded, 0 Warning(s), 0 Error(s)`.

## Test

```bash
dotnet test
```

Runs the full suite (67 tests): DSP math, WAV round-trips, APO
import/export, engine contract (mock), and — when a sound device is
present — a real OpenAL playback smoke test.

## Run

```bash
dotnet run --project src/ImGuiApp
```

A 1280x720 window titled "ImGui Audio EQ - Starter" opens showing:

- the ImGui demo window (toggleable)
- the "Audio EQ (starter)" panel — sliders are **UI-only** (labeled as
  such) until the audition flow wires them to the engine
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

The solution (`CsharpImGuiAudioEq.slnx`) currently contains:

| Project              | Path                    | Purpose                              |
| -------------------- | ----------------------- | ------------------------------------ |
| `ImGuiApp`           | `src/ImGuiApp`          | the desktop application (UI shell)   |
| `AudioEq.Core`       | `src/AudioEq.Core`      | pure DSP + WAV render + APO presets  |
| `AudioEq.Engine`     | `src/AudioEq.Engine`    | `IEqEngine` backends: OpenAL Soft + mock |
| `AudioEq.Core.Tests` | `tests/AudioEq.Core.Tests` | xUnit suite for the core (67 tests) |

UI code must only talk to `AudioEq.Core` through plain state, and to audio
playback only through `IEqEngine` — never the other way round (principle 2 in
`PYRAMID-OF-INTENTS.md`). See [audio-backend-decision](audio-backend-decision.md)
for why OpenAL Soft won the backend spike.

## Suggested workflow

1. Read [architecture](architecture.md) to understand the frame loop and
   where the audio path plugs in.
2. Tweak the starter UI in `Application.DrawUI()`, or the DSP in
   `AudioEq.Core/Dsp/` (tests first — see the working agreement in TODOS.md).
3. Rebuild and rerun; changes are a ~2 second iteration.
