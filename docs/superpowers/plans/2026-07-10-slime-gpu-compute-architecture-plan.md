# Slime GPU Compute Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the scatter-sensitive slime workload toward GPU compute without desynchronizing particle simulation, surface reconstruction, rendering, and gameplay contact.

**Architecture:** Replace the current "GPU surface -> CPU readback -> CPU Mesh -> GPU render" loop with a shared GPU data pipeline. CPU remains the game-control and Unity-event authority at first, then neighbor-heavy particle simulation and surface reconstruction progressively move onto GPU buffers; CPU receives compact summaries and low-rate event data instead of full mesh data.

**Tech Stack:** Unity 6000.4.2f1, C#, ComputeShader/HLSL, ComputeBuffer/GraphicsBuffer, AsyncGPUReadback for summaries only, Burst/Jobs as CPU fallback, EditMode tests, Windows desktop target.

---

## Current Problem

The current GPU mode only moves surface reconstruction to compute shader, then reads the generated mesh back to CPU every surface update. That creates a round trip:

`CPU particles -> GPU surface compute -> GPU readback -> CPU Mesh arrays -> Mesh upload -> GPU render`

This is useful as a compatibility bridge for `CurrentSurfaceMesh`, but it is the wrong final rendering path. It creates visual lag because the rendered mesh is always a completed historical GPU job, and it can lower FPS because the readback currently scales with GPU vertex-buffer capacity.

Scatter also increases cost outside rendering. The CPU simulation has neighbor-heavy PBF jobs, shape/cohesion work, and surface sparse block work. The final target must therefore migrate or reduce the scatter-sensitive calculations, not just change the final draw call.

## Architecture Direction

Use one shared particle snapshot per simulation tick:

1. `SlimeSimulator` owns high-level control, group state, input, checkpoints, and gameplay events.
2. `SlimeGpuComputePipeline` owns GPU buffers for particles, predicted positions, spatial grid, neighbor lists, density fields, generated surface vertices, indirect draw args, and compact summary output.
3. Surface rendering reads GPU-generated vertex buffers directly with `Graphics.DrawProceduralIndirect` or an equivalent indirect draw path.
4. CPU mesh readback is removed from the visual path. It becomes a debug/fallback path only.
5. Gameplay contact uses Unity physics callbacks, particle summary probes, or low-rate simplified surface/contact readback, not full mesh readback every frame.

## Migration Phases

### Phase 0: Instrument Before Moving More Code

**Purpose:** Verify the actual bottleneck split between CPU simulation, GPU compute, readback bandwidth, mesh upload, and render draw.

**Files:**
- Modify: `Assets/Slime/SlimeGpuSurfacePipeline.cs`
- Modify: `Assets/Slime/Slime_SurfaceBuilder.cs`
- Modify: `Assets/Slime/SlimeStatusHud.cs`
- Test: `Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs`

- [ ] Add counters for active block count, generated vertex count, vertex buffer capacity, estimated readback bytes, readback latency frames, and surface mode.
- [ ] Display counters in `SlimeStatusHud` when a `SlimeSurfaceBuilder` is present.
- [ ] Add an EditMode test for readback byte estimation using `vertexCount * 24` and capacity-based upper bound.
- [ ] Run `dotnet build .\Assembly-CSharp-Editor.csproj`.
- [ ] In Unity Profiler, record CPU mode, current GPU readback mode, and scattered state.

**Exit Criteria:**
- We can explain a frame spike with numbers: CPU simulation ms, GPU surface dispatch ms, readback bytes, mesh upload ms, active block count.

### Phase 1: Remove Full Mesh Readback From Visual Rendering

**Purpose:** Stop the visible slime from depending on CPU readback.

**Files:**
- Create: `Assets/Slime/SlimeGpuSurfaceRenderer.cs`
- Create: `Assets/Slime/SlimeGpuSurfaceDraw.shader`
- Modify: `Assets/Slime/SlimeGpuSurfacePipeline.cs`
- Modify: `Assets/Slime/Slime_SurfaceBuilder.cs`
- Test: `Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs`

