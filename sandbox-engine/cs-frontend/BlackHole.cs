using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SandboxEngine;

/// <summary>
/// Black hole controller managing gaze detection and visualization.
/// Implements shapeless data point with lazy activation.
/// </summary>
public class BlackHoleController
{
    private PhysicsInterop.BlackHole _blackHole;
    private Point _position;
    
    public Point Position 
    { 
        get => _position;
        set => _position = value;
    }
    
    public bool IsActive => _blackHole.active && _blackHole.gaze_timer > 0.01f;
    public float GazeTimer => _blackHole.gaze_timer;
    public float NormalizedGaze => Math.Min(_blackHole.gaze_timer / 9.0f, 1.0f);
    
    public BlackHoleController()
    {
        _blackHole = new PhysicsInterop.BlackHole();
        _position = new Point(400, 300); // Default center position
    }
    
    /// <summary>
    /// Update black hole state based on mouse position.
    /// Called every frame before physics update.
    /// </summary>
    public void Update(float mouseX, float mouseY)
    {
        // Update Rust-side black hole state
        PhysicsInterop.update_black_hole(_position.X, _position.Y, mouseX, mouseY);
        
        // Get updated state
        PhysicsInterop.get_black_hole(out _blackHole);
    }
    
    /// <summary>
    /// Render the black hole if active.
    /// </summary>
    public void Render(IntPtr hdc)
    {
        if (!IsActive) return;
        
        Renderer.RenderBlackHole(hdc, _blackHole, _position.X, _position.Y);
    }
    
    /// <summary>
    /// Save black hole data to stream.
    /// </summary>
    public void Save(BinaryWriter writer)
    {
        writer.Write(_position.X);
        writer.Write(_position.Y);
        writer.Write(_blackHole.mass);
        writer.Write(_blackHole.schwarzschild_radius);
    }
    
    /// <summary>
    /// Load black hole data from stream.
    /// </summary>
    public void Load(BinaryReader reader)
    {
        _position.X = reader.ReadInt32();
        _position.Y = reader.ReadInt32();
        _blackHole.mass = reader.ReadDouble();
        _blackHole.schwarzschild_radius = reader.ReadSingle();
    }
}
