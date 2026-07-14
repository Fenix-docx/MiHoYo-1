# 史莱姆模拟系统技术文档

> 创建日期: 2026-07-04  
> 适用范围: Unity 史莱姆粒子模拟、表面重建、分裂吸收与交互控制实现  
> 关联: [shape-constraint-analysis.md](shape-constraint-analysis.md)

---

## 第一章 项目概述

本项目实现了一个基于粒子流体的史莱姆模拟系统。底层以 Position Based Fluids
(PBF) 约束粒子密度，中层用显式分组 ID 管理分裂、吸收和控制切换，上层把粒子投影
到稀疏密度场，并通过 Marching Cubes 重建可渲染的连续表面。

文档中以下旧表述已按源码修正：

- `CellSize = 0.5` 是密度场体素边长；PBF 邻域哈希使用 `H = 1.0` 作为网格边长。
- `DensityProjectionJob` 是粒子驱动的串行 `IJob`，不存在并行写入冲突；不是并行投影。
- Marching Cubes 每个活跃块当前遍历 `5 * 5 * 5 = 125` 个候选 MC cell，不是旧文档中的 `6 * 6 * 6 = 216`。
- `SlimeParticle.ID` 是显式分组标签，不是连通域分析 CCL 推导出的标签。
- `Slime_Renderer` 只是调试粒子渲染器；主表面由 `Slime_SurfaceBuilder` 生成 Mesh 并用材质绘制。
- 史莱姆玻璃材质如果使用 `URP_SSR_Glass.shader`，核心是屏幕空间折射、SSR/天空反射和 Fresnel，而不是手写 Lambert/Phong 光照。

---

## 第二章 相关技术与理论基础

本章说明项目涉及的核心图形学、物理模拟和渲染理论。第三章再对应到具体源码实现。

### 2.1 坐标空间与变换矩阵推导

系统同时使用 Unity 世界空间和模拟空间。源码约定见 `Slime_Constants.cs`：

```csharp
public const float Scale = 0.1f;   // Unity world = sim * Scale
public const float InvScale = 10f; // sim = Unity world * InvScale
```

因此点坐标的基本映射为：

```text
p_world = S * p_sim
p_sim   = S^-1 * p_world

S = | Scale  0      0      0 |
    | 0      Scale  0      0 |
    | 0      0      Scale  0 |
    | 0      0      0      1 |
```

对普通点使用齐次坐标 `w = 1`，平移、旋转、缩放都会生效；对方向向量使用 `w = 0`，
平移项不参与计算。源码中对应为：

```csharp
static float3 TransformPoint(float4x4 matrix, float3 point)
{
    float4 v = mul(matrix, new float4(point, 1f));
    return v.xyz;
}

static float3 TransformVector(float4x4 matrix, float3 vector)
{
    float4 v = mul(matrix, new float4(vector, 0f));
    return v.xyz;
}
```

复杂形状约束把目标 Mesh 的局部空间映射到模拟空间。若 `M_lw` 是 Mesh 的
`localToWorldMatrix`，则：

```text
M_local_to_sim = S^-1 * M_lw
M_sim_to_local = M_wl * S
```

源码实现：

```csharp
float4x4 GetShapeLocalToSim()
{
    Matrix4x4 matrix = _activeShapeTargetTransform != null
        ? _activeShapeTargetTransform.localToWorldMatrix
        : Matrix4x4.identity;
    matrix = Matrix4x4.Scale(Vector3.one * SlimeConst.InvScale) * matrix;
    return matrix;
}

float4x4 GetShapeSimToLocal()
{
    Matrix4x4 matrix = _activeShapeTargetTransform != null
        ? _activeShapeTargetTransform.worldToLocalMatrix
        : Matrix4x4.identity;
    matrix *= Matrix4x4.Scale(Vector3.one * SlimeConst.Scale);
    return matrix;
}
```

Marching Cubes 生成的顶点已经被写成世界空间，所以渲染时使用单位矩阵：

```text
v_world = (origin_sim + (p_block + minCoord) * CellSize) * Scale
```

对应源码在 `Slime_MarchingCubes.cs` 中计算 `v0/v1/v2`，随后
`Slime_SurfaceBuilder.LateUpdate()` 直接执行：