- [ ] Keep the existing CPU surface mode unchanged.
- [ ] Add a new mode name such as `GpuDirectRender`.
- [ ] Change the compute pipeline so generated surface vertices remain in a GPU buffer.
- [ ] Write indirect draw args from the generated vertex count.
- [ ] Draw triangles from the GPU vertex buffer without creating or updating a CPU `Mesh`.
- [ ] Keep `GpuAsyncReadback` as a compatibility/debug mode only.
- [ ] Add tests that default mode remains CPU and that GPU direct mode can be selected without replacing CPU mode.
- [ ] Run `dotnet build .\Assembly-CSharp-Editor.csproj`.
- [ ] Verify in Unity that movement lag disappears in `GpuDirectRender` because the visual path no longer waits for CPU mesh readback.

**Exit Criteria:**
- The visible surface no longer uses `CurrentSurfaceMesh` in GPU direct mode.
- `AsyncGPUReadback` is not required for normal visual rendering.
- Existing door/hazard scripts still work in CPU or compatibility mode.

### Phase 2: Replace Sparse Key Linear Scans With GPU Hash Lookup

**Purpose:** Remove the `FindKeyIndex` linear scan that explodes when the slime scatters into many sparse blocks.

**Files:**
- Modify: `Assets/Slime/SlimeGpuSurfacePipeline.cs`
- Modify: `Assets/Slime/Slime_Surface.compute`
- Test: `Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs`

- [ ] Build an open-addressed block-key hash table on CPU from the active sparse block keys.
- [ ] Upload hash keys and hash values to compute buffers.
- [ ] Replace shader `FindKeyIndex(int3 key)` linear scan with bounded hash probing.
- [ ] Use the hash lookup in blur and marching cubes owner checks.
- [ ] Add EditMode tests for hash capacity, negative block coordinates, and collision probing using the same C# hash function as the shader.
- [ ] Run `dotnet build .\Assembly-CSharp-Editor.csproj`.
- [ ] In Unity, compare scattered-state GPU dispatch time before/after this phase.

**Exit Criteria:**
- Sparse lookup cost is approximately independent of active block count except for normal hash probe count.
- Scattered-state shader time drops before changing simulation.

### Phase 3: Change GPU Density Projection To Particle-Driven Scatter

**Purpose:** Replace the current `block * 64 cells * all particles` density projection with `particles * 5 * 5 * 5 neighbor cells`, matching the CPU algorithmic shape.

**Files:**
- Modify: `Assets/Slime/SlimeGpuSurfacePipeline.cs`
- Modify: `Assets/Slime/Slime_Surface.compute`
- Test: `Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs`

- [ ] Use the block hash table from Phase 2 to map particle-neighbor cells to density slots.
- [ ] Dispatch `ProjectDensity` by particle index instead of density-cell index.
- [ ] Accumulate density using fixed-point `uint` atomics because portable float atomics are not reliable across Unity/DX targets.
- [ ] Convert fixed-point density to float before blur or inside the raw-density read helper.
- [ ] Keep the CPU density path unchanged for fallback.
- [ ] Add tests for fixed-point conversion bounds and deterministic accumulation scale.
- [ ] Run `dotnet build .\Assembly-CSharp-Editor.csproj`.
- [ ] Profile scattered-state density projection.

**Exit Criteria:**
- GPU density projection scales with particle count and local support radius, not with active block count times all particles.
- Dense and scattered visual output remain close to CPU mode.

### Phase 4: Introduce GPU Spatial Grid For Particle Simulation

**Purpose:** Build the GPU data structure required before moving PBF simulation jobs.

**Files:**
- Create: `Assets/Slime/SlimeGpuSimulationPipeline.cs`
- Create: `Assets/Slime/Slime_Simulation.compute`
- Modify: `Assets/Slime/Slime_Simulator.cs`
- Test: `Assets/Slime/Tests/Editor/SlimeSplitTests.cs`

