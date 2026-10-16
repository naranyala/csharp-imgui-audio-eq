using System.Numerics;
using System.Runtime.InteropServices;
using System.Diagnostics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace ImGuiApp;

[StructLayout(LayoutKind.Sequential)]
internal struct ImGuiVertex
{
    public Vector2 Pos;
    public Vector2 UV;
    public uint Col;
}

internal sealed unsafe class ImGuiController : IDisposable
{
    private readonly GL _gl;
    private readonly IWindow _window;
    private readonly List<IKeyboard> _keyboards = new();
    private ICursor? _cursor;

    private uint _program;
    private int _projUniform;
    private int _textureUniform;
    private uint _vao;
    private uint _vbo;
    private uint _ibo;
    private uint _fontTexture;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastFrameSeconds;

    public ImGuiController(GL gl, IWindow window, IInputContext input)
    {
        _gl = gl;
        _window = window;

        ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

        CreateDeviceObjects();

        foreach (var keyboard in input.Keyboards)
        {
            _keyboards.Add(keyboard);
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
            keyboard.KeyChar += OnKeyChar;
        }

        foreach (var mouse in input.Mice)
        {
            _cursor ??= mouse.Cursor;
            mouse.MouseMove += OnMouseMove;
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.Scroll += OnScroll;
        }

        _window.FocusChanged += OnFocusChanged;
    }

    public void UpdateIMGui()
    {
        var io = ImGui.GetIO();

        var fbSize = _window.FramebufferSize;
        var winSize = _window.Size;

        io.DisplaySize = new Vector2(winSize.X, winSize.Y);
        io.DisplayFramebufferScale = winSize.X > 0 && winSize.Y > 0
            ? new Vector2((float)fbSize.X / winSize.X, (float)fbSize.Y / winSize.Y)
            : Vector2.One;

        var now = _clock.Elapsed.TotalSeconds;
        var deltaTime = _lastFrameSeconds > 0 ? (float)(now - _lastFrameSeconds) : 1f / 60f;
        _lastFrameSeconds = now;
        io.DeltaTime = deltaTime > 0f ? deltaTime : 1f / 60f;

        UpdateModifiers(io);

        io.MouseDrawCursor = false;
        if (_cursor is not null)
        {
            _cursor.CursorMode = CursorMode.Normal;
        }

        ImGui.NewFrame();
    }

