# Native Windows Sandbox Rendering Engine

A pure native Windows application built with **Rust** (physics core) and **C#** (Windows Forms frontend). No web technologies, no Electron, no Chromium - just pure Win32 GDI rendering.

## Architecture

```
┌─────────────────────────────────────┐
│     C# Windows Forms Frontend       │
│  - Input handling, UI, menu system  │
│  - GDI rendering via P/Invoke       │
│  - Double-buffered painting         │
└──────────────┬──────────────────────┘
               │ P/Invoke FFI
┌──────────────▼──────────────────────┐
│      Rust Physics Core (.dll)       │
│  - Spatial hash grid (O(n) collisions) │
│  - Particle free-list allocator     │
│  - Black hole gaze detection        │
│  - Euler integration physics        │
└─────────────────────────────────────┘
```

## Features

### Physics System
- **Spatial Hash Grid**: O(n) average collision detection using cell-based partitioning
- **Particle System**: Free-list allocator with 2000 particle capacity
- **Euler Integration**: Gravity, velocity, boundary bouncing with restitution
- **Elastic Collisions**: Impulse-based resolution with mass conservation

### Black Hole Simulation
- **Shapeless Data Point**: Zero render cost when not observed
- **Gaze Detection**: Activates when mouse within 100px
- **9-Second Timer**: Hairy Ball Theorem activation
  - t < 9s: Normal lensing
  - t = 9s: Singularity point surfaces
  - Visual effects: Event horizon, accretion ring, photon sphere

### Rendering
- **Pure GDI**: Ellipse, Rectangle, Polygon via gdi32.dll
- **Unified Cache**: One entry per color (brush + pen together)
- **Double Buffering**: Manual BitBlt-style via GetHdc/ReleaseHdc
- **Dark Theme**: #141414 background

### Tools
- **Draw**: Place shapes (Circle, Rect, Triangle, Line)
- **Move**: Drag objects (placeholder)
- **Delete**: Remove objects by clicking
- **Particles**: Spawn 20 particles with random velocities
- **Force**: Radial impulse blast

## Building

### Prerequisites
- **Rust**: `rustup install stable`
- **.NET 8 SDK**: Download from Microsoft
- **Windows 10/11 x64**

### Build Steps
```batch
build.bat
```

Or manually:
```batch
cd rust-core
cargo build --release

cd ..\cs-frontend
dotnet build -c Release
```

Output: `cs-frontend\bin\Release\net8.0-windows\SandboxEngine.exe`

## Controls

| Key | Action |
|-----|--------|
| D | Draw tool |
| M | Move tool |
| X | Delete tool |
| P | Particles tool |
| F | Force blast |
| 1-4 | Change shape (Circle/Rect/Triangle/Line) |
| Right-click | About dialog |

## Performance

Optimized for low-tier laptops (Ryzen 7 4800U, Vega 7 iGPU):
- **Zero heap allocations** in hot paths
- **Spatial hashing** reduces collision checks from O(n²) to O(n)
- **GDI object caching** prevents handle leaks
- **Particle free-list** reuses dead particles
- **Lazy black hole rendering** only when observed

## File Structure

```
sandbox-engine/
├── rust-core/
│   ├── Cargo.toml          # Rust crate config
│   └── src/lib.rs          # Physics engine (500+ lines)
├── cs-frontend/
│   ├── SandboxEngine.csproj
│   ├── app.manifest        # DPI awareness
│   ├── Program.cs          # Entry point
│   ├── PhysicsInterop.cs   # P/Invoke declarations
│   ├── Renderer.cs         # GDI rendering
│   ├── ToolController.cs   # Input handling
│   ├── BlackHole.cs        # Black hole logic
│   └── SandboxCanvas.cs    # Main canvas
└── build.bat               # Build script
```

## Technical Details

### Spatial Hash Implementation
```rust
// Each frame:
1. Clear all cells to -1
2. For each active object:
   - Calculate cell index from position
   - Set object.grid_next = cells[cell_idx]
   - Set cells[cell_idx] = object_index
3. Collision check: iterate 3x3 neighboring cells
   - Follow linked list via grid_next field
   - NEVER use mass field for linked list pointers
```

### Particle Free-List
```rust
// Allocation:
idx = free_list[free_count--]
particles[idx].active = true

// Deallocation:
particles[i].active = false
free_list[free_count++] = i  // Only if free_count < MAX

// Loop bound uses high_water_mark (never decrements)
for i in 0..high_water_mark {
    if !particles[i].active { continue }
    // Update particle
}
```

### GDI Cache
```csharp
// Unified brush+pen per color
Dictionary<uint, (IntPtr brush, IntPtr pen)> cache;

GetCached(color):
  if exists: return cached
  else: create both, add to cache, return
  
Cleanup():
  DeleteObject(all brushes)
  DeleteObject(all pens)
  cache.Clear()
```

## License

MIT License - Use freely for any purpose.
