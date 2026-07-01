using System.Collections.Generic;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Slime
{
    /// <summary>
    /// Global constants, kernel functions, and spatial-hash utilities.
    /// All lengths are in simulation space. The slime body is particle-driven:
    /// move controllers/particles, not the GameObject Transform.
    /// </summary>
    public static class SlimeConst
    {
        // ── Space scaling ──
        public const float Scale = 0.1f;       // Unity world = sim * Scale
        public const float InvScale = 10f;      // sim = Unity world * InvScale

        // ── Particle / simulation (simulation-space units) ──
        public const float H = 1.0f;            // smoothing / constraint radius
        public const float H2 = H * H;          // 1.0
        public const float CellSize = 0.5f;     // spatial-hash cell edge = H / 2
        public const float Mass = 1.0f;         // particle mass
        public const float TargetDensity = 1.5f;// rest density
        public const float PredictStep = 0.02f; // position-prediction sub-step
        public const float DeltaTime = 0.02f;   // physics delta-time

        // ── Particle count ──
        public const int Width = 16;            // hemisphere diameter in grid cells
        public const int Num = Width * Width * Width / 2; // 2048

        // ── Density-field / sparse block ──
        public const int GridSize = 4 * 4 * 4;  // cells per block (64)
        public const int GridNum = 768;          // max active blocks

        // ── Kernel pre-computed constants (h = 1 simplifies several) ──
        // Poly6   coeff = 315 / (64π h⁹)  = 315 / (64π)  because h=1
        // Spiky    gradient norm = -45 / (π h⁶)  = -45/π
        // Spiky    kernel norm   = 15 / (π h⁶)   = 15/π
        public const float Poly6Norm = 315f / (64f * PI);    // ≈ 1.566
        public const float SpikyGradNorm = -45f / PI;        // ≈ -14.324
        public const float SpikyKernelNorm = 15f / PI;       // ≈ 4.775

        // Tensile-instability (s_corr) parameters
        public const float TensileDq = 0.25f * H;           // 0.25
        public const float TensileK = 0.1f;

        /// <summary>Coordinate → cell index.</summary>
        public static int3 GetCoord(float3 pos)
        {
            return (int3)floor(pos / H);
        }

        /// <summary>Pack 3-D cell coordinate into a single integer key.
        /// 10 bits per component → [-512, 511]. No collisions within that range.</summary>
        public static int GetKey(int3 coord)
        {
            unchecked
            {
                int key = coord.x & 1023;
                key = (key << 10) | (coord.y & 1023);
                key = (key << 10) | (coord.z & 1023);
                return key;
            }
        }

        // ── Kernel functions ──

        /// <summary>Poly6 kernel: W(r²). Returns 0 when r² ≥ H².</summary>
        public static float SmoothingKernelPoly6(float r2)
        {
            if (r2 < H2)
            {
                float v = H2 - r2;
                return v * v * v * Poly6Norm;
            }
            return 0f;
        }

        /// <summary>Poly6 kernel with arbitrary support radius.</summary>
        public static float SmoothingKernelPoly6(float dst, float radius)
        {
            if (dst < radius)
            {
                float scale = 315f / (64f * PI * pow(radius, 9));
                float v = radius * radius - dst * dst;
                return v * v * v * scale;
            }
            return 0f;
        }

        /// <summary>Spiky gradient magnitude ∂W/∂r. Returns 0 when r > H.</summary>
        public static float DerivativeSpikyPow3(float r)
        {
            if (r <= H)
            {
                float v = H - r;
                return -v * v * 3f * SpikyKernelNorm;
            }
            return 0f;
        }

        /// <summary>Spiky gradient magnitude with arbitrary radius.</summary>
        public static float DerivativeSpikyPow3(float dst, float radius)
        {
            if (dst <= radius)
            {
                float scale = 45f / (pow(radius, 6) * PI);
                float v = radius - dst;
                return -v * v * scale;
            }
            return 0f;
        }

        /// <summary>Spiky kernel value W(r).</summary>
        public static float SpikyKernelPow3(float r)
        {
            if (r < H)
            {
                float v = H - r;
                return v * v * v * SpikyKernelNorm;
            }
            return 0f;
        }

        /// <summary>Spiky kernel value with arbitrary radius.</summary>
        public static float SpikyKernelPow3(float dst, float radius)
        {
            if (dst < radius)
            {
                float scale = 15f / (PI * pow(radius, 6));
                float v = radius - dst;
                return v * v * v * scale;
            }
            return 0f;
        }

        /// <summary>Tensile-instability correction s_corr.</summary>
        public static float Scorr(float r2)
        {
            float w = SmoothingKernelPoly6(r2);
            float wDq = SmoothingKernelPoly6(TensileDq * TensileDq);
            if (wDq < 1e-10f) return 0f;
            float corr = w / wDq;
            return -TensileK * corr * corr * corr * corr;
        }
    }

    /// <summary>Stable sort comparator for (hash, index) pairs.</summary>
    public struct Int2Comparer : IComparer<int2>
    {
        public int Compare(int2 a, int2 b)
        {
            return a.x.CompareTo(b.x);
        }
    }
}
