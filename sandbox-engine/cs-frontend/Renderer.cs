using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SandboxEngine;

/// <summary>
/// GDI Renderer with object caching to prevent handle leaks.
/// Uses direct Win32 GDI calls for maximum performance on low-end hardware.
/// </summary>
public static class Renderer
{
    // GDI cache: one entry per unique COLORREF color
    private const int CACHE_SIZE = 256;
    
    [StructLayout(LayoutKind.Sequential)]
    private struct CacheEntry
    {
        public uint color;
        public IntPtr brush;
        public IntPtr pen;
        
        public CacheEntry(uint c, IntPtr b, IntPtr p)
        {
            color = c;
            brush = b;
            pen = p;
        }
    }
    
    private static readonly CacheEntry[] _gdiCache = new CacheEntry[CACHE_SIZE];
    private static int _gdiCacheCount = 0;
    
    // Win32 GDI P/Invoke declarations
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateSolidBrush(uint crColor);
    
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreatePen(int fnPenStyle, int nWidth, uint crColor);
    
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);
    
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);
    
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Ellipse(IntPtr hdc, int left, int top, int right, int bottom);
    
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Rectangle(IntPtr hdc, int left, int top, int right, int bottom);
    
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Polygon(IntPtr hdc, POINT[] lpPoints, int cPoints);
    
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveToEx(IntPtr hdc, int x, int y, IntPtr lpPoint);
    
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LineTo(IntPtr hdc, int x, int y);
    
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int SetBkMode(IntPtr hdc, int iBkMode);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
        
        public POINT(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }
    
    private const int TRANSPARENT = 1;
    private const int PS_SOLID = 0;
    
    /// <summary>
    /// Get cached brush and pen for a color.
    /// Creates both in a single cache entry on first miss.
    /// Returns (brush, pen) tuple.
    /// </summary>
    public static (IntPtr brush, IntPtr pen) GetCachedGdiObjects(uint color)
    {
        // Search existing cache
        for (int i = 0; i < _gdiCacheCount; i++)
        {
            if (_gdiCache[i].color == color)
            {
                return (_gdiCache[i].brush, _gdiCache[i].pen);
            }
        }
        
        // Cache miss - create both brush and pen
        IntPtr brush = CreateSolidBrush(color);
        IntPtr pen = CreatePen(PS_SOLID, 1, color);
        
        if (_gdiCacheCount < CACHE_SIZE)
        {
            _gdiCache[_gdiCacheCount] = new CacheEntry(color, brush, pen);
            _gdiCacheCount++;
            return (brush, pen);
        }
        
        // Cache full - return temporary handles (caller must delete)
        return (brush, pen);
    }
    
    /// <summary>
    /// Cleanup all cached GDI objects.
    /// Call before application exit.
    /// </summary>
    public static void CleanupGdiCache()
    {
        for (int i = 0; i < _gdiCacheCount; i++)
        {
            if (_gdiCache[i].brush != IntPtr.Zero)
                DeleteObject(_gdiCache[i].brush);
            if (_gdiCache[i].pen != IntPtr.Zero)
                DeleteObject(_gdiCache[i].pen);
        }
        _gdiCacheCount = 0;
    }
    
    /// <summary>
    /// Render a game object to the specified DC.
    /// </summary>
    public static void RenderObject(IntPtr hdc, PhysicsInterop.GameObject obj)
    {
        if (!obj.active) return;
        
        var (brush, pen) = GetCachedGdiObjects(obj.color);
        IntPtr oldBrush = SelectObject(hdc, brush);
        IntPtr oldPen = SelectObject(hdc, pen);
        
        int x = (int)obj.x;
        int y = (int)obj.y;
        int r = (int)obj.radius;
        int w = (int)obj.width;
        int h = (int)obj.height;
        
        switch ((ShapeType)obj.shape)
        {
            case ShapeType.Circle:
                Ellipse(hdc, x - r, y - r, x + r, y + r);
                break;
                
            case ShapeType.Rect:
                Rectangle(hdc, x - w/2, y - h/2, x + w/2, y + h/2);
                break;
                
            case ShapeType.Triangle:
                var triPoints = new POINT[]
                {
                    new POINT(x, y - r),
                    new POINT(x - r, y + r),
                    new POINT(x + r, y + r)
                };
                Polygon(hdc, triPoints, 3);
                break;
                
            case ShapeType.Line:
                SetBkMode(hdc, TRANSPARENT);
                MoveToEx(hdc, x - w/2, y - h/2, IntPtr.Zero);
                LineTo(hdc, x + w/2, y + h/2);
                break;
        }
        
        // Restore old objects (don't delete cached ones)
        SelectObject(hdc, oldBrush);
        SelectObject(hdc, oldPen);
    }
    
    /// <summary>
    /// Render a particle to the specified DC.
    /// </summary>
    public static void RenderParticle(IntPtr hdc, PhysicsInterop.Particle particle)
    {
        if (!particle.active) return;
        
        var (brush, pen) = GetCachedGdiObjects(particle.color);
        IntPtr oldBrush = SelectObject(hdc, brush);
        IntPtr oldPen = SelectObject(hdc, pen);
        
        int size = (int)particle.size;
        int x = (int)particle.x;
        int y = (int)particle.y;
        
        Ellipse(hdc, x - size/2, y - size/2, x + size/2, y + size/2);
        
        SelectObject(hdc, oldBrush);
        SelectObject(hdc, oldPen);
    }
    
    /// <summary>
    /// Render the black hole visualization.
    /// Only renders when gaze_timer > 0.
    /// </summary>
    public static void RenderBlackHole(IntPtr hdc, PhysicsInterop.BlackHole bh, int centerX, int centerY)
    {
        if (bh.gaze_timer <= 0.01f) return;
        
        float t = Math.Min(bh.gaze_timer / 9.0f, 1.0f);
        float radius = bh.schwarzschild_radius * (1.0f + t * 2.0f);
        int r = (int)radius;
        int x = centerX;
        int y = centerY;
        
        // Event horizon: black filled circle
        IntPtr blackBrush = CreateSolidBrush(0x00000000);
        IntPtr blackPen = CreatePen(PS_SOLID, 1, 0x00000000);
        IntPtr oldBrush = SelectObject(hdc, blackBrush);
        IntPtr oldPen = SelectObject(hdc, blackPen);
        
        Ellipse(hdc, x - r, y - r, x + r, y + r);
        
        SelectObject(hdc, oldBrush);
        SelectObject(hdc, oldPen);
        DeleteObject(blackBrush);
        DeleteObject(blackPen);
        
        // Accretion glow ring: orange, only if t > 0.1
        if (t > 0.1f)
        {
            int glowRadius = (int)(r * 1.2f);
            int thickness = (int)(2.0f + t * 5.0f);
            
            IntPtr orangePen = CreatePen(PS_SOLID, thickness, 0x000080FF); // Orange in COLORREF
            IntPtr oldPen2 = SelectObject(hdc, orangePen);
            
            // Draw ring as unfilled ellipse
            IntPtr nullBrush = CreateSolidBrush(0xFFFFFF); // White background
            IntPtr oldBrush2 = SelectObject(hdc, nullBrush);
            
            Ellipse(hdc, x - glowRadius, y - glowRadius, x + glowRadius, y + glowRadius);
            
            SelectObject(hdc, oldPen2);
            SelectObject(hdc, oldBrush2);
            DeleteObject(orangePen);
            DeleteObject(nullBrush);
        }
        
        // Hairy Ball singularity point: white 3px circle, only if t >= 1.0
        if (t >= 1.0f)
        {
            IntPtr whiteBrush = CreateSolidBrush(0x00FFFFFF);
            IntPtr whitePen = CreatePen(PS_SOLID, 1, 0x00FFFFFF);
            IntPtr oldBrush3 = SelectObject(hdc, whiteBrush);
            IntPtr oldPen3 = SelectObject(hdc, whitePen);
            
            Ellipse(hdc, x - 2, y - 2, x + 2, y + 2);
            
            SelectObject(hdc, oldBrush3);
            SelectObject(hdc, oldPen3);
            DeleteObject(whiteBrush);
            DeleteObject(whitePen);
        }
    }
}

/// <summary>
/// Shape type enumeration matching Rust side.
/// </summary>
public enum ShapeType
{
    Circle = 0,
    Rect = 1,
    Triangle = 2,
    Line = 3
}
