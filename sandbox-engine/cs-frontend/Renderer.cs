using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace SandboxEngine
{
    /// <summary>
    /// OpenGL 4.3 instanced renderer.
    /// One draw call for all objects regardless of count.
    /// Gravitational lensing via two-pass framebuffer (Priority 4).
    /// </summary>
    public class GLRenderer : IDisposable
    {
        // Unit circle geometry (shared across all instances)
        private int _vao, _vbo, _ibo;
        // Per-instance data buffer (position, radius, color, shape)
        private int _instanceVbo;
        private const int MaxInstances = 1000;
        private const int InstanceStride = 20; // 5 * 4 bytes
        private byte[] _instanceData;

        // Particle buffers
        private int _particleVao, _particleVbo;
        private const int MaxParticles = 4000;

        // Framebuffer for lensing post-process pass (Priority 4)
        private int _fbo, _fboTex, _fboDepth;
        private int _quadVao, _quadVbo;
        private int _sceneWidth, _sceneHeight;

        // Shaders
        private int _objectShader;
        private int _particleShader;
        private int _lensShader;

        // Pinned buffers for bulk P/Invoke copy (Priority 2)
        private PhysicsInterop.RenderInstance[] _renderBuf;
        private GCHandle _renderBufHandle;
        private PhysicsInterop.Particle[] _particleBuf;
        private GCHandle _particleBufHandle;

        public GLRenderer()
        {
            _instanceData = new byte[MaxInstances * InstanceStride];
            _renderBuf    = new PhysicsInterop.RenderInstance[MaxInstances];
            _particleBuf  = new PhysicsInterop.Particle[MaxParticles];
            // Pin buffers so GC doesn't move them during P/Invoke
            _renderBufHandle  = GCHandle.Alloc(_renderBuf, GCHandleType.Pinned);
            _particleBufHandle = GCHandle.Alloc(_particleBuf, GCHandleType.Pinned);
        }

        public void Initialize(int width, int height)
        {
            _sceneWidth  = width;
            _sceneHeight = height;

            BuildCircleMesh();
            BuildParticleMesh();
            BuildFramebuffer(width, height);
            BuildQuadMesh();
            CompileShaders();
        }

        // --- Geometry setup ---

        private void BuildCircleMesh()
        {
            const int segments = 32;
            var verts = new float[(segments + 2) * 2];
            verts[0] = 0; verts[1] = 0; // center
            for (int i = 0; i <= segments; i++)
            {
                float a = i * 2.0f * MathF.PI / segments;
                verts[(i + 1) * 2]     = MathF.Cos(a);
                verts[(i + 1) * 2 + 1] = MathF.Sin(a);
            }

            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * 4, verts, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 8, 0);

            // Instance buffer: layout(location=1..4) x,y,radius,color,shape
            _instanceVbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, MaxInstances * InstanceStride, IntPtr.Zero, BufferUsageHint.DynamicDraw);

            // x, y (loc 1 = vec2, offset 0)
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, InstanceStride, 0);
            GL.VertexAttribDivisor(1, 1);

            // radius (loc 2 = float, offset 8)
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, InstanceStride, 8);
            GL.VertexAttribDivisor(2, 1);

            // color packed uint (loc 3, offset 12)
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribIPointer(3, 1, VertexAttribIntegerType.UnsignedInt, InstanceStride, (IntPtr)12);
            GL.VertexAttribDivisor(3, 1);

            // shape int (loc 4, offset 16)
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribIPointer(4, 1, VertexAttribIntegerType.Int, InstanceStride, (IntPtr)16);
            GL.VertexAttribDivisor(4, 1);

            GL.BindVertexArray(0);
        }

        private void BuildParticleMesh()
        {
            // Each particle: x, y, size, r, g, b, a (7 floats)
            _particleVao = GL.GenVertexArray();
            _particleVbo = GL.GenBuffer();
            GL.BindVertexArray(_particleVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _particleVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, MaxParticles * 7 * 4, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 28, 0); // x,y,size
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 28, 12); // r,g,b,a
            GL.BindVertexArray(0);
        }

        private void BuildFramebuffer(int w, int h)
        {
            // Color texture
            _fboTex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _fboTex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, w, h, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            // Depth renderbuffer
            _fboDepth = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _fboDepth);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, w, h);

            _fbo = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, _fboTex, 0);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
                RenderbufferTarget.Renderbuffer, _fboDepth);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        private void BuildQuadMesh()
        {
            float[] quad = { -1,-1,0,0,  1,-1,1,0,  1,1,1,1,  -1,-1,0,0,  1,1,1,1,  -1,1,0,1 };
            _quadVao = GL.GenVertexArray();
            _quadVbo = GL.GenBuffer();
            GL.BindVertexArray(_quadVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _quadVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, quad.Length * 4, quad, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 16, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 16, 8);
            GL.BindVertexArray(0);
        }

        // --- Shader compilation ---

        private void CompileShaders()
        {
            _objectShader   = CompileProgram(ObjectVert, ObjectFrag);
            _particleShader = CompileProgram(ParticleVert, ParticleFrag);
            _lensShader     = CompileProgram(LensVert, LensFrag);
        }

        private static int CompileProgram(string vert, string frag)
        {
            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, vert);
            GL.CompileShader(vs);

            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, frag);
            GL.CompileShader(fs);

            int prog = GL.CreateProgram();
            GL.AttachShader(prog, vs);
            GL.AttachShader(prog, fs);
            GL.LinkProgram(prog);
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
            return prog;
        }

        // --- Render frame ---

        public void RenderFrame(float bhX, float bhY, float gazeT, float interpAlpha)
        {
            // Pass 1: render scene to FBO
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
            GL.Viewport(0, 0, _sceneWidth, _sceneHeight);
            GL.ClearColor(0.078f, 0.078f, 0.118f, 1.0f); // #141414 approx
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            var proj = Matrix4.CreateOrthographicOffCenter(0, _sceneWidth, _sceneHeight, 0, -1, 1);

            // --- Draw objects (Priority 2: one bulk copy, one draw call) ---
            int objCount = PhysicsInterop.copy_render_instances(
                _renderBufHandle.AddrOfPinnedObject(), MaxInstances);

            if (objCount > 0)
            {
                // Upload instance data
                GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);
                var ptr = _renderBufHandle.AddrOfPinnedObject();
                GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero,
                    objCount * InstanceStride, ptr);

                GL.UseProgram(_objectShader);
                GL.UniformMatrix4(GL.GetUniformLocation(_objectShader, "uProj"), false, ref proj);

                GL.BindVertexArray(_vao);
                // DrawArraysInstanced: 34 = triangle fan for 32-segment circle
                GL.DrawArraysInstanced(PrimitiveType.TriangleFan, 0, 34, objCount);
                GL.BindVertexArray(0);
            }

            // --- Draw particles ---
            int partCount = PhysicsInterop.copy_active_particles(
                _particleBufHandle.AddrOfPinnedObject(), MaxParticles);

            if (partCount > 0)
            {
                var partVerts = new float[partCount * 7];
                for (int i = 0; i < partCount; i++)
                {
                    var p = _particleBuf[i];
                    float alpha = p.Life / p.MaxLife;
                    float r = ((p.Color >> 16) & 0xFF) / 255f;
                    float g = ((p.Color >>  8) & 0xFF) / 255f;
                    float b = (p.Color & 0xFF) / 255f;
                    int off = i * 7;
                    partVerts[off]   = p.X;
                    partVerts[off+1] = p.Y;
                    partVerts[off+2] = p.Size;
                    partVerts[off+3] = r;
                    partVerts[off+4] = g;
                    partVerts[off+5] = b;
                    partVerts[off+6] = alpha;
                }

                GL.BindBuffer(BufferTarget.ArrayBuffer, _particleVbo);
                GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, partCount * 28, partVerts);

                GL.UseProgram(_particleShader);
                GL.UniformMatrix4(GL.GetUniformLocation(_particleShader, "uProj"), false, ref proj);
                GL.Enable(EnableCap.ProgramPointSize);
                GL.BindVertexArray(_particleVao);
                GL.DrawArrays(PrimitiveType.Points, 0, partCount);
                GL.BindVertexArray(0);
            }

            // Pass 2: gravitational lensing post-process (Priority 4)
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, _sceneWidth, _sceneHeight);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            GL.UseProgram(_lensShader);
            GL.BindTexture(TextureTarget.Texture2D, _fboTex);
            GL.Uniform1(GL.GetUniformLocation(_lensShader, "uScene"), 0);
            GL.Uniform2(GL.GetUniformLocation(_lensShader, "uBhPos"),
                bhX / _sceneWidth, 1.0f - bhY / _sceneHeight); // flip Y for GL
            GL.Uniform1(GL.GetUniformLocation(_lensShader, "uGaze"), gazeT);
            GL.Uniform2(GL.GetUniformLocation(_lensShader, "uResolution"),
                (float)_sceneWidth, (float)_sceneHeight);

            GL.BindVertexArray(_quadVao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            GL.BindVertexArray(0);
        }

        public void Resize(int w, int h)
        {
            _sceneWidth = w; _sceneHeight = h;
            // Rebuild FBO at new size
            GL.DeleteFramebuffer(_fbo);
            GL.DeleteTexture(_fboTex);
            GL.DeleteRenderbuffer(_fboDepth);
            BuildFramebuffer(w, h);
        }

        public void Dispose()
        {
            _renderBufHandle.Free();
            _particleBufHandle.Free();
            GL.DeleteVertexArray(_vao);
            GL.DeleteBuffer(_vbo);
            GL.DeleteBuffer(_instanceVbo);
            GL.DeleteVertexArray(_particleVao);
            GL.DeleteBuffer(_particleVbo);
            GL.DeleteFramebuffer(_fbo);
            GL.DeleteTexture(_fboTex);
            GL.DeleteRenderbuffer(_fboDepth);
            GL.DeleteVertexArray(_quadVao);
            GL.DeleteBuffer(_quadVbo);
            GL.DeleteProgram(_objectShader);
            GL.DeleteProgram(_particleShader);
            GL.DeleteProgram(_lensShader);
        }

        // --- GLSL Shaders ---

        // Instanced object shader
        private const string ObjectVert = @"#version 430 core
