# Troubleshooting

## Build fails immediately after cloning

```bash
dotnet --version   # must be 10.x
dotnet restore
dotnet build
```

- Wrong SDK: install .NET 10 SDK; `global.json` can be added to pin the version.
- Locked packages behind a proxy: `dotnet nuget locals all --clear` then restore again.

Expected healthy output: `Build succeeded. 0 Warning(s) 0 Error(s)`.

## Window does not open / app exits instantly

Run in the foreground to see the exception:

```bash
dotnet run --project src/ImGuiApp
```

- **Headless / no display**: GLFW needs `DISPLAY` (X11/XWayland) or `WAYLAND_DISPLAY`. Over SSH use `ssh -X` or run on the desktop session.
- **Shader compile error**: thrown as `InvalidOperationException` containing the GLSL log — shown in the terminal output.
- **OpenGL below 3.3**: the context request in `Program.cs` fails. Older machines can try changing the API version to `2, 1` — but the ImGui shaders here are `#version 330 core`, so the shader code would need a GLSL 120 rewrite instead.

## Black window, no UI (no errors)

- GPU driver issue: check `glxinfo -B` (Linux) reports OpenGL 3.3+.
- Framebuffer scale zero: happens if the window is 0-sized (minimized) — the controller early-outs; resizing the window restores rendering.

## Mouse/keyboard not reaching ImGui

- Focus events are wired via `IWindow.FocusChanged`; clicking the window should restore input.
- Only the keyboards/mice present **at startup** are subscribed (in `ImGuiController`'s constructor). Hot-plugged devices are not attached — extend the constructor with `input.GamepadConnected`-style events if needed.

## Text input produces wrong characters

`IKeyboard.KeyChar` delivers a `char`; codepoints outside the BMP are not representable. Rare for an EQ UI; if needed, switch to UTF-16 surrogate pair handling.

## Performance

- VSync is on by default (`options.VSync = true`) — disable it to measure raw frame rate.
- Vertex/index buffers are re-uploaded every frame with `StreamDraw`; that is normal for ImGui and cheap for typical UI sizes.
- The FPS readout in the starter panel (`ImGui.GetIO().Framerate`) is smoothed by ImGui.

## Wayland vs X11

Verified on Wayland through XWayland (`DISPLAY=:0` + `WAYLAND_DISPLAY=wayland-0`). Native Wayland window creation depends on the GLFW build bundled with Silk.NET; if window decorations or input misbehave, running under XWayland is the fallback.

## Cleaning

```bash
dotnet clean
git clean -xdf     # also removes bin/obj (careful: untracked files)
```
