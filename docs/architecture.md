# Architecture

## Components

```
Program.cs
  └─ creates IWindow (OpenGL 3.3 core, vsync, 1280x720)
      └─ Application            frame callbacks + UI state
           ├─ GL                Silk.NET OpenGL binding
           ├─ IInputContext     keyboards/mice from Silk.NET.Input
           ├─ ImGuiController   the bridge between Silk.NET and ImGui
           │    ├─ input events -> ImGui IO
           │    ├─ font atlas   -> GL texture
           │    └─ draw data    -> GL draw calls
           └─ (planned) IEqEngine   audio playback via AudioEq.Engine
                                   — not wired yet (audition-flow TODO)

AudioEq.Core    pure DSP + WAV + APO (no I/O, no UI, fully unit-tested)
  ├─ Dsp/Biquad, Dsp/EqChain
  ├─ Audio/WavData, Audio/OfflineRenderer
  └─ Presets/ApoPreset (+ extensions)

AudioEq.Engine  playback backends behind one contract
  ├─ IEqEngine / EqEngineBase
  ├─ OpenAlPlaybackEngine   OpenAL Soft, Float32 streaming queue
  └─ MockEqEngine           wall-clock fallback (no device needed)
```

| Project / File        | Responsibility |
| --------------------- | -------------- |
| `Program.cs`          | Entry point. Configures `WindowOptions` (title, size, vsync, graphics API) and starts the window run loop. |
| `Application.cs`      | Subscribes to `Load`, `Update`, `Render`, `FramebufferResize`. Owns all application UI state (gain, cutoff, band array) and draws the widgets in `DrawUI()`. |
| `ImGuiController.cs`  | Owns the ImGui context, input wiring, font texture, GPU buffers, shaders, and draw submission. Also saves/restores GL state so ImGui never leaks state into the host app. |
| `AudioEq.Core`        | Pure functions and classes: RBJ biquads, N-band chain with live `SetGains`, magnitude-response curves, WAV read/write, offline render, APO parse/serialize. No audio device, no window — safe to test everywhere. |
| `AudioEq.Engine`      | `IEqEngine` is the only type the UI will use for playback. `OpenAlPlaybackEngine` streams Float32 blocks through the live `EqChain` on a refill thread (lock-free gain swap, underrun counter); `MockEqEngine` implements the same contract without a device. |

## Frame lifecycle

Silk.NET's `window.Run()` drives two events every frame:

```
PollEvents
  └─ input events fire (key/mouse/scroll/focus)
     └─ ImGuiController handlers push them into ImGui IO (io.Add*Event)

Update(delta)
  └─ Application.OnUpdate
       ├─ ImGuiController.UpdateIMGui()
       │    ├─ io.DisplaySize / io.DisplayFramebufferScale (DPI aware)
       │    ├─ io.DeltaTime (Stopwatch based)
       │    ├─ poll modifier keys (Ctrl/Shift/Alt/Super)
       │    └─ ImGui.NewFrame()
       └─ Application.DrawUI()      <- build all windows/widgets here

Render(delta)
  └─ Application.OnRender
       ├─ glClear                   <- clear the backbuffer
       └─ ImGuiController.RenderImGui()
            ├─ ImGui.Render()       <- finalize frame, produce ImDrawData
            ├─ backup GL state
            ├─ setup ortho projection, blend/scissor state
            ├─ for each ImDrawList:
            │    ├─ upload vtx/idx buffers (StreamDraw)
            │    └─ for each ImDrawCmd: scissor + bind texture + draw
            └─ restore GL state
```

Key rule: **all ImGui widget calls must happen between `UpdateIMGui()` (which calls `ImGui.NewFrame()`) and `RenderImGui()` (which calls `ImGui.Render()`).** In this codebase that window is exactly `Application.DrawUI()`.

## UI state

`Application` keeps plain fields (`_gainDb`, `_bandGains`, ...). ImGui is immediate mode: the UI reads and writes those fields every frame. There is no retained widget tree to manage.

Window open/close state is passed via `ref` (`ImGui.ShowDemoWindow(ref _showDemo)`), and per-widget IDs via `ImGui.PushID(i)` so repeated sliders with the same label stay unique.

The starter panel shows an explicit **UI-only** banner: sliders do not
process audio yet (principle 1 — no fake functionality). The banner is
removed only when the audition flow honestly connects UI → engine.

## Audio path (contract-first)

The UI must never touch a backend directly (principle 2). Playback goes
through `IEqEngine`:

```
UI thread                          audio/refill thread
─────────                          ───────────────────
ApplyGains(float[] gains)   →      lock-free chain swap
                                   (EqChain.SetGains — state preserved,
                                    no rebuild, no reset click)
Play(buffer, rate, ch)      →      OpenAL Soft refill loop:
                                   unqueue Float32 → ProcessInPlace
                                   per channel → requeue
                                   (no allocs, underruns counted)
```

`MockEqEngine` implements the same contract on a wall-clock thread, so UI
development and conformance tests run on machines without audio hardware.

Wired state: engine exists and is tested end-to-end on real hardware; the
UI has not been connected yet — that is the audition-flow task in TODOS.md.

## Graphics details

- Context: OpenGL **3.3 core**, forward compatible, context flags `ForwardCompatible`.
- Vertex layout (matches `ImDrawVert`, 20 bytes): `vec2 pos`, `vec2 uv`, `u8x4 color`.
- Index type: `ushort` (`ImDrawIdx`).
- One shader program (`uProj`, `uTexture` uniforms), orthographic projection built from `DisplayPos`/`DisplaySize`.
- `glDrawElementsBaseVertex` is used so multi-list meshes with `VtxOffset` work (`ImGuiBackendFlags.RendererHasVtxOffset` is enabled).
- Scissor rects are converted to framebuffer coordinates (y flipped) before `glScissor`.

## Multi-viewport note

Viewports (dragging windows out of the main window) are **not** enabled. Enabling them additionally requires `ImGuiBackendFlags.PlatformHasViewports` / `RendererHasViewports` and per-viewport window creation — deliberately out of scope for the starter.
