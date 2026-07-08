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
        public int LastVisitedBlockCount { get; private set; }
        public int LastSkippedBlockCount { get; private set; }

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
            LastVisitedBlockCount = 0;
            LastSkippedBlockCount = 0;
            return false;
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
