# 修复任务：UIMotion P0 验收发现的 3 类问题（限定范围，不要扩大改动）

你是实现者。上一轮你交付的 P0 已通过独立验收的**编译与运行证据**部分，但验收发现以下问题，需要修复。
仓库：`E:\Projects_Unity\MiHoYo`。原始任务说明见 `.dsh-scratch/codex-review/impl-brief.md`（其硬性约束**继续有效**：不启动 Unity、不碰 Library/Temp/ProjectSettings/.plastic、不 git、不写 .meta、不改设计文档、不改简报里点名的禁改文件）。

## 问题 1（必须修，会导致 Unity 测试真实失败）：字符串被写坏成 `?`

有 5 处中文字符串在落盘时被有损编码成了 `?`，其中 **2 处是功能性断言**：

| 位置 | 现状 | 问题 |
|---|---|---|
| `Assets/Editor/UINavigationQueueTests.cs:155` | `LogAssert.Expect(LogType.Warning, "[UINavigator] ????????????");` | 代码实际打印 `"[UINavigator] 转移句柄失效，强制完成。"`（`UINavigator.cs:183`）→ **期望永不匹配，测试必失败** |
| `Assets/Editor/UINavigationQueueTests.cs:165` | `LogAssert.Expect(LogType.Error, "[UINavigator] ??????????????????");` | 代码实际打印 `"[UINavigator] 转移看门狗超时，强制完成并继续队列。"`（`UINavigator.cs:188`）→ **测试必失败** |
| `Assets/Editor/UINavigationQueueTests.cs:142` | 注释 `// ???????... IsDone?` | 仅注释，但请一并修好 |
| `Assets/A_UI/UI/Package/Navigation/UINavigator.cs:317` | 注释 `// ???????????????????` | 仅注释，请补回原意（说明这里为什么 catch 后强制完成） |
| `Assets/A_UI/UI/Motion/Integration/UIPanelMotion.cs:16` | `/// <summary>???????????????????</summary>` | XML 文档，请补回原意 |

要求：
- 把 2 处断言**改成与代码实际日志逐字一致**的中文（不要改动代码里的日志文案去迁就测试；以代码为准）。
- 修复后必须自证：对 `Assets/A_UI/UI/Motion/**`、`Assets/A_UI/UI/Package/**`、`Assets/Editor/MotionTests.cs`、`Assets/Editor/UINavigationQueueTests.cs` 全量搜索连续三个 `?`，结果必须为 0 条，并粘贴搜索命令与原始输出。
- **根因**：请查明你写文件的通道为什么会有损（你用了若干 `write-*.py`；Windows 上 Python 默认编码不是 UTF-8，`open(..., 'w')` 会把中文写成 `?`）。在报告里写清根因，并说明以后怎么避免（例如显式 `encoding='utf-8'`）。如果你无法确证根因，就写"未确证"，不要编。

## 问题 2（必须修，违反简报红线 1"默认零行为变化"）：Push 被悄悄收紧了

你在 `UINavigator.ValidateTarget`（`UINavigator.cs:210-227`）里加了 `FindNode(target) == null → 丢弃`，即**目标必须是导航树里的节点**。

但原始 `UINavigator.Push(UIBase)`（改动前版本见 `.dsh-scratch/codex-review/evidence/UINavigator.before.cs`）的语义是：只有当 `current != null && restrictToDirectChildren` 时才做父子校验；**其余情况（栈为空、或 `restrictToDirectChildren == false`）任意非空面板都能 Push 成功**。

所以你的实现缩小了可接受输入域，而这条行为变化在实现报告 §5.2/§6 里**没有披露**。

要求：恢复原始可接受域，同时保留"出队时用最新 Current 重新校验"这一 §8.5 要求。即：
- `current != null && current != target && restrictToDirectChildren` → 仍要求目标是树节点且是 current 的直接子节点（保持你现在的严格校验）；
- 其余情况 → 只要目标非空即可执行（与改动前一致），不要因为"不在导航树里"就丢弃。
- 为此补一条 Editor 测试：在 `restrictToDirectChildren` 关闭（或栈为空）时，Push 一个**未登记进树**的 UIBase 必须成功。注意 `restrictToDirectChildren` 是 `[SerializeField] private`，你可能需要加一个 `internal` 的设置入口或通过反射在测试里设置——选一种**不破坏既有 public API** 的方式，并在报告里说明你的选择。
- 如果你认为"必须收紧"才是对的，不要默默改：在报告里单独论证，并把开关做成显式配置。

## 问题 3（建议修）：一条测试的名字承诺了它没验证的行为

`Assets/Editor/UINavigationQueueTests.cs:188` `ReopeningDuringExitKillsOldCompletionAndKeepsPosition` 的名字里有 "KeepsPosition"，但**没有任何位置断言**。

要求：要么补上位置连续性断言（退场到一半反悔重开时，`anchoredPosition` 不得跳变），要么把测试名改成与断言相符。二选一，并说明理由。

## 不要做的事

- 不要顺手重构、不要格式化既有文件、不要改 `docs/ui-motion-module-design.md`、不要新增 P1+ 功能。
- 不要为了让测试通过而放宽断言（问题 1 是修期望值让它匹配代码，不是改代码让它匹配 `?`）。
- 不要删除或"清理" `.dsh-scratch/codex-review/` 下的验收工件（`verify-build.ps1`、`verify-diff.ps1`、`impl-brief.md`、你上一轮的 `evidence/` 等）；那是验收方的东西。

## 验证与交付

1. 重新证明两个程序集编译通过（沿用你上一轮那套"补齐 Compile 项 + 逐文件探针 + 恢复"的方法，或等价的可审计方法），**必须包含新改动的测试文件**；给出命令与原始输出。
2. 问题 1 的 `?` 全量搜索结果（0 条）。
3. 复跑你的控制台证据程序，确认仍全绿（并粘贴原始输出）。
4. 明确区分"已验证 / 未验证"——Unity Editor 测试仍然无法在本机执行（Editor 被占用），继续如实标注为"已编译、未执行"。
5. 把本次修复追加成实现报告的新章节（写到 `.dsh-scratch/codex-review/impl-report.md` 末尾，标题 `## 8. 验收后修复（第 2 轮）`），包含：每条问题的根因、改法、证据、以及你不同意或未采纳的点。
