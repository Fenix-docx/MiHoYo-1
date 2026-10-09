# 执行任务：UIMotion P1（曲线完备）—— 在 P0 基础上增量实现

你是实现者。仓库：`E:\Projects_Unity\MiHoYo`（Unity 6000.3.24f1，uGUI，C# 9）。
规格来源（唯一权威）：`E:\Projects_Unity\MiHoYo\docs\ui-motion-module-design.md`。

P0 已交付并通过验收（编译双工具链 / 纯逻辑 25 项 / Unity EditMode 24 项）。本轮实现 **P1**，验收标准见文档 §14 的 P1 行。

---

## 0. 现场状态（很重要，先读这段再动手）

1. **用户的 Unity Editor 正在运行并持有工程锁**（`Temp\UnityLockfile` 存在）。
   → **严禁启动 Unity**（任何形式，包括 batchmode）、严禁删除锁文件、严禁碰 `Library/`、`Temp/`、`ProjectSettings/`、`.plastic/`。
   → Unity 侧的 EditMode 测试由验收方（另一个 agent）在用户关闭 Editor 后统一执行，**不是你的事**。
2. **Motion 目录下的文件今天被人（用户）手动改过**，最近的一次是 `Integration/UIPanelMotion.cs`（13:57:29）。
   → 这些是**未提交的人工改动**（整个 `Assets/A_UI/UI/Motion/` 还是 `??` 未纳入版本库，没有回滚网）。
   → 你必须**保留**这些改动，只做增量；**不允许**重排版、不允许重命名既有成员、不允许"顺手重构"。
3. **动手前强制备份**：任何你要修改的既有文件，先复制到 `.dsh-scratch/backups/p1/<相对路径>`，并在报告里记录"修改前的 SHA256"。新建文件不需要。
4. 不要用 git（不 add / 不 commit / 不 stash）。不要写 `.meta`（Unity 导入时会自动生成）。不要改设计文档与 `docs/ui-motion-usage.md`。

---

## 1. P1 范围（文档 §14 的 P1 行）

> `PathChain` + G1 拼接 + 弧长 LUT + `Arc`/`CatmullRom` + Scene Gizmo
> 验收：测试 2 通过；三种分辨率形状一致；编辑器可画点创作

具体拆成五块：

### A. 段类型补全（`Path/PathSegment.cs`，该文件最后修改于 10/6，不在用户今天的改动里）

现只有 `Line` 与 `CubicBezier`。按文档 §6.1 补 `QuadBezier`、`Arc`、`CatmullRom`：

| 类型 | 参数 | 要求 |
|---|---|---|
| `QuadBezier` | p0, c, p1 | 精确解析求值 + 精确切线 |
| `Arc` | center, radius, a0, a1, ellipseScale | **解析求值与解析弧长**（不要用 LUT 近似弧长）；`ellipseScale` 实现椭圆弧 |
| `CatmullRom` | p0..p3 | 曲线**必须穿过 p1 与 p2**（这是它存在的意义）；切线用标准 CR 切线 |

- 段结构必须保持 **blittable**（无托管引用、无装箱），`Evaluate`/`Tangent` 保持 `switch` 分发。
- `Arc` 的参数如何编码进这个 struct 由你决定，但必须：不引入托管字段、语义写进注释、并保证 `Evaluate(0)` == 起点、`Evaluate(1)` == 终点。
- `Tangent(t)` 必须返回**单位向量**（现有实现已归一化，并有退化回退；新增类型同样要有退化回退，不能返回 NaN）。
- `SdfContour` **不在本轮**（文档把它归在 L4 场驱动的几何化形式）。

### B. `Path/PathChain.cs`（新建）

