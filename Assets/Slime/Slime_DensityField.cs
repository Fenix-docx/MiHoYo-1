using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Slime
{
    public static class SlimeDensityField
    {
        [BurstCompile]
        public struct CalcBoundsJob : IJob
        {
            [ReadOnly] public NativeArray<SlimeParticle> Ps;
            [WriteOnly] public NativeArray<float3> Bounds;

            public void Execute()
            {
                float3 mn = new float3(float.MaxValue);
                float3 mx = new float3(float.MinValue);
                for (int i = 0; i < Ps.Length; i++)
                {
                    float3 p = Ps[i].Position;
                    mn = min(mn, p); mx = max(mx, p);
                }
                Bounds[0] = floor(mn / SlimeConst.CellSize) * SlimeConst.CellSize - SlimeConst.CellSize;
                Bounds[1] = ceil(mx / SlimeConst.CellSize) * SlimeConst.CellSize + SlimeConst.CellSize;
            }
        }

        [BurstCompile]
        public struct ClearGridJob : IJob
        {
            public NativeArray<float> Grid;
            public void Execute() { for (int i = 0; i < Grid.Length; i++) Grid[i] = 0f; }
        }

        [BurstCompile]
        public struct AllocateBlockJob : IJob
        {
            [ReadOnly] public NativeArray<SlimeParticle> Ps;
            public NativeHashMap<int3, int> GridLut;
            public float3 MinPos;
            public int MaxBlocks;

            public void Execute()
            {
                int ptr = 0;
                for (int i = 0; i < Ps.Length && ptr < MaxBlocks; i++)
                {
                    float3 rel = Ps[i].Position - MinPos;
                    int3 coord = (int3)floor(rel / SlimeConst.CellSize);
                    int3 bMin = (coord - 2) >> 2;
                    int3 bMax = (coord + 2) >> 2;
                    for (int bz = bMin.z; bz <= bMax.z && ptr < MaxBlocks; bz++)
                    for (int by = bMin.y; by <= bMax.y && ptr < MaxBlocks; by++)
                    for (int bx = bMin.x; bx <= bMax.x && ptr < MaxBlocks; bx++)
                    {
                        int3 key = new int3(bx, by, bz);
                        if (GridLut.ContainsKey(key)) continue;
                        GridLut.TryAdd(key, ptr * SlimeConst.GridSize);
                        ptr++;
                    }
                }
            }
        }

        [BurstCompile]
        public struct DensityProjectionJob : IJob
        {
            [ReadOnly] public NativeArray<SlimeParticle> Ps;
            [ReadOnly] public NativeHashMap<int3, int> GridLut;
            public NativeArray<float> Grid;
            public float3 MinPos;

            public void Execute()
            {
                for (int i = 0; i < Ps.Length; i++)
                {
                    float3 rel = Ps[i].Position - MinPos;
                    int3 center = (int3)floor(rel / SlimeConst.CellSize);
                    for (int dz = -2; dz <= 2; dz++)
                    for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int3 coord = center + new int3(dx, dy, dz);
                        int3 key = coord >> 2;
                        if (!GridLut.ContainsKey(key)) continue;
                        int off = GridLut[key];
                        float3 cellPos = (0.5f + (float3)coord) * SlimeConst.CellSize;
                        float r2 = lengthsq(cellPos - rel);
                        if (r2 > SlimeConst.H2) continue;
                        Grid[off + LIdx(coord)] += SlimeConst.SmoothingKernelPoly6(r2);
                    }
                }
            }
            static int LIdx(int3 c) => (c.x & 3) + 4 * ((c.y & 3) + 4 * (c.z & 3));
        }

        [BurstCompile]
        public struct GridBlurJob : IJob
        {
            public const int BlockScratchLength = 6 * 6 * 6;

            [ReadOnly] public NativeArray<int3> Keys;
            [ReadOnly] public NativeHashMap<int3, int> GridLut;
            public NativeArray<float> GridRead;
            public NativeArray<float> GridWrite;
            public NativeArray<float> BlockScratch;

            public void Execute()
            {
                for (int i = 0; i < Keys.Length; i++)
                {
                    int3 key = Keys[i];
                    if (!GridLut.ContainsKey(key)) continue;

                    int off = GridLut[key];
                    if (!BlockScratch.IsCreated || BlockScratch.Length < BlockScratchLength)
                        continue;
                    NativeArray<float> block = BlockScratch;
                    int3 bc = key * 4 - 1;

                    ClearBlock(block);

                    for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int3 nk = key + new int3(dx, dy, dz);
                        if (!GridLut.ContainsKey(nk)) continue;
                        int no = GridLut[nk];
                        for (int j = 0; j < 64; j++)
                        {
                            int3 c = (nk * 4) + LC(j) - bc;
                            if (any(c < 0) || any(c >= 6)) continue;
                            block[BI(c)] = GridRead[no + j];
                        }
                    }

                    for (int j = 0; j < 64; j++)
                    {
                        int3 c = LC(j) + 1;
                        float s = 0f, w = 0f;
                        for (int dz = -1; dz <= 1; dz++)
                        for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int3 nc = c + new int3(dx, dy, dz);
                            float weight = 1f - 0.5f * length(new float3(dx, dy, dz));
                            s += block[BI(nc)] * weight;
                            w += weight;
                        }
                        GridWrite[off + j] = w > 1e-6f ? s / w : 0f;
                    }
                }
            }

            static void ClearBlock(NativeArray<float> block)
            {
                for (int i = 0; i < BlockScratchLength; i++)
                    block[i] = 0f;
            }

            static int3 LC(int idx) => new int3(idx & 3, (idx >> 2) & 3, (idx >> 4) & 3);
            static int BI(int3 c) => c.x + 6 * (c.y + 6 * c.z);
        }
    }
}
