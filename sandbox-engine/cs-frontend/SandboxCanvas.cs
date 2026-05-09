using System;
using System.Drawing;
using System.Windows.Forms;

namespace SandboxEngine
{
    public class FpsCalculator
    {
        private readonly long[] _timestamps = new long[30];
        private int _currentIndex;
        private int _frameCount;

        public void RecordFrame()
        {
            long now = DateTime.UtcNow.Ticks / 10000; // Convert to ms
            _timestamps[_currentIndex] = now;
            _currentIndex = (_currentIndex + 1) % _timestamps.Length;
            if (_frameCount < _timestamps.Length) _frameCount++;
        }

        public double GetFps()
        {
            if (_frameCount < 2) return 0.0;

            long minTime = long.MaxValue, maxTime = long.MinValue;
            for (int i = 0; i < _frameCount; i++)
            {
                if (_timestamps[i] < minTime) minTime = _timestamps[i];
                if (_timestamps[i] > maxTime) maxTime = _timestamps[i];
            }

            long elapsed = maxTime - minTime;
            if (elapsed <= 0) return 60.0;

            return (_frameCount - 1) * 1000.0 / elapsed;
        }
    }

    public class SandboxCanvas : Panel
    {
        private bool _physicsInitialized;
        private readonly Timer _renderTimer;
        private readonly FpsCalculator _fpsCalc;
        private readonly ToolController _toolController;
        private readonly BlackHoleController _blackHole;
        private readonly Label _statusLabel;
        private Point _lastMousePos;

        public SandboxCanvas()
        {
            DoubleBuffered = false; // We handle double buffering manually with GDI
            SetStyle(ControlStyles.Opaque, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);

            _fpsCalc = new FpsCalculator();
            _blackHole = new BlackHoleController();

            _toolController = new ToolController(
                this,
                SpawnParticles,
                AddObject,
                RemoveObject,
                () => PhysicsInterop.get_max_objects()
            );

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            Resize += OnResize;

            _renderTimer = new Timer { Interval = 16 };
            _renderTimer.Tick += OnRenderTick;

            _statusLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                BackColor = Color.FromArgb(30, 30, 40),
                ForeColor = Color.LightGray,
                Font = new Font("Consolas", 9f),
                Text = "FPS: 0 | Objects: 0 | Particles: 0 | Tool: Draw"
            };
            Controls.Add(_statusLabel);
        }

        public void Initialize()
        {
            if (_physicsInitialized) return;
            if (Width <= 0 || Height <= 0) return;

            PhysicsInterop.init_physics(Width, Height);
            _physicsInitialized = true;
            _renderTimer.Start();
        }

        private void OnRenderTick(object sender, EventArgs e)
        {
            float dt = 0.016f;
            PhysicsInterop.update_physics(dt);

            var bhPos = PhysicsInterop.get_black_hole_position();
            _blackHole.Update(bhPos.Item1, bhPos.Item2, _lastMousePos.X, _lastMousePos.Y, dt);

            Invalidate();
            UpdateStatusLabel();
        }

        private void OnMouseDown(object sender, MouseEventArgs e) => _toolController.OnMouseDown(sender, e);
        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            _lastMousePos = e.Location;
            _toolController.OnMouseMove(sender, e);
        }
        private void OnMouseUp(object sender, MouseEventArgs e) => _toolController.OnMouseUp(sender, e);

        private void OnResize(object sender, EventArgs e)
        {
            if (Width > 0 && Height > 0)
            {
                PhysicsInterop.init_physics(Width, Height);
            }
        }

        private void SpawnParticles(float x, float y, int count) => PhysicsInterop.spawn_particles(x, y, count);
        private int AddObject(float x, float y, int shape, uint color, float size) =>
            PhysicsInterop.add_object(x, y, shape, color, size);
        private void RemoveObject(int index) => PhysicsInterop.remove_object(index);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            IntPtr hdc = e.Graphics.GetHdc();
            try
            {
                Renderer.ClearBackground(hdc, Width, Height);

                int maxObjects = PhysicsInterop.get_max_objects();
                for (int i = 0; i < maxObjects; i++)
                {
                    GameObject obj;
                    if (PhysicsInterop.get_object_at(i, out obj))
                    {
                        Renderer.RenderObject(hdc, obj);
                    }
                }

                int maxParticles = 2000;
                for (int i = 0; i < maxParticles; i++)
                {
                    Particle p;
                    if (PhysicsInterop.get_particle_at(i, out p))
                    {
                        Renderer.RenderParticle(hdc, p);
                    }
                }

                _blackHole.Render(hdc);
            }
            finally
            {
                e.Graphics.ReleaseHdc(hdc);
            }
        }

        private void UpdateStatusLabel()
        {
            _fpsCalc.RecordFrame();
            double fps = _fpsCalc.GetFps();
            int objCount = PhysicsInterop.get_object_count();
            int partCount = PhysicsInterop.get_particle_active_count();
            string toolName = _toolController.GetToolName();

            _statusLabel.Text = $"FPS: {fps:F0} | Objects: {objCount} | Particles: {partCount} | Tool: {toolName}";
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _renderTimer.Stop();
            Renderer.CleanupGdiCache();
            base.OnHandleDestroyed(e);
        }
    }
}