```csharp
Graphics.DrawMesh(_mesh, Matrix4x4.identity, surfaceMaterial, gameObject.layer);
```

这点很重要：如果再把 GameObject 的 Transform 矩阵乘一次，会导致表面产生二次位移。

### 2.2 PBF 粒子流体基础

PBF 的核心思想是直接修正位置，而不是显式求压力力。每个粒子通过密度约束保持近似体积：

```text
rho_i = Σ_j m_j W(|p_i - p_j|, h)
C_i(p) = rho_i / rho_0 - 1
lambda_i = -C_i / (Σ_k |∇_p_k C_i|^2 + epsilon)
delta_p_i = (1 / rho_0) Σ_j (lambda_i + lambda_j + s_corr) ∇W_spiky(p_i - p_j)
```

本项目使用两类核函数：

```text
Poly6:
W_poly6(r) = 315 / (64π h^9) * (h^2 - r^2)^3, 0 <= r <= h

Spiky 梯度:
∇W_spiky(r) = -45 / (π h^6) * (h - r)^2 * normalize(r)
```

由于 `h = H = 1.0`，源码把常量预计算为：

```csharp
public const float Poly6Norm = 315f / (64f * PI);
public const float SpikyGradNorm = -45f / PI;
public const float SpikyKernelNorm = 15f / PI;
```

拉伸不稳定修正项为：

```text
s_corr = -k * (W(r) / W(delta_q))^4
delta_q = 0.25h
```

源码中 `TensileK = 0.1`，`TensileDq = 0.25 * H`。

实现上的注意点：`ComputeLambdaJob` 里的变量 `rho` 是被项目参数缩放后的约束量，不应直接
理解为严格物理密度。理论描述使用标准 PBF 公式，阅读代码时要以实际调参量为准。

### 2.3 空间哈希与邻域查询

若每个粒子都遍历所有其他粒子，复杂度是 `O(n^2)`。项目使用三维空间哈希把粒子放入
边长为 `H` 的格子：

```csharp
public static int3 GetCoord(float3 pos)
{
    return (int3)floor(pos / H);
}

public static int GetKey(int3 coord)
{
    int key = coord.x & 1023;
    key = (key << 10) | (coord.y & 1023);
    key = (key << 10) | (coord.z & 1023);
    return key;
}
```

每个粒子只访问当前格及周围 `3 * 3 * 3 = 27` 个格子。这样在粒子分布较均匀时，邻域查询
接近 `O(n)`。

### 2.4 稀疏密度场与隐式曲面

为了把粒子变成连续表面，项目构建标量密度场：

```text
D(x) = Σ_i W_poly6(|x - p_i|, H)
```

随后取等值面：

```text
D(x) = isoLevel
```

密度场不是全局三维数组，而是稀疏块结构：

- 每个 block 有 `4 * 4 * 4 = 64` 个 cell。
- 每个 cell 的边长是 `CellSize = 0.5` 模拟单位。
- 只为粒子附近分配 block，最大活跃块数 `GridNum = 768`。

光栅化流程是：

```text
粒子位置 -> 计算 AABB -> 分配稀疏 block -> 粒子投影密度 -> 3x3x3 加权模糊
```

加权模糊的作用是填平粒子采样造成的密度低谷，降低 Marching Cubes 表面破洞概率。

### 2.5 Marching Cubes 表面重建

Marching Cubes 对每个体素的 8 个角点比较 `isoLevel`，得到 8-bit 配置：

```text
config = Σ_i inside_i * 2^i
inside_i = D(corner_i) > isoLevel
```

若 `config` 为 0 或 255，说明该体素完全在等值面外或内，不产生三角形；其他情况通过查表
得到三角形边，并在线性插值位置放置顶点：

```text
t = (isoLevel - d0) / (d1 - d0)
p = p0 + t * (p1 - p0)
```

法线来自密度场梯度。源码使用中心差分加三线性采样：

```text
n = normalize((
    D(p - ex) - D(p + ex),
    D(p - ey) - D(p + ey),
    D(p - ez) - D(p + ez)
))
```

当前实现每个活跃 block 收集 `8 * 8 * 8` 的临时密度块，遍历 `x/y/z = 1..5`，
即 `5 * 5 * 5 = 125` 个候选 MC cell。边界处通过 owner 判定跳过可由邻块负责的 cell，
避免重复三角形。

