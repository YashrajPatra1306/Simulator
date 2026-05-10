use std::cell::RefCell;
use std::f32::consts::PI;
use std::sync::atomic::{AtomicU32, Ordering};

// --- Constants ---
const MAX_OBJECTS: usize = 1000;
const MAX_PARTICLES: usize = 2000;
const SPATIAL_CELL_SIZE: f32 = 100.0;
const GRAVITY_CONSTANT: f32 = 500.0;
const BH_MASS: f64 = 1.0e12;
const BH_GAZE_TRIGGER_DIST: f32 = 100.0;
const BH_GAZE_MAX_TIME: f32 = 9.0;

// --- Structures ---
#[repr(C)]
#[derive(Clone, Copy)]
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
    pub shape: i32, // 0:Circle, 1:Rect, 2:Triangle, 3:Line
    pub color: u32,
    pub active: bool,
    pub grid_next: i32, // Linked list pointer for spatial hash
}

#[repr(C)]
#[derive(Clone, Copy)]
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

pub struct ParticlePool {
    pub particles: [Particle; MAX_PARTICLES],
    pub free_list: [i32; MAX_PARTICLES],
    pub free_count: i32,
    pub high_water_mark: i32, // Never decrements
    pub active_count: i32,    // For display only
}

pub struct SpatialGrid {
    pub cells: Vec<i32>,
    pub cols: i32,
    pub rows: i32,
}

pub struct BlackHole {
    pub x: f32,
    pub y: f32,
    pub mass: f64,
    pub active: bool,
    pub gaze_timer: f32,
    pub schwarzschild_radius: f32,
}

// --- Global State (Thread Local) ---
thread_local! {
    static OBJECTS: RefCell<Vec<GameObject>> = RefCell::new(Vec::with_capacity(MAX_OBJECTS));
    static PARTICLES: RefCell<ParticlePool> = RefCell::new(ParticlePool {
        particles: [Particle { x: 0.0, y: 0.0, vx: 0.0, vy: 0.0, life: 0.0, max_life: 0.0, color: 0, size: 0.0, active: false }; MAX_PARTICLES],
        free_list: [0; MAX_PARTICLES],
        free_count: 0,
        high_water_mark: 0,
        active_count: 0,
    });
    static GRID: RefCell<SpatialGrid> = RefCell::new(SpatialGrid {
        cells: Vec::new(),
        cols: 0,
        rows: 0,
    });
    static BLACK_HOLE: RefCell<BlackHole> = RefCell::new(BlackHole {
        x: 0.0, y: 0.0, mass: BH_MASS, active: false, gaze_timer: 0.0, schwarzschild_radius: 50.0,
    });
    static GRAVITY: RefCell<f32> = RefCell::new(GRAVITY_CONSTANT);
}

// Random Number Generator (XORShift32)
static RAND_STATE: AtomicU32 = AtomicU32::new(12345);

fn rand_f32() -> f32 {
    let mut state = RAND_STATE.load(Ordering::Relaxed);
    state ^= state << 13;
    state ^= state >> 17;
    state ^= state << 5;
    RAND_STATE.store(state, Ordering::Relaxed);
    (state as f32) / (u32::MAX as f32)
}

// --- FFI Exports ---

#[no_mangle]
pub extern "C" fn init_physics(width: i32, height: i32) {
    OBJECTS.with(|objs| objs.borrow_mut().clear());
    
    PARTICLES.with(|p| {
        let mut pool = p.borrow_mut();
        for i in 0..MAX_PARTICLES {
            pool.particles[i].active = false;
            pool.free_list[i] = i as i32;
        }
        pool.free_count = MAX_PARTICLES as i32;
        pool.high_water_mark = 0;
        pool.active_count = 0;
    });

    resize_grid(width, height);
    
    BLACK_HOLE.with(|bh| {
        let mut b = bh.borrow_mut();
        b.x = width as f32 / 2.0;
        b.y = height as f32 / 2.0;
        b.gaze_timer = 0.0;
        b.active = false;
    });
}

fn resize_grid(width: i32, height: i32) {
    let cols = (width as f32 / SPATIAL_CELL_SIZE).ceil() as i32 + 1;
    let rows = (height as f32 / SPATIAL_CELL_SIZE).ceil() as i32 + 1;
    GRID.with(|grid| {
        let mut g = grid.borrow_mut();
        g.cols = cols;
        g.rows = rows;
        g.cells = vec![-1; (cols * rows) as usize];
    });
}

#[no_mangle]
pub extern "C" fn add_object(x: f32, y: f32, shape: i32, color: u32, size: f32) -> i32 {
    OBJECTS.with(|objs| {
        let mut objects = objs.borrow_mut();
        if objects.len() >= MAX_OBJECTS { return -1; }
        
        let obj = GameObject {
            x, y, vx: 0.0, vy: 0.0,
            radius: size, width: size, height: size,
            mass: 1.0, restitution: 0.8,
            shape, color, active: true, grid_next: -1,
        };
        objects.push(obj);
        (objects.len() - 1) as i32
    })
}

