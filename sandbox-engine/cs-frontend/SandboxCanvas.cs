using System;
using System.Drawing;
using System.Windows.Forms;

namespace SandboxEngine
{
    public class SandboxCanvas : Panel
    {
        private ToolController _toolController;
        private FpsCalculator _fpsCalc = new FpsCalculator();
        private bool _physicsInitialized = false;

        public SandboxCanvas() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);
            _toolController = new ToolController(this);
        }

        public void SetTool(ToolType t) => _toolController.CurrentTool = t;
        public void SetShape(ShapeType s) => _toolController.CurrentShape = s;
        public ToolType CurrentTool => _toolController.CurrentTool;

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            Initialize();
        }

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            if (Width > 0 && Height > 0) {
                // Only re-init physics grid, don't clear objects (handled in Rust resize_grid)
                // In a real impl, we'd add a specific resize_ffi call, but init_physics is safe enough for dev
                PhysicsInterop.init_physics(Width, Height); 
            }
        }

        public void Initialize() {
            if (_physicsInitialized) return;
            if (Width <= 0 || Height <= 0) return; // Guard against zero size
            
            PhysicsInterop.init_physics(Width, Height);
            _physicsInitialized = true;
            
            // Add default objects
            PhysicsInterop.add_object(200, 200, 0, 0xFF00FF00, 30);
            PhysicsInterop.add_object(400, 300, 1, 0xFFFF0000, 50);
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            _fpsCalc.Update();

            IntPtr hdc = e.Graphics.GetHdc();
            Renderer.ClearBackground(hdc, Width, Height);

            // Render Objects (Mock data fetch for simplicity - in real app, marshal array)
            // For this snippet, we assume the Rust side updates internal state and we just draw known test objects
            // A full implementation would have get_object_at(i) FFI calls.
            
            // Render Black Hole
            float bhX, bhY;
            PhysicsInterop.get_black_hole_position(out bhX, out bhY);
            float gaze = PhysicsInterop.get_black_hole_gaze();
            Renderer.RenderBlackHole(hdc, bhX, bhY, gaze, 50.0f);

            e.Graphics.ReleaseHdc(hdc);

            // Status Bar Info
            string status = $"FPS: {_fpsCalc.GetFps():0} | Objects: {PhysicsInterop.get_object_count()} | Particles: {PhysicsInterop.get_particle_active_count()} | Tool: {CurrentTool}";
            TextRenderer.DrawText(e.Graphics, status, SystemFonts.CaptionFont, new Point(10, 10), Color.White);
        }

        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            _toolController.HandleMouseDown(e.X, e.Y, e.Button);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            if (e.Button == MouseButtons.Left) {
                _toolController.HandleMouseDrag(e.X, e.Y);
                Invalidate();
            }
            // Update Black Hole Gaze
            PhysicsInterop.update_physics(0.016f, e.X, e.Y, Width, Height);
        }
        
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            _toolController.HandleMouseUp();
        }
    }

    public class FpsCalculator {
        private long[] _timestamps = new long[30];
        private int _currentIndex = 0;
        private int _count = 0;

        public void Update() {
            _timestamps[_currentIndex] = DateTime.Now.Ticks / 10000; // ms
            _currentIndex = (_currentIndex + 1) % 30;
            if (_count < 30) _count++;
        }

        public double GetFps() {
            if (_count < 2) return 0.0;
            // Circular buffer logic
            int oldestIdx = _currentIndex; 
            int newestIdx = (_currentIndex - 1 + 30) % 30;
            
            long elapsed = _timestamps[newestIdx] - _timestamps[oldestIdx];
            if (elapsed <= 0) return 60.0;
            
            return (_count - 1) * 1000.0 / elapsed;
        }
    }
}