- 由若干段拼成，提供 `Evaluate(u)` / `Tangent(u)` / `EvaluateByArc(u)` / `TotalLength`。
- **C0 拼接**：相邻段端点自动对齐（后段起点吸附到前段终点）。
- **G1（可选模式）**：交点两侧切线强制共线——按文档 §6.1 的做法调整控制点（Bezier 段把 `c0` 设为 `p0 + (p_prev - p_next)/6` 的镜像形式）；G1 关闭时保持各段原样。
- **累积弧长 LUT**：跨段累积；`EvaluateByArc` 用二分 + 线性插值。每段分辨率按类型给（文档 §6.1 建议 Line:2 / Bezier·CR:32 / Arc:解析）——**注意 P0 的 `MotionMath.BuildArcLut` 对三次曲线实际用了 257 点**，你要保证新链条的匀速性验收（见 §3 测试）能过，并在报告里说明你最终选的分辨率与理由。
- 段退化（零长段、控制点重合）不能产生 NaN 或除零。
- **不允许在 `Sample`/采样路径上分配托管内存**（LUT 在构造时建好；`EvaluateByArc` 不得每次 new 数组）。采样路径上不要用 LINQ、闭包、装箱。

### C. 接入 `MotionTrack` / `MotionPlan`（这两个文件**今天被用户改过** → 只做最小增量）

- `MotionPlan` 需要能接受 `PathChain`（例如新增 `Curve(PathChain)` 重载或等价入口）。
- **P0 的 `Curve(PathSegment)` 必须继续可用**，锚点式 `From/To` 也必须继续可用 —— 既有 24 个 Unity 测试是回归闸门，全部必须仍然通过（你无法运行它们，所以改动要保守）。
- 过冲外推（文档 §6.2）在链条层面要成立：`u<0` 沿**首段起点切线**外推，`u>1` 沿**末段终点切线**外推；`ClampOutside(true)` 时钳制。
- 保持 P0 的"位置是时间的纯函数"性质：`Sample(t)` 不依赖帧历史。

### D. 创作路径（文档 §6.3 的第 1、2 条 —— P1 验收里的"编辑器可画点创作"）

1. **代码 DSL**（`Path/PathBuilder.cs`，新建）：至少提供文档点名的两种形态：
   - `Path.Through(points)` —— 一串点自动生成平滑的 `CatmullRom` 链；
   - 一个"给定两端点 + 弧高生成弧线"的便捷入口（文档写作 `CubicBezier.Arc(arcHeight)`，命名你定但要直观）。
2. **编辑器资产与画点**（`Path/MotionPathAsset.cs` + `Editor/` 下的编辑与绘制）：
   - `MotionPathAsset`：ScriptableObject，按**p 空间**存控制点（与运行时一致），提供 `BuildChain()` 产出 `PathChain`；
   - Scene 视图可视化：路径折线、控制点、切线箭头、方向指示（这就是文档说的 Scene Gizmo）；
   - **可在 Scene 视图拖点编辑**：选中资产后显示可拖拽手柄，拖动即写回资产，**必须支持 Undo**（`Undo.RecordObject`）；
   - 资产改动后 `PathChain` 缓存要能重建（别把陈旧缓存喂给运行时）。

### E. 测试（Editor 测试，写进 `Assets/Editor/`）

文档 §13 测试 2：**`PathChain` 相邻段端点误差 `< 1e-4`；G1 模式下切线夹角 `< 0.5°`**。
另外必须覆盖（对应 §14 的"三种分辨率形状一致"）：

- **链条匀速性**：多段链（含贝塞尔 + 弧 + CR）等时间采样 100 点，相邻点间距 `std/mean < 2%`；
- **弧的解析弧长**：`Arc` 段 `TotalLength` 与解析弧长公式的相对误差 `< 0.1%`；
- **CatmullRom 穿过控制点**：`p1`/`p2` 对应的参数处采样点与输入点误差 `< 1e-4`；
- **三种分辨率形状一致**：同一条 p 空间路径，在父 rect 高为 1080 / 1080（宽 1920/2560/1440）三种情况下采样，按父高归一化后**形状相同**（顶点两两误差 `< 1e-3`）；这条可以直接在 EditMode 测试里用假 RectTransform 断言，不需要真机；
- **链条过冲外推**：`Ease.OutBack` 下 `u>1` 的采样落在末段终点切线延长线上；
- **既有 P0 测试不许退化**：不要修改 P0 已有测试的断言（如需新增用新方法）。

