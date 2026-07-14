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
        GpuDirectRender,
    }

    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SlimeSurfaceBuilder : MonoBehaviour
    {
        public const string GpuDirectSurfaceShaderName = "Slime/GPU Surface Draw";

        [Header("Surface")]
        public Material surfaceMaterial;
        [SerializeField] SlimeSurfaceBuildMode surfaceBuildMode = SlimeSurfaceBuildMode.Cpu;
        [SerializeField] ComputeShader gpuSurfaceComputeShader;
        [SerializeField] Material gpuDirectSurfaceMaterial;

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
        Material _runtimeGpuDirectSurfaceMaterial;
        int _autoOverflowFallbackFramesRemaining;

        public Mesh CurrentSurfaceMesh => _mesh;
        public bool HasSurface => _mesh != null && _mesh.vertexCount > 0;
        public int LastMarchingCubeBlockCount { get; private set; }
        public int LastSkippedMarchingCubeBlockCount { get; private set; }
        public SlimeSurfaceBuildMode SurfaceBuildMode => surfaceBuildMode;
        public bool LastGpuSurfaceSupported => _gpuPipeline != null && _gpuPipeline.IsSupported;
        public bool LastGpuSurfaceOverflow => _gpuPipeline != null && _gpuPipeline.LastOverflow;
        public bool HasPendingGpuReadback => _gpuPipeline != null && _gpuPipeline.HasPendingReadback;
        public bool HasPendingGpuDirectStatsReadback => _gpuPipeline != null && _gpuPipeline.HasPendingDirectStatsReadback;
        public SlimeGpuSurfaceScheduleStatus LastGpuScheduleStatus =>
            _gpuPipeline != null ? _gpuPipeline.LastScheduleStatus : SlimeGpuSurfaceScheduleStatus.NotScheduled;
        public string LastGpuScheduleFailure => _gpuPipeline != null ? _gpuPipeline.LastScheduleFailure : string.Empty;
        public bool LastGpuDirectDrawAttempted => _gpuPipeline != null && _gpuPipeline.LastDirectDrawAttempted;
        public bool LastGpuDirectDrawSubmitted => _gpuPipeline != null && _gpuPipeline.LastDirectDrawSubmitted;
        public int LastSurfaceVertexCount => _mesh != null ? _mesh.vertexCount : 0;
        public int LastGpuSurfaceVertexCount => _gpuPipeline != null ? _gpuPipeline.LastVertexCount : 0;
        public int LastGpuSurfaceVertexCapacity => _gpuPipeline != null ? _gpuPipeline.LastVertexBufferCapacity : 0;
        public long LastGpuRequestedVertexReadbackBytes => _gpuPipeline != null ? _gpuPipeline.LastRequestedVertexReadbackBytes : 0L;
        public long LastGpuUsedVertexReadbackBytes => _gpuPipeline != null ? _gpuPipeline.LastUsedVertexReadbackBytes : 0L;
        public int LastGpuReadbackLatencyFrames => _gpuPipeline != null ? _gpuPipeline.LastReadbackLatencyFrames : 0;
        public bool LastGpuReadbackSuppressed => _gpuPipeline != null && _gpuPipeline.LastReadbackSuppressed;
        public int LastGpuPendingReadbackAgeFrames => _gpuPipeline != null ? _gpuPipeline.PendingReadbackAgeFrames : 0;
        public int LastGpuScheduledReadbackGeneration => _gpuPipeline != null ? _gpuPipeline.LastScheduledReadbackGeneration : 0;
        public int LastGpuAppliedReadbackGeneration => _gpuPipeline != null ? _gpuPipeline.LastAppliedReadbackGeneration : 0;
        public int LastGpuDroppedReadbackGeneration => _gpuPipeline != null ? _gpuPipeline.LastDroppedReadbackGeneration : 0;

        public static bool IsGpuDirectSurfaceShaderName(string shaderName)
        {
            return shaderName == GpuDirectSurfaceShaderName;
        }

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
                _gpuPipeline?.ClearDirectSurface();
                _autoOverflowFallbackFramesRemaining = 0;
                BuildSurfaceCpu(particles, isoLevel);
                return;
            }

            _gpuPipeline?.ClearDirectSurface();
            TryApplyCompletedGpuReadback();

            if (ShouldUseGpuReadbackThisFrame() &&
                _gpuPipeline != null &&
                _gpuPipeline.HasPendingReadback)
                return;

            NativeArray<int3> keys;
            float3 minPos;
            float3 maxPos;
            if (!PrepareSparseSurfaceData(particles, out keys, out minPos, out maxPos))
            {
                LastMarchingCubeBlockCount = 0;
                LastSkippedMarchingCubeBlockCount = 0;
                DiscardPendingGpuReadbacks();
                _gpuPipeline?.ClearDirectSurface();
                _autoOverflowFallbackFramesRemaining = 0;
                _mesh.Clear();
                keys.Dispose();
                return;
            }

            try
            {
                bool scheduledGpu = ShouldUseGpuThisFrame() &&
                                    TryBuildSurfaceGpu(particles, keys, minPos, maxPos, isoLevel);

                if (!scheduledGpu)
                {
                    _gpuPipeline?.ClearDirectSurface();
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
                case SlimeSurfaceBuildMode.GpuDirectRender:
                    return true;
                default:
                    return false;
            }
        }

        bool ShouldUseGpuReadbackThisFrame()
        {
            if (_gpuPipeline == null || !_gpuPipeline.IsSupported)
                return false;

            return surfaceBuildMode == SlimeSurfaceBuildMode.GpuAsyncReadback ||
                   surfaceBuildMode == SlimeSurfaceBuildMode.GpuDirectRender ||
                   (surfaceBuildMode == SlimeSurfaceBuildMode.Auto &&
                    _autoOverflowFallbackFramesRemaining <= 0);
        }

        void DiscardPendingGpuReadbacks()
        {
            _gpuPipeline?.DiscardPendingReadbacks();
        }

        bool TryApplyCompletedGpuReadback()
        {
            if (_gpuPipeline == null || !_gpuPipeline.TryApplyCompletedReadback(_mesh))
                return false;

            LastMarchingCubeBlockCount = _gpuPipeline.LastVisitedBlockCount;
            LastSkippedMarchingCubeBlockCount = _gpuPipeline.LastSkippedBlockCount;

            if (surfaceBuildMode == SlimeSurfaceBuildMode.Auto)
                _autoOverflowFallbackFramesRemaining = _gpuPipeline.LastOverflow ? 1 : 0;

            return true;
        }

        bool TryBuildSurfaceGpu(
            NativeArray<SlimeParticle> particles,
            NativeArray<int3> keys,
            float3 minPos,
            float3 maxPos,
            float isoLevel)
        {
            return _gpuPipeline != null &&
                   _gpuPipeline.IsSupported &&
                   _gpuPipeline.TrySchedule(
                       particles,
                       keys,
                       _gridLut,
                       minPos,
                       maxPos,
                       isoLevel,
                       requestReadback: true);
        }

        bool PrepareSparseSurfaceData(
            NativeArray<SlimeParticle> particles,
            out NativeArray<int3> keys,
            out float3 minPos,
            out float3 maxPos)
        {
            // 1. Bounds
            new SlimeDensityField.CalcBoundsJob { Ps = particles, Bounds = _bounds }.Schedule().Complete();
            minPos = _bounds[0];
            maxPos = _bounds[1];

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
            float3 maxPos;
            if (!PrepareSparseSurfaceData(particles, out keys, out minPos, out maxPos))
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
            if (surfaceBuildMode != SlimeSurfaceBuildMode.Cpu)
                TryApplyCompletedGpuReadback();

            // Phase B: particles and surface are rendered in simulation/world space.
            // Do not let the GameObject Transform move the reconstructed surface.
            if (_mesh && _mesh.vertexCount > 0 && surfaceMaterial)
                Graphics.DrawMesh(_mesh, Matrix4x4.identity, surfaceMaterial, gameObject.layer);
        }

        Material ResolveGpuDirectSurfaceMaterial()
        {
            if (IsGpuDirectSurfaceMaterial(gpuDirectSurfaceMaterial))
                return gpuDirectSurfaceMaterial;

            if (IsGpuDirectSurfaceMaterial(_runtimeGpuDirectSurfaceMaterial))
                return _runtimeGpuDirectSurfaceMaterial;

            Shader shader = Shader.Find(GpuDirectSurfaceShaderName);
            if (shader == null)
                return null;

            _runtimeGpuDirectSurfaceMaterial = new Material(shader)
            {
                name = "Slime GPU Surface Runtime",
            };

            if (surfaceMaterial)
                CopyDirectSurfaceProperties(_runtimeGpuDirectSurfaceMaterial, surfaceMaterial);

            return _runtimeGpuDirectSurfaceMaterial;
        }

        static bool IsGpuDirectSurfaceMaterial(Material material)
        {
            return material != null &&
                   material.shader != null &&
                   IsGpuDirectSurfaceShaderName(material.shader.name);
        }

        static void CopyDirectSurfaceProperties(Material target, Material source)
        {
            if (target == null || source == null)
                return;

            if (source.HasProperty("_BaseColor") && target.HasProperty("_BaseColor"))
                target.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            else if (source.HasProperty("_Color") && target.HasProperty("_BaseColor"))
                target.SetColor("_BaseColor", source.GetColor("_Color"));

            if (source.HasProperty("_Opacity") && target.HasProperty("_Opacity"))
                target.SetFloat("_Opacity", source.GetFloat("_Opacity"));
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
            if (_runtimeGpuDirectSurfaceMaterial) Destroy(_runtimeGpuDirectSurfaceMaterial);
        }
    }
}
