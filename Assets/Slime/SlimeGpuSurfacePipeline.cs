using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Slime
{
    public enum SlimeGpuSurfaceScheduleStatus
    {
        NotScheduled,
        Scheduled,
        Unsupported,
        InvalidInput,
        PendingReadbackLimit,
        PendingBufferResizeBlocked,
        MissingKernel,
    }

    public sealed class SlimeGpuSurfacePipeline : IDisposable
    {
        const int ParticleStride = 16;
        const int MaxPendingReadbackCount = 1;
        const int DrawArgsCount = 4;
        const int StatsCount = 4;
        const int StatsStride = 4;
        const float DensityQuantizeScale = 4096f;
        public const int SurfaceVertexStride = 24;
        public const int MaxAsyncReadbackVertexCount = 262144;
        static readonly int3 MissingHashKey = new int3(int.MinValue, int.MinValue, int.MinValue);

        readonly ComputeShader _computeShader;
        readonly Queue<PendingReadback> _pendingReadbacks = new Queue<PendingReadback>(MaxPendingReadbackCount);
        readonly uint[] _zeroStats = new uint[StatsCount];
        readonly uint[] _zeroDrawArgs = new uint[DrawArgsCount];

        ComputeBuffer _particleBuffer;
        ComputeBuffer _keyBuffer;
        ComputeBuffer _keyHashKeysBuffer;
        ComputeBuffer _keyHashValuesBuffer;
        ComputeBuffer _densityAccumBuffer;
        ComputeBuffer _densityReadBuffer;
        ComputeBuffer _densityWriteBuffer;
        ComputeBuffer _vertexBuffer;
        ComputeBuffer _statsBuffer;
        ComputeBuffer _drawArgsBuffer;

        int _particleCapacity;
        int _keyCapacity;
        int _keyHashKeysCapacity;
        int _keyHashValuesCapacity;
        int _activeKeyHashCapacity;
        int _densityAccumCapacity;
        int _densityReadCapacity;
        int _densityWriteCapacity;
        int _vertexCapacity;
        int _statsCapacity;
        int _drawArgsCapacity;

        int3[] _keyHashKeys = Array.Empty<int3>();
        int[] _keyHashValues = Array.Empty<int>();
        Vector3[] _positions = Array.Empty<Vector3>();
        Vector3[] _normals = Array.Empty<Vector3>();
        int[] _meshIndices = Array.Empty<int>();
        Bounds _directSurfaceBounds = new Bounds(Vector3.zero, Vector3.one);
        bool _hasDirectSurface;
        bool _directStatsReadbackPending;
        bool _disposed;
        int _directStatsFrameIssued;
        int _nextReadbackGeneration;

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
            public bool StatsDecoded;
            public ReadbackStatsSnapshot StatsSnapshot;
            public bool VertexRequestIssued;
            public int VertexCount;
            public int VertexReadbackBytes;
            public int Generation;
        }

        public readonly struct ReadbackStatsSnapshot
        {
            public ReadbackStatsSnapshot(
                int visitedBlockCount,
                int skippedBlockCount,
                bool overflow,
                int vertexCount)
            {
                VisitedBlockCount = visitedBlockCount;
                SkippedBlockCount = skippedBlockCount;
                Overflow = overflow;
                VertexCount = vertexCount;
            }

            public int VisitedBlockCount { get; }
            public int SkippedBlockCount { get; }
            public bool Overflow { get; }
            public int VertexCount { get; }
        }

        public SlimeGpuSurfacePipeline(ComputeShader computeShader)
        {
            _computeShader = computeShader;
            IsSupported = _computeShader != null && SystemInfo.supportsComputeShaders;
        }

        public bool IsSupported { get; }
        public bool HasPendingReadback => _pendingReadbacks.Count > 0;
        public bool HasPendingDirectStatsReadback => _directStatsReadbackPending;
        public bool CanDrawDirectSurface => _hasDirectSurface && _vertexBuffer != null && _drawArgsBuffer != null;
        public int MaxPendingReadbacks => MaxPendingReadbackCount;
        public SlimeGpuSurfaceScheduleStatus LastScheduleStatus { get; private set; } = SlimeGpuSurfaceScheduleStatus.NotScheduled;
        public string LastScheduleFailure { get; private set; } = string.Empty;
        public bool LastDirectDrawAttempted { get; private set; }
        public bool LastDirectDrawSubmitted { get; private set; }
        public int LastVertexCount { get; private set; }
        public int LastVertexBufferCapacity => _vertexCapacity;
        public long LastRequestedVertexReadbackBytes { get; private set; }
        public long LastUsedVertexReadbackBytes { get; private set; }
        public int LastReadbackLatencyFrames { get; private set; }
        public bool LastReadbackSuppressed { get; private set; }
        public bool LastOverflow { get; private set; }
        public int LastVisitedBlockCount { get; private set; }
        public int LastSkippedBlockCount { get; private set; }
        public int LastScheduledReadbackGeneration { get; private set; }
        public int LastAppliedReadbackGeneration { get; private set; }
        public int LastDroppedReadbackGeneration { get; private set; }
        public int LastScheduledReadbackFrame { get; private set; }
        public int LastAppliedReadbackFrame { get; private set; }
        public int LastDroppedReadbackFrame { get; private set; }
        public int PendingReadbackGeneration => _pendingReadbacks.Count > 0 ? _pendingReadbacks.Peek().Generation : 0;
        public int PendingReadbackAgeFrames =>
            _pendingReadbacks.Count > 0
                ? Mathf.Max(0, Time.frameCount - _pendingReadbacks.Peek().FrameIssued)
                : 0;

        public static long EstimateSurfaceVertexReadbackBytes(int vertexCount)
        {
            return vertexCount <= 0 ? 0L : (long)vertexCount * SurfaceVertexStride;
        }

        public static ReadbackStatsSnapshot DecodeReadbackStats(uint[] stats, int vertexCapacity)
        {
            if (stats == null)
                return new ReadbackStatsSnapshot(0, 0, false, 0);

            int visitedBlockCount = stats.Length > 0 ? ClampToInt(stats[0]) : 0;
            int skippedBlockCount = stats.Length > 1 ? ClampToInt(stats[1]) : 0;
            bool overflow = stats.Length > 2 && stats[2] != 0;
            int vertexCount = stats.Length > 3 ? ClampToInt(stats[3]) : 0;
            vertexCount = Mathf.Clamp(vertexCount, 0, Mathf.Max(0, vertexCapacity));
            return new ReadbackStatsSnapshot(visitedBlockCount, skippedBlockCount, overflow, vertexCount);
        }

        public static bool ShouldSuppressVertexReadback(ReadbackStatsSnapshot snapshot)
        {
            return snapshot.Overflow || snapshot.VertexCount > MaxAsyncReadbackVertexCount;
        }

        public static bool ShouldClearMeshAfterReadback(
            bool readbackSucceeded,
            bool readbackSuppressed,
            int vertexCount)
        {
            return readbackSucceeded && !readbackSuppressed && vertexCount == 0;
        }

        public static int CalculateKeyHashCapacity(int keyCount)
        {
            return Mathf.NextPowerOfTwo(Mathf.Max(1, keyCount * 2));
        }

        public static int ResolveActiveKeyHashCapacity(int keyCount, int allocatedCapacity)
        {
            int requiredCapacity = CalculateKeyHashCapacity(keyCount);
            return allocatedCapacity >= requiredCapacity ? requiredCapacity : 0;
        }

        public static int HashBlockKey(int3 key)
        {
            unchecked
            {
                uint x = (uint)key.x * 73856093u;
                uint y = (uint)key.y * 19349663u;
                uint z = (uint)key.z * 83492791u;
                uint hash = x ^ y ^ z;
                hash ^= hash >> 16;
                return (int)hash;
            }
        }

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
            float3 maxPos,
            float isoLevel,
            bool requestReadback)
        {
            if (!IsSupported)
            {
                SetScheduleStatus(SlimeGpuSurfaceScheduleStatus.Unsupported, "compute shader unsupported or missing");
                return false;
            }

            if (!particles.IsCreated || particles.Length == 0 ||
                !keys.IsCreated || keys.Length == 0 ||
                !gridLut.IsCreated || gridLut.Count != keys.Length)
            {
                SetScheduleStatus(SlimeGpuSurfaceScheduleStatus.InvalidInput, "missing particles, keys, or grid lookup");
                return false;
            }

            if (requestReadback && _pendingReadbacks.Count >= MaxPendingReadbackCount)
            {
                SetScheduleStatus(SlimeGpuSurfaceScheduleStatus.PendingReadbackLimit, "async readback still pending");
                return false;
            }

            if (!_computeShader.HasKernel("ClearDensity") ||
                !_computeShader.HasKernel("ProjectDensity") ||
                !_computeShader.HasKernel("FinalizeDensity") ||
                !_computeShader.HasKernel("BlurDensity") ||
                !_computeShader.HasKernel("MarchingCubes") ||
                !_computeShader.HasKernel("BuildDrawArgs"))
            {
                SetScheduleStatus(SlimeGpuSurfaceScheduleStatus.MissingKernel, "required compute kernel missing");
                return false;
            }

            if (_pendingReadbacks.Count > 0 && !HasBufferCapacity(particles.Length, keys.Length))
            {
                SetScheduleStatus(SlimeGpuSurfaceScheduleStatus.PendingBufferResizeBlocked, "pending readback blocks buffer resize");
                return false;
            }

            EnsureBuffers(particles.Length, keys.Length);

            _particleBuffer.SetData(particles);
            _keyBuffer.SetData(keys);
            UploadKeyHash(keys);
            _statsBuffer.SetData(_zeroStats);
            _drawArgsBuffer.SetData(_zeroDrawArgs);
            LastRequestedVertexReadbackBytes = 0L;
            LastUsedVertexReadbackBytes = 0L;
            LastReadbackSuppressed = false;
            _hasDirectSurface = !requestReadback;
            if (!requestReadback)
            {
                LastVertexCount = 0;
                LastReadbackLatencyFrames = 0;
                LastOverflow = false;
            }
            UpdateDirectSurfaceBounds(minPos, maxPos);

            int clearKernel = _computeShader.FindKernel("ClearDensity");
            int projectKernel = _computeShader.FindKernel("ProjectDensity");
            int finalizeKernel = _computeShader.FindKernel("FinalizeDensity");
            int blurKernel = _computeShader.FindKernel("BlurDensity");
            int marchingCubesKernel = _computeShader.FindKernel("MarchingCubes");
            int buildDrawArgsKernel = _computeShader.FindKernel("BuildDrawArgs");

            BindCommon(clearKernel, particles.Length, keys.Length, minPos, isoLevel);
            BindCommon(projectKernel, particles.Length, keys.Length, minPos, isoLevel);
            BindCommon(finalizeKernel, particles.Length, keys.Length, minPos, isoLevel);
            BindCommon(blurKernel, particles.Length, keys.Length, minPos, isoLevel);
            BindCommon(marchingCubesKernel, particles.Length, keys.Length, minPos, isoLevel);
            BindCommon(buildDrawArgsKernel, particles.Length, keys.Length, minPos, isoLevel);

            int densityGroups = Mathf.CeilToInt(keys.Length * SlimeConst.GridSize / 64f);
            int particleGroups = Mathf.CeilToInt(particles.Length / 64f);
            int keyGroups = Mathf.CeilToInt(keys.Length / 64f);

            _computeShader.Dispatch(clearKernel, densityGroups, 1, 1);
            _computeShader.Dispatch(projectKernel, particleGroups, 1, 1);
            _computeShader.Dispatch(finalizeKernel, densityGroups, 1, 1);
            _computeShader.Dispatch(blurKernel, densityGroups, 1, 1);
            _computeShader.Dispatch(marchingCubesKernel, keyGroups, 1, 1);
            _computeShader.Dispatch(buildDrawArgsKernel, 1, 1, 1);

            if (requestReadback)
            {
                int generation = ++_nextReadbackGeneration;
                LastScheduledReadbackGeneration = generation;
                LastScheduledReadbackFrame = Time.frameCount;
                _pendingReadbacks.Enqueue(new PendingReadback
                {
                    Stats = AsyncGPUReadback.Request(_statsBuffer),
                    FrameIssued = Time.frameCount,
                    VertexCapacity = _vertexCapacity,
                    Generation = generation,
                });
            }
            else
            {
                RequestDirectStatsReadback();
            }

            LastVisitedBlockCount = keys.Length;
            LastSkippedBlockCount = 0;
            SetScheduleStatus(SlimeGpuSurfaceScheduleStatus.Scheduled, string.Empty);
            return true;
        }

        public bool DrawDirectSurface(Material material, int layer)
        {
            LastDirectDrawAttempted = true;
            LastDirectDrawSubmitted = false;

            if (!CanDrawDirectSurface || material == null)
                return false;

            material.SetBuffer("_SurfaceVertices", _vertexBuffer);
            Graphics.DrawProceduralIndirect(
                material,
                _directSurfaceBounds,
                MeshTopology.Triangles,
                _drawArgsBuffer,
                0,
                null,
                null,
                ShadowCastingMode.On,
                true,
                layer);
            LastDirectDrawSubmitted = true;
            return true;
        }

        public void ClearDirectSurface()
        {
            _hasDirectSurface = false;
            if (_drawArgsBuffer != null)
                _drawArgsBuffer.SetData(_zeroDrawArgs);
        }

        public bool TryApplyCompletedReadback(Mesh mesh)
        {
            if (_pendingReadbacks.Count == 0)
                return false;

            while (_pendingReadbacks.Count > 0)
            {
                PendingReadback pending = _pendingReadbacks.Peek();
                if ((!pending.StatsDecoded && !pending.Stats.done) ||
                    (pending.VertexRequestIssued && !pending.Vertices.done))
                    return false;

                if (!pending.Discarded && mesh == null)
                    return false;

                _pendingReadbacks.Dequeue();
                LastReadbackLatencyFrames = Mathf.Max(0, Time.frameCount - pending.FrameIssued);

                if (pending.Discarded)
                {
                    MarkDroppedReadback(pending);
                    continue;
                }

                if (!pending.StatsDecoded)
                {
                    if (pending.Stats.hasError)
                    {
                        ResetReadbackMetrics();
                        MarkDroppedReadback(pending);
                        return false;
                    }

                    pending.StatsSnapshot = DecodeReadbackStats(pending.Stats.GetData<uint>(), pending.VertexCapacity);
                    pending.StatsDecoded = true;
                }

                ApplyReadbackStats(pending.StatsSnapshot);

                if (!pending.VertexRequestIssued)
                {
                    if (pending.StatsSnapshot.VertexCount == 0)
                    {
                        LastReadbackSuppressed = false;
                        MarkAppliedReadback(pending);
                        if (ShouldClearMeshAfterReadback(true, false, 0))
                            mesh.Clear();
                        return true;
                    }

                    if (ShouldSuppressVertexReadback(pending.StatsSnapshot))
                    {
                        LastReadbackSuppressed = true;
                        LastRequestedVertexReadbackBytes = 0L;
                        LastUsedVertexReadbackBytes = 0L;
                        MarkDroppedReadback(pending);
                        return true;
                    }

                    pending.VertexRequestIssued = true;
                    pending.VertexCount = pending.StatsSnapshot.VertexCount;
                    pending.VertexReadbackBytes = (int)EstimateSurfaceVertexReadbackBytes(pending.VertexCount);
                    LastRequestedVertexReadbackBytes = pending.VertexReadbackBytes;
                    pending.Vertices = AsyncGPUReadback.Request(_vertexBuffer, pending.VertexReadbackBytes, 0);
                    _pendingReadbacks.Enqueue(pending);
                    return false;
                }

                if (pending.Vertices.hasError)
                {
                    ResetReadbackMetrics();
                    MarkDroppedReadback(pending);
                    return false;
                }

                if (pending.VertexCount == 0)
                {
                    LastReadbackSuppressed = false;
                    MarkAppliedReadback(pending);
                    if (ShouldClearMeshAfterReadback(true, false, 0))
                        mesh.Clear();
                    return true;
                }

                NativeArray<SurfaceVertexGpu> vertices = pending.Vertices.GetData<SurfaceVertexGpu>();
                int vertexCount = Mathf.Min(pending.VertexCount, vertices.Length);
                LastVertexCount = vertexCount;
                LastUsedVertexReadbackBytes = EstimateSurfaceVertexReadbackBytes(vertexCount);
                LastReadbackSuppressed = false;

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
                MarkAppliedReadback(pending);
                return true;
            }

            return false;
        }

        void MarkAppliedReadback(PendingReadback pending)
        {
            LastAppliedReadbackGeneration = pending.Generation;
            LastAppliedReadbackFrame = Time.frameCount;
        }

        void MarkDroppedReadback(PendingReadback pending)
        {
            LastDroppedReadbackGeneration = pending.Generation;
            LastDroppedReadbackFrame = Time.frameCount;
        }

        static ReadbackStatsSnapshot DecodeReadbackStats(NativeArray<uint> stats, int vertexCapacity)
        {
            int visitedBlockCount = stats.Length > 0 ? ClampToInt(stats[0]) : 0;
            int skippedBlockCount = stats.Length > 1 ? ClampToInt(stats[1]) : 0;
            bool overflow = stats.Length > 2 && stats[2] != 0;
            int vertexCount = stats.Length > 3 ? ClampToInt(stats[3]) : 0;
            vertexCount = Mathf.Clamp(vertexCount, 0, Mathf.Max(0, vertexCapacity));
            return new ReadbackStatsSnapshot(visitedBlockCount, skippedBlockCount, overflow, vertexCount);
        }

        void ApplyReadbackStats(ReadbackStatsSnapshot snapshot)
        {
            LastVisitedBlockCount = snapshot.VisitedBlockCount;
            LastSkippedBlockCount = snapshot.SkippedBlockCount;
            LastOverflow = snapshot.Overflow;
            LastVertexCount = snapshot.VertexCount;
            LastUsedVertexReadbackBytes = 0L;
        }

        void ResetReadbackMetrics()
        {
            LastVertexCount = 0;
            LastUsedVertexReadbackBytes = 0L;
            LastReadbackSuppressed = false;
            LastOverflow = false;
            LastVisitedBlockCount = 0;
            LastSkippedBlockCount = 0;
        }

        void RequestDirectStatsReadback()
        {
            if (_directStatsReadbackPending || _statsBuffer == null)
                return;

            _directStatsReadbackPending = true;
            _directStatsFrameIssued = Time.frameCount;
            AsyncGPUReadback.Request(_statsBuffer, OnDirectStatsReadback);
        }

        void OnDirectStatsReadback(AsyncGPUReadbackRequest request)
        {
            _directStatsReadbackPending = false;
            if (_disposed)
                return;

            LastReadbackLatencyFrames = Mathf.Max(0, Time.frameCount - _directStatsFrameIssued);
            if (request.hasError)
            {
                ResetReadbackMetrics();
                return;
            }

            ApplyReadbackStats(DecodeReadbackStats(request.GetData<uint>(), _vertexCapacity));
        }

        void SetScheduleStatus(SlimeGpuSurfaceScheduleStatus status, string failure)
        {
            LastScheduleStatus = status;
            LastScheduleFailure = failure ?? string.Empty;
        }

        void BindCommon(int kernel, int particleCount, int keyCount, float3 minPos, float isoLevel)
        {
            _computeShader.SetBuffer(kernel, "_Particles", _particleBuffer);
            _computeShader.SetBuffer(kernel, "_Keys", _keyBuffer);
            _computeShader.SetBuffer(kernel, "_KeyHashKeys", _keyHashKeysBuffer);
            _computeShader.SetBuffer(kernel, "_KeyHashValues", _keyHashValuesBuffer);
            _computeShader.SetBuffer(kernel, "_DensityAccum", _densityAccumBuffer);
            _computeShader.SetBuffer(kernel, "_DensityRead", _densityReadBuffer);
            _computeShader.SetBuffer(kernel, "_DensityWrite", _densityWriteBuffer);
            _computeShader.SetBuffer(kernel, "_Vertices", _vertexBuffer);
            _computeShader.SetBuffer(kernel, "_Stats", _statsBuffer);
            _computeShader.SetBuffer(kernel, "_DrawArgs", _drawArgsBuffer);

            _computeShader.SetInt("_ParticleCount", particleCount);
            _computeShader.SetInt("_KeyCount", keyCount);
            _computeShader.SetInt("_KeyHashCapacity", _activeKeyHashCapacity);
            _computeShader.SetInt("_MaxVertexCount", _vertexCapacity);
            _computeShader.SetVector("_MinPos", new Vector4(minPos.x, minPos.y, minPos.z, 0f));
            _computeShader.SetFloat("_IsoLevel", isoLevel);
            _computeShader.SetFloat("_CellSize", SlimeConst.CellSize);
            _computeShader.SetFloat("_H2", SlimeConst.H2);
            _computeShader.SetFloat("_Poly6Norm", SlimeConst.Poly6Norm);
            _computeShader.SetFloat("_Scale", SlimeConst.Scale);
            _computeShader.SetFloat("_DensityQuantizeScale", DensityQuantizeScale);
            _computeShader.SetFloat("_InvDensityQuantizeScale", 1f / DensityQuantizeScale);
        }

        void UploadKeyHash(NativeArray<int3> keys)
        {
            int capacity = ResolveActiveKeyHashCapacity(keys.Length, _keyHashKeysCapacity);
            if (capacity <= 0)
                throw new InvalidOperationException("GPU key hash buffer is smaller than the active hash table.");

            _activeKeyHashCapacity = capacity;
            EnsureHashArrays(capacity);

            for (int i = 0; i < capacity; i++)
            {
                _keyHashKeys[i] = MissingHashKey;
                _keyHashValues[i] = -1;
            }

            int mask = capacity - 1;
            for (int i = 0; i < keys.Length; i++)
            {
                int3 key = keys[i];
                int slot = HashBlockKey(key) & mask;

                for (int probe = 0; probe < capacity; probe++)
                {
                    if (_keyHashValues[slot] < 0 || SameKey(_keyHashKeys[slot], key))
                    {
                        _keyHashKeys[slot] = key;
                        _keyHashValues[slot] = i;
                        break;
                    }

                    slot = (slot + 1) & mask;
                }
            }

            _keyHashKeysBuffer.SetData(_keyHashKeys, 0, 0, capacity);
            _keyHashValuesBuffer.SetData(_keyHashValues, 0, 0, capacity);
        }

        void EnsureHashArrays(int capacity)
        {
            if (_keyHashKeys.Length < capacity)
                _keyHashKeys = new int3[capacity];

            if (_keyHashValues.Length < capacity)
                _keyHashValues = new int[capacity];
        }

        static bool SameKey(int3 a, int3 b)
        {
            return a.x == b.x && a.y == b.y && a.z == b.z;
        }

        void UpdateDirectSurfaceBounds(float3 minPos, float3 maxPos)
        {
            Vector3 worldMin = (Vector3)(minPos * SlimeConst.Scale);
            Vector3 worldMax = (Vector3)(maxPos * SlimeConst.Scale);
            Vector3 size = worldMax - worldMin;
            if (!IsFinite(size) || size.sqrMagnitude < 1e-8f)
                size = Vector3.one * SlimeConst.Scale;

            _directSurfaceBounds = new Bounds((worldMin + worldMax) * 0.5f, size);
            _directSurfaceBounds.Expand(SlimeConst.H * SlimeConst.Scale * 4f);
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
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
            int keyHashCapacity = CalculateKeyHashCapacity(keyCount);

            return _particleBuffer != null && _particleCapacity >= particleCount &&
                   _keyBuffer != null && _keyCapacity >= keyCount &&
                   _keyHashKeysBuffer != null && _keyHashKeysCapacity >= keyHashCapacity &&
                   _keyHashValuesBuffer != null && _keyHashValuesCapacity >= keyHashCapacity &&
                   _densityAccumBuffer != null && _densityAccumCapacity >= densityCount &&
                   _densityReadBuffer != null && _densityReadCapacity >= densityCount &&
                   _densityWriteBuffer != null && _densityWriteCapacity >= densityCount &&
                   _vertexBuffer != null && _vertexCapacity >= maxVertexCount &&
                   _statsBuffer != null && _statsCapacity >= StatsCount &&
                   _drawArgsBuffer != null && _drawArgsCapacity >= DrawArgsCount;
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
            int keyHashCapacity = CalculateKeyHashCapacity(keyCount);

            EnsureBuffer(ref _particleBuffer, ref _particleCapacity, particleCount, ParticleStride, ComputeBufferType.Structured);
            EnsureBuffer(ref _keyBuffer, ref _keyCapacity, keyCount, 12, ComputeBufferType.Structured);
            EnsureBuffer(ref _keyHashKeysBuffer, ref _keyHashKeysCapacity, keyHashCapacity, 12, ComputeBufferType.Structured);
            EnsureBuffer(ref _keyHashValuesBuffer, ref _keyHashValuesCapacity, keyHashCapacity, 4, ComputeBufferType.Structured);
            EnsureBuffer(ref _densityAccumBuffer, ref _densityAccumCapacity, densityCount, 4, ComputeBufferType.Structured);
            EnsureBuffer(ref _densityReadBuffer, ref _densityReadCapacity, densityCount, 4, ComputeBufferType.Structured);
            EnsureBuffer(ref _densityWriteBuffer, ref _densityWriteCapacity, densityCount, 4, ComputeBufferType.Structured);
            EnsureBuffer(ref _vertexBuffer, ref _vertexCapacity, maxVertexCount, SurfaceVertexStride, ComputeBufferType.Structured);
            EnsureBuffer(ref _statsBuffer, ref _statsCapacity, StatsCount, StatsStride, ComputeBufferType.Structured);
            EnsureBuffer(ref _drawArgsBuffer, ref _drawArgsCapacity, DrawArgsCount, 4, ComputeBufferType.IndirectArguments);
        }

        void WaitForPendingReadbacks()
        {
            while (_pendingReadbacks.Count > 0)
            {
                PendingReadback pending = _pendingReadbacks.Dequeue();
                pending.Stats.WaitForCompletion();
                if (pending.VertexRequestIssued)
                    pending.Vertices.WaitForCompletion();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            DiscardPendingReadbacks();
            WaitForPendingReadbacks();
            _positions = Array.Empty<Vector3>();
            _normals = Array.Empty<Vector3>();
            _meshIndices = Array.Empty<int>();

            _particleBuffer?.Release();
            _keyBuffer?.Release();
            _keyHashKeysBuffer?.Release();
            _keyHashValuesBuffer?.Release();
            _densityAccumBuffer?.Release();
            _densityReadBuffer?.Release();
            _densityWriteBuffer?.Release();
            _vertexBuffer?.Release();
            _statsBuffer?.Release();
            _drawArgsBuffer?.Release();

            _particleBuffer = null;
            _keyBuffer = null;
            _keyHashKeysBuffer = null;
            _keyHashValuesBuffer = null;
            _densityAccumBuffer = null;
            _densityReadBuffer = null;
            _densityWriteBuffer = null;
            _vertexBuffer = null;
            _statsBuffer = null;
            _drawArgsBuffer = null;

            LastRequestedVertexReadbackBytes = 0L;
            LastUsedVertexReadbackBytes = 0L;
            LastReadbackSuppressed = false;
            _directStatsReadbackPending = false;
            _hasDirectSurface = false;
            _keyHashKeys = Array.Empty<int3>();
            _keyHashValues = Array.Empty<int>();

            _particleCapacity = 0;
            _keyCapacity = 0;
            _keyHashKeysCapacity = 0;
            _keyHashValuesCapacity = 0;
            _activeKeyHashCapacity = 0;
            _densityAccumCapacity = 0;
            _densityReadCapacity = 0;
            _densityWriteCapacity = 0;
            _vertexCapacity = 0;
            _statsCapacity = 0;
            _drawArgsCapacity = 0;
        }
    }
}
