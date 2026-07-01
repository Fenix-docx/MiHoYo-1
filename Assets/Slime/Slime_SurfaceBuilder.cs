using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Slime
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SlimeSurfaceBuilder : MonoBehaviour
    {
        [Header("Surface")]
        public Material surfaceMaterial;

        NativeArray<float> _grid, _gridTemp;
        NativeHashMap<int3, int> _gridLut;
        NativeArray<float3> _bounds;
        Mesh _mesh;
        MeshFilter _mf;
        MeshRenderer _mr;

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
            _gridLut = new NativeHashMap<int3, int>(SlimeConst.GridNum, Allocator.Persistent);
            _bounds = new NativeArray<float3>(2, Allocator.Persistent);
        }

        public void BuildSurface(NativeArray<SlimeParticle> particles, float isoLevel)
        {
            // 1. Bounds
            new SlimeDensityField.CalcBoundsJob { Ps = particles, Bounds = _bounds }.Schedule().Complete();
            float3 minPos = _bounds[0];

            // 2. Clear (IJob, single-threaded)
            new SlimeDensityField.ClearGridJob { Grid = _grid }.Schedule().Complete();

            // 3. Allocate sparse blocks
            _gridLut.Clear();
            new SlimeDensityField.AllocateBlockJob
            {
                Ps = particles, GridLut = _gridLut, MinPos = minPos,
                MaxBlocks = SlimeConst.GridNum,
            }.Schedule().Complete();

            var keys = _gridLut.GetKeyArray(Allocator.TempJob);
            if (keys.Length == 0) { keys.Dispose(); _mesh.Clear(); return; }

            // 4. Density projection
            new SlimeDensityField.DensityProjectionJob
            {
                Ps = particles, GridLut = _gridLut, Grid = _grid, MinPos = minPos
            }.Schedule().Complete();

            // 5. Blur (single-threaded)
            new SlimeDensityField.GridBlurJob
            {
                Keys = keys, GridLut = _gridLut, GridRead = _grid, GridWrite = _gridTemp
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
                    VertsOut = verts, NormsOut = norms, TrisOut = tris,
                    IsoLevel = isoLevel,
                    Origin = minPos,
                    CellScale = SlimeConst.Scale,
                }.Schedule().Complete();

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
                keys.Dispose();
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
            if (_grid.IsCreated) _grid.Dispose();
            if (_gridTemp.IsCreated) _gridTemp.Dispose();
            if (_gridLut.IsCreated) _gridLut.Dispose();
            if (_bounds.IsCreated) _bounds.Dispose();
            if (_mesh) Destroy(_mesh);
        }
    }
}
