using System;
using System.Windows.Forms;

namespace SandboxEngine
{
    public class ToolController
    {
        private SandboxCanvas _canvas;
        public ToolType CurrentTool { get; set; } = ToolType.Draw;
        public ShapeType CurrentShape { get; set; } = ShapeType.Circle;
        private bool _isDragging = false;
        private int _dragStartX, _dragStartY;

        public ToolController(SandboxCanvas canvas) {
            _canvas = canvas;
        }

        public void HandleMouseDown(int x, int y, MouseButtons button) {
            if (button == MouseButtons.Right) {
                // Color picker logic would go here
                return;
            }

            _isDragging = true;
            _dragStartX = x;
            _dragStartY = y;

            switch (CurrentTool) {
                case ToolType.Draw:
                    uint color = 0xFFFFFFFF; // Default white
                    float size = 20.0f;
                    int shapeCode = (int)CurrentShape;
                    PhysicsInterop.add_object(x, y, shapeCode, color, size);
                    break;
                case ToolType.Particles:
                    PhysicsInterop.spawn_particles(x, y, 50);
                    _isDragging = false; // Spawn once on click
                    break;
            }
        }

        public void HandleMouseDrag(int x, int y) {
            if (!_isDragging) return;

            if (CurrentTool == ToolType.Move) {
                // Move logic requires getting object under cursor, omitted for brevity
            }
        }

        public void HandleMouseUp() {
            _isDragging = false;
        }
    }
}
