# ImGui controller internals

`src/ImGuiApp/ImGuiController.cs` is the entire bridge between Silk.NET (window/input/GL) and ImGui.NET. This document explains each part so it can be extended confidently.

## Construction

```csharp
ImGui.CreateContext();
io.ConfigFlags  |= ImGuiConfigFlags.NavEnableKeyboard;
io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
CreateDeviceObjects();          // shaders + VAO/VBO/IBO + font texture
// subscribe input events
// subscribe window focus
```

### Device objects

- Compiles the vertex/fragment shaders (`#version 330 core`) and throws `InvalidOperationException` with the shader log on failure.
- Creates a VAO with three attributes at stride 20:
  - location 0: `Pos`  (float x2, offset 0)
  - location 1: `UV`   (float x2, offset 8)
  - location 2: `Color` (u8 normalized x4, offset 16)
- Bakes the default font: `io.Fonts.GetTexDataAsRGBA32` -> `glTexImage2D` (RGBA8, linear filtering, clamp-to-edge) -> `io.Fonts.SetTexID` -> `ClearTexData()` frees the CPU copy.

## Input path

Event-driven; handlers push straight into ImGui IO as they fire:

| Silk.NET event              | ImGui IO call                  |
| --------------------------- | ------------------------------ |
| `IKeyboard.KeyDown/KeyUp`   | `io.AddKeyEvent(mapped, bool)` |
| `IKeyboard.KeyChar`         | `io.AddInputCharacter(char)`   |
| `IMouse.MouseMove`          | `io.AddMousePosEvent(x, y)`    |
| `IMouse.MouseDown/Up`       | `io.AddMouseButtonEvent(i, b)` |
| `IMouse.Scroll`             | `io.AddMouseWheelEvent(x, y)`  |
| `IWindow.FocusChanged`      | `io.AddFocusEvent(bool)`       |

Modifier keys cannot be event-mapped directly, so `UpdateModifiers()` polls `IsKeyPressed` for `ControlLeft/Right`, `ShiftLeft/Right`, `AltLeft/Right`, `SuperLeft/Right` each frame and emits `ImGuiKey.ModCtrl/ModShift/ModAlt/ModSuper`.

### Key mapping

`MapKey(Key)` translates `Silk.NET.Input.Key` to `ImGuiKey`:

- Contiguous ranges use arithmetic: `A-Z`, `Number0-9` -> `_0.._9`, `F1-F24`.
- Everything else is an explicit `switch` (arrows, navigation, keypad, punctuation, modifiers).
- Unknown keys return `ImGuiKey.None` and are not forwarded.

Note the spelling traps: Silk uses `Key.BackSlash` (capital S), `Key.ShiftLeft` (not `LeftShift`), `Key.Number0` (not `D0`), and `Key.GraveAccent`.

## Frame API

### `UpdateIMGui()`

Called first in the `Update` callback:

1. `io.DisplaySize` = logical window size; `io.DisplayFramebufferScale` = framebuffer / logical size (handles DPI scaling; scissor and viewport math rely on this).
2. `io.DeltaTime` from a `Stopwatch`, clamped to a sane fallback (1/60 s) for the first frame.
3. Modifier polling (above).
4. `ImGui.NewFrame()` — starts the ImGui frame.

### `RenderImGui()`

Called after `glClear` in the `Render` callback:

1. `ImGui.Render()` then `ImGui.GetDrawData()`.
2. Early-out if the framebuffer is 0x0 (minimized) or there are no command lists.
3. `drawData.ScaleClipRects(framebufferScale)` — clip rects are in points, GL wants pixels.
4. **Backup GL state** (see below).
5. **Setup render state**: blend on (src-alpha / one-minus-src-alpha), cull/depth/stencil off, scissor on, polygon mode fill, full viewport, ortho matrix uploaded, program/texture unit/VAO bound.
6. **Submit**: for every `ImDrawList`, stream its vertex/index buffers with `BufferUsageARB.StreamDraw`; for every `ImDrawCmd`, apply scissor (y-flipped against `fbHeight`), bind `cmd.TextureId`, and issue `DrawElementsBaseVertex` with `IdxOffset`/`VtxOffset`.
7. **Restore GL state**.

## GL state backup/restore

Saved and restored so the host renderer is untouched:

- viewport, scissor box, polygon mode, unpack alignment
- active texture unit, current program, `TEXTURE_2D` binding, array/element buffer, VAO
- enable bits: blend, cull face, depth test, stencil test, scissor test
- blend: src/dst rgb + alpha factors, blend equations (rgb + alpha)

`UserCallback` commands (custom ImGui callbacks) are skipped — the starter has no use for them.

## Disposal

`Dispose()` unsubscribes `FocusChanged`, destroys the ImGui context, and deletes program, buffers, VAO, and the font texture. It runs while the GL context is still alive because `Application` is disposed before the window (`using` order in `Program.cs`).

## Extension ideas

- **Custom font**: load a TTF before `CreateDeviceObjects()` via `io.Fonts.AddFontFromFileTTF`, then rebuild the font texture (extract font texture creation into a method you can call again).
- **Mouse cursor shapes**: map `ImGui.GetMouseCursor()` to `ICursor.StandardCursor`.
- **Texture from your own data**: upload with a separate GL texture ID and pass it via `ImGui.Image((IntPtr)id, size, ...)` — `cmd.TextureId` routing already handles it.
