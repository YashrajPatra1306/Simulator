using System;
using System.Windows.Forms;
using System.Drawing;

namespace SandboxEngine
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            var canvas = new SandboxCanvas();
            var form = new Form
            {
                Text = "Native Sandbox Engine (Rust+C#)",
                ClientSize = new Size(1024, 768),
                BackColor = Color.FromArgb(20, 20, 30), // Fixed: BackColor
                StartPosition = FormStartPosition.CenterScreen
            };
            
            form.Controls.Add(canvas);
            canvas.Dock = DockStyle.Fill;
            
            // Menu Setup
            var menu = new MenuStrip();
            var toolsMenu = new ToolStripMenuItem("Tools");
            var shapesMenu = new ToolStripMenuItem("Shapes");
            
            AddToolItem(toolsMenu, "Draw", ToolType.Draw, canvas, Keys.D);
            AddToolItem(toolsMenu, "Move", ToolType.Move, canvas, Keys.M);
            AddToolItem(toolsMenu, "Delete", ToolType.Delete, canvas, Keys.Delete);
            AddToolItem(toolsMenu, "Particles", ToolType.Particles, canvas, Keys.P);
            
            AddShapeItem(shapesMenu, "Circle", ShapeType.Circle, canvas, Keys.C);
            AddShapeItem(shapesMenu, "Rectangle", ShapeType.Rect, canvas, Keys.R);
            AddShapeItem(shapesMenu, "Triangle", ShapeType.Triangle, canvas, Keys.T);
            
            menu.Items.Add(toolsMenu);
            menu.Items.Add(shapesMenu);
            form.MainMenuStrip = menu;
            form.Controls.Add(menu);
            
            // About Box on Start
            ShowAboutDialog();
            
            Application.Run(form);
        }

        static void AddToolItem(ToolStripMenu menu, string text, ToolType type, SandboxCanvas canvas, Keys key) {
            var item = new ToolStripMenuItem(text);
            item.ShortcutKeys = key;
            item.Click += (s, e) => canvas.SetTool(type); // Fixed: Call public method
            menu.Items.Add(item);
        }

        static void AddShapeItem(ToolStripMenu menu, string text, ShapeType type, SandboxCanvas canvas, Keys key) {
            var item = new ToolStripMenuItem(text);
            item.ShortcutKeys = key;
            item.Click += (s, e) => canvas.SetShape(type); // Fixed: Call public method
            menu.Items.Add(item);
        }

        static void ShowAboutDialog() {
            MessageBox.Show(
                "Native Sandbox Rendering Engine\n" + // Fixed: \n
                "Rust Physics Core + C# GDI Renderer\n\n" +
                "Controls:\n" +
                "D: Draw | M: Move | Del: Delete\n" +
                "P: Particles | Right Click: Color",
                "About",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }
    }

    public enum ToolType { Draw, Move, Delete, Particles, Force }
    public enum ShapeType { Circle, Rect, Triangle }
}
