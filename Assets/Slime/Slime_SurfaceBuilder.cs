using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static Unity.Mathematics.math;

namespace Slime
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SlimeSurfaceBuilder : MonoBehaviour
    {
        [Header("Surface")]
        public Material surfaceMaterial;

        [Header("Selection Outline")]
        public bool enableSelectionOutline = true;
        [Min(0f)] public float selectionOutlineWidth = 0.045f;
        public Color selectionOutlineColor = new Color(0.15f, 0.85f, 1f, 0.85f);
        [Min(0f)] public float selectionOutlineGlow = 3f;
        public Material selectionOutlineMaterial;

        NativeArray<float> _grid, _gridTemp;
        NativeHashMap<int3, int> _gridLut;
        NativeArray<float3> _bounds;
        Mesh _mesh;
        Mesh _selectionSurfaceMesh;
        Mesh _selectionOutlineMesh;
        MeshFilter _mf;
        MeshRenderer _mr;
        Material _runtimeSelectionOutlineMaterial;
        Material _runtimeSelectionOutlineSource;

        public Mesh CurrentSurfaceMesh => _mesh;
        public bool HasSurface => _mesh != null && _mesh.vertexCount > 0;
        public Mesh CurrentSelectionOutlineMesh => _selectionOutlineMesh;
        public bool HasSelectionOutline => _selectionOutlineMesh != null && _selectionOutlineMesh.vertexCount > 0;

        void Awake()
        {
            _mf = GetComponent<MeshFilter>();
            _mr = GetComponent<MeshRenderer>();
            if (!surfaceMaterial) surfaceMaterial = _mr.sharedMaterial;
            _mf.sharedMesh = null;
            _mr.enabled = false;
            _mesh = new Mesh { name = "SlimeSurface", indexFormat = IndexFormat.UInt32 };
            _selectionSurfaceMesh = new Mesh { name = "SlimeSelectedSurface", indexFormat = IndexFormat.UInt32 };
            _selectionOutlineMesh = new Mesh { name = "SlimeSelectedOutline", indexFormat = IndexFormat.UInt32 };

            int len = SlimeConst.GridSize * SlimeConst.GridNum;
            _grid = new NativeArray<float>(len, Allocator.Persistent);
            _gridTemp = new NativeArray<float>(len, Allocator.Persistent);
            _gridLut = new NativeHashMap<int3, int>(SlimeConst.GridNum, Allocator.Persistent);
            _bounds = new NativeArray<float3>(2, Allocator.Persistent);
        }

        public void BuildSurface(NativeArray<SlimeParticle> particles, float isoLevel)
        {
            BuildSurface(particles, isoLevel, selectedGroupId: -1);
        }

        public void BuildSurface(NativeArray<SlimeParticle> particles, float isoLevel, int selectedGroupId)
        {
            BuildMeshFromParticles(particles, isoLevel, _mesh);
            BuildSelectionOutline(particles, isoLevel, selectedGroupId);

            if (_mf.sharedMesh) _mf.sharedMesh = null;
        }

        void BuildMeshFromParticles(NativeArray<SlimeParticle> particles, float isoLevel, Mesh targetMesh)
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
            if (keys.Length == 0) { keys.Dispose(); targetMesh.Clear(); return; }

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
                    targetMesh.SetVertexBufferParams(verts.Length,
                        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, stream: 0),
                        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, stream: 1));
                    targetMesh.SetIndexBufferParams(tris.Length, IndexFormat.UInt32);
                    targetMesh.SetVertexBufferData(verts.AsArray(), 0, 0, verts.Length, stream: 0);
                    targetMesh.SetVertexBufferData(norms.AsArray(), 0, 0, norms.Length, stream: 1);
                    targetMesh.SetIndexBufferData(tris.AsArray(), 0, 0, tris.Length);
                    targetMesh.subMeshCount = 1;
                    targetMesh.SetSubMesh(0, new SubMeshDescriptor(0, tris.Length, MeshTopology.Triangles));
                    targetMesh.RecalculateBounds();
                }
                else targetMesh.Clear();
            }
            finally
            {
                keys.Dispose();
                verts.Dispose();
                norms.Dispose();
                tris.Dispose();
            }
        }

        void BuildSelectionOutline(NativeArray<SlimeParticle> particles, float isoLevel, int selectedGroupId)
        {
            if (!enableSelectionOutline || selectedGroupId < 0 || !particles.IsCreated)
            {
                ClearSelectionOutline();
                return;
            }

            var selectedParticles = new NativeList<SlimeParticle>(Allocator.TempJob);
            try
            {
                for (int i = 0; i < particles.Length; i++)
                {
                    SlimeParticle particle = particles[i];
                    if (particle.ID == selectedGroupId)
                        selectedParticles.Add(particle);
                }

                if (selectedParticles.Length == 0)
                {
                    ClearSelectionOutline();
                    return;
                }

                BuildMeshFromParticles(selectedParticles.AsArray(), isoLevel, _selectionSurfaceMesh);
                RebuildOutlineMesh(_selectionSurfaceMesh, _selectionOutlineMesh, selectionOutlineWidth);
            }
            finally
            {
                selectedParticles.Dispose();
            }
        }

        void RebuildOutlineMesh(Mesh sourceMesh, Mesh outlineMesh, float width)
        {
            if (sourceMesh == null || sourceMesh.vertexCount == 0 || sourceMesh.GetIndexCount(0) == 0 || width <= 0f)
            {
                outlineMesh.Clear();
                return;
            }

            Vector3[] vertices = sourceMesh.vertices;
            Vector3[] normals = sourceMesh.normals;
            int[] triangles = sourceMesh.triangles;

            if (vertices == null || normals == null || vertices.Length == 0 || normals.Length != vertices.Length ||
                triangles == null || triangles.Length == 0)
            {
                outlineMesh.Clear();
                return;
            }

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 normal = normals[i];
                if (!IsFinite(normal) || normal.sqrMagnitude < 1e-8f)
                    normal = Vector3.up;
                vertices[i] += normal.normalized * width;
            }

            outlineMesh.Clear();
            outlineMesh.indexFormat = sourceMesh.indexFormat;
            outlineMesh.vertices = vertices;
            outlineMesh.triangles = triangles;
            outlineMesh.normals = normals;
            outlineMesh.RecalculateBounds();
        }

        void ClearSelectionOutline()
        {
            if (_selectionSurfaceMesh != null)
                _selectionSurfaceMesh.Clear();
            if (_selectionOutlineMesh != null)
                _selectionOutlineMesh.Clear();
        }

        void LateUpdate()
        {
            // Phase B: particles and surface are rendered in simulation/world space.
            // Do not let the GameObject Transform move the reconstructed surface.
            if (_mesh && _mesh.vertexCount > 0 && surfaceMaterial)
                Graphics.DrawMesh(_mesh, Matrix4x4.identity, surfaceMaterial, gameObject.layer);

            Material outlineMaterial = GetSelectionOutlineMaterial();
            if (_selectionOutlineMesh && _selectionOutlineMesh.vertexCount > 0 && outlineMaterial)
                Graphics.DrawMesh(_selectionOutlineMesh, Matrix4x4.identity, outlineMaterial, gameObject.layer);
        }

        void OnDestroy()
        {
            if (_grid.IsCreated) _grid.Dispose();
            if (_gridTemp.IsCreated) _gridTemp.Dispose();
            if (_gridLut.IsCreated) _gridLut.Dispose();
            if (_bounds.IsCreated) _bounds.Dispose();
            if (_mesh) Destroy(_mesh);
            if (_selectionSurfaceMesh) Destroy(_selectionSurfaceMesh);
            if (_selectionOutlineMesh) Destroy(_selectionOutlineMesh);
            if (_runtimeSelectionOutlineMaterial) Destroy(_runtimeSelectionOutlineMaterial);
        }

        Material GetSelectionOutlineMaterial()
        {
            if (!enableSelectionOutline)
                return null;

            if (_runtimeSelectionOutlineMaterial == null || _runtimeSelectionOutlineSource != selectionOutlineMaterial)
            {
                if (_runtimeSelectionOutlineMaterial)
                {
                    if (Application.isPlaying) Destroy(_runtimeSelectionOutlineMaterial);
                    else DestroyImmediate(_runtimeSelectionOutlineMaterial);
                }

                _runtimeSelectionOutlineSource = selectionOutlineMaterial;
                _runtimeSelectionOutlineMaterial = selectionOutlineMaterial
                    ? new Material(selectionOutlineMaterial)
                    : CreateDefaultOutlineMaterial();
            }

            ConfigureOutlineMaterial(_runtimeSelectionOutlineMaterial);
            return _runtimeSelectionOutlineMaterial;
        }

        Material CreateDefaultOutlineMaterial()
        {
            Shader shader = Shader.Find("HDRP/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogError("SlimeSurfaceBuilder: no unlit shader found for selection outline.", this);
                return null;
            }

            Material material = new Material(shader) { name = "Slime Selection Outline (Runtime)" };
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        void ConfigureOutlineMaterial(Material material)
        {
            if (material == null)
                return;

            Color color = selectionOutlineColor;
            Color glow = new Color(
                color.r * selectionOutlineGlow,
                color.g * selectionOutlineGlow,
                color.b * selectionOutlineGlow,
                color.a);

            SetColorIfPresent(material, "_BaseColor", color);
            SetColorIfPresent(material, "_Color", color);
            SetColorIfPresent(material, "_EmissionColor", glow);
            SetColorIfPresent(material, "_EmissiveColor", glow);
            SetColorIfPresent(material, "_EmissiveColorLDR", glow);
            SetFloatIfPresent(material, "_EmissiveIntensity", selectionOutlineGlow);
            SetFloatIfPresent(material, "_UseEmissiveIntensity", 1f);

            SetFloatIfPresent(material, "_SurfaceType", 1f);
            SetFloatIfPresent(material, "_BlendMode", 0f);
            SetFloatIfPresent(material, "_AlphaSrcBlend", (float)BlendMode.SrcAlpha);
            SetFloatIfPresent(material, "_AlphaDstBlend", (float)BlendMode.OneMinusSrcAlpha);
            SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            SetFloatIfPresent(material, "_ZWrite", 0f);
            SetFloatIfPresent(material, "_TransparentZWrite", 0f);
            SetFloatIfPresent(material, "_ZTestTransparent", (float)CompareFunction.LessEqual);
            SetFloatIfPresent(material, "_ZTestDepthEqualForOpaque", (float)CompareFunction.LessEqual);

            int cullFront = (int)CullMode.Front;
            SetFloatIfPresent(material, "_Cull", cullFront);
            SetFloatIfPresent(material, "_CullMode", cullFront);
            SetFloatIfPresent(material, "_CullModeForward", cullFront);
            SetFloatIfPresent(material, "_OpaqueCullMode", cullFront);
            SetFloatIfPresent(material, "_TransparentCullMode", cullFront);

            material.renderQueue = (int)RenderQueue.Transparent;
        }

        static void SetColorIfPresent(Material material, string propertyName, Color value)
        {
            if (material.HasProperty(propertyName))
                material.SetColor(propertyName, value);
        }

        static void SetFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
                material.SetFloat(propertyName, value);
        }

        static bool IsFinite(Vector3 value)
        {
            return isfinite(value.x) && isfinite(value.y) && isfinite(value.z);
        }
    }
}
