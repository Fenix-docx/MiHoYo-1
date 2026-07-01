# 复杂形状约束 — 根因分析、参考方案与落地计划

> 创建日期: 2026-06-29
> 关联: [[CLAUDE.md]] | [[史莱姆实现方案-五层架构详解]]

---

## 现状概述

项目有三层形状约束：

| 层 | 文件 | 机制 | 适用场景 |
|---|------|------|----------|
| **A** | `Slime_CohesionJobs.cs:40-58` `ApplyShapeMatchJob` | 平移形状匹配：粒子被拉到 `质心 + restOffset` | 保持初始球状 blob |
| **B** | `Slime_SimulationJobs.cs:261-328` `ApplyShapeTargetJob` | XPBD 风格位置投影 + SDF 边界推回 | 把粒子约束到 Mesh 内部 |
| **C** | `Slime_SimulationJobs.cs:331-372` `UpdateJob` | AABB 推出（BoxCollider 快照） | 静态障碍物碰撞 |

层 B（`Slime_ShapeTarget.cs` 553行 + `ApplyShapeTargetJob`）是专门为「给定 Mesh → 粒子遵循形状凝聚 → 果冻定型」设计的系统。但它对复杂网格失败了。

---

## 一、根因分析

### 🔴 根因 1：SDF 构建复杂度 O(voxels × triangles)，无 Burst、无空间加速

**调用链** (`Slime_ShapeTarget.cs:159-172`)：
```
TryBuild()
  for each SDF voxel (如 128³ = 2M):
    DistanceToMesh(p, vertices, triangles)              // Line 452
      for each triangle (如 10K):
        DistanceSqToTriangle(p, a, b, c)                // Line 505
    IsInsideClosedMesh(p, vertices, triangles)           // Line 465
      for each triangle:
        RayIntersectsTriangle()                          // Line 480
```

- 10K 三角形 × 128³ 体素 ≈ **220 亿** 次三角形距离计算 + 220 亿次射线相交
- 在托管 C# 中运行，**非 Burst**，**无空间加速结构**（无 BVH/格网）
- 后果：超过简单 Cube 的网格会挂起数分钟

**修复方向**: 均匀空间哈希格网加速 (cell ±2)，Burst Job，降至 O(1)/体素

---

### 🔴 根因 2：`IsWatertight()` 拒绝几乎所有现实网格

`Slime_ShapeTarget.cs:388-408` — 每条边必须恰好出现 2 次：

```csharp
foreach (int count in edgeUseCounts.Values)
    if (count != 2) return false;   // T-junction / 非流形 → 失败
```

- 几乎所有 DCC 导出的 FBX 都有 UV 接缝、T 型连接、微小缝隙
- 水密检测失败 → `isClosedVolume = false`
- → `UsesSurfaceFallback = true`
- → `DisableBoundaryProjection = true` → 粒子从薄壳中逃逸

**修复方向**: 用广义绕数 (GWN) 替代；GWN 对非水密网格依然稳健

---

### 🔴 根因 3：表面后备用薄壳 → 无体积感

`Slime_ShapeTarget.cs:255-277` `FillRestPositions()`：
```csharp
// 非水密网格的内部采样点不足 → 走表面薄壳
FillSurfaceFallback(restPositions, vertices, triangles, ...);
```

`FillSurfaceFallback` (Line 279-327) 只在三角形表面采样，沿法线偏移薄层。结果：
- 粒子集中在一层薄壳 → 不填充体积 → 无「果冻」体感
- PBF 密度约束推开粒子 → 史莱姆爆炸散开

**修复方向**: 即使网格非水密，用 GWN 足够多的内部采样点 → 直接填充内部

---

### 🟡 根因 4：形状匹配是纯平移，不支持旋转

`Slime_SimulationJobs.cs:287-288`：
```csharp
float3 target = center + restOffset;  // 仅平移，无旋转 R
```

Müller 2005 经典形状匹配：`goal_i = R × (x_i^0 − cm^0) + cm`。
缺失 R 导致 mesh transform 旋转时，rest positions 不跟随。

**修复方向**: 协方差矩阵 → 极分解 → 最优旋转 R（Müller 2005 式 5-7）

---

### 🟡 根因 5：PBF 与 shape 约束串行单次，无迭代收敛

调度顺序 (`Slime_Simulator.cs:548-587`)：
```
ComputeDeltaPos (PBF 密度约束) → ApplyShapeTarget (形状约束) → UpdateJob
```
一次执行，不迭代。PBF 推出粒子 → shape 拉回 → 下一子步再推出 → 拉锯战。

**修复方向**: 2-3 次 XPBD 迭代循环（阶段 3，可选）

---

## 二、参考方案

### 学术论文

