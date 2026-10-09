# UI 2D 运动模块设计方案（UIMotion / Motion2D）

> 目标：给本项目 uGUI 层提供一套**统一的 2D 位移引擎**，覆盖 ①直线 ②曲线 ③多组件协同运动 三类需求，
> 并能与已有的 `GooeyUIGroup` / `SdfBlendField` / `UIPackage`（面板导航）三层协同工作。
>
> 本文只做方案设计，不含实现。文末给出 P0–P4 落地阶段与验收清单。

---

## 0. 结论摘要

| 维度 | 决策 | 一句话理由 |
|------|------|-----------|
| 核心模型 | **位置是时间的纯函数** `p = f(t)`，动力学只做后处理 | 可 Seek、可回放、可从任意点接管；积分推进做不到这三件事 |
| 时间重映射 vs 几何 | **正交两通道**：`u = Ease(t/dur)`、`p = Path(u)` | 缓动负责速度手感，路径负责轨迹形状，互不污染 |
| 曲线表示 | 段数组 `PathChain`（Line/Quad/Cubic/Arc/CatmullRom）+ **弧长 LUT** | Bezier 的 `t` 不是匀速，弧长参数化才能做出匀速滑行 |
| 坐标空间 | 路径存在**等比空间**（高=1，复用 `SdfUiSpace` 的 p 空间约定），锚点在**采样时刻**解析 | 换分辨率/动态宽度下形状与相对距离不变 |
| 协同运动 | **五级耦合模型 L0–L4**（时间编排 / 装配体 / 跟随链 / 弹簧 / 场驱动） | 不同视觉意图需要不同耦合强度，用一套机制硬套会处处别扭 |
| 与已有代码 | `UIBase.Open/Close` 需要**异步化**（退场动画后再 `SetActive(false)`） | 现在的同步 `SetActive(false)` 会当场杀掉退场动效 |
| 导航与转移冲突 | 转移期间的导航调用**排队**（不忽略），出队时重新解析与重新校验 | 忽略会丢点击（连点两次返回只回一层）；排队让"栈推进"与"视觉推进"同拍 |
| 与 Gooey 联动 | 运动模块**上报间距曲线与静止状态**，Gooey 侧做 blur 补偿与脏标记 | 融合桥在通断临界区会闪；静止时遮罩可以不算 |
| 依赖 | 不依赖 DOTween（但共存并定义边界） | DOTween 适合"一次性补间"，不适合"多组件强耦合 + 可回放 + 场驱动" |
| 数据结构 | 核心采样走 blittable struct + switch，可选 Burst；托管层只做创作与调度 | 避免虚调用，为后续上 Job 留路 |

---

## 1. 目标与非目标

### 1.1 目标

1. **直线**：任意两点间位移，端点是"相对锚点"而非硬编码坐标（跟随布局变化）。
2. **曲线**：任意可拼接的 2D 轨迹，支持弧长匀速、切线朝向、过冲外推、闭合路径。
3. **多组件协同**：一组 UI 元素在运动中保持某种关系（刚性编队 / 顺序跟随 / 弹性约束 / 共同响应一个场）。
4. **可控时间**：暂停（`timeScale = 0` 的菜单）、加速、跳过到终态、倒放、确定性回放。
5. **可中断**：动画被打断时从当前视觉状态无缝接管，不跳变。
6. **可测试**：核心采样是纯函数，Editor 测试能断言弧长误差、确定性、收敛性，不依赖 Play Mode。
7. **零 GC**：稳态每帧 0 字节托管分配。

### 1.2 非目标

- 不做完整 Tween 库（不做音频、材质、颜色插值的通用补间；颜色/材质交给既有 SDF 控制器与材质动画）。
- 不做骨骼动画 / 2D 物理模拟器。
- 不接管 `CanvasScaler` 的适配策略；只在既有适配结果上工作。
- 不改 `GooeyUIGroup` 的遮罩算法（只增加一个可选的参数输入通道）。

---

## 2. 现状与约束（来自仓库实测）

| 事实 | 出处 | 对本方案的影响 |
|------|------|---------------|
| UI 栈是 **uGUI 2.0.0**，面板体系为 `UIBase` / `UIManager` / `UINavigator` / `UINavButton` | `Packages/manifest.json`、`Assets/A_UI/UI/Package/**` | 写入目标是 `RectTransform`；入场/退场要挂在 `UIBase.OnOpened/OnClosed` 上 |
| `UIBase.Open/Close` 是**同步** `SetActive`，`IsOpen` 真值就是 `gameObject.activeSelf` | `UIbase.cs` 注释明确写了这条不变量 | 退场动画必须"延迟关闭"，不能为了动画破坏 `IsOpen` 语义 |
| `GooeyUIGroup` 在 **`LateUpdate`** 每帧 CPU 重算全屏遮罩（blur + threshold，`downsample=4`） | `GooeyUIGroup.cs` L14–33、L211 | 运动必须在 `LateUpdate` 之前完成采样；否则视觉落后一帧并抖动 |
| Gooey 的桥接对**间距**极敏感（blur ≈ 期望间距的 2~3 倍，30px 间距 → 70~90px；过大反而断） | `GooeyUIGroup.cs` 实测调参注释 | 协同运动的元素间距会穿越"通断临界区"，必须显式处理（见 §8.3） |
| `GooeyWarpEffect` 已有 `warpBlend`（形变强度 ≈ 颈部半径） | `GooeyUIGroup.cs` L88–92 | 速度 → 形变拉伸的接口已经现成，运动模块只需输出速度 |
| `SdfUiSpace` 定义了 UI ↔ p 空间换算：`p = (local - center) * 2 / 高` | `SdfUiSpace.cs` | 路径/控制点直接用这套空间存，视觉与运动同源 |
| 项目**已装 DOTween**（`Assets/Plugins/Demigiant`），但 `Assets/Scripts` 下没有使用 | grep 实测 | 存在"两套系统抢写 `anchoredPosition`"的风险，需要单一写入者仲裁 |
| Editor 测试是项目惯例（`Assets/Editor/GooeyUIGroupPhase2Tests.cs`、`Assets/Slime/Tests/Editor/*`） | 目录 | 核心数学必须可被 Editor 测试覆盖 |
| 渲染管线状态复杂（HDRP 与 URP 同时在 manifest；Gooey 注释记录"相机→RT 不可用"） | `GooeyUIGroup.cs` L22–27 | 运动模块**不碰相机/RT/Layer**，纯 `RectTransform` 写入，任何管线下都成立 |

---

## 3. 核心抽象：位置是时间的纯函数

