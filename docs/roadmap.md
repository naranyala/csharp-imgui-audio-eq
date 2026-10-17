# Roadmap: from starter to a real audio EQ

This repo started as a UI/windowing starter and has grown: the DSP core,
APO preset layer, and device playback engine now exist and are tested.
The source of truth for "what is done vs next" is
[`TODOS.md`](../TODOS.md); this document explains the shape of the
journey and what remains.

## Stage 1 — project structure ✅

```
CsharpImGuiAudioEq.slnx
src/
  ImGuiApp/        window + UI
  AudioEq.Core/    class library: filters, DSP, WAV, APO (unit-testable)
  AudioEq.Engine/  class library: IEqEngine, OpenAL Soft + mock backends
tests/
  AudioEq.Core.Tests/
```

DSP and audio I/O are testable without a GUI; the UI references both
libraries but neither references the UI.

## Stage 2 — DSP core ✅

- Biquad library (RBJ cookbook): peaking, low shelf, high shelf, notch —
  degenerate parameters clamped to stable coefficients.
- N-band `EqChain` with per-band gains, live `SetGains` (state-preserving),
  and `MagnitudeResponseDb` for the curve widget.
- Offline WAV render (PCM16/float32) with deterministic output.
- Unit tests: flat/±6 dB, selectivity, clamps, impulse decay, measured
  RMS vs analytic magnitude, degenerate-input safety.

Not yet: LPF/HPF, golden vectors from a NumPy reference, callback-side
parameter smoothing (P1 in TODOS.md).

## Stage 3 — real-time audio I/O ✅ (backend chosen)

Backend decision: **OpenAL Soft via `Silk.NET.OpenAL` + bundled natives**
— full evidence in [audio-backend-decision](audio-backend-decision.md).
Rejected PortAudio (native-dep story), miniaudio (no official binding),
NAudio (Windows-only).

Implemented: `IEqEngine` contract, `OpenAlPlaybackEngine` (Float32
streaming queue, lock-free gain swap, underrun counter, enumeration),
`MockEqEngine` fallback; verified on real hardware by the test suite.

Hard real-time rules honored on the refill path: no locks, no
allocations, no ImGui/GL calls; parameters arrive via the lock-free
`ApplyGains` swap. One-pole smoothing of gain changes is still P1
(zipper-noise polish).

## Stage 4 — UI upgrades (mostly open)

- Wire the UI to `IEqEngine` (audition flow + transport) — the P0 critical
  path; the UI-only banner goes away only when this honestly lands.
- Frequency-response curve plot from `EqChain.MagnitudeResponseDb` (core
  math exists and is tested; the `ImDrawList` widget is open).
- Spectrum analyzer (FftSharp; ring buffer from the engine).
- Per-band draggable handles, preset browser on top of the APO/JSON store.

## Stage 5 — polish / Tier 2 / release (open)

- A/B compare, bypass, undo, band solo/mute.
- Tier-2 backends (WASAPI/CoreAudio) behind the same `IEqEngine` contract;
  PipeWire filter-chain export for system-wide Linux EQ.
- CI, LICENSE, icons, packaging, real-machine test matrix.

## Suggested order of work

1. Audition flow: UI → `IEqEngine` (decision recorded: live processing)
2. Transport: play/pause, loop, track label
3. Response-curve widget (math is ready)
4. Parametric editing + preamp end-to-end + smoothing (P1)
5. Preset store/bundle/browser, spectrum, conformance suite
6. Tier 2 + release track