### 2.6 表面材质与光照模型

项目表面渲染有两种实际路径：

- `SlimeSurfaceBuilder` 生成连续 Mesh，这是主渲染路径。
- `SlimeRenderer` 用 `Graphics.DrawMeshInstanced` 绘制小方块粒子，这是调试路径。

如果场景使用 `Assets/Materials/SSR_Glass.mat` 对应的 `URP_SSR_Glass.shader`，材质不是传统
Lambert/Phong，而是透明玻璃近似模型。核心公式如下。

Fresnel 项：

```text
F = saturate(bias + (1 - bias) * (1 - saturate(dot(N, V)))^power)
```

折射采样偏移：

```text
uv_refract = uv_screen + N_view.xy * refractionStrength * saturate(1 + N_view.z)
C_refract = SampleOpaque(uv_refract, roughness * 0.5) * absorption
```

反射方向：

```text
R_view  = reflect(-V_view, N_view)
R_world = reflect(-V_world, N_world)
```

屏幕空间反射与天空盒反射混合：

```text
skyWeight = saturate((1 - ssrAlpha) + SSRToSkyBlend)
C_reflect = lerp(C_ssr, C_sky, skyWeight)
reflectionWeight = saturate((ReflectionStrength * ssrAlpha + SkyStrength * skyWeight) * F)
```

最终颜色：

```text
C_surface = BaseColor * (0.16 + F * 0.5)
C_final = lerp(C_refract, C_reflect, reflectionWeight) + C_surface
alpha = saturate(Opacity + F * 0.36)
```

源码片段：

```hlsl
half fresnel = saturate(_FresnelBias + (1.0 - _FresnelBias) *
    pow(1.0 - saturate(dot(normalWS, viewDirWS)), _FresnelPower));

half3 refractedGlass = sceneColor * absorption;
half3 reflectionColor = lerp(ssr.rgb, skyReflection, skyWeight);
half reflectionWeight = saturate(
    (_ReflectionStrength * ssr.a + _SkyReflectionStrength * skyWeight) * fresnel);
half3 color = lerp(refractedGlass, reflectionColor, reflectionWeight) + surfaceTint;
```

若使用 Unity HDRP/URP Lit 材质，则逐光源 BRDF 由引擎实现，本文档不把它写成项目自定义
光照算法。

---

## 第三章 系统设计与实现（重点章节）

### 3.1 总体架构图

```text
┌─────────────────────────────────────────────────────────────────────┐
│                         Unity 场景与输入层                           │
│  Input.GetAxisRaw / KeyCode     Camera Facing     Rigidbody Motor     │
└───────────────┬───────────────────────────────┬─────────────────────┘
                │                               │
                v                               v
┌──────────────────────────────┐    ┌──────────────────────────────────┐
│ Slime_ControllerMotor         │    │ Slime_Simulator.Update            │
│ - WASD / Shift / Space        │    │ - Q/E 凝聚参数                    │
│ - MovePosition/MoveRotation   │    │ - F 分裂, Tab 切换控制            │
│ - CommandedVelocity           │    └─────────────────┬────────────────┘
└───────────────┬──────────────┘                      │
                v                                      v
┌─────────────────────────────────────────────────────────────────────┐
│                         Slime_Simulator.FixedUpdate                  │
│ SyncTransformTranslation                                             │
│ SyncControllerBodyTranslation                                        │
│ Simulate() x 2                                                       │
│ Control() -> UpdateAbsorb() -> BuildSurface() -> DebugRenderer       │
└───────────────┬─────────────────────────────────────┬───────────────┘
                │                                     │
                v                                     v
┌──────────────────────────────────┐   ┌────────────────────────────────┐
│ PBF / Cohesion / Collision Jobs   │   │ Group Logic                     │
│ ApplyForce -> Hash -> Sort        │   │ SlimeGroupManager               │
│ BuildLut -> Shuffle               │   │ - explicit ID groups            │
│ ComputeLambda -> ComputeDeltaPos  │   │ - split / absorb / switch       │
│ ShapeTarget? -> Update -> XSPH    │   └────────────────────────────────┘
└───────────────┬──────────────────┘
                v
┌─────────────────────────────────────────────────────────────────────┐
│                         Surface Reconstruction                       │
│ Slime_DensityField: sparse density grid + blur                       │
│ Slime_MarchingCubes: iso-surface triangles + normals                 │
│ Slime_SurfaceBuilder: Mesh vertex/index buffer + DrawMesh            │
└─────────────────────────────────────────────────────────────────────┘
```

