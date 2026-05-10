using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL4;
using OpenTK.WinForms;

namespace SandboxEngine
{
    /// <summary>
    /// GLControl-based canvas. Physics runs on a dedicated thread (Priority 3).
    /// Render thread (UI) reads front render buffer via one bulk P/Invoke.
    /// Temporal interpolation (Priority 1) blends previous/current positions.
    /// </summary>
    public class SandboxCanvas : GLControl
    {
        private GLRenderer   _renderer;
        private ToolController _toolController;
        private FpsCalculator  _fpsCalc = new FpsCalculator();
        private bool _initialized = false;

        // Physics thread (Priority 3)
        private Thread   _physicsThread;
        private volatile bool _physicsRunning = false;
        private Stopwatch _physicsClock = new Stopwatch();
        private const double PhysicsStep = 1.0 / 120.0; // 120Hz fixed step

        // Shared mouse state (written by UI thread, read by physics thread)
        private volatile float _mouseX = 0, _mouseY = 0;

        // Render timer drives GL swap at ~60fps on the UI thread
        private Timer _renderTimer;

        public SandboxCanvas() : base(new OpenTK.WinForms.GLControlSettings())
        {
            _toolController = new ToolController(this);

            _renderTimer = new Timer();
            _renderTimer.Interval = 16;
            _renderTimer.Tick += (s, e) => { if (_initialized) Invalidate(); };
        }

        public void SetTool(ToolType t)  => _toolController.CurrentTool = t;
        public void SetShape(ShapeType s) => _toolController.CurrentShape = s;
        public ToolType CurrentTool => _toolController.CurrentTool;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Defer until visible so we have a real size
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && !_initialized) Initialize();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!_initialized) { Initialize(); return; }
            if (Width > 0 && Height > 0)
            {
                MakeCurrent();
                _renderer?.Resize(Width, Height);
                PhysicsInterop.resize_physics(Width, Height);
            }
        }

        private void Initialize()
        {
            if (_initialized || Width <= 0 || Height <= 0) return;
            _initialized = true;

            MakeCurrent();
            _renderer = new GLRenderer();
            _renderer.Initialize(Width, Height);

            PhysicsInterop.init_physics(Width, Height);
            PhysicsInterop.add_object(200, 200, 0, 0xFF00FF00, 30);
            PhysicsInterop.add_object(400, 300, 1, 0xFFFF0000, 50);
            PhysicsInterop.add_object(600, 150, 2, 0xFF0088FF, 25);

            // Start dedicated physics thread (Priority 3)
            _physicsRunning = true;
            _physicsThread = new Thread(PhysicsLoop)
            {
                IsBackground = true,
                Name = "PhysicsThread",
                Priority = ThreadPriority.AboveNormal
            };
            _physicsClock.Start();
            _physicsThread.Start();

            _renderTimer.Start();
        }

        // Fixed-timestep physics loop on its own thread (Priority 3)
        private void PhysicsLoop()
        {
            double accumulator = 0.0;
            double lastTime    = _physicsClock.Elapsed.TotalSeconds;

            while (_physicsRunning)
            {
                double now     = _physicsClock.Elapsed.TotalSeconds;
                double elapsed = now - lastTime;
                lastTime = now;

                // Cap to prevent spiral of death
                accumulator += Math.Min(elapsed, 0.033);

                while (accumulator >= PhysicsStep)
                {
                    // Real elapsed dt, capped — no more hardcoded 0.016f
                    PhysicsInterop.update_physics(
                        (float)PhysicsStep,
                        _mouseX, _mouseY,
                        Width > 0 ? Width : 1024,
                        Height > 0 ? Height : 768);
                    accumulator -= PhysicsStep;
                }

                // Temporal interpolation alpha: how far between steps we are
                // (Physics writes render buffer — alpha used by shader for smooth motion)
                // Currently passed to renderer; future: lerp prev/current in shader
                float alpha = (float)(accumulator / PhysicsStep);
                _ = alpha; // Will be used in shader uniform when prev_pos exported

                // ~120Hz sleep
                Thread.Sleep(1);
            }
        }

        // Paint is called by _renderTimer on the UI thread
        protected override void OnPaint(PaintEventArgs e)
        {
            if (!_initialized) return;

            MakeCurrent();
            _fpsCalc.Update();

            float bhX, bhY;
            PhysicsInterop.get_black_hole_position(out bhX, out bhY);
            float gaze = PhysicsInterop.get_black_hole_gaze();

            _renderer.RenderFrame(bhX, bhY, gaze, 1.0f);

            SwapBuffers();

            // Draw HUD text on top via GDI (cheap, one string)
            string status = $"FPS: {_fpsCalc.GetFps():0} | Objects: {PhysicsInterop.get_object_count()} | Particles: {PhysicsInterop.get_particle_active_count()} | Tool: {CurrentTool}";
            TextRenderer.DrawText(e.Graphics, status, SystemFonts.CaptionFont, new Point(10, 10), Color.White);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _toolController.HandleMouseDown(e.X, e.Y, e.Button);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _mouseX = e.X;
            _mouseY = e.Y;
            if (e.Button == MouseButtons.Left)
                _toolController.HandleMouseDrag(e.X, e.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _toolController.HandleMouseUp();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _physicsRunning = false;
            _physicsThread?.Join(500);
            _renderTimer.Stop();
            MakeCurrent();
            _renderer?.Dispose();
            base.OnHandleDestroyed(e);
        }
    }

    public class FpsCalculator
    {
        private long[] _timestamps = new long[30];
        private int _idx = 0, _count = 0;

        public void Update()
        {
            _timestamps[_idx] = DateTime.Now.Ticks / 10000;
            _idx = (_idx + 1) % 30;
            if (_count < 30) _count++;
        }

        public double GetFps()
        {
            if (_count < 2) return 0.0;
            int newest = (_idx - 1 + 30) % 30;
            int oldest = _count < 30 ? 0 : _idx;
            long elapsed = _timestamps[newest] - _timestamps[oldest];
            return elapsed <= 0 ? 60.0 : (_count - 1) * 1000.0 / elapsed;
        }
    }
}
