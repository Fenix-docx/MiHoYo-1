using Unity.Collections;
using UnityEngine;
using Unity.Mathematics;
namespace Slime
{
    /// <summary>
    /// Debug particle renderer via Graphics.DrawMeshInstanced (CPU batches).
    /// Uses HDRP/Unlit — no custom shader required.
    /// </summary>
    [RequireComponent(typeof(SlimeSimulator))]
    public class SlimeRenderer : MonoBehaviour
    {
        [Header("Visual")]
        public float particleSize = 0.03f;
        public Color particleColor = Color.cyan;

        const int BATCH_SIZE = 1023; // GPU instancing limit

        NativeArray<SlimeParticle> _particles;
        Material _material;
        Mesh _mesh;
        Matrix4x4[] _batchMatrices;
        int _count;
        bool _ready;

        public void Initialise(int particleCount)
        {
            _count = Mathf.Max(0, particleCount);
            if (_batchMatrices == null || _batchMatrices.Length != BATCH_SIZE)
                _batchMatrices = new Matrix4x4[BATCH_SIZE];

            if (_material == null)
                _material = CreateMaterial();
            if (_mesh == null)
                _mesh = CreateCubeMesh();
            _ready = true;
        }

        public void SetParticles(NativeArray<SlimeParticle> particles)
        {
            _particles = particles;
            _count = particles.IsCreated ? particles.Length : 0;
        }

        void Update()
        {
            if (!_ready || !_particles.IsCreated || _material == null || _mesh == null)
                return;

            _material.color = particleColor;
            float s = particleSize;

            for (int batch = 0; batch < _count; batch += BATCH_SIZE)
            {
                int n = Mathf.Min(BATCH_SIZE, _count - batch);
                int validCount = 0;
                for (int i = 0; i < n; i++)
                {
                    float3 pos = _particles[batch + i].Position * SlimeConst.Scale;
                    if (!IsRenderable(pos))
                        continue;

                    _batchMatrices[validCount++] = Matrix4x4.TRS(
                        new Vector3(pos.x, pos.y, pos.z),
                        Quaternion.identity,
                        Vector3.one * s);
                }

                if (validCount > 0)
                    Graphics.DrawMeshInstanced(_mesh, 0, _material, _batchMatrices, validCount);
            }
        }

        void OnDestroy()
        {
            if (_mesh != null && _mesh.name == "SlimeParticleCube")
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
            }
            if (_material != null)
            {
                if (Application.isPlaying) Destroy(_material);
                else DestroyImmediate(_material);
            }
        }

        static Material CreateMaterial()
        {
            var shader = Shader.Find("HDRP/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader != null)
            {
                var mat = new Material(shader);
                mat.enableInstancing = true;
                mat.color = Color.cyan;
                Debug.Log($"SlimeRenderer: using '{shader.name}' (instanced).");
                return mat;
            }

            Debug.LogError("SlimeRenderer: no shader found!");
            return null;
        }

        static Mesh CreateCubeMesh()
        {
            var mesh = new Mesh { name = "SlimeParticleCube" };

            // Unit cube centred at origin
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3( 0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f,  0.5f, -0.5f), new Vector3(-0.5f,  0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f,  0.5f), new Vector3( 0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f,  0.5f,  0.5f), new Vector3(-0.5f,  0.5f,  0.5f),
            };

            mesh.triangles = new[]
            {
                0,2,1, 0,3,2,   // back
                4,5,6, 4,6,7,   // front
                0,1,5, 0,5,4,   // bottom
                2,3,7, 2,7,6,   // top
                0,4,7, 0,7,3,   // left
                1,2,6, 1,6,5,   // right
            };

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static bool IsRenderable(float3 pos)
        {
            return IsFinite(pos.x) && IsFinite(pos.y) && IsFinite(pos.z) && math.lengthsq(pos) < 1e10f;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
