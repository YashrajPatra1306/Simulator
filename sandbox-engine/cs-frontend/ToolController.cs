using System;
using System.Drawing;
using System.Windows.Forms;

namespace SandboxEngine
{
    public enum ToolType { Draw, Move, Delete, Particles, Force }
    public enum ShapeType { Circle, Rect, Triangle, Line }

    public class ToolController
    {
        public ToolType CurrentTool { get; set; } = ToolType.Draw;
        public ShapeType CurrentShape { get; set; } = ShapeType.Circle;
        public uint CurrentColor { get; set; } = 0xFFAA5500;
        public float CurrentSize { get; set; } = 15.0f;
        public bool IsDragging { get; private set; }
        public int DraggedIndex { get; private set; } = -1;

        private readonly Control _canvas;
        private readonly Action<float, float, int> _onSpawnParticles;
        private readonly Func<float, float, int, uint, float, int> _onAddObject;
        private readonly Action<int> _onRemoveObject;
        private readonly Func<int> _getMaxObjects;

        public ToolController(
            Control canvas,
            Action<float, float, int> onSpawnParticles,
            Func<float, float, int, uint, float, int> onAddObject,
            Action<int> onRemoveObject,
            Func<int> getMaxObjects)
        {
            _canvas = canvas;
            _onSpawnParticles = onSpawnParticles;
            _onAddObject = onAddObject;
            _onRemoveObject = onRemoveObject;
            _getMaxObjects = getMaxObjects;
        }

        public void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            float x = e.X, y = e.Y;

            switch (CurrentTool)
            {
                case ToolType.Draw:
                    _onAddObject(x, y, (int)CurrentShape, CurrentColor, CurrentSize);
                    break;

                case ToolType.Move:
                    IsDragging = true;
                    // Find object at position (simplified - would need physics query)
                    DraggedIndex = -1;
                    break;

                case ToolType.Delete:
                    int maxObjects = _getMaxObjects();
                    for (int i = 0; i < maxObjects; i++)
                    {
                        GameObject obj;
                        if (PhysicsInterop.get_object_at(i, out obj) && obj.active)
                        {
                            float dx = obj.x - x;
                            float dy = obj.y - y;
                            if (dx * dx + dy * dy < obj.radius * obj.radius)
                            {
                                _onRemoveObject(i);
                                break;
                            }
                        }
                    }
                    break;

                case ToolType.Particles:
                    _onSpawnParticles(x, y, 20);
                    break;

                case ToolType.Force:
                    // Radial impulse blast - handled in physics layer
                    break;
            }
        }

        public void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!IsDragging || DraggedIndex < 0) return;

            float x = e.X, y = e.Y;
            // Would update object position via physics interop
        }

        public void OnMouseUp(object sender, MouseEventArgs e)
        {
            IsDragging = false;
            DraggedIndex = -1;
        }

        public string GetToolName()
        {
            return CurrentTool.ToString();
        }
    }
}
