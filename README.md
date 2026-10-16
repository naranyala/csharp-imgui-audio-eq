# C# ImGui Audio EQ (Starter)

A cross-platform .NET desktop app starter built with **Dear ImGui** (via ImGui.NET) and **Silk.NET** (GLFW + OpenGL 3.3). It ships a working window, input handling, and a ready-to-extend UI scaffold aimed at becoming an audio equalizer.

## Features

- Immediate-mode UI with the official ImGui demo window and a starter "Audio EQ" panel
- OpenGL 3.3 core renderer with full GL state save/restore around ImGui draw calls
- Keyboard, mouse, scroll, focus, and modifier input mapped to ImGui
- Font atlas baked at startup (no external font files required)
- DPI/framebuffer-scale aware rendering, vsync, resizable window
- Zero warnings, zero errors on build (verified on Linux/Wayland)

## Tech stack

| Component    | Package                | Version |
| ------------ | ---------------------- | ------- |
| UI           | ImGui.NET              | 1.91.6.1 |
| Windowing    | Silk.NET.Windowing     | 2.23.0 |
| Graphics     | Silk.NET.OpenGL        | 2.23.0 |
| Input        | Silk.NET.Input         | 2.23.0 |
| Runtime      | .NET                   | 10.0 (net10.0) |

## Quick start

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download) and a GPU/driver with OpenGL 3.3+ (any desktop from the last decade).

```bash
dotnet build
dotnet run --project src/ImGuiApp
```

## Project layout

```
CsharpImGuiAudioEq.slnx
PYRAMID-OF-INTENTS.md  vision → principles → goals → epics
TODOS.md               phased task tracker with [Ex/Gx] intent tags
docs/
  getting-started.md    setup, build, run, publish
  architecture.md       components and frame lifecycle
  imgui-controller.md   how the ImGui <-> Silk.NET bridge works
  roadmap.md            plan for growing this into a real audio EQ
  troubleshooting.md    common errors and fixes
src/ImGuiApp/
  Program.cs            window + OpenGL context creation
  Application.cs        frame callbacks and the starter EQ UI
  ImGuiController.cs    input, font atlas, and GL rendering bridge
  ImGuiApp.csproj       project and package references
```

## Documentation

- [`PYRAMID-OF-INTENTS.md`](PYRAMID-OF-INTENTS.md) — vision → principles → goals → epics
- [`TODOS.md`](TODOS.md) — phased tracker (3 done / 29 open)
- [Getting started](docs/getting-started.md)
- [Architecture](docs/architecture.md)
- [ImGui controller internals](docs/imgui-controller.md)
- [Roadmap to a full audio EQ](docs/roadmap.md)
- [Troubleshooting](docs/troubleshooting.md)

## Where to start hacking

- Change the UI in `Application.DrawUI()` (src/ImGuiApp/Application.cs)
- Change window properties in `Program.cs` (title, size, vsync, GL version)
- Renderer details (shaders, blending, draw submission) live in `ImGuiController.cs`
