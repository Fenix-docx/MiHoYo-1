# UIMotion P0 验收报告（独立复跑）

**验收对象**：`.dsh-scratch/codex-review/impl-report.md` 所交付的 UIMotion P0（16 个新文件 + 2 个改造文件）
**验收人**：DSH agent（非实现方）
**验收方式**：不看结论看证据 —— 自己写审计/构建脚本、复跑实现方的证据、逐行核对源码与设计文档
**结论**：**有条件通过**。生产代码可用且质量高于预期；**2 项必须修复**（已派第 2 轮，见文末），其余为应记录的已知限制。

---

## 1. 结论一览

| 维度 | 判定 | 依据 |
|---|---|---|
| 编译（运行时程序集） | ✅ 通过（双工具链交叉确认） | 我的 `verify-build.ps1` + Unity 自己的 Bee/Csc 管线 |
| 编译（Editor 程序集/测试） | ✅ 通过 | 同上，测试类进入 `Assembly-CSharp-Editor.dll` |
| 纯逻辑运行证据 | ✅ 通过（逐位可复现） | 我复跑控制台 harness，输出与报告完全一致 |
| 向后兼容（无动画面板路径） | ✅ 代码层通过 | 无 recipe 时 `BeginTransition` 全程同步；有对应测试（未执行） |
| 既有 public API 未破 | ✅ 通过 | 逐个核对 17 个方法 + 属性 |
| §8.5 十项落点 | ✅ 十项均可指到具体代码 | 见 §4 表 |
| Unity 行为（测试真实执行） | ⚠️ 未验证（环境所限） | Editor 被占用，禁起第二实例 |
| 红线（git/禁区/meta） | ✅ 干净（我的脚本一度误报） | 见 §6 |
| 报告自身可信度 | ✅ 高（但有 2 处遗漏） | 见 §5.D2 / §7 |

**唯一硬伤在测试文件，不在生产代码**：2 处 Unity 断言的中文期望值在落盘时被损坏成 `?`，在真实 Unity 里**必然失败**——而这恰好落在"本机跑不了 Unity 测试"的证据盲区里。

---

## 2. 我实际执行了什么（可复跑）

| # | 动作 | 结果 |
|---|---|---|
| 1 | 派活前抓取工作区基线快照（`git status --porcelain -uall`，451 条既有改动） | 建立了"相对增量"diff 基准 |
| 2 | 自写 `verify-diff.ps1`：对基线做增量 diff + 越界红线检查 | 见 §6 |
| 3 | 自写 `verify-build.ps1`：**把磁盘上存在但 Unity csproj 未列出的 .cs 补进编译范围**（临时 `*-Verify.csproj`，输出重定向到 scratch）后再构建 | 两个程序集 **Exit 0**；`MissingAdded=0` |
| 4 | 检查 Unity **自己的**编译产物：`Library/ScriptAssemblies/Assembly-CSharp.dll` 是否含新类型 | 含 `MotionClock/MotionSystem/MotionMath/UINavigationQueue/UIPanelMotion/MotionPlan/RectBinder`；Editor 程序集含 `UINavigationQueueTests` |
| 5 | 检查 `Editor.log`（Unity session 日志） | 无 `error CS` / `Compilation failed`；`CompileScripts: 3422ms` 正常完成 |
| 6 | 复跑实现方的控制台证据程序 `dotnet run --project .../MotionProof.csproj` | **25 checks PASS，数值与报告逐位一致** |
| 7 | 手推核对数学：`RectBinder` 往返、`AnchorPoint` 的 52 单位期望、`Ease` 公式对照标准形式、`MotionClock` 父子/Seek/Pause 语义、`backBurst` → `AAAII`/`started==6` | 全部自洽 |
| 8 | 全文件搜索编码损坏（连续 `???` / `U+FFFD` / mojibake） | 命中 5 处，见 §5.D1 |

