# UIMotion P0 实现报告

## 1. 交付清单

已交付简报要求的 16 个新文件及 2 个既有文件改造。运行时与 Editor 程序集已编译；纯数学和队列策略已实际执行。Unity Editor 测试已写、已随 Assembly-CSharp-Editor 编译通过、未在 Unity 中执行，不能称测试全绿。

| 路径 | 行数 | 职责 |
|---|---:|---|
| `Assets/A_UI/UI/Motion/Bind/RectBinder.cs` | 18 | 唯一 anchoredPosition 写入口，补偿 anchor/pivot |
| `Assets/A_UI/UI/Motion/Core/MotionClock.cs` | 48 | 根/子时钟、暂停/恢复、缩放/倒放、Seek 与共享帧推进 |
| `Assets/A_UI/UI/Motion/Core/MotionHandle.cs` | 21 | generation 保护的 struct 句柄与生命周期 API |
| `Assets/A_UI/UI/Motion/Core/MotionMath.cs` | 70 | 三次曲线、切线、弧长 LUT/二分、外推与矩形锚点纯数学 |
| `Assets/A_UI/UI/Motion/Core/MotionSystem.cs` | 149 | Update 调度、可复用池槽位、句柄回调、布局租约恢复 |
| `Assets/A_UI/UI/Motion/Core/MotionTypes.cs` | 29 | MotionSample、AnchorRef、空间与位置通道类型 |
| `Assets/A_UI/UI/Motion/Integration/MotionOwnership.cs` | 54 | 静态所有权声明/查询与活动轨道互斥、冲突 LogError |
| `Assets/A_UI/UI/Motion/Integration/UIPanelMotion.cs` | 40 | 可选进出场配方、当前位置反悔接管与 instant 终态 |
| `Assets/A_UI/UI/Motion/MotionPlan.cs` | 34 | From/To/Curve/Duration/Ease/Clock/Bind/Start 链式构建 |
| `Assets/A_UI/UI/Motion/Path/Ease.cs` | 35 | 五类缓动；保留过冲值，弹性参数作为振幅 |
| `Assets/A_UI/UI/Motion/Path/PathSegment.cs` | 44 | blittable 参数段，Line/CubicBezier 的 tag + switch |
| `Assets/A_UI/UI/Motion/Tracks/AnchorResolver.cs` | 33 | 采样时读取 rect，同父级解析及 p/local 换算 |
| `Assets/A_UI/UI/Motion/Tracks/MotionTrack.cs` | 50 | 路径、时间、缓动、位置/速度采样，复用 LUT |
| `Assets/A_UI/UI/Package/Navigation/UINavigationQueue.cs` | 52 | 纯意图队列、折叠、Back 快进、清队与溢出 |
| `Assets/A_UI/UI/Package/UIbase.cs` | 117 | 可空 PlayOpen/PlayClose、异步完成/强制完成与旧无动画路径 |
| `Assets/A_UI/UI/Package/Navigation/UINavigator.cs` | 504 | 出队解析校验、等待/提交导航、看门狗、输入恢复 |
| `Assets/Editor/MotionTests.cs` | 90 | 测试 1/3/4/9，以及 Skip/旧句柄隔离 |
| `Assets/Editor/UINavigationQueueTests.cs` | 195 | 测试 12–20，以及同步默认路径、丢信号/失效与反悔 |

## 2. 与设计文档的对应关系

