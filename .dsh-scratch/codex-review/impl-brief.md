# 执行任务：实现 UIMotion P0（按设计文档，一次性交付 + 可审计证据）

## 0. 你的身份与产出

你是本任务的**实现者**。仓库：`E:\Projects_Unity\MiHoYo`（Unity 6000.3.24f1，uGUI，C# 9）。
规格来源（**唯一权威**）：`E:\Projects_Unity\MiHoYo\docs\ui-motion-module-design.md`。先完整读一遍，再动手。

你要交付 **P0 阶段**（文档 §14 表格里的 P0 行），**不要**顺手做 P1–P4，也不要为 P1–P4 建空壳文件。

产出两份东西：
1. 代码（见 §2 清单）；
2. 实现报告：写到 `E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\impl-report.md`，内容格式见 §5。

## 1. 环境事实（已实测，按此办事）

- `dotnet 10.0.401` 可用；`dotnet build Assembly-CSharp.csproj --nologo -v m` 在本仓库**基线 5.7 秒通过**，0 错误、3 个既有警告（SlimeGpuSurfacePipeline 的 CS0649×2、Slime_ControllerMotor 的 CS0414）。这三个警告是**既有的**，不要试图修。
- `Assembly-CSharp-Editor.csproj` 存在，内含 `nunit.framework` 引用（`Library\PackageCache\com.unity.ext.nunit@d8c07649098d\net40\unity-custom\nunit.framework.dll`），并已包含 `Assets\Editor\GooeyUIGroupPhase2Tests.cs`。test-framework 1.6.0 已装。
- Unity 版本 6000.3.24f1，安装于 `C:\Users\12118\Unity\6000.3.24f1\Editor\Unity.exe`。
- **⚠️ 一个 Unity Editor 正开着本工程（pid 15944）**。绝对不要启动 Unity（batchmode 也不行，会锁冲突并打断用户），不要 kill 任何 Unity 进程，不要碰 `Library\`、`Temp\`、`ProjectSettings\`、`.plastic\`。
- **⚠️ 最大的证据陷阱**：`Assembly-CSharp.csproj` / `Assembly-CSharp-Editor.csproj` 是 Unity 生成的，用**显式 `<Compile Include="...">` 列表**。你新增的 .cs 文件**不会**自动进去。你若直接 `dotnet build Assembly-CSharp.csproj`，新文件会被**静默跳过**，于是"编译通过"是**假的**。你必须解决这一点（推荐做法见 §4）。

## 2. 必须实现的文件（路径即规格，命名空间与 §12 一致）

按文档 §12 的文件清单，**只建 P0 需要的这些**：

运行时代码（命名空间 `UIPackage.Motion`）：
- `Assets/A_UI/UI/Motion/Core/MotionClock.cs` —— 时间源（pause/resume/timeScale/Seek/父子时钟共享）
- `Assets/A_UI/UI/Motion/Core/MotionTypes.cs` —— `MotionSample`、`AnchorRef`（§4.2 的 edge/offset/space）、`BindSpace`/`PathSpace`
- `Assets/A_UI/UI/Motion/Core/MotionHandle.cs` —— struct 句柄（`IsValid`/`IsDone`/`Progress`/`Kill`/`Skip`/`OnComplete`/`OnUpdate`），指向内部池槽位
- `Assets/A_UI/UI/Motion/Core/MotionSystem.cs` —— 调度器：注册、更新相位（§8.2：在 `Update` 推进）、池的存活回收
- `Assets/A_UI/UI/Motion/Core/MotionMath.cs` —— 纯函数：三次贝塞尔求值/切线、弧长 LUT 与二分查找、缓动、外推
- `Assets/A_UI/UI/Motion/Path/PathSegment.cs` —— P0 只要 `Line` + `CubicBezier`，但**结构必须能扩**到 §6.1 的其余类型（blittable struct + tag + switch，不要写成只能放两种段的死结构）
- `Assets/A_UI/UI/Motion/Path/Ease.cs` —— 含 `Linear`/`CubicOut`/`QuintOut`/`OutBack(overshoot)`/`OutElastic(overshoot)`；**必须支持 u<0 与 u>1 的过冲外推**（§6.2：沿端点切线延长）
- `Assets/A_UI/UI/Motion/Tracks/AnchorResolver.cs` —— 锚点**在采样时刻**解析（§4.2）；P0 只支持同父级；跨父级 Reparent 留 TODO 注释但**不要实现**
- `Assets/A_UI/UI/Motion/Tracks/MotionTrack.cs` —— 轨道 = 路径 + 时长 + 缓动 + 通道
- `Assets/A_UI/UI/Motion/Bind/RectBinder.cs` —— 唯一写入者，只写 `anchoredPosition`
- `Assets/A_UI/UI/Motion/Integration/MotionOwnership.cs` —— 写入权静态声明与冲突报错（§8.1；报告风格参考 `UIManager` 对重复 PanelId 的处理：宁可 `LogError`，不静默取第一个）
- `Assets/A_UI/UI/Motion/Integration/UIPanelMotion.cs` —— 面板进出场的运动配方（接到 UIBase 的 PlayOpen/PlayClose）
- `Assets/A_UI/UI/Motion/MotionPlan.cs` —— 链式构建器（§11 的 API 草图，P0 子集：`From/To/Curve/Duration/Ease/Clock/Bind/Start`）

导航侧：
- `Assets/A_UI/UI/Package/UIbase.cs` —— **改造**为异步（§8.4 的代码骨架：`PlayOpen()/PlayClose()` 返回**可空** `MotionHandle?`、`IsTransitioning`、`TransitionCompleted` 通知）
- `Assets/A_UI/UI/Package/Navigation/UINavigationQueue.cs` —— 新建，实现 §8.5 全部规则
- `Assets/A_UI/UI/Package/Navigation/UINavigator.cs` —— **改造**接入队列（`Push/Back/Replace/ResetToRoot/CloseAll`、看门狗、输入屏蔽、`IsNavigating`、`NavigationIdle`）

测试：
- `Assets/Editor/MotionTests.cs` —— 文档 §13 的测试 **1 / 3 / 4 / 9**
- `Assets/Editor/UINavigationQueueTests.cs` —— 文档 §13 的测试 **12–20**

## 3. 硬性约束

1. **向后兼容、默认零行为变化**：`PlayOpen()/PlayClose()` 返回 null（默认无动画）时，`UIBase.Open/Close` 的行为必须与现在**完全一致**（同步 SetActive + OnOpened/OnClosed 立即调用）。现存场景/预制体一个都不用改。
2. **保持不变量**：`IsOpen === gameObject.activeSelf` 语义不变（退场动画期间面板仍 activeSelf == true）。
3. **不要改**这些文件：`Assets/Scripts/GooeyUIGroup.cs`、`GooeyWarpEffect.cs`、`SdfBlendField.cs`、`SdfFieldNode.cs`、`SdfUiSpace.cs`、`UIManager.cs`、`Subpanel.cs`、`UINavButton.cs`、`UINavNode.cs`、`UINavigationLocator.cs`。P0 不需要碰它们。（若你发现**必须**改，先停下并在报告里说明，不要擅自改。）
4. **不要改设计文档** `docs/ui-motion-module-design.md`（那是评审对象，必须保持稳定）。发现文档与代码不符，写进报告的 §5.6。
5. **不要用 git**：不 `git add`、不 `git commit`、不 `git stash`。留工作区改动给验收。也不要碰 `.plastic/`。
6. **不要写 `.meta`**：新建 .cs 不手写 meta（Unity 导入时自动生成）；也不要动任何既有 `.meta`。
7. **不新增依赖**：不引入任何包，不依赖 DOTween，不复用 `Assets/Plugins` 里的第三方代码。
8. **C# 9**（Unity 6000.3）：禁止 file-scoped namespace、raw string literal、`required`、collection expressions、`params` 集合改进等 C# 10+ 语法。
9. **注释风格**：跟随你正在修改的文件自身风格（`UIbase.cs`/`UINavigator.cs` 是中文 XML 注释、4 空格缩进、解释"为什么这么设计"而不是复述代码）。新建文件统一用中文注释 + 同一风格。不要给既有文件做无关的重排版。
10. **P0 的已知取舍要如实标注**：文档 §10.2 明说"状态化通道（弹簧）的 Seek 需要重演"是 P0 不需要的；P0 不允许偷偷实现弹簧/协同层。若你为将来的扩展留了接缝，用 TODO 注释写明"P1/P2 才实现"。

## 4. 证据要求（最重要，直接决定验收通过与否）

你必须**实际执行**验证并**原样粘贴命令与输出**。至少包含：

A. **编译验证（必须证明新文件真的被编译了）**。基线：`dotnet build Assembly-CSharp.csproj --nologo -v m`。
推荐做法（任选，但必须能证明新文件在编译范围内）：
   - 把 `Assembly-CSharp.csproj` 复制成项目根下的临时 `Assembly-CSharp-Verify.csproj`（放根目录，`Assets\...` 相对路径才有效），把新文件的 `<Compile Include>` 补进去，构建这个副本；`Assembly-CSharp-Editor.csproj` 同理处理测试文件。构建完删掉临时副本。
   - 或任何你更信得过的方式。
**自证不是假通过**：请给出证据表明新文件确实参与了编译——例如故意在某个新文件里引入一个语法错误、确认构建**失败**、再改回来（把两次输出都贴出来）；或贴出构建日志里包含新文件名的证据。必须让验收者能确信"新文件编译过了"。

B. **运行验证（必须有真实执行输出，不接受只有编译）**。写一个**不依赖 Unity 运行时**的小控制台程序（`dotnet run`），覆盖纯逻辑：
   - 弧长参数化匀速性（文档 §6.1 的验收：等时间采样点间距 `std/mean < 2%`）
   - 缓动过冲外推（`u<0`/`u>1` 的采样点确实落在端点切线延长线上）
   - 确定性（同一 t 两次采样逐位相等；不同 dt 序列下同 t 结果相同）
   - 锚点相对解析、路径拼接连续性（若 P0 有 PathChain 的最小形态）
   实现方式自选：可以引用 Unity 安装目录里的 `UnityEngine.CoreModule.dll`（`Vector2`/`Mathf` 是纯托管代码，脱离 Unity 运行时可用），也可以让核心数学不依赖 UnityEngine。**但不要让测试程序污染仓库结构**——放 `.dsh-scratch/` 下，或做完删除，并在报告里说明。
   - 如果导航队列的合并规则/出队校验/看门狗**也能**在不启动 Unity 的情况下被驱动（例如通过可替换的接入点/接口/假面板），请一并覆盖——这会显著提高验收通过率。**但不许为了可测性把 `UINavigator` 的既有 public API 搞坏**；宁可少测，也不要为了测试扭曲设计。

C. **明确区分"已验证 / 未验证"**。Unity Editor 测试（§13 的 12–20 依赖 MonoBehaviour/EventSystem 的）在本机**跑不了**（Editor 被占用，禁止起第二个实例）。这类必须写清楚："已写、已随 Assembly-CSharp-Editor 编译通过、未在 Unity 中执行"，不要含糊成"通过"。

D. 若任何步骤你**没能**完成或跑失败，如实写，不要美化。验收者会独立复跑你的命令。

## 5. 实现报告格式（写到 `.dsh-scratch/codex-review/impl-report.md`）

```
# UIMotion P0 实现报告

