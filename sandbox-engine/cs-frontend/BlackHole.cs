using System;
using System.Drawing;
using System.Windows.Forms;

namespace SandboxEngine
{
    public class BlackHoleController
    {
        private Point _position;
        private float _gazeTimer;

        public Point Position => _position;
        public float GazeTimer => _gazeTimer;
        public bool IsActive => _gazeTimer > 0.01f;
        public float SchwarzschildRadius { get; set; } = 50.0f;

        public void Update(float x, float y, float mouseX, float mouseY, float dt)
        {
            _position = new Point((int)x, (int)y);
            PhysicsInterop.update_black_hole(x, y, mouseX, mouseY, dt);
            _gazeTimer = PhysicsInterop.get_black_hole_gaze_timer();
        }

        public void Render(IntPtr hdc)
        {
            Renderer.RenderBlackHole(hdc, _position.X, _position.Y, _gazeTimer, SchwarzschildRadius);
        }
    }
}