**关于第 3 步 `MissingAdded=0` 的解读**：不是"没有新文件"，而是运行中的 Unity 在 13:36:43 已重新生成 csproj 并**把 13 条 Motion 编译项写进去了**，所以我的"补齐"无事可做、构建通过即等价于"新文件确实参与编译"。这比裸跑 `dotnet build` 强得多（裸跑会静默跳过新文件 → 假通过，这正是我在派活简报里点名要求实现方自证的点，它用"逐文件 `#error` 探针 + 恢复"做到了）。

---

## 3. 与设计文档的符合度

**总体：高度符合，且把文档里含糊处做了明确取舍并主动披露。**

实现的聪明之处（值得确认的部分）：
- **探针不污染 `Assets/`**：我建议的"故意注入语法错误"若打在 `Assets/` 里会触发用户正在跑的 Editor 导入坏代码；它改为在 scratch 复制探针副本，要求日志出现 16 个 marker，再用正式源码恢复构建。**比我的要求更安全**，且反证有效。
- **`UIPanelMotion` 默认 `useUnscaledTime = true`**（`UIPanelMotion.cs:13`）：与 `UINavigator` 用 `Time.unscaledTimeAsDouble` 做看门狗判定自洽 → 我原本担心的"暂停菜单(timeScale=0)下动画被看门狗提前截断"**不成立**。
- **`CloseAll` 保持"不发 Navigated"的既有约定**（`UINavigator.cs:293`），并写了注释说明——这类"旧行为不能顺手改"的细节抓得很准。
- **`Awake` 的 `CloseAll` 不会为空面板创建轨道**：`UIBase.BeginTransition` 在 `IsOpen == open` 时直接 return → 场景里本来就是关闭的面板不会白白起一条退场轨道。
- **队列与 Motion 完全解耦**：`UINavigationQueue.cs` 不 `using UIPackage.Motion`，只依赖 `UIBase` → 文件归属判断正确（导航语义归导航包）。

---

## 4. §8.5 十项逐条核对（我核对的落点，非引用实现方的说法）

| # | 文档要求 | 代码落点 | 核对结论 |
|---|---|---|---|
| 1 | 栈推进与视觉推进同拍 | `UINavigator.cs:171-198`（ready → commit）、`:262-296`（commit 前不动 navStack） | ✅ navStack 只在 commit 委托里变更 |
| 2 | 出队时才解析 targetId + 用最新 Current 重算校验 | `:232-236`（`EnsureCache` 后查 `pageById`）、`:210-227` | ✅ 顺序正确 |
| 3 | 合并规则（同 target 折叠 / Back 快进 / ToRoot 清队 / 上限丢最新） | `UINavigationQueue.cs:25-43` | ✅ 含 `backBurst` 语义正确（idle 时 `ResetBurst`，不会永久 instant） |
| 4 | 看门狗：IsDone → 句柄失效 → unscaled 墙钟兜底 | `:180-190` | ✅ 判定顺序与文档一致；`deadline = duration*3+.5` |
| 5 | 输入屏蔽 + 还原 | `:298-328`（`Touch` 记录原值并置 false；`RestoreInput` 还原） | ✅ 超时路径也会还原（`:194` 在 ready 分支） |
| 6 | API 契约变化（返回值=已接受、Current 滞后） | `:140-147` 注释 + 实现 | ✅ 既有 17 个方法签名全部保留 |
| 7 | 队列上限丢最新 + 告警 | `UINavigationQueue.cs:34-38` | ✅ |
| 8 | 校验失败不卡死队列 | `:239/:240` 提前 return；`ProcessNavigation` 的 `while` 继续出队 | ✅ |
| 9 | 事件时机（出队执行时发 Navigated；新增 NavigationIdle） | `:197`（commit 后）、`:204-208`（`hadWork && !IsNavigating`） | ✅ 入队时不发；CloseAll 不发（与旧行为一致） |
| 10 | 队列归属导航包 | `Assets/A_UI/UI/Package/Navigation/UINavigationQueue.cs` | ✅ 不依赖 Motion 命名空间 |

---

## 5. 我独立发现的缺陷

