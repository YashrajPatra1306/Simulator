//! Rust Physics Core for Sandbox Rendering Engine
//! Pure std library implementation with FFI exports for C# P/Invoke

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
    pub shape: i32,
    pub color: u32,
    pub active: bool,
    pub grid_next: i32,
}

impl Default for GameObject {
    fn default() -> Self {
        GameObject {
            x: 0.0, y: 0.0, vx: 0.0, vy: 0.0,
            radius: 10.0, width: 20.0, height: 20.0,
            mass: 1.0, restitution: 0.8,
            shape: 0, color: 0xFFAA5500,
            active: true, grid_next: -1,
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
            x: 0.0, y: 0.0, vx: 0.0, vy: 0.0,
            life: 0.0, max_life: 1.0,
            color: 0xFFFFFF00, size: 2.0,
            active: false,
        }
    }
}

struct ParticlePool {
    particles: [Particle; MAX_PARTICLES],
    free_list: [i32; MAX_PARTICLES],
    free_count: i32,
    high_water_mark: i32,
}

impl Default for ParticlePool {
    fn default() -> Self {
        let mut free_list = [0; MAX_PARTICLES];
        for i in 0..MAX_PARTICLES {
            free_list[i] = (MAX_PARTICLES - 1 - i) as i32;
        }
        ParticlePool {
            particles: [Particle::default(); MAX_PARTICLES],
            free_list,
            free_count: MAX_PARTICLES as i32,
            high_water_mark: 0,
        }
    }
}

struct SpatialGrid {
    cells: Vec<i32>,
    cols: i32,
    rows: i32,
}

impl Default for SpatialGrid {
    fn default() -> Self {
        SpatialGrid { cells: Vec::new(), cols: 0, rows: 0 }
    }
}

struct BlackHoleData {
    x: f32, y: f32, mass: f64,
    active: bool, gaze_timer: f32,
    schwarzschild_radius: f32,
}

impl Default for BlackHoleData {
    fn default() -> Self {
        BlackHoleData {
            x: 0.0, y: 0.0, mass: 1e12,
            active: false, gaze_timer: 0.0,
            schwarzschild_radius: 50.0,
        }
    }
}

// ============================================================================
// Thread-Local Global State
// ============================================================================

thread_local! {
    static OBJECTS: RefCell<Vec<GameObject>> = RefCell::new(Vec::with_capacity(MAX_OBJECTS));
    static PARTICLES: RefCell<ParticlePool> = RefCell::new(ParticlePool::default());
    static GRID: RefCell<SpatialGrid> = RefCell::new(SpatialGrid::default());
    static BLACK_HOLE: RefCell<BlackHoleData> = RefCell::new(BlackHoleData::default());
    static GRAVITY: RefCell<f32> = RefCell::new(GRAVITY_DEFAULT);
}

// ============================================================================
// Random Number Generator (XORShift32)
// ============================================================================

static mut RAND_STATE: u32 = 123456789;

fn rand_f32() -> f32 {
    unsafe {
        let mut x = RAND_STATE;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        RAND_STATE = x;
        (x as f32) / (u32::MAX as f32)
    }
}

fn rand_range(min: f32, max: f32) -> f32 {
    min + rand_f32() * (max - min)
}

// ============================================================================
// Physics Functions
// ============================================================================

