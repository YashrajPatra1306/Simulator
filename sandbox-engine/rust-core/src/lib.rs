//! Rust Physics Core for Sandbox Rendering Engine
//! 
//! This module provides physics simulation, spatial hashing, particle systems,
//! and black hole simulation. Exposed via FFI for C# consumption.

use std::sync::atomic::{AtomicBool, Ordering};
use std::cell::RefCell;

// ============================================================================
// Constants
// ============================================================================

const MAX_OBJECTS: usize = 1000;
const MAX_PARTICLES: usize = 2000;
const SPATIAL_CELL_SIZE: f32 = 100.0;
const GRAVITY_DEFAULT: f32 = 500.0;
const BLACK_HOLE_GAZE_MAX: f32 = 9.0;

// ============================================================================
// Data Structures
// ============================================================================

#[repr(i32)]
#[derive(Clone, Copy, PartialEq, Debug)]
pub enum Shape {
    Circle = 0,
    Rect = 1,
    Triangle = 2,
    Line = 3,
}

#[repr(C)]
#[derive(Clone, Copy, Debug)]
pub struct GameObject {
    pub x: f32,
    pub y: f32,
    pub vx: f32,
    pub vy: f32,
    pub radius: f32,
    pub width: f32,
    pub height: f32,
    pub mass: f32,
    pub restitution: f32,
    pub shape: i32, // Shape enum as i32
    pub color: u32, // COLORREF format
    pub active: bool,
    pub grid_next: i32, // Linked list pointer for spatial hash (-1 = end)
}

impl Default for GameObject {
    fn default() -> Self {
        GameObject {
            x: 0.0,
            y: 0.0,
            vx: 0.0,
            vy: 0.0,
            radius: 0.0,
            width: 0.0,
            height: 0.0,
            mass: 1.0,
            restitution: 0.8,
            shape: Shape::Circle as i32,
            color: 0x00FF0000, // Red in COLORREF
            active: false,
            grid_next: -1,
        }
    }
}

#[repr(C)]
#[derive(Clone, Copy, Debug)]
pub struct Particle {
    pub x: f32,
    pub y: f32,
    pub vx: f32,
    pub vy: f32,
    pub life: f32,
    pub max_life: f32,
    pub color: u32,
    pub size: f32,
    pub active: bool,
}

impl Default for Particle {
    fn default() -> Self {
        Particle {
            x: 0.0,
            y: 0.0,
            vx: 0.0,
            vy: 0.0,
            life: 0.0,
            max_life: 1.0,
            color: 0x00FFFFFF,
            size: 2.0,
            active: false,
        }
    }
}

#[repr(C)]
#[derive(Debug)]
pub struct ParticlePool {
    pub particles: [Particle; MAX_PARTICLES],
    pub free_list: [i32; MAX_PARTICLES],
    pub free_count: i32,
    pub high_water_mark: i32, // Never decrements - tracks max allocated index
    pub active_count: i32,    // For display only
}

impl ParticlePool {
    fn new() -> Self {
        let mut pool = ParticlePool {
            particles: [Particle::default(); MAX_PARTICLES],
            free_list: [0; MAX_PARTICLES],
            free_count: MAX_PARTICLES as i32,
            high_water_mark: 0,
            active_count: 0,
        };
        // Initialize free list with all indices
        for i in 0..MAX_PARTICLES {
            pool.free_list[i] = i as i32;
        }
        pool
    }

    fn alloc(&mut self) -> Option<i32> {
        if self.free_count == 0 {
            return None;
        }
        self.free_count -= 1;
        let idx = self.free_list[self.free_count as usize];
        self.particles[idx as usize].active = true;
        self.high_water_mark = self.high_water_mark.max(idx + 1);
        self.active_count += 1;
        Some(idx)
    }

    fn free(&mut self, idx: i32) {
        if idx < 0 || idx >= MAX_PARTICLES as i32 {
            return;
        }
        if !self.particles[idx as usize].active {
            return; // Already inactive
        }
        self.particles[idx as usize].active = false;
        if self.free_count < MAX_PARTICLES as i32 {
            self.free_list[self.free_count as usize] = idx;
            self.free_count += 1;
            self.active_count = self.active_count.saturating_sub(1);
        }
    }
}