### D1【高 · 必须修】2 处 Unity 断言的期望字符串被写坏成 `?` → 测试必失败

```
Assets/Editor/UINavigationQueueTests.cs:155  LogAssert.Expect(LogType.Warning, "[UINavigator] ????????????");
Assets/Editor/UINavigationQueueTests.cs:165  LogAssert.Expect(LogType.Error,   "[UINavigator] ??????????????????");
```
而代码实际打印的是**中文**（`UINavigator.cs:183` / `:188`）。`LogAssert.Expect` 要求逐字匹配 → 期望永不满足 → `Test18_DisabledPanelInvalidatesHandleButQueueRecovers` 与 `Test19_TimeoutAlsoRestoresOriginalRaycastValue` **在 Unity 里必然失败**。

根因是落盘时有损编码（同批还有 3 处注释/XML 文档被损坏：`UINavigationQueueTests.cs:142`、`UINavigator.cs:317`、`UIPanelMotion.cs:16`）。全仓连续 `???` 命中恰好这 5 处，**生产代码的行为性字符串未被污染**（否则影响会大得多）。

为什么没被发现：证据链里"solver 能跑的"只有纯逻辑控制台程序（直接测 `UINavigationQueue`，不经过 `UINavigator` 的日志），而依赖日志断言的导航测试**只编译未执行**——缺陷精确落在盲区中心。

### D2【中 · 必须修】`Push` 被悄悄收紧了可接受输入域（违反"默认零行为变化"）

`ValidateTarget`（`UINavigator.cs:210-227`）要求**目标必须是导航树节点**（`FindNode(target) == null → 丢弃`）。
对照改动前（我在会话开始时读过的 410 行原版，`:121-152`）：只有 `current != null && restrictToDirectChildren` 时才做父子校验，**栈为空、或 `restrictToDirectChildren == false` 时任意非空面板都能 Push 成功**。

即：新实现缩小了输入域。这条变化在实现报告 §5.2 / §6 里**没有披露**。实际影响面窄（默认 `restrictToDirectChildren == true`、且项目自带 `UINavigationValidator` 会把游离面板当问题提示），但它确实违反了派活简报的红线 1，应以"恢复原域 + 保留出队重校验"方式修掉。

### D3【低-中】面板进出场绕过了"锚点延迟解析"原则：父 rect 退化时终点会塌到父中心

`UIPanelMotion.Play`（`:27-38`）在**启动瞬间**用父 `RectTransform` 把 `home` 换算成 p 空间并**烘死**进 `PathSegment.Line`。若此刻 `parent.rect.height ≈ 0`：
- `AnchorResolver.ToP` 的 `Math.Max(1e-5, |h|)` 防住了除零（**不会 NaN**，这点做对了）；
- 但反变换 `FromP` 乘 `|h| * .5 ≈ 0` → 整条路径塌到 `space.rect.center` → 面板滑向父中心而非 `home`。

触发条件是"父 rect 由 `LayoutGroup`/`ContentSizeFitter` 驱动且尚未完成首次布局"，而 `UINavigator.Awake → CloseAll`（默认 `closeAllOnAwake = true`）与 `Start → ResetToRoot` 都在首次布局之前/边缘。**开启**路径的错位是持久的（终点被写死），关闭路径只是滑出轨迹不对（随即被 `SetActive(false)` 掩盖）。

报告 §5.2 披露了"`home` 首次捕获后布局基准变化会过期"，但没披露这一支。与文档 §4.2"锚点在采样时刻解析"的原则冲突——面板配方走的是 `Curve` 烘焙路径，绕过了该原则（`From/To` 的 `AnchorRef` 路线才是延迟解析的）。

### D4【低】文档承诺的"起点前反向回弹(anticipation)"在 P0 不可达

`MotionTrack.Sample`（`:37`）先 `t = Clamp01(seconds / Duration)` 才喂给 `Ease`，而 `OutBack` 在 `t∈[0,1]` 上只产生 `u > 1`、不产生 `u < 0`。于是 `EvaluateByArc` 的 `u < 0` 外推分支只能由测试直接调 `MotionMath` 触发（`MotionTests.cs:26` 传 `-.1f`），实际运行路径不可达。属功能缺口非错误；`u > 1` 外推是真实可达且被正确测试的。

