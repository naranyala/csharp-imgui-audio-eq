# Architecture

## Components

```
Program.cs
  └─ creates IWindow (OpenGL 3.3 core, vsync, 1280x720)
      └─ Application            frame callbacks + UI state
           ├─ GL                Silk.NET OpenGL binding
           ├─ IInputContext     keyboards/mice from Silk.NET.Input
           └─ ImGuiController   the bridge between Silk.NET and ImGui
                ├─ input events -> ImGui IO
                ├─ font atlas   -> GL texture
                └─ draw data    -> GL draw calls
```

| File                     | Responsibility |
| ------------------------ | -------------- |
| `Program.cs`             | Entry point. Configures `WindowOptions` (title, size, vsync, graphics API) and starts the window run loop. |
| `Application.cs`         | Subscribes to `Load`, `Update`, `Render`, `FramebufferResize`. Owns all application UI state (gain, cutoff, band array) and draws the widgets in `DrawUI()`. |
| `ImGuiController.cs`     | Owns the ImGui context, input wiring, font texture, GPU buffers, shaders, and draw submission. Also saves/restores GL state so ImGui never leaks state into the host app. |

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

## Graphics details

- Context: OpenGL **3.3 core**, forward compatible, context flags `ForwardCompatible`.
- Vertex layout (matches `ImDrawVert`, 20 bytes): `vec2 pos`, `vec2 uv`, `u8x4 color`.
- Index type: `ushort` (`ImDrawIdx`).
- One shader program (`uProj`, `uTexture` uniforms), orthographic projection built from `DisplayPos`/`DisplaySize`.
- `glDrawElementsBaseVertex` is used so multi-list meshes with `VtxOffset` work (`ImGuiBackendFlags.RendererHasVtxOffset` is enabled).
- Scissor rects are converted to framebuffer coordinates (y flipped) before `glScissor`.

## Multi-viewport note

Viewports (dragging windows out of the main window) are **not** enabled. Enabling them additionally requires `ImGuiBackendFlags.PlatformHasViewports` / `RendererHasViewports` and per-viewport window creation — deliberately out of scope for the starter.