| 文档 | 实现 |
|---|---|
| §3、§10.2–10.3 | MotionClock / MotionTrack / MotionMath：位置由绝对轨道时间采样；子时钟读父时钟；共享时钟每帧仅推进一次 |
| §4.1–4.2 | MotionTypes.AnchorRef、AnchorResolver、RectBinder：父空间 p 坐标、edge/offset/space、动态 rect 解析、拉伸 anchor/pivot 补偿 |
| §4.3 | 明确拒绝跨父级，并保留 P2 TODO；未实现 Reparent |
| §5–6.2 | Line/CubicBezier、257 点曲线 LUT 与二分查询、Linear/CubicOut/QuintOut/OutBack/OutElastic、ClampOutside 和端点切线外推 |
| §8.1 | MotionOwnership.Declare/IsOwnedBy/Acquire；冲突 LogError；LayoutElement.ignoreLayout 期间租约及结束还原、强制重建；不引用 DOTween |
| §8.2 | MotionSystem 的 DefaultExecutionOrder(-100) 与 Update；未添加 LateUpdate 特殊相位 |
| §8.4 | UIBase 的可空钩子、版本化完成回调、退出期间保持 activeSelf、Kill(false) 反悔；UIPanelMotion 从当前位置重启 |
| §8.5 | 见下方十项代码定位 |
| §9 | blittable 路径段 + switch；池槽位复用；采样复用 LUT；没有 Burst 或新包 |
| §10.2 | Skip 立即绑定终态并完成一次；Kill(false) 保持当前值并取消回调；已完成槽位复用后旧句柄失效 |
| §11 | new MotionPlan().From(...).To(...).Duration(...).Ease(...).Clock(...).Bind(...).Start()；Curve(PathSegment) 替代接口路径 |
| §13 | MotionTests：1/3/4/9；UINavigationQueueTests：12–20，另补失效、丢回调、默认同步、输入超时恢复 |
| §14 | 限于 P0。没有创建 PathChain、协同层、弹簧、Gooey 反馈、编辑器工具的空壳 |

§8.5 十项落点：

- 不变量/栈与视觉同步：`Assets/A_UI/UI/Package/Navigation/UINavigator.cs:228`；收集 SetOpen；全部面板结束后 commit 才改栈。
- 出队解析：`Assets/A_UI/UI/Package/Navigation/UINavigator.cs:232`；EnsureCache 后按最新 PanelId 查找；ValidateTarget 使用最新 Current。
- 合并策略：`Assets/A_UI/UI/Package/Navigation/UINavigationQueue.cs:25`；相同表示的连续 Push 折叠；不同目标保留；Back 第 4 条起 instant；ToRoot/CloseAll 清待执行队列。
- 看门狗：`Assets/A_UI/UI/Package/Navigation/UINavigator.cs:180`；IsDone → 无效 → unscaled deadline；duration*3+.5；强制完成并继续。
- 输入屏蔽：`Assets/A_UI/UI/Package/Navigation/UINavigator.cs:298`；离场 blocksRaycasts=false；可选屏蔽进场；RestoreInput 恢复原值。
- 契约变化：`Assets/A_UI/UI/Package/Navigation/UINavigator.cs:140`；保留原 public API；异步时 Current 在落定前保持旧栈。
- 队列上限：`Assets/A_UI/UI/Package/Navigation/UINavigationQueue.cs:34`；默认 8，丢最新并 LogWarning。
- 校验失败不卡死：`Assets/A_UI/UI/Package/Navigation/UINavigator.cs:239`；丢弃后由 ProcessNavigation while 继续处理。
- 事件时机：`Assets/A_UI/UI/Package/Navigation/UINavigator.cs:197`；提交栈时发 Navigated；全部待处理工作结束发 NavigationIdle；CloseAll 保持原来不发 Navigated。
- 文件归属：`Assets/A_UI/UI/Package/Navigation/UINavigationQueue.cs:16`；队列位于导航包，不依赖 Motion 类型。

现有调用方 UINavButton.cs:69/71/79/85 不读取返回值或立即读取 Current，无需修改。UINavigator 原有 17 个 public 方法签名、UIBase 的 Open/Close 均由源码检查确认保留；既有调用者随程序集编译。

## 3. 我做的设计判断与偏离

