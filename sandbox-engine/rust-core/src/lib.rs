use std::sync::{RwLock, OnceLock};
use std::f32::consts::PI;
use std::sync::atomic::{AtomicU32, Ordering};

// --- Constants ---
const MAX_OBJECTS: usize = 1000;
const MAX_PARTICLES: usize = 4000; // Doubled for arena bursts
const SPATIAL_CELL_SIZE: f32 = 100.0;
const GRAVITY_CONSTANT: f32 = 500.0;
const BH_MASS: f64 = 1.0e12;
const BH_GAZE_TRIGGER_DIST: f32 = 100.0;
const BH_GAZE_MAX_TIME: f32 = 9.0;
const SLEEP_VELOCITY_THRESHOLD: f32 = 0.5;
const SLEEP_FRAME_THRESHOLD: u32 = 60;
const SOLVER_ITERATIONS: usize = 8; // Constraint solver iterations
const BVH_NULL: i32 = -1;

// --- SoA Layout for GameObjects ---
// Separated into hot (physics) and cold (render) data
pub struct ObjectSoA {
    // Hot: touched every physics frame
    pub x:            [f32; MAX_OBJECTS],
    pub y:            [f32; MAX_OBJECTS],
    pub prev_x:       [f32; MAX_OBJECTS], // For temporal interpolation
    pub prev_y:       [f32; MAX_OBJECTS],
    pub vx:           [f32; MAX_OBJECTS],
    pub vy:           [f32; MAX_OBJECTS],
    pub ax:           [f32; MAX_OBJECTS], // Previous acceleration for Verlet
    pub ay:           [f32; MAX_OBJECTS],
    pub mass:         [f32; MAX_OBJECTS],
    pub restitution:  [f32; MAX_OBJECTS],
    pub radius:       [f32; MAX_OBJECTS],
    pub sleep_timer:  [u32; MAX_OBJECTS],
    pub sleeping:     [bool; MAX_OBJECTS],
    pub active:       [bool; MAX_OBJECTS],
    // Cold: touched only for rendering
    pub width:        [f32; MAX_OBJECTS],
    pub height:       [f32; MAX_OBJECTS],
    pub shape:        [i32; MAX_OBJECTS],
    pub color:        [u32; MAX_OBJECTS],
    pub count: usize, // Slot high-water mark
}

impl ObjectSoA {
    fn new() -> Self {
        ObjectSoA {
            x:           [0.0; MAX_OBJECTS],
            y:           [0.0; MAX_OBJECTS],
            prev_x:      [0.0; MAX_OBJECTS],
            prev_y:      [0.0; MAX_OBJECTS],
            vx:          [0.0; MAX_OBJECTS],
            vy:          [0.0; MAX_OBJECTS],
            ax:          [0.0; MAX_OBJECTS],
            ay:          [0.0; MAX_OBJECTS],
            mass:        [1.0; MAX_OBJECTS],
            restitution: [0.8; MAX_OBJECTS],
            radius:      [0.0; MAX_OBJECTS],
            sleep_timer: [0u32; MAX_OBJECTS],
            sleeping:    [false; MAX_OBJECTS],
            active:      [false; MAX_OBJECTS],
            width:       [0.0; MAX_OBJECTS],
            height:      [0.0; MAX_OBJECTS],
            shape:       [0i32; MAX_OBJECTS],
            color:       [0u32; MAX_OBJECTS],
            count: 0,
        }
    }
}

// --- Particle Memory Arena ---
// Two particle types stored in the same arena
#[repr(C)]
#[derive(Clone, Copy)]
pub struct Particle {
    pub x:        f32,
    pub y:        f32,
    pub vx:       f32,
    pub vy:       f32,
    pub life:     f32,
    pub max_life: f32,
    pub color:    u32,
    pub size:     f32,
    pub active:   bool,
}

pub struct ParticleArena {
    pub particles:       [Particle; MAX_PARTICLES],
    pub free_list:       [i32; MAX_PARTICLES],
    pub free_count:      i32,
    pub high_water_mark: i32,
    pub active_count:    i32,
}

