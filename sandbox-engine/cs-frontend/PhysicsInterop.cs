using System;
using System.Runtime.InteropServices;

namespace SandboxEngine;

/// <summary>
/// P/Invoke declarations for calling the Rust physics core DLL.
/// All functions use C-compatible calling convention.
/// </summary>
public static class PhysicsInterop
{
    private const string DllName = "sandbox_core.dll";

    // GameObject structure matching Rust layout
    [StructLayout(LayoutKind.Sequential)]
    public struct GameObject
    {
        public float x;
        public float y;
        public float vx;
        public float vy;
        public float radius;
        public float width;
        public float height;
        public float mass;
        public float restitution;
        public int shape;      // Shape enum as i32
        public uint color;     // COLORREF format
        [MarshalAs(UnmanagedType.Bool)]
        public bool active;
        public int grid_next;  // Linked list pointer for spatial hash
    }

    // Particle structure matching Rust layout
    [StructLayout(LayoutKind.Sequential)]
    public struct Particle
    {
        public float x;
        public float y;
        public float vx;
        public float vy;
        public float life;
        public float max_life;
        public uint color;
        public float size;
        [MarshalAs(UnmanagedType.Bool)]
        public bool active;
    }

    // BlackHole structure matching Rust layout
    [StructLayout(LayoutKind.Sequential)]
    public struct BlackHole
    {
        public float x;
        public float y;
        public double mass;
        [MarshalAs(UnmanagedType.Bool)]
        public bool active;
        public float gaze_timer;
        public float schwarzschild_radius;
    }

    /// <summary>
    /// Initialize physics system with given dimensions.
    /// Must be called before any other physics functions.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void init_physics(int width, int height);

    /// <summary>
    /// Update physics simulation by dt seconds.
    /// Call this every frame with delta time (e.g., 0.016f for 60fps).
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void update_physics(float dt);

    /// <summary>
    /// Spawn particles at given position.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void spawn_particles(float x, float y, int count);

    /// <summary>
    /// Add an object to the simulation.
    /// Returns the index of the created object, or -1 on failure.
    /// shape: 0=Circle, 1=Rect, 2=Triangle, 3=Line
    /// color: COLORREF format (0x00BBGGRR)
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int add_object(float x, float y, int shape, uint color, float size);

    /// <summary>
    /// Remove (deactivate) an object by index.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void remove_object(int index);

    /// <summary>
    /// Get count of active objects.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int get_object_count();

    /// <summary>
    /// Get count of active particles.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int get_particle_active_count();

    /// <summary>
    /// Set gravity value (units/s²).
    /// Default is 500.0.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void set_gravity(float g);

    /// <summary>
    /// Get object data at specified index.
    /// Returns true if successful, false if index is invalid.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool get_object_at(int index, out GameObject obj);

    /// <summary>
    /// Get particle data at specified index.
    /// Returns true if successful, false if index is invalid.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool get_particle_at(int index, out Particle particle);

    /// <summary>
    /// Get black hole data.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void get_black_hole(out BlackHole bh);

    /// <summary>
    /// Update black hole position and check activation based on mouse proximity.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void update_black_hole(float x, float y, float mouse_x, float mouse_y);

    /// <summary>
    /// Get maximum object capacity.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int get_max_objects();

    /// <summary>
    /// Get maximum particle capacity.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int get_max_particles();
}