fn build_spatial_grid(width: i32, height: i32) {
    GRID.with(|grid| {
        let mut g = grid.borrow_mut();
        let cols = (width as f32 / SPATIAL_CELL_SIZE).ceil() as i32 + 1;
        let rows = (height as f32 / SPATIAL_CELL_SIZE).ceil() as i32 + 1;
        
        if g.cells.len() != (cols * rows) as usize {
            g.cells = vec![-1; (cols * rows) as usize];
        } else {
            for cell in g.cells.iter_mut() {
                *cell = -1;
            }
        }
        g.cols = cols;
        g.rows = rows;

        OBJECTS.with(|objs| {
            let objects = objs.borrow();
            for (idx, obj) in objects.iter().enumerate() {
                if !obj.active { continue; }
                let col = (obj.x / SPATIAL_CELL_SIZE).clamp(0.0, (cols - 1) as f32) as i32;
                let row = (obj.y / SPATIAL_CELL_SIZE).clamp(0.0, (rows - 1) as f32) as i32;
                let cell_idx = (row * cols + col) as usize;
                
                // Store index in grid_next, NOT in mass field
                let mut obj_copy = *obj;
                obj_copy.grid_next = g.cells[cell_idx];
                g.cells[cell_idx] = idx as i32;
                
                // Update the object in the vector
                drop(objects);
                let mut objs_mut = objs.borrow_mut();
                objs_mut[idx].grid_next = obj_copy.grid_next;
            }
        });
    });
}

fn resolve_collision(a: &mut GameObject, b: &GameObject) {
    let dx = a.x - b.x;
    let dy = a.y - b.y;
    let dist_sq = dx * dx + dy * dy;
    let min_dist = a.radius + b.radius;
    
    if dist_sq > 0.0 && dist_sq < min_dist * min_dist {
        let dist = dist_sq.sqrt();
        let nx = dx / dist;
        let ny = dy / dist;
        
        let dvx = a.vx - b.vx;
        let dvy = a.vy - b.vy;
        let dvn = dvx * nx + dvy * ny;
        
        if dvn > 0.0 { return; }
        
        let restitution = (a.restitution + b.restitution) * 0.5;
        let j = -(1.0 + restitution) * dvn / (1.0 / a.mass + 1.0 / b.mass);
        
        let impulse_x = j * nx;
        let impulse_y = j * ny;
        
        a.vx += impulse_x / a.mass;
        a.vy += impulse_y / a.mass;
    }
}

fn update_physics_internal(dt: f32, width: i32, height: i32) {
    let gravity = GRAVITY.with(|g| *g.borrow());
    
    build_spatial_grid(width, height);
    
    OBJECTS.with(|objs| {
        let mut objects = objs.borrow_mut();
        
        for i in 0..objects.len() {
            let obj = &mut objects[i];
            if !obj.active { continue; }
            
            obj.vy += gravity * dt;
            obj.x += obj.vx * dt;
            obj.y += obj.vy * dt;
            
            if obj.x - obj.radius < 0.0 {
                obj.x = obj.radius;
                obj.vx = -obj.vx * obj.restitution;
            }
            if obj.x + obj.radius > width as f32 {
                obj.x = width as f32 - obj.radius;
                obj.vx = -obj.vx * obj.restitution;
            }
            if obj.y - obj.radius < 0.0 {
                obj.y = obj.radius;
                obj.vy = -obj.vy * obj.restitution;
            }
            if obj.y + obj.radius > height as f32 {
                obj.y = height as f32 - obj.radius;
                obj.vy = -obj.vy * obj.restitution;
            }
            
            let col = (obj.x / SPATIAL_CELL_SIZE).clamp(0.0, (GRID.with(|g| g.borrow().cols - 1) as f32)) as i32;
            let row = (obj.y / SPATIAL_CELL_SIZE).clamp(0.0, (GRID.with(|g| g.borrow().rows - 1) as f32)) as i32;
            
            GRID.with(|grid| {
                let g = grid.borrow();
                let cols = g.cols;
                for di in -1..=1 {
                    for dj in -1..=1 {
                        let nc = col + di;
                        let nr = row + dj;
                        if nc < 0 || nc >= cols || nr < 0 || nr >= g.rows { continue; }
                        let cell_idx = (nr * cols + nc) as usize;
                        let mut j = g.cells[cell_idx];
                        while j != -1 {
                            let j_idx = j as usize;
                            if j_idx != i && j_idx < objects.len() {
                                let b_copy = objects[j_idx];
                                drop(g);
                                resolve_collision(&mut objects[i], &b_copy);
                                let g2 = grid.borrow();
                                j = g2.cells[cell_idx];
                                continue;
                            }
                            j = if j_idx < objects.len() { objects[j_idx].grid_next } else { -1 };
                        }
                    }
                }
            });
        }
    });
    
    BLACK_HOLE.with(|bh| {
        let bh_data = bh.borrow();
        if bh_data.active {
            OBJECTS.with(|objs| {
                let mut objects = objs.borrow_mut();
                for obj in objects.iter_mut() {
                    if !obj.active { continue; }
                    let dx = bh_data.x - obj.x;
                    let dy = bh_data.y - obj.y;
                    let dist_sq = dx * dx + dy * dy;
                    if dist_sq > 100.0 {
                        let dist = dist_sq.sqrt();
                        let force = (6.674e-11 * bh_data.mass * obj.mass as f64 / dist_sq as f64) as f32 * 100000.0;
                        obj.vx += (dx / dist) * force * dt;
                        obj.vy += (dy / dist) * force * dt;
                    }
                }
            });
        }
    });
    
    PARTICLES.with(|pool| {
        let mut p = pool.borrow_mut();
        for i in 0..p.high_water_mark as usize {
            let part = &mut p.particles[i];
            if !part.active { continue; }
            part.life -= dt;
            if part.life <= 0.0 {
                part.active = false;
                if p.free_count < MAX_PARTICLES as i32 {
                    p.free_list[p.free_count as usize] = i as i32;
                    p.free_count += 1;
                }
                continue;
            }
            part.x += part.vx * dt;
            part.y += part.vy * dt;
            part.vy += gravity * dt * 0.5;
            
            if part.x < 0.0 || part.x > width as f32 || part.y < 0.0 || part.y > height as f32 {
                part.active = false;
                if p.free_count < MAX_PARTICLES as i32 {
                    p.free_list[p.free_count as usize] = i as i32;
                    p.free_count += 1;
                }
            }
        }
    });
}

