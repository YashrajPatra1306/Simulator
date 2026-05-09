using System;
using System.Drawing;
using System.Windows.Forms;

namespace SandboxEngine;

/// <summary>
/// Main application entry point.
/// Creates the main window with menu, toolbar, canvas, and status bar.
/// </summary>
public partial class MainForm : Form
{
    private SandboxCanvas _canvas = null!;
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private Timer _statusUpdateTimer = null!;
    
    public MainForm()
    {
        InitializeComponents();
    }
    
    private void InitializeComponents()
    {
        // Main form settings
        Text = "Sandbox Engine - Rust + C# Native";
        Size = new Size(1024, 768);
        StartPosition = FormStartPosition.CenterScreen;
        
        // Create menu strip
        var menuStrip = new MenuStrip();
        
        // File menu
        var fileMenu = new ToolStripMenuItem("File");
        var newSceneItem = new ToolStripMenuItem("New Scene", null, (s, e) => NewScene());
        var openSceneItem = new ToolStripMenuItem("Open Scene...", null, (s, e) => OpenScene());
        var saveSceneItem = new ToolStripMenuItem("Save Scene...", null, (s, e) => SaveScene());
        var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => Close());
        
        fileMenu.DropDownItems.AddRange(new[] { newSceneItem, openSceneItem, saveSceneItem, 
            new ToolStripSeparator(), exitItem });
        menuStrip.Items.Add(fileMenu);
        
        // Tools menu
        var toolsMenu = new ToolStripMenuItem("Tools");
        var drawItem = new ToolStripMenuItem("Draw (D)", null, (s, e) => SetTool(ToolType.Draw));
        var moveItem = new ToolStripMenuItem("Move (M)", null, (s, e) => SetTool(ToolType.Move));
        var deleteItem = new ToolStripMenuItem("Delete (X)", null, (s, e) => SetTool(ToolType.Delete));
        var particlesItem = new ToolStripMenuItem("Particles (P)", null, (s, e) => SetTool(ToolType.Particles));
        var forceItem = new ToolStripMenuItem("Force Blast (F)", null, (s, e) => SetTool(ToolType.Force));
        
        toolsMenu.DropDownItems.AddRange(new[] { drawItem, moveItem, deleteItem, 
            new ToolStripSeparator(), particlesItem, forceItem });
        menuStrip.Items.Add(toolsMenu);
        
        // Shapes menu
        var shapesMenu = new ToolStripMenuItem("Shapes");
        var circleItem = new ToolStripMenuItem("Circle (1)", null, (s, e) => SetShape(ShapeType.Circle));
        var rectItem = new ToolStripMenuItem("Rectangle (2)", null, (s, e) => SetShape(ShapeType.Rect));
        var triangleItem = new ToolStripMenuItem("Triangle (3)", null, (s, e) => SetShape(ShapeType.Triangle));
        var lineItem = new ToolStripMenuItem("Line (4)", null, (s, e) => SetShape(ShapeType.Line));
        
        shapesMenu.DropDownItems.AddRange(new[] { circleItem, rectItem, triangleItem, lineItem });
        menuStrip.Items.Add(shapesMenu);
        
        // Options menu
        var optionsMenu = new ToolStripMenuItem("Options");
        var lowPowerItem = new ToolStripMenuItem("Low Power Mode", null, (s, e) => ToggleLowPowerMode());
        lowPowerItem.CheckOnClick = true;
        optionsMenu.DropDownItems.Add(lowPowerItem);
        menuStrip.Items.Add(optionsMenu);
        
        // Help menu
        var helpMenu = new ToolStripMenuItem("Help");
        var aboutItem = new ToolStripMenuItem("About", null, (s, e) => ShowAbout());
        helpMenu.DropDownItems.Add(aboutItem);
        menuStrip.Items.Add(helpMenu);
        
