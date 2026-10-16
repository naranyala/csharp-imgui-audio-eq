# Roadmap: from starter to a real audio EQ

This repo (`csharp-imgui-audio-eq`) is currently a UI/windowing starter. This document sketches how it can grow into an actual equalizer. Nothing here is implemented yet — it is a plan.

## Stage 1 — project structure

Split into multiple projects so DSP and audio I/O stay testable without a GUI:

```
CsharpImGuiAudioEq.slnx
src/
  ImGuiApp/        window + UI (existing)
  AudioEq.Core/    class library: filters, DSP, preset model (unit-testable)
  AudioEq.Engine/  class library: device I/O, real-time graph (optional, can live in Core)
tests/
  AudioEq.Core.Tests/
```

Add with:

```bash
dotnet new classlib -n AudioEq.Core -o src/AudioEq.Core
dotnet new xunit   -n AudioEq.Core.Tests -o tests/AudioEq.Core.Tests
dotnet sln add src/AudioEq.Core tests/AudioEq.Core.Tests
dotnet add src/ImGuiApp reference src/AudioEq.Core
```

## Stage 2 — DSP core (no audio yet)

- Biquad filter library (RBJ cookbook): peaking, low shelf, high shelf, LPF/HPF.
- Per-band parameter model: frequency, gain dB, Q -> coefficient recompute.
- Sample-accurate processing API: `Process(Span<float> buffer, int channels)`.
- Unit tests: impulse response, magnitude sweeps vs. analytic response.

The starter UI sliders in `Application.DrawUI()` map naturally onto this model:
`_bandGains[i]` -> band i gain; `_cutoffHz` -> global filter frequency.

## Stage 3 — real-time audio I/O

Choose one:

| Option            | Pros                              | Cons                          |
| ----------------- | --------------------------------- | ----------------------------- |
| PortAudio (e.g. `PortAudioSharp2`) | cross-platform, low latency       | binding maturity varies       |
| NAudio            | great on Windows                  | Windows-only                  |
| OpenAL Soft       | simple API, game-audio heritage   | extra native dependency       |
| miniaudio (via P/Invoke or binding) | tiny, cross-platform, callback-based | you maintain the binding      |

Hard real-time rules for the audio callback:

- no locks, no allocations, no ImGui/GL calls on the audio thread.
- parameters (band gains, bypass) passed via lock-free atomics or a single-producer/single-consumer ring buffer; smooth them inside the callback (one-pole ramp) to avoid zipper noise.

## Stage 4 — UI upgrades

- Frequency-response curve plot: compute the summed magnitude response of all biquads and draw it with `ImGui.GetWindowDrawList().PathLine/PathFillConvex` over a log-frequency grid (this is the signature EQ visual).
- Spectrum analyzer: feed FFT magnitudes (FftSharp or MathNet.Numerics) into the same plot as a background layer; push audio frames from the audio thread into a ring buffer read by the UI thread.
- Per-band draggable handles instead of plain sliders (handle input inside the plot widget using `ImGui.InvisibleButton` + `ImGui.SetMouseCursor`).
- Preset management: save/load JSON presets; ImGui layout persists automatically to `imgui.ini`.

## Stage 5 — polish

- A/B compare, bypass, undo (keep a small ring of parameter snapshots).
- Analyzer window size/overlap controls, band solo/mute.
- Packaging: self-contained single-file publish per platform (see [getting-started](getting-started.md)).

## Suggested order of work

1. `AudioEq.Core` + biquad tests (pure, fast to validate)
2. offline WAV render test (process a file through the EQ, write output)
3. device I/O + callback plumbing with a null/bypass processor
4. enable the processor, wire UI sliders to parameters
5. response curve + spectrum visuals
6. presets and polish