### 3.1 为什么不用"速度积分"

数值积分（`v += a*dt; p += v*dt`）有三个 UI 场景下致命的缺陷：

1. **不可 Seek**：跳过动画、拖时间轴预览、回放录制都无法实现（结果依赖历史）。
2. **不可确定**：帧率不同结果不同，Editor 卡顿会导致动画走样，测试无法断言。
3. **接管困难**：中断后需要知道"当前速度"才能续接，而纯积分系统的速度是隐式的。

所以主通道一律采用**解析采样**：

```
u  = Ease(t / duration)                 // 时间通道：速度手感
p0 = Path.Evaluate(u) + AnchorOffset    // 几何通道：轨迹形状
p  = p0 + Σ Modifier_j(t, p0, v)        // 叠加通道：噪声/弹簧/约束
Bind(p) → RectTransform
```

**动力学（弹簧、约束）是后处理叠加层，不是主通道**。它是状态化的，所以：

- 有明确的**收敛判据**（能量低于阈值 → 冻结，避免永远抖）；
- 在 Seek / 回放时用固定步长重演（warm-up）而不是靠当前帧率（见 §10.3）。

### 3.2 管线分层

```
                ┌──────────────────────────────────────────────┐
                │ MotionClock   （可暂停/缩放/Seek 的时间源）    │
                └───────────────────┬──────────────────────────┘
                                    │ t
   ┌────────────────────────────────▼─────────────────────────────┐
   │ Track 轨道层                                                  │
   │   Anchor 解析 → Path 采样 → Ease 重映射 → 通道输出 (pos/rot/scale) │
   └────────────────────────────────┬─────────────────────────────┘
                                    │ MotionSample[]
   ┌────────────────────────────────▼─────────────────────────────┐
   │ Coordination 协同层（按拓扑深度分桶，逐桶推进）                  │
   │   L0 Stagger → L1 Rig → L2 FollowChain → L3 Spring → L4 Field  │
   └────────────────────────────────┬─────────────────────────────┘
                                    │ 解算后的 MotionSample[]
   ┌────────────────────────────────▼─────────────────────────────┐
   │ Constraint 约束层（2~4 次 relax 迭代）                          │
   │   画布边界 / 面板边界 / 最小间距 / 与静态元素避让                 │
   └────────────────────────────────┬─────────────────────────────┘
                                    │
   ┌────────────────────────────────▼─────────────────────────────┐
   │ Binder 写入层（唯一写入者仲裁）                                 │
   │   RectBinder → RectTransform；GooeyMotionFeedback → 速度/间距  │
   └──────────────────────────────────────────────────────────────┘
```

关键点：**协同层与约束层都工作在"同一帧内已解析的目标位置"上**，按依赖深度分桶推进，避免"follower 用上一帧 leader 位置"造成的一帧延迟累积（这与 Slime 的 8 步 Job 链是同一个教训）。

---

## 4. 空间与坐标（UI 特有的三个坑）

### 4.1 路径存在"等比空间"，不存像素

控制点若按像素存，`CanvasScaler` 换分辨率/宽高比后曲线会变形（16:9 的弧线在 4:3 上被压扁）。因此：

- 路径几何存在 **p 空间**（`p = (local - center) * 2 / 场高`），直接复用 `SdfUiSpace.TryGetFieldContext` 的 `scaleToP`；
- 采样时再映射回目标空间的 local 坐标。

**验收**：同一曲线资产在 1920×1080 / 2560×1080 / 1440×1080 三种 ReferenceResolution 下，形状一致（像素位置不同，但"弧高/跨度"比一致）。

### 4.2 锚点延迟到采样时刻解析

UI 的起终点几乎永远不是常数值，而是"某个 rect 的某条边往外偏一点"：

```csharp
public struct AnchorRef
{
    public RectTransform target;    // null ⇒ 画布/面板
    public AnchorEdge    edge;      // Left/Right/Top/Bottom/Center/Slot(index)
    public Vector2       offset;    // 以目标短边为基准的比例偏移
    public RectTransform space;     // 解算到哪个父空间（默认 target 的父级）
}
```

**必须延迟解析**：布局重建（`ContentSizeFitter`、动态文本宽度、多语言）会改变 rect 尺寸，烘焙坐标会在这些时刻错位。

### 4.3 跨父级运动与 Reparent

"从 A 面板飞到 B 面板"会跨越不同的 `RectTransform` 父链。若地址冲突（不同父级的 local 空间不同），需要：

```csharp
// 跨容器飞行：路径在"共同的画布空间"里定义，中途切父级时补偿 local 坐标
void ReparentPreservingScreen(RectTransform node, RectTransform newParent)
{
    Vector3 world = node.position;                                  // 保持屏幕位置
    node.SetParent(newParent, worldPositionStays: true);
    // 若父级有非 1 缩放/旋转：SetParent(true) 会改写 localScale，
    // 需要显式存回原 localScale / localRotation（UI 常见事故）
}
```

设计上把"路径定义空间"与"写入空间"分开：

| 字段 | 含义 |
|------|------|
| `PathSpace` | 路径几何定义在哪个空间（画布 / 面板 / 某个 rect 的等比空间） |
| `BindSpace` | 采样结果写进哪个 `RectTransform` 的 `anchoredPosition` |
| `ReparentPlan` | 可选：在 `u` 的某区间内换父级，附带屏幕位置补偿 |

P0 只做同父级；跨父级放到 P2（它只影响少数"飞入飞离"效果，但接口要一开始就留出来，否则后期改动会波及所有轨道）。

---

## 5. 直线运动

直线不是曲线的特例代码，而是**段类型 `Line`**；但它有独立的语义糖：

```csharp
MotionPlan
    .From(Anchor.Right(cardRect, offset: 0.05f))   // 从卡片右侧 5% 短边处出发
    .To(Anchor.Slot(slotRect, index))              // 到槽位
    .Duration(0.25f)
    .Ease(Ease.CubicOut)
    .Bind(BindTarget.AnchoredPosition);
```

直线特有需求（比曲线更"业务"）：

1. **相对方向**：`From.Left / Right / Above / Below(rect, distance)`，distance 用"短边比例"而非像素。
2. **镜像/翻转**：`Mirror(Horizontal)` 一键把整条轨道水平镜像（用于左右手性布局）。
3. **越界钳制**：起点或终点在画布外时，允许"截断路径"（把端点沿直线推回画布内，保持方向）而不是整体平移。
4. **纯轴运动特化**：`AxisMotion(X | Y)` 只写一个分量，保留另一个分量由布局系统控制（例如 `HorizontalLayoutGroup` 管理的横向列表，只做纵向飞入）——这是**避免与 LayoutGroup 抢写**的关键手段（见 §8.1）。