1. **提交时机**：§8.5 同时要求“Current 仍是离开面板”和“Navigated 在出队执行时”。实现将一次出队执行定义为完整视觉事务：开始 SetOpen，结束提交栈并发 Navigated。无动画时整个事务仍在原调用中同步完成。排队入队绝不发 Navigated。这样满足关键不变量，但不是“动画开始瞬间改栈”。
2. **重置屏障**：ToRoot/CloseAll 立即清空待执行队列，等当前事务安全结束后执行清场；不强杀正在运行的面板。不同目标的 Push 全部保留，P0 未启用可选的中间动画 supersede 优化。
3. **P0 单段而非 PathChain**：§14 将 PathChain/G1 放在 P1；本阶段仍实现单段弧长 LUT 满足测试 1。曲线用 256 小区间（257 点），比文中示例 32 点更稳妥，分配只发生在轨道创建时。
4. **空间**：复用 SdfUiSpace 的公式约定，没有调用它或修改它。P0 父 RectTransform 就是路径等比空间。Slot(index) 指目标的直接 RectTransform 子节点，子节点仍须与绑定对象同父级；null target 指绑定父 RectTransform。
5. **静态声明边界**：第一次 Motion 启动会添加 MotionOwnership 声明；外部 Animator/DOTween 应先 Declare 或查询 IsOwnedBy。P0 对目标自身启用的 Animator 保守拒绝，不扫描动画 clip 中的属性曲线；精确 Validator 留 P4。ScrollRect.content 本身拒绝，content 内相对移动的子节点允许。
6. **创作 API**：用值类型 Ease/PathSegment，未引入 IPath2D/IEase 虚调用或额外 UIMotion facade 文件。唯一输出通道为 anchoredPosition；rotation/scale 字段只是 MotionSample 数据，不写 Transform。
7. **生命周期**：没有 UIPanelMotion 时 PlayOpen/PlayClose 返回 null；SetActive 与 OnOpened/OnClosed 在同一调用内完成。配方是可选组件；没有修改任何场景/预制体。instant 分支不调用 PlayOpen/PlayClose、不进入 IsTransitioning，并直接吸附已挂配方的终态。
8. **时钟/句柄**：时间用 double，采样用 float；子时钟是父时间的仿射映射，Seek 在下一次采样反映。轨道起点是注册时钟值。自动完成后槽位可复用，旧 generation 句柄不再可查询历史完成信息；调用方应订阅 OnComplete 或记录完成结果，不长期轮询过期句柄。Kill(false) 不发完成，导航通过失效分支解除等待。
9. **性能**：采样/稳态更新不新建数组、闭包或 LINQ；轨道创建、导航事务、事件派发结束时允许分配。完成回调使用 Action，多播 GetInvocationList 在完成时分配，不声称全生命周期零 GC。未实现弹簧/状态化 Seek 重演、通用 Handoff 或其他 P1–P4 功能。

## 4. 验证证据

### 4.1 编译验证

没有运行裸 `dotnet build Assembly-CSharp.csproj`：原项目会写 Temp，违反本任务硬约束。基线采用同样源码和引用的根目录副本，只将自身及 firstpass 引用的中间/输出目录重定向到 `.dsh-scratch/codex-review/build/`。原始 csproj 未修改。编译器只读既有 Unity/Library 程序集引用，没有写入 Library。

完整可复跑命令（仓库根目录 PowerShell）：

```powershell
python .dsh-scratch/codex-review/verify-build.py
```

脚本：生成根目录的三个 `*-Verify.csproj`，补齐显式 Compile 项并重定向 ProjectReference；对所有 16 个运行时交付文件、2 个 Editor 测试文件逐个生成“原源码 + 首行 #error”的 scratch 副本，要求日志出现每个 marker；恢复正式源码 Compile 项，要求构建成功；finally 删除三个临时项目。**错误绝不注入 Assets**，避免运行中的 Editor 自动导入错误代码。`compiled-sources.json` 记录正式编译输入 SHA256。

运行时探针预期失败 16 错误；Editor 探针预期失败 2 错误；恢复后运行时 0 错误/3 个原有警告，Editor 0 错误。以下均为实际命令与原始输出。


基线（修改前）：

```text
$ dotnet build Assembly-CSharp-Verify.csproj --nologo -v m
  正在确定要还原的项目…
  已还原 E:\Projects_Unity\MiHoYo\Assembly-CSharp-firstpass-Verify.csproj (用时 74 毫秒)。
  已还原 E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj (用时 83 毫秒)。
  Assembly-CSharp-firstpass-Verify -> E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\build\bin\Assembly-CSharp-firstpass.dll
E:\Projects_Unity\MiHoYo\Assets\Slime\Scripts\SlimeGpuSurfacePipeline.cs(76,27): warning CS0649: 从未对字段“SlimeGpuSurfacePipeline.SurfaceVertexGpu.Normal”赋值，字段将一直保持其默认值 [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\Assets\Slime\Scripts\SlimeGpuSurfacePipeline.cs(75,27): warning CS0649: 从未对字段“SlimeGpuSurfacePipeline.SurfaceVertexGpu.Position”赋值，字段将一直保持其默认值 [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\Assets\Slime\Scripts\Slime_ControllerMotor.cs(63,15): warning CS0414: 字段“SlimeControllerMotor._lastCollisionSoundTime”已被赋值，但从未使用过它的值 [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
  Assembly-CSharp-Verify -> E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\build\bin\Assembly-CSharp.dll

已成功生成。

E:\Projects_Unity\MiHoYo\Assets\Slime\Scripts\SlimeGpuSurfacePipeline.cs(76,27): warning CS0649: 从未对字段“SlimeGpuSurfacePipeline.SurfaceVertexGpu.Normal”赋值，字段将一直保持其默认值 [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\Assets\Slime\Scripts\SlimeGpuSurfacePipeline.cs(75,27): warning CS0649: 从未对字段“SlimeGpuSurfacePipeline.SurfaceVertexGpu.Position”赋值，字段将一直保持其默认值 [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\Assets\Slime\Scripts\Slime_ControllerMotor.cs(63,15): warning CS0414: 字段“SlimeControllerMotor._lastCollisionSoundTime”已被赋值，但从未使用过它的值 [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
    3 个警告
    0 个错误

已用时间 00:00:04.91
```


