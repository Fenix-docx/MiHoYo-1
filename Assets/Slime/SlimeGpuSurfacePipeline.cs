using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Slime
{
    public sealed class SlimeGpuSurfacePipeline : IDisposable
    {
        const int ParticleStride = 16;
        const int SurfaceVertexStride = 24;

        readonly ComputeShader _computeShader;

        ComputeBuffer _particleBuffer;
        ComputeBuffer _keyBuffer;
        ComputeBuffer _densityReadBuffer;
        ComputeBuffer _densityWriteBuffer;
        ComputeBuffer _vertexBuffer;
        ComputeBuffer _statsBuffer;

        int _particleCapacity;
        int _keyCapacity;
        int _densityReadCapacity;
        int _densityWriteCapacity;
        int _vertexCapacity;
        int _statsCapacity;

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
            EnsureBuffer(ref _densityReadBuffer, ref _densityReadCapacity, densityCount, 4, ComputeBufferType.Structured);
            EnsureBuffer(ref _densityWriteBuffer, ref _densityWriteCapacity, densityCount, 4, ComputeBufferType.Structured);
            EnsureBuffer(ref _vertexBuffer, ref _vertexCapacity, maxVertexCount, SurfaceVertexStride, ComputeBufferType.Append);
            EnsureBuffer(ref _statsBuffer, ref _statsCapacity, 4, 4, ComputeBufferType.Structured);
        }

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

            _particleCapacity = 0;
            _keyCapacity = 0;
            _densityReadCapacity = 0;
            _densityWriteCapacity = 0;
            _vertexCapacity = 0;
            _statsCapacity = 0;
        }
    }
}