---

## 2. 硬性约束（与 P0 相同，继续有效）

1. **向后兼容**：P0 的公开 API 与默认行为不许变（`MotionPlan.From/To/Curve(PathSegment)/Duration/Ease/Clock/Bind/ClampOutside/Start`、`MotionHandle`、`AnchorRef`、`Ease`、`AnchorResolver`、`RectBinder`、`MotionSystem`、`UIPanelMotion`）。
2. **不要改**：`docs/ui-motion-module-design.md`、`docs/ui-motion-usage.md`、`Assets/Scripts/Gooey*`、`Assets/A_UI/UI/Package/UIbase.cs`、`Assets/A_UI/UI/Package/UIManager.cs`、`Assets/A_UI/UI/Package/Navigation/*`（P1 不涉及导航）。
3. **不要启动 Unity**（见 §0.1）。
4. **C# 9**：禁止 file-scoped namespace、raw string、`required`、collection expressions。
5. **注释风格**：跟随被改文件自身风格（中文注释、解释"为什么"）；新文件同样中文注释。
6. **不要删/改 `.dsh-scratch/codex-review/` 下验收方的工件**；你的本轮报告写到 `.dsh-scratch/codex-review/p1-report.md`（新文件）。

---

## 3. 证据要求（决定验收通过与否）

A. **编译验证**：两个程序集都要通过，且**自证新文件真的在编译范围内**（沿用你 P0 的做法：临时 `*-Verify.csproj` 副本 + 逐文件 `#error` 探针 + 恢复；**错误探针绝不能注入 `Assets/`**，因为用户的 Editor 正处于打开状态、会立刻导入坏代码）。给出命令与原始输出。
B. **运行验证（必须有真实执行输出）**：扩展或新建你 P0 的控制台证据程序，实际跑出：
   - 链条 C0 端点误差、G1 切线夹角；
   - 多段链的匀速性 `std/mean`；
   - `Arc` 解析弧长误差；
   - `CatmullRom` 穿过控制点误差；
   - 三种分辨率下归一化形状一致性；
   - 链条过冲外推共线性。
   粘贴原始输出（含 ResULT 行）。
C. **Unity EditMode 测试**：写进 `Assets/Editor/`，**只编译不执行**（用户的 Editor 占着锁，禁止你再起一个）。报告里必须明确写"已写、已随 Assembly-CSharp-Editor 编译通过、未在 Unity 中执行"，不许含糊成"通过"。
D. **明确区分"已验证 / 未验证"**。任何没做到的、跑失败的都如实写，不要美化。验收方会独立复跑你的命令。

---

## 4. 报告格式（写到 `.dsh-scratch/codex-review/p1-report.md`）

```
# UIMotion P1 实现报告
## 1. 交付清单（每个文件：路径 / 行数 / 一行职责 / 新建还是修改）
## 2. 与设计文档的对应（§6.1/§6.2/§6.3/§12/§13-2/§14-P1 逐条 → 落点）
## 3. 我对既有文件的改动清单 + 修改前 SHA256（含备份路径）
## 4. 我的设计判断与偏离（文档没说清或你认为不对的地方）
## 5. 段类型的参数编码与 LUT 分辨率选择（含理由）
## 6. 验证证据
   6.1 编译（命令 + 原始输出 + 新文件确实被编译的反证）
   6.2 运行（命令 + 原始输出）
   6.3 Editor 测试（已写/已编译/未执行）
## 7. 已知问题与未实现
## 8. 与其他系统的接触面（特别是有没有碰到用户今天改过的文件，改了哪几行）
```

## 5. 交付判据

- 既有 24 个 Unity 测试的语义不被破坏（代码层面可核对）；
- 新文件的编译有反证、纯逻辑有真实运行输出；
- §13 测试 2 与"三种分辨率形状一致"都有对应断言（Editor 测试里）；
- 编辑期画点创作**真能用**（Scene 手柄 + Undo + 写回资产），不是只有 Gizmo 画线；
- 报告如实、可复跑。