### D5【低】测试名承诺了它没验证的行为
`ReopeningDuringExitKillsOldCompletionAndKeepsPosition`（`:188`）没有任何位置断言 → 名实不符。

### D6【低】静态状态与快速进入播放模式
`MotionSystem.instance` / `MotionOwnership.active` 均为 `static`。若项目开启"禁用 Domain Reload"，跨播放会话可能残留；当前 `OnDestroy` 会清理，风险低，但建议加 `[RuntimeInitializeOnLoadMethod]` 兜底。

### D7【提示】`MotionSystem.Instance` 在非播放模式也会创建 GameObject
会写入当前打开的场景。P0 的调用路径全在运行时，风险低；但未来 Editor 工具调用会弄脏用户场景。

### D8【提示】测试不驱动 `Update()`
EditMode 测试手动调 `ProcessNavigation` 与 `MotionSystem.Advance`，因此**真实运行时的 `Update` 蹦床路径无任何测试覆盖**（若 `Update` 的时间源写错，测试全绿）。建议补一条对 `Update` 的间接覆盖或至少加断言。

---

## 6. 红线审计（含对我自己工具的一次更正）

审计结果：**干净**。
- 新增 16 个源码文件 + 修改 2 个既有文件（`UIbase.cs`、`UINavigator.cs`），无删除、无暂存（未 `git add`）、HEAD 未变。
- 简报点名的禁改文件（`GooeyUIGroup/SdfUiSpace/UIManager/UINavButton/UINavNode/...`、设计文档）**逐一核对未动**。
- 未写 `Library/`、`Temp/`、`ProjectSettings/`、`.plastic/`。

**更正我自己的脚本**：`verify-diff.ps1` 一度把 22 个新增 `.meta` 标为红线违规，这是**假阳性**。证据：
1. `.meta` 创建时间比对应 `.cs` 晚约 30 秒（`Motion.meta` 13:31:06 ← `MotionSystem.cs` 13:30:34），是导入滞后而非同时写入；
2. 内容是标准 Unity 格式（`fileFormatVersion: 2` / `guid` / `folderAsset: yes` / `DefaultImporter`）；
3. `Editor.log` 有对应的 `Start importing Assets/A_UI/UI/Motion using Guid(...) (DefaultImporter)`，GUID 与 `.meta` 完全一致；
4. 实现方的生成脚本里没有任何 `.meta` / `guid` 写入痕迹。

结论：**.meta 与 csproj 都是运行中的 Unity 自动生成/重生成的**，实现方没有违规。这条也顺带证明了一件更有价值的事：**用户的实时 Editor 已经把这个工程连同新代码编译通过了**。

---

## 7. 报告质量的评价（作为交付物之一）

优点：证据优先（原始输出、逐文件反证、失败记录保留不美化）、主动披露 9 条偏离与 8 条已知缺陷、明确区分"已编译/已执行/未验证"、文档行号引用经我抽查**准确**（`:126 :214 :416 :467 :537 :558 :644 :798 :818` 均对得上）。

两处遗漏：未发现 D1（无法发现，本机跑不了 Unity 测试），未披露 D2。§5.3"需要用户决策的点：无"略显自满——D2 属于需要用户拍板的行为变化。

---

## 8. 证据边界（为什么这三件事没验证）

| 未验证项 | 原因 | 影响 |
|---|---|---|
| Unity 测试真实执行 | 用户的 Editor 正在运行（pid 15944），禁止起第二实例 | D1 因此逃逸 |
| 视觉/帧率/分辨率/Gooey 相位 | 同上 | 观感类验收欠账 |
| Profiler / 0 GC / §9.3 时间预算 | 同上 | 报告未声称达标，符合预期 |

这三项**不能**用控制台结果替代——实现方在报告里也正是这么写的，这点值得肯定。

---

## 9. 处置

