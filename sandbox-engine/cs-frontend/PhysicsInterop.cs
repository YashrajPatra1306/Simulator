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
        public static extern void update_physics(float dt, float mouseX, float mouseY, int width, int height);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int add_object(float x, float y, int shape, uint color, float size);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void remove_object(int index);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void spawn_particles(float x, float y, int count);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int get_object_count();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int get_particle_active_count();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int get_max_objects();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void set_gravity(float g);

        // Fixed: Out parameters instead of tuple
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void get_black_hole_position(out float x, out float y);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern float get_black_hole_gaze();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool get_black_hole_active();
    }
}