---

## 6. 曲线运动

### 6.1 段类型与拼接

运行时段类型（blittable，`switch` 分发，可 Burst）：

| 类型 | 参数 | 用途 |
|------|------|------|
| `Line` | p0, p1 | 直线滑入/滑出 |
| `QuadBezier` | p0, c, p1 | 单控制点抛物线（飞入、跳跃） |
| `CubicBezier` | p0, c0, c1, p1 | 手绘曲线、S 形 |
| `Arc` | center, radius, a0, a1, (ellipseScale) | 环形/扇形菜单、tab 指示器绕行 |
| `CatmullRom` | p0..p3 | 从一串点自动生成平滑轨迹（编辑器画点） |
| `SdfContour` | field, iso, start, dir | 沿 SDF 等值线滑行（L4 场驱动的几何化形式） |

`PathChain` 把段拼起来，并做两件事：

1. **C0/C1 拼接**：相邻段端点自动对齐；`G1` 模式下把交点两侧切线强制共线（Bezier 段把 `c0` 设为 `p0 + (p_prev - p_next)/6` 的镜像形式），避免出现"折角"。
2. **弧长参数化**：每段按需要采样 N 个点（Line: 2，Bezier/CR: 32，Arc: 解析）累积弧长做 LUT，`EvaluateByArc(s)` 用二分 + 线性插值。

**为什么弧长参数化是硬需求**：三次 Bezier 在控制点密集处速度骤降、稀疏处骤增，视觉上是"卡顿-冲刺"。匀速滑行的观感几乎完全取决于这一步。

**验收**：等时间间隔采样 100 点，相邻点间距的 `std/mean < 2%`（用 `duration` 与 `Ease.Linear` 断言）。

### 6.2 缓动与路径正交（含过冲外推）

`Ease.OutBack` / `OutElastic` 的 `u` 会 `>1` 或 `<0`。这**不是错误**——它是 UI 里最常用的"预期动作（anticipation）"：

- `u < 0`：起点前的小幅反向回弹；
- `u > 1`：终点后的过冲再回落。

因此路径采样**必须支持外推**：

```
u < 0 : p = p0 + Tangent(0) * (u * L)      // 沿起点切线反向延长
u > 1 : p = p1 + Tangent(1) * ((u-1) * L)  // 沿终点切线延长
```

同时提供 `ClampOutside` 开关（有些场景不允许越界，例如面板内元素）。

### 6.3 路径创作方式（三选一，按成本递增）

1. **代码 DSL**（`CubicBezier.Arc(arcHeight)`、`Path.Through(points)`）——P0 起就有，覆盖 90% 需求。
2. **编辑器画点**：Scene 视图里拖点生成 `CatmullRom`，存进 `MotionPathAsset`（ScriptableObject）。
3. **从既有几何生成**：`FromSpriteContour(sprite)`（复用 Gooey 已有的 sprite alpha 剪影逻辑思路）、`FromRect(rect, cornerRadius)`（圆角矩形绕行轨迹）、`FromSdfField(field, iso)`。

第 3 条是本项目独有优势：`GooeyUIGroup` 已经在生产 sprite 剪影与覆盖度场，**"沿按钮轮廓滑一圈"这类效果可以直接从视觉数据里派生路径**，不需要美术再画一遍。

---

## 7. 多组件协同运动（核心）

### 7.1 五级耦合模型

| 级别 | 名称 | 耦合强度 | 数学形式 | UI 用例 |
|------|------|---------|---------|---------|
| **L0** | 时间编排 Stagger | 无（仅时间相关） | `u_i = Ease(clamp((t - d_i)/dur))` | 列表逐项入场、卡片波浪、对角波 |
| **L1** | 装配体 Rig | 硬（刚体/软体变换） | `p_i = T(u) · o_i`（`o_i` 局部偏移） | 整组沿曲线飞入、扇形菜单、标签页整体位移 |
| **L2** | 跟随链 FollowChain | 中（运动学延迟） | `p_i = PathHistory(s_0 - Σ d_i)` | 拖尾、蛇形展开、"设置齿轮→一串图标" |
| **L3** | 弹簧耦合 Spring | 软（动力学 + 约束） | 二阶阻尼 + 距离约束 relax | 黏液感胶质拖尾、抗抖、粘性吸附 |
| **L4** | 场驱动 Field | 统计（共同响应环境） | `p_i' = p_i + F(p_i) · dt` | 整体绕开障碍、沿轮廓排队、避让鼠标 |

**选择原则**：能降级就别升级。同一效果的耦合级别越高，可预测性越差、调试成本越高。
例如"卡片依次入场"只需 L0；不要为了"看起来高级"上 L3。

### 7.2 L0 时间编排（Stagger）

四种延迟来源，覆盖绝大多数 UI 编排：

```csharp
public enum StaggerSource
{
    Index,          // 按子物体顺序：d_i = i * step
    Distance,       // 按到某个锚点的距离：d_i = dist_i / speed
    Grid,           // 按网格坐标：d_i = (|col - c0| + |row - r0|) * step  → 对角波
    Radial,         // 按极角：d_i = |Δangle| / angularSpeed        → 螺旋波
}
```

配套两个修饰：

- **TotalDuration 归一化**：给定总时长时反解 `step`，保证最后一项不超时；
- **Weight（波包）**：给每个元素额外乘一个"波包权重" `w_i`（例如中间元素幅度更大），形成"拱形波"而不是等幅平移。

### 7.3 L1 装配体 Rig

```csharp
public sealed class MotionRig
{
    // 组的位姿曲线：整组沿一条曲线走，并在途中旋转/缩放
    public PathChain      groupPath;
    public AnimationCurve curveRotation;   // u → 度
    public AnimationCurve curveScale;      // u → 系数
    public float          softness;        // 0 = 刚体；>0 = 软编队
}
```

- `softness = 0`：子元素刚性跟随（`p_i = T(u) · o_i`），适合"整块面板 + 内部元素一起飞"。
- `softness > 0`：**软编队**——子元素对组变换的响应按权重插值：

  ```
  w_i = lerp(1, w_i^authored, softness)
  p_i = T(w_i 加权位姿) · o_i
  ```

  效果是"整组像液体一样弯折"：靠近旋转中心的元素几乎不动，远端元素被拉走。这与 Gooey 的黏液观感天然一致。

