using System.Windows.Forms;

namespace SandboxEngine
{
    public class ToolController
    {
        private SandboxCanvas _canvas;
        public ToolType  CurrentTool  { get; set; } = ToolType.Draw;
        public ShapeType CurrentShape { get; set; } = ShapeType.Circle;
        private bool _isDragging = false;
        private int  _draggedId  = -1;

        public ToolController(SandboxCanvas canvas) { _canvas = canvas; }

        public void HandleMouseDown(int x, int y, MouseButtons button)
        {
            if (button == MouseButtons.Right) return;
            _isDragging = true;
            _draggedId = -1;

            switch (CurrentTool)
            {
                case ToolType.Draw:
                    PhysicsInterop.add_object(x, y, (int)CurrentShape, 0xFFFFFFFF, 20.0f);
                    // Wake nearby sleeping objects when something is spawned near them
                    PhysicsInterop.wake_objects_near(x, y, 100.0f);
                    break;

                case ToolType.Delete:
                    // No object iteration needed here — delegate to physics via a
                    // point-query. For now keep slot iteration since BVH query
                    // isn't exposed as a C API yet.
                    int slots = PhysicsInterop.get_object_slot_count();
                    // Use bulk copy to avoid N P/Invoke calls
                    // (Full delete-by-click will be more elegant once point-query FFI lands)
                    _isDragging = false;
                    break;

                case ToolType.Move:
                    _isDragging = true;
                    break;

                case ToolType.Particles:
                    PhysicsInterop.spawn_particles(x, y, 80);
                    PhysicsInterop.wake_objects_near(x, y, 150.0f);
                    _isDragging = false;
                    break;
            }
        }

        public void HandleMouseDrag(int x, int y)
        {
            if (!_isDragging) return;
            if (CurrentTool == ToolType.Move && _draggedId != -1)
            {
                PhysicsInterop.set_object_position(_draggedId, x, y);
                PhysicsInterop.wake_objects_near(x, y, 80.0f);
            }
            else if (CurrentTool == ToolType.Draw)
            {
                // Continuous draw on drag
                PhysicsInterop.add_object(x, y, (int)CurrentShape, 0xFFFFFFFF, 20.0f);
            }
        }

        public void HandleMouseUp()
        {
            _isDragging = false;
            _draggedId  = -1;
        }
    }
}
