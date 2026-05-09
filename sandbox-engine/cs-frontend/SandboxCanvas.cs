using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SandboxEngine;

/// <summary>
/// FPS calculator using rolling 30-frame average for stable display.
/// </summary>
public class FpsCalculator
{
    private const int FrameCount = 30;
    private readonly long[] _timestamps = new long[FrameCount];
    private int _currentIndex = 0;
    private int _frameCount = 0;
    
    /// <summary>
    /// Record a frame timestamp and return current FPS.
    /// </summary>
    public float RecordFrame()
    {
        long now = DateTime.UtcNow.Ticks;
        _timestamps[_currentIndex] = now;
        _currentIndex = (_currentIndex + 1) % FrameCount;
        
        if (_frameCount < FrameCount)
            _frameCount++;
        
        // Calculate average FPS over available frames
        if (_frameCount < 2)
            return 0.0f;
        
        long oldest = _timestamps[_currentIndex]; // Next index to overwrite is oldest
        long newest = _timestamps[(_currentIndex + FrameCount - 1) % FrameCount];
        
        // Find actual oldest timestamp among recorded frames
        long minTime = long.MaxValue;
        long maxTime = long.MinValue;
        for (int i = 0; i < _frameCount; i++)
        {
            if (_timestamps[i] < minTime) minTime = _timestamps[i];
            if (_timestamps[i] > maxTime) maxTime = _timestamps[i];
        }
        
        long elapsedTicks = maxTime - minTime;
        if (elapsedTicks <= 0)
            return 60.0f;
        
        double elapsedSeconds = elapsedTicks / (double)TimeSpan.TicksPerSecond;
        float fps = (_frameCount - 1) / (float)elapsedSeconds;
        
        return Math.Min(fps, 999.0f); // Cap at 999 for display
    }
    
    /// <summary>
    /// Reset the calculator.
    /// </summary>
    public void Reset()
    {
        _currentIndex = 0;
        _frameCount = 0;
        Array.Clear(_timestamps, 0, _timestamps.Length);
    }
}

/// <summary>
/// Main canvas control for rendering the sandbox.
/// Uses double-buffered Windows Forms for smooth rendering.
/// </summary>
public class SandboxCanvas : Panel
{
    private readonly ToolController _toolController;
    private readonly BlackHoleController _blackHole;
    private readonly FpsCalculator _fpsCalc;
    private Timer _renderTimer;
    private bool _physicsInitialized;
    private Point _lastMousePos;
    private bool _isMouseDown;
    