// --- BVH Node ---
#[derive(Clone, Copy)]
pub struct BvhNode {
    pub min_x:  f32,
    pub min_y:  f32,
    pub max_x:  f32,
    pub max_y:  f32,
    pub left:   i32,  // BVH_NULL = leaf
    pub right:  i32,
    pub obj_id: i32,  // valid when leaf
    pub parent: i32,
}

impl BvhNode {
    fn is_leaf(&self) -> bool { self.left == BVH_NULL }
    fn aabb_area(&self) -> f32 {
        (self.max_x - self.min_x) * (self.max_y - self.min_y)
    }
}

pub struct Bvh {
    pub nodes: Vec<BvhNode>,
    pub root:  i32,
    pub free:  Vec<i32>,
}

impl Bvh {
    fn new() -> Self { Bvh { nodes: Vec::with_capacity(MAX_OBJECTS * 2), root: BVH_NULL, free: Vec::new() } }

    fn alloc_node(&mut self) -> i32 {
        if let Some(idx) = self.free.pop() { return idx; }
        let idx = self.nodes.len() as i32;
        self.nodes.push(BvhNode { min_x: 0.0, min_y: 0.0, max_x: 0.0, max_y: 0.0, left: BVH_NULL, right: BVH_NULL, obj_id: -1, parent: BVH_NULL });
        idx
    }

    fn insert(&mut self, obj_id: i32, x: f32, y: f32, r: f32) {
        let leaf = self.alloc_node();
        let margin = r * 1.2; // Slight margin so small moves don't require reinsert
        self.nodes[leaf as usize] = BvhNode {
            min_x: x - margin, min_y: y - margin,
            max_x: x + margin, max_y: y + margin,
            left: BVH_NULL, right: BVH_NULL,
            obj_id, parent: BVH_NULL,
        };

        if self.root == BVH_NULL { self.root = leaf; return; }

        // Find best sibling using surface area heuristic
        let best = self.find_best_sibling(leaf);
        let old_parent = self.nodes[best as usize].parent;
        let new_parent = self.alloc_node();

        let combined = self.merge_aabb(leaf, best);
        self.nodes[new_parent as usize] = BvhNode {
            min_x: combined.0, min_y: combined.1,
            max_x: combined.2, max_y: combined.3,
            left: best, right: leaf,
            obj_id: -1, parent: old_parent,
        };
        self.nodes[leaf as usize].parent = new_parent;
        self.nodes[best as usize].parent = new_parent;

        if old_parent == BVH_NULL {
            self.root = new_parent;
        } else {
            let op = old_parent as usize;
            if self.nodes[op].left == best { self.nodes[op].left = new_parent; }
            else { self.nodes[op].right = new_parent; }
        }
        self.refit(new_parent);
    }

    fn find_best_sibling(&self, leaf: i32) -> i32 {
        let mut best = self.root;
        let leaf_area = self.nodes[leaf as usize].aabb_area();
        let mut node = self.root;
        while !self.nodes[node as usize].is_leaf() {
            let n = &self.nodes[node as usize];
            let combined = self.combined_area(node, leaf);
            let cost = combined;
            let left_cost = if self.nodes[n.left as usize].is_leaf() {
                self.combined_area(n.left, leaf)
            } else {
                self.combined_area(n.left, leaf) - self.nodes[n.left as usize].aabb_area()
            };
            let right_cost = if self.nodes[n.right as usize].is_leaf() {
                self.combined_area(n.right, leaf)
            } else {
                self.combined_area(n.right, leaf) - self.nodes[n.right as usize].aabb_area()
            };
            if cost < left_cost && cost < right_cost { best = node; break; }
            best = node;
            node = if left_cost < right_cost { n.left } else { n.right };
        }
        best
    }

    fn combined_area(&self, a: i32, b: i32) -> f32 {
        let na = &self.nodes[a as usize];
        let nb = &self.nodes[b as usize];
        let w = na.max_x.max(nb.max_x) - na.min_x.min(nb.min_x);
        let h = na.max_y.max(nb.max_y) - na.min_y.min(nb.min_y);
        w * h
    }