### 3.2 模块划分

| 模块 | 核心文件 | 职责 |
|---|---|---|
| 输入模块 | `Slime_ControllerMotor.cs`, `Slime_Simulator.cs` | 读取 WASD/Shift/Space/Q/E/F/Tab，驱动 kinematic Rigidbody，输出 `CommandedVelocity`。 |
| 模拟模块 | `Slime_SimulationJobs.cs`, `Slime_Constants.cs`, `Slime_Particle.cs` | PBF 约束求解、空间哈希、碰撞、速度重建、XSPH 粘性。 |
| 凝聚模块 | `Slime_CohesionJobs.cs` | 计算每组质心，并用平移形状匹配让粒子回到紧凑 rest shape。 |
| 逻辑模块 | `SlimeGroupManager.cs`, `Slime_Simulator.cs` | 显式 ID 分组、分裂、吸收、控制切换、冷却和吸收重装条件。 |
| 渲染模块 | `Slime_DensityField.cs`, `Slime_MarchingCubes.cs`, `Slime_SurfaceBuilder.cs`, `URP_SSR_Glass.shader` | 粒子到密度场、Marching Cubes 表面重建、Mesh 绘制和玻璃材质。 |
| 形状目标模块 | `Slime_ShapeTarget.cs`, `ApplyShapeTargetJob` | 从目标 Mesh 构建 SDF/rest 粒子，并进行 XPBD 风格投影。 |
| 关卡交互模块 | `SlimeDoorSwitchOnContact.cs`, `SlimeCheckpointManager.cs` 等 | 检测史莱姆表面或粒子与机关/存档点的接触。 |
| 调试与测试模块 | `Slime_Renderer.cs`, `Tests/Editor/*.cs` | 粒子调试绘制、分裂/表面/形状目标/检查点单元测试。 |

### 3.3 主循环设计

`FixedUpdate()` 是系统入口。代码流程如下：

```csharp
void FixedUpdate()
{
    if (!_initialised) return;

    SyncTransformTranslation();
    SyncControllerBodyTranslation();
    RebuildShapeTargetIfNeeded();
    UpdateShapeTargetMorph(Time.fixedDeltaTime);

    for (int i = 0; i < 2; i++)
        Simulate();

    Control();
    UpdateAbsorb();

    if (enableSurface && _surfaceBuilder)
        _surfaceBuilder.BuildSurface(_particles, isoLevel);

    _renderer?.SetParticles(_particles);
}
```

为什么这么做：

- 同步 Transform 和 Controller Body 放在模拟前，保证粒子缓冲区先对齐外部移动。
- 每个物理 tick 做 2 个子步，降低 PBF 约束误差。
- `Control()` 放在模拟后，用最新粒子位置计算各组质心和控制器中心。
- `UpdateAbsorb()` 放在模拟后，接触判断使用本帧更新后的体积位置。
- 表面重建最后执行，保证 Mesh 反映最终粒子状态。

### 3.4 输入模块实现

移动输入由 `Slime_ControllerMotor` 处理：

```csharp
Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
Quaternion rotation = ResolveCameraFacingRotation(_rb.rotation);
float speed = Input.GetKey(KeyCode.LeftShift) ? moveSpeed * sprintMultiplier : moveSpeed;
Vector3 v = CalculatePlanarMoveVelocity(rotation, input, speed);

CommandedVelocity = new Vector3(v.x, _vy, v.z);
_rb.MovePosition(pos);
_rb.MoveRotation(rotation);
_rb.linearVelocity = CommandedVelocity;
```

设计原因：

- `Slime_ControllerMotor` 只负责控制器刚体，不直接改粒子。
- `CommandedVelocity` 暴露给模拟器，由 `ApplyForceJob` 决定哪些粒子跟随。
- 方向基于相机平面投影，避免镜头旋转后 WASD 方向不符合玩家直觉。

`Slime_Simulator.Update()` 处理状态型操作：