运行时逐文件反证：

```text
$ dotnet build Assembly-CSharp-Verify.csproj --nologo -v m -m:1 -p:BuildInParallel=false -p:RestoreDisableParallel=true
  正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  Assembly-CSharp-firstpass-Verify -> E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\build\bin\Assembly-CSharp-firstpass.dll
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\MotionHandle.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_MotionHandle” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\MotionPlan.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_MotionPlan” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\MotionSystem.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_MotionSystem” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\MotionTypes.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_MotionTypes” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\MotionTrack.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_MotionTrack” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\PathSegment.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_PathSegment” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\AnchorResolver.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_AnchorResolver” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\MotionClock.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_MotionClock” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\UINavigationQueue.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_UINavigationQueue” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\RectBinder.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_RectBinder” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\MotionOwnership.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_MotionOwnership” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\MotionMath.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_MotionMath” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\UINavigator.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_UINavigator” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\UIPanelMotion.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_UIPanelMotion” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\UIbase.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_UIbase” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\Ease.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_Ease” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]

生成失败。

E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\MotionHandle.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_MotionHandle” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\MotionPlan.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_MotionPlan” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\MotionSystem.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_MotionSystem” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\MotionTypes.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_MotionTypes” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\MotionTrack.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_MotionTrack” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\PathSegment.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_PathSegment” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\AnchorResolver.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_AnchorResolver” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\MotionClock.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_MotionClock” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\UINavigationQueue.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_UINavigationQueue” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\RectBinder.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_RectBinder” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\MotionOwnership.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_MotionOwnership” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\MotionMath.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_MotionMath” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\UINavigator.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_UINavigator” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\UIPanelMotion.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_UIPanelMotion” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\UIbase.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_UIbase” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\Ease.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_Ease” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
    0 个警告
    16 个错误

已用时间 00:00:23.05

EXIT_CODE=1
```


运行时恢复正式源码：

```text
$ dotnet build Assembly-CSharp-Verify.csproj --nologo -v m -m:1 -p:BuildInParallel=false -p:RestoreDisableParallel=true
  正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  Assembly-CSharp-firstpass-Verify -> E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\build\bin\Assembly-CSharp-firstpass.dll
E:\Projects_Unity\MiHoYo\Assets\Slime\Scripts\SlimeGpuSurfacePipeline.cs(76,27): warning CS0649: 从未对字段“SlimeGpuSurfacePipeline.SurfaceVertexGpu.Normal”赋值，字段将一直保持其默认值 [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\Assets\Slime\Scripts\SlimeGpuSurfacePipeline.cs(75,27): warning CS0649: 从未对字段“SlimeGpuSurfacePipeline.SurfaceVertexGpu.Position”赋值，字段将一直保持其默认值 [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\Assets\Slime\Scripts\Slime_ControllerMotor.cs(63,15): warning CS0414: 字段“SlimeControllerMotor._lastCollisionSoundTime”已被赋值，但从未使用过它的值 [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
  Assembly-CSharp-Verify -> E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\build\bin\Assembly-CSharp.dll

已成功生成。

E:\Projects_Unity\MiHoYo\Assets\Slime\Scripts\SlimeGpuSurfacePipeline.cs(76,27): warning CS0649: 从未对字段“SlimeGpuSurfacePipeline.SurfaceVertexGpu.Normal”赋值，字段将一直保持其默认值 [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\Assets\Slime\Scripts\SlimeGpuSurfacePipeline.cs(75,27): warning CS0649: 从未对字段“SlimeGpuSurfacePipeline.SurfaceVertexGpu.Position”赋值，字段将一直保持其默认值 [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
E:\Projects_Unity\MiHoYo\Assets\Slime\Scripts\Slime_ControllerMotor.cs(63,15): warning CS0414: 字段“SlimeControllerMotor._lastCollisionSoundTime”已被赋值，但从未使用过它的值 [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Verify.csproj]
    3 个警告
    0 个错误

已用时间 00:00:02.62

EXIT_CODE=0
```


