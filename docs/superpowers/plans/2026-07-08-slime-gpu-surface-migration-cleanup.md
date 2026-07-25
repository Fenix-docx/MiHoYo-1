# Slime GPU Surface Migration And Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move slime surface reconstruction computation toward a GPU compute path while first cleaning the duplicated CPU mesh contact logic that gameplay scripts depend on.

**Architecture:** Keep PBF simulation, split/absorb, collision events, and gameplay triggers on CPU. Preserve `SlimeSurfaceBuilder.CurrentSurfaceMesh` and `HasSurface` so existing door, reward, and hazard scripts keep working while a new GPU surface pipeline computes density/MC and asynchronously reads the resulting mesh back to CPU. Refactor duplicated surface-contact code into a shared utility before integrating GPU readback.

**Tech Stack:** Unity 6000.4.2f1, C#, Unity.Mathematics, Unity.Collections, Unity EditMode tests, ComputeShader, GraphicsBuffer/ComputeBuffer, AsyncGPUReadback, Mesh.SetVertexBufferData.

---

## Scope

This plan intentionally migrates only the surface reconstruction path. Do not migrate `Slime_SimulationJobs.cs`, `Slime_CohesionJobs.cs`, `SlimeGroupManager.cs`, split/absorb, controller movement, or collision event dispatch in this branch.

The first GPU implementation keeps CPU Mesh output through `AsyncGPUReadback` because `SlimeDoorSwitchOnContact` and `SlimeGameOverOnContact` read `SlimeSurfaceBuilder.CurrentSurfaceMesh` for gameplay contact checks.

## File Structure

- Create `Assets/Slime/SlimeSurfaceContactUtility.cs`
  Shared mesh-to-collider contact helper used by all slime surface contact gameplay scripts.
- Modify `Assets/Slime/SlimeDoorSwitchOnContact.cs`
  Replace local mesh vertex scanning with `SlimeSurfaceContactUtility`.
- Modify `Assets/Scripts/SlimeGameOverOnContact.cs`
  Replace local mesh vertex scanning with `SlimeSurfaceContactUtility`; guard optional audio playback.
- Modify `Assets/Slime/Slime_SurfaceBuilder.cs`
  Split CPU builder into a named path and add a surface build mode that can select CPU, GPU async, or auto fallback.
- Create `Assets/Slime/SlimeGpuSurfacePipeline.cs`
  Own GPU buffers, dispatch compute kernels, manage readback latency, and write completed readback data to `Mesh`.
- Create `Assets/Slime/Slime_Surface.compute`
  GPU density, blur, Marching Cubes, and compact surface vertex output kernels.
- Modify `Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs`
  Add utility and surface builder mode tests.
- Modify `docs/slime-technical-overview.md`
  Document the new surface build modes and async readback limitation.

## Test Command

Run EditMode tests from a Developer PowerShell:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.4.2f1\Editor\Unity.exe" `
  -batchmode -quit `
  -projectPath "E:\Unity(E;)\MiHoYo" `
  -runTests -testPlatform EditMode `
  -testResults "Temp\SlimeEditModeResults.xml"
```

Expected success output: Unity exits with code `0`, and `Temp\SlimeEditModeResults.xml` contains no failed test cases.

---

### Task 1: Extract Shared Surface Contact Utility

**Files:**
- Create: `Assets/Slime/SlimeSurfaceContactUtility.cs`
- Modify: `Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs`

- [ ] **Step 1: Write failing utility tests**

Append these tests inside `SlimeSurfaceBuilderTests`:

```csharp
[Test]
public void SurfaceContactUtilityFindsColliderNearMeshVertex()
{
    GameObject colliderObject = new GameObject("surface contact target");
    Mesh mesh = new Mesh { name = "contact mesh" };
    var vertices = new System.Collections.Generic.List<Vector3>(16);

    try
    {
        BoxCollider box = colliderObject.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = Vector3.one * 0.25f;

        mesh.vertices = new[]
        {
            Vector3.zero,
            Vector3.right,
            Vector3.up,
        };
        mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
        mesh.RecalculateBounds();

        bool touched = SlimeSurfaceContactUtility.TryGetMeshContact(
            mesh,
            new Collider[] { box },
            vertices,
            maxVertexChecks: 16,
            contactPadding: 0.05f,
            out Vector3 contactPoint);

        Assert.That(touched, Is.True);
        Assert.That(Vector3.Distance(contactPoint, Vector3.zero), Is.LessThan(0.001f));
    }
    finally
    {
        Object.DestroyImmediate(mesh);
        Object.DestroyImmediate(colliderObject);
    }
}

[Test]
public void SurfaceContactUtilityIgnoresFarCollider()
{
    GameObject colliderObject = new GameObject("far surface contact target");
    Mesh mesh = new Mesh { name = "far contact mesh" };
    var vertices = new System.Collections.Generic.List<Vector3>(16);

    try
    {
        colliderObject.transform.position = Vector3.one * 10f;
        BoxCollider box = colliderObject.AddComponent<BoxCollider>();
        box.size = Vector3.one * 0.25f;

        mesh.vertices = new[]
        {
            Vector3.zero,
            Vector3.right,
            Vector3.up,
        };
        mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
        mesh.RecalculateBounds();

        bool touched = SlimeSurfaceContactUtility.TryGetMeshContact(
            mesh,
            new Collider[] { box },
            vertices,
            maxVertexChecks: 16,
            contactPadding: 0.05f,
            out _);

        Assert.That(touched, Is.False);
    }
    finally
    {
        Object.DestroyImmediate(mesh);
        Object.DestroyImmediate(colliderObject);
    }
}
```

Add these usings at the top of the test file:

```csharp
using UnityEngine;
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.4.2f1\Editor\Unity.exe" `
  -batchmode -quit `
  -projectPath "E:\Unity(E;)\MiHoYo" `
  -runTests -testPlatform EditMode `
  -testResults "Temp\SlimeEditModeResults.xml" `
  -testFilter "Slime.Tests.SlimeSurfaceBuilderTests"