    fn merge_aabb(&self, a: i32, b: i32) -> (f32, f32, f32, f32) {
        let na = &self.nodes[a as usize];
        let nb = &self.nodes[b as usize];
        (na.min_x.min(nb.min_x), na.min_y.min(nb.min_y),
         na.max_x.max(nb.max_x), na.max_y.max(nb.max_y))
    }

    fn refit(&mut self, mut node: i32) {
        while node != BVH_NULL {
            let n = &self.nodes[node as usize];
            let (l, r, p) = (n.left, n.right, n.parent);
            if l != BVH_NULL && r != BVH_NULL {
                let merged = self.merge_aabb(l, r);
                let n = &mut self.nodes[node as usize];
                n.min_x = merged.0; n.min_y = merged.1;
                n.max_x = merged.2; n.max_y = merged.3;
            }
            node = p;
        }
    }

    fn query(&self, qx: f32, qy: f32, qr: f32, results: &mut Vec<i32>) {
        if self.root == BVH_NULL { return; }
        let mut stack = [0i32; 64];
        let mut top = 0usize;
        stack[top] = self.root; top += 1;
        while top > 0 {
            top -= 1;
            let node = stack[top];
            let n = &self.nodes[node as usize];
            if qx + qr < n.min_x || qx - qr > n.max_x || qy + qr < n.min_y || qy - qr > n.max_y { continue; }
            if n.is_leaf() { results.push(n.obj_id); continue; }
            if top + 2 < 64 {
                stack[top] = n.left;  top += 1;
                stack[top] = n.right; top += 1;
            }
        }
    }

    fn clear(&mut self) { self.nodes.clear(); self.root = BVH_NULL; self.free.clear(); }
}

// --- Spatial Grid (kept for particles) ---
pub struct SpatialGrid {
    pub cells: Vec<i32>,
    pub cols:  i32,
    pub rows:  i32,
}

// --- Black Hole ---
pub struct BlackHole {
    pub x:                  f32,
    pub y:                  f32,
    pub mass:               f64,
    pub active:             bool,
    pub gaze_timer:         f32,
    pub schwarzschild_radius: f32,
}

// --- Render Instance (packed, GPU-friendly, 20 bytes) ---
#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct RenderInstance {
    pub x:     f32,
    pub y:     f32,
    pub radius: f32,
    pub color: u32,
    pub shape: i32,
}

// --- Full simulation state behind RwLock ---
pub struct SimState {
    pub objects:   ObjectSoA,
    pub particles: ParticleArena,
    pub grid:      SpatialGrid,
    pub bvh:       Bvh,
    pub black_hole: BlackHole,
    pub gravity:   f32,
    pub width:     i32,
    pub height:    i32,
    // Render double-buffer: physics writes to back, render reads from front
    pub render_front: Vec<RenderInstance>,
    pub render_back:  Vec<RenderInstance>,
    pub render_count: usize,
    pub interp_alpha: f32,
}

impl SimState {
    fn new() -> Self {
        SimState {
            objects: ObjectSoA::new(),
            particles: ParticleArena {
                particles: [Particle { x:0.0,y:0.0,vx:0.0,vy:0.0,life:0.0,max_life:0.0,color:0,size:0.0,active:false }; MAX_PARTICLES],
                free_list: [0i32; MAX_PARTICLES],
                free_count: 0,
                high_water_mark: 0,
                active_count: 0,
            },
            grid: SpatialGrid { cells: Vec::new(), cols: 0, rows: 0 },
            bvh: Bvh::new(),
            black_hole: BlackHole { x:0.0,y:0.0,mass:BH_MASS,active:false,gaze_timer:0.0,schwarzschild_radius:50.0 },
            gravity: GRAVITY_CONSTANT,
            width: 0,
            height: 0,
            render_front: vec![RenderInstance::default(); MAX_OBJECTS],
            render_back:  vec![RenderInstance::default(); MAX_OBJECTS],
            render_count: 0,
            interp_alpha: 1.0,
        }
    }
}