- **D1、D2 必须修**（已派第 2 轮修复，简报：`.dsh-scratch/codex-review/repair-brief.md`）：修复期望字符串、恢复 `Push` 原可接受域并补测试、修 D5 名实不符。
- **D3–D8 建议记录为已知限制**，不阻塞 P0；D3 若要修，方向是让面板配方走 `AnchorRef` 的延迟解析路线，或在 `Play` 时检测退化 rect 并降级 + 告警。
- **P0 之后的门槛**：必须在用户空闲时于 Unity 内跑一次 EditMode 测试（`Window > General > Test Runner`）+ 一次真机视觉验收，才算真正闭环。

---

## 10. 第 2 轮修复复验（✅ 通过）

复验由我独立执行（不是引用实现方自述）：

| 检查 | 方法 | 结果 |
|---|---|---|
| 编码损坏是否清零 | 三个目录全量搜索连续 `???` | **0 条** |
| 两程序集编译 | 我的 `verify-build.ps1`（补齐 Compile 项后构建） | **PASS，0 错误 0 警告** |
| 控制台证据复跑 | `dotnet run .../MotionProof.csproj` | **25 checks PASS** |
| 第 2 轮到底改了哪些文件 | 对实现方自留的 before 快照做**内容级 diff**（`Compare-Object`） | 仅 `UINavigator.cs` / `UIPanelMotion.cs` / `UINavigationQueueTests.cs` |

（注：我的 `verify-diff.ps1` 按 git 状态码比较，无法察觉"第 2 轮又改了同一文件"，因此这一轮改用 before 快照做内容 diff——工具局限已记录。）

### D1 逐字核对

- `UINavigationQueueTests.cs:184` 期望 `"[UINavigator] 转移句柄失效，强制完成。"` ≡ 代码 `UINavigator.cs:183` ✅
- `:159` / `:194` 期望 `"[UINavigator] 转移看门狗超时，强制完成并继续队列。"` ≡ 代码 `:188` ✅
- 3 处注释/XML 文档恢复原意（`UINavigator.cs:317`、`UIPanelMotion.cs:16`、测试 `:171`）✅
- 根因**未确证**——实现方如实标注为"未确证"而不是编一个解释，这点比给个漂亮答案更可信。

### D2 的修法优于我的要求

它没有简单放宽，而是引入 `targetWasRegistered`：

```csharp
if (target == null) { warn; return false; }                                  // 只拒空
if (current != null && current != target && restrictToDirectChildren) { ...FindNode(target)... }   // 严格校验收回此分支
bool targetWasRegistered = FindNode(target) != null;
commit = () => { if (target != null && (!targetWasRegistered || FindNode(target) != null)) navStack.Push(target); ... };
```

→ **同时**恢复了改动前的可接受域（栈为空 / 关闭限制时游离面板可入栈）**和**"已登记目标在转移期间被移除则拒绝提交"的保护。两个保证都成立，比"只放宽"更正确。

### D5 用改名解决
`ReopeningDuringExitIgnoresOldCompletionAndStaysOpen` ✅ 名实相符。

### 新增的两条测试是有效覆盖，不是凑数

- `PushUnregisteredPanelSucceedsWhenStackIsEmptyOrRestrictionIsDisabled`：3 组 `[TestCase]` 恰好覆盖我指出的两个放宽条件，并**刻意省略**（栈非空 + 限制开启）这个应当被拒的组合；断言 `FindNode == null` 证明目标确实游离、`Navigated` 恰好一次、事后仍未登记。
- `QueuedPushRevalidatesAgainstLatestCurrentAndContinues`：补上了 §8.5 第 2/8 项此前只被间接覆盖的路径——出队时用最新 `Current` 重校验，非法意图被丢弃后**后续意图仍继续执行**。

### 第 2 轮遗留（非阻塞）