#[repr(C)]
#[derive(Debug)]
pub struct SpatialGrid {
    pub cells: Vec<i32>,
    pub cell_size: f32,
    pub width: i32,
    pub height: i32,
}

impl SpatialGrid {
    fn new(width: i32, height: i32) -> Self {
        let cols = (width as f32 / SPATIAL_CELL_SIZE).ceil() as i32 + 1;
        let rows = (height as f32 / SPATIAL_CELL_SIZE).ceil() as i32 + 1;
        let cell_count = (cols * rows) as usize;
        SpatialGrid {
            cells: vec![-1; cell_count],
            cell_size: SPATIAL_CELL_SIZE,
            width,
            height,
        }
    }

    fn resize(&mut self, width: i32, height: i32) {
        let cols = (width as f32 / self.cell_size).ceil() as i32 + 1;
        let rows = (height as f32 / self.cell_size).ceil() as i32 + 1;
        let cell_count = (cols * rows) as usize;
        self.cells = vec![-1; cell_count];
        self.width = width;
        self.height = height;
    }

    fn clear(&mut self) {
        for cell in &mut self.cells {
            *cell = -1;
        }
    }

    fn get_cell_index(&self, x: f32, y: f32) -> Option<usize> {
        if x < 0.0 || y < 0.0 {
            return None;
        }
        let col = (x / self.cell_size) as i32;
        let row = (y / self.cell_size) as i32;
        let cols = (self.width as f32 / self.cell_size).ceil() as i32 + 1;
        if col >= cols || row < 0 {
            return None;
        }
        Some((row * cols + col) as usize)
    }

    fn insert(&mut self, obj_idx: i32, x: f32, y: f32, objects: &mut [GameObject]) {
        if let Some(cell_idx) = self.get_cell_index(x, y) {
            if cell_idx < self.cells.len() {
                objects[obj_idx as usize].grid_next = self.cells[cell_idx];
                self.cells[cell_idx] = obj_idx;
            }
        }
    }
}

#[repr(C)]
#[derive(Debug)]
pub struct BlackHole {
    pub x: f32,
    pub y: f32,
    pub mass: f64,
    pub active: bool,
    pub gaze_timer: f32,
    pub schwarzschild_radius: f32,
}

impl Default for BlackHole {
    fn default() -> Self {
        BlackHole {
            x: 0.0,
            y: 0.0,
            mass: 1.0e12, // Arbitrary large mass for visual effect
            active: false,
            gaze_timer: 0.0,
            schwarzschild_radius: 20.0,
        }
    }
}

// ============================================================================
// Global State (Thread-local for safety)
// ============================================================================

thread_local! {
    static PHYSICS_INITIALIZED: AtomicBool = AtomicBool::new(false);
    
    static OBJECTS: RefCell<Vec<GameObject>> = RefCell::new(Vec::with_capacity(MAX_OBJECTS));
    static PARTICLE_POOL: RefCell<Option<ParticlePool>> = RefCell::new(None);
    static SPATIAL_GRID: RefCell<Option<SpatialGrid>> = RefCell::new(None);
    static BLACK_HOLE: RefCell<BlackHole> = RefCell::new(BlackHole::default());
    static GRAVITY: RefCell<f32> = RefCell::new(GRAVITY_DEFAULT);
}

// ============================================================================
// FFI Functions
// ============================================================================

/// Initialize physics system with given dimensions
#[no_mangle]
pub extern "C" fn init_physics(width: i32, height: i32) {
    OBJECTS.with(|objs| {
        objs.borrow_mut().clear();
        objs.borrow_mut().reserve(MAX_OBJECTS);
    });
    
    PARTICLE_POOL.with(|pool| {
        *pool.borrow_mut() = Some(ParticlePool::new());
    });
    
    SPATIAL_GRID.with(|grid| {
        *grid.borrow_mut() = Some(SpatialGrid::new(width, height));
    });
    
    BLACK_HOLE.with(|bh| {
        *bh.borrow_mut() = BlackHole::default();
    });
    
    PHYSICS_INITIALIZED.with(|init| init.store(true, Ordering::SeqCst));
}