// ============================================================================
// FFI Exports
// ============================================================================

#[no_mangle]
pub extern "C" fn init_physics(width: i32, height: i32) {
    OBJECTS.with(|objs| objs.borrow_mut().clear());
    PARTICLES.with(|pool| {
        let mut p = pool.borrow_mut();
        p.high_water_mark = 0;
        p.free_count = MAX_PARTICLES as i32;
        for i in 0..MAX_PARTICLES {
            p.free_list[i] = (MAX_PARTICLES - 1 - i) as i32;
            p.particles[i] = Particle::default();
        }
    });
    GRID.with(|grid| {
        let mut g = grid.borrow_mut();
        let cols = (width as f32 / SPATIAL_CELL_SIZE).ceil() as i32 + 1;
        let rows = (height as f32 / SPATIAL_CELL_SIZE).ceil() as i32 + 1;
        g.cells = vec![-1; (cols * rows) as usize];
        g.cols = cols;
        g.rows = rows;
    });
    BLACK_HOLE.with(|bh| *bh.borrow_mut() = BlackHoleData::default());
}

#[no_mangle]
pub extern "C" fn update_physics(dt: f32) {
    GRID.with(|g| {
        let width = g.borrow().cols as f32 * SPATIAL_CELL_SIZE;
        let height = g.borrow().rows as f32 * SPATIAL_CELL_SIZE;
        update_physics_internal(dt, width as i32, height as i32);
    });
}

#[no_mangle]
pub extern "C" fn spawn_particles(x: f32, y: f32, count: i32) {
    PARTICLES.with(|pool| {
        let mut p = pool.borrow_mut();
        for _ in 0..count {
            if p.free_count <= 0 { break; }
            p.free_count -= 1;
            let idx = p.free_list[p.free_count as usize] as usize;
            p.particles[idx] = Particle {
                x, y,
                vx: rand_range(-100.0, 100.0),
                vy: rand_range(-100.0, 100.0),
                life: rand_range(0.5, 1.5),
                max_life: 1.5,
                color: 0xFFFFFF00,
                size: rand_range(1.0, 3.0),
                active: true,
            };
            if idx as i32 >= p.high_water_mark {
                p.high_water_mark = idx as i32 + 1;
            }
        }
    });
}

