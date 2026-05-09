# Sandbox Rendering Engine

A pure native Windows sandbox rendering engine built with **Rust** (physics core) and **C#** (Windows Forms frontend). No web technologies, no Electron, no Chromium - just pure Win32/GDI rendering.

## Architecture

```
┌─────────────────────────────────────────────┐
│         C# Windows Forms Frontend           │
│  - UI, Input, Menu, Toolbar                 │
│  - GDI Rendering with object caching        │
│  - P/Invoke to Rust DLL                     │
└───────────────────┬─────────────────────────┘
                    │ FFI (C-compatible)
┌───────────────────▼─────────────────────────┐
│          Rust Physics Core (DLL)            │
│  - Spatial hashing (O(n) collision)         │
│  - Particle system (free-list allocation)   │
│  - Black hole simulation                    │
│  - Gaze detection & Hairy Ball effect       │
└─────────────────────────────────────────────┘
```

## Features

### Physics Simulation
- **Spatial Hash Grid**: O(n) average collision detection using cell-based partitioning
- **Euler Integration**: Velocity Verlet-style physics with gravity
- **Elastic Collisions**: Impulse-based resolution with restitution
- **Boundary Bouncing**: Objects bounce off window edges

### Particle System
- **Free-List Allocation**: Efficient particle reuse without garbage collection
- **High-Water Mark Tracking**: `particleCount` never decrements, preventing skip bugs
- **Configurable Lifetimes**: Each particle has independent life/max_life

### Black Hole (Shapeless Data Point)
- **Lazy Activation**: Only renders when mouse is within 100px (gaze detection)
- **Gaze Timer**: 9-second timer triggers Hairy Ball Theorem visualization
- **Gravitational Pull**: Real inverse-square law force (scaled for visuals)
- **Visual Effects**:
  - Event horizon (black circle, scales with gaze)
  - Accretion glow ring (orange, appears at t > 0.1)
  - Singularity point (white, appears at t >= 1.0)

### Tools
- **Draw**: Create objects (Circle, Rect, Triangle, Line)
- **Move**: Drag objects with mouse
- **Delete**: Remove objects
- **Particles**: Spawn particle burst
- **Force Blast**: Radial impulse (placeholder for future enhancement)

### Performance Optimizations
- **GDI Object Caching**: One cache entry per color holds both brush AND pen
- **Double Buffering**: Windows Forms `DoubleBuffered` + custom backbuffer
- **Rolling FPS Average**: 30-frame circular buffer for stable display
- **Low Power Mode**: Doubles dt (0.032s), halves timer frequency (32ms)

## Project Structure

```
/sandbox-engine
├── rust-core/
│   ├── Cargo.toml          # Rust crate config (cdylib)
│   └── src/
│       └── lib.rs          # Physics, spatial hash, particles, black hole
├── cs-frontend/
│   ├── SandboxEngine.csproj
│   ├── app.manifest        # DPI awareness, Windows 10/11 support
│   ├── PhysicsInterop.cs   # P/Invoke declarations
│   ├── Renderer.cs         # GDI rendering with caching
│   ├── ToolController.cs   # Input handling, tool logic
│   ├── BlackHole.cs        # Black hole controller
│   ├── SandboxCanvas.cs    # Main canvas control, FPS calc
│   └── Program.cs          # Application entry point
└── build.bat               # Build script (Rust + C#)
```

## Requirements

- **Windows 10/11 x64**
- **Rust** (https://rustup.rs/) - for physics core
- **.NET 8 SDK** (https://dotnet.microsoft.com/) - for C# frontend
- **Hardware**: Tested on Ryzen 7 4800U, Vega 7 iGPU, 8GB RAM

## Building

### Quick Build
```batch
build.bat
```

### Manual Build

1. **Build Rust Core**:
```batch
cd rust-core
cargo build --release
copy target\release\sandbox_core.dll ..\cs-frontend\
```

2. **Build C# Frontend**:
```batch
cd cs-frontend
dotnet restore
dotnet build --configuration Release
```

## Running

```batch
cd cs-frontend
dotnet run --configuration Release
```

Or directly execute:
```
cs-frontend\bin\Release\net8.0-windows\SandboxEngine.exe
```

## Controls

| Key | Action |
|-----|--------|
| **D** | Draw tool |
| **M** | Move tool |
| **X** | Delete tool |
| **P** | Particles tool |
| **F** | Force blast tool |
| **1** | Circle shape |
| **2** | Rectangle shape |
| **3** | Triangle shape |
| **4** | Line shape |
| **Right-click** | Color picker |

## File Format

Scene files use plain text format:
```
shape x y vx vy radius width height color mass restitution
```

Example:
```
0 400.0 300.0 0.0 50.0 20.0 20.0 20.0 16711680 1.0 0.8
1 200.0 150.0 10.0 0.0 30.0 60.0 40.0 65280 2.5 0.7
```

Where:
- `shape`: 0=Circle, 1=Rect, 2=Triangle, 3=Line
- `color`: COLORREF format (0x00BBGGRR)

## Technical Details

### Spatial Hash Implementation
```rust
// grid_next field (NOT mass) stores linked list pointer
objects[i].grid_next = cells[cell_idx];
cells[cell_idx] = i;

// Traversal
j = cells[cell_idx];
while j != -1 {
    process(j);
    j = objects[j].grid_next;
}
```

### GDI Cache Pattern
```csharp
// One entry per color holds BOTH brush and pen
for (int i = 0; i < cacheCount; i++) {
    if (cache[i].color == color) {
        return (cache[i].brush, cache[i].pen);
    }
}
// Miss: create both in single entry
cache[cacheCount++] = (color, CreateBrush(), CreatePen());
```

### Particle Free-List
```rust
// Alloc: pop from free stack
free_count -= 1;
idx = free_list[free_count];
particles[idx].active = true;

// Free: push to free stack (with bounds check)
if free_count < MAX_PARTICLES {
    free_list[free_count++] = idx;
    particles[idx].active = false;
}
```

### Black Hole Gaze Detection
```rust
// Mouse proximity check (replace with frustum dot product in 3D)
let dist = sqrt((mouse_x - bh.x)² + (mouse_y - bh.y)²);
bh.active = dist < 100.0;

// Timer: increment when active, decay when inactive
bh.gaze_timer = if active {
    min(gaze_timer + dt, 9.0)
} else {
    max(gaze_timer - dt * 2.0, 0.0)
};
```

## Known Limitations

1. **Object Dragging**: Move tool finds objects but doesn't update position in Rust (would need additional FFI setter)
2. **Force Blast**: Placeholder implementation - velocity changes not applied to Rust side
3. **Collision Symmetry**: Only updates one object's velocity in pair (future: accumulate impulses)
4. **Max Capacity**: Hard limits at 1000 objects, 2000 particles

## License

MIT License - See LICENSE file for details.

## Contributing

Issues and pull requests welcome. Key areas for improvement:
- Add FFI setters for object properties (velocity, position)
- Implement proper force blast in Rust core
- Add more shapes (polygon, star)
- Implement scene serialization/deserialization in Rust
- Add camera pan/zoom
