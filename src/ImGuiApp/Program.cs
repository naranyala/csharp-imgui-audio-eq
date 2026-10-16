using ImGuiApp;
using Silk.NET.Maths;
using Silk.NET.Windowing;

var options = WindowOptions.Default;
options.Title = "ImGui Audio EQ - Starter";
options.Size = new Vector2D<int>(1280, 720);
options.VSync = true;
options.API = new GraphicsAPI(
    ContextAPI.OpenGL,
    ContextProfile.Core,
    ContextFlags.ForwardCompatible,
    new APIVersion(3, 3));

using var window = Window.Create(options);
using var app = new Application(window);
window.Run();