```csharp
if (Input.GetKeyDown(KeyCode.Q))
    ChangeControlledCohesion(-concentrationStep, -stiffnessStep, 1);
if (Input.GetKeyDown(KeyCode.E))
    ChangeControlledCohesion(concentrationStep, stiffnessStep, -1);
if (Input.GetKeyDown(keySplit))
    Split(GetFacingDirection());
if (Input.GetKeyDown(keySwitchControl))
    SwitchControl();
```

### 3.5 PBF 模拟模块实现

`Simulate()` 的 Job 链如下：

```text
ApplyForce
-> Hash
-> Sort
-> BuildLut
-> Shuffle
-> ComputeLambda
-> ComputeDeltaPos
-> ApplyShapeTarget? 
-> Update
-> ApplyViscosity
```

关键代码片段：

```csharp
new SlimeSimulation.ApplyForceJob { ... }.Schedule(...).Complete();
new SlimeSimulation.HashJob { ... }.Schedule(...).Complete();
_hashes.SortJob(new Int2Comparer()).Schedule().Complete();
new SlimeSimulation.BuildLutJob { ... }.Schedule().Complete();
new SlimeSimulation.ShuffleJob { ... }.Schedule(...).Complete();
new SlimeSimulation.ComputeLambdaJob { ... }.Schedule(...).Complete();
new SlimeSimulation.ComputeDeltaPosJob { ... }.Schedule(...).Complete();
new SlimeSimulation.UpdateJob { ... }.Schedule(...).Complete();
new SlimeSimulation.ApplyViscosityJob { ... }.Schedule(...).Complete();
```

为什么要 `Hash -> Sort -> BuildLut -> Shuffle`：

- `Hash` 将粒子映射到空间格。
- `Sort` 让同一格子的粒子在数组中连续。
- `BuildLut` 记录每个格子在排序数组中的 `[start, end)`。
- `Shuffle` 同步重排粒子、速度、rest offset 和组 ID，后续邻域遍历可以连续访问内存。

分组感知 PBF 是分裂功能能稳定工作的关键：

```csharp
if (Ids[j] != id) continue;
```

这行分别出现在 `ComputeLambdaJob`、`ComputeDeltaPosJob` 和 `ApplyViscosityJob`。它的作用是让
不同分裂体成为相互独立的流体，不会在 PBF 压力或粘性阶段互相推开、拖拽。

### 3.6 凝聚与形状匹配实现

普通凝聚由两部分组成。

第一部分是 `ApplyForceJob` 中的控制器中心吸引：

```csharp
float3 toCenter = controllerCenter - p.Position;
float len = length(toCenter);

if (len < controllerRadius)
{
    velocity = lerp(controllerVelocity, velocity,
        lerp(1f, len * 0.1f, controllerConcentration * 0.002f));
    velocity += controllerConcentration * SlimeConst.DeltaTime *
                min(1f, len) * normalizesafe(toCenter);
}
```

第二部分是 `Slime_CohesionJobs.cs` 的平移形状匹配：

```csharp
float3 center = Centres[g] / Counts[g];
float3 restCenter = RestCentres[g] / Counts[g];
float3 target = center + RestOffsets[i] - restCenter;
float3 correction = (target - currentPos) * stiffness;
Velocity[i] += correction * inverseDt;
```

为什么只做平移形状匹配：

- 分裂后每个组只需要恢复为紧凑 blob，不需要保持复杂刚体姿态。
- 不计算旋转矩阵可以避免协方差矩阵和极分解开销。
- 对史莱姆这种软体效果，平移恢复加 PBF 约束比刚性形变更自然。

复杂 Mesh 形状目标则走 `ApplyShapeTargetJob`：

```csharp
float3 restOffset = TransformVector(LocalToSimVector,
    RestLocalPositions[i] - ShapeLocalCenter);
float3 target = center + restOffset;

float alpha = Compliance / max(DeltaTime * DeltaTime, 1e-6f);
float weight = saturate(Strength / (1f + alpha));
particle.Position = lerp(particle.Position, target, weight);
```

这是 XPBD 风格的位置投影，`Compliance` 越大，约束越软。

### 3.7 渲染模块实现

表面重建入口在 `Slime_SurfaceBuilder.BuildSurface()`：