## 1. 交付清单
（每个文件：路径 / 行数 / 一句话职责）

## 2. 与设计文档的对应关系
（逐条：文档的哪一节 → 落到哪个文件/哪个类型。P0 范围内有没有没落地的条目？为什么）

## 3. 我做的设计判断与偏离
（文档没说清或你认为文档不对的地方，你实际怎么选的、为什么。诚实优先。）

## 4. 验证证据
4.1 编译验证（命令 + 原始输出 + 证明新文件被编译的证据）
4.2 运行验证（命令 + 原始输出）
4.3 未验证的部分及原因

## 5. 已知问题 / 遗留
5.1 未实现（P0 范围内）
5.2 已知缺陷或风险
5.3 需要用户决策的点

## 6. 文档与代码不符之处
（文件名+行号）

## 7. 改动文件列表
（精确到路径；区分"新建"与"修改"）
```

## 6. 交付判据（验收者会用这些卡你）

- 新文件在**编译范围内**被证明编译通过（有反证，不是裸 build）。
- 纯逻辑有**真实运行输出**。
- `UIBase` 无动画路径行为与改造前一致；`IsOpen` 不变量未被破坏。
- `UINavigator` 的既有 public API 未被破坏（`Push/Back/Replace/ResetToRoot/CloseAll/Navigated/Current/RootPage/Nodes` 签名与语义对既有调用方仍可用）。
- §8.5 的十项（不变量、出队解析、合并规则、看门狗、输入屏蔽、契约变化、队列上限、校验失败不卡死、事件时机、文件归属）都能在代码里指出具体位置。
- 报告里的每一条"已验证"都能被独立复跑复现。