```

Expected: FAIL because `SlimeSurfaceContactUtility` does not exist.

- [ ] **Step 3: Create `SlimeSurfaceContactUtility`**

Create `Assets/Slime/SlimeSurfaceContactUtility.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Slime
{
    public static class SlimeSurfaceContactUtility
    {
        public static bool TryGetSurfaceContact(
            SlimeSurfaceBuilder slimeSurface,
            Collider[] colliders,
            List<Vector3> surfaceVertices,
            int maxVertexChecks,
            float contactPadding,
            out Vector3 contactPoint)
        {
            contactPoint = Vector3.zero;
            if (slimeSurface == null || !slimeSurface.HasSurface)
                return false;

            return TryGetMeshContact(
                slimeSurface.CurrentSurfaceMesh,
                colliders,
                surfaceVertices,
                maxVertexChecks,
                contactPadding,
                out contactPoint);
        }

        public static bool TryGetMeshContact(
            Mesh surfaceMesh,
            Collider[] colliders,
            List<Vector3> surfaceVertices,
            int maxVertexChecks,
            float contactPadding,
            out Vector3 contactPoint)
        {
            contactPoint = Vector3.zero;
            if (surfaceMesh == null || surfaceMesh.vertexCount == 0)
                return false;

            if (colliders == null || colliders.Length == 0)
                return false;

            surfaceVertices.Clear();
            surfaceMesh.GetVertices(surfaceVertices);
            if (surfaceVertices.Count == 0)
                return false;

            float padding = Mathf.Max(contactPadding, 0.0001f);
            Bounds surfaceBounds = surfaceMesh.bounds;
            surfaceBounds.Expand(padding * 2f);

            int stride = Mathf.Max(1, Mathf.CeilToInt(surfaceVertices.Count / (float)Mathf.Max(1, maxVertexChecks)));
            float paddingSqr = padding * padding;
            int checks = 0;

            for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
            {
                Collider targetCollider = colliders[colliderIndex];
                if (!IsUsable(targetCollider))
                    continue;

                Bounds targetBounds = targetCollider.bounds;
                targetBounds.Expand(padding * 2f);
                if (!targetBounds.Intersects(surfaceBounds))
                    continue;

                for (int vertexIndex = 0;
                     vertexIndex < surfaceVertices.Count && checks < maxVertexChecks;
                     vertexIndex += stride, checks++)
                {
                    Vector3 point = surfaceVertices[vertexIndex];
                    if (!IsFinite(point))
                        continue;

                    if (targetBounds.SqrDistance(point) > paddingSqr)
                        continue;

                    Vector3 closest = targetCollider.ClosestPoint(point);
                    if ((closest - point).sqrMagnitude <= paddingSqr)
                    {
                        contactPoint = point;
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool IsSlimeObject(Component component)
        {
            if (component == null)
                return false;

            return component.GetComponentInParent<SlimeSimulator>() != null ||
                   component.GetComponentInParent<SlimeSurfaceBuilder>() != null ||
                   component.GetComponentInParent<SlimeControllerMotor>() != null;
        }

        public static bool IsUsable(Collider targetCollider)
        {
            return targetCollider != null &&
                   targetCollider.enabled &&
                   targetCollider.gameObject.activeInHierarchy;
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
```

- [ ] **Step 4: Run utility tests**

Run the same filtered Unity command.

Expected: PASS for `SurfaceContactUtilityFindsColliderNearMeshVertex` and `SurfaceContactUtilityIgnoresFarCollider`.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Slime/SlimeSurfaceContactUtility.cs Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs
git commit -m "refactor: extract slime surface contact utility"
```

---

### Task 2: Refactor Surface Contact Users

**Files:**
- Modify: `Assets/Slime/SlimeDoorSwitchOnContact.cs`
- Modify: `Assets/Scripts/SlimeGameOverOnContact.cs`
- Modify: `Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs`

- [ ] **Step 1: Write a test for the shared slime-object helper**

Append this test to `SlimeSurfaceBuilderTests`:

```csharp
[Test]
public void SurfaceContactUtilityRecognizesSlimeComponents()
{
    GameObject slimeObject = new GameObject("slime object");
    GameObject plainObject = new GameObject("plain object");

    try
    {
        SlimeSimulator simulator = slimeObject.AddComponent<SlimeSimulator>();
        BoxCollider slimeCollider = slimeObject.AddComponent<BoxCollider>();
        BoxCollider plainCollider = plainObject.AddComponent<BoxCollider>();

        Assert.That(SlimeSurfaceContactUtility.IsSlimeObject(slimeCollider), Is.True);
        Assert.That(SlimeSurfaceContactUtility.IsSlimeObject(simulator), Is.True);
        Assert.That(SlimeSurfaceContactUtility.IsSlimeObject(plainCollider), Is.False);
    }
    finally
    {
        Object.DestroyImmediate(slimeObject);
        Object.DestroyImmediate(plainObject);
    }
}
```

- [ ] **Step 2: Run test to verify current helper behavior**

Run:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.4.2f1\Editor\Unity.exe" `
  -batchmode -quit `
  -projectPath "E:\Unity(E;)\MiHoYo" `
  -runTests -testPlatform EditMode `
  -testResults "Temp\SlimeEditModeResults.xml" `
  -testFilter "Slime.Tests.SlimeSurfaceBuilderTests.SurfaceContactUtilityRecognizesSlimeComponents"
```

Expected: PASS if Task 1 implemented `IsSlimeObject` exactly.

- [ ] **Step 3: Refactor `SlimeDoorSwitchOnContact`**

Remove the local `IsPointTouchingCollider` method from `Assets/Slime/SlimeDoorSwitchOnContact.cs`.

Replace `IsAcceptedPhysicsContact` body with:

```csharp
bool IsAcceptedPhysicsContact(Collider other)
{
    if (!SlimeSurfaceContactUtility.IsUsable(other))
        return false;

    return !onlyPhysicsContactFromSlime || SlimeSurfaceContactUtility.IsSlimeObject(other);
}
```

Replace `IsSlimeSurfaceTouchingSwitch` body with:

```csharp
bool IsSlimeSurfaceTouchingSwitch()
{
    if (!ResolveSlimeSurface() || slimeSurface == null || !slimeSurface.HasSurface)
        return false;

    if (_contactColliders == null || _contactColliders.Length == 0)
        RefreshContactColliders();

    return SlimeSurfaceContactUtility.TryGetSurfaceContact(
        slimeSurface,
        _contactColliders,
        _surfaceVertices,
        maxSurfaceVertexChecks,
        contactPadding,
        out _);
}
```

- [ ] **Step 4: Refactor `SlimeGameOverOnContact`**

In `Assets/Scripts/SlimeGameOverOnContact.cs`, remove `SurfaceTouchesCollider`, `IsUsable`, `IsFinite(Vector3)`, and `IsFinite(float)`.

Replace `TouchesCurrentSlimeSurface(out Vector3 contactPoint)` with:

```csharp
public bool TouchesCurrentSlimeSurface(out Vector3 contactPoint)
{
    contactPoint = transform.position;
    if (slimeSurface == null || !slimeSurface.HasSurface)
        return false;

    if (_targetColliders == null || _targetColliders.Length == 0)
        CacheTargetColliders();

    return SlimeSurfaceContactUtility.TryGetSurfaceContact(
        slimeSurface,
        _targetColliders,
        _surfaceVertices,
        maxSurfaceVertexChecks,
        contactPadding,
        out contactPoint);
}
```

Replace `TryAddParticles()` first line:

```csharp
audioSource.PlayOneShot(audioClip);
```

with:

```csharp
if (audioSource != null && audioClip != null)
    audioSource.PlayOneShot(audioClip);
```

Replace `IsSlimeObject(Component component)` with:

```csharp
static bool IsSlimeObject(Component component)
{
    return SlimeSurfaceContactUtility.IsSlimeObject(component);
}
```

- [ ] **Step 5: Run contact-related tests**

Run:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.4.2f1\Editor\Unity.exe" `
  -batchmode -quit `
  -projectPath "E:\Unity(E;)\MiHoYo" `
  -runTests -testPlatform EditMode `
  -testResults "Temp\SlimeEditModeResults.xml" `
  -testFilter "Slime.Tests.SlimeSurfaceBuilderTests"
```

Expected: PASS. Also confirm Unity compilation has no errors.

- [ ] **Step 6: Commit**

```powershell
git add Assets/Slime/SlimeDoorSwitchOnContact.cs Assets/Scripts/SlimeGameOverOnContact.cs Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs
git commit -m "refactor: reuse shared slime surface contact checks"
```

---

### Task 3: Split CPU Surface Builder From Mode Selection

**Files:**
- Modify: `Assets/Slime/Slime_SurfaceBuilder.cs`
- Modify: `Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs`

- [ ] **Step 1: Write failing mode test**

Append this test to `SlimeSurfaceBuilderTests`:

```csharp
[Test]
public void SurfaceBuilderDefaultsToCpuSurfaceMode()
{
    GameObject slimeObject = new GameObject("surface builder mode test");

    try
    {
        slimeObject.AddComponent<MeshFilter>();
        slimeObject.AddComponent<MeshRenderer>();
        SlimeSurfaceBuilder builder = slimeObject.AddComponent<SlimeSurfaceBuilder>();

        Assert.That(builder.SurfaceBuildMode, Is.EqualTo(SlimeSurfaceBuildMode.Cpu));
    }
    finally
    {
        Object.DestroyImmediate(slimeObject);
    }
}
```

Expected failure before implementation: `SlimeSurfaceBuildMode` or `SurfaceBuildMode` does not exist.

- [ ] **Step 2: Add build mode enum and public property**

At the top of `Assets/Slime/Slime_SurfaceBuilder.cs`, inside namespace `Slime` before `SlimeSurfaceBuilder`, add:

```csharp
public enum SlimeSurfaceBuildMode
{
    Cpu,
    GpuAsyncReadback,
    Auto,
}
```

Inside `SlimeSurfaceBuilder`, under the `surfaceMaterial` field, add:

```csharp
[SerializeField] SlimeSurfaceBuildMode surfaceBuildMode = SlimeSurfaceBuildMode.Cpu;
[SerializeField] ComputeShader gpuSurfaceComputeShader;

public SlimeSurfaceBuildMode SurfaceBuildMode => surfaceBuildMode;
```

- [ ] **Step 3: Rename current surface build body to CPU path**

Change `public void BuildSurface(NativeArray<SlimeParticle> particles, float isoLevel)` to:

```csharp
public void BuildSurface(NativeArray<SlimeParticle> particles, float isoLevel)
{
    switch (surfaceBuildMode)
    {
        case SlimeSurfaceBuildMode.GpuAsyncReadback:
            BuildSurfaceCpu(particles, isoLevel);
            break;
        case SlimeSurfaceBuildMode.Auto:
            BuildSurfaceCpu(particles, isoLevel);
            break;
        default:
            BuildSurfaceCpu(particles, isoLevel);
            break;
    }
}

void BuildSurfaceCpu(NativeArray<SlimeParticle> particles, float isoLevel)
{
    // Move the existing BuildSurface implementation here unchanged.
}
```

Move the existing implementation into `BuildSurfaceCpu`. Do not change density, blur, MC, mesh write, or `LastMarchingCubeBlockCount` behavior in this task.

- [ ] **Step 4: Run surface tests**

Run:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.4.2f1\Editor\Unity.exe" `
  -batchmode -quit `
  -projectPath "E:\Unity(E;)\MiHoYo" `
  -runTests -testPlatform EditMode `
  -testResults "Temp\SlimeEditModeResults.xml" `
  -testFilter "Slime.Tests.SlimeSurfaceBuilderTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Slime/Slime_SurfaceBuilder.cs Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs
git commit -m "refactor: split slime surface build mode from cpu path"
```

---

### Task 4: Add GPU Pipeline Shell With Safe CPU Fallback

**Files:**
- Create: `Assets/Slime/SlimeGpuSurfacePipeline.cs`
- Modify: `Assets/Slime/Slime_SurfaceBuilder.cs`
- Modify: `Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs`

- [ ] **Step 1: Write unsupported pipeline test**

Append this test to `SlimeSurfaceBuilderTests`:

```csharp
[Test]
public void GpuSurfacePipelineReportsUnsupportedWithoutShader()
{
    using SlimeGpuSurfacePipeline pipeline = new SlimeGpuSurfacePipeline(null);
    Assert.That(pipeline.IsSupported, Is.False);
}
```

Expected failure before implementation: `SlimeGpuSurfacePipeline` does not exist.

- [ ] **Step 2: Create pipeline shell**

Create `Assets/Slime/SlimeGpuSurfacePipeline.cs`:

```csharp
using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Slime
{
    public sealed class SlimeGpuSurfacePipeline : IDisposable
    {
        readonly ComputeShader _computeShader;

        public SlimeGpuSurfacePipeline(ComputeShader computeShader)
        {
            _computeShader = computeShader;
            IsSupported = _computeShader != null && SystemInfo.supportsComputeShaders;
        }

        public bool IsSupported { get; }
        public bool HasPendingReadback => false;
        public int LastVertexCount { get; private set; }
        public int LastReadbackLatencyFrames { get; private set; }
        public bool LastOverflow { get; private set; }

        public bool TrySchedule(
            NativeArray<SlimeParticle> particles,
            NativeArray<int3> keys,
            NativeHashMap<int3, int> gridLut,
            float3 minPos,
            float isoLevel)
        {
            LastVertexCount = 0;
            LastReadbackLatencyFrames = 0;
            LastOverflow = false;
            return IsSupported && particles.IsCreated && keys.IsCreated && keys.Length > 0;
        }

        public bool TryApplyCompletedReadback(Mesh mesh)
        {
            return false;
        }

        public void Dispose()
        {
        }
    }
}
```

- [ ] **Step 3: Integrate shell into `SlimeSurfaceBuilder`**

Add field:

```csharp
SlimeGpuSurfacePipeline _gpuPipeline;
```

In `Awake()`, after `_bounds` allocation:

```csharp
_gpuPipeline = new SlimeGpuSurfacePipeline(gpuSurfaceComputeShader);
```

In `BuildSurface`, replace the switch with:

```csharp
public void BuildSurface(NativeArray<SlimeParticle> particles, float isoLevel)
{
    if (surfaceBuildMode == SlimeSurfaceBuildMode.Cpu)
    {
        BuildSurfaceCpu(particles, isoLevel);
        return;
    }

    if (surfaceBuildMode == SlimeSurfaceBuildMode.GpuAsyncReadback && !TryBuildSurfaceGpuAsync(particles, isoLevel))
        return;

    if (surfaceBuildMode == SlimeSurfaceBuildMode.Auto && TryBuildSurfaceGpuAsync(particles, isoLevel))
        return;

    BuildSurfaceCpu(particles, isoLevel);
}

bool TryBuildSurfaceGpuAsync(NativeArray<SlimeParticle> particles, float isoLevel)
{
    return _gpuPipeline != null &&
           _gpuPipeline.IsSupported &&
           _gpuPipeline.TryApplyCompletedReadback(_mesh);
}
```

In `OnDestroy()` before mesh destruction:

```csharp
_gpuPipeline?.Dispose();
_gpuPipeline = null;
```

This task makes GPU modes safe but still CPU-fallback only.

- [ ] **Step 4: Run tests**

Run the filtered surface tests. Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Slime/SlimeGpuSurfacePipeline.cs Assets/Slime/Slime_SurfaceBuilder.cs Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs
git commit -m "feat: add slime gpu surface pipeline shell"
```

---

### Task 5: Build CPU Sparse Metadata Once For CPU And GPU Paths

**Files:**
- Modify: `Assets/Slime/Slime_SurfaceBuilder.cs`
- Modify: `Assets/Slime/SlimeGpuSurfacePipeline.cs`
- Modify: `Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs`

- [ ] **Step 1: Add a test that CPU mode still reports skipped blocks**

Keep the existing `MarchingCubesReportsSkippedBlocksBelowIso` test unchanged. It guards MC behavior while this task moves sparse metadata code around.

Run it before editing:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.4.2f1\Editor\Unity.exe" `
  -batchmode -quit `
  -projectPath "E:\Unity(E;)\MiHoYo" `
  -runTests -testPlatform EditMode `
  -testResults "Temp\SlimeEditModeResults.xml" `
  -testFilter "Slime.Tests.SlimeSurfaceBuilderTests.MarchingCubesReportsSkippedBlocksBelowIso"
```

Expected: PASS before refactor.

- [ ] **Step 2: Extract `PrepareSparseSurfaceData` in `SlimeSurfaceBuilder`**

Add method:

```csharp
bool PrepareSparseSurfaceData(
    NativeArray<SlimeParticle> particles,
    out NativeArray<int3> keys,
    out float3 minPos)
{
    new SlimeDensityField.CalcBoundsJob { Ps = particles, Bounds = _bounds }.Schedule().Complete();
    minPos = _bounds[0];

    new SlimeDensityField.ClearGridJob { Grid = _grid }.Schedule().Complete();

    _gridLut.Clear();
    new SlimeDensityField.AllocateBlockJob
    {
        Ps = particles,
        GridLut = _gridLut,
        MinPos = minPos,
        MaxBlocks = SlimeConst.GridNum,
    }.Schedule().Complete();

    keys = _gridLut.GetKeyArray(Allocator.TempJob);
    return keys.Length > 0;
}
```

Update `BuildSurfaceCpu` to call `PrepareSparseSurfaceData`. Keep density projection, blur, and MC generation in `BuildSurfaceCpu`.

When `keys.Length == 0`, keep current behavior:

```csharp
LastMarchingCubeBlockCount = 0;
LastSkippedMarchingCubeBlockCount = 0;
keys.Dispose();
_mesh.Clear();
return;
```

- [ ] **Step 3: Schedule GPU only after sparse metadata is prepared**

Update `TryBuildSurfaceGpuAsync` signature:

```csharp
bool TryBuildSurfaceGpuAsync(
    NativeArray<SlimeParticle> particles,
    NativeArray<int3> keys,
    float3 minPos,
    float isoLevel)
```

Call:

```csharp
return _gpuPipeline != null &&
       _gpuPipeline.IsSupported &&
       _gpuPipeline.TrySchedule(particles, keys, _gridLut, minPos, isoLevel);
```

Change `BuildSurface` to:

```csharp
public void BuildSurface(NativeArray<SlimeParticle> particles, float isoLevel)
{
    if (surfaceBuildMode == SlimeSurfaceBuildMode.Cpu)
    {
        BuildSurfaceCpu(particles, isoLevel);
        return;
    }

    if (_gpuPipeline != null && _gpuPipeline.TryApplyCompletedReadback(_mesh))
    {
        LastMarchingCubeBlockCount = _gpuPipeline.LastVisitedBlockCount;
        LastSkippedMarchingCubeBlockCount = _gpuPipeline.LastSkippedBlockCount;
    }

    NativeArray<int3> keys;
    float3 minPos;
    if (!PrepareSparseSurfaceData(particles, out keys, out minPos))
    {
        LastMarchingCubeBlockCount = 0;
        LastSkippedMarchingCubeBlockCount = 0;
        _mesh.Clear();
        return;
    }

    try
    {
        bool scheduledGpu = surfaceBuildMode != SlimeSurfaceBuildMode.Cpu &&
            TryBuildSurfaceGpuAsync(particles, keys, minPos, isoLevel);

        if (!scheduledGpu && surfaceBuildMode == SlimeSurfaceBuildMode.Auto)
            BuildSurfaceCpuPrepared(particles, keys, minPos, isoLevel);
        else if (!scheduledGpu && surfaceBuildMode == SlimeSurfaceBuildMode.GpuAsyncReadback)
            BuildSurfaceCpuPrepared(particles, keys, minPos, isoLevel);
    }
    finally
    {
        keys.Dispose();
    }
}
```

Rename the CPU implementation that receives already prepared keys:

```csharp
void BuildSurfaceCpuPrepared(
    NativeArray<SlimeParticle> particles,
    NativeArray<int3> keys,
    float3 minPos,
    float isoLevel)
```

Keep a thin `BuildSurfaceCpu` wrapper:

```csharp
void BuildSurfaceCpu(NativeArray<SlimeParticle> particles, float isoLevel)
{
    NativeArray<int3> keys;
    float3 minPos;
    if (!PrepareSparseSurfaceData(particles, out keys, out minPos))
    {
        LastMarchingCubeBlockCount = 0;
        LastSkippedMarchingCubeBlockCount = 0;
        _mesh.Clear();
        return;
    }

    try
    {
        BuildSurfaceCpuPrepared(particles, keys, minPos, isoLevel);
    }
    finally
    {
        keys.Dispose();
    }
}
```

- [ ] **Step 4: Add missing GPU stats properties**

In `SlimeGpuSurfacePipeline`, add:

```csharp
public int LastVisitedBlockCount { get; private set; }
public int LastSkippedBlockCount { get; private set; }
```

Set both to zero in the shell `TrySchedule`.

- [ ] **Step 5: Run surface tests**

Run `SlimeSurfaceBuilderTests`. Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add Assets/Slime/Slime_SurfaceBuilder.cs Assets/Slime/SlimeGpuSurfacePipeline.cs
git commit -m "refactor: share slime surface sparse metadata preparation"
```

---

### Task 6: Add GPU Buffers And Compute Shader Asset

**Files:**
- Create: `Assets/Slime/Slime_Surface.compute`
- Modify: `Assets/Slime/SlimeGpuSurfacePipeline.cs`

- [ ] **Step 1: Add compute shader asset with exact kernel names**

Create `Assets/Slime/Slime_Surface.compute`:

```hlsl
#pragma kernel ClearDensity
#pragma kernel ProjectDensity
#pragma kernel BlurDensity
#pragma kernel MarchingCubes

struct SlimeParticleGpu
{
    float3 Position;
    int ID;
};

struct SurfaceVertexGpu
{
    float3 Position;
    float3 Normal;
};

StructuredBuffer<SlimeParticleGpu> _Particles;
StructuredBuffer<int3> _Keys;
RWStructuredBuffer<float> _DensityRead;
RWStructuredBuffer<float> _DensityWrite;
AppendStructuredBuffer<SurfaceVertexGpu> _Vertices;
RWStructuredBuffer<uint> _Stats;

int _ParticleCount;
int _KeyCount;
float3 _MinPos;
float _IsoLevel;
float _CellSize;
float _H2;
float _Poly6Norm;
float _Scale;

int LocalIndex(int3 c)
{
    return (c.x & 3) + 4 * ((c.y & 3) + 4 * (c.z & 3));
}

float Poly6(float r2)
{
    if (r2 >= _H2)
        return 0.0;

    float v = _H2 - r2;
    return v * v * v * _Poly6Norm;
}

[numthreads(64, 1, 1)]
void ClearDensity(uint3 id : SV_DispatchThreadID)
{
    uint index = id.x;
    uint count = (uint)(_KeyCount * 64);
    if (index >= count)
        return;

    _DensityRead[index] = 0.0;
    _DensityWrite[index] = 0.0;
}

[numthreads(64, 1, 1)]
void ProjectDensity(uint3 id : SV_DispatchThreadID)
{
    uint cellIndex = id.x;
    uint cellCount = (uint)(_KeyCount * 64);
    if (cellIndex >= cellCount)
        return;

    uint keyIndex = cellIndex / 64;
    uint local = cellIndex - keyIndex * 64;
    int3 localCoord = int3(local & 3, (local >> 2) & 3, (local >> 4) & 3);
    int3 coord = _Keys[keyIndex] * 4 + localCoord;
    float3 cellPos = (0.5 + (float3)coord) * _CellSize;

    float density = 0.0;
    for (int i = 0; i < _ParticleCount; i++)
    {
        float3 rel = _Particles[i].Position - _MinPos;
        density += Poly6(dot(cellPos - rel, cellPos - rel));
    }

    _DensityRead[cellIndex] = density;
}

[numthreads(64, 1, 1)]
void BlurDensity(uint3 id : SV_DispatchThreadID)
{
    uint cellIndex = id.x;
    uint cellCount = (uint)(_KeyCount * 64);
    if (cellIndex >= cellCount)
        return;

    _DensityWrite[cellIndex] = _DensityRead[cellIndex];
}

[numthreads(64, 1, 1)]
void MarchingCubes(uint3 id : SV_DispatchThreadID)
{
    uint keyIndex = id.x;
    if (keyIndex >= (uint)_KeyCount)
        return;

    _Stats[0] = (uint)_KeyCount;
}
```

This compiles and exercises the dispatch path. Full blur and MC generation are added in Task 7.

- [ ] **Step 2: Add GPU buffer fields**

In `SlimeGpuSurfacePipeline`, add:

```csharp
const int ParticleStride = 16;
const int SurfaceVertexStride = 24;

ComputeBuffer _particleBuffer;
ComputeBuffer _keyBuffer;
ComputeBuffer _densityReadBuffer;
ComputeBuffer _densityWriteBuffer;
ComputeBuffer _vertexBuffer;
ComputeBuffer _statsBuffer;

int _particleCapacity;
int _keyCapacity;
int _densityCapacity;
int _vertexCapacity;
```

- [ ] **Step 3: Add buffer allocation helpers**

Add methods:

```csharp
void EnsureBuffer(ref ComputeBuffer buffer, ref int capacity, int count, int stride, ComputeBufferType type)
{
    if (count <= 0)
        count = 1;

    if (buffer != null && capacity >= count)
        return;

    buffer?.Release();
    capacity = Mathf.NextPowerOfTwo(count);
    buffer = new ComputeBuffer(capacity, stride, type);
}

void EnsureBuffers(int particleCount, int keyCount)
{
    int densityCount = keyCount * SlimeConst.GridSize;
    int maxVertexCount = Mathf.Max(1024, keyCount * 125 * 15);

    EnsureBuffer(ref _particleBuffer, ref _particleCapacity, particleCount, ParticleStride, ComputeBufferType.Structured);
    EnsureBuffer(ref _keyBuffer, ref _keyCapacity, keyCount, 12, ComputeBufferType.Structured);
    EnsureBuffer(ref _densityReadBuffer, ref _densityCapacity, densityCount, 4, ComputeBufferType.Structured);
    EnsureBuffer(ref _densityWriteBuffer, ref _densityCapacity, densityCount, 4, ComputeBufferType.Structured);
    EnsureBuffer(ref _vertexBuffer, ref _vertexCapacity, maxVertexCount, SurfaceVertexStride, ComputeBufferType.Append);
    EnsureBuffer(ref _statsBuffer, ref _keyCapacity, 4, 4, ComputeBufferType.Structured);
}
```

Use separate capacity fields for density write and stats if the compiler flags reused `ref` arguments. The final code must not pass the same field by `ref` twice in one call expression.

- [ ] **Step 4: Release buffers in `Dispose`**

Replace `Dispose()` with:

```csharp
public void Dispose()
{
    _particleBuffer?.Release();
    _keyBuffer?.Release();
    _densityReadBuffer?.Release();
    _densityWriteBuffer?.Release();
    _vertexBuffer?.Release();
    _statsBuffer?.Release();

    _particleBuffer = null;
    _keyBuffer = null;
    _densityReadBuffer = null;
    _densityWriteBuffer = null;
    _vertexBuffer = null;
    _statsBuffer = null;
}
```

- [ ] **Step 5: Run Unity compile through surface tests**

Run `SlimeSurfaceBuilderTests`. Expected: PASS. No compute shader import errors in Editor log.

- [ ] **Step 6: Commit**

```powershell
git add Assets/Slime/Slime_Surface.compute Assets/Slime/SlimeGpuSurfacePipeline.cs
git commit -m "feat: add slime gpu surface compute buffers"
```

---

### Task 7: Implement GPU Dispatch And Async Readback Skeleton

**Files:**
- Modify: `Assets/Slime/SlimeGpuSurfacePipeline.cs`
- Modify: `Assets/Slime/Slime_Surface.compute`
- Modify: `Assets/Slime/Slime_SurfaceBuilder.cs`

- [ ] **Step 1: Add readback data struct**

In `SlimeGpuSurfacePipeline`, add:

```csharp
struct SurfaceVertexGpu
{
    public float3 Position;
    public float3 Normal;
}

struct PendingReadback
{
    public UnityEngine.Rendering.AsyncGPUReadbackRequest Vertices;
    public int FrameIssued;
}

readonly Queue<PendingReadback> _pendingReadbacks = new Queue<PendingReadback>(3);
SurfaceVertexGpu[] _readbackVertices = Array.Empty<SurfaceVertexGpu>();
int[] _meshIndices = Array.Empty<int>();
```

Add usings:

```csharp
using System.Collections.Generic;
using UnityEngine.Rendering;
```

- [ ] **Step 2: Dispatch clear/project/blur/MC kernels**

Replace `TrySchedule` body with:

```csharp
public bool TrySchedule(
    NativeArray<SlimeParticle> particles,
    NativeArray<int3> keys,
    NativeHashMap<int3, int> gridLut,
    float3 minPos,
    float isoLevel)
{
    if (!IsSupported || !particles.IsCreated || !keys.IsCreated || keys.Length == 0)
        return false;

    EnsureBuffers(particles.Length, keys.Length);

    _particleBuffer.SetData(particles);
    _keyBuffer.SetData(keys);
    _vertexBuffer.SetCounterValue(0);

    int clearKernel = _computeShader.FindKernel("ClearDensity");
    int projectKernel = _computeShader.FindKernel("ProjectDensity");
    int blurKernel = _computeShader.FindKernel("BlurDensity");
    int mcKernel = _computeShader.FindKernel("MarchingCubes");

    BindCommon(clearKernel, particles.Length, keys.Length, minPos, isoLevel);
    BindCommon(projectKernel, particles.Length, keys.Length, minPos, isoLevel);
    BindCommon(blurKernel, particles.Length, keys.Length, minPos, isoLevel);
    BindCommon(mcKernel, particles.Length, keys.Length, minPos, isoLevel);

    int densityGroups = Mathf.CeilToInt(keys.Length * SlimeConst.GridSize / 64f);
    int keyGroups = Mathf.CeilToInt(keys.Length / 64f);

    _computeShader.Dispatch(clearKernel, Mathf.Max(1, densityGroups), 1, 1);
    _computeShader.Dispatch(projectKernel, Mathf.Max(1, densityGroups), 1, 1);
    _computeShader.Dispatch(blurKernel, Mathf.Max(1, densityGroups), 1, 1);
    _computeShader.Dispatch(mcKernel, Mathf.Max(1, keyGroups), 1, 1);

    if (_pendingReadbacks.Count < 3)
    {
        _pendingReadbacks.Enqueue(new PendingReadback
        {
            Vertices = AsyncGPUReadback.Request(_vertexBuffer),
            FrameIssued = Time.frameCount,
        });
    }

    LastVisitedBlockCount = keys.Length;
    LastSkippedBlockCount = 0;
    return true;
}
```

Add `BindCommon`:

```csharp
void BindCommon(int kernel, int particleCount, int keyCount, float3 minPos, float isoLevel)
{
    _computeShader.SetBuffer(kernel, "_Particles", _particleBuffer);
    _computeShader.SetBuffer(kernel, "_Keys", _keyBuffer);
    _computeShader.SetBuffer(kernel, "_DensityRead", _densityReadBuffer);
    _computeShader.SetBuffer(kernel, "_DensityWrite", _densityWriteBuffer);
    _computeShader.SetBuffer(kernel, "_Vertices", _vertexBuffer);
    _computeShader.SetBuffer(kernel, "_Stats", _statsBuffer);

    _computeShader.SetInt("_ParticleCount", particleCount);
    _computeShader.SetInt("_KeyCount", keyCount);
    _computeShader.SetVector("_MinPos", new Vector4(minPos.x, minPos.y, minPos.z, 0f));
    _computeShader.SetFloat("_IsoLevel", isoLevel);
    _computeShader.SetFloat("_CellSize", SlimeConst.CellSize);
    _computeShader.SetFloat("_H2", SlimeConst.H2);
    _computeShader.SetFloat("_Poly6Norm", SlimeConst.Poly6Norm);
    _computeShader.SetFloat("_Scale", SlimeConst.Scale);
}
```

- [ ] **Step 3: Apply completed readback to mesh**

Replace `TryApplyCompletedReadback` with:

```csharp
public bool TryApplyCompletedReadback(Mesh mesh)
{
    if (mesh == null || _pendingReadbacks.Count == 0)
        return false;

    PendingReadback pending = _pendingReadbacks.Peek();
    if (!pending.Vertices.done)
        return false;

    _pendingReadbacks.Dequeue();
    LastReadbackLatencyFrames = Mathf.Max(0, Time.frameCount - pending.FrameIssued);

    if (pending.Vertices.hasError)
    {
        LastVertexCount = 0;
        return false;
    }

    NativeArray<SurfaceVertexGpu> data = pending.Vertices.GetData<SurfaceVertexGpu>();
    int vertexCount = data.Length;
    LastVertexCount = vertexCount;

    if (vertexCount == 0)
    {
        mesh.Clear();
        return true;
    }

    EnsureReadbackArrays(vertexCount);
    data.CopyTo(_readbackVertices);

    mesh.SetVertexBufferParams(
        vertexCount,
        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, stream: 0),
        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, stream: 1));
    mesh.SetIndexBufferParams(vertexCount, IndexFormat.UInt32);

    mesh.SetVertexBufferData(_readbackVertices, 0, 0, vertexCount, stream: 0);
    mesh.SetIndexBufferData(_meshIndices, 0, 0, vertexCount);
    mesh.subMeshCount = 1;
    mesh.SetSubMesh(0, new SubMeshDescriptor(0, vertexCount, MeshTopology.Triangles));
    mesh.RecalculateBounds();
    return true;
}
```

Add:

```csharp
void EnsureReadbackArrays(int vertexCount)
{
    if (_readbackVertices.Length < vertexCount)
        _readbackVertices = new SurfaceVertexGpu[Mathf.NextPowerOfTwo(vertexCount)];

    if (_meshIndices.Length < vertexCount)
    {
        _meshIndices = new int[Mathf.NextPowerOfTwo(vertexCount)];
        for (int i = 0; i < _meshIndices.Length; i++)
            _meshIndices[i] = i;
    }
}
```

- [ ] **Step 4: Fix vertex buffer stream write**

If Unity rejects writing `SurfaceVertexGpu[]` into stream 0/1 separately, split readback arrays:

```csharp
Vector3[] _positions = Array.Empty<Vector3>();
Vector3[] _normals = Array.Empty<Vector3>();
```

Fill them from `_readbackVertices` and call:

```csharp
mesh.SetVertexBufferData(_positions, 0, 0, vertexCount, stream: 0);
mesh.SetVertexBufferData(_normals, 0, 0, vertexCount, stream: 1);
```

Use this split-array path if the combined struct path fails compilation.

- [ ] **Step 5: Run tests**

Run full EditMode tests. Expected: PASS. GPU path may still generate zero vertices because MC kernel is still skeleton; CPU fallback remains available.

- [ ] **Step 6: Commit**

```powershell
git add Assets/Slime/SlimeGpuSurfacePipeline.cs Assets/Slime/Slime_Surface.compute Assets/Slime/Slime_SurfaceBuilder.cs
git commit -m "feat: schedule slime gpu surface readback"
```

---

### Task 8: Port Marching Cubes Logic To Compute Shader

**Files:**
- Modify: `Assets/Slime/Slime_Surface.compute`
- Modify: `Assets/Slime/SlimeGpuSurfacePipeline.cs`
- Modify: `Assets/Slime/Slime_SurfaceBuilder.cs`

- [ ] **Step 1: Copy lookup tables**

Copy these exact tables from `Assets/Slime/Slime_MarchingCubes.cs` into `Assets/Slime/Slime_Surface.compute`:

- `TriTable` as `static const int _TriTable[4096]`
- `EdgeVert` as `static const int2 _EdgeVert[12]`
- `VertPos` as `static const int3 _VertPos[8]`

Do not change table values or triangle winding. The CPU job currently swaps `e1` and `e2`; preserve that winding in HLSL generation.

- [ ] **Step 2: Implement density reads for sparse blocks**

Add helper:

```hlsl
float ReadDensityByKeyIndex(uint keyIndex, int localIndex)
{
    return _DensityWrite[keyIndex * 64 + localIndex];
}
```

For first GPU version, gather the 8x8x8 block scratch by scanning `_Keys` to find neighbor block indices:

```hlsl
int FindKeyIndex(int3 key)
{
    for (int i = 0; i < _KeyCount; i++)
    {
        int3 candidate = _Keys[i];
        if (all(candidate == key))
            return i;
    }
    return -1;
}
```

This is intentionally simple and correct for `GridNum = 768`; optimize to a GPU hash table after visual parity is proven.

- [ ] **Step 3: Implement blur kernel**

Replace skeleton `BlurDensity` body with 3x3x3 weighted blur matching `SlimeDensityField.GridBlurJob`:

```hlsl
float WeightForOffset(int3 o)
{
    return 1.0 - 0.5 * length((float3)o);
}
```

For each active cell, read the corresponding global coordinate from `_Keys[keyIndex] * 4 + localCoord`, scan neighbor blocks using `FindKeyIndex`, and write the weighted average to `_DensityWrite[cellIndex]`.

- [ ] **Step 4: Implement MC helpers**

Add HLSL helpers equivalent to CPU code:

```hlsl
int BlkIdx(int3 c)
{
    return c.x + 8 * (c.y + 8 * c.z);
}

float3 EdgePos(float block[512], int3 c, int edge, float isoLevel)
{
    int2 ev = _EdgeVert[edge];
    int3 c0 = c + _VertPos[ev.x];
    int3 c1 = c + _VertPos[ev.y];
    float v0 = block[BlkIdx(c0)];
    float v1 = block[BlkIdx(c1)];
    float denom = v1 - v0;
    float t = abs(denom) < 1e-6 ? 0.5 : (isoLevel - v0) / denom;
    t = clamp(t, 0.0, 1.0);
    return (float3)c0 + ((float3)c1 - (float3)c0) * t;
}
```

If HLSL compiler rejects local `float block[512]`, use `groupshared float _BlockScratch[512]` and one thread group per key with `numthreads(128,1,1)`. Fill scratch cooperatively, then use `GroupMemoryBarrierWithGroupSync()`.

- [ ] **Step 5: Implement MC append**

For each active block:

1. Gather neighbor density into 8x8x8 scratch.
2. Compute `maxDensity`; increment `_Stats[1]` for skipped blocks using `InterlockedAdd`.
3. For `x/y/z = 1..5`, compute config from 8 corners.
4. Preserve owner rule:

```hlsl
int3 gridCell = minCoord + mc;
int3 ownerKey = gridCell >> 2;
if (any(mc < 2) && FindKeyIndex(ownerKey) >= 0)
    continue;
```

5. Append three `SurfaceVertexGpu` values per generated triangle. Positions must be world space:

```hlsl
float3 world = (_MinPos + (p + (float3)minCoord) * _CellSize) * _Scale;
```

6. Compute face normal from triangle cross product. Use face normal for all three vertices in first GPU parity pass. The CPU trilinear gradient normal can be ported after geometry parity.

- [ ] **Step 6: Guard vertex overflow**

Before append, compare append counter capacity by passing `_MaxVertexCount` from C#. If the next triangle would exceed capacity, set `_Stats[2] = 1` and skip appending.

Add C# binding:

```csharp
_computeShader.SetInt("_MaxVertexCount", _vertexCapacity);
```

Add pipeline property:

```csharp
public bool LastOverflow { get; private set; }
```

Read stats in a separate `AsyncGPUReadback.Request(_statsBuffer)` queued with vertex readback, and set `LastOverflow`.

- [ ] **Step 7: Test manually in scene**

In the Unity editor:

1. Open the slime test scene containing `SlimeSimulator`.
2. Set `SlimeSurfaceBuilder.surfaceBuildMode` to `GpuAsyncReadback`.
3. Assign `Slime_Surface.compute` to `gpuSurfaceComputeShader`.
4. Enter Play Mode.
5. Confirm surface appears after 1-3 frames.
6. Press split key and confirm the surface follows both bodies.
7. Confirm `SlimeDoorSwitchOnContact` and `SlimeGameOverOnContact` still trigger from surface contact.

Expected: visual surface present; one-frame or two-frame surface latency acceptable; no CPU `MarchingCubesJob` spike while GPU path is supported.

- [ ] **Step 8: Commit**

```powershell
git add Assets/Slime/Slime_Surface.compute Assets/Slime/SlimeGpuSurfacePipeline.cs Assets/Slime/Slime_SurfaceBuilder.cs
git commit -m "feat: compute slime surface marching cubes on gpu"
```

---

### Task 9: Add Auto Fallback And Debug Stats

**Files:**
- Modify: `Assets/Slime/Slime_SurfaceBuilder.cs`
- Modify: `Assets/Slime/SlimeGpuSurfacePipeline.cs`
- Modify: `docs/slime-technical-overview.md`

- [ ] **Step 1: Expose GPU stats on builder**

Add properties to `SlimeSurfaceBuilder`:

```csharp
public bool LastGpuSurfaceSupported => _gpuPipeline != null && _gpuPipeline.IsSupported;
public bool LastGpuSurfaceOverflow => _gpuPipeline != null && _gpuPipeline.LastOverflow;
public int LastGpuSurfaceVertexCount => _gpuPipeline != null ? _gpuPipeline.LastVertexCount : 0;
public int LastGpuReadbackLatencyFrames => _gpuPipeline != null ? _gpuPipeline.LastReadbackLatencyFrames : 0;
```

- [ ] **Step 2: Make Auto mode fallback deterministic**

In `BuildSurface`, use this behavior:

- `Cpu`: always `BuildSurfaceCpu`.
- `GpuAsyncReadback`: schedule GPU if supported; if unsupported, run CPU for this frame.
- `Auto`: schedule GPU if supported and no overflow; otherwise run CPU for this frame.

The implementation must keep the last valid Mesh visible when a readback is pending.

- [ ] **Step 3: Document surface modes**

Append a short section to `docs/slime-technical-overview.md` under rendering/performance:

```markdown
### GPU Surface Reconstruction Mode

`SlimeSurfaceBuilder` supports CPU and GPU async-readback surface modes.
The GPU path keeps the authoritative simulation on CPU, uploads particles and sparse block metadata to a compute shader, reconstructs the surface on GPU, then asynchronously reads compact vertices back into the existing CPU `Mesh`.

This preserves gameplay scripts that read `CurrentSurfaceMesh`, including surface contact switches, at the cost of 1-3 frames of readback latency. Avoid synchronous `ComputeBuffer.GetData()` in gameplay; it stalls the main thread and defeats the migration.
```

- [ ] **Step 4: Run full tests**

Run the full EditMode test command.

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Slime/Slime_SurfaceBuilder.cs Assets/Slime/SlimeGpuSurfacePipeline.cs docs/slime-technical-overview.md
git commit -m "docs: document slime gpu surface fallback"
```

---

### Task 10: Profile And Acceptance Pass

**Files:**
- No required code files.
- Optional notes: `docs/slime-technical-overview.md`

- [ ] **Step 1: Capture CPU baseline**

In Play Mode with CPU mode:

1. Start profiler.
2. Split slime into at least two bodies.
3. Record `PlayerLoop -> FixedUpdate -> SlimeSurfaceBuilder.BuildSurface`.
4. Note peak CPU time and active block count from `LastMarchingCubeBlockCount`.

Expected: CPU mode reproduces current spike behavior.

- [ ] **Step 2: Capture GPU async-readback profile**

Switch to `GpuAsyncReadback`, assign compute shader, repeat the same split scenario.

Expected:

- `SlimeSurfaceBuilder.BuildSurface` no longer includes CPU `MarchingCubesJob` as the dominant cost.
- No synchronous GPU readback marker appears on main thread.
- Mesh appears with stable 1-3 frame latency.
- Surface-contact scripts still trigger.

- [ ] **Step 3: Record acceptance notes**

Add a brief note to `docs/slime-technical-overview.md` with measured values:

```markdown
### Measured GPU Surface Migration Result

- Scene:
- Particle count:
- Split state:
- CPU mode BuildSurface peak:
- GPU async mode BuildSurface peak:
- Readback latency:
- Surface contact scripts verified:
```

Fill each line with the measured value from the profiler session.

- [ ] **Step 4: Final full test run**

Run the full EditMode test command.

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add docs/slime-technical-overview.md
git commit -m "docs: record slime gpu surface profiling results"
```

---

## Self-Review

- Spec coverage: The plan covers shared surface-contact cleanup, `SlimeGameOverOnContact` null-audio cleanup, `SlimeDoorSwitchOnContact` reuse, `SlimeSurfaceBuilder` mode split, GPU compute surface pipeline, async readback, CPU Mesh preservation, fallback behavior, documentation, and profiling.
- Scope check: PBF, cohesion, split/absorb, controller movement, and collision event dispatch are intentionally excluded.
- Placeholder scan: No step relies on an unnamed file or unspecified API. The only mechanical copy is the Marching Cubes lookup tables from the existing source file, which is required to preserve parity and avoid retyping thousands of constants incorrectly.
- Type consistency: `SlimeSurfaceBuildMode`, `SlimeSurfaceContactUtility`, and `SlimeGpuSurfacePipeline` names are used consistently across tasks.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-08-slime-gpu-surface-migration-cleanup.md`. Two execution options:

1. Subagent-Driven (recommended) - dispatch a fresh subagent per task, review between tasks, fast iteration
2. Inline Execution - execute tasks in this session using executing-plans, batch execution with checkpoints

