# MiHoYo - Unity 6 HDRP 项目

## 项目概况

- **引擎**：Unity 6（`render-pipelines.high-definition` 17.4.0）
- **渲染管线**：HDRP
- **语言**：C# + Unity.Mathematics + Burst
- **史莱姆代码**：`Assets/Slime/`
- **参考项目**：`e:\Unity(E;)\Unity_Slime-main\`（clousq 原版，PBF + Marching Cubes + 各向异性渲染）

---

## 知识库入口

> **必读**：`C:\Users\12118\Documents\Obsidian Vault\Clippings\Bilibili\史莱姆\史莱姆实现方案-五层架构详解.md`
> 
> 含完整伪代码、核函数公式、文件清单、实现顺序、性能数据和踩坑记录。

---

## 史莱姆五层架构与实现状态

```
① 粒子物理 (PBF)     ✅ 已实现 — Slime_SimulationJobs.cs (8步Job链)
② 凝聚约束            ✅ 已实现 — Slime_CohesionJobs.cs (平移形状匹配)
③ 密度场光栅化        ✅ 已实现 — Slime_DensityField.cs + Slime_SurfaceBuilder.cs
④ 表面重建 (MC)       ✅ 已实现 — Slime_MarchingCubes.cs
⑤ 特效/交互           🔧 进行中
```

### 交互与物理（Phase 4 已实现）

| 模块 | 文件 | 状态 |
|------|------|------|
| BoxCollider AABB 碰撞 | `Slime_SimulationJobs.cs` UpdateJob | ✅ |
| Rigidbody 电机 (WASD+Space) | `Slime_ControllerMotor.cs` | ✅ |
| 速度跟随增益 | `Slime_SimulationJobs.cs` ApplyForceJob `VelocityFollowGain` | ✅ |

**关键设计决策**：
- BoxCollider → sim-space 快照在 `Initialise()` 一次性读取，非每帧更新
- 粒子碰撞完全在 CPU Burst Job 中计算，不经过 Unity Physics.Simulate
- 控制用 kinematic Rigidbody（自包含重力/地板），Velocity 通过 `VelocityFollowGain` 驱动粒子
- 控制器 Center 保持粒子质心（自凝聚不变），Velocity 来自 Rigidbody

### 形状约束（Mesh Shape Target）（🔴 有问题）

| 模块 | 文件 | 状态 |
|------|------|------|
| SDF 构建（暴力法，无加速，无Burst） | `Slime_ShapeTarget.cs` | 复杂网格不可用 |
| Shape Target Job（XPBD 投影 + SDF 边界） | `Slime_SimulationJobs.cs` ApplyShapeTargetJob | ✅ 逻辑正确 |
| Mesh 形状目标配置 | `Slime_Simulator.cs` | ✅ |
| 单元测试 | `Tests/Editor/SlimeShapeTargetTests.cs` | ✅ 14 个测试 |

> **详细分析**：`docs/shape-constraint-analysis.md`
> 
> 含根因分析（5个）、参考方案（Müller 2005 / GWN / Feng&Crane 2024）、四阶段落地计划、验证清单。

---

## 项目文档

- `docs/shape-constraint-analysis.md` — 复杂形状约束根因分析与改进计划
- `C:\Users\12118\Documents\Obsidian Vault\Clippings\Bilibili\史莱姆\史莱姆实现方案-五层架构详解.md` — 五层架构完整参考

---

## 文件结构速查

```
Assets/Slime/
├── Slime_Constants.cs          # 全局常量 + 核函数 + 空间哈希工具
├── Slime_Particle.cs           # SlimeParticle + SlimeController + SlimeBoxCollider
├── Slime_SimulationJobs.cs     # PBF 8 步 Job + ApplyShapeTargetJob + UpdateJob
├── Slime_CohesionJobs.cs       # 质心计算 + 平移形状匹配
├── Slime_DensityField.cs       # 稀疏密度场 (光栅化 + 模糊)
├── Slime_MarchingCubes.cs      # MC 表面重建 (含查表)
├── Slime_ShapeTarget.cs        # SDF 构建 + rest 粒子放置 + 网格工具
├── Slime_SurfaceBuilder.cs     # 密度场→MC→Mesh 流水线
├── Slime_Simulator.cs          # MonoBehaviour 入口 (初始化/调度/Control)
├── Slime_Renderer.cs           # 粒子渲染 (GPU Instancing)
├── Slime_ControllerMotor.cs    # WASD+Space Rigidbody 电机
└── Tests/Editor/
    └── SlimeShapeTargetTests.cs # 形状目标 14 个单元测试
```

---

## 关键常量 (SlimeConst)

| 常量 | 值 | 含义 |
|------|-----|------|
| H / H2 | 1.0 / 1.0 | 平滑/约束半径 |
| CellSize | 0.5 | 密度场 cell 边长 (sim) |
| Scale / InvScale | 0.1 / 10 | world ↔ sim 缩放 |
| GridSize | 64 | 每个块 cell 数 (4³) |
| GridNum | 768 | 最大活跃块数 |
| Num | 2048 | 粒子数 |
| TargetDensity | 1.5 | 静止密度 |
| DeltaTime / PredictStep | 0.02 / 0.02 | 物理步长 |