**分层运动（父走直线、子延迟波浪）**是最典型的组合：面板走 L1 刚性装配体，子元素各自挂 L0 轨道并相对父级定位——两级共用同一个 `MotionClock`，这样"跳过动画"只需对 clock 做一次 Seek。

### 7.4 L2 跟随链 FollowChain

朴素做法"follower 按固定**时间**延迟追 leader 历史位置"在曲线急弯处会把链子挤成一团（内圈元素互相重叠）。正确做法是**按弧长回溯**：

```
每帧把 leader 的世界位置 + 累积弧长压进环形缓冲
follower_i 的目标 = 缓冲里"累积弧长 = s_leader - Σ_{j<=i} spacing_j"的位置
```

环形缓冲容量 = `最大总间距 / 最小位移步长 + 余量`；弯道处回滚距离自动变短（因为回溯按弧长而非按直线距离），链子保持均匀间距。

UI 场景尺寸小，缓冲一般 128~512 个样本足够；样本是 `Vector2 + float`（12 字节），内存可忽略。

### 7.5 L3 弹簧耦合 SpringCoupling

每个元素的最终位置 = "目标点（来自 L0–L2 采样）" 被弹簧拉过去，再加一组约束 relax：

```
// 半隐式欧拉 + 临界阻尼；ω 固有频率、ζ 阻尼比
v += (-2ζω·v - ω²·(x - target)) * dt
x += v * dt

// 可选：链式距离约束（PBD relax，2~4 次迭代）
for it in 2..4:
    for each neighbor pair (i, i+1):
        修正使 |x_{i+1} - x_i| ≈ spacing
```

三个必须处理的数值问题：

1. **低帧率爆炸**：`dt` 抖动（Editor 卡顿、暂停恢复）会让显式积分发散。
   → 用**临界阻尼弹簧的解析闭式解**（对常目标：`x(t)` 有解析形式），或 `dt` 钳制 + 子步（`substep = ceil(dt / (0.5/ω))`）。
2. **永不静止**：加入收敛判据 `|v| < ε 且 |x - target| < ε'` 持续 N 帧 → 冻结该元素并上报"静止"，同时把最终值**吸附**回目标（否则会留 0.3px 的偏差，UI 上肉眼可见）。
3. **scale 为 0 / 折叠面板**：约束解算要 guard（除零、NaN 传播会污染整链）。

`ω / ζ` 配方（起始建议，需实测校准）：

| 手感 | ω | ζ |
|------|---|---|
| 干脆（无过冲） | 26 | 1.0 |
| 轻微回弹 | 22 | 0.85 |
| 明显黏液/胶质 | 16 | 0.55 |
| 果冻（配合 warp 拉伸） | 12 | 0.35 |

### 7.6 L4 场驱动

把一组元素放进同一个 2D 向量场，各自按所在位置采样方向：

```csharp
public interface IVectorField2D
{
    Vector2 Sample(Vector2 p, float t);   // 返回单位方向或速度
}
```

场可以来自：

- **SDF 梯度**：`F = -∇SDF`（远离/靠近场源）或 `F = perp(∇SDF)`（沿等值线滑行 → "沿轮廓排队"）；
- **避让场**：每个障碍物（按钮、面板边缘）贡献一个排斥项，`1/d²` 衰减；
- **鼠标/指针场**：把指针当作移动斥力源，元素被"吹开"，松手后由 L3 拉回；
- **噪声场**：Perlin 向量场 → 无方向感的自然漂浮（呼吸/悬浮）。

**本项目独有优势**：`SdfBlendField` 已经在维护 UI 空间里的 SDF 场（p 空间），
**视觉（融合）与运动（场驱动）可以同源** —— 融合桥张开的方向天然就是元素被推开的方向。
这会让"黏液分离/聚合"的效果在视觉与运动上自洽，而不是两套系统各演一半。

### 7.7 组合示例（把三类需求串起来）

**场景**：点击"设置"按钮 → 主面板直线滑入；面板内 4 个图标沿弧线散开成扇形；彼此间距由弹簧维持；最后吸附回布局槽位。

```csharp
// ① 直线（L1 刚性装配体，整块面板）
var panelHandle = UIMotion.Play(MotionPlan
    .From(Anchor.Right(panelRect, 1.05f))       // 从画布右侧外
    .To(Anchor.Center(panelRect))
    .Duration(0.32f).Ease(Ease.QuintOut)
    .Bind(panelRect));

// ② 曲线（L0 编排 + L4 极坐标环编队）：4 个图标从中心沿弧形散开
var fanned = UIMotion.Play(MotionPlan
    .Rig(MotionRig.ArcScatter(center, radius: new Vector2(180, 120),
                              fromDeg: -35f, toDeg: 35f))   // 极坐标编队
    .Stagger(StaggerSource.Radial, step: 0.028f)             // 按角度错峰
    .Duration(0.42f).Ease(Ease.OutBack(1.15f))
    .Bind(iconRects));

// ③ 协同（L3）：间距由弹簧 + 距离约束维持，防止扇形展开时互相穿透
fanned.Couple(SpringCoupling.Chain(iconRects, omega: 20f, zeta: 0.8f,
                                   minSpacing: 96f));

// ④ 收敛：结束后吸附回布局槽位（交给约束层，避免长期偏差）
fanned.OnComplete(ConstraintConverge.ToSlots(iconRects, epsilon: 0.25f));

// ⑤ Gooey 联动：运动期间上报间距曲线，遮罩侧做 blur 补偿（见 §8.3）
fanned.Feedback(GooeyMotionFeedback.Group(gooeyGroup, blurCompensation: true));
```

---

## 8. 与现有 UI 栈的集成

### 8.1 唯一写入者仲裁（最容易出事的地方）

`anchoredPosition` 可能被五个系统同时写：本模块、`HorizontalLayoutGroup` 等布局组件、
`Animator`、DOTween、`ScrollRect`。冲突表现为"某帧突然跳回原位"。

仲裁规则：

| 冲突方 | 处理 |
|--------|------|
| LayoutGroup | 运动期间给元素挂 `LayoutElement.ignoreLayout = true`，结束后还原并 `LayoutRebuilder.ForceRebuildLayoutImmediate` 一次 |
| Animator | 面板级 Animator 不使用 `anchoredPosition` 曲线（在 Validator 里检测并报错） |
| DOTween | 共存但**互斥**：DOTween 直接补间 `anchoredPosition` 的调用点禁止与本模块同一元素（提供 `MotionOwnership` 查询让调用方自律） |
| ScrollRect | 运动元素必须不在滚动内容下，或运动在 content 的 local 空间内做相对位移 |
| ContentSizeFitter | 只允许改变子元素位置、不允许改变自身尺寸；尺寸变化走"重采样"路径 |

