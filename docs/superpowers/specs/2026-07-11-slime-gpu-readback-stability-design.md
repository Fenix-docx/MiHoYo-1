# Slime GPU Readback Stability Design

## Problem

The GPU surface path performs two asynchronous readbacks: statistics first, then
the exact vertex range. The current three-frame age limit covers both stages.
At high frame rates this is only a few milliseconds, so valid requests are often
classified as stale. That starts a full CPU surface build and can clear the last
valid mesh, causing both the frame-rate regression and visible flashing.

This timing behavior is separate from the global density field combining nearby
particle groups. Group-aware reconstruction remains a later, independent change.

## Considered Approaches

1. Increase the frame-age limit. This only moves the failure threshold and still
   converts variable GPU latency into unpredictable CPU work.
2. Keep the timeout but preserve the mesh during CPU fallback. This removes some
   flashing but retains the expensive fallback and competing CPU/GPU generations.
3. Keep the last successfully applied mesh while one GPU generation is pending.
   This is the selected approach because it makes latency affect visual freshness,
   not visibility or per-frame CPU cost.

## Design

`GpuAsyncReadback` and the compatibility `GpuDirectRender` mode will allow one
generation in flight. While it is pending, the builder returns after applying any
completed result and does not prepare sparse data or run the CPU surface path.

The readback pipeline will treat an error, suppression, or rejected generation as
an update failure. It will update diagnostics and retire the request, but will not
clear the current Unity `Mesh`. A successful zero-vertex result remains meaningful
and may clear the mesh. Invalid input with no particles/blocks also clears it.

The fixed frame-age CPU fallback fields and control flow will be removed. Readback
latency and generation diagnostics remain available in the HUD. The existing
single-pending-request limit provides back-pressure without producing extra work.

## Testing

Editor tests will cover the policy decisions independently of Unity GPU timing:

- an in-flight request keeps the previous surface rather than requesting CPU work;
- stale/error/suppressed updates preserve the previous mesh;
- a successful empty surface is still allowed to clear the mesh;
- the existing readback decoding and generation tests continue to pass.

Build verification will include the editor assembly build, the available test
command, and `git diff --check`. Unity compute-shader and Play Mode validation must
still be performed in the Editor because no Unity executable is available locally.