/// Update physics simulation by dt seconds
#[no_mangle]
pub extern "C" fn update_physics(dt: f32) {
    if !PHYSICS_INITIALIZED.with(|init| init.load(Ordering::SeqCst)) {
        return;
    }

    let gravity = *GRAVITY.borrow();
    
    // Update objects
    {
        let mut objects = OBJECTS.borrow_mut();
        let mut grid = SPATIAL_GRID.borrow_mut();
        let bh = BLACK_HOLE.borrow();
        
        if let Some(ref mut grid) = *grid {
            // Build spatial grid
            grid.clear();
            for (idx, obj) in objects.iter_mut().enumerate() {
                if obj.active {
                    grid.insert(idx as i32, obj.x, obj.y, &mut objects);
                }
            }
            
            // Apply gravity and integrate
            for obj in objects.iter_mut() {
                if !obj.active {
                    continue;
                }
                
                // Apply global gravity
                obj.vy += gravity * dt;
                
                // Apply black hole gravity if active
                if bh.active {
                    let dx = bh.x - obj.x;
                    let dy = bh.y - obj.y;
                    let dist_sq = dx * dx + dy * dy;
                    if dist_sq > 100.0 { // Guard against singularity (dist > 10)
                        let dist = dist_sq.sqrt();
                        let force = (6.674e-11f32 * bh.mass as f32 * obj.mass) / dist_sq;
                        let force = force * 100000.0; // Scale for visual sanity
                        let fx = force * (dx / dist);
                        let fy = force * (dy / dist);
                        obj.vx += fx * dt;
                        obj.vy += fy * dt;
                    }
                }
                
                // Euler integration
                obj.x += obj.vx * dt;
                obj.y += obj.vy * dt;
                
                // Boundary collision
                if obj.x - obj.radius < 0.0 {
                    obj.x = obj.radius;
                    obj.vx = -obj.vx * obj.restitution;
                }
                if obj.x + obj.radius > grid.width as f32 {
                    obj.x = grid.width as f32 - obj.radius;
                    obj.vx = -obj.vx * obj.restitution;
                }
                if obj.y - obj.radius < 0.0 {
                    obj.y = obj.radius;
                    obj.vy = -obj.vy * obj.restitution;
                }
                if obj.y + obj.radius > grid.height as f32 {
                    obj.y = grid.height as f32 - obj.radius;
                    obj.vy = -obj.vy * obj.restitution;
                }
            }
            
            // Collision detection using spatial hash
            let mut checked_pairs: Vec<(usize, usize)> = Vec::new();
            for (idx, obj) in objects.iter().enumerate() {
                if !obj.active {
                    continue;
                }
                
                if let Some(center_cell) = grid.get_cell_index(obj.x, obj.y) {
                    let cols = (grid.width as f32 / grid.cell_size).ceil() as i32 + 1;
                    
                    // Check 3x3 neighborhood
                    let center_row = center_cell / cols as usize;
                    let center_col = center_cell % cols as usize;
                    
                    for row_off in -1..=1 {
                        for col_off in -1..=1 {
                            let check_row = center_row as i32 + row_off;
                            let check_col = center_col as i32 + col_off;
                            
                            if check_row < 0 || check_col < 0 {
                                continue;
                            }
                            
                            let check_cell = (check_row * cols + check_col) as usize;
                            if check_cell >= grid.cells.len() {
                                continue;
                            }
                            
                            // Traverse linked list in this cell
                            let mut j = grid.cells[check_cell];
                            while j != -1 {
                                let j_idx = j as usize;
                                if j_idx != idx && j_idx < objects.len() {
                                    let pair = (idx.min(j_idx), idx.max(j_idx));
                                    if !checked_pairs.contains(&pair) {
                                        checked_pairs.push(pair);
                                        
                                        let other = &objects[j_idx];
                                        if other.active {
                                            resolve_collision(&mut objects[idx], other);
                                        }
                                    }
                                }
                                j = objects[j_idx as usize].grid_next;
                            }
                        }
                    }
                }
            }
        }
    }
    
    // Update particles
    {
        let mut pool_opt = PARTICLE_POOL.borrow_mut();
        if let Some(ref mut pool) = *pool_opt {
            for i in 0..pool.high_water_mark as usize {
                if !pool.particles[i].active {
                    continue;
                }
                
                pool.particles[i].vy += gravity * dt;
                pool.particles[i].x += pool.particles[i].vx * dt;
                pool.particles[i].y += pool.particles[i].vy * dt;
                pool.particles[i].life += dt;
                
                if pool.particles[i].life >= pool.particles[i].max_life {
                    pool.free(i as i32);
                }
            }
        }
    }
    
    // Update black hole gaze timer
    {
        let mut bh = BLACK_HOLE.borrow_mut();
        if bh.active {
            bh.gaze_timer = (bh.gaze_timer + dt).min(BLACK_HOLE_GAZE_MAX);
        } else {
            bh.gaze_timer = (bh.gaze_timer - dt * 2.0).max(0.0);
        }
    }
}