拒绝"运行时动态争夺所有权"的复杂方案：**启动与打开面板时静态声明所有权**，冲突直接报错（与 `UIManager` 对 `PanelId` 重复的处理风格一致：宁可直接 `LogError`，也不静默取第一个）。

### 8.2 采样相位

`GooeyUIGroup` 在 `LateUpdate` 采样遮罩，所以：

- **本模块统一在 `Update` 推进**（`[DefaultExecutionOrder(-100)]` 保证早于 UI 逻辑的常规 `Update`）；
  - 优点：与 Gooey 的相位关系由 Unity 的"Update 全部早于 LateUpdate"保证，不依赖执行顺序配置；
  - 代价：运动值在当帧 `LateUpdate` 已被 Gooey 使用，视觉不落后。
- 若某些元素需要在 `LateUpdate` 里跟随其他系统的结果（例如 Cinemachine 之类），提供 `MotionPhase.LateUpdateBeforeGooey` 选项，并用 Script Execution Order 显式排到 `GooeyUIGroup` 之前。

**验收**：打开/关闭面板时，Gooey 融合桥不出现"一帧延迟"的抖动（逐帧录制遮罩，检查桥宽单调变化）。

### 8.3 Gooey 融合联动（本项目特有的协同）

1. **桥的闪烁问题**：融合桥在"间距穿过 blur 临界区"时会瞬间通断。
   与其调慢运动，不如让**遮罩参数成为运动的函数**：

   ```
   GooeyUIGroup.blurRadius(u) = lerp(断, 连, smoothstep(间距(u)))
   ```

   运动模块把参与同一 Gooey 组的元素**间距曲线**上报（`float curve(u)`），
   Gooey 侧读它做 blur 补偿 → 视觉上是"黏液拉丝"，而不是"闪一下连上"。

2. **速度 → 形变拉伸**：`GooeyWarpEffect.warpBlend` 已经是"颈部半径"语义，
   直接把 `|v|` 映射到 `warpBlend`（幅值受 `maxStretch` 限制），运动方向上产生拖尾感。

3. **静止脏标记**：遮罩每帧 CPU 重算（`downsample=4` 的全屏 blur + threshold）。
   运动模块上报"整组静止"（§7.5 的收敛判据已产出该信息）→ Gooey 可跳过该帧重算。
   这是**运动模块对渲染性能的正向贡献**，也是本方案与 Gooey 的第二个耦合点。

### 8.4 面板进出场：`UIBase` 需要异步化

现状：`Open()` / `Close()` 同步 `SetActive`，`IsOpen` 就是 `activeSelf`。退场动画要求"先播完再 `SetActive(false)`"。

设计（**保持 `IsOpen` 唯一真值不变**）：

```csharp
public class UIBase : MonoBehaviour
{
    public bool IsOpen       => gameObject.activeSelf;   // 不变量保持不变
    public bool IsTransitioning { get; private set; }    // 新增，仅供导航/输入屏蔽使用

    // 用可空句柄：MotionHandle 是 struct，没有动画时必须能表达"没有"（不要用 default 哨兵）
    protected virtual MotionHandle? PlayOpen()  => null;  // 默认无动画 ⇒ 行为与现在完全一致
    protected virtual MotionHandle? PlayClose() => null;

    public void Open()
    {
        if (IsOpen) return;
        gameObject.SetActive(true);
        IsTransitioning = true;
        var h = PlayOpen();
        if (!h.HasValue) { IsTransitioning = false; OnOpened(); }
        else h.Value.OnComplete(() => { IsTransitioning = false; OnOpened(); });
    }

    public void Close()
    {
        if (!IsOpen) return;
        var h = PlayClose();
        if (!h.HasValue) { gameObject.SetActive(false); OnClosed(); return; }
        IsTransitioning = true;
        h.Value.OnComplete(() => { IsTransitioning = false; gameObject.SetActive(false); OnClosed(); });
    }
}
```

两条必须明确的约束：

1. **`IsOpen` 语义不变**：退场期间面板仍 `activeSelf == true`；
2. **可反悔**：退场动画播放中被再次 `Open()` → 直接 `Kill` 退场轨道并从当前位置接管（§10.1），不会出现"关到一半又打开"的错乱。

注意异步化带来的一个**有意为之**的副作用：`Push` 里 `SetOpen(current,false)` 与 `SetOpen(target,true)` 现在会**重叠**（旧面板在播退场、新面板在播进场），这正是交叉淡化（cross-fade）想要的画面，但也意味着**转移期间画面里同时存在两个面板** —— 因此必须配输入屏蔽（见 §8.5 的"输入屏蔽"）。

### 8.5 导航排队：`UINavigationQueue`（已定：排队，不忽略）

现状：`Push/Back/Replace/ResetToRoot` 是**同步改栈 + 同步 SetOpen**。异步化之后，快速连点会产生"栈已经推进到 C，而视觉还停在 A→B 的半途"这类错乱。

**决策：转移期间的任何导航调用一律入队，出队时执行。**

关键不变量：**栈推进与视觉推进同拍** —— 一次导航转移 = 一次 `SetOpen` 组合 + 该组合的转移结束，中途不改栈。这样 `Current`、`Navigated` 事件、画面三者永远互相自洽。

#### 队列只存"语义意图"，不存解析结果

```csharp
public enum NavIntentKind { Push, Back, Replace, ToRoot }

struct NavIntent
{
    public NavIntentKind kind;
    public UIBase        target;      // Push / Replace
    public string        targetId;    // 数据驱动入口（Push(string)）
    public int           enqueueFrame;
}
```

**出队时才解析与校验**（这条比合并规则更重要）：

- `targetId` → 实例：走 `pageById`，**必须 `EnsureCache()` 后再取**（导航树可能在排队期间被改过）；
- `restrictToDirectChildren` 的父子校验用**最新**的 `Current` 重算（入队时的 Current 已经不是出队时的 Current）；
- 校验失败 → 丢弃该条 + `LogWarning` + **继续出队**（绝不因为一条坏意图卡死整条队列）。

#### 合并规则

