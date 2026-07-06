using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Slime.Tests
{
    public class SlimeSurfaceBuilderTests
    {
        [Test]
        public void MarchingCubesReportsSkippedBlocksBelowIso()
        {
            var keys = new NativeArray<int3>(1, Allocator.TempJob);
            var grid = new NativeArray<float>(SlimeConst.GridSize, Allocator.TempJob);
            var gridLut = new NativeHashMap<int3, int>(1, Allocator.TempJob);
            var blockScratch = new NativeArray<float>(SlimeMC.MarchingCubesJob.BlockScratchLength, Allocator.TempJob);
            var stats = new NativeArray<int>(SlimeMC.MarchingCubesJob.StatsLength, Allocator.TempJob);
            var verts = new NativeList<float3>(4096, Allocator.TempJob);
            var norms = new NativeList<float3>(4096, Allocator.TempJob);
            var tris = new NativeList<int>(4096, Allocator.TempJob);

            try
            {
                keys[0] = new int3(0, 0, 0);
                Assert.That(gridLut.TryAdd(keys[0], 0), Is.True);
                for (int i = 0; i < grid.Length; i++)
                    grid[i] = 0.25f;

                new SlimeMC.MarchingCubesJob
                {
                    Keys = keys,
                    GridLut = gridLut,
                    Grid = grid,
                    BlockScratch = blockScratch,
                    Stats = stats,
                    VertsOut = verts,
                    NormsOut = norms,
                    TrisOut = tris,
                    IsoLevel = 1f,
                    Origin = new float3(0f, 0f, 0f),
                    CellScale = 1f,
                }.Schedule().Complete();

                Assert.That(stats[0], Is.EqualTo(1), "one sparse block should be inspected");
                Assert.That(stats[1], Is.EqualTo(1), "below-iso blocks should be skipped before cube traversal");
                Assert.That(verts.Length, Is.EqualTo(0));
                Assert.That(tris.Length, Is.EqualTo(0));
            }
            finally
            {
                tris.Dispose();
                norms.Dispose();
                verts.Dispose();
                stats.Dispose();
                blockScratch.Dispose();
                gridLut.Dispose();
                grid.Dispose();
                keys.Dispose();
            }
        }

        [Test]
        public void MarchingCubesKeepsBoundarySurfaceFromNeighbourDensity()
        {
            var keys = new NativeArray<int3>(1, Allocator.TempJob);
            var grid = new NativeArray<float>(SlimeConst.GridSize * 2, Allocator.TempJob);
            var gridLut = new NativeHashMap<int3, int>(2, Allocator.TempJob);
            var blockScratch = new NativeArray<float>(SlimeMC.MarchingCubesJob.BlockScratchLength, Allocator.TempJob);
            var stats = new NativeArray<int>(SlimeMC.MarchingCubesJob.StatsLength, Allocator.TempJob);
            var verts = new NativeList<float3>(4096, Allocator.TempJob);
            var norms = new NativeList<float3>(4096, Allocator.TempJob);
            var tris = new NativeList<int>(4096, Allocator.TempJob);

            try
            {
                int3 ownerKey = new int3(0, 0, 0);
                int3 neighbourKey = new int3(1, 0, 0);
                keys[0] = ownerKey;
                Assert.That(gridLut.TryAdd(ownerKey, 0), Is.True);
                Assert.That(gridLut.TryAdd(neighbourKey, SlimeConst.GridSize), Is.True);

                for (int j = 0; j < SlimeConst.GridSize; j++)
                {
                    int3 c = LocalCoord(j);
                    if (c.x == 0)
                        grid[SlimeConst.GridSize + j] = 2f;
                }

                new SlimeMC.MarchingCubesJob
                {
                    Keys = keys,
                    GridLut = gridLut,
                    Grid = grid,
                    BlockScratch = blockScratch,
                    Stats = stats,
                    VertsOut = verts,
                    NormsOut = norms,
                    TrisOut = tris,
                    IsoLevel = 1f,
                    Origin = new float3(0f, 0f, 0f),
                    CellScale = 1f,
                }.Schedule().Complete();

                Assert.That(stats[0], Is.EqualTo(1));
                Assert.That(stats[1], Is.EqualTo(0), "neighbour density must keep the owner block active");
                Assert.That(verts.Length, Is.GreaterThan(0));
                Assert.That(tris.Length, Is.GreaterThan(0));
            }
            finally
            {
                tris.Dispose();
                norms.Dispose();
                verts.Dispose();
                stats.Dispose();
                blockScratch.Dispose();
                gridLut.Dispose();
                grid.Dispose();
                keys.Dispose();
            }
        }

        static int3 LocalCoord(int idx)
        {
            return new int3(idx & 3, (idx >> 2) & 3, (idx >> 4) & 3);
        }
    }
}
