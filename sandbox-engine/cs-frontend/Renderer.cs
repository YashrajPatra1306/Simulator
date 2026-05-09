using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace SandboxEngine
{
    public static class Renderer
    {
        private static readonly Dictionary<uint, (IntPtr brush, IntPtr pen)> _gdiCache = new();
        private const int CACHE_SIZE = 256;

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(uint color);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreatePen(int style, int width, uint color);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool Ellipse(IntPtr hdc, int left, int top, int right, int bottom);

        [DllImport("gdi32.dll")]
        private static extern bool Rectangle(IntPtr hdc, int left, int top, int right, int bottom);

        [DllImport("gdi32.dll")]
        private static extern bool Polygon(IntPtr hdc, POINT[] points, int count);

        [DllImport("gdi32.dll")]
        private static extern IntPtr GetStockObject(int fnObject);

        private const int NULL_BRUSH = 5;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X, Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        public static (IntPtr brush, IntPtr pen) GetCachedGDIObjects(uint color)
        {
            if (_gdiCache.TryGetValue(color, out var cached))
            {
                return cached;
            }

            var brush = CreateSolidBrush(color);
            var pen = CreatePen(0, 1, color);

            if (_gdiCache.Count >= CACHE_SIZE)
            {
                return (brush, pen);
            }

            _gdiCache[color] = (brush, pen);
            return (brush, pen);
        }

        public static void CleanupGdiCache()
        {
            foreach (var (_, (brush, pen)) in _gdiCache)
            {
                DeleteObject(brush);
                DeleteObject(pen);
            }
            _gdiCache.Clear();
        }

        public static void RenderObject(IntPtr hdc, GameObject obj)
        {
            if (!obj.active) return;

            var (brush, pen) = GetCachedGDIObjects(obj.color);
            IntPtr oldBrush = SelectObject(hdc, brush);
            IntPtr oldPen = SelectObject(hdc, pen);

            int x = (int)obj.x, y = (int)obj.y;
            int r = (int)obj.radius;

            switch (obj.shape)
            {
                case 0: // Circle
                    Ellipse(hdc, x - r, y - r, x + r, y + r);
                    break;
                case 1: // Rect
                    Rectangle(hdc, x - r, y - r, x + r, y + r);
                    break;
                case 2: // Triangle
                    POINT[] tri = new POINT[]
                    {
                        new POINT(x, y - r),
                        new POINT(x - r, y + r),
                        new POINT(x + r, y + r)
                    };
                    Polygon(hdc, tri, 3);
                    break;
                case 3: // Line
                    // Simplified as thin rectangle
                    Rectangle(hdc, x - r, y - 2, x + r, y + 2);
                    break;
            }

            SelectObject(hdc, oldBrush);
            SelectObject(hdc, oldPen);
        }

        public static void RenderParticle(IntPtr hdc, Particle p)
        {
            if (!p.active) return;

            var (brush, _) = GetCachedGDIObjects(p.color);
            IntPtr oldBrush = SelectObject(hdc, brush);
            IntPtr oldPen = SelectObject(hdc, brush);

            int size = (int)p.size;
            Ellipse(hdc, (int)p.x - size, (int)p.y - size, (int)p.x + size, (int)p.y + size);

            SelectObject(hdc, oldBrush);
            SelectObject(hdc, oldPen);
        }

        public static void RenderBlackHole(IntPtr hdc, float x, float y, float gazeTimer, float baseRadius)
        {
            if (gazeTimer < 0.01f) return;

            float t = Math.Min(gazeTimer / 9.0f, 1.0f);
            int cx = (int)x, cy = (int)y;

            // Event horizon - black circle
            IntPtr blackBrush = CreateSolidBrush(0x000000);
            IntPtr oldBrush = SelectObject(hdc, blackBrush);
            IntPtr blackPen = CreatePen(0, 1, 0x000000);
            IntPtr oldPen = SelectObject(hdc, blackPen);

            int horizonRadius = (int)(baseRadius * (1.0f + t * 2.0f));
            Ellipse(hdc, cx - horizonRadius, cy - horizonRadius, cx + horizonRadius, cy + horizonRadius);

            SelectObject(hdc, oldBrush);
            SelectObject(hdc, oldPen);
            DeleteObject(blackBrush);
            DeleteObject(blackPen);

            // Accretion glow ring - orange, only if t > 0.1
            if (t > 0.1f)
            {
                uint orange = 0x0080FF; // BGR format
                var (orangeBrush, orangePen) = GetCachedGDIObjects(orange);
                
                // Use NULL_BRUSH to not fill interior
                IntPtr nullBrush = GetStockObject(NULL_BRUSH);
                oldBrush = SelectObject(hdc, nullBrush);
                
                int thick = 2 + (int)(t * 5);
                IntPtr thickPen = CreatePen(0, thick, orange);
                oldPen = SelectObject(hdc, thickPen);

                int glowRadius = (int)(horizonRadius * 1.2f);
                Ellipse(hdc, cx - glowRadius, cy - glowRadius, cx + glowRadius, cy + glowRadius);

                SelectObject(hdc, oldBrush);
                SelectObject(hdc, oldPen);
                DeleteObject(thickPen);
            }

            // Hairy Ball singularity point - white dot at center, only if t >= 1.0
            if (t >= 1.0f)
            {
                IntPtr whiteBrush = CreateSolidBrush(0xFFFFFF);
                IntPtr whitePen = CreatePen(0, 1, 0xFFFFFF);
                oldBrush = SelectObject(hdc, whiteBrush);
                oldPen = SelectObject(hdc, whitePen);

                Ellipse(hdc, cx - 3, cy - 3, cx + 3, cy + 3);

                SelectObject(hdc, oldBrush);
                SelectObject(hdc, oldPen);
                DeleteObject(whiteBrush);
                DeleteObject(whitePen);
            }
        }

        public static void ClearBackground(IntPtr hdc, int width, int height)
        {
            IntPtr darkBrush = CreateSolidBrush(0xFF141414); // Dark blue-gray in BGR
            IntPtr oldBrush = SelectObject(hdc, darkBrush);
            
            var rect = new RECT { left = 0, top = 0, right = width, bottom = height };
            FillRect(hdc, ref rect, darkBrush);
            
            SelectObject(hdc, oldBrush);
            DeleteObject(darkBrush);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left, top, right, bottom;
        }

        [DllImport("gdi32.dll")]
        private static extern bool FillRect(IntPtr hdc, ref RECT lprc, IntPtr hbr);
    }
}
