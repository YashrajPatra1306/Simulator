using System;
using System.Windows.Forms;

namespace SandboxEngine
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var form = new Form
            {
                Text = "Sandbox Rendering Engine - Native Windows Application",
                Width = 1024,
                Height = 768,
                StartPosition = FormStartPosition.CenterScreen,
                BackgroundColor = System.Drawing.Color.FromArgb(20, 20, 30)
            };

            var canvas = new SandboxCanvas
            {
                Dock = DockStyle.Fill
            };

            form.Controls.Add(canvas);
            form.Load += (s, e) => canvas.Initialize();

            // Menu strip
            var menu = new MenuStrip();
            
            var fileMenu = new ToolStripMenuItem("File");
            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => form.Close();
            fileMenu.DropDownItems.Add(exitItem);
            
            var toolsMenu = new ToolStripMenuItem("Tools");
            
            var drawItem = new ToolStripMenuItem("Draw (D)");
            drawItem.Click += (s, e) => SetTool(canvas, ToolType.Draw);
            
            var moveItem = new ToolStripMenuItem("Move (M)");
            moveItem.Click += (s, e) => SetTool(canvas, ToolType.Move);
            
            var deleteItem = new ToolStripMenuItem("Delete (X)");
            deleteItem.Click += (s, e) => SetTool(canvas, ToolType.Delete);
            
            var particlesItem = new ToolStripMenuItem("Particles (P)");
            particlesItem.Click += (s, e) => SetTool(canvas, ToolType.Particles);
            
            var forceItem = new ToolStripMenuItem("Force Blast (F)");
            forceItem.Click += (s, e) => SetTool(canvas, ToolType.Force);
            
            toolsMenu.DropDownItems.AddRange(new[] { drawItem, moveItem, deleteItem, particlesItem, forceItem });
            
            var shapesMenu = new ToolStripMenuItem("Shapes");
            
            var circleItem = new ToolStripMenuItem("Circle (1)");
            circleItem.Click += (s, e) => SetShape(canvas, ShapeType.Circle);
            
            var rectItem = new ToolStripMenuItem("Rectangle (2)");
            rectItem.Click += (s, e) => SetShape(canvas, ShapeType.Rect);
            
            var triItem = new ToolStripMenuItem("Triangle (3)");
            triItem.Click += (s, e) => SetShape(canvas, ShapeType.Triangle);
            
            var lineItem = new ToolStripMenuItem("Line (4)");
            lineItem.Click += (s, e) => SetShape(canvas, ShapeType.Line);
            
            shapesMenu.DropDownItems.AddRange(new[] { circleItem, rectItem, triItem, lineItem });
            
            menu.Items.AddRange(new ToolStripItem[] { fileMenu, toolsMenu, shapesMenu });
            form.MainMenuStrip = menu;
            form.Controls.Add(menu);

            // Keyboard shortcuts
            form.KeyPreview = true;
            form.KeyDown += (s, e) =>
            {
                switch (e.KeyCode)
                {
                    case Keys.D: SetTool(canvas, ToolType.Draw); break;
                    case Keys.M: SetTool(canvas, ToolType.Move); break;
                    case Keys.X: SetTool(canvas, ToolType.Delete); break;
                    case Keys.P: SetTool(canvas, ToolType.Particles); break;
                    case Keys.F: SetTool(canvas, ToolType.Force); break;
                    case Keys.D1: SetShape(canvas, ShapeType.Circle); break;
                    case Keys.D2: SetShape(canvas, ShapeType.Rect); break;
                    case Keys.D3: SetShape(canvas, ShapeType.Triangle); break;
                    case Keys.D4: SetShape(canvas, ShapeType.Line); break;
                }
            };

            // About dialog on right-click
            form.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    MessageBox.Show(
                        "Sandbox Rendering Engine\n" +
                        "Native Windows Application\n\n" +
                        "Rust Physics Core + C# Windows Forms\n" +
                        "Pure Win32 GDI Rendering\n\n" +
                        "Left Click: Use current tool\n" +
                        "Right Click: Show this help\n\n" +
                        "Keys: D=Draw, M=Move, X=Delete, P=Particles, F=Force\n" +
                        "1-4: Change shape",
                        "About",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            };

            Application.Run(form);
        }

        static void SetTool(SandboxCanvas canvas, ToolType tool)
        {
            // Would need to expose tool setter via reflection or interface
            // Simplified for now
        }

        static void SetShape(SandboxCanvas canvas, ShapeType shape)
        {
            // Would need to expose shape setter via reflection or interface
            // Simplified for now
        }
    }
}