        // Create toolbar
        var toolStrip = new ToolStrip();
        toolStrip.Items.Add(new ToolStripButton("Draw", null, (s, e) => SetTool(ToolType.Draw)));
        toolStrip.Items.Add(new ToolStripButton("Move", null, (s, e) => SetTool(ToolType.Move)));
        toolStrip.Items.Add(new ToolStripButton("Delete", null, (s, e) => SetTool(ToolType.Delete)));
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(new ToolStripButton("Circle", null, (s, e) => SetShape(ShapeType.Circle)));
        toolStrip.Items.Add(new ToolStripButton("Rect", null, (s, e) => SetShape(ShapeType.Rect)));
        toolStrip.Items.Add(new ToolStripButton("Triangle", null, (s, e) => SetShape(ShapeType.Triangle)));
        toolStrip.Items.Add(new ToolStripButton("Line", null, (s, e) => SetShape(ShapeType.Line)));
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(new ToolStripButton("Particles", null, (s, e) => SetTool(ToolType.Particles)));
        toolStrip.Items.Add(new ToolStripButton("Force", null, (s, e) => SetTool(ToolType.Force)));
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(new ToolStripButton("Low Power", null, (s, e) => ToggleLowPowerMode()));
        
        // Create canvas
        _canvas = new SandboxCanvas();
        _canvas.Dock = DockStyle.Fill;
        
        // Create status strip
        _statusStrip = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel("Ready");
        _statusStrip.Items.Add(_statusLabel);
        
        // Status update timer
        _statusUpdateTimer = new Timer();
        _statusUpdateTimer.Interval = 100; // Update every 100ms
        _statusUpdateTimer.Tick += (s, e) => {
            if (_canvas != null)
                _statusLabel.Text = _canvas.GetStatusInfo();
        };
        
        // Add controls
        Controls.Add(_canvas);
        Controls.Add(toolStrip);
        Controls.Add(_statusStrip);
        MainMenuStrip = menuStrip;
        MenuStrip = menuStrip;
        
        // Handle form load
        Load += (s, e) => {
            _canvas.Initialize();
            _statusUpdateTimer.Start();
        };
        
        // Handle key presses for shortcuts
        KeyPreview = true;
        KeyDown += (s, e) => {
            switch (e.KeyCode)
            {
                case Keys.D: SetTool(ToolType.Draw); break;
                case Keys.M: SetTool(ToolType.Move); break;
                case Keys.X: SetTool(ToolType.Delete); break;
                case Keys.P: SetTool(ToolType.Particles); break;
                case Keys.F: SetTool(ToolType.Force); break;
                case Keys.D1: SetShape(ShapeType.Circle); break;
                case Keys.D2: SetShape(ShapeType.Rect); break;
                case Keys.D3: SetShape(ShapeType.Triangle); break;
                case Keys.D4: SetShape(ShapeType.Line); break;
            }
        };
    }
    
    private void NewScene()
    {
        var result = MessageBox.Show("Clear all objects?", "New Scene", 
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result == DialogResult.Yes)
        {
            // Reinitialize physics
            _canvas.Initialize();
        }
    }
    
    private void OpenScene()
    {
        using var dialog = new OpenFileDialog();
        dialog.Filter = "Scene Files (*.txt)|*.txt|All Files (*.*)|*.*";
        dialog.Title = "Open Scene";
        
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _canvas.LoadScene(dialog.FileName);
        }
    }
    
    private void SaveScene()
    {
        using var dialog = new SaveFileDialog();
        dialog.Filter = "Scene Files (*.txt)|*.txt|All Files (*.*)|*.*";
        dialog.Title = "Save Scene";
        dialog.DefaultExt = "txt";
        
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _canvas.SaveScene(dialog.FileName);
        }
    }
    
    private void SetTool(ToolType tool)
    {
        _canvas.SetTool(tool);
    }
    
    private void SetShape(ShapeType shape)
    {
        _canvas.SetShape(shape);
    }
    
    private void ToggleLowPowerMode()
    {
        _canvas.ToggleLowPowerMode();
    }
    
    private void ShowAbout()
    {
        MessageBox.Show(
            "Sandbox Rendering Engine\n" +
            "Native Windows Application\n\n" +
            "Rust Physics Core + C# WPF Frontend\n" +
            "Pure Win32/GDI Rendering\n\n" +
            "Features:\n" +
            "- Spatial hashing for O(n) collision detection\n" +
            "- Particle system with free-list allocation\n" +
            "- Black hole simulation with gaze detection\n" +
            "- Hairy Ball Theorem visualization\n\n" +
            "Controls:\n" +
            "D/M/X/P/F - Tools\n" +
            "1/2/3/4 - Shapes\n" +
            "Right-click - Color picker",
            "About Sandbox Engine",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
    
    [STAThread]
    public static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault();
        Application.Run(new MainForm());
    }
}
