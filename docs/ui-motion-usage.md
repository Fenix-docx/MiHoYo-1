# UIMotion P0 配置手册

> 配套文档：设计规格 `docs/ui-motion-module-design.md`（§8.4/§8.5/§12 是本手册的依据）。
> 代码位置：`Assets/A_UI/UI/Motion/`（命名空间 `UIPackage.Motion`）+ `Assets/A_UI/UI/Package/Navigation/UINavigationQueue.cs`。
> 证据状态：编译（双工具链）、纯逻辑 25 项、Unity EditMode 24 项均已验证通过。

---

## 0. 全景：什么要手配，什么自动

| 类型 | 配置方式 | 说明 |
|---|---|---|
| `UIPanelMotion` | **Inspector 手加**（挂在面板根上） | 唯一实际需要加的组件。**不加 = 行为与改造前完全一致**（同步开关，已被 `DefaultPanelPathRemainsSynchronous` 测试守住） |
| `UINavigator` | Inspector 上 3 个新字段 | `maxQueue` / `fastForwardThreshold` / `blockEnteringRaycasts` |
| `MotionOwnership` | 可选，Inspector 手加 + 选 Owner | 用来**提前声明**某 rect 归 DOTween/Animator 写；Motion 之后就不会碰它 |
| `MotionSystem` | **不要手加** | 首次 `MotionPlan.Start()` 时自动创建 `UIMotion System` 对象（Play 模式 `DontDestroyOnLoad`），已标 `[DefaultExecutionOrder(-100)]` |
| `MotionClock` / `MotionPlan` / `PathSegment` / `Ease` / `AnchorRef` | 纯代码 | 都不是组件 |

**不需要做的事**：不用往场景里放管理器；不用改 `GooeyUIGroup` 的执行顺序（Motion 在 `Update` 跑，Gooey 仍在 `LateUpdate`，相位关系由 Unity 保证）；不用改任何既有场景/预制体；不用加 asmdef。

---

## 1. 面板进出场（最常用，零代码）

1. 选中面板根 GameObject（**必须与 `UIBase` 在同一个 GameObject 上**，因为 `UIBase.PlayOpen/PlayClose` 用 `GetComponent<UIPanelMotion>()` 取）；
2. `Add Component` → **`UIPanelMotion`**（会用 `[RequireComponent(typeof(RectTransform))]` 自动补 RectTransform）；
3. 面板的父级必须是 RectTransform（否则启动时抛 `InvalidOperationException`）。

### 字段

| 字段 | 默认 | 含义 |
|---|---|---|
| `enterOffset` | `(2, 0)` | 进场起点相对 home 的偏移 |
| `exitOffset` | `(-2, 0)` | 退场终点相对 home 的偏移 |
| `duration` | `0.3` | 秒 |
| `useUnscaledTime` | `true` | 用非缩放时间；与导航看门狗（unscaled 判定）自洽，暂停菜单下动画照跑 |

### ★ 单位是 p 空间，不是像素

偏移量在**父级的 p 空间**（高 = 1 的等比空间，同 `SdfUiSpace` 的约定）里解释：

| 想要的效果 | 填 |
|---|---|
| 从右侧一个父高之外飞入 | `enterOffset = (2, 0)`（默认：+2 等于向右一个父高） |
| 从左侧飞入 | `enterOffset = (-2, 0)`，并把 `exitOffset` 改成 `(2, 0)` |
| 从上方落下 | `enterOffset = (0, 1)` |
| 只滑出半个父高 | `exitOffset = (-1, 0)` |

好处：换分辨率 / 宽高比时位移量等比缩放，不会出现"1600 宽下飞出屏幕、4K 下只挪一点"。

### 行为细节

- 面板的 anchor/pivot 都被正确补偿（`RectBinder` 往返恒等，含拉伸锚点）。
- `home` 在**首次播放或首次 Snap 时捕获**（当时的 `anchoredPosition`）。运行中若改变了面板的布局基准，需要重建配方或改用 §3 的 `AnchorRef` 轨道（已知限制 D3）。
- 反悔（退场到一半又打开）会从**当前位置**续接，不跳变。
- instant 路径（连点返回的第 4 条起）不创建轨道，直接写终态。

---

## 2. 导航队列（`UINavigator` 上的 3 个新字段）

| 字段 | 默认 | 含义 |
|---|---|---|
| `maxQueue` | `8` | 待执行意图上限；超出丢弃**最新**一条并 `LogWarning` |
| `fastForwardThreshold` | `3` | 连点 `Back`：第 4 条起改走 instant（跳过动画直接切） |
| `blockEnteringRaycasts` | `false` | 默认只屏蔽"正在离开"的面板；打开后进场面板也一并屏蔽（防连点） |