layout(location=0) in vec2 aLocalPos;
layout(location=1) in vec2 aPos;
layout(location=2) in float aRadius;
layout(location=3) in uint aColor;
layout(location=4) in int aShape;

uniform mat4 uProj;

out vec4 vColor;
out vec2 vLocal;
flat out int vShape;

void main() {
    vLocal = aLocalPos;
    vColor = vec4(
        float((aColor >> 16u) & 0xFFu) / 255.0,
        float((aColor >>  8u) & 0xFFu) / 255.0,
        float( aColor         & 0xFFu) / 255.0,
        1.0
    );
    vShape = aShape;
    vec2 world = aPos + aLocalPos * aRadius;
    gl_Position = uProj * vec4(world, 0.0, 1.0);
}";

        private const string ObjectFrag = @"#version 430 core
in vec4 vColor;
in vec2 vLocal;
flat in int vShape;
out vec4 fragColor;

void main() {
    if (vShape == 0) {
        // Circle: discard outside unit circle
        if (dot(vLocal, vLocal) > 1.0) discard;
    }
    // Shapes 1 (rect) and 2 (triangle) are clipped by geometry
    // Add slight edge softness
    float edge = 1.0 - smoothstep(0.85, 1.0, length(vLocal));
    fragColor = vec4(vColor.rgb, vColor.a * edge);
}";

        // Particle shader (point sprites)
        private const string ParticleVert = @"#version 430 core
