using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace SandboxEngine
{
    public static class Renderer
    {
        private static Dictionary<uint, (IntPtr brush, IntPtr pen)> _cache = new();
        private const int CACHE_SIZE = 256;
        private const int NULL_BRUSH = 5;
        private const int WHITE_BRUSH = 0;

        [DllImport("user32.dll")]
        private static extern IntPtr FillRect(IntPtr hdc, ref RECT lprc, IntPtr hbr);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(uint crColor);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreatePen(int fnPenStyle, int nWidth, uint crColor);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern IntPtr GetStockObject(int fnObject);

        [DllImport("gdi32.dll")]
        private static extern bool Ellipse(IntPtr hdc, int left, int top, int right, int bottom);

        [DllImport("gdi32.dll")]
        private static extern bool Rectangle(IntPtr hdc, int left, int top, int right, int bottom);

        [DllImport("gdi32.dll")]
        private static extern bool Polygon(IntPtr hdc, POINT[] lpPoints, int nCount);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; public RECT(int l, int t, int r, int b) { Left=l; Top=t; Right=r; Bottom=b; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; public POINT(int x, int y) { X=x; Y=y; } }

        public struct GameObject {
            public float x, y, vx, vy, radius, width, height, mass, restitution;
            public int shape; public uint color; public bool active;
        }

        public struct Particle {
            public float x, y, vx, vy, life, max_life, size; public uint color; public bool active;
        }

        public static void ClearBackground(IntPtr hdc, int w, int h) {
            IntPtr brush = CreateSolidBrush(0xFF141414); // Dark background
            RECT rect = new RECT(0, 0, w, h);
            FillRect(hdc, ref rect, brush);
            DeleteObject(brush);
        }

        public static (IntPtr brush, IntPtr pen) GetCachedGDI(uint color) {
            if (_cache.TryGetValue(color, out var handle)) return handle;

            IntPtr hBrush = CreateSolidBrush(color);
            IntPtr hPen = CreatePen(0, 1, color);

            if (_cache.Count >= CACHE_SIZE) {
                // Simple leak-safe fallback if cache full
                return (hBrush, hPen);
            }

            _cache[color] = (hBrush, hPen);
            return (hBrush, hPen);
        }

        public static void RenderObject(IntPtr hdc, GameObject obj) {
            var (hBrush, hPen) = GetCachedGDI(obj.color);
            IntPtr oldBrush = SelectObject(hdc, hBrush);
            IntPtr oldPen = SelectObject(hdc, hPen);

            int x = (int)obj.x, y = (int)obj.y, r = (int)obj.radius;
            int w = (int)obj.width, h = (int)obj.height;

            switch (obj.shape) {
                case 0: // Circle
                    Ellipse(hdc, x - r, y - r, x + r, y + r);
                    break;
                case 1: // Rect
                    Rectangle(hdc, x - w/2, y - h/2, x + w/2, y + h/2);
                    break;
                case 2: // Triangle
                    POINT[] pts = new POINT[] {
                        new POINT(x, y - r),
                        new POINT(x - r, y + r),
                        new POINT(x + r, y + r)
                    };
                    Polygon(hdc, pts, 3);
                    break;
            }

            SelectObject(hdc, oldBrush);
            SelectObject(hdc, oldPen);
        }

        public static void RenderParticle(IntPtr hdc, Particle p) {
            var (hBrush, hPen) = GetCachedGDI(p.color);
            IntPtr oldBrush = SelectObject(hdc, hBrush);
            IntPtr oldPen = SelectObject(hdc, hPen); // Fixed: was selecting brush twice

            int s = (int)p.size;
            Ellipse(hdc, (int)p.x - s, (int)p.y - s, (int)p.x + s, (int)p.y + s);

            SelectObject(hdc, oldBrush);
            SelectObject(hdc, oldPen);
        }

        public static void RenderBlackHole(IntPtr hdc, float x, float y, float gazeT, float radius) {
            if (gazeT < 0.01f) return;

            // Event Horizon (Black)
            IntPtr hBlack = CreateSolidBrush(0x000000);
            IntPtr oldBrush = SelectObject(hdc, hBlack);
            IntPtr oldPen = SelectObject(hdc, GetStockObject(NULL_BRUSH)); // No outline
            
            int r = (int)(radius * (1.0f + gazeT * 2.0f));
            Ellipse(hdc, (int)x - r, (int)y - r, (int)x + r, (int)y + r);
            
            SelectObject(hdc, oldBrush);
            SelectObject(hdc, oldPen);
            DeleteObject(hBlack);

            // Accretion Ring (Orange Glow) - Transparent Center
            if (gazeT > 0.1f) {
                IntPtr hOrange = CreatePen(0, (int)(2 + gazeT * 5), 0x00A0FF);
                oldPen = SelectObject(hdc, hOrange);
                oldBrush = SelectObject(hdc, GetStockObject(NULL_BRUSH)); // Fixed: Transparent center

                int glowR = (int)(radius * 1.2f * (1.0f + gazeT));
                Ellipse(hdc, (int)x - glowR, (int)y - glowR, (int)x + glowR, (int)y + glowR);

                SelectObject(hdc, oldPen);
                SelectObject(hdc, oldBrush);
                DeleteObject(hOrange);
            }

            // Hairy Ball Singularity (White Dot at center if fully gazed)
            if (gazeT >= 1.0f) {
                IntPtr hWhite = CreateSolidBrush(0xFFFFFF);
                oldBrush = SelectObject(hdc, hWhite);
                oldPen = SelectObject(hdc, GetStockObject(NULL_BRUSH));
                Ellipse(hdc, (int)x - 3, (int)y - 3, (int)x + 3, (int)y + 3);
                SelectObject(hdc, oldBrush);
                SelectObject(hdc, oldPen);
                DeleteObject(hWhite);
            }
        }

        public static void Cleanup() {
            foreach (var h in _cache.Values) {
                DeleteObject(h.brush);
                DeleteObject(h.pen);
            }
            _cache.Clear();
        }
    }
}
