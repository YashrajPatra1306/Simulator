using System;
using System.Drawing;
using System.Windows.Forms;

namespace SandboxEngine;

/// <summary>
/// Tool types available in the sandbox.
/// </summary>
public enum ToolType
{
    Draw = 0,
    Move = 1,
    Delete = 2,
    Particles = 3,
    Force = 4  // Radial impulse blast
}

/// <summary>
/// Manages tool state and input handling.
/// </summary>
public class ToolController
{
    public ToolType CurrentTool { get; set; } = ToolType.Draw;
    public ShapeType CurrentShape { get; set; } = ShapeType.Circle;
    public Color CurrentColor { get; set; } = Color.FromArgb(100, 150, 255);
    public float CurrentSize { get; set; } = 20.0f;
    public bool LowPowerMode { get; set; } = false;
    
    // Mouse state for move tool
    private int _dragObjectIndex = -1;
    private Point _lastMousePos;
    
    /// <summary>
    /// Get delta time based on power mode.
    /// </summary>
    public float GetDeltaTime()
    {
        return LowPowerMode ? 0.032f : 0.016f;
    }
    
    /// <summary>
    /// Get timer interval in milliseconds.
    /// </summary>
    public int GetTimerInterval()
    {
        return LowPowerMode ? 32 : 16;
    }
    
    /// <summary>
    /// Handle mouse down event.
    /// Returns true if event was handled.
    /// </summary>
    public bool OnMouseDown(MouseEventArgs e, int canvasWidth, int canvasHeight, 
        out int affectedObjectIndex)
    {
        affectedObjectIndex = -1;
        _lastMousePos = e.Location;
        
        switch (CurrentTool)
        {
            case ToolType.Draw:
                // Add object at mouse position
                uint colorRef = (uint)ColorTranslator.ToOle(CurrentColor);
                affectedObjectIndex = PhysicsInterop.add_object(
                    e.X, e.Y, 
                    (int)CurrentShape, 
                    colorRef, 
                    CurrentSize);
                return true;
                
            case ToolType.Move:
                // Find object under cursor (simple distance check)
                int maxObjects = PhysicsInterop.get_max_objects();
                for (int i = 0; i < maxObjects; i++)
                {
                    if (PhysicsInterop.get_object_at(i, out var obj) && obj.active)
                    {
                        float dx = e.X - obj.x;
                        float dy = e.Y - obj.y;
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (dist <= obj.radius)
                        {
                            _dragObjectIndex = i;
                            affectedObjectIndex = i;
                            return true;
                        }
                    }
                }
                return false;
                
            case ToolType.Delete:
                // Remove object under cursor
                for (int i = 0; i < maxObjects; i++)
                {
                    if (PhysicsInterop.get_object_at(i, out var obj) && obj.active)
                    {
                        float dx = e.X - obj.x;
                        float dy = e.Y - obj.y;
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (dist <= obj.radius)
                        {
                            PhysicsInterop.remove_object(i);
                            affectedObjectIndex = i;
                            return true;
                        }
                    }
                }
                return false;
                
            case ToolType.Particles:
                // Spawn particles at mouse position
                PhysicsInterop.spawn_particles(e.X, e.Y, 20);
                return true;
                
            case ToolType.Force:
                // Apply radial impulse (handled in physics update)
                return true;
                
            default:
                return false;
        }
    }
    
    /// <summary>
    /// Handle mouse move event.
    /// </summary>
    public void OnMouseMove(MouseEventArgs e)
    {
        if (CurrentTool == ToolType.Move && _dragObjectIndex >= 0)
        {
            // Object dragging is handled by directly modifying position in physics
            // For simplicity, we just track the last position
            _lastMousePos = e.Location;
        }
    }
    
    /// <summary>
    /// Handle mouse up event.
    /// </summary>
    public void OnMouseUp()
    {
        _dragObjectIndex = -1;
    }
    
    /// <summary>
    /// Get the index of object being dragged, or -1.
    /// </summary>
    public int GetDraggedObjectIndex() => _dragObjectIndex;
    
    /// <summary>
    /// Apply force blast from mouse position.
    /// Called during physics update when Force tool is active and mouse is pressed.
    /// </summary>
    public void ApplyForceBlast(float mouseX, float mouseY, float blastRadius = 150.0f, float forceMagnitude = 500.0f)
    {
        int maxObjects = PhysicsInterop.get_max_objects();
        for (int i = 0; i < maxObjects; i++)
        {
            if (PhysicsInterop.get_object_at(i, out var obj) && obj.active)
            {
                float dx = obj.x - mouseX;
                float dy = obj.y - mouseY;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                
                if (dist < blastRadius && dist > 1.0f)
                {
                    // Calculate impulse direction
                    float nx = dx / dist;
                    float ny = dy / dist;
                    
                    // Apply velocity change (simplified - would need Rust-side API for full implementation)
                    // For now, this is a placeholder for future enhancement
                }
            }
        }
    }
    
    /// <summary>
    /// Get display name for current tool.
    /// </summary>
    public string GetToolName()
    {
        return CurrentTool switch
        {
            ToolType.Draw => "Draw",
            ToolType.Move => "Move",
            ToolType.Delete => "Delete",
            ToolType.Particles => "Particles",
            ToolType.Force => "Force Blast",
            _ => "Unknown"
        };
    }
    
    /// <summary>
    /// Get display name for current shape.
    /// </summary>
    public string GetShapeName()
    {
        return CurrentShape switch
        {
            ShapeType.Circle => "Circle",
            ShapeType.Rect => "Rectangle",
            ShapeType.Triangle => "Triangle",
            ShapeType.Line => "Line",
            _ => "Unknown"
        };
    }
}