Editor 逐文件反证：

```text
$ dotnet build Assembly-CSharp-Editor-Verify.csproj --nologo -v m -m:1 -p:BuildInParallel=false -p:RestoreDisableParallel=true
  正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  Assembly-CSharp-firstpass-Verify -> E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\build\bin\Assembly-CSharp-firstpass.dll
  Assembly-CSharp-Verify -> E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\build\bin\Assembly-CSharp.dll
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\MotionTests.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_MotionTests” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Editor-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\UINavigationQueueTests.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_UINavigationQueueTests” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Editor-Verify.csproj]

生成失败。

E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\MotionTests.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_MotionTests” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Editor-Verify.csproj]
E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\probes\UINavigationQueueTests.cs(1,8): error CS1029: #错误:“UIMOTION_COMPILE_PROBE_UINavigationQueueTests” [E:\Projects_Unity\MiHoYo\Assembly-CSharp-Editor-Verify.csproj]
    0 个警告
    2 个错误

已用时间 00:00:21.98

EXIT_CODE=1
```


Editor 恢复正式源码：

```text
$ dotnet build Assembly-CSharp-Editor-Verify.csproj --nologo -v m -m:1 -p:BuildInParallel=false -p:RestoreDisableParallel=true
  正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  Assembly-CSharp-firstpass-Verify -> E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\build\bin\Assembly-CSharp-firstpass.dll
  Assembly-CSharp-Verify -> E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\build\bin\Assembly-CSharp.dll
  Assembly-CSharp-Editor-Verify -> E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\build\bin\Assembly-CSharp-Editor.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:02.57

EXIT_CODE=0
```


过程中出现过两类失败，均保留，未当作通过：

- 首次默认并行 Editor build 退出 1，摘要显示 0 警告/0 错误，没有提供有效诊断。追加串行 MSBuild 与 Restore 参数后成功。诊断全文保存在 `evidence/editor-diagnostic.txt`；未把“恢复成功”归因于未经证实的根因。
- 控制台 csproj 初稿相对路径多写了一层 `..`，出现 CS2001；修正为 `../../../Assets` 后编译并运行成功。

首次 Editor 失败原始输出：

```text
$ dotnet build Assembly-CSharp-Editor-Verify.csproj --nologo -v m
  正在确定要还原的项目…

生成失败。
    0 个警告
    0 个错误

已用时间 00:00:03.00
```

### 4.2 运行验证

控制台直接链接正式 MotionMath/PathSegment/Ease/MotionClock/MotionTypes/UINavigationQueue 源文件，引用已安装 Unity 的 CoreModule **纯托管 Vector2 实现**；未启动或依赖 Unity 引擎运行时。没有复制一份不同的核心算法。

队列所需 UIBase 仅用空身份类替身，不伪造 MonoBehaviour 行为。因此该输出只证明队列策略；不能证明 UINavigator、RectTransform 解析、看门狗或输入屏蔽。锚点运行用例验证同一 MotionMath.AnchorPoint 函数的短边比例/边缘公式，真实 Transform 解析保留 Editor 测试。