fn resolve_collision(a: &mut GameObject, b: &GameObject) {
    let dx = b.x - a.x;
    let dy = b.y - a.y;
    let dist = (dx * dx + dy * dy).sqrt();
    
    let min_dist = a.radius + b.radius;
    if dist >= min_dist || dist < 0.001 {
        return;
    }
    
    // Normal vector
    let nx = dx / dist;
    let ny = dy / dist;
    
    // Relative velocity
    let dvx = a.vx - b.vx;
    let dvy = a.vy - b.vy;
    
    // Relative velocity along normal
    let dvn = dvx * nx + dvy * ny;
    
    // Don't resolve if velocities are separating
    if dvn < 0.0 {
        return;
    }
    
    // Impulse scalar
    let restitution = a.restitution.min(b.restitution);
    let impulse = -(1.0 + restitution) * dvn / (1.0 / a.mass + 1.0 / b.mass);
    
    // Apply impulse
    a.vx += impulse * nx / a.mass;
    a.vy += impulse * ny / a.mass;
    // Note: We don't modify b's velocity here since we're iterating and b might be processed later
    // In a full implementation, we'd accumulate impulses or process symmetrically
}

/// Spawn particles at given position
#[no_mangle]
pub extern "C" fn spawn_particles(x: f32, y: f32, count: i32) {
    if !PHYSICS_INITIALIZED.with(|init| init.load(Ordering::SeqCst)) {
        return;
    }
    
    PARTICLE_POOL.with(|pool_opt| {
        if let Some(ref mut pool) = *pool_opt.borrow_mut() {
            for _ in 0..count {
                if let Some(idx) = pool.alloc() {
                    pool.particles[idx as usize].x = x;
                    pool.particles[idx as usize].y = y;
                    pool.particles[idx as usize].vx = (rand_f32() - 0.5) * 200.0;
                    pool.particles[idx as usize].vy = (rand_f32() - 0.5) * 200.0;
                    pool.particles[idx as usize].life = 0.0;
                    pool.particles[idx as usize].max_life = 2.0 + rand_f32();
                    pool.particles[idx as usize].color = 0x00FFFF00; // Yellow
                    pool.particles[idx as usize].size = 2.0 + rand_f32() * 3.0;
                }
            }
        }
    });
}

fn rand_f32() -> f32 {
    use std::time::SystemTime;
    let now = SystemTime::now()
        .duration_since(SystemTime::UNIX_EPOCH)
        .unwrap_or_default()
        .subsec_nanos();
    ((now % 10000) as f32) / 10000.0
}