static SIM: OnceLock<RwLock<SimState>> = OnceLock::new();

fn sim() -> &'static RwLock<SimState> {
    SIM.get_or_init(|| RwLock::new(SimState::new()))
}

static RAND_STATE: AtomicU32 = AtomicU32::new(12345);

fn rand_f32() -> f32 {
    let mut s = RAND_STATE.load(Ordering::Relaxed);
    s ^= s << 13; s ^= s >> 17; s ^= s << 5;
    RAND_STATE.store(s, Ordering::Relaxed);
    (s as f32) / (u32::MAX as f32)
}

// --- Init ---
#[no_mangle]
pub extern "C" fn init_physics(width: i32, height: i32) {
    let mut state = sim().write().unwrap();
    state.objects = ObjectSoA::new();
    state.bvh.clear();

    let p = &mut state.particles;
    for i in 0..MAX_PARTICLES { p.particles[i].active = false; p.free_list[i] = i as i32; }
    p.free_count = MAX_PARTICLES as i32;
    p.high_water_mark = 0;
    p.active_count = 0;

    state.width = width;
    state.height = height;
    resize_grid_internal(&mut state, width, height);

    state.black_hole.x = width as f32 / 2.0;
    state.black_hole.y = height as f32 / 2.0;
    state.black_hole.gaze_timer = 0.0;
    state.black_hole.active = false;
}

fn resize_grid_internal(state: &mut SimState, width: i32, height: i32) {
    let cols = (width as f32 / SPATIAL_CELL_SIZE).ceil() as i32 + 1;
    let rows = (height as f32 / SPATIAL_CELL_SIZE).ceil() as i32 + 1;
    state.grid.cols = cols;
    state.grid.rows = rows;
    state.grid.cells = vec![-1; (cols * rows) as usize];
}

#[no_mangle]
pub extern "C" fn resize_physics(width: i32, height: i32) {
    let mut state = sim().write().unwrap();
    state.width = width;
    state.height = height;
    resize_grid_internal(&mut state, width, height);
}

// --- Object management ---
#[no_mangle]
pub extern "C" fn add_object(x: f32, y: f32, shape: i32, color: u32, size: f32) -> i32 {
    let mut state = sim().write().unwrap();
    let objs = &mut state.objects;
    if objs.count >= MAX_OBJECTS { return -1; }
    let idx = objs.count;
    objs.x[idx] = x; objs.y[idx] = y;
    objs.prev_x[idx] = x; objs.prev_y[idx] = y;
    objs.vx[idx] = 0.0; objs.vy[idx] = 0.0;
    objs.ax[idx] = 0.0; objs.ay[idx] = 0.0;
    objs.mass[idx] = 1.0; objs.restitution[idx] = 0.8;
    objs.radius[idx] = size; objs.width[idx] = size; objs.height[idx] = size;
    objs.shape[idx] = shape; objs.color[idx] = color;
    objs.active[idx] = true; objs.sleeping[idx] = false; objs.sleep_timer[idx] = 0;
    objs.count = idx + 1;
    state.bvh.insert(idx as i32, x, y, size);
    idx as i32
}

#[no_mangle]
pub extern "C" fn remove_object(index: i32) {
    let mut state = sim().write().unwrap();
    let idx = index as usize;
    if idx < state.objects.count { state.objects.active[idx] = false; }
}

#[no_mangle]
pub extern "C" fn set_object_position(index: i32, x: f32, y: f32) {
    let mut state = sim().write().unwrap();
    let idx = index as usize;
    if idx < state.objects.count && state.objects.active[idx] {
        state.objects.prev_x[idx] = state.objects.x[idx];
        state.objects.prev_y[idx] = state.objects.y[idx];
        state.objects.x[idx] = x;
        state.objects.y[idx] = y;
        state.objects.vx[idx] = 0.0;
        state.objects.vy[idx] = 0.0;
        state.objects.sleeping[idx] = false;
        state.objects.sleep_timer[idx] = 0;
    }
}