```text
$ dotnet run --project .dsh-scratch/codex-review/console/MotionProof.csproj --no-launch-profile
ARC intervals=100 LUT=257 length=3.8907583 mean=0.038904730156064035 std/mean=0.00032667219802022963 threshold=0.02 PASS
BACK t=-0.1 u=-0.5368912 cross=-7.450581E-09 projection=-2.0889137 expected=-2.088914 PASS
BACK t=0.7 u=1.0801995 cross=0 projection=0.3120368 expected=0.31203678 PASS
ELASTIC overshoot=1.4 t=0.1 u=1.6742641 PASS
DETERMINISM dtA=[0.0625 x8] dtB=[0.125,0.375] t=0.5 xBits=3FB1194C yBits=3ED3B7D0 repeated=equal sequences=equal PASS
CLOCK shared-seek pause resume reverse PASS
ANCHOR right+0.1short before=(56,0) after=(108,0) deltaX=52 PASS (pure rectangle math only)
QUEUE duplicate-fold=1 Back5=AAAII ToRoot-clear=PASS CloseAll-clear=PASS overflow=reject-newest order=A,B warnings=1 PASS
RESULT 25 checks PASS; Unity lifecycle/navigation/watchdog/RectTransform resolution NOT EXECUTED; PathChain is P1, not implemented.
```

确定性用两组不同、可精确二进制表示的 dt，最终 t 都为 0.5；另验证同一 t 重复采样的 float bit pattern。它证明固定输入下与历史路径无关，不声称任意浮点累加序列的时钟值逐位一致，也不声称不同 CPU/运行时平台浮点结果逐位一致。

首次控制台失败原始输出：

```text
$ dotnet run --project .dsh-scratch/codex-review/console/MotionProof.csproj --no-launch-profile
CSC : error CS2001: 未能找到源文件“E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\console\../../../../Assets/A_UI/UI/Motion/Core/MotionClock.cs”。 [E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\console\MotionProof.csproj]
CSC : error CS2001: 未能找到源文件“E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\console\../../../../Assets/A_UI/UI/Package/Navigation/UINavigationQueue.cs”。 [E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\console\MotionProof.csproj]
CSC : error CS2001: 未能找到源文件“E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\console\../../../../Assets/A_UI/UI/Motion/Core/MotionMath.cs”。 [E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\console\MotionProof.csproj]
CSC : error CS2001: 未能找到源文件“E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\console\../../../../Assets/A_UI/UI/Motion/Core/MotionTypes.cs”。 [E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\console\MotionProof.csproj]
CSC : error CS2001: 未能找到源文件“E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\console\../../../../Assets/A_UI/UI/Motion/Path/Ease.cs”。 [E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\console\MotionProof.csproj]
CSC : error CS2001: 未能找到源文件“E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\console\../../../../Assets/A_UI/UI/Motion/Path/PathSegment.cs”。 [E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\console\MotionProof.csproj]
dotnet : 
At line:85 char:117
+ ... onsole.py'; dotnet run --project '.dsh-scratch/codex-review/console/M ...
+                 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : NotSpecified: (:String) [], RemoteException
    + FullyQualifiedErrorId : NativeCommandError
 
生成失败。请修复生成错误并重新运行。
```

源码与范围检查：

```text
$ python .dsh-scratch/codex-review/audit-scope.py
COMPILED_SOURCE_HASHES: 18/18 match final source
UNCHANGED_SHA256: docs/ui-motion-module-design.md b003cb4ef339302aef603a91234b66ddbf5775ee7a7b1d532c79e7cf9edb2b60
UNCHANGED_SHA256: Assets/Scripts/GooeyUIGroup.cs 6905222576abd4cb4db91786391fcbaccc7c305b9adf7ee3307f468ddea95aed
EXISTING_PUBLIC_METHOD_SIGNATURES: Assets/A_UI/UI/Package/UIbase.cs 2 retained
EXISTING_PUBLIC_METHOD_SIGNATURES: Assets/A_UI/UI/Package/Navigation/UINavigator.cs 17 retained
ADDED_LINE_WHITESPACE: PASS (Python source comparison; no git invoked)
TEMP_ROOT_VERIFY_PROJECTS: removed
```

### 4.3 未验证的部分及原因

