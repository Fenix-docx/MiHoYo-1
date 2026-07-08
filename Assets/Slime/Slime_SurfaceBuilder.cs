using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Slime
{
    public enum SlimeSurfaceBuildMode
    {
        Cpu,
        GpuAsyncReadback,
        Auto,
    }

    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SlimeSurfaceBuilder : MonoBehaviour
    {
        [Header("Surface")]
        public Material surfaceMaterial;
        [SerializeField] SlimeSurfaceBuildMode surfaceBuildMode = SlimeSurfaceBuildMode.Cpu;
        [SerializeField] ComputeShader gpuSurfaceComputeShader;

        NativeArray<float> _grid, _gridTemp;
        NativeArray<float> _blurBlockScratch;
        NativeArray<float> _mcBlockScratch;
        NativeArray<int> _mcStats;
        NativeHashMap<int3, int> _gridLut;
        NativeArray<float3> _bounds;
        SlimeGpuSurfacePipeline _gpuPipeline;
        Mesh _mesh;
        MeshFilter _mf;
        MeshRenderer _mr;
        int _autoOverflowFallbackFramesRemaining;

        public Mesh CurrentSurfaceMesh => _mesh;
        public bool HasSurface => _mesh != null && _mesh.vertexCount > 0;
        public int LastMarchingCubeBlockCount { get; private set; }
        public int LastSkippedMarchingCubeBlockCount { get; private set; }
        public SlimeSurfaceBuildMode SurfaceBuildMode => surfaceBuildMode;
        public bool LastGpuSurfaceSupported => _gpuPipeline != null && _gpuPipeline.IsSupported;
        public bool LastGpuSurfaceOverflow => _gpuPipeline != null && _gpuPipeline.LastOverflow;
        public int LastGpuSurfaceVertexCount => _gpuPipeline != null ? _gpuPipeline.LastVertexCount : 0;
        public int LastGpuReadbackLatencyFrames => _gpuPipeline != null ? _gpuPipeline.LastReadbackLatencyFrames : 0;

        void Awake()
        {
            _mf = GetComponent<MeshFilter>();
            _mr = GetComponent<MeshRenderer>();
            if (!surfaceMaterial) surfaceMaterial = _mr.sharedMaterial;
            _mf.sharedMesh = null;
            _mr.enabled = false;
            _mesh = new Mesh { name = "SlimeSurface", indexFormat = IndexFormat.UInt32 };

            int len = SlimeConst.GridSize * SlimeConst.GridNum;
            _grid = new NativeArray<float>(len, Allocator.Persistent);
            _gridTemp = new NativeArray<float>(len, Allocator.Persistent);
            _blurBlockScratch = new NativeArray<float>(SlimeDensityField.GridBlurJob.BlockScratchLength, Allocator.Persistent);
            _mcBlockScratch = new NativeArray<float>(SlimeMC.MarchingCubesJob.BlockScratchLength, Allocator.Persistent);
            _mcStats = new NativeArray<int>(SlimeMC.MarchingCubesJob.StatsLength, Allocator.Persistent);
            _gridLut = new NativeHashMap<int3, int>(SlimeConst.GridNum, Allocator.Persistent);
            _bounds = new NativeArray<float3>(2, Allocator.Persistent);
            _gpuPipeline = new SlimeGpuSurfacePipeline(gpuSurfaceComputeShader);
        }

        public void BuildSurface(NativeArray<SlimeParticle> particles, float isoLevel)
        {
            if (surfaceBuildMode == SlimeSurfaceBuildMode.Cpu)
            {
                DiscardPendingGpuReadbacks();
                _autoOverflowFallbackFramesRemaining = 0;
                BuildSurfaceCpu(particles, isoLevel);
                return;
            }

            if (_gpuPipeline != null && _gpuPipeline.TryApplyCompletedReadback(_mesh))
            {
                LastMarchingCubeBlockCount = _gpuPipeline.LastVisitedBlockCount;
                LastSkippedMarchingCubeBlockCount = _gpuPipeline.LastSkippedBlockCount;

                if (surfaceBuildMode == SlimeSurfaceBuildMode.Auto)
                    _autoOverflowFallbackFramesRemaining = _gpuPipeline.LastOverflow ? 1 : 0;
            }

            NativeArray<int3> keys;
            float3 minPos;
            if (!PrepareSparseSurfaceData(particles, out keys, out minPos))
            {
                LastMarchingCubeBlockCount = 0;
                LastSkippedMarchingCubeBlockCount = 0;
                DiscardPendingGpuReadbacks();
                _autoOverflowFallbackFramesRemaining = 0;
                _mesh.Clear();
                keys.Dispose();
                return;
            }

            try
            {
                bool scheduledGpu = ShouldUseGpuThisFrame() &&
                                    TryBuildSurfaceGpuAsync(particles, keys, minPos, isoLevel);

                if (!scheduledGpu)
                {
                    BuildSurfaceCpuPrepared(particles, keys, minPos, isoLevel);
                    DiscardPendingGpuReadbacks();

                    if (surfaceBuildMode == SlimeSurfaceBuildMode.Auto &&
                        _autoOverflowFallbackFramesRemaining > 0)
                        _autoOverflowFallbackFramesRemaining--;
                }
            }
            finally
            {
                keys.Dispose();
            }
        }

        bool ShouldUseGpuThisFrame()
        {
            if (_gpuPipeline == null || !_gpuPipeline.IsSupported)
                return false;

            switch (surfaceBuildMode)
            {
                case SlimeSurfaceBuildMode.GpuAsyncReadback:
                    return true;
                case SlimeSurfaceBuildMode.Auto:
                    return _autoOverflowFallbackFramesRemaining <= 0;
                default:
                    return false;
            }
        }

        void DiscardPendingGpuReadbacks()
        {
            _gpuPipeline?.DiscardPendingReadbacks();
        }

        bool TryBuildSurfaceGpuAsync(
            NativeArray<SlimeParticle> particles,
            NativeArray<int3> keys,
            float3 minPos,
            float isoLevel)
        {
            return _gpuPipeline != null &&
                   _gpuPipeline.IsSupported &&
                   _gpuPipeline.TrySchedule(particles, keys, _gridLut, minPos, isoLevel);
        }

        bool PrepareSparseSurfaceData(
            NativeArray<SlimeParticle> particles,
            out NativeArray<int3> keys,
            out float3 minPos)
        {
            // 1. Bounds
            new SlimeDensityField.CalcBoundsJob { Ps = particles, Bounds = _bounds }.Schedule().Complete();
            minPos = _bounds[0];

            // 2. Clear (IJob, single-threaded)
            new SlimeDensityField.ClearGridJob { Grid = _grid }.Schedule().Complete();

            // 3. Allocate sparse blocks
            _gridLut.Clear();
            new SlimeDensityField.AllocateBlockJob
            {
                Ps = particles, GridLut = _gridLut, MinPos = minPos,
                MaxBlocks = SlimeConst.GridNum,
            }.Schedule().Complete();

            keys = _gridLut.GetKeyArray(Allocator.TempJob);
            return keys.Length > 0;
        }

        void BuildSurfaceCpu(NativeArray<SlimeParticle> particles, float isoLevel)
        {
            NativeArray<int3> keys;
            float3 minPos;
            if (!PrepareSparseSurfaceData(particles, out keys, out minPos))
            {
                LastMarchingCubeBlockCount = 0;
                LastSkippedMarchingCubeBlockCount = 0;
                DiscardPendingGpuReadbacks();
                keys.Dispose();
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

        void BuildSurfaceCpuPrepared(
            NativeArray<SlimeParticle> particles,
            NativeArray<int3> keys,
            float3 minPos,
            float isoLevel)
        {
            // 4. Density projection
            new SlimeDensityField.DensityProjectionJob
            {
                Ps = particles, GridLut = _gridLut, Grid = _grid, MinPos = minPos
            }.Schedule().Complete();

            // 5. Blur (single-threaded)
            new SlimeDensityField.GridBlurJob
            {
                Keys = keys, GridLut = _gridLut, GridRead = _grid, GridWrite = _gridTemp,
                BlockScratch = _blurBlockScratch,
            }.Schedule().Complete();

            var tmp = _grid; _grid = _gridTemp; _gridTemp = tmp;

            // 6. Marching Cubes
            var verts = new NativeList<float3>(4096, Allocator.TempJob);
            var norms = new NativeList<float3>(4096, Allocator.TempJob);
            var tris = new NativeList<int>(4096, Allocator.TempJob);

            try
            {
                new SlimeMC.MarchingCubesJob
                {
                    Keys = keys, GridLut = _gridLut, Grid = _grid,
                    BlockScratch = _mcBlockScratch, Stats = _mcStats,
                    VertsOut = verts, NormsOut = norms, TrisOut = tris,
                    IsoLevel = isoLevel,
                    Origin = minPos,
                    CellScale = SlimeConst.Scale,
                }.Schedule().Complete();
                LastMarchingCubeBlockCount = _mcStats.Length > 0 ? _mcStats[0] : 0;
                LastSkippedMarchingCubeBlockCount = _mcStats.Length > 1 ? _mcStats[1] : 0;

                // 7. Write mesh
                if (verts.Length > 0 && tris.Length > 0)
                {
                    _mesh.SetVertexBufferParams(verts.Length,
                        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, stream: 0),
                        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, stream: 1));
                    _mesh.SetIndexBufferParams(tris.Length, IndexFormat.UInt32);
                    _mesh.SetVertexBufferData(verts.AsArray(), 0, 0, verts.Length, stream: 0);
                    _mesh.SetVertexBufferData(norms.AsArray(), 0, 0, norms.Length, stream: 1);
                    _mesh.SetIndexBufferData(tris.AsArray(), 0, 0, tris.Length);
                    _mesh.subMeshCount = 1;
                    _mesh.SetSubMesh(0, new SubMeshDescriptor(0, tris.Length, MeshTopology.Triangles));
                    _mesh.RecalculateBounds();
                }
                else _mesh.Clear();
            }
            finally
            {
                verts.Dispose();
                norms.Dispose();
                tris.Dispose();
            }

            if (_mf.sharedMesh) _mf.sharedMesh = null;
        }

        void LateUpdate()
        {
            // Phase B: particles and surface are rendered in simulation/world space.
            // Do not let the GameObject Transform move the reconstructed surface.
            if (_mesh && _mesh.vertexCount > 0 && surfaceMaterial)
                Graphics.DrawMesh(_mesh, Matrix4x4.identity, surfaceMaterial, gameObject.layer);
        }

        void OnDestroy()
        {
            _gpuPipeline?.Dispose();
            _gpuPipeline = null;
            if (_grid.IsCreated) _grid.Dispose();
            if (_gridTemp.IsCreated) _gridTemp.Dispose();
            if (_blurBlockScratch.IsCreated) _blurBlockScratch.Dispose();
            if (_mcBlockScratch.IsCreated) _mcBlockScratch.Dispose();
            if (_mcStats.IsCreated) _mcStats.Dispose();
            if (_gridLut.IsCreated) _gridLut.Dispose();
            if (_bounds.IsCreated) _bounds.Dispose();
            if (_mesh) Destroy(_mesh);
        }
    }
}