#[no_mangle]
pub extern "C" fn add_object(x: f32, y: f32, shape: i32, color: u32, size: f32) -> i32 {
    OBJECTS.with(|objs| {
        let mut objects = objs.borrow_mut();
        if objects.len() >= MAX_OBJECTS { return -1; }
        let idx = objects.len();
        objects.push(GameObject {
            x, y, vx: 0.0, vy: 0.0,
            radius: size, width: size * 2.0, height: size * 2.0,
            mass: 1.0, restitution: 0.8,
            shape, color, active: true, grid_next: -1,
        });
        idx as i32
    })
}

#[no_mangle]
pub extern "C" fn remove_object(index: i32) {
    OBJECTS.with(|objs| {
        let mut objects = objs.borrow_mut();
        if index >= 0 && (index as usize) < objects.len() {
            objects[index as usize].active = false;
        }
    });
}

#[no_mangle]
pub extern "C" fn get_object_count() -> i32 {
    OBJECTS.with(|objs| {
        objs.borrow().iter().filter(|o| o.active).count() as i32
    })
}

#[no_mangle]
pub extern "C" fn get_particle_active_count() -> i32 {
    PARTICLES.with(|pool| {
        let p = pool.borrow();
        p.particles.iter().filter(|p| p.active).count() as i32
    })
}

#[no_mangle]
pub extern "C" fn set_gravity(g: f32) {
    GRAVITY.with(|gravity| *gravity.borrow_mut() = g);
}

#[no_mangle]
pub extern "C" fn update_black_hole(x: f32, y: f32, mouse_x: f32, mouse_y: f32, dt: f32) {
    BLACK_HOLE.with(|bh| {
        let mut b = bh.borrow_mut();
        b.x = x;
        b.y = y;
        
        let dx = mouse_x - x;
        let dy = mouse_y - y;
        let dist = (dx * dx + dy * dy).sqrt();
        
        b.active = dist < 100.0;
        
        if b.active {
            b.gaze_timer = (b.gaze_timer + dt).min(BLACK_HOLE_GAZE_MAX);
        } else {
            b.gaze_timer = (b.gaze_timer - dt * 2.0).max(0.0);
        }
    });
}

#[no_mangle]
pub extern "C" fn get_black_hole_gaze_timer() -> f32 {
    BLACK_HOLE.with(|bh| bh.borrow().gaze_timer)
}

#[no_mangle]
pub extern "C" fn get_black_hole_position() -> (f32, f32) {
    BLACK_HOLE.with(|bh| {
        let b = bh.borrow();
        (b.x, b.y)
    })
}

#[no_mangle]
pub extern "C" fn get_max_objects() -> i32 {
    MAX_OBJECTS as i32
}

#[no_mangle]
pub extern "C" fn get_object_at(index: i32, out: *mut GameObject) -> bool {
    if index < 0 || (index as usize) >= MAX_OBJECTS { return false; }
    OBJECTS.with(|objs| {
        let objects = objs.borrow();
        if index as usize >= objects.len() { return false; }
        unsafe {
            *out = objects[index as usize];
        }
        true
    })
}

#[no_mangle]
pub extern "C" fn get_particle_at(index: i32, out: *mut Particle) -> bool {
    if index < 0 || (index as usize) >= MAX_PARTICLES { return false; }
    PARTICLES.with(|pool| {
        let p = pool.borrow();
        if index as usize >= p.particles.len() || !p.particles[index as usize].active { return false; }
        unsafe {
            *out = p.particles[index as usize];
        }
        true
    })
}