#[no_mangle]
pub extern "C" fn remove_object(index: i32) {
    OBJECTS.with(|objs| {
        let mut objects = objs.borrow_mut();
        let idx = index as usize;
        if idx < objects.len() {
            objects[idx].active = false;
        }
    });
}

#[no_mangle]
pub extern "C" fn spawn_particles(x: f32, y: f32, count: i32) {
    PARTICLES.with(|p| {
        let mut pool = p.borrow_mut();
        for _ in 0..count {
            if pool.free_count <= 0 { break; }
            
            let idx = pool.free_count - 1;
            let free_idx = pool.free_list[idx as usize] as usize;
            pool.free_count -= 1;
            
            if free_idx >= pool.high_water_mark as usize {
                pool.high_water_mark = (free_idx + 1) as i32;
            }

            let angle = rand_f32() * 2.0 * PI;
            let speed = rand_f32() * 100.0 + 50.0;
            
            pool.particles[free_idx] = Particle {
                x, y,
                vx: angle.cos() * speed,
                vy: angle.sin() * speed,
                life: 1.0,
                max_life: 1.0,
                color: 0xFFFF00,
                size: 3.0,
                active: true,
            };
            pool.active_count += 1;
        }
    });
}

fn build_spatial_grid(width: i32, height: i32) {
    // PASS 1: Calculate cell indices and build the grid structure (Read-only borrow of objects)
    let insertions: Vec<(usize, usize, i32)> = OBJECTS.with(|objs| {
        let objects = objs.borrow();
        GRID.with(|grid| {
            let mut g = grid.borrow_mut();
            // Reset grid
            g.cells.fill(-1);
            
            let mut result = Vec::new();
            for (idx, obj) in objects.iter().enumerate() {
                if !obj.active { continue; }
                
                let col = (obj.x / SPATIAL_CELL_SIZE).clamp(0.0, (g.cols - 1) as f32) as i32;
                let row = (obj.y / SPATIAL_CELL_SIZE).clamp(0.0, (g.rows - 1) as f32) as i32;
                let cell_idx = (row * g.cols + col) as usize;
                
                // Store the current head of this cell to become the 'next' pointer
                let next = g.cells[cell_idx];
                g.cells[cell_idx] = idx as i32;
                
                result.push((idx, cell_idx, next));
            }
            result
        })
    });

    // PASS 2: Write grid_next into objects (Mutable borrow of objects)
    OBJECTS.with(|objs| {
        let mut objects = objs.borrow_mut();
        for (idx, _cell_idx, next) in insertions {
            if idx < objects.len() {
                objects[idx].grid_next = next;
            }
        }
    });
}

fn resolve_collision(a: &mut GameObject, b: &GameObject) {
    // Simple elastic impulse (One-sided for stability in this prototype)
    let dx = a.x - b.x;
    let dy = a.y - b.y;
    let dist = (dx * dx + dy * dy).sqrt();
    
    if dist < a.radius + b.radius && dist > 0.001 {
        let nx = dx / dist;
        let ny = dy / dist;
        
        let rel_vx = a.vx - b.vx;
        let rel_vy = a.vy - b.vy;
        let vel_along_normal = rel_vx * nx + rel_vy * ny;
        
        if vel_along_normal < 0.0 {
            let e = a.restitution.min(b.restitution);
            let j = -(1.0 + e) * vel_along_normal;
            let inv_mass_a = 1.0 / a.mass;
            let inv_mass_b = 1.0 / b.mass;
            
            let impulse = j / (inv_mass_a + inv_mass_b);
            
            a.vx += impulse * inv_mass_a * nx;
            a.vy += impulse * inv_mass_a * ny;
            // Note: We do not modify b's velocity here to avoid double-modification in the loop
        }
        
        // Positional correction
        let overlap = (a.radius + b.radius - dist) / 2.0;
        a.x += nx * overlap;
        a.y += ny * overlap;
    }
}

fn update_black_hole(dt: f32, mouse_x: f32, mouse_y: f32) {
    BLACK_HOLE.with(|bh| {
        let mut b = bh.borrow_mut();
        
        let dx = mouse_x - b.x;
        let dy = mouse_y - b.y;
        let dist = (dx * dx + dy * dy).sqrt();
        
        if dist < BH_GAZE_TRIGGER_DIST {
            b.active = true;
            b.gaze_timer = (b.gaze_timer + dt).min(BH_GAZE_MAX_TIME);
        } else {
            b.active = false;
            b.gaze_timer = (b.gaze_timer - dt * 2.0).max(0.0);
        }
    });
}