    public void RenderImGui()
    {
        ImGui.Render();

        var drawData = ImGui.GetDrawData();
        var fbWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
        var fbHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
        if (fbWidth <= 0 || fbHeight <= 0 || drawData.CmdListsCount == 0)
        {
            return;
        }

        drawData.ScaleClipRects(drawData.FramebufferScale);

        var state = BackupState();
        SetupRenderState(drawData, fbWidth, fbHeight);

        var clipOff = drawData.DisplayPos;
        var clipScale = drawData.FramebufferScale;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var list = drawData.CmdLists[n];

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(list.VtxBuffer.Size * sizeof(ImGuiVertex)),
                (void*)list.VtxBuffer.Data,
                BufferUsageARB.StreamDraw);

            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ibo);
            _gl.BufferData(
                BufferTargetARB.ElementArrayBuffer,
                (nuint)(list.IdxBuffer.Size * sizeof(ushort)),
                (void*)list.IdxBuffer.Data,
                BufferUsageARB.StreamDraw);

            for (int cmdIndex = 0; cmdIndex < list.CmdBuffer.Size; cmdIndex++)
            {
                var cmd = list.CmdBuffer[cmdIndex];

                if (cmd.UserCallback != IntPtr.Zero)
                {
                    continue;
                }

                var clipMin = new Vector2(
                    (cmd.ClipRect.X - clipOff.X) * clipScale.X,
                    (cmd.ClipRect.Y - clipOff.Y) * clipScale.Y);
                var clipMax = new Vector2(
                    (cmd.ClipRect.Z - clipOff.X) * clipScale.X,
                    (cmd.ClipRect.W - clipOff.Y) * clipScale.Y);

                if (clipMax.X <= clipMin.X || clipMax.Y <= clipMin.Y)
                {
                    continue;
                }

                int scissorX = (int)clipMin.X;
                int scissorY = (int)(fbHeight - clipMax.Y);
                int scissorW = (int)(clipMax.X - clipMin.X);
                int scissorH = (int)(clipMax.Y - clipMin.Y);

                _gl.Scissor(scissorX, scissorY, (uint)scissorW, (uint)scissorH);

                _gl.BindTexture(TextureTarget.Texture2D, (uint)cmd.TextureId);

                _gl.DrawElementsBaseVertex(
                    PrimitiveType.Triangles,
                    (uint)cmd.ElemCount,
                    DrawElementsType.UnsignedShort,
                    (void*)((byte*)list.IdxBuffer.Data + cmd.IdxOffset * sizeof(ushort)),
                    (int)cmd.VtxOffset);
            }
        }

        RestoreState(state);
    }

    private void CreateDeviceObjects()
    {
        _program = CreateProgram();
        _projUniform = _gl.GetUniformLocation(_program, "uProj");
        _textureUniform = _gl.GetUniformLocation(_program, "uTexture");

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ibo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, GLEnum.Float, false, (uint)sizeof(ImGuiVertex), (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, GLEnum.Float, false, (uint)sizeof(ImGuiVertex), (void*)8);
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 4, GLEnum.UnsignedByte, true, (uint)sizeof(ImGuiVertex), (void*)16);

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ibo);
        _gl.BindVertexArray(0);

        CreateFontTexture();
    }

    private void CreateFontTexture()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int _);

        _fontTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _fontTexture);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        _gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            InternalFormat.Rgba,
            (uint)width,
            (uint)height,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            (void*)pixels);

        io.Fonts.SetTexID((IntPtr)_fontTexture);
        io.Fonts.ClearTexData();
    }

    private void SetupRenderState(ImDrawDataPtr drawData, int fbWidth, int fbHeight)
    {
        _gl.Enable(EnableCap.Blend);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.StencilTest);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);

        _gl.Viewport(0, 0, (uint)fbWidth, (uint)fbHeight);

        float l = drawData.DisplayPos.X;
        float r = l + drawData.DisplaySize.X;
        float t = drawData.DisplayPos.Y;
        float b = t + drawData.DisplaySize.Y;

        ReadOnlySpan<float> ortho = stackalloc float[]
        {
            2f / (r - l), 0f, 0f, 0f,
            0f, 2f / (t - b), 0f, 0f,
            0f, 0f, -1f, 0f,
            (r + l) / (l - r), (t + b) / (b - t), 0f, 1f,
        };

        _gl.UseProgram(_program);
        _gl.Uniform1(_textureUniform, 0);
        _gl.UniformMatrix4(_projUniform, false, ortho);

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindVertexArray(_vao);
    }

    private GLState BackupState()
    {
        var s = new GLState();

        int* ip = stackalloc int[4];
        _gl.GetInteger(GetPName.Viewport, ip);
        s.ViewportX = ip[0];
        s.ViewportY = ip[1];
        s.ViewportW = ip[2];
        s.ViewportH = ip[3];

        _gl.GetInteger(GetPName.ScissorBox, ip);
        s.ScissorX = ip[0];
        s.ScissorY = ip[1];
        s.ScissorW = ip[2];
        s.ScissorH = ip[3];

        _gl.GetInteger(GetPName.PolygonMode, ip);
        s.PolygonModeFront = ip[0];

        _gl.GetInteger(GetPName.UnpackAlignment, ip);
        s.UnpackAlignment = ip[0];

        s.ActiveTexture = (uint)_gl.GetInteger(GetPName.ActiveTexture);
        s.Program = (uint)_gl.GetInteger(GetPName.CurrentProgram);
        s.Texture2D = (uint)_gl.GetInteger(GetPName.TextureBinding2D);
        s.ArrayBuffer = (uint)_gl.GetInteger(GetPName.ArrayBufferBinding);
        s.ElementArrayBuffer = (uint)_gl.GetInteger(GetPName.ElementArrayBufferBinding);
        s.VertexArray = (uint)_gl.GetInteger(GetPName.VertexArrayBinding);

        s.Blend = _gl.GetBoolean(GetPName.Blend);
        s.CullFace = _gl.GetBoolean(GetPName.CullFace);
        s.DepthTest = _gl.GetBoolean(GetPName.DepthTest);
        s.StencilTest = _gl.GetBoolean(GetPName.StencilTest);
        s.ScissorTest = _gl.GetBoolean(GetPName.ScissorTest);

        s.BlendSrcRgb = _gl.GetInteger(GetPName.BlendSrcRgb);
        s.BlendDstRgb = _gl.GetInteger(GetPName.BlendDstRgb);
        s.BlendSrcAlpha = _gl.GetInteger(GetPName.BlendSrcAlpha);
        s.BlendDstAlpha = _gl.GetInteger(GetPName.BlendDstAlpha);
        s.BlendEquationRgb = _gl.GetInteger(GetPName.BlendEquationRgb);
        s.BlendEquationAlpha = _gl.GetInteger(GetPName.BlendEquationAlpha);

        return s;
    }

    private void RestoreState(in GLState s)
    {
        _gl.Viewport(s.ViewportX, s.ViewportY, (uint)s.ViewportW, (uint)s.ViewportH);
        _gl.Scissor(s.ScissorX, s.ScissorY, (uint)s.ScissorW, (uint)s.ScissorH);
        _gl.PolygonMode(TriangleFace.FrontAndBack, (PolygonMode)s.PolygonModeFront);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, s.UnpackAlignment);

        _gl.UseProgram(s.Program);
        _gl.ActiveTexture((TextureUnit)s.ActiveTexture);
        _gl.BindTexture(TextureTarget.Texture2D, s.Texture2D);
        _gl.BindVertexArray(s.VertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, s.ArrayBuffer);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, s.ElementArrayBuffer);

        SetCap(EnableCap.Blend, s.Blend);
        SetCap(EnableCap.CullFace, s.CullFace);
        SetCap(EnableCap.DepthTest, s.DepthTest);
        SetCap(EnableCap.StencilTest, s.StencilTest);
        SetCap(EnableCap.ScissorTest, s.ScissorTest);

        _gl.BlendEquationSeparate(
            (BlendEquationModeEXT)s.BlendEquationRgb,
            (BlendEquationModeEXT)s.BlendEquationAlpha);
        _gl.BlendFuncSeparate(
            (BlendingFactor)s.BlendSrcRgb,
            (BlendingFactor)s.BlendDstRgb,
            (BlendingFactor)s.BlendSrcAlpha,
            (BlendingFactor)s.BlendDstAlpha);
    }

    private void SetCap(EnableCap cap, bool enabled)
    {
        if (enabled)
        {
            _gl.Enable(cap);
        }
        else
        {
            _gl.Disable(cap);
        }
    }

    private void UpdateModifiers(ImGuiIOPtr io)
    {
        bool ctrl = false, shift = false, alt = false, super = false;
        foreach (var kb in _keyboards)
        {
            ctrl |= kb.IsKeyPressed(Key.ControlLeft) || kb.IsKeyPressed(Key.ControlRight);
            shift |= kb.IsKeyPressed(Key.ShiftLeft) || kb.IsKeyPressed(Key.ShiftRight);
            alt |= kb.IsKeyPressed(Key.AltLeft) || kb.IsKeyPressed(Key.AltRight);
            super |= kb.IsKeyPressed(Key.SuperLeft) || kb.IsKeyPressed(Key.SuperRight);
        }

        io.AddKeyEvent(ImGuiKey.ModCtrl, ctrl);
        io.AddKeyEvent(ImGuiKey.ModShift, shift);
        io.AddKeyEvent(ImGuiKey.ModAlt, alt);
        io.AddKeyEvent(ImGuiKey.ModSuper, super);
    }

    private static ImGuiKey MapKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            return ImGuiKey.A + ((int)key - (int)Key.A);
        }

        if (key >= Key.Number0 && key <= Key.Number9)
        {
            return ImGuiKey._0 + ((int)key - (int)Key.Number0);
        }

        if (key >= Key.F1 && key <= Key.F24)
        {
            return ImGuiKey.F1 + ((int)key - (int)Key.F1);
        }

        return key switch
        {
            Key.Space => ImGuiKey.Space,
            Key.Enter => ImGuiKey.Enter,
            Key.Tab => ImGuiKey.Tab,
            Key.Backspace => ImGuiKey.Backspace,
            Key.Insert => ImGuiKey.Insert,
            Key.Delete => ImGuiKey.Delete,
            Key.Escape => ImGuiKey.Escape,
            Key.Right => ImGuiKey.RightArrow,
            Key.Left => ImGuiKey.LeftArrow,
            Key.Down => ImGuiKey.DownArrow,
            Key.Up => ImGuiKey.UpArrow,
            Key.PageUp => ImGuiKey.PageUp,
            Key.PageDown => ImGuiKey.PageDown,
            Key.Home => ImGuiKey.Home,
            Key.End => ImGuiKey.End,
            Key.CapsLock => ImGuiKey.CapsLock,
            Key.ScrollLock => ImGuiKey.ScrollLock,
            Key.NumLock => ImGuiKey.NumLock,
            Key.PrintScreen => ImGuiKey.PrintScreen,
            Key.Pause => ImGuiKey.Pause,
            Key.Menu => ImGuiKey.Menu,
            Key.ShiftLeft => ImGuiKey.LeftShift,
            Key.ShiftRight => ImGuiKey.RightShift,
            Key.ControlLeft => ImGuiKey.LeftCtrl,
            Key.ControlRight => ImGuiKey.RightCtrl,
            Key.AltLeft => ImGuiKey.LeftAlt,
            Key.AltRight => ImGuiKey.RightAlt,
            Key.SuperLeft => ImGuiKey.LeftSuper,
            Key.SuperRight => ImGuiKey.RightSuper,
            Key.GraveAccent => ImGuiKey.GraveAccent,
            Key.Apostrophe => ImGuiKey.Apostrophe,
            Key.Comma => ImGuiKey.Comma,
            Key.Minus => ImGuiKey.Minus,
            Key.Period => ImGuiKey.Period,
            Key.Slash => ImGuiKey.Slash,
            Key.Semicolon => ImGuiKey.Semicolon,
            Key.Equal => ImGuiKey.Equal,
            Key.LeftBracket => ImGuiKey.LeftBracket,
            Key.BackSlash => ImGuiKey.Backslash,
            Key.RightBracket => ImGuiKey.RightBracket,
            Key.Keypad0 => ImGuiKey.Keypad0,
            Key.Keypad1 => ImGuiKey.Keypad1,
            Key.Keypad2 => ImGuiKey.Keypad2,
            Key.Keypad3 => ImGuiKey.Keypad3,
            Key.Keypad4 => ImGuiKey.Keypad4,
            Key.Keypad5 => ImGuiKey.Keypad5,
            Key.Keypad6 => ImGuiKey.Keypad6,
            Key.Keypad7 => ImGuiKey.Keypad7,
            Key.Keypad8 => ImGuiKey.Keypad8,
            Key.Keypad9 => ImGuiKey.Keypad9,
            Key.KeypadDecimal => ImGuiKey.KeypadDecimal,
            Key.KeypadDivide => ImGuiKey.KeypadDivide,
            Key.KeypadMultiply => ImGuiKey.KeypadMultiply,
            Key.KeypadSubtract => ImGuiKey.KeypadSubtract,
            Key.KeypadAdd => ImGuiKey.KeypadAdd,
            Key.KeypadEnter => ImGuiKey.KeypadEnter,
            Key.KeypadEqual => ImGuiKey.KeypadEqual,
            _ => ImGuiKey.None,
        };
    }

    private uint CompileShader(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            var log = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new InvalidOperationException($"Shader compile failed: {log}");
        }

        return shader;
    }

    private uint CreateProgram()
    {
        var vs = CompileShader(ShaderType.VertexShader, VertexShaderSource);
        var fs = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);

        var program = _gl.CreateProgram();
        _gl.AttachShader(program, vs);
        _gl.AttachShader(program, fs);
        _gl.LinkProgram(program);

        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
        {
            var log = _gl.GetProgramInfoLog(program);
            throw new InvalidOperationException($"Program link failed: {log}");
        }

        _gl.DetachShader(program, vs);
        _gl.DetachShader(program, fs);
        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);

        return program;
    }

    private void OnFocusChanged(bool focused) => ImGui.GetIO().AddFocusEvent(focused);

    private void OnKeyDown(IKeyboard keyboard, Key key, int scanCode)
    {
        var mapped = MapKey(key);
        if (mapped != ImGuiKey.None)
        {
            ImGui.GetIO().AddKeyEvent(mapped, true);
        }
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int scanCode)
    {
        var mapped = MapKey(key);
        if (mapped != ImGuiKey.None)
        {
            ImGui.GetIO().AddKeyEvent(mapped, false);
        }
    }

    private void OnKeyChar(IKeyboard keyboard, char character)
    {
        ImGui.GetIO().AddInputCharacter(character);
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        ImGui.GetIO().AddMousePosEvent(position.X, position.Y);
    }

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        ImGui.GetIO().AddMouseButtonEvent((int)button, true);
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        ImGui.GetIO().AddMouseButtonEvent((int)button, false);
    }

    private void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        ImGui.GetIO().AddMouseWheelEvent(wheel.X, wheel.Y);
    }

    public void Dispose()
    {
        _window.FocusChanged -= OnFocusChanged;

        ImGui.DestroyContext();

        _gl.DeleteProgram(_program);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ibo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteTexture(_fontTexture);
    }

    private struct GLState
    {
        public uint ActiveTexture;
        public uint Program;
        public uint Texture2D;
        public uint ArrayBuffer;
        public uint ElementArrayBuffer;
        public uint VertexArray;
        public bool Blend;
        public bool CullFace;
        public bool DepthTest;
        public bool StencilTest;
        public bool ScissorTest;
        public int BlendSrcRgb;
        public int BlendDstRgb;
        public int BlendSrcAlpha;
        public int BlendDstAlpha;
        public int BlendEquationRgb;
        public int BlendEquationAlpha;
        public int ViewportX;
        public int ViewportY;
        public int ViewportW;
        public int ViewportH;
        public int ScissorX;
        public int ScissorY;
        public int ScissorW;
        public int ScissorH;
        public int PolygonModeFront;
        public int UnpackAlignment;
    }

    private const string VertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec2 aPos;
        layout (location = 1) in vec2 aUV;
        layout (location = 2) in vec4 aColor;
        uniform mat4 uProj;
        out vec2 vUV;
        out vec4 vColor;
        void main()
        {
            vUV = aUV;
            vColor = aColor;
            gl_Position = uProj * vec4(aPos, 0.0, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core
        in vec2 vUV;
        in vec4 vColor;
        uniform sampler2D uTexture;
        out vec4 fragColor;
        void main()
        {
            fragColor = vColor * texture(uTexture, vUV);
        }
        """;
}