- **Unity Editor 测试：已写、已随 Assembly-CSharp-Editor 编译通过、未在 Unity 中执行。** 包括测试 9 的真正 RectTransform 尺寸变更、测试 12–20 的导航事务、丢回调/无效句柄/禁用面板恢复、unscaled 看门狗与 CanvasGroup 输入屏蔽。
- 没有 Unity Play Mode/场景视觉验收：三种分辨率、30/60/144 fps、Gooey 相位/桥闪烁、Editor 暂停、布局重建后的像素表现、反悔连续性、实际菜单手感都未验证。
- 未执行 Profiler，不能宣称 0 GC 或达到 §9.3 时间预算。
- 没有运行 `git diff --check`，因为用户禁止使用 git；以 Python 检查新增行尾空白替代，没有将它冒称 git 验证。
- PathChain 拼接连续性不在 P0，未实现、未测。弹簧、协同、Gooey 反馈均不在本次验证范围。
- 全程未启动/终止 Unity；只读进程查询确认 pid 15944 仍为 Unity。没有写 Library/Temp/ProjectSettings/.plastic，没有 git 操作，没有写或修改 .meta。

## 5. 已知问题 / 遗留

### 5.1 未实现（P0 范围内）

简报列出的 P0 代码文件和指定测试均已交付。验收中的 **Unity 测试实际执行、视觉和性能校准仍未完成**，原因是禁止启动/操作占用中的 Editor。它们不能用独立控制台结果替代。

### 5.2 已知缺陷或风险

- 单段固定分辨率 LUT 在极端尖点、自交或退化曲线上不保证所有曲线都满足 2% 的等弦长指标；当前真实输出只覆盖给定非平凡三次曲线。端点控制点重合有割线回退，完整自适应/多段策略留后续阶段。
- 动态锚点当前支持直线 From/To；Curve 是固定 p 空间控制点的单段。UIPanelMotion 的 home 在第一次播放时捕获；若外部之后改变面板的布局基准，需要重新创建/配置配方，或直接使用动态 AnchorRef 构建轨道。
- 所有权声明无法阻止绕开声明直接写 Transform 的第三方脚本；Animator 父级动画 clip 的子路径属性并未扫描，精确 Validator 未实现。当前 Animator 检查可能保守拒绝仅做颜色动画的目标。
- LayoutGroup 恢复会立即重排，可能将动画终点吸附回布局位置，这是设计规定的还原策略，实际观感未测。运行时按需添加的 LayoutElement/CanvasGroup/MotionOwnership 会保留在对象上，原 ignoreLayout/blocksRaycasts 值会恢复。
- 句柄信息只在当前 generation 生命周期内有效，完成槽位复用后不能回查历史。已完成轨道不能通过 Seek 倒放；倒放须在活动时钟上进行，或重新创建轨道。
- 队列仅折叠相同目标表示（同对象或相同字符串）；对象入口与字符串入口指向同一面板时会等出队解析后作为 Current 相同的 no-op 消除，不在入队时解析它们。
- 自定义 UIBase 派生类负责自己的 PlayOpen 反悔起点/instant 自定义视觉属性；默认 UIPanelMotion 处理本模块位置配方。此处没有实现 P3 的通用速度连续 Handoff。

### 5.3 需要用户决策的点

无阻塞实现的待批准事项。验收时应确认第 3 节的“完成时提交栈/事件”解释是否符合产品期望，以及后续 Editor 手工测试的执行时间。没有为这些决定修改设计文档。

## 6. 文档与代码不符之处

| 文档位置 | 实际实现与理由 |
|---|---|
| docs/ui-motion-module-design.md:126 | 按同一 p 空间公式实现，没有调用 SdfUiSpace.TryGetFieldContext，避免引入跨系统上下文依赖 |
| docs/ui-motion-module-design.md:214 | 示例 Bezier 32 采样点；实现 MotionTrack 使用 257 点 LUT；只实现 P0 单段 |
| docs/ui-motion-module-design.md:416 | Validator 的 Animator 曲线精确检查属于 P4；P0 在 MotionOwnership.Acquire 保守检查本对象 Animator 并执行声明互斥 |
| docs/ui-motion-module-design.md:467 | 示意 PlayOpen/PlayClose 默认 null；UIBase 无组件时返回 null，有可选 UIPanelMotion 时委托组件 |
| docs/ui-motion-module-design.md:537 | 公开 TransitionCompleted 通知，同时导航在 Update 轮询 IsTransitioning/句柄完成状态；不依赖单一事件，防止丢回调卡队列 |
| docs/ui-motion-module-design.md:558 | “出队执行时”被解释为完整转移事务结束提交时，以兼容 :557 的 Current 滞后约定；无动画仍同步 |
| docs/ui-motion-module-design.md:644 | API 为 new MotionPlan + Ease/PathSegment 值类型；没有 UIMotion facade、接口或非 P0 DSL |
| docs/ui-motion-module-design.md:798 | 弧长 LUT 列在 P1，但 P0 测试 1 已要求；本次先实现单段 LUT，PathChain/G1 留 P1 |
| docs/ui-motion-module-design.md:818 | 未做 Profiler 校准或回写文档：本次禁止启动 Unity 且禁止改设计文档 |

