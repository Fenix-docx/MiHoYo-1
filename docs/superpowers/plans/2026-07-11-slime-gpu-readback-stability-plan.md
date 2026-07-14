# Slime GPU Readback Stability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent asynchronous GPU surface latency from triggering CPU reconstruction or clearing the last valid slime mesh.

**Architecture:** Keep one GPU generation in flight and retain the last successfully applied Unity `Mesh` until a newer successful result arrives. Centralize the clear/preserve decision in a pure pipeline policy so Editor tests can verify it without depending on GPU timing.

**Tech Stack:** Unity 6, C#, Unity `AsyncGPUReadback`, NUnit Editor tests

---

### Task 1: Capture the stable-mesh policy in failing tests

**Files:**
- Modify: `Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs:341-455`

- [x] **Step 1: Replace stale-timeout expectations with pending and mesh-policy tests**

```csharp
[Test]
public void GpuSurfacePipelineOnlyClearsMeshForSuccessfulEmptySurface()
{
    Assert.That(SlimeGpuSurfacePipeline.ShouldClearMeshAfterReadback(
        readbackSucceeded: false, readbackSuppressed: false, vertexCount: 0), Is.False);
    Assert.That(SlimeGpuSurfacePipeline.ShouldClearMeshAfterReadback(
        readbackSucceeded: true, readbackSuppressed: true, vertexCount: 0), Is.False);
    Assert.That(SlimeGpuSurfacePipeline.ShouldClearMeshAfterReadback(
        readbackSucceeded: true, readbackSuppressed: false, vertexCount: 0), Is.True);
    Assert.That(SlimeGpuSurfacePipeline.ShouldClearMeshAfterReadback(
        readbackSucceeded: true, readbackSuppressed: false, vertexCount: 3), Is.False);
}

[Test]
public void SurfaceBuilderKeepsLastMeshWhileGpuReadbackIsPending()
{
    string builderSource = File.ReadAllText("Assets/Slime/Slime_SurfaceBuilder.cs");

    Assert.That(builderSource, Does.Contain("_gpuPipeline.HasPendingReadback)\n                return;"));
    Assert.That(builderSource, Does.Not.Contain("cpuFallbackWhileGpuReadbackStale"));
    Assert.That(builderSource, Does.Not.Contain("ShouldCpuFallbackForStaleGpuReadback"));
}
```

- [x] **Step 2: Build to verify RED**

Run: `dotnet build .\Assembly-CSharp-Editor.csproj`

Expected: FAIL because `ShouldClearMeshAfterReadback` does not exist and stale fallback remains in the builder.

### Task 2: Remove frame-age CPU fallback

**Files:**
- Modify: `Assets/Slime/Slime_SurfaceBuilder.cs:24-28,95-225`
- Test: `Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs`

- [x] **Step 1: Make an in-flight GPU request return immediately**

Remove `maxGpuReadbackAgeFrames`, `cpuFallbackWhileGpuReadbackStale`, and `ShouldCpuFallbackForStaleGpuReadback`. Replace the pending condition with:

```csharp
if (ShouldUseGpuReadbackThisFrame() &&
    _gpuPipeline != null &&
    _gpuPipeline.HasPendingReadback)
    return;
```

Call the pipeline without an age limit:

```csharp
if (_gpuPipeline == null || !_gpuPipeline.TryApplyCompletedReadback(_mesh))
    return false;
```

- [x] **Step 2: Build to verify the builder policy compiles**

Run: `dotnet build .\Assembly-CSharp-Editor.csproj`

Expected: `ShouldClearMeshAfterReadback` remains the only new missing member.

### Task 3: Preserve Mesh on failed or suppressed readbacks

**Files:**
- Modify: `Assets/Slime/SlimeGpuSurfacePipeline.cs:160-180,352-515`
- Test: `Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs`

- [x] **Step 1: Add the pure clear policy**

```csharp
public static bool ShouldClearMeshAfterReadback(
    bool readbackSucceeded,
    bool readbackSuppressed,
    int vertexCount)
{
    return readbackSucceeded && !readbackSuppressed && vertexCount == 0;
}
```

- [x] **Step 2: Remove stale classification from completed readbacks**

Change `TryApplyCompletedReadback` to accept only `Mesh mesh`. Remove `IsReadbackStale`, the stale branch, and its frame-age argument. Keep latency measurement as a diagnostic.

- [x] **Step 3: Apply the clear policy at terminal branches**

For stats/vertex errors and suppressed readbacks, update metrics and return without calling `mesh.Clear()`. For successful zero-vertex results, call `mesh.Clear()` because the empty surface is authoritative. Normal vertex results continue replacing the mesh buffers.

- [x] **Step 4: Run tests and build to verify GREEN**

Run: `dotnet build .\Assembly-CSharp-Editor.csproj`

Run: `dotnet test .\Assembly-CSharp-Editor.csproj --no-build`

Expected: exit code 0 for both commands.

### Task 4: Remove obsolete HUD stale-drop signal and verify

**Files:**
- Modify: `Assets/Slime/SlimeGpuSurfacePipeline.cs:130-180,474-486`
- Modify: `Assets/Slime/Slime_SurfaceBuilder.cs:55-75`
- Modify: `Assets/Slime/SlimeStatusHud.cs:185-205`

- [x] **Step 1: Remove the obsolete stale-only diagnostic**

Remove `LastReadbackDroppedAsStale`, `LastGpuReadbackDroppedAsStale`, and the HUD `X` suffix. Retain pending age, latency, scheduled/applied/dropped generations, and suppression diagnostics.

- [x] **Step 2: Run full available verification**

Run: `dotnet build .\Assembly-CSharp-Editor.csproj`

Run: `dotnet test .\Assembly-CSharp-Editor.csproj --no-build`

Run: `git diff --check`

Expected: all commands exit 0; build may retain pre-existing warnings.

- [x] **Step 3: Review the scoped diff**

Run: `git diff -- Assets/Slime/Slime_SurfaceBuilder.cs Assets/Slime/SlimeGpuSurfacePipeline.cs Assets/Slime/SlimeStatusHud.cs Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs`

Expected: only fixed-age fallback removal, stable-mesh readback handling, obsolete HUD cleanup, and their tests.