既有的 `openRootOnStart` / `closeAllOnAwake` / `restrictToDirectChildren` 语义未变。

### 必须知道的契约变化

| 契约 | 现在 |
|---|---|
| `Push/Back/Replace/ResetToRoot` 返回值 | **"已接受"**（可能只是入队），不再等于"已完成" |
| 返回后立刻读 `Current` | 仍是**即将离开**的面板（`activeSelf == true`） |
| `Navigated(from,to)` | 在**出队执行时**触发（入队不触发）；`CloseAll` 沿用旧行为不发 |
| 等真正落定 | `nav.NavigationIdle += ...`，或读 `nav.IsNavigating` |

`UINavButton` **不需要改**（它不消费返回值）。需要审查的是任何"Push 完立刻读 `Current` / 立刻再 Push"的自写代码。

---

## 3. 写入权仲裁（你那份 DOTween 按钮脚本会撞上这条）

规则：**同一 `RectTransform` 的 `anchoredPosition`，同一时刻只能有一个所有者。**

| 场景 | 做法 |
|---|---|
| 该元素**只用 DOTween**（例如 `Assets/A_UI/SlimeButtonMotion.cs` 那一类） | Inspector 给它的 GameObject 加 `MotionOwnership`，`Owner` 选 `DOTween`。之后 Motion 启动时 `Acquire` 会 `LogError` 拒绝，不会静默抢写 |
| 该元素**只用 Motion** | 什么都不用加（运行时自动挂 `MotionOwnership`，Owner = `Motion`） |
| 先 DOTween 后 Motion | 先 `tween.Kill()`（或把该组件停掉），再 `MotionPlan.Start()` |
| 该元素既不用 DOTween 也不用 Motion | 不用管 |

会被自动拒绝并 `LogError` 的两种情形：
1. 目标自身挂着 **enabled 的 `Animator`**；
2. 目标本身就是某个 **`ScrollRect.content`**（内部子节点相对位移是允许的）。

### 与 LayoutGroup 的关系

被运动元素的父级若有 `LayoutGroup`，运动期间系统会自动加 `LayoutElement` 并置 `ignoreLayout = true`，轨道结束时还原原值并 `LayoutRebuilder.ForceRebuildLayoutImmediate`。
→ **副作用：动画终点会被布局吸附回槽位**，这是设计规定的行为（不是 bug）。

---

## 4. 代码侧：自定义位移轨道

### 4.1 直线（锚点式，推荐）

```csharp
using UIPackage.Motion;

var h = new MotionPlan()
    .From(new AnchorRef(slotRect, AnchorEdge.Right, new Vector2(0.05f, 0)))  // 槽位右侧 5% 短边
    .To(new AnchorRef(targetRect))                                           // 默认 Center
    .Duration(0.35f)
    .Ease(Ease.CubicOut)
    .Bind(targetRect)        // 被移动的 rect
    .Start();
```

`AnchorRef(RectTransform target, AnchorEdge edge, Vector2 offset, RectTransform space, int slotIndex)`

| 参数 | 说明 |
|---|---|
| `target` | 作为锚点的 rect；传 `null` 表示**绑定对象的父级**本身 |
| `edge` | `AnchorEdge`：`Center`（默认）/ `Left` / `Right` / `Top` / `Bottom` / `Slot` |
| `offset` | **按目标短边比例**（不是像素）：`0.05` = 短边的 5% |
| `space` | 必须是 `null` 或等于绑定对象的父级（P0 不支持跨父级） |
| `slotIndex` | 仅 `edge == Slot` 时用：取 `target` 的第 N 个直接子节点 |

锚点在**每一帧采样时**重新解析 → 布局变化（动态文本宽度、分辨率切换）会自动跟上。

### 4.2 曲线

`Curve` 的控制点坐标同样是**父级的 p 空间**，用公开的换算工具：

```csharp
var parent = (RectTransform)targetRect.parent;
Vector2 p0 = AnchorResolver.ToP(localStart, parent);   // 父局部坐标 → p 空间
Vector2 p1 = AnchorResolver.ToP(localEnd,   parent);

var h = new MotionPlan()
    .Curve(PathSegment.CubicBezier(p0, p0 + new Vector2(0.8f, 0.6f), p1 + new Vector2(-0.8f, 0.6f), p1))
    .Duration(0.5f)
    .Ease(Ease.OutBack(1.2f))     // 过冲：沿端点切线外推
    .ClampOutside(false)          // 想禁止越界就传 true
    .Bind(targetRect)
    .Start();
```