## 7. 改动文件列表

新建源码：

- `Assets/A_UI/UI/Motion/Bind/RectBinder.cs`
- `Assets/A_UI/UI/Motion/Core/MotionClock.cs`
- `Assets/A_UI/UI/Motion/Core/MotionHandle.cs`
- `Assets/A_UI/UI/Motion/Core/MotionMath.cs`
- `Assets/A_UI/UI/Motion/Core/MotionSystem.cs`
- `Assets/A_UI/UI/Motion/Core/MotionTypes.cs`
- `Assets/A_UI/UI/Motion/Integration/MotionOwnership.cs`
- `Assets/A_UI/UI/Motion/Integration/UIPanelMotion.cs`
- `Assets/A_UI/UI/Motion/MotionPlan.cs`
- `Assets/A_UI/UI/Motion/Path/Ease.cs`
- `Assets/A_UI/UI/Motion/Path/PathSegment.cs`
- `Assets/A_UI/UI/Motion/Tracks/AnchorResolver.cs`
- `Assets/A_UI/UI/Motion/Tracks/MotionTrack.cs`
- `Assets/A_UI/UI/Package/Navigation/UINavigationQueue.cs`
- `Assets/Editor/MotionTests.cs`
- `Assets/Editor/UINavigationQueueTests.cs`

修改源码：

- `Assets/A_UI/UI/Package/UIbase.cs`
- `Assets/A_UI/UI/Package/Navigation/UINavigator.cs`


新增验证/报告工件（均在 scratch，不进入 Unity Assets）：

- `.dsh-scratch/codex-review/verify-build.py`
- `.dsh-scratch/codex-review/audit-scope.py`
- `.dsh-scratch/codex-review/make-report.py`
- `.dsh-scratch/codex-review/prepare-verify.py`
- `.dsh-scratch/codex-review/write-motion.py`
- `.dsh-scratch/codex-review/write-motion2.py`
- `.dsh-scratch/codex-review/write-navigation.py`
- `.dsh-scratch/codex-review/write-tests.py`
- `.dsh-scratch/codex-review/write-console.py`
- `.dsh-scratch/codex-review/console/MotionProof.csproj`
- `.dsh-scratch/codex-review/console/Program.cs`
- `.dsh-scratch/codex-review/impl-report.md`
- `.dsh-scratch/codex-review/evidence/baseline.txt`
- `.dsh-scratch/codex-review/evidence/before-hashes.json`
- `.dsh-scratch/codex-review/evidence/compiled-sources.json`
- `.dsh-scratch/codex-review/evidence/console-first-failure.txt`
- `.dsh-scratch/codex-review/evidence/console-run.txt`
- `.dsh-scratch/codex-review/evidence/editor-diagnostic.txt`
- `.dsh-scratch/codex-review/evidence/editor-final-passed.txt`
- `.dsh-scratch/codex-review/evidence/editor-probe-failed.txt`
- `.dsh-scratch/codex-review/evidence/editor-serial-build.txt`
- `.dsh-scratch/codex-review/evidence/first-editor-build.txt`
- `.dsh-scratch/codex-review/evidence/first-runtime-build.txt`
- `.dsh-scratch/codex-review/evidence/runtime-final-passed.txt`
- `.dsh-scratch/codex-review/evidence/runtime-probe-failed.txt`
- `.dsh-scratch/codex-review/evidence/scope-audit.txt`
- `.dsh-scratch/codex-review/evidence/UIbase.before.cs`
- `.dsh-scratch/codex-review/evidence/UINavigator.before.cs`
- `.dsh-scratch/codex-review/evidence/verification-transcript.txt`

三个根目录验证 csproj、scratch build/bin/obj 和错误探针副本均为临时验证文件，已在完成后删除；复跑脚本会重新生成。保留控制台源文件及原始日志用于验收。未改动此目录已有的评审文档、impl-brief.md、verify-build.ps1、verify-diff.ps1。没有提交版本控制。