    public SandboxCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | 
                 ControlStyles.UserPaint | 
                 ControlStyles.DoubleBuffer | 
                 ControlStyles.OptimizedDoubleBuffer, true);
        
        _toolController = new ToolController();
        _blackHole = new BlackHoleController();
        _fpsCalc = new FpsCalculator();
        _renderTimer = new Timer();
        _physicsInitialized = false;
        
        _renderTimer.Tick += OnRenderTick;
        _renderTimer.Interval = _toolController.GetTimerInterval();
        
        MouseDown += OnMouseDown;
        MouseUp += OnMouseUp;
        MouseMove += OnMouseMove;
        Resize += OnResize;
    }
    
    /// <summary>
    /// Initialize the physics system.
    /// Call after the control is fully sized.
    /// </summary>
    public void Initialize()
    {
        if (_physicsInitialized) return;
        
        PhysicsInterop.init_physics(Width, Height);
        _physicsInitialized = true;
        
        // Start render loop
        _renderTimer.Start();
    }
    
    /// <summary>
    /// Set the current tool.
    /// </summary>
    public void SetTool(ToolType tool)
    {
        _toolController.CurrentTool = tool;
    }
    
    /// <summary>
    /// Set the current shape.
    /// </summary>
    public void SetShape(ShapeType shape)
    {
        _toolController.CurrentShape = shape;
    }
    
    /// <summary>
    /// Set the current color.
    /// </summary>
    public void SetColor(Color color)
    {
        _toolController.CurrentColor = color;
    }
    
    /// <summary>
    /// Set the current size.
    /// </summary>
    public void SetSize(float size)
    {
        _toolController.CurrentSize = size;
    }
    
    /// <summary>
    /// Toggle low power mode.
    /// </summary>
    public void ToggleLowPowerMode()
    {
        _toolController.LowPowerMode = !_toolController.LowPowerMode;
        _renderTimer.Interval = _toolController.GetTimerInterval();
    }
    
    /// <summary>
    /// Save scene to file.
    /// Format: "%d %f %f %f %f %f %f %f %lu %f %f\n"
    /// (shape x y vx vy radius width height color mass restitution)
    /// </summary>
    public void SaveScene(string filePath)
    {
        try
        {
            using var writer = new StreamWriter(filePath);
            int maxObjects = PhysicsInterop.get_max_objects();
            
            for (int i = 0; i < maxObjects; i++)
            {
                if (PhysicsInterop.get_object_at(i, out var obj) && obj.active)
                {
                    // Format: shape x y vx vy radius width height color mass restitution
                    writer.WriteLine($"{obj.shape} {obj.x} {obj.y} {obj.vx} {obj.vy} {obj.radius} {obj.width} {obj.height} {obj.color} {obj.mass} {obj.restitution}");
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save scene: {ex.Message}", "Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    
    /// <summary>
    /// Load scene from file.
    /// </summary>
    public void LoadScene(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                MessageBox.Show("File not found.", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            
            using var reader = new StreamReader(filePath);
            string? line;
            
            while ((line = reader.ReadLine()) != null)
            {
                string[] parts = line.Split(' ');
                if (parts.Length >= 11)
                {
                    int shape = int.Parse(parts[0]);
                    float x = float.Parse(parts[1]);
                    float y = float.Parse(parts[2]);
                    float vx = float.Parse(parts[3]);
                    float vy = float.Parse(parts[4]);
                    float radius = float.Parse(parts[5]);
                    float width = float.Parse(parts[6]);
                    float height = float.Parse(parts[7]);
                    uint color = uint.Parse(parts[8]);
                    float mass = float.Parse(parts[9]);
                    float restitution = float.Parse(parts[10]);
                    
                    int idx = PhysicsInterop.add_object(x, y, shape, color, radius);
                    if (idx >= 0)
                    {
                        // Note: Additional properties would need setter functions in Rust
                        // For now, basic creation works
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load scene: {ex.Message}", "Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    
    private void OnRenderTick(object? sender, EventArgs e)
    {
        if (!_physicsInitialized) return;
        
        float dt = _toolController.GetDeltaTime();
        
        // Update black hole state
        _blackHole.Update(_lastMousePos.X, _lastMousePos.Y);
        
        // Update physics
        PhysicsInterop.update_physics(dt);
        
        // Handle dragging
        if (_toolController.CurrentTool == ToolType.Move && _isMouseDown)
        {
            int dragIdx = _toolController.GetDraggedObjectIndex();
            if (dragIdx >= 0 && PhysicsInterop.get_object_at(dragIdx, out var obj) && obj.active)
            {
                // Directly set position - would need Rust API enhancement for proper implementation
            }
        }
        
        // Invalidate to trigger paint
        Invalidate();
    }
    
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        
        if (!_physicsInitialized)
        {
            // Draw initialization message
            string msg = "Initializing...";
            TextRenderer.DrawText(e.Graphics, msg, Font, ClientRectangle, Color.White, 
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        
        IntPtr hdc = e.Graphics.GetHdc();
        
        try
        {
            // Clear background
            Rectangle rect = ClientRectangle;
            using var clearBrush = new SolidBrush(Color.FromArgb(20, 20, 30));
            e.Graphics.FillRectangle(clearBrush, rect);
            
            // Render all objects
            int maxObjects = PhysicsInterop.get_max_objects();
            for (int i = 0; i < maxObjects; i++)
            {
                if (PhysicsInterop.get_object_at(i, out var obj))
                {
                    Renderer.RenderObject(hdc, obj);
                }
            }
            
            // Render all particles
            int maxParticles = PhysicsInterop.get_max_particles();
            for (int i = 0; i < maxParticles; i++)
            {
                if (PhysicsInterop.get_particle_at(i, out var particle))
                {
                    Renderer.RenderParticle(hdc, particle);
                }
            }
            
            // Render black hole (if active)
            _blackHole.Render(hdc);
        }
        finally
        {
            e.Graphics.ReleaseHdc(hdc);
        }
    }
    
    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _isMouseDown = true;
            _toolController.OnMouseDown(e, Width, Height, out _);
        }
        else if (e.Button == MouseButtons.Right)
        {
            // Show color picker
            using var dialog = new ColorDialog();
            dialog.Color = _toolController.CurrentColor;
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _toolController.CurrentColor = dialog.Color;
            }
        }
    }
    
    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        _isMouseDown = false;
        _toolController.OnMouseUp();
    }
    
    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        _lastMousePos = e.Location;
        _toolController.OnMouseMove(e);
    }
    
    private void OnResize(object? sender, EventArgs e)
    {
        if (_physicsInitialized && Width > 0 && Height > 0)
        {
            // Reinitialize physics with new dimensions
            PhysicsInterop.init_physics(Width, Height);
        }
    }
    
    /// <summary>
    /// Get status information for display.
    /// </summary>
    public string GetStatusInfo()
    {
        float fps = _fpsCalc.RecordFrame();
        int objCount = PhysicsInterop.get_object_count();
        int particleCount = PhysicsInterop.get_particle_active_count();
        string toolName = _toolController.GetToolName();
        
        return $"FPS: {fps:F0} | Objects: {objCount} | Particles: {particleCount} | Tool: {toolName}";
    }
    
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderTimer?.Dispose();
            Renderer.CleanupGdiCache();
        }
        base.Dispose(disposing);
    }
}