| 入队情形 | 策略 | 理由 |
|---------|------|------|
| 连续 `Push(同一个 target)` | 折叠为 1 条 | 栈语义下第二次本来就是 no-op（`current == target` 直接 `return true`），折叠不改变结果 |
| `Push(A)` 后 `Push(B)` | 两条都保留；A 的进场允许被 supersede（Handoff 到终态） | 每次点击的意图都要尊重；中间态没必要播完整 |
| 连续 `Back` ×N | 全部保留；`N > fastForwardThreshold`（默认 3）时**第 4 条起改用 instant 变体**（跳过动画直接切） | 尊重"N 次回退"的语义，但不让"狂点返回"在输入停止后再补播 2 秒动画 |
| `ToRoot` / `CloseAll` | **先清空队列**再执行 | 它们是"清场"语义，队列里挂着的旧导航意图已过时 |
| 队列长度 > `maxQueue`（默认 8） | 丢弃**最新**一条 + `LogWarning` | 保留已排队的顺序语义；丢最新比丢最旧更可预测（不静默改写用户已表达的意图） |

#### 完成信号与看门狗

`UINavigator` 在一次转移开始时收集本次 `SetOpen` 触碰到的所有面板，等它们全部 `TransitionCompleted`（`UIBase.IsTransitioning == false`）——**以运动句柄的 `IsDone` 为准**，墙钟只作兜底。

看门狗（**没有它，一次丢失的完成回调会让整个 UI 永久卡死**）：

```
deadline = expectedDuration * 3 + 0.5s   // 用 unscaledTime，且优先判 MotionHandle.IsDone
超时 → ForceComplete（面板置终态、IsTransitioning = false）+ LogError + 继续出队
```

`timeScale = 0` 的暂停菜单、面板在转移中被禁用/销毁，都会让"按墙钟算的预期时长"失真 —— 所以判定顺序必须是：**句柄完成 → 句柄失效 → 墙钟兜底**，且兜底用 unscaledTime。

#### 输入屏蔽

转移期间把"正在离开"的面板 `CanvasGroup.blocksRaycasts = false`（完成或超时后还原），否则退场面板在 0.3 秒里仍然吃点击。是否同时屏蔽进场面板做成开关（默认不屏蔽，允许"进场即可点"的快速手感）。

#### API 契约变化（必须写进注释与调用方约定）

| 契约 | 现状 | 异步 + 排队之后 |
|------|------|----------------|
| `Push/Back/Replace/ResetToRoot` 返回值 | "是否执行成功" | **"是否已接受"**（可能只是入队） |
| 返回后立即读 `Current` | 已是新面板 | 仍是**即将离开**的面板（`activeSelf == true`） |
| `Navigated(from,to)` 时机 | 栈变更后立即触发 | 与栈变更同拍，但**在出队执行时**（入队时不触发） |
| 等待"真正落定" | 不需要 | 订阅新增的 `event Action NavigationIdle`，或读 `bool IsNavigating` |

**改动面很小**：`UINavButton` 调用 `nav.Push/Back/ResetToRoot` 时**不消费返回值**（实测 `UINavButton.cs` L69/L79/L85 全是丢弃返回值），因此导航排队对它**零改动**；真正需要审查的是任何"Push 完立刻读 `Current` / 立刻再 Push"的代码。

文件归属：**队列属于导航包，不属于运动模块**（`Assets/A_UI/UI/Package/Navigation/UINavigationQueue.cs`）—— 它是导航语义，运动模块只是它的"忙碌信号"来源之一。

### 8.6 与 DOTween 的边界

| 场景 | 用谁 |
|------|------|
| 单个元素的简单位移/缩放/透明度补间 | DOTween（已装、生态好） |
| 曲线轨迹、多元素协同、需要 Seek/回放、场驱动 | 本模块 |
| `Sequence` 编排 | 本模块（`MotionClock` 支持统一 Seek/Kill，DOTween Sequence 的"跳到最后"会留下中间残值） |

硬边界：**同一 `RectTransform` 的同一属性，同一时间只能有一个所有者**（由 §8.1 的静态声明保证）。

---

## 9. 数据结构与性能

### 9.1 数据布局

- **创作期（托管）**：`MotionPlan`（Builder，链式）、`MotionPathAsset`（ScriptableObject）、`MotionRig`、`StaggerPlan` —— 允许 class/接口，追求可读性。
- **运行期（blittable）**：烘焙成

  ```
  struct TrackSoA { float[] t0, invDur; int[] pathId, easeId; Vector2[] from, to; ... }
  struct PathSegment { int type; Vector2 p0, p1, p2, p3; float a0, a1, radius; }
  ```

  采样走 `switch` 分发，不用虚函数；LUT 按段索引共享。
  托管数组足够（UI 元素数量级在百），**Burst/Job 是可选项而非前提**：只有实测超过预算才上。

### 9.2 零 GC 约定

- 轨道对象池化（`MotionHandle` 是 struct 句柄，内部指向池槽位）；
- 禁止在采样路径上 `new`、LINQ、装箱、闭包（`OnComplete` 回调用预分配委托数组或 `Action` 池）；
- 字符串只出现在编辑器与错误日志。

### 9.3 性能预算（量级估算，P0 后须用 Profiler 校准）

| 项目 | 量级 |
|------|------|
| 路径采样（LUT 查找 + 插值） | ~20–40 ns / 元素 |
| 每个 Modifier | ~10–50 ns / 元素 |
| 约束 relax（2 次迭代 / 邻居对） | ~30 ns / 对 |
| **200 元素 × 4 通道** | **≈ 0.03–0.08 ms / 帧**（单线程估算） |

对比参照：`GooeyUIGroup` 每帧 CPU 重算 1/4 分辨率全屏遮罩的两轮可分离模糊，是**毫秒量级**。
结论：**运动模块不应成为瓶颈**；性能优化优先级应低于"遮罩脏标记"（§8.3.3）。

---

## 10. 中断、跳过与回放

### 10.1 Handoff（无缝接管）

快速连点 tab / 连按返回时，动效会被打断。接管流程：

```
1. Capture(): 读取当前 anchoredPosition
   - 解析通道：直接作为新轨道的"起点覆盖"（From 被运行时值替换）
   - 状态化通道（弹簧）：同时捕获速度 v
2. 新轨道以 (p_now, v_now) 为初值重新播放
3. C1 匹配（可选）：把新曲线起点切线方向旋转到与 v_now 一致，避免方向上的折角
4. 若新旧轨道目标相同且进度已过 90% → 直接 Snap 到终态（避免"永远追不上"的抖动）
```

### 10.2 Skip / Seek

- `Skip(handle)`：Seek 到 `u = 1` 并立即 `Bind`，然后触发 `OnComplete`。
  **必须触发 `OnComplete`**，否则依赖它的 `UIBase.IsTransitioning` 会永久卡住。
