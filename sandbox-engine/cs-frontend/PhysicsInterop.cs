using System;
using System.Runtime.InteropServices;

namespace SandboxEngine
{
    [StructLayout(LayoutKind.Sequential)]
    public struct GameObject
    {
        public float x, y, vx, vy;
        public float radius, width, height;
        public float mass, restitution;
        public int shape;
        public uint color;
        [MarshalAs(UnmanagedType.Bool)]
        public bool active;
        public int grid_next;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Particle
    {
        public float x, y, vx, vy;
        public float life, max_life;
        public uint color;
        public float size;
        [MarshalAs(UnmanagedType.Bool)]
        public bool active;
    }

    public static class PhysicsInterop
    {
        private const string DllName = "sandbox_core.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void init_physics(int width, int height);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void update_physics(float dt);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void spawn_particles(float x, float y, int count);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int add_object(float x, float y, int shape, uint color, float size);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void remove_object(int index);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int get_object_count();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int get_particle_active_count();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void set_gravity(float g);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void update_black_hole(float x, float y, float mouse_x, float mouse_y, float dt);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern float get_black_hole_gaze_timer();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern (float, float) get_black_hole_position();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int get_max_objects();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool get_object_at(int index, out GameObject obj);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool get_particle_at(int index, out Particle particle);
    }
}