/// Add an object to the simulation
#[no_mangle]
pub extern "C" fn add_object(
    x: f32,
    y: f32,
    shape: i32,
    color: u32,
    size: f32,
) -> i32 {
    if !PHYSICS_INITIALIZED.with(|init| init.load(Ordering::SeqCst)) {
        return -1;
    }
    
    OBJECTS.with(|objs| {
        let mut objects = objs.borrow_mut();
        
        // Find first inactive slot or append
        let idx = objects
            .iter()
            .position(|o| !o.active)
            .unwrap_or_else(|| {
                objects.push(GameObject::default());
                objects.len() - 1
            });
        
        objects[idx].x = x;
        objects[idx].y = y;
        objects[idx].vx = 0.0;
        objects[idx].vy = 0.0;
        objects[idx].shape = shape;
        objects[idx].color = color;
        objects[idx].radius = size;
        objects[idx].width = size;
        objects[idx].height = size;
        objects[idx].mass = 1.0;
        objects[idx].restitution = 0.8;
        objects[idx].active = true;
        objects[idx].grid_next = -1;
        
        idx as i32
    })
}

/// Remove an object by index
#[no_mangle]
pub extern "C" fn remove_object(index: i32) {
    if index < 0 {
        return;
    }
    
    OBJECTS.with(|objs| {
        let mut objects = objs.borrow_mut();
        if index as usize < objects.len() {
            objects[index as usize].active = false;
        }
    });
}

/// Get object count (active only)
#[no_mangle]
pub extern "C" fn get_object_count() -> i32 {
    OBJECTS.with(|objs| {
        objs.borrow()
            .iter()
            .filter(|o| o.active)
            .count() as i32
    })
}

/// Get active particle count
#[no_mangle]
pub extern "C" fn get_particle_active_count() -> i32 {
    PARTICLE_POOL.with(|pool_opt| {
        pool_opt
            .borrow()
            .as_ref()
            .map(|p| p.active_count)
            .unwrap_or(0)
    })
}

/// Set gravity value
#[no_mangle]
pub extern "C" fn set_gravity(g: f32) {
    GRAVITY.with(|grav| *grav.borrow_mut() = g);
}

/// Get object data at index (for rendering)
#[no_mangle]
pub extern "C" fn get_object_at(index: i32, out: *mut GameObject) -> bool {
    if index < 0 || out.is_null() {
        return false;
    }
    
    OBJECTS.with(|objs| {
        let objects = objs.borrow();
        if index as usize >= objects.len() {
            return false;
        }
        unsafe {
            *out = objects[index as usize];
        }
        true
    })
}

/// Get particle data at index (for rendering)
#[no_mangle]
pub extern "C" fn get_particle_at(index: i32, out: *mut Particle) -> bool {
    if index < 0 || out.is_null() {
        return false;
    }
    
    PARTICLE_POOL.with(|pool_opt| {
        if let Some(ref pool) = *pool_opt.borrow() {
            if index >= pool.high_water_mark {
                return false;
            }
            unsafe {
                *out = pool.particles[index as usize];
            }
            return true;
        }
        false
    })
}

/// Get black hole data
#[no_mangle]
pub extern "C" fn get_black_hole(out: *mut BlackHole) {
    if out.is_null() {
        return;
    }
    
    BLACK_HOLE.with(|bh| {
        unsafe {
            *out = *bh.borrow();
        }
    });
}

/// Update black hole position and check activation
#[no_mangle]
pub extern "C" fn update_black_hole(x: f32, y: f32, mouse_x: f32, mouse_y: f32) {
    BLACK_HOLE.with(|bh| {
        let mut bh = bh.borrow_mut();
        bh.x = x;
        bh.y = y;
        
        // Check if mouse is within 100px of black hole
        let dx = mouse_x - bh.x;
        let dy = mouse_y - bh.y;
        let dist = (dx * dx + dy * dy).sqrt();
        bh.active = dist < 100.0;
    });
}

/// Get max objects constant
#[no_mangle]
pub extern "C" fn get_max_objects() -> i32 {
    MAX_OBJECTS as i32
}

/// Get max particles constant
#[no_mangle]
pub extern "C" fn get_max_particles() -> i32 {
    MAX_PARTICLES as i32
}