- `Seek(t)`：因为主通道是纯函数，Seek 是幂等且 O(1) 的。
- **状态化通道（弹簧/约束）在 Seek 时需要"重演"**：从该轨道的起点以固定步长（1/120s）推进到目标时间，上限 `maxWarmupSteps`（超限则直接采样解析值 + 速度归零并在 Editor 报 warning）。这是"纯函数为主 + 动力学为辅"架构必须付出的代价，必须**显式承认**而不是假装没有。

### 10.3 确定性回放

因为采样不依赖帧率：

- 同一 `t` 必得同一结果（Editor 测试可断言）；
- 可以录制"时间轴脚本"（事件 + 时间戳）用于自动截图对比测试；
- 可以接 Timeline / 录屏做"逐帧一致性"验证。

---

## 11. API 草图

```csharp
namespace UIPackage.Motion
{
    /// 采样结果：纯数据
    public struct MotionSample
    {
        public Vector2 position;    // BindSpace 下的 anchoredPosition
        public Vector2 velocity;    // 供弹簧/拖尾/Gooey 拉伸使用
        public float   rotation;    // 度，可选
        public float   scale;       // 统一缩放系数，可选
        public float   weight;      // 混合权重
    }

    public static class UIMotion
    {
        public static MotionHandle Play(MotionPlan plan);
        public static void         Kill(MotionHandle h, bool snapToEnd = true);
        public static void         Skip(MotionHandle h);           // 等价 Seek(end) + Complete
        public static void         Pause(MotionClock clock);
        public static void         Resume(MotionClock clock);
        public static MotionClock  NewClock(float timeScale = 1f, bool useUnscaledTime = false);
    }

    public sealed class MotionPlan
    {
        public MotionPlan From(AnchorRef from);
        public MotionPlan To(AnchorRef to);
        public MotionPlan Curve(IPath2D path);              // 与 From/To 二选一
        public MotionPlan Rig(MotionRig rig);               // L1
        public MotionPlan Stagger(StaggerSource src, float step);
        public MotionPlan Duration(float seconds);
        public MotionPlan Ease(IEase ease);
        public MotionPlan Clock(MotionClock clock);         // 与父级共享时间线
        public MotionPlan Modifier(IMotionModifier m);
        public MotionPlan Couple(SpringCoupling coupling);  // L3
        public MotionPlan Drive(IVectorField2D field);      // L4
        public MotionPlan Feedback(GooeyMotionFeedback fb); // 与 GooeyUI 联动
        public MotionPlan Bind(RectTransform target);       // 或 RectTransform[]
        public MotionHandle Start();
    }

    public struct MotionHandle            // struct 句柄，指向内部池槽位
    {
        public bool  IsValid { get; }
        public bool  IsDone  { get; }
        public float Progress { get; }
        public void  Kill(bool snapToEnd = true);
        public void  OnComplete(System.Action cb);
        public void  OnUpdate(System.Action<MotionSample> cb);
        public void  Handoff(MotionPlan next);   // 从当前视觉状态接管
    }
}
```

---

## 12. 文件清单（建议）

放 `Assets/A_UI/UI/Motion/`，命名空间 `UIPackage.Motion`（与既有 `UIPackage` 同级，便于被发现，又不污染导航包）：

```
Assets/A_UI/UI/Motion/
├── Core/
│   ├── MotionClock.cs          时间源（暂停/缩放/Seek/父子时钟）
│   ├── MotionSystem.cs         调度器：注册、相位、拓扑分桶、唯一写入者仲裁
│   ├── MotionHandle.cs         句柄 + 池
│   ├── MotionTypes.cs          MotionSample / AnchorRef / BindSpace
│   └── MotionMath.cs           纯函数：Bezier/弧长/噪声/弹簧解析解
├── Path/
│   ├── PathSegment.cs          blittable 段（Line/Quad/Cubic/Arc/CatmullRom/SdfContour）
│   ├── PathChain.cs            拼接 + G1 + 弧长 LUT
│   ├── PathBuilder.cs          代码 DSL（Arc / Through / FromRect / FromSpriteContour）
│   └── Ease.cs                 缓动（含 OutBack / OutElastic 的过冲外推）
├── Tracks/
│   ├── MotionTrack.cs          轨道 = 路径 + 时间 + 通道
│   ├── AnchorResolver.cs       锚点延迟解析 + 跨父级 Reparent
│   └── Modifiers/              Noise / Clamp / Deadzone / Blend / SpeedStretch
├── Coordination/
│   ├── StaggerPlan.cs          L0
│   ├── MotionRig.cs            L1（含 softness 软编队）
│   ├── FollowChain.cs          L2（弧长回溯环形缓冲）
│   ├── SpringCoupling.cs       L3（+ 距离约束 relax）
│   ├── VectorFieldDriver.cs    L4（含 SDF 梯度/切向场）
│   └── ConstraintConverge.cs   终点吸附 / 最小间距 / 边界
├── Bind/
│   ├── RectBinder.cs           唯一写入者
│   └── GooeyMotionFeedback.cs  间距曲线 + 速度 + 静止脏标记
├── Integration/
│   ├── UIPanelMotion.cs        接到 UIBase.OnOpened/OnClosed
│   └── MotionOwnership.cs      写入权静态声明与冲突报错
└── Editor/
    ├── MotionPathAssetEditor.cs    路径画点与预览
    ├── MotionGizmoDrawer.cs        Scene 视图画路径/切线/速度色带
    ├── MotionValidator.cs          冲突检测（Animator 曲线、LayoutGroup、DOTween 同属性）
    └── MotionTimelineWindow.cs     时间轴拖动 / Seek / 逐帧回放（P4）

Assets/A_UI/UI/Package/Navigation/UINavigationQueue.cs   导航意图队列（§8.5，归导航包）
Assets/Editor/MotionTests.cs       Editor 测试（见 §13）
```

---

## 13. 测试与验收清单

**单元测试（Editor，无 Play Mode 依赖）**