// --- Particles ---
#[no_mangle]
pub extern "C" fn spawn_particles(x: f32, y: f32, count: i32) {
    let mut state = sim().write().unwrap();
    let pool = &mut state.particles;
    for _ in 0..count {
        if pool.free_count <= 0 { break; }
        let idx = pool.free_count - 1;
        let free_idx = pool.free_list[idx as usize] as usize;
        pool.free_count -= 1;
        if free_idx >= pool.high_water_mark as usize { pool.high_water_mark = (free_idx + 1) as i32; }
        let angle = rand_f32() * 2.0 * PI;
        let speed = rand_f32() * 100.0 + 50.0;
        pool.particles[free_idx] = Particle {
            x, y,
            vx: angle.cos() * speed,
            vy: angle.sin() * speed,
            life: 1.0, max_life: 1.0,
            color: 0xFFFF00, size: 3.0, active: true,
        };
        pool.active_count += 1;
    }
}

// --- Core physics update ---
#[no_mangle]
pub extern "C" fn update_physics(dt: f32, mouse_x: f32, mouse_y: f32, width: i32, height: i32) {
    let dt = dt.min(0.033); // Cap at 33ms to prevent tunneling
    let mut state = sim().write().unwrap();

    // Black hole gaze update
    {
        let bh = &mut state.black_hole;
        let dx = mouse_x - bh.x;
        let dy = mouse_y - bh.y;
        let dist = (dx*dx + dy*dy).sqrt();
        if dist < BH_GAZE_TRIGGER_DIST {
            bh.active = true;
            bh.gaze_timer = (bh.gaze_timer + dt).min(BH_GAZE_MAX_TIME);
        } else {
            bh.active = false;
            bh.gaze_timer = (bh.gaze_timer - dt * 2.0).max(0.0);
        }
    }

    let gravity = state.gravity;
    let bh_active = state.black_hole.active && state.black_hole.gaze_timer > 0.1;
    let bh_x = state.black_hole.x;
    let bh_y = state.black_hole.y;
    let bh_mass = state.black_hole.mass;
    let count = state.objects.count;

    // --- Velocity Verlet integration (Priority 2) ---
    // Also handles: object sleeping (Priority 5)
    for i in 0..count {
        let objs = &mut state.objects;
        if !objs.active[i] { continue; }
        if objs.sleeping[i] { continue; }

        // Save previous position for temporal interpolation
        objs.prev_x[i] = objs.x[i];
        objs.prev_y[i] = objs.y[i];

        // Compute current acceleration
        let mut new_ax = 0.0f32;
        let mut new_ay = gravity;

        // Black hole gravity
        if bh_active {
            let dx = bh_x - objs.x[i];
            let dy = bh_y - objs.y[i];
            let dist_sq = dx*dx + dy*dy;
            if dist_sq > 100.0 {
                let dist = dist_sq.sqrt();
                let force = ((6.674e-11 * bh_mass * objs.mass[i] as f64) / dist_sq as f64 * 100000.0) as f32;
                new_ax += (dx / dist) * force / objs.mass[i];
                new_ay += (dy / dist) * force / objs.mass[i];
            }
        }

        // Velocity Verlet: x += v*dt + 0.5*a*dt^2
        objs.x[i] += objs.vx[i] * dt + 0.5 * objs.ax[i] * dt * dt;
        objs.y[i] += objs.vy[i] * dt + 0.5 * objs.ay[i] * dt * dt;

        // Velocity Verlet: v += 0.5*(a_prev + a_new)*dt
        objs.vx[i] += 0.5 * (objs.ax[i] + new_ax) * dt;
        objs.vy[i] += 0.5 * (objs.ay[i] + new_ay) * dt;

        objs.ax[i] = new_ax;
        objs.ay[i] = new_ay;

        // Boundary bounce
        let r = objs.radius[i];
        let rest = objs.restitution[i];
        if objs.x[i] < r { objs.x[i] = r; objs.vx[i] = objs.vx[i].abs() * rest; }
        if objs.x[i] > width as f32 - r { objs.x[i] = width as f32 - r; objs.vx[i] = -objs.vx[i].abs() * rest; }
        if objs.y[i] < r { objs.y[i] = r; objs.vy[i] = objs.vy[i].abs() * rest; }
        if objs.y[i] > height as f32 - r { objs.y[i] = height as f32 - r; objs.vy[i] = -objs.vy[i].abs() * rest; }

        // Object sleeping check (Priority 5)
        let speed = objs.vx[i].abs() + objs.vy[i].abs();
        if speed < SLEEP_VELOCITY_THRESHOLD {
            objs.sleep_timer[i] += 1;
            if objs.sleep_timer[i] >= SLEEP_FRAME_THRESHOLD {
                objs.sleeping[i] = true;
                objs.vx[i] = 0.0;
                objs.vy[i] = 0.0;
            }
        } else {
            objs.sleep_timer[i] = 0;
        }
    }

    // --- BVH rebuild (only active, awake objects) ---
    state.bvh.clear();
    for i in 0..count {
        if state.objects.active[i] && !state.objects.sleeping[i] {
            state.bvh.insert(i as i32, state.objects.x[i], state.objects.y[i], state.objects.radius[i]);
        }
    }

    // --- Iterative constraint solver (Priority 6) ---
    // Collect candidate pairs from BVH
    let mut pairs: Vec<(usize, usize)> = Vec::new();
    for i in 0..count {
        if !state.objects.active[i] || state.objects.sleeping[i] { continue; }
        let mut results = Vec::new();
        state.bvh.query(state.objects.x[i], state.objects.y[i], state.objects.radius[i] * 2.0, &mut results);
        for &j_id in &results {
            let j = j_id as usize;
            if j > i && state.objects.active[j] {
                pairs.push((i, j));
            }
        }
    }

    // Run SOLVER_ITERATIONS passes over all pairs
    for _ in 0..SOLVER_ITERATIONS {
        for &(i, j) in &pairs {
            let dx = state.objects.x[i] - state.objects.x[j];
            let dy = state.objects.y[i] - state.objects.y[j];
            let dist_sq = dx*dx + dy*dy;
            let min_dist = state.objects.radius[i] + state.objects.radius[j];
            if dist_sq < min_dist * min_dist && dist_sq > 0.001 {
                let dist = dist_sq.sqrt();
                let nx = dx / dist;
                let ny = dy / dist;
                let rel_vx = state.objects.vx[i] - state.objects.vx[j];
                let rel_vy = state.objects.vy[i] - state.objects.vy[j];
                let vel_along = rel_vx * nx + rel_vy * ny;
                if vel_along < 0.0 {
                    let e = state.objects.restitution[i].min(state.objects.restitution[j]);
                    let inv_ma = 1.0 / state.objects.mass[i];
                    let inv_mb = 1.0 / state.objects.mass[j];
                    let impulse = -(1.0 + e) * vel_along / (inv_ma + inv_mb);
                    state.objects.vx[i] += impulse * inv_ma * nx;
                    state.objects.vy[i] += impulse * inv_ma * ny;
                    state.objects.vx[j] -= impulse * inv_mb * nx;
                    state.objects.vy[j] -= impulse * inv_mb * ny;
                    // Wake sleeping neighbors
                    state.objects.sleeping[j] = false;
                    state.objects.sleep_timer[j] = 0;
                }
                let overlap = (min_dist - dist) / 2.0;
                state.objects.x[i] += nx * overlap;
                state.objects.y[i] += ny * overlap;
                state.objects.x[j] -= nx * overlap;
                state.objects.y[j] -= ny * overlap;
            }
        }
    }

    // --- Particle update ---
    let pool = &mut state.particles;
    for i in 0..pool.high_water_mark as usize {
        if !pool.particles[i].active { continue; }
        let p = &mut pool.particles[i];
        p.x += p.vx * dt;
        p.y += p.vy * dt;
        p.life -= dt;
        if p.life <= 0.0 {
            p.active = false;
            pool.free_list[pool.free_count as usize] = i as i32;
            pool.free_count += 1;
            pool.active_count -= 1;
        }
    }

    // --- Write render back-buffer (packed RenderInstance, 20 bytes) ---
    // Renderer reads front buffer; we write back buffer then swap
    let mut rc = 0usize;
    for i in 0..count {
        if !state.objects.active[i] { continue; }
        if rc < state.render_back.len() {
            state.render_back[rc] = RenderInstance {
                x:      state.objects.x[i],
                y:      state.objects.y[i],
                radius: state.objects.radius[i],
                color:  state.objects.color[i],
                shape:  state.objects.shape[i],
            };
            rc += 1;
        }
    }
    state.render_count = rc;
    std::mem::swap(&mut state.render_front, &mut state.render_back);
}