- [ ] Add GPU buffers for particles, predicted positions, old positions, lambda, delta position, grid cell counters, grid cell particle indices, and compact simulation summary.
- [ ] Build a fixed-capacity uniform-grid bucket structure with atomic counters.
- [ ] Keep CPU simulation authoritative in this phase; upload CPU particles and validate GPU grid summaries against CPU `HashJob` / `BuildLutJob` behavior.
- [ ] Add debug readback for grid overflow count and max cell occupancy.
- [ ] Add tests for cell-key packing and overflow policy.
- [ ] Run `dotnet build .\Assembly-CSharp-Editor.csproj`.
- [ ] Profile scattered-state grid build time.

**Exit Criteria:**
- GPU can build a neighbor-query structure from the same particles that CPU simulation uses.
- Overflow is visible and triggers CPU fallback instead of silent corruption.

### Phase 5: Move PBF Neighbor-Heavy Simulation Kernels To GPU

**Purpose:** Offload the scatter-sensitive physics work while keeping control flow and Unity events on CPU.

**Files:**
- Modify: `Assets/Slime/SlimeGpuSimulationPipeline.cs`
- Modify: `Assets/Slime/Slime_Simulation.compute`
- Modify: `Assets/Slime/Slime_Simulator.cs`
- Test: `Assets/Slime/Tests/Editor/SlimeSplitTests.cs`
- Test: `Assets/Slime/Tests/Editor/SlimeCollisionEventTests.cs`

- [ ] Port `ApplyForceJob` to a GPU kernel using controller/group data buffers.
- [ ] Port `ComputeLambdaJob` using the GPU grid neighbor query.
- [ ] Port `ComputeDeltaPosJob` using the same neighbor query.
- [ ] Port `UpdateJob` and `ApplyViscosityJob`.
- [ ] Keep shape target and group control data CPU-authored, uploaded once per simulation tick.
- [ ] Read back compact summaries only: controlled center, group counts, overflow flags, and event flags.
- [ ] Add a runtime switch: `CpuSimulation`, `GpuSimulationExperimental`, `Auto`.
- [ ] Add tests that CPU mode remains default and mode switching preserves particle count/group state.
- [ ] Run `dotnet build .\Assembly-CSharp-Editor.csproj`.
- [ ] In Unity, compare CPU and GPU simulation center drift over 10 seconds in dense and scattered states.

**Exit Criteria:**
- Scattered-state simulation cost drops or becomes more stable.
- CPU gameplay systems receive current centers and group state without full particle readback every frame.

### Phase 6: Collision And Gameplay Contact Strategy

**Purpose:** Avoid pretending Unity CPU colliders are automatically available inside compute shaders.

**Files:**
- Create: `Assets/Slime/SlimeGpuColliderProxy.cs`
- Modify: `Assets/Slime/SlimeGpuSimulationPipeline.cs`
- Modify: `Assets/Slime/Slime_Simulation.compute`
- Modify: `Assets/Slime/SlimeDoorSwitchOnContact.cs`
- Modify: `Assets/Scripts/SlimeGameOverOnContact.cs`
- Test: `Assets/Slime/Tests/Editor/SlimeCollisionEventTests.cs`

- [ ] Define supported GPU collider proxies: plane, sphere, capsule, box.
- [ ] Upload proxy collider data for level geometry that needs GPU collision.
- [ ] Keep unsupported colliders on CPU fallback or physics-callback contact path.
- [ ] Change door/hazard scripts so they prefer physics/proxy contact in GPU mode and use surface mesh only in CPU/debug modes.
- [ ] Add tests that unsupported collider types do not silently claim GPU support.
- [ ] Run `dotnet build .\Assembly-CSharp-Editor.csproj`.
- [ ] Validate level scenes for which colliders need proxy components.

**Exit Criteria:**
- GPU simulation does not ignore collision just because CPU colliders are inaccessible.
- Gameplay contact remains deterministic enough for doors, hazards, rewards, and checkpoints.

### Phase 7: Make GPU Surface Consume GPU Simulation Directly