```csharp
new SlimeDensityField.CalcBoundsJob { Ps = particles, Bounds = _bounds }.Schedule().Complete();
new SlimeDensityField.ClearGridJob { Grid = _grid }.Schedule().Complete();
new SlimeDensityField.AllocateBlockJob { ... }.Schedule().Complete();
new SlimeDensityField.DensityProjectionJob { ... }.Schedule().Complete();
new SlimeDensityField.GridBlurJob { ... }.Schedule().Complete();
new SlimeMC.MarchingCubesJob { ... }.Schedule().Complete();
```

密度投影代码：

```csharp
for (int i = 0; i < Ps.Length; i++)
{
    float3 rel = Ps[i].Position - MinPos;
    int3 center = (int3)floor(rel / SlimeConst.CellSize);
    for (int dz = -2; dz <= 2; dz++)
    for (int dy = -2; dy <= 2; dy++)
    for (int dx = -2; dx <= 2; dx++)
    {
        int3 coord = center + new int3(dx, dy, dz);
        float3 cellPos = (0.5f + (float3)coord) * SlimeConst.CellSize;
        float r2 = lengthsq(cellPos - rel);
        if (r2 > SlimeConst.H2) continue;
        Grid[off + LIdx(coord)] += SlimeConst.SmoothingKernelPoly6(r2);
    }
}
```

为什么用稀疏块：

- 史莱姆分裂后多个 body 可能相距较远，全局包围盒会浪费大量空体素。
- 每个粒子只会影响附近 cell，稀疏分配更适合动态分布。
- `GridNum = 768` 给活跃块设置上限，防止极端情况下分配过多 NativeArray 空间。

Marching Cubes 关键逻辑：

```csharp
if (maxDensity <= IsoLevel)
{
    skippedBlocks++;
    continue;
}

for (int z = 1; z < 6; z++)
for (int y = 1; y < 6; y++)
for (int x = 1; x < 6; x++)
{
    int config = 0;
    for (int vi = 0; vi < 8; vi++)
        if (block[BlkIdx(corner)] > IsoLevel)
            config |= 1 << vi;

    if (config == 0 || config == 255) continue;
    // 查 triTable，插值生成三角形和法线
}
```

为什么要 `maxDensity <= IsoLevel` early-out：

- 没有任何角点可能穿过等值面时，整块可跳过。
- 分裂后空块较多，early-out 能明显减少 MC 查表和法线计算。

### 3.8 分裂、吸收与控制切换

系统没有用连通域分析从密度场反推 body，而是让每个粒子携带权威分组 ID：

```csharp
public struct SlimeParticle
{
    public float3 Position;
    public int ID;
}
```

分裂流程：

```csharp
indices.Sort((a, b) =>
    math.dot(particles[b].Position, facingDir)
        .CompareTo(math.dot(particles[a].Position, facingDir)));

int newGroup = AllocateSlot(_initialConcentration, _initialStiffness, _initialCohesionStep);

for (int k = 0; k < splitCount; k++)
{
    int i = indices[k];
    SlimeParticle p = particles[i];
    p.ID = newGroup;
    p.Position += facingDir * separation;
    particles[i] = p;
    velocity[i] = new float3(0f, 0f, 0f);
}
```

为什么用位置偏移，而不是冲量：

- 冲量会在速度重建后残留漂移，分裂体像持续受力。
- 位置偏移是一次性分离，速度清零后分身能立即稳定下来。

吸收流程：

```csharp
float threshold = ctrlR + r + contactSlack;
if (d <= threshold && d < bestDist)
    best = g;
```

吸收时将目标组粒子 ID 改回受控组，并把速度向主体平均速度混合：

```csharp
p.ID = ctrl;
particles[i] = p;
velocity[i] = lerp(velocity[i], ctrlVel, 0.5f);
```

设计原因：

- 显式 ID 避开 CCL 的帧间 ID 重映射问题。
- 分裂体继承初始凝聚参数，不继承父体当前 Q/E 调整后的扩散状态。
- 吸收冷却和 `absorbReady` 防止刚分裂后立即合并。

### 3.9 坐标同步实现

项目存在两条同步路径。

Transform 平移同步：

```csharp
Vector3 worldDelta = current - _lastTransformPosition;
float3 simDelta = (float3)worldDelta * SlimeConst.InvScale;

TranslateParticles(_particles, simDelta);
TranslateParticles(_particlesTemp, simDelta);
TranslatePositions(_posOld, simDelta);
TranslatePositions(_posPredict, simDelta);
```