// --- Bulk copy for renderer (Priority 2 P/Invoke fix) ---
// Copies packed RenderInstance array into caller-provided buffer.
// Returns count written. One P/Invoke call per frame for all objects.
#[no_mangle]
pub extern "C" fn copy_render_instances(out_buf: *mut RenderInstance, buf_len: i32) -> i32 {
    let state = sim().read().unwrap();
    let count = state.render_count.min(buf_len as usize);
    if count == 0 || out_buf.is_null() { return 0; }
    unsafe {
        std::ptr::copy_nonoverlapping(state.render_front.as_ptr(), out_buf, count);
    }
    count as i32
}

#[no_mangle]
pub extern "C" fn copy_active_particles(out_buf: *mut Particle, buf_len: i32) -> i32 {
    let state = sim().read().unwrap();
    let pool = &state.particles;
    let mut written = 0i32;
    for i in 0..pool.high_water_mark as usize {
        if written >= buf_len { break; }
        if pool.particles[i].active {
            unsafe { *out_buf.add(written as usize) = pool.particles[i]; }
            written += 1;
        }
    }
    written
}

// --- Queries ---
#[no_mangle]
pub extern "C" fn get_object_count() -> i32 {
    let state = sim().read().unwrap();
    state.objects.active[..state.objects.count].iter().filter(|&&a| a).count() as i32
}