1. 弧长参数化：`Ease.Linear` + 三次 Bezier，等时间采样点间距 `std/mean < 2%`。
2. 拼接连续性：`PathChain` 相邻段端点误差 `< 1e-4`；G1 模式下切线夹角 `< 0.5°`。
3. 过冲外推：`Ease.OutBack` 下 `u<0` / `u>1` 的采样点落在起点切线/终点切线的延长线上。
4. 确定性：同一 `t` 两次采样逐位相等；不同 `Time.deltaTime` 序列下同 `t` 结果相同。
5. 弹簧收敛：给定目标，`ω/ζ` 各配方在预期时间内进入 `ε` 且不超调超过配方预期；从任意初始速度不发散。
6. 约束 relax：链式距离约束迭代 4 次后间距误差 `< 0.5px`；不产生 NaN（含 scale=0 退化输入）。
7. FollowChain：直线与急弯（半径 < 间距）两种情况下的间距均匀性。
8. Stagger：`Grid` / `Radial` 延迟顺序与预期索引排列一致；`TotalDuration` 归一化后末项不超时。
9. 锚点：在 rect 尺寸变化后（模拟布局重建）终点自动跟上，无需重新 `Play`。
10. Handoff：打断后位置连续（相邻帧位移 `< 速度上限 * dt`），无跳变。
11. Skip：`Skip` 后 `IsDone == true` 且 `OnComplete` 被调用一次（且仅一次）。

**导航队列测试（Editor，用假 `UIBase` + 可控时长的转移，不进 Play Mode）**

12. 转移期间 `Push` 入队：返回 `true` 表示已接受；`Current` 不变；`Navigated` **不**触发；转移结束后才执行且只执行一次。
13. 连点两次 `Back` 只回一层是**错误**行为 → 断言队列执行完后回退两层（这是"排队而非忽略"的核心验收）。
14. 连续 `Push(同一 target)` 折叠为 1 条：`Navigated` 只触发一次，栈深度只 +1。
15. `Back` ×5 的 fast-forward：前 3 条播动画、后 2 条 instant（断言第 4 条起未产生 `IsTransitioning == true` 的时段）。
16. `ToRoot` 清空队列：先入队 2 条 `Push`，再 `ToRoot` → 断言那 2 条被丢弃、栈落在根。
17. 出队时重新校验：入队 `Push(某子面板)` 后把该面板从树里摘掉 → 断言该条被丢弃 + `LogWarning` + **后续条目仍正常执行**（队列不卡死）。
18. 看门狗：故意让某面板的转移完成回调不触发 → 断言 deadline 后 `ForceComplete` + `LogError` + 队列继续；`timeScale = 0` 下用 unscaledTime 判定、不误超时。
19. 转移期间"离开面板"的 `blocksRaycasts` 为 false，完成/超时后还原。
20. `maxQueue` 溢出丢弃最新一条并告警，已排队顺序不变。

**场景验收（Editor 手工）**

- 三种 ReferenceResolution（1920×1080 / 2560×1080 / 1440×1080）下曲线形状一致；
- `EditorApplication.isPaused` 时不推进；恢复后不跳变；
- `Time.timeScale = 0`（暂停菜单）下 UI 动效按配置表现（可暂停 / 不受影响，二者都要能配）；
- 30 / 60 / 144 fps 下观感一致（帧率无关性）；
- 运动期间 Gooey 融合桥单调变化、不闪；
- 运动期间元素之间无穿透（约束层生效）；
- 静止后帧率恢复（脏标记生效）。

**构建验收**

- Editor 程序集编译通过；`Assets/Editor` 测试全绿；`git diff --check` 干净。
- Unity 内的 Play Mode 视觉验收需在 Editor 完成（本机无 Unity 可执行文件时不可跳过）。

---

## 14. 落地阶段

| 阶段 | 交付 | 验收 |
|------|------|------|
| **P0 最小闭环** | `MotionClock` + `Line`/`CubicBezier` + `Ease` + `RectBinder` + `AnchorRef`（同父级）+ `UIBase` 异步化 + `UINavigationQueue`（§8.4/§8.5） | 能替换掉 `UIBase` 的硬切换，做出"面板直线滑入 + 元素贝塞尔飞入"；连点返回不丢层；测试 1/3/4/9/12–20 通过 |
| **P1 曲线完备** | `PathChain` + G1 拼接 + 弧长 LUT + `Arc`/`CatmullRom` + Scene Gizmo | 测试 2 通过；三种分辨率形状一致；编辑器可画点创作 |
| **P2 协同基础** | `StaggerPlan` + `MotionRig` + `FollowChain` + 跨父级 Reparent | 测试 7/8 通过；完成 §7.7 示例场景 |
| **P3 动力学** | `SpringCoupling` + 约束层 + `Handoff` + `VectorFieldDriver` | 测试 5/6/10 通过；黏液拖尾观感验收 |
| **P4 编辑器与联动** | Timeline 窗口（Seek/回放）+ `MotionValidator` + `GooeyMotionFeedback`（blur 补偿 + 脏标记） | 测试 11 通过；Gooey 桥不闪、静止帧率恢复 |

每阶段结束都必须是"可用状态"：P0 就能替换现有硬切换，P2 就能覆盖绝大多数 UI 动效需求。

---

## 15. 风险与未决问题

| 风险 / 问题 | 影响 | 应对 |
|------------|------|------|
| **导航与异步关闭的交互** | ~~未定义~~ **已定：排队**（§8.5） | 队列本身已定型；剩余是调参与契约铺开：`fastForwardThreshold` / `maxQueue` 需实测手感，`Push` 返回值语义与 `Current` 滞后需审查所有调用方（已知 `UINavButton` 零改动） |
| 看门狗在 `timeScale = 0` / 面板被禁用时误判超时 | 暂停菜单里可能被"强制完成"，或反之卡死 | 判定顺序固定为 `IsDone` → 句柄失效 → unscaledTime 兜底；测试 18 覆盖 |
| 状态化通道（弹簧）与 Seek/回放的语义冲突 | 回放时弹簧结果与实时不同 | 已在 §10.2 定为"固定步长重演 + 步数上限 + 超限降级并告警"，需在 P3 实测重演成本 |
| 与 LayoutGroup 的所有权争夺 | 运动中突然跳回槽位 | 静态声明 + `ignoreLayout` + Validator 报错；不接受"运行时动态争抢" |
| 约束层引入的复杂度可能"反噬"（元素开始互相推挤、抖动） | 观感变差、调试成本高 | 约束默认关闭，按需 opt-in；提供逐元素 `lockPosition` 排除项 |
| Gooey blur 补偿需要改动 `GooeyUIGroup` | 触碰已验证的视觉代码 | 补偿是**可选输入通道**（未接时行为与现在完全一致），并加 Editor 测试保证默认路径零回归 |
| 项目已装 DOTween 但未使用 | 团队可能下意识用 DOTween 实现协同需求 | §8.6 的边界表写进文档与 `MotionOwnership` 报错信息里 |
| 性能预算目前是估算，非实测 | 可能低估 | P0 结束时用 Profiler 校准并回写本文档 |