Controller Body 平移同步：

```csharp
Vector3 worldDelta = current - _lastControllerBodyPosition;
float3 simDelta = (float3)worldDelta * SlimeConst.InvScale;

TranslateParticles(_particles, simDelta, groupId);
TranslateParticles(_particlesTemp, simDelta, groupId);
TranslatePositions(_posOld, _particles, simDelta, groupId);
TranslatePositions(_posPredict, _particles, simDelta, groupId);
```

为什么需要两条路径：

- 场景或父物体移动时，需要整体平移所有粒子。
- 玩家控制器移动时，只应移动当前受控组。
- 当 `controllerBody.transform == transform` 时跳过 Transform 同步，避免同一位移被应用两次。

控制切换时会重置 `_lastControllerBodyPosition` 和 `_lastControllerBodyGroupId`，防止电机瞬移被误当成
本帧物理位移。

---

## 第四章 关键问题与优化方向

### 4.1 密度场破洞

破洞主要来自粒子采样不足、密度场分辨率与 `isoLevel` 不匹配，以及模糊次数较少。当前项目只做
一次 `3 * 3 * 3` 加权模糊，因此在稀疏或拉伸区域仍可能出现密度低谷。

可选优化：

- 增加粒子数，提高密度场采样率。
- 适当调低 `isoLevel`，但会让表面膨胀。
- 做两次模糊，计算成本小于各向异性核。
- 引入各向异性核函数，效果好但需要邻域协方差和特征分解，成本最高。

### 4.2 Marching Cubes 性能

当前 `MarchingCubesJob` 是单线程 `IJob`，主要成本来自：

- 遍历所有活跃 block。
- 每个 block 收集邻居密度到 `8 * 8 * 8` scratch。
- 对生成顶点做中心差分法线。
- 未做顶点去重，每个三角形独立写 3 个顶点。

可选优化是三遍并行化：

```text
Pass 1: CountTriangles(IJobParallelFor) 统计每个 cell 顶点数
Pass 2: PrefixSum 计算写入偏移
Pass 3: GenerateTriangles(IJobParallelFor) 并行写顶点/索引
```

这样可以避免 `NativeList` 多线程写入冲突，并把最重的查表与生成过程并行化。

### 4.3 GPU 表面重建模式

`SlimeSurfaceBuilder` 支持 CPU 和 GPU async-readback 两种表面重建模式。GPU 路径仍然让
CPU 侧模拟保持权威，只把粒子和稀疏 block 元数据上传到 compute shader，在 GPU 上重建表面，
再通过异步读回把紧凑顶点写回现有 CPU `Mesh`。

这样可以保留依赖 `CurrentSurfaceMesh` 的玩法脚本，包括表面接触开关，但代价是会有随 GPU
负载变化的读回延迟。玩法代码中应避免同步调用 `ComputeBuffer.GetData()`；它会阻塞主线程，
抵消这次迁移的性能收益。

模式行为：

- `Cpu`：始终使用 CPU 稀疏密度场和 Marching Cubes。
- `GpuAsyncReadback`：GPU 支持且调度成功时提交异步重建；不支持或调度失败时，本帧回退到 CPU。
- `Auto`：GPU 支持时优先提交异步重建；若某次 GPU 结果发生 overflow，本帧使用 CPU 覆盖一次，
  然后在后续符合条件的帧继续重试 GPU。

GPU 读回尚未完成时，`SlimeSurfaceBuilder` 会继续保留上一帧有效 Mesh，不会因为暂时没有新的
GPU 结果而清空表面，也不会按固定帧数切换到完整 CPU 重建。读回错误或被容量策略抑制时同样
保留最后有效 Mesh；只有成功返回零顶点或没有任何活跃 block 时才清空表面。

### 4.4 GPU 表面重建性能验收状态

本环境中的 GPU 表面重建性能验收状态为 **pending / unverified**。已使用
`C:\Users\12118\Unity\6000.4.2f1\Editor\Unity.exe` 在 batchmode 下完成 37 个 EditMode 测试，
结果为 37 passed、0 failed。当前仍未完成 Play Mode profiler、GPU 模式场景验收和正式的
shader 性能验证，因此不应视为 profiling passed。