#[no_mangle]
pub extern "C" fn get_particle_active_count() -> i32 {
    sim().read().unwrap().particles.active_count
}

#[no_mangle]
pub extern "C" fn get_max_objects() -> i32 { MAX_OBJECTS as i32 }

#[no_mangle]
pub extern "C" fn get_object_slot_count() -> i32 {
    sim().read().unwrap().objects.count as i32
}

#[no_mangle]
pub extern "C" fn get_particle_high_water_mark() -> i32 {
    sim().read().unwrap().particles.high_water_mark
}

#[no_mangle]
pub extern "C" fn set_gravity(g: f32) { sim().write().unwrap().gravity = g; }

#[no_mangle]
pub extern "C" fn get_black_hole_position(out_x: *mut f32, out_y: *mut f32) {
    let state = sim().read().unwrap();
    unsafe {
        if !out_x.is_null() { *out_x = state.black_hole.x; }
        if !out_y.is_null() { *out_y = state.black_hole.y; }
    }
}

#[no_mangle]
pub extern "C" fn get_black_hole_gaze() -> f32 { sim().read().unwrap().black_hole.gaze_timer }

#[no_mangle]
pub extern "C" fn get_black_hole_active() -> bool { sim().read().unwrap().black_hole.active }

#[no_mangle]
pub extern "C" fn wake_objects_near(x: f32, y: f32, radius: f32) {
    let mut state = sim().write().unwrap();
    let count = state.objects.count;
    for i in 0..count {
        if !state.objects.active[i] || !state.objects.sleeping[i] { continue; }
        let dx = state.objects.x[i] - x;
        let dy = state.objects.y[i] - y;
        if dx*dx + dy*dy < radius*radius {
            state.objects.sleeping[i] = false;
            state.objects.sleep_timer[i] = 0;
        }
    }
}
