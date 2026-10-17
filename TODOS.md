# TODOS — csharp-imgui-audio-eq

Parent intent for every item: `PYRAMID-OF-INTENTS.md`. Tags `[Ex/Gx]` point at
the epic and goal each task serves. Phase order is intentional — finish P0
before starting P1. Phases map to the delivery stages in
`docs/roadmap.md` (P0 ≈ stages 1–3, P1 ≈ stage 4, P2 ≈ stage 5+).
Format inspired by `../dart-flutter-audio-eq/TODOS.md`.

Legend: `[ ]` open · `[x]` done · `[~]` in progress / blocked (note why).

**Status 2026-10-16**: 11 done / 22 open · `dotnet test` 67/67 ·
strongest area E3 (DSP ~70%); biggest gap G1 (audible) — P0 items below are
the critical path to "audibly real" (audition flow + transport remain).

## P0 — Make it real (audible EQ on Tier 1)

- [x] Starter scaffold: solution + `ImGuiApp` (ImGui.NET 1.91 + Silk.NET
      2.23) — OpenGL 3.3 core window, keyboard/mouse/focus input, full
      `ImGuiController` bridge (font atlas, GL state save/restore, streamed
      draw submission), starter "Audio EQ" panel with demo/metrics toggles
- [x] Docs set: `README.md` + `docs/` (getting-started, architecture,
      imgui-controller, roadmap, troubleshooting)
- [x] `[E7/*]` Quality baseline: `dotnet build` 0 warnings / 0 errors;
      run verified on Linux/Wayland (2026-10-16, killed by timeout after 6 s
      with no GL/shader errors)
- [x] `[E3/*]` Split `AudioEq.Core` class library + `AudioEq.Core.Tests`
      (xUnit); referenced from `ImGuiApp` — see docs/roadmap.md stage 1
- [x] `[E3/G4]` RBJ biquads (`src/AudioEq.Core/Dsp/Biquad.cs`): peaking +
      low/high shelf + notch (W3C EQ Cookbook), DF1 state, `MagnitudeAt`,
      degenerate inputs clamped to finite stable coefficients; 16 unit tests
      (flat transparency, ±6 dB ≈ ×2/×0.5, selectivity, magnitudeAt
      agreement, shelf direction, notch depth, clamp/NaN safety, clamped
      ranges, in-place ≡ copy, Reset ≡ fresh, Q selectivity, impulse decay,
      extreme boundedness)
- [x] `[E3/G4]` Offline WAV render (`Audio/WavData.cs` + `Audio/OfflineRenderer.cs`):
      minimal RIFF reader/writer (PCM16 + IEEE float32), per-channel fresh
      `EqChain`, preamp as flat post-gain; 10 tests (flat transparency +
      metadata, real RIFF round-trip, boosted ≈×2 at center, preamp gain,
      channel independence, no cross-channel state leak, input immutability,
      cut ≈ halves loudness, byte-exact determinism, RenderToPcm16)
- [x] `[E2/G3]` Equalizer APO `.txt` import/export
      (`Presets/ApoPreset.cs`): preamp + PK/LS/HS, OFF skip, log-distance
      `ToGains` band mapping, round-trip serialize; 14 tests (parse sample,
      nearest-band mapping ±preamp, headroom mode, round-trip, unsupported
      ON type throws, empty file, case/CRLF, last-preamp-wins, formatting,
      GraphicEQ ignored, ON NO throws, empty gains, ToBiquads magnitude,
      shelf type mapping)
- [x] `[E4/G1]` UI-only honesty label on the starter panel (principle 1):
      sliders move but do not affect audio until G1 lands; `TextDisabled`
      banner + per-item note, removed only by the audition-flow item
- [x] `[E4/G1]` Backend decision record: time-boxed spike finished
      2026-10-16 (PortAudio vs miniaudio vs OpenAL Soft vs NAudio, NuGet +
      system-lib probes) — `docs/audio-backend-decision.md`; winner
      Silk.NET.OpenAL + bundled Soft natives
- [x] `[E4/G1]` Device playback engine: `src/AudioEq.Engine` — `IEqEngine`
      contract + `MockEqEngine` (UI usable with no device) +
      `OpenAlPlaybackEngine` (Float32 streaming queue refill thread runs the
      `EqChain` in real time, lock-free chain swap from `ApplyGains`,
      enumeration + underrun counter); 4 tests (mock state/processing,
      device smoke: streams to completion on real hardware)
- [ ] `[E4/G1]` Audition flow: render-based hot-swap (temp WAV from current
      state, like the Dart project — `OfflineRenderer.RenderToPcm16` is the
      ready building block) **or** live byte processing via `IEqEngine` —
      decision recorded in the backend decision doc; bypass serves the
      untouched original; honest banner updated when G1 is honestly met