layout(location=0) in vec3 aData;  // x, y, size
layout(location=1) in vec4 aColor; // r, g, b, alpha

uniform mat4 uProj;
out vec4 vColor;

void main() {
    vColor = aColor;
    gl_PointSize = aData.z * 2.0;
    gl_Position = uProj * vec4(aData.xy, 0.0, 1.0);
}";

        private const string ParticleFrag = @"#version 430 core
in vec4 vColor;
out vec4 fragColor;
void main() {
    vec2 c = gl_PointCoord * 2.0 - 1.0;
    if (dot(c, c) > 1.0) discard;
    fragColor = vColor;
}";

        // Gravitational lensing post-process (Priority 4)
        // Simplified Schwarzschild lens: bends UV coordinates around BH position
        private const string LensVert = @"#version 430 core
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aUV;
out vec2 vUV;
void main() { vUV = aUV; gl_Position = vec4(aPos, 0.0, 1.0); }";

        private const string LensFrag = @"#version 430 core
in vec2 vUV;
out vec4 fragColor;

uniform sampler2D uScene;
uniform vec2  uBhPos;       // BH position in UV space (0..1)
uniform float uGaze;        // 0..9
uniform vec2  uResolution;

void main() {
    float strength = uGaze * 0.04;  // Scale lensing with gaze time

    vec2 delta = vUV - uBhPos;
    // Correct for aspect ratio so distortion is circular
    delta.x *= uResolution.x / uResolution.y;
    float dist = length(delta);

    vec2 distorted = vUV;
    if (dist > 0.001 && uGaze > 0.05) {
        // Inverse square lensing approximation
        float bend = strength / (dist * dist + 0.01);
        bend = min(bend, 0.3); // Clamp to prevent UV explosion
        vec2 dir = normalize(delta);
        dir.x /= (uResolution.x / uResolution.y); // undo aspect correction
        distorted = vUV - dir * bend;
    }

    // Black hole event horizon: draw black circle
    float bhRadius = 0.03 + uGaze * 0.008;
    float rawDist = length(vUV - uBhPos) * (uResolution.y / uResolution.x);
    if (rawDist < bhRadius * (uResolution.x / uResolution.y)) {
        fragColor = vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    // Accretion ring glow
    float ringInner = bhRadius * 1.1;
    float ringOuter = bhRadius * 1.6;
    float ringDist  = length(vUV - uBhPos);
    float ringMask  = smoothstep(ringInner, ringInner + 0.005, ringDist)
                    * (1.0 - smoothstep(ringOuter - 0.005, ringOuter, ringDist));
    vec3 ringColor  = mix(vec3(1.0, 0.4, 0.0), vec3(1.0, 0.9, 0.4), ringDist / ringOuter);

    vec4 scene = texture(uScene, clamp(distorted, 0.0, 1.0));
    fragColor   = vec4(scene.rgb + ringColor * ringMask * uGaze * 0.15, 1.0);
}";
    }
}
