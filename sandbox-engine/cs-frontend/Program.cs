using System;
using System.Drawing;
using System.Windows.Forms;

namespace SandboxEngine
{
    public enum ToolType  { Draw, Move, Delete, Particles }
    public enum ShapeType { Circle, Rect, Triangle }

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
                Text        = "Sandbox Engine — Rust Physics + OpenGL",
                ClientSize  = new Size(1024, 768),
                BackColor   = Color.FromArgb(20, 20, 30),
                StartPosition = FormStartPosition.CenterScreen
            };

            form.Controls.Add(canvas);
            canvas.Dock = DockStyle.Fill;

            var menu       = new MenuStrip();
            var toolsMenu  = new ToolStripMenuItem("Tools");
            var shapesMenu = new ToolStripMenuItem("Shapes");
            var physMenu   = new ToolStripMenuItem("Physics");

            AddToolItem(toolsMenu,  "Draw",      ToolType.Draw,      canvas, Keys.D);
            AddToolItem(toolsMenu,  "Move",      ToolType.Move,      canvas, Keys.M);
            AddToolItem(toolsMenu,  "Delete",    ToolType.Delete,    canvas, Keys.Delete);
            AddToolItem(toolsMenu,  "Particles", ToolType.Particles, canvas, Keys.P);

            AddShapeItem(shapesMenu, "Circle",    ShapeType.Circle,   canvas, Keys.C);
            AddShapeItem(shapesMenu, "Rectangle", ShapeType.Rect,     canvas, Keys.R);
            AddShapeItem(shapesMenu, "Triangle",  ShapeType.Triangle, canvas, Keys.T);

            // Gravity toggles
            AddMenuItem(physMenu, "Normal Gravity",   Keys.G, () => PhysicsInterop.set_gravity(500.0f));
            AddMenuItem(physMenu, "Zero Gravity",     Keys.Z, () => PhysicsInterop.set_gravity(0.0f));
            AddMenuItem(physMenu, "Reverse Gravity",  Keys.V, () => PhysicsInterop.set_gravity(-500.0f));

            menu.Items.Add(toolsMenu);
            menu.Items.Add(shapesMenu);
            menu.Items.Add(physMenu);
            form.MainMenuStrip = menu;
            form.Controls.Add(menu);

            ShowAboutDialog();
            Application.Run(form);
        }

        static void AddToolItem(ToolStripMenuItem menu, string text, ToolType type, SandboxCanvas canvas, Keys key)
        {
            var item = new ToolStripMenuItem(text) { ShortcutKeys = key };
            item.Click += (s, e) => canvas.SetTool(type);
            menu.DropDownItems.Add(item);
        }

        static void AddShapeItem(ToolStripMenuItem menu, string text, ShapeType type, SandboxCanvas canvas, Keys key)
        {
            var item = new ToolStripMenuItem(text) { ShortcutKeys = key };
            item.Click += (s, e) => canvas.SetShape(type);
            menu.DropDownItems.Add(item);
        }

        static void AddMenuItem(ToolStripMenuItem menu, string text, Keys key, System.Action action)
        {
            var item = new ToolStripMenuItem(text) { ShortcutKeys = key };
            item.Click += (s, e) => action();
            menu.DropDownItems.Add(item);
        }

        static void ShowAboutDialog()
        {
            MessageBox.Show(
                "Sandbox Engine\n" +
                "Rust Physics Core (RwLock, BVH, Verlet, Sleeping)\n" +
                "OpenGL 4.3 Instanced Renderer + Gravitational Lensing\n\n" +
                "Controls:\n" +
                "D: Draw | M: Move | Del: Delete | P: Particles\n" +
                "C: Circle | R: Rect | T: Triangle\n" +
                "G: Normal Gravity | Z: Zero-G | V: Reverse\n" +
                "Mouse near center: Black Hole (hold 9s)",
                "About",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
