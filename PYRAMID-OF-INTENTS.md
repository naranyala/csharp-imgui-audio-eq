# Pyramid of Intents — csharp-imgui-audio-eq

How to read this file: top = stable, long-lived intent. Bottom = concrete,
changeable work. Higher levels constrain lower levels — a TODO that conflicts
with a level above it is wrong and must be re-scoped, not the other way round.

Structure inspired by `../dart-flutter-audio-eq/PYRAMID-OF-INTENTS.md`;
epic IDs `E1–E8` deliberately mirror that project so the two codebases can be
compared task-for-task.

```
            L0 VISION (why this exists)
            L1 MISSION + PRINCIPLES (how we behave)
            L2 STRATEGIC GOALS (what winning looks like)
            L3 EPICS / CAPABILITIES (system must do X)
            L4 TASKS (work items) → see TODOS.md
```

---

## L0 — Vision

A free, open, cross-platform **desktop** audio equalizer written in C# with
Dear ImGui: correct sound first, immediately responsive UI second, every
desktop platform eventually — one codebase, no per-OS forks.

- Tier 1 now: **Linux + Windows** (GLFW/OpenGL windowing and desktop audio
  I/O are both mature there).
- Tier 2 later: **macOS** (window/GL path already runs; CoreAudio specifics
  untested).
- Mobile explicitly out of scope — this project is desktop-first.

## L1 — Mission + Principles

Mission: ship the smallest desktop EQ that is **audibly real** on Tier 1
(playback where sliders change what you hear), then widen platform coverage
and system integration without forking the app.

Principles (in priority order):

1. **Correctness over features.** A 5-band EQ that truly changes sound beats
   a 31-band mock. Any slider shown before the engine exists must be labeled
   as UI-only — no fake processing, ever.
2. **UI is a client, never the owner.** ImGui code (`Application.DrawUI`)
   touches only plain application state; DSP and audio sit behind an
   `EqEngine` contract; device/platform code never leaks into widgets.
3. **Real-time safety.** The audio callback allocates nothing, locks nothing,
   does no I/O. Parameters cross the thread boundary lock-free (atomics or an
   SPSC ring) and are smoothed inside the callback to avoid zipper noise.
4. **Reference before invention.** RBJ biquad cookbook, Equalizer APO file
   format, PipeWire filter-chain layouts, proven device APIs — copy proven
   designs instead of inventing new ones.
5. **Open formats first.** Equalizer APO `.txt` and AutoEQ profiles before
   any proprietary preset format.
6. **Test the contract.** Pure C# DSP is unit-tested (flat ≈ transparent,
   +6 dB ≈ ×2 amplitude, clamping respected); every engine backend passes the
   same conformance suite; golden vectors catch coefficient bugs that
   sine-RMS tests miss.
7. **No drivers, no hacks.** Supported OS APIs only — no kernel DSP drivers,
   no unsigned code, no process injection.

## L2 — Strategic goals

| ID | Goal | Done when |
|----|------|-----------|
| G1 | Audible in-app audition on Tier 1 | Device backend processes real buffers; moving a band slider changes what the user hears; bypass plays the untouched original; UI-only state honestly labeled until then |
| G2 | System-wide path on Linux | App exports **and documents activation of** a PipeWire filter-chain preset; exact PipeWire version + commands recorded in README |
| G3 | Preset library worth opening the app for | APO `.txt` import/export round-trip + ≥20 AutoEQ-derived headphone presets with attribution + named user presets (CRUD, persisted as JSON) |
| G4 | Trustworthy DSP core | Pure-C# RBJ biquads behind xUnit tests (flat ≈ transparent, ±6 dB ≈ ×2, shelf direction, clamps) plus golden vectors from `tool/golden_vectors.py` |
| G5 | Observable sound | Static log-frequency response curve drawn with `ImDrawList` (preamp included), later live spectrum fed from the audio-thread ring buffer |
| G6 | Tier-2 ready | Windows (WASAPI/APO) and macOS (CoreAudio) backends implement `EqEngine` with zero `DrawUI()` changes; tier-2 warnings disappear platform by platform |
| G7 | Installable on Tier 1 | LICENSE decided before importing any derived code, CI green, one packaged artifact per Tier-1 OS; a stranger can install it |

Non-goals (explicit): mobile ports, DAW plugin formats (VST/AU/LV2/CLAP),
room-correction measurement (REW-style), convolution/FIR engines (until IIR
proves insufficient), kernel/driver-level EQ, accounts / cloud sync /
subscriptions.

## L3 — Epics / Capabilities

- **E1 Engine abstraction** — `EqEngine` contract + interchangeable backends
  (`OfflineRenderEngine` for WAV render today, `DevicePlaybackEngine` for
  real-time next, `MockEqEngine` for UI work) with one shared conformance
  test suite per backend.
- **E2 Preset system** — built-ins (Flat/Bass/Vocal/Treble) → APO `.txt`
  import/export → AutoEQ profile bundle → searchable headphone-match browser
  → named user presets (JSON CRUD) with last-used migration; window layout
  persists via `imgui.ini`.
- **E3 DSP core** — C# biquads (peaking/shelf/notch from the W3C EQ
  Cookbook), per-band Q + filter-type editing, preamp gain, offline WAV
  render path, callback-side parameter smoothing, golden-vector validation;
  FFI/SIMD only if profiling demands it.
- **E4 Auditioning** — device enumeration + playback backend (time-boxed
  PortAudio-vs-miniaudio spike), transport (play/pause/seek/loop), render-
  based hot-swap or live processing (decision record required), debounced
  re-render on slider drags, honest A/B bypass.
- **E5 Visualization** — static EQ response curve now (pure draw-list math,
  free of permissions), live spectrum later (SPSC ring from the audio thread
  + FFT).
- **E6 Platform wiring** — PipeWire filter-chain export on Linux, WASAPI/APO
  path on Windows, CoreAudio stubs on macOS behind a capability flag; device
  selection; no platform `#if` inside UI code.
- **E7 Quality gates** — `dotnet build` warning-free, `dotnet test` green,
  CI when the repo goes remote, real-machine matrix (Linux distros × Windows
  versions) before any release claim.
- **E8 Release & distribution** — LICENSE first, icon + display name +
  version scheme, packaging (one format per Tier-1 OS: self-contained
  publish / .deb-or-AppImage / .msi), install story in README, no telemetry
  (none planned).

## L4 — Tasks

Lives in `TODOS.md`. Every task carries its epic + goal tags (e.g.
`[E3/G4]`). If a task has no parent goal, either the pyramid is incomplete or
the task doesn't belong — resolve before coding.