- **测试编号重复**：`Test18_` 前缀现在被 4 个方法共用、`Test19_` 被 2 个共用，与文档 §13 的编号清单不再一一对应，按编号索引会混乱。建议改 `Test18a/18b` 或语义化命名。
- `restrictToDirectChildren` 通过反射私有字段设置：不破坏 public API，可接受；但若将来开放为公开配置项，该测试要跟着改。
- D3/D4/D6/D7/D8 未处理（本轮未在范围内），仍按已知限制记录。

**复验结论**：D1 / D2 / D5 均**真实修复**，未引入新问题，编译与运行证据复跑一致。P0 的验收结论从"有条件通过"更新为：**代码与测试交付物通过；唯一剩余门槛是在真实 Unity 中执行 EditMode 测试与视觉验收**。

---

## 11. Unity 真实执行（用户关闭 Editor 后补齐 —— 门槛已过 ✅）

### 11.1 命令

```powershell
# 目标套件（P0 的两个测试类）
Unity.exe -batchmode -projectPath "E:\Projects_Unity\MiHoYo" -runTests -testPlatform EditMode `
  -testFilter "MotionTests|UINavigationQueueTests" `
  -testResults "E:\Projects_Unity\MiHoYo\.dsh-scratch\test-results-editmode.xml" `
  -logFile    "E:\Projects_Unity\MiHoYo\.dsh-scratch\unity-test.log"