后续在可用 Unity Editor 环境中需要记录：

| Scene | Particle count | Split state | CPU mode BuildSurface peak | GPU async mode BuildSurface peak | Readback latency | Surface contact scripts verified | Shader compile/import status |
|---|---:|---|---:|---:|---|---|---|
| Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending |

### 4.5 复杂 Mesh 形状目标

当前 `Slime_ShapeTarget.cs` 的主要限制：

- SDF 构建是体素到三角形的距离查询，缺少空间加速时复杂度过高。
- `IsWatertight()` 对现实 FBX 网格过严，UV 接缝、T 型连接和微缝都可能失败。
- 非水密网格会走表面薄壳 fallback，内部没有体积采样，容易被 PBF 推散。
- 形状匹配只做平移，目标 Mesh 旋转时粒子不会执行旋转匹配。

建议后续优先做：

- 三角形空间哈希或 BVH，加速 SDF 构建。
- 使用广义绕数 GWN 判断内外，替代严格水密检查。
- 实现带旋转的 shape matching：协方差矩阵 -> 极分解 -> 旋转矩阵。
- 将 PBF 与 Shape 约束做 2-3 次小迭代，提高收敛。

---

## 第五章 文件索引

| 文件 | 行数 | 职责 |
|---|---:|---|
| `Assets/Slime/Slime_Constants.cs` | 156 | 全局常量、核函数、空间哈希工具。 |
| `Assets/Slime/Slime_Particle.cs` | 41 | `SlimeParticle`、`SlimeController`、`SlimeBoxCollider` 数据结构。 |
| `Assets/Slime/Slime_SimulationJobs.cs` | 582 | PBF Job 链、碰撞、XSPH 粘性、形状目标投影。 |
| `Assets/Slime/Slime_CohesionJobs.cs` | 113 | 质心计算、平移形状匹配。 |
| `Assets/Slime/Slime_DensityField.cs` | 169 | 稀疏密度场、粒子投影、网格模糊。 |
| `Assets/Slime/Slime_MarchingCubes.cs` | 521 | Marching Cubes 查表、顶点插值、法线计算。 |
| `Assets/Slime/Slime_SurfaceBuilder.cs` | 266 | 密度场到 Mesh 的调度与绘制。 |
| `Assets/Slime/Slime_Simulator.cs` | 1628 | MonoBehaviour 主入口、初始化、调度、控制、同步、分裂吸收。 |
| `Assets/Slime/Slime_ControllerMotor.cs` | 150 | WASD/Shift/Space 控制器电机。 |
| `Assets/Slime/SlimeGroupManager.cs` | 458 | 显式 ID 分组、分裂、吸收、控制切换。 |
| `Assets/Slime/Slime_Renderer.cs` | 145 | 调试粒子渲染。 |
| `Assets/Slime/Slime_ShapeTarget.cs` | 553 | Mesh SDF 构建、rest 粒子生成、复杂形状目标数据。 |
| `Assets/Shaders/URP_SSR_Glass.shader` | 305 | 透明玻璃材质、屏幕空间折射/反射、Fresnel。 |
| `Assets/Slime/Tests/Editor/SlimeSplitTests.cs` | 948 | 分裂、吸收、控制切换相关测试。 |
| `Assets/Slime/Tests/Editor/SlimeSurfaceBuilderTests.cs` | 128 | 表面重建边界行为测试。 |
| `Assets/Slime/Tests/Editor/SlimeShapeTargetTests.cs` | 1132 | 形状目标和 SDF 相关测试。 |

---

## 第六章 参考资料

- Macklin, M. and Muller, M. (2013). Position Based Fluids. ACM TOG 32(4).
- Muller, M. et al. (2005). Meshless Deformations Based on Shape Matching. ACM TOG 24(3).
- Jacobson, A. et al. (2013). Robust Inside-Outside Segmentation using Generalized Winding Numbers. ACM TOG 32(4).
- Yu, J. and Turk, G. (2013). Reconstructing Surfaces of Particle-based Fluids Using Anisotropic Kernels. ACM TOG 32(1).
- Macklin, M. et al. (2016). XPBD: Position-Based Simulation of Compliant Constrained Dynamics. VRIPHYS.
