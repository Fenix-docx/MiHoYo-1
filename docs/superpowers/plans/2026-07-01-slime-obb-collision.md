# Slime OBB Collision Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the slime's static obstacle response from world-space AABB collision with oriented-box collision so particles can collide with and move along sloped static colliders.

**Architecture:** Keep the existing CPU Burst PBF pipeline and replace only the static collider payload and `UpdateJob` collision math. Static `BoxCollider` and `MeshCollider` obstacles are captured as sim-space OBBs; particles are transformed into collider-local coordinates for penetration/segment tests, then pushed out along the correct world-space normal.

**Tech Stack:** Unity 6000.4, Burst jobs, Unity.Mathematics, NUnit editor tests.

---

### Task 1: Capture Static Colliders as OBBs

**Files:**
- Modify: `Assets/Slime/Slime_Particle.cs`
- Modify: `Assets/Slime/Slime_Simulator.cs`
- Test: `Assets/Slime/Tests/Editor/SlimeShapeTargetTests.cs`

- [ ] **Step 1: Write failing test**

Add an editor test that creates a rotated static cube, calls `RebuildColliders()`, and asserts the collected collider stores local axes matching the obstacle transform instead of only `bounds.extents`.

- [ ] **Step 2: Run test to verify it fails**

Run Unity editor tests for `SlimeStaticColliderCollectionTests`. Expected failure before implementation: `SlimeBoxCollider` has no orientation fields or collected axes are not populated.

- [ ] **Step 3: Implement minimal collider payload**

Extend `SlimeBoxCollider` with `AxisX`, `AxisY`, and `AxisZ` in sim-space orientation, and populate them from the collider transform in `RebuildColliders()`.

- [ ] **Step 4: Run test to verify it passes**

Run the same editor test class. Expected: rotated collider axes and extents are collected from local shape data.

### Task 2: Resolve Particles Against OBB Slopes

**Files:**
- Modify: `Assets/Slime/Slime_SimulationJobs.cs`
- Test: `Assets/Slime/Tests/Editor/SlimeShapeTargetTests.cs`

- [ ] **Step 1: Write failing test**

Add a job-level test that places a particle inside a 45-degree rotated box slope and asserts `UpdateJob` pushes the particle to the OBB surface along the rotated normal, not to a world-axis face.

- [ ] **Step 2: Run test to verify it fails**

Run the test. Expected failure before implementation: the particle is pushed along world X/Y/Z because `UpdateJob` still performs AABB penetration.

- [ ] **Step 3: Implement OBB collision**

Inside `UpdateJob`, compute local coordinates via dot products against the collider axes, run the existing penetration and segment tests in local space, convert the contact normal/point back to sim space, and reconstruct velocity from the corrected position.

- [ ] **Step 4: Run test to verify it passes**

Run the job-level collision test. Expected: particle is finite and exits on the rotated slope normal.

### Task 3: Preserve Movement Behavior

**Files:**
- Modify: `Assets/Slime/Slime_SimulationJobs.cs`
- Test: `Assets/Slime/Tests/Editor/SlimeShapeTargetTests.cs`

- [ ] **Step 1: Add regression coverage**

Keep existing tests for transform-follow, static collider collection, and dynamic rigidbody filtering. Add one assertion that dynamic rigidbodies remain ignored after the collider payload changes.

- [ ] **Step 2: Remove hard floor interference**

Replace the unconditional `p.Position.y = max(1f, p.Position.y)` with an optional floor height field. Disable it when static colliders are present so slopes can control contact.

- [ ] **Step 3: Run verification**

Run the slime editor tests if Unity CLI is available. If the CLI is unavailable, run compile-adjacent checks and report that Unity test execution needs to be done in the editor.
