using System;
using System.Runtime.InteropServices;

namespace SandboxEngine
{
    public static class PhysicsInterop
    {
        private const string DllName = "sandbox_core";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void init_physics(int width, int height);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void resize_physics(int width, int height);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void update_physics(float dt, float mouseX, float mouseY, int width, int height);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int add_object(float x, float y, int shape, uint color, float size);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void remove_object(int index);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void set_object_position(int index, float x, float y);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void spawn_particles(float x, float y, int count);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void wake_objects_near(float x, float y, float radius);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void set_gravity(float g);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int get_object_count();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int get_particle_active_count();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int get_object_slot_count();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int get_particle_high_water_mark();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void get_black_hole_position(out float x, out float y);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern float get_black_hole_gaze();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool get_black_hole_active();

        // Priority 2: One P/Invoke bulk copy for all render data per frame
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int copy_render_instances(IntPtr outBuf, int bufLen);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int copy_active_particles(IntPtr outBuf, int bufLen);

        // Packed render struct: 20 bytes, GPU-friendly (x, y, radius, color, shape)
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct RenderInstance
        {
            public float X, Y, Radius;
            public uint  Color;
            public int   Shape;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Particle
        {
            public float X, Y, VX, VY, Life, MaxLife;
            public uint  Color;
            public float Size;
            [MarshalAs(UnmanagedType.I1)] public bool Active;
        }
    }
}
