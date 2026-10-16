using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace ImGuiApp;

internal sealed class Application : IDisposable
{
    private readonly IWindow _window;
    private GL _gl = null!;
    private IInputContext _input = null!;
    private ImGuiController? _controller;

    private bool _showDemo = true;
    private bool _showMetrics = false;
    private float _gainDb;
    private float _cutoffHz = 1000f;
    private int _bandCount = 5;
    private readonly float[] _bandGains = new float[16];

    public Application(IWindow window)
    {
        _window = window;
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.FramebufferResize += OnFramebufferResize;
    }

    private void OnLoad()
    {
        _gl = _window.CreateOpenGL();
        _gl.ClearColor(0.10f, 0.10f, 0.12f, 1.0f);

        _input = _window.CreateInput();
        _controller = new ImGuiController(_gl, _window, _input);
    }

    private void OnFramebufferResize(Vector2D<int> size)
    {
        if (size.X > 0 && size.Y > 0)
        {
            _gl.Viewport(size);
        }
    }

    private void OnUpdate(double deltaSeconds)
    {
        if (_controller is null)
        {
            return;
        }

        _controller.UpdateIMGui();
        DrawUI();
    }

    private void OnRender(double deltaSeconds)
    {
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        _controller?.RenderImGui();
    }

    private void DrawUI()
    {
        if (_showDemo)
        {
            ImGui.ShowDemoWindow(ref _showDemo);
        }

        if (_showMetrics)
        {
            ImGui.ShowMetricsWindow(ref _showMetrics);
        }

        ImGui.SetNextWindowPos(new Vector2(20, 20), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(360, 420), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Audio EQ (starter)", ref _showDemo))
        {
            ImGui.TextUnformatted($"FPS: {ImGui.GetIO().Framerate:0.0}");
            ImGui.Separator();

            ImGui.SliderFloat("Master gain (dB)", ref _gainDb, -24f, 12f);
            ImGui.SliderFloat("Cutoff (Hz)", ref _cutoffHz, 20f, 20000f, "%.0f Hz", ImGuiSliderFlags.Logarithmic);
            ImGui.SliderInt("Bands", ref _bandCount, 1, 16);

            ImGui.Separator();
            ImGui.TextUnformatted("Band gains");

            for (int i = 0; i < _bandCount; i++)
            {
                ImGui.PushID(i);
                ImGui.SliderFloat("##gain", ref _bandGains[i], -24f, 12f);
                ImGui.SameLine();
                ImGui.TextUnformatted($"Band {i + 1}: {_bandGains[i]:0.0} dB");
                ImGui.PopID();
            }

            ImGui.Separator();
            ImGui.Checkbox("Show demo window", ref _showDemo);
            ImGui.Checkbox("Show metrics window", ref _showMetrics);

            if (ImGui.Button("Reset"))
            {
                _gainDb = 0f;
                _cutoffHz = 1000f;
                Array.Clear(_bandGains);
            }
        }
        ImGui.End();
    }

    public void Dispose()
    {
        _controller?.Dispose();
        _input.Dispose();
        _gl.Dispose();
    }
}
