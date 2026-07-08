using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Slime
{
    public sealed class SlimeGpuSurfacePipeline : IDisposable
    {
        const int ParticleStride = 16;
        const int SurfaceVertexStride = 24;
        const int MaxPendingReadbacks = 3;

        readonly ComputeShader _computeShader;
        readonly Queue<PendingReadback> _pendingReadbacks = new Queue<PendingReadback>(MaxPendingReadbacks);
        readonly uint[] _zeroStats = new uint[4];

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

        Vector3[] _positions = Array.Empty<Vector3>();
        Vector3[] _normals = Array.Empty<Vector3>();
        int[] _meshIndices = Array.Empty<int>();

        struct SurfaceVertexGpu
        {
            public float3 Position;
            public float3 Normal;
        }

        struct PendingReadback
        {
            public AsyncGPUReadbackRequest Vertices;
            public AsyncGPUReadbackRequest Stats;
            public int FrameIssued;
            public int VertexCapacity;
            public bool Discarded;
        }

        public SlimeGpuSurfacePipeline(ComputeShader computeShader)
        {
            _computeShader = computeShader;
            IsSupported = _computeShader != null && SystemInfo.supportsComputeShaders;
        }

        public bool IsSupported { get; }
        public bool HasPendingReadback => _pendingReadbacks.Count > 0;
        public int LastVertexCount { get; private set; }
        public int LastReadbackLatencyFrames { get; private set; }
        public bool LastOverflow { get; private set; }
        public int LastVisitedBlockCount { get; private set; }
        public int LastSkippedBlockCount { get; private set; }

        public void DiscardPendingReadbacks()
        {
            int count = _pendingReadbacks.Count;
            for (int i = 0; i < count; i++)
            {
                PendingReadback pending = _pendingReadbacks.Dequeue();
                pending.Discarded = true;
                _pendingReadbacks.Enqueue(pending);
            }
        }

        public bool TrySchedule(
            NativeArray<SlimeParticle> particles,
            NativeArray<int3> keys,
            NativeHashMap<int3, int> gridLut,
            float3 minPos,
            float isoLevel)
        {
            if (!IsSupported ||
                !particles.IsCreated || particles.Length == 0 ||
                !keys.IsCreated || keys.Length == 0 ||
                !gridLut.IsCreated || gridLut.Count() != keys.Length)
                return false;

            if (_pendingReadbacks.Count >= MaxPendingReadbacks)
                return false;

            if (!_computeShader.HasKernel("ClearDensity") ||
                !_computeShader.HasKernel("ProjectDensity") ||
                !_computeShader.HasKernel("BlurDensity") ||
                !_computeShader.HasKernel("MarchingCubes"))
                return false;

            if (_pendingReadbacks.Count > 0 && !HasBufferCapacity(particles.Length, keys.Length))
                return false;

            EnsureBuffers(particles.Length, keys.Length);

            _particleBuffer.SetData(particles);
            _keyBuffer.SetData(keys);
            _statsBuffer.SetData(_zeroStats);

            int clearKernel = _computeShader.FindKernel("ClearDensity");
            int projectKernel = _computeShader.FindKernel("ProjectDensity");
            int blurKernel = _computeShader.FindKernel("BlurDensity");
            int marchingCubesKernel = _computeShader.FindKernel("MarchingCubes");

            BindCommon(clearKernel, particles.Length, keys.Length, minPos, isoLevel);
            BindCommon(projectKernel, particles.Length, keys.Length, minPos, isoLevel);
            BindCommon(blurKernel, particles.Length, keys.Length, minPos, isoLevel);
            BindCommon(marchingCubesKernel, particles.Length, keys.Length, minPos, isoLevel);

            int densityGroups = Mathf.CeilToInt(keys.Length * SlimeConst.GridSize / 64f);
            int keyGroups = Mathf.CeilToInt(keys.Length / 64f);

            _computeShader.Dispatch(clearKernel, densityGroups, 1, 1);
            _computeShader.Dispatch(projectKernel, densityGroups, 1, 1);
            _computeShader.Dispatch(blurKernel, densityGroups, 1, 1);
            _computeShader.Dispatch(marchingCubesKernel, keyGroups, 1, 1);

            _pendingReadbacks.Enqueue(new PendingReadback
            {
                Vertices = AsyncGPUReadback.Request(_vertexBuffer),
                Stats = AsyncGPUReadback.Request(_statsBuffer),
                FrameIssued = Time.frameCount,
                VertexCapacity = _vertexCapacity,
            });

            LastVisitedBlockCount = keys.Length;
            LastSkippedBlockCount = 0;
            return true;
        }

        public bool TryApplyCompletedReadback(Mesh mesh)
        {
            if (_pendingReadbacks.Count == 0)
                return false;

            while (_pendingReadbacks.Count > 0)
            {
                PendingReadback pending = _pendingReadbacks.Peek();
                if (!pending.Vertices.done || !pending.Stats.done)
                    return false;

                if (!pending.Discarded && mesh == null)
                    return false;

                _pendingReadbacks.Dequeue();
                LastReadbackLatencyFrames = Mathf.Max(0, Time.frameCount - pending.FrameIssued);

                if (pending.Discarded)
                    continue;

                if (pending.Vertices.hasError || pending.Stats.hasError)
                {
                    LastVertexCount = 0;
                    LastOverflow = false;
                    LastVisitedBlockCount = 0;
                    LastSkippedBlockCount = 0;
                    return false;
                }

                NativeArray<uint> stats = pending.Stats.GetData<uint>();
                LastVisitedBlockCount = stats.Length > 0 ? ClampToInt(stats[0]) : 0;
                LastSkippedBlockCount = stats.Length > 1 ? ClampToInt(stats[1]) : 0;
                LastOverflow = stats.Length > 2 && stats[2] != 0;

                int vertexCount = stats.Length > 3 ? ClampToInt(stats[3]) : 0;
                vertexCount = Mathf.Clamp(vertexCount, 0, pending.VertexCapacity);
                LastVertexCount = vertexCount;

                if (vertexCount == 0)
                {
                    mesh.Clear();
                    return true;
                }

                NativeArray<SurfaceVertexGpu> vertices = pending.Vertices.GetData<SurfaceVertexGpu>();
                vertexCount = Mathf.Min(vertexCount, vertices.Length);
                LastVertexCount = vertexCount;

                EnsureReadbackArrays(vertexCount);
                for (int i = 0; i < vertexCount; i++)
                {
                    SurfaceVertexGpu vertex = vertices[i];
                    _positions[i] = new Vector3(vertex.Position.x, vertex.Position.y, vertex.Position.z);
                    _normals[i] = new Vector3(vertex.Normal.x, vertex.Normal.y, vertex.Normal.z);
                }

                mesh.SetVertexBufferParams(vertexCount,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, stream: 0),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, stream: 1));
                mesh.SetIndexBufferParams(vertexCount, IndexFormat.UInt32);
                mesh.SetVertexBufferData(_positions, 0, 0, vertexCount, stream: 0);
                mesh.SetVertexBufferData(_normals, 0, 0, vertexCount, stream: 1);
                mesh.SetIndexBufferData(_meshIndices, 0, 0, vertexCount);
                mesh.subMeshCount = 1;
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, vertexCount, MeshTopology.Triangles));
                mesh.RecalculateBounds();
                return true;
            }

            return false;
        }

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
            _computeShader.SetInt("_MaxVertexCount", _vertexCapacity);
            _computeShader.SetVector("_MinPos", new Vector4(minPos.x, minPos.y, minPos.z, 0f));
            _computeShader.SetFloat("_IsoLevel", isoLevel);
            _computeShader.SetFloat("_CellSize", SlimeConst.CellSize);
            _computeShader.SetFloat("_H2", SlimeConst.H2);
            _computeShader.SetFloat("_Poly6Norm", SlimeConst.Poly6Norm);
            _computeShader.SetFloat("_Scale", SlimeConst.Scale);
        }

        void EnsureReadbackArrays(int vertexCount)
        {
            if (_positions.Length >= vertexCount &&
                _normals.Length >= vertexCount &&
                _meshIndices.Length >= vertexCount)
                return;

            int capacity = Mathf.NextPowerOfTwo(Mathf.Max(1, vertexCount));
            _positions = new Vector3[capacity];
            _normals = new Vector3[capacity];
            _meshIndices = new int[capacity];

            for (int i = 0; i < _meshIndices.Length; i++)
                _meshIndices[i] = i;
        }

        static int ClampToInt(uint value)
        {
            return value > int.MaxValue ? int.MaxValue : (int)value;
        }

        bool HasBufferCapacity(int particleCount, int keyCount)
        {
            int densityCount = keyCount * SlimeConst.GridSize;
            int maxVertexCount = Mathf.Max(1024, keyCount * 125 * 15);

            return _particleBuffer != null && _particleCapacity >= particleCount &&
                   _keyBuffer != null && _keyCapacity >= keyCount &&
                   _densityReadBuffer != null && _densityReadCapacity >= densityCount &&
                   _densityWriteBuffer != null && _densityWriteCapacity >= densityCount &&
                   _vertexBuffer != null && _vertexCapacity >= maxVertexCount &&
                   _statsBuffer != null && _statsCapacity >= 4;
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
            EnsureBuffer(ref _vertexBuffer, ref _vertexCapacity, maxVertexCount, SurfaceVertexStride, ComputeBufferType.Structured);
            EnsureBuffer(ref _statsBuffer, ref _statsCapacity, 4, 4, ComputeBufferType.Structured);
        }

        void WaitForPendingReadbacks()
        {
            while (_pendingReadbacks.Count > 0)
            {
                PendingReadback pending = _pendingReadbacks.Dequeue();
                pending.Vertices.WaitForCompletion();
                pending.Stats.WaitForCompletion();
            }
        }

        public void Dispose()
        {
            DiscardPendingReadbacks();
            WaitForPendingReadbacks();
            _positions = Array.Empty<Vector3>();
            _normals = Array.Empty<Vector3>();
            _meshIndices = Array.Empty<int>();

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