# 全量 EditMode（回归，去掉 -testFilter）
```

### 11.2 结果

| 套件 | total | passed | failed | result | 产物 |
|---|---|---|---|---|---|
| **P0 目标套件** | 24 | **24** | **0** | **Passed** | `test-results-editmode.xml` |
| 全量 EditMode（含既有 Gooey 测试） | 29 | 28 | 1 | Failed(Child) | `test-results-editmode-full.xml` |

P0 套件 24/24 全绿，覆盖 §13 的 1/3/4/9、12–20，外加 `DefaultPanelPathRemainsSynchronous`、D2 的 3 组 `[TestCase]`、`QueuedPushRevalidatesAgainstLatestCurrentAndContinues`、`ReopeningDuringExitIgnoresOldCompletionAndStaysOpen`、以及句柄复用与丢回调两条。

### 11.3 这条结果的**自证力**（比"全绿"更重要）

`LogAssert.Expect` 在期望未被满足时会直接判该测试失败。因此那两个我曾预测"在 Unity 里必失败"的测试——`Test18_DisabledPanelInvalidatesHandleButQueueRecovers`、`Test19_TimeoutAlsoRestoresOriginalRaycastValue`——现在**通过**，一次性证明三件事：

1. **D1 的字符串修复真实生效**（旧串 `"????????????"` 不可能等于中文日志）；
2. "**句柄失效 → 强制完成**"与"**看门狗超时 → 强制完成并继续队列**"这两条日志路径**确实被走到**（否则期望落空、测试必失败）；
3. §8.5 第 4 项（看门狗）与第 5 项（输入恢复）由"代码可指认"升级为"行为已验证"。

另外：
- `DefaultPanelPathRemainsSynchronous` 通过 → **无动画面板的向后兼容在真实 Unity 中得到确认**（此前只是代码层推理）；
- D2 三组参数化用例通过 → 恢复后的可接受域真实成立；
- `Test18_WatchdogUsesUnscaledTimeAndContinuesQueue` 在 `Time.timeScale = 0` 下通过 → unscaled 看门狗行为确认；
- `Test09_AnchorResamplesResizedRect` 用真实 `RectTransform` 改尺寸后通过 → 锚点延迟解析确认。

### 11.4 唯一失败的归属（有证据，不是猜）

`GooeyUI.Tests.GooeyUIGroupPhase2Tests.DrawingRect_MatchesUnityImage_ForTrimmedSpriteAndPreserveAspect` → `Showcase button sprite missing.`

- 该测试用**硬编码 GUID** 取资源：`AssetDatabase.GUIDToAssetPath("41a85c88cc395114e9b6c1b146910e2a")`（`Assets/Editor/GooeyUIGroupPhase2Tests.cs:126`）；
- 该 GUID 在 `Assets/` 下**全量搜索零命中**（已实测）→ `GUIDToAssetPath` 返回空串 → `LoadAllAssetsAtPath("")` 无结果 → `sprite == null` → 断言失败；
- 该测试类对 `UIBase` / `UINavigator` / `Motion` **零引用**（grep 证实）。

**结论：既有的、与本次改动无关的环境性失败**（美术资源被删除/重导入导致 GUID 变更）。它同样会发生在手动跑全量套件时。修法：把硬编码 GUID 改为按路径查找，或更新为当前 GUID。

### 11.5 过程记录（诚实）

- 第一次启动把 `-testResults/-logFile` 指向 `.dsh-scratch`，Unity 打印了两次 `.dsh-scratch is not a valid directory name`——**但结果文件仍正常生成**，属噪声警告。
- 我一度误判"作业秒退 = 失败"：**`Unity.exe` 在 Windows 上是启动器，它立刻返回，真正的编辑器进程在后台继续**。第二次启动因此被第一次的残留进程用工程锁挡住（`Another Unity instance is running with this project open`）。
- 教训：**不要用启动器的返回码或作业退出码判断 Unity 结果，要读 `-testResults` XML 与 `-logFile`**；启动后要确认派生进程并等它退出。

### 11.6 至此的证据完整度

| 项目 | 状态 |
|---|---|
| 编译（两程序集，双工具链） | ✅ 已验证 |
| 纯逻辑运行证据（25 项） | ✅ 已验证、可复现 |
| **Unity EditMode 行为（24 项）** | ✅ **已验证**（本节） |
| 全量回归（既有 5 个 Gooey 测试） | ✅ 4 过 1 失败，失败已归因为既有 GUID 陈旧 |
| 视觉 / 帧率 / 分辨率 / Gooey 相位 / Profiler | ⚠️ 仍为欠账（§8），需人工观感验收 |

---

## 12. 验证过程中对工作区的意外影响与归属

跑一次真实的 Unity 批处理会导入整个工程，因此审计（`verify-diff.ps1` 的内容哈希版）在验证末期报出了两个**不属于** P0 交付物的变化。已逐一查清归属：

| 变化 | 归属 | 依据 | 影响 |
|---|---|---|---|
| `Assets/A_UI/SlimeButtonMotion.cs` + `.meta` 新增（`??`） | **不是实现方、也不是验收方创建的** | 双方都被明令禁止在 `Assets/` 下建文件；内容为 `using DG.Tweening` + `Dictionary<GameObject, Tween>` 的"按钮组"运动脚本，与本次任务无关 | **无**。我的两次 Unity 批处理把它一起编译了，`Editor.log` 无 `error CS` → 该文件本身可编译。注意：`verify-build.ps1` 的补全范围只覆盖 `Motion/` 与 `Package/`，**不覆盖它**，它的编译结论来自 Unity 自己的管线 |
| `ProjectSettings/TimeManager.asset` 被改（`M`） | **本次验证的副作用**（批处理 Unity 退出时保存工程设置；用户的 Editor 退出保存也会产生同样结果） | `git diff` 显示只是序列化升级：`Fixed Timestep: 0.02` → `serializedVersion: 2` + `m_Rate` 有理数形式（`2822399 / 141120000 ≈ 0.02`，值等价）；`m_TimeScale` 仍为 **1** | **无功能影响**。特别确认：测试里的 `Time.timeScale = 0` **没有**被留在工程设置里（`finally` 还原生效）。若要恢复原文件格式：`git checkout -- ProjectSettings/TimeManager.asset`（下次打开 Editor 仍会再升级一次） |

同时确认：`.meta` 一类的"红线"命中仍是 `verify-diff.ps1` 的**已知假阳性**——运行中的 Unity 导入新资源时必然生成 `.meta`，这不是实现方的越界行为（§6 已记录）。`verify-diff.ps1` 现在也会明确打印"内容哈希覆盖 4 个历史哈希；406 个既有文件缺少历史哈希"——**它诚实暴露了自己的覆盖边界**，没有假装全量可比。