**Purpose:** Remove duplicated CPU particle upload when both simulation and surface are on GPU.

**Files:**
- Modify: `Assets/Slime/SlimeGpuSurfacePipeline.cs`
- Modify: `Assets/Slime/SlimeGpuSimulationPipeline.cs`
- Modify: `Assets/Slime/Slime_SurfaceBuilder.cs`
- Test: `Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs`

- [ ] Allow surface pipeline to consume the simulation pipeline particle buffer directly.
- [ ] Keep an upload path only when CPU simulation is active.
- [ ] Ensure simulation and surface kernels run in deterministic command order.
- [ ] Add mode compatibility checks so `GpuDirectRender` can use CPU particles or GPU particles, but never mixes stale buffers silently.
- [ ] Run `dotnet build .\Assembly-CSharp-Editor.csproj`.
- [ ] Profile CPU mode, GPU surface only, and GPU simulation + GPU surface.

**Exit Criteria:**
- In full GPU mode, particles are not copied CPU -> GPU twice per frame.
- Surface reconstructs from the same particle state used by simulation.

### Phase 8: Auto Fallback And Shipping Settings

**Purpose:** Make Windows desktop builds robust across GPUs.

**Files:**
- Modify: `Assets/Slime/Slime_SurfaceBuilder.cs`
- Modify: `Assets/Slime/Slime_Simulator.cs`
- Modify: `Assets/Slime/SlimeStatusHud.cs`
- Test: `Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs`

- [ ] Add capability checks for compute shader support, buffer sizes, grid overflow, surface overflow, and collider proxy support.
- [ ] In `Auto`, fall back in this order: GPU simulation + GPU direct surface, CPU simulation + GPU direct surface, CPU simulation + CPU surface.
- [ ] Expose HUD diagnostics for the active path and fallback reason.
- [ ] Add tests for fallback priority decisions using pure C# policy helpers.
- [ ] Run `dotnet build .\Assembly-CSharp-Editor.csproj`.
- [ ] Build a Windows desktop player and profile dense/scattered scenes.

**Exit Criteria:**
- Failed GPU capability does not break gameplay.
- The user can see why a scene fell back to CPU.

## Recommended Execution Order

1. Phase 0 first, because without metrics we will not know whether a later change improved the right thing.
2. Phases 1-3 next, because they fix the current surface pipeline and remove the readback/render lag.
3. Phases 4-5 after the surface path is stable, because GPU simulation changes gameplay authority and needs stricter validation.
4. Phase 6 before calling GPU simulation production-ready, because collision is the biggest correctness risk.
5. Phases 7-8 last, to remove duplicate data movement and make the feature shippable.

## Design Constraints

- Do not make CPU Mesh readback part of the normal GPU visual path.
- Do not move simulation to GPU before the neighbor grid has overflow reporting.
- Do not claim GPU collision support for arbitrary Unity colliders.
- Keep CPU modes working until GPU simulation and GPU rendering are both verified in scene.
- Prefer compact summary readback over full particle or full mesh readback.

## Validation Targets

- Dense normal state: GPU mode should not be slower than CPU mode.
- Scattered state: FPS should not drop to one tenth of normal.
- Visual latency: GPU direct render should track movement without obvious historical mesh lag.
- Gameplay: doors, hazards, checkpoints, split/switch control, and respawn must still work.
- Diagnostics: HUD should show active mode, fallback reason, active blocks, generated vertices, GPU overflow, and readback latency when a readback path is active.

## Self-Review

- Spec coverage: The plan covers rendering latency, scatter-sensitive surface reconstruction, scatter-sensitive particle simulation, CPU/GPU synchronization, collision correctness, gameplay contact, fallback, and diagnostics.
- Placeholder scan: No implementation step relies on unspecified "later" work; each phase has files, concrete actions, tests, and exit criteria.
- Type consistency: New planned units are consistently named `SlimeGpuSimulationPipeline`, `SlimeGpuSurfaceRenderer`, `SlimeGpuColliderProxy`, and `GpuDirectRender`.