`PathSegment.Line(from, to)` 与 `PathSegment.CubicBezier(p0, c0, c1, p3)`（P0 只有这两种；Arc/CatmullRom/SDF 轮廓在 P1）。

### 4.3 缓动

`Ease.Linear` / `Ease.CubicOut` / `Ease.QuintOut` / `Ease.OutBack(overshoot = 1.70158f)` / `Ease.OutElastic(amplitude = 1)`

缓动**不钳制输出**：`OutBack` / `OutElastic` 会返回 `u > 1`，几何层沿终点切线外推 —— 这就是"过冲再回落"。不想越界加 `.ClampOutside(true)`。

### 4.4 句柄

```csharp
h.IsValid / h.IsDone / h.Progress / h.ExpectedDuration
h.OnComplete(() => { ... });     // 完成回调；Kill(false) 不触发
h.OnUpdate(sample => { ... });   // 每帧样本（含 velocity）
h.Kill(snapToEnd: true);         // 立即结束并吸附终态（等价 Skip）
h.Kill(snapToEnd: false);        // 停在当前位置，不触发完成回调
h.Skip();                        // = Kill(true)，**会触发** OnComplete
```

---

## 5. P0 的硬限制（配错会抛异常或 LogError）

| 触发条件 | 结果 |
|---|---|
| `Bind` 的目标没有 RectTransform 父级 | `InvalidOperationException` |
| 既没给 `Curve` 也没给 `From` + `To` | `InvalidOperationException` |
| 锚点的 `space` 不是绑定对象的父级，或锚点不是父级的直接子节点 | `ArgumentException`：P0 不支持跨父级 |
| `edge == Slot` 且 `slotIndex` 越界 | `ArgumentOutOfRangeException` |
| 同一 rect 上已有活动轨道 | `LogError`：请先 `Kill` 再接管 |
| 目标自身有 enabled 的 `Animator` | `LogError`：拒绝启动 |
| 目标就是 `ScrollRect.content` | `LogError`：拒绝启动 |
| `allowUnregisteredPush`…（无此开关） | 游离面板（不在导航树里）在"栈为空"或 `restrictToDirectChildren = false` 时可 Push 成功（保持改动前语义） |

**只写 `anchoredPosition`**：`MotionSample` 里的 `rotation` / `scale` / `weight` 目前只是数据，不写 Transform（P0 范围）。

---

## 6. 排查清单

| 症状 | 原因 / 处理 |
|---|---|
| 面板直接切换、没有动画 | 面板根上没挂 `UIPanelMotion`（这是正常的向后兼容行为） |
| 飞入方向/距离不对 | `enterOffset` 单位是 p 空间（父高 = 1），不是像素 |
| `LogError` 所有权冲突 | 同一 rect 被 DOTween/Animator 与 Motion 同时写 → 见 §3 |
| 面板滑向奇怪位置（父中心） | 父 rect 由布局驱动、首帧前 `rect.height ≈ 0` → 已知限制 D3，先让布局跑完再播 |
| 动画结束后元素弹回原位 | 父级有 `LayoutGroup` → 设计行为（结束时还原 `ignoreLayout` 并重排） |
| 连点返回只回一层 | 不该发生；若出现记录 `maxQueue` / `fastForwardThreshold` 的实际值 |
| 暂停菜单（`timeScale = 0`）里动画被瞬间切完 | 该面板配方被改成 scaled 时间 → 看门狗在 `3 × duration + 0.5s`（unscaled）后强制完成 |
| 曲线形状在不同分辨率下不一致 | 控制点应存在 p 空间（用 `AnchorResolver.ToP`），不要存像素 |
| 编辑器里改脚本后场景变脏 | `MotionSystem` 只在首次 `Start()` 时自动创建；不要在编辑模式下调用 `Start()`（已知提示 D7） |

---

## 7. 建议的落地顺序

1. 先给 **1 个**面板挂 `UIPanelMotion`，用默认值跑一遍进/退场（含连点返回、退场中反悔）；
2. 确认观感后统一填 `duration`（建议 0.25–0.35s）与方向；
3. 给 `UINavigator` 显式填 `maxQueue` / `fastForwardThreshold`（别依赖默认值，方便后来人看懂意图）；
4. 若有元素自己用 DOTween 做位移动画，给它加 `MotionOwnership`（Owner = `DOTween`），把冲突前置暴露；
5. 需要曲线/多元素协同效果时再写 `MotionPlan`（P0 只有直线与三次贝塞尔；PathChain、协同层 L0–L4 在 P1/P2）。
