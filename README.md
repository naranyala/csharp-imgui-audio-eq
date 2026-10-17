# C# ImGui Audio EQ

A cross-platform .NET desktop audio equalizer, built with **Dear ImGui**
(via ImGui.NET) and **Silk.NET** (GLFW + OpenGL 3.3). The project ships a
working window and UI scaffold plus a tested DSP core, Equalizer APO
preset support, and real device playback — the app is not yet wired to hear
it (the UI honestly says so; see [TODOS.md](TODOS.md)).

## What works today

| Area | State |
| ---- | ----- |
| Desktop shell | OpenGL 3.3 window, full input mapping, DPI-aware ImGui bridge |
| DSP core | RBJ biquads (peaking/shelf/notch), N-band chain, magnitude response |
| WAV render | offline EQ of PCM16/float32 files, deterministic output |
| Presets | Equalizer APO `.txt` import/export, tested round-trip |
| Device playback | `IEqEngine` + OpenAL Soft backend (bundled natives) + mock fallback |
| UI ↔ audio | **not connected yet** — starter sliders are UI-only until the audition flow lands |
| Quality | 0 warnings / 0 errors · 67/67 tests |

## Tech stack

| Component    | Package                          | Version  |
| ------------ | -------------------------------- | -------- |
| UI           | ImGui.NET                        | 1.91.6.1 |
| Windowing    | Silk.NET.Windowing               | 2.23.0   |
| Graphics     | Silk.NET.OpenGL                  | 2.23.0   |
| Input        | Silk.NET.Input                   | 2.23.0   |
| Audio        | Silk.NET.OpenAL + Soft.Native    | 2.23.0 / 1.23.1 |
| Tests        | xUnit                            | 2.9.3    |
| Runtime      | .NET                             | 10.0     |

## Quick start

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download) and a
GPU/driver with OpenGL 3.3+. A sound device (e.g. PipeWire/PulseAudio) is
optional — engine tests soft-skip and the mock backend always works.

```bash
dotnet build                     # 0 warnings / 0 errors
dotnet test                      # runs AudioEq.Core.Tests (67 tests)
dotnet run --project src/ImGuiApp
```

## Project layout

```
CsharpImGuiAudioEq.slnx
PYRAMID-OF-INTENTS.md  vision → principles → goals → epics
TODOS.md               phased task tracker with [Ex/Gx] intent tags
docs/
  getting-started.md    setup, build, run, publish
  architecture.md       components, frame lifecycle, audio path
  audio-backend-decision.md  why OpenAL Soft won the backend spike
  imgui-controller.md   how the ImGui <-> Silk.NET bridge works
  roadmap.md            status + path from here to a full audio EQ
  troubleshooting.md    common errors and fixes
src/ImGuiApp/
  Program.cs            window + OpenGL context creation
  Application.cs        frame callbacks and the starter EQ UI
  ImGuiController.cs    input, font atlas, and GL rendering bridge
src/AudioEq.Core/
  Dsp/Biquad.cs         RBJ cookbook biquads + in-place reconfigure
  Dsp/EqChain.cs        N-band chain, magnitude response, live SetGains
  Audio/WavData.cs      minimal RIFF/WAVE reader-writer (PCM16, float32)
  Audio/OfflineRenderer.cs  file-based EQ render path
  Presets/ApoPreset.cs  Equalizer APO .txt parse/serialize
src/AudioEq.Engine/
  IEqEngine.cs          playback-backend contract (UI depends only on this)
  EqEngineBase.cs       lock-free gain swap + mono/stereo plumbing
  OpenAlPlaybackEngine.cs  real device playback (OpenAL Soft, Float32)
  MockEqEngine.cs       no-device fallback (same contract)
tests/AudioEq.Core.Tests/   xUnit suite (67 tests)
```

## Documentation

- [`PYRAMID-OF-INTENTS.md`](PYRAMID-OF-INTENTS.md) — vision → principles → goals → epics
- [`TODOS.md`](TODOS.md) — phased tracker (11 done / 22 open)
- [Getting started](docs/getting-started.md)
- [Architecture](docs/architecture.md)
- [Audio backend decision](docs/audio-backend-decision.md)
- [ImGui controller internals](docs/imgui-controller.md)
- [Roadmap](docs/roadmap.md)
- [Troubleshooting](docs/troubleshooting.md)

## Where to start hacking

- UI: `Application.DrawUI()` (src/ImGuiApp/Application.cs) — honest
  UI-only banner lives there; remove it via the audition-flow task
- DSP: `Biquad`/`EqChain` in `src/AudioEq.Core/Dsp/` — everything is
  covered by tests; extend the suite with any change
- Playback: implement or tweak an `IEqEngine` backend in
  `src/AudioEq.Engine/` — the UI never sees backend types
- Next milestones: audition flow + transport (see TODOS.md P0)