fn apply_black_hole_gravity() {
    BLACK_HOLE.with(|bh| {
        let b = bh.borrow();
        if !b.active || b.gaze_timer < 0.1 { return; }
        
        let bh_x = b.x;
        let bh_y = b.y;
        let bh_mass = b.mass;
        
        OBJECTS.with(|objs| {
            let mut objects = objs.borrow_mut();
            for obj in objects.iter_mut() {
                if !obj.active { continue; }
                
                let dx = bh_x - obj.x;
                let dy = bh_y - obj.y;
                let dist_sq = dx * dx + dy * dy;
                
                if dist_sq > 100.0 { // Avoid singularity
                    let dist = dist_sq.sqrt();
                    let force = (6.674e-11 * bh_mass * obj.mass as f64) / dist_sq as f64;
                    let force = (force * 100000.0) as f32; // Scale for visuals
                    
                    let fx = (dx / dist) * force;
                    let fy = (dy / dist) * force;
                    
                    obj.vx += fx * obj.mass; // Simplified integration
                    obj.vy += fy * obj.mass;
                }
            }
        });
    });
}

#[no_mangle]
pub extern "C" fn update_physics(dt: f32, mouse_x: f32, mouse_y: f32, width: i32, height: i32) {
    let gravity = GRAVITY.with(|g| *g.borrow());
    
    update_black_hole(dt, mouse_x, mouse_y);
    apply_black_hole_gravity();
    
    build_spatial_grid(width, height);
    
    // Update Objects
    OBJECTS.with(|objs| {
        let mut objects = objs.borrow_mut();
        let cols = GRID.with(|g| g.borrow().cols);
        let rows = GRID.with(|g| g.borrow().rows);
        
        for i in 0..objects.len() {
            if !objects[i].active { continue; }
            
            let obj = &mut objects[i];
            
            // Gravity
            obj.vy += gravity * dt;
            
            // Integration
            obj.x += obj.vx * dt;
            obj.y += obj.vy * dt;
            
            // Boundary Bounce
            if obj.x < obj.radius { obj.x = obj.radius; obj.vx *= -obj.restitution; }
            if obj.x > width as f32 - obj.radius { obj.x = width as f32 - obj.radius; obj.vx *= -obj.restitution; }
            if obj.y < obj.radius { obj.y = obj.radius; obj.vy *= -obj.restitution; }
            if obj.y > height as f32 - obj.radius { obj.y = height as f32 - obj.radius; obj.vy *= -obj.restitution; }
            
            // Collision Detection via Spatial Grid
            let col = (obj.x / SPATIAL_CELL_SIZE).clamp(0.0, (cols - 1) as f32) as i32;
            let row = (obj.y / SPATIAL_CELL_SIZE).clamp(0.0, (rows - 1) as f32) as i32;
            
            GRID.with(|grid| {
                let g = grid.borrow();
                for r in (row-1).max(0)..=(row+1).min(rows-1) {
                    for c in (col-1).max(0)..=(col+1).min(cols-1) {
                        let cell_idx = (r * g.cols + c) as usize;
                        let mut j = g.cells[cell_idx];
                        
                        while j != -1 {
                            let j_idx = j as usize;
                            if j_idx != i && j_idx < objects.len() {
                                // Safe copy pattern to avoid double mutable borrow
                                let b_copy = objects[j_idx]; 
                                resolve_collision(&mut objects[i], &b_copy);
                                
                                // Traverse using the object's field directly, not the grid cell head
                                j = objects[j_idx].grid_next;
                            } else {
                                break;
                            }
                        }
                    }
                }
            });
        }
    });
    
    // Update Particles
    PARTICLES.with(|p| {
        let mut pool = p.borrow_mut();
        for i in 0..pool.high_water_mark as usize {
            if !pool.particles[i].active { continue; }
            
            let part = &mut pool.particles[i];
            part.x += part.vx * dt;
            part.y += part.vy * dt;
            part.life -= dt;
            
            if part.life <= 0.0 {
                part.active = false;
                pool.free_list[pool.free_count as usize] = i as i32;
                pool.free_count += 1;
                pool.active_count -= 1;
            }
        }
    });
}

#[no_mangle]
pub extern "C" fn get_object_count() -> i32 {
    OBJECTS.with(|objs| objs.borrow().iter().filter(|o| o.active).count() as i32)
}

#[no_mangle]
pub extern "C" fn get_particle_active_count() -> i32 {
    PARTICLES.with(|p| p.borrow().active_count)
}

#[no_mangle]
pub extern "C" fn get_max_objects() -> i32 {
    MAX_OBJECTS as i32
}

#[no_mangle]
pub extern "C" fn set_gravity(g: f32) {
    GRAVITY.with(|grav| *grav.borrow_mut() = g);
}

// Fixed: C-ABI compatible out pointers instead of tuple
#[no_mangle]
pub extern "C" fn get_black_hole_position(out_x: *mut f32, out_y: *mut f32) {
    BLACK_HOLE.with(|bh| {
        let b = bh.borrow();
        unsafe {
            if !out_x.is_null() { *out_x = b.x; }
            if !out_y.is_null() { *out_y = b.y; }
        }
    });
}

#[no_mangle]
pub extern "C" fn get_black_hole_gaze() -> f32 {
    BLACK_HOLE.with(|bh| bh.borrow().gaze_timer)
}

#[no_mangle]
pub extern "C" fn get_black_hole_active() -> bool {
    BLACK_HOLE.with(|bh| bh.borrow().active)
}