| 方案 | 论文 | 贡献 | 适用 |
|------|------|------|------|
| **旋转形状匹配** | Müller 2005 *Meshless Deformations Based on Shape Matching* | 极分解求 R；不需网格连通性 | ⭐⭐⭐ 解决根因 4 |
| **XPBD 柔度** | Macklin 2016 *XPBD* | 刚度与 Δt 解耦；α/dt² | ✅ 已实现 |
| **广义绕数 GWN** | Jacobson 2013 *Robust Inside-Outside Segmentation using Generalized Winding Numbers* | 非水密网格稳健判内外 | ⭐⭐⭐ 解决根因 2 |
| **向量热扩散 SDF** | Feng & Crane SIGGRAPH 2024 *A Heat Method for Generalized Signed Distance* | 破几何体鲁棒 SDF | ⭐⭐ 好但复杂 |

### 工程参考

| 方案 | 来源 | 要点 |
|------|------|------|
| **VFX Graph MeshToSDFBaker** | Unity Package `visualeffectgraph` (已在本项目 Library 中存在) | GPU 烘焙 SDF；Jump Flood Algorithm |
| **PBD2D** | [github.com/andywiecko/PBD2D](https://github.com/andywiecko/PBD2D) | Unity + Burst 形状匹配；含旋转 + 碰撞 |
| **Obi Softbody** | Virtual Method Studio | 完整 Müller 形状匹配 + 簇形变 |

### 关键技术公式

**极分解求 R** (Müller 2005)：
```
A_pq = Σ m_i · p_i · q_i^T       // 协方差矩阵 3×3
A_pq = R · S                      // 极分解
R = A_pq · S^{-1}                 // 最优旋转（<1ms for 2048粒）
goal_i = R · q_i + cm             // 目标位置（含旋转！）
```

**广义绕数** (Jacobson 2013)：
```
w(p) = Σ (solid_angle of triangle t at point p) / (4π)
p 在内部 iff w(p) > 0.5
```
不依赖水密性，对 T 型连接 / 缝隙 / 自交均稳健。

---

## 三、落地计划

### 阶段 1：修复 SDF 构建瓶颈（P0，~5-7h）

| 步骤 | 文件 | 改动 |
|------|------|------|
| 1a | `Slime_ShapeTarget.cs` | `DistanceToMesh` / `IsInsideClosedMesh` 用空间哈希格网加速三角形查询 (cell ±2)，O(1) |
| 1b | 同上 | `TryBuild` 改为 Burst `IJob` |
| 1c | 同上 | 用广义绕数 (GWN) 替代 `IsWatertight` + ray-cast 内外判定 |
| 1d | 同上 | `FillRestPositions`：GWN 内部采样点足够时直接填充，不走薄壳 |

**验证**: 10K 三角形 FBX 在 <2s 内构建 SDF；非水密网格正常填充内部粒子

---

### 阶段 2：旋转形状匹配（P1，~3-4h）

| 步骤 | 文件 | 改动 |
|------|------|------|
| 2a | `Slime_CohesionJobs.cs` 或新文件 | 协方差矩阵 `A_pq` + 极分解 `R` + `goal_i = R·rest_i + cm` |
| 2b | `Slime_SimulationJobs.cs` `ApplyShapeTargetJob` | 用 R 替代纯平移 target 计算 |
| 2c | `Slime_Simulator.cs` | 传入 Rotation 矩阵到 `ApplyShapeTargetJob` |

**验证**: 旋转 `shapeTargetTransform`，粒子跟随旋转

---

### 阶段 3：XPBD 迭代收敛（P2，~1-2h，可选）

| 步骤 | 文件 | 改动 |
|------|------|------|
| 3a | `Slime_Simulator.cs` | `ComputeDeltaPos` ↔ `ApplyShapeTarget` 间 2-3 次迭代 |
| 3b | `Slime_SimulationJobs.cs` | 验证柔度公式 `alpha = Compliance / (dt²)` 已正确 |

**验证**: 边界抖动消失

---

### 阶段 4：GPU 加速 + 法线平滑（可选）

| 步骤 | 说明 |
|------|------|
| 4a | 利用 Library 中已有的 `MeshToSDFBaker` (VFX Graph) Jump Flood 算法 |
| 4b | `SlimeSdf.Gradient` 解析法线替代有限差分 |

---

## 四、验证清单

| 验证项 | 测试方式 |
|--------|----------|
| 复杂网格 SDF | 10K 三角形 FBX，`TryBuild` <2s |
| 非水密填充 | 有孔洞的 open mesh 正常填充内部粒子 |
| 旋转响应 | `shapeTargetTransform.rotation` 变更 → 粒子跟踪旋转 |
| 果冻效果 | 粒子填入 mesh 形状，挤压恢复，不越界 |
| 回归测试 | `SlimeShapeTargetTests.cs` 14 个测试全过 |
| 编译 | `dotnet build Assembly-CSharp.csproj` 0 错误 |
