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

## Audio: engine tests or playback fail

- **`OpenAlPlaybackEngine` reports `IsAvailable == false`**: OpenAL Soft
  could not open a device. On Linux check the sound stack is running:
  `pactl info` (PipeWire/PulseAudio) and `aplay -l` (ALSA devices). The
  bundled Soft natives do not need a distro package.
- **Test soft-skips with no message**: `OpenAlEngine_SinesToDeviceAndComplete`
  returns early when no device is present — that is by design; the mock
  tests still prove the contract.
- **Sound plays but is silent**: check the system mixer (PipeWire/Pulse)
  volume and the active sink (`pactl list short sinks`).
- **Underrun counter grows**: the refill thread fell behind (heavy system
  load or a tiny buffer). Buffers are 1024 frames (~23 ms at 44.1 kHz);
  raise `FramesPerBlock` in `OpenAlPlaybackEngine` if your machine needs
  more headroom.
- **No sound from the app UI**: expected today — the UI is not wired to
  the engine yet (honest UI-only banner in the starter panel; see TODOS.md
  audition-flow item).

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
