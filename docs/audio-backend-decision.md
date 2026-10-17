# Audio backend decision record

Status: decided 2026-10-16 (P0 `[E4/G1]` spike, time-boxed).
Revisit trigger: a Tier-2 goal needs device APIs OpenAL Soft cannot reach.

## Question

Which library drives device I/O on Tier 1 (Linux PipeWire-first, Windows),
runs a Float32 callback that executes our `EqChain` in real time, and swaps
behind `IEqEngine` without touching UI code?

## Method (evidence, this machine)

Probes run 2026-10-16 on the dev Linux box:

| Probe | Result |
|---|---|
| NuGet: PortAudioSharp2 | exists (200) |
| NuGet: miniaudio official | 404 (no official binding) |
| NuGet: MiniaudioSharp / Miniaudio-CS / Hexa.NET.MiniAudio | exist, community, <1.1 |
| NuGet: Silk.NET.OpenAL + Silk.NET.OpenAL.Soft.Native | both exist (binding 2.23.0, natives 1.23.1) |
| NuGet: NAudio | exists (Windows-only) |
| NuGet: FftSharp | exists (kept for P1 spectrum) |
| `ldconfig -p` | `libpulse`, `libasound`, `libopenal.so.1` present; **no libportaudio** |
| Audio stack | HDA Intel ALC256 + PipeWire + PulseAudio compat; `aplay -l` lists cards |

## Options

| Option | Pros | Cons |
|---|---|---|
| PortAudioSharp2 | pro-audio pedigree, low latency, clean callback | no system lib here; native dep ships how?; wrapper is single-maintainer; extra install step for users |
| miniaudio bindings | single-header C core, great feature set | **no official C# binding**; community packages immature; FFI surface maintenance burden (principle 4 counterweight) |
| NAudio | mature, WASAPI excellent | **Windows-only** — fails Linux Tier-1; cannot be the common layer |
| Silk.NET.OpenAL + Soft.Native | **already on our stack** (Silk 2.23); **bundled natives** (self-contained per G7, no distro deps); Float32 via `AL_EXT_float32` (always on in Soft); streaming queue API = natural refill callback; one library covers Linux + Windows + macOS (Tier 2 free); system `libopenal.so.1` as fallback | game-mixer semantics, not pro-audio; device enumeration is default-centric; no system-wide routing (E6 stays a separate PipeWire path anyway) |

## Decision

**OpenAL Soft via `Silk.NET.OpenAL` (2.23.0) + `Silk.NET.OpenAL.Soft.Native`
(1.23.1 — natives use OpenAL Soft's own versioning).** Implementation:
`src/AudioEq.Engine/OpenAlPlaybackEngine.cs` — a refill thread unqueues
processed Float32 buffers, runs the current `EqChain` pair (lock-free swap
from `ApplyGains`) on each block, requeues. Underruns are counted, not
hidden. Device enumeration uses ALC's multi-string specifier; `MockEqEngine`
implements the same `IEqEngine` for no-device fallback and conformance
tests.

Rejected: PortAudio (native-dep story + maintenance risk), miniaudio
(no official binding), NAudio (Windows-only).

## Audition-flow sub-decision (required by the audition-flow TODO)

Chosen for G1: **live byte processing through `IEqEngine`** — the OpenAL
streaming path *is* the real-time callback, and a mono/stereo buffer is
trivially produced from the track or a synthesized tone. Render-based
hot-swap (`OfflineRenderer.RenderToPcm16`) stays as the file-render test
path and future "export processed WAV" feature; it is not the G1 audition
route now that the engine exists.

## Consequences

- G7 self-containment holds: no system audio package required to run.
- Tier-2 backends (WASAPI/CoreAudio) later become separate `IEqEngine`
  implementations chosen at startup — zero `DrawUI()` changes (E1 contract).
- E6 system-wide Linux stays PipeWire filter-chain export, deliberately
  independent of this in-app playback path.
- If a future goal needs pro-audio device control (ASIO-class), re-run this
  spike — the contract swap is one constructor in startup wiring.