- [ ] `[E4/G1]` Transport: play/pause, track label, loop for audition
- [x] `[E7/*]` Quality gates green: `dotnet build` 0 warnings / 0 errors +
      `dotnet test` 67/67 passing as one command; recorded in README

## P1 — Make it trustworthy and observable

- [ ] `[E5/G5]` Static EQ response curve: log-frequency grid, summed
      magnitude from the biquad chain, band markers, preamp included — drawn
      with `ImDrawList` inside the starter panel (core math already exposed
      + tested: `EqChain.MagnitudeResponseDb`, curve tests; widget open)
- [ ] `[E3/G4]` Truly parametric editing: per-band Q + filter type
      (PK/LS/HS/notch) in the UI with persistence; close the pyramid's
      "peaking/shelf/notch" promise; unit tests
- [ ] `[E3/G4]` Preamp applied end-to-end (UI slider exists; wire it into
      engine playback (`IEqEngine`), render path already applies it, APO
      round-trip — no band smear)
- [ ] `[E3/G4]` Callback-side parameter smoothing: one-pole ramp on gain/Q
      changes; slider drags debounced (trailing coalesce) so rapid gestures
      produce one coefficient update stream
- [ ] `[E2/G3]` Named preset store: JSON CRUD in the user config directory
      (or next to the exe for portable use); migrate today's last-used gains
- [ ] `[E2/G3]` Bundle ≥20 AutoEQ-derived headphone presets as embedded
      assets with attribution shown in UI
- [ ] `[E2/G3]` Preset library UI: searchable browser (model search,
      source attribution, apply + save-a-copy); replaces the plain band row
      once the bundle exceeds ~8 items
- [ ] `[E5/G5]` Live spectrum: SPSC ring buffer fed by the audio callback,
      FFT (FftSharp or hand-rolled radix-2) rendered behind the same plot
- [ ] `[E3/G4]` Golden vectors: `tool/golden_vectors.py` (NumPy reference
      responses) consumed as xUnit theory data to catch coefficient bugs
- [ ] `[E7/*]` Engine conformance tests: same gain/enable/clamp suite runs
      against mock + offline-render + OpenAL backends (mock + contract
      already exist from the device-playback item; extend the suite)

## P2 — Tier 2 + shippable

- [ ] `[E1/G6]` Windows device backend verified end-to-end (WASAPI shared
      mode); document any APO integration constraints (blocked on shipping
      `IEqEngine` from P0 — contract exists, backend swap is startup wiring)
- [ ] `[E6/G2]` Linux system-wide path: export a PipeWire filter-chain
      preset (squigwire/eq-cli pattern); document exact PipeWire version +
      activation commands in README
- [ ] `[E1/G6]` macOS CoreAudio backend behind the capability flag; swap in
      startup wiring only — zero `DrawUI()` changes
- [ ] `[E6/*]` Capability flag: platform announces native-DSP support the
      same way for every backend; tier-2 banners disappear as flags flip
- [ ] `[E7/*]` CI (GitHub Actions or equivalent): build + test on push;
      publish artifacts on tag; `dotnet list package --outdated` monthly
- [ ] `[E6/G1]` Output device selection + volume (default sink only today;
      OpenAL enumeration surface exists on `OpenAlPlaybackEngine`)
- [ ] `[E8/G7]` LICENSE file — decide BEFORE importing any GPL-derived code
      or accepting contributions; unblocks the whole release track
- [ ] `[E8/G7]` Identity: app icon, display name, versioning scheme
- [ ] `[E8/G7]` Packaging, one format per Tier-1 OS (Linux: pick ONE of
      .deb/AppImage/Flatpak; Windows: .msi or self-contained zip)
- [ ] `[E7/G7]` Real-machine matrix before any release claim: distro ×
      driver versions actually tested, recorded in README

## Backlog (do NOT promote without a goal)

- DAW plugin formats (VST3/LV2/CLAP) — needs a goal first, currently non-goal
- Room-correction measurement (REW-style) — needs a goal first
- Convolution / FIR engine (CamillaDSP-class) — only if IIR proves insufficient
- FFI/SIMD DSP in C++/Rust — only if profiling demands; principle 4 first
- Live byte-stream audition instead of render-based hot-swap — decide in the
  E4 item (engine now exists; decide when audition flow starts)
- System-wide EQ via APO/driver install automation — needs a goal + safety
  review (principle 7)
- Multi-viewport / docking ImGui — needs a goal first
- Per-app profiles, loudness compensation, crossfeed — capture as G/E first
- Recording (capture + save WAV) — needs a goal first
- Streaming radio / URL playback — needs a goal first
- First-run onboarding explainer — needs a goal first
- i18n — needs a goal first
- Cloud sync / accounts — explicitly out of scope until vision changes

## Working agreement

- One task in flight at a time; check `[~]` with date + blocker when paused.
- Every completion names its `[Ex/Gx]`; pyramid violations get re-scoped,
  not merged.
- Review monthly: promote at most 2 backlog items, retire stale ones.
