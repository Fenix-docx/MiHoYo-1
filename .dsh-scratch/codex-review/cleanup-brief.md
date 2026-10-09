# 清理任务：收尾我（验收方）留下的脚本副作用

你是执行者。上一轮你实现并修复了 UIMotion P0；这次的任务是**收尾验收脚本的副作用**——这些副作用是**验收方（另一个 agent）**留下的，不是你的锅，但现在要由你来清理和加固。

仓库：`E:\Projects_Unity\MiHoYo`。

## 现在正在发生的事（重要）

**有一个 Unity batchmode 实例正在跑 EditMode 测试**（验收方刚启动，工程锁已被它持有）。因此：

- **绝对不要启动 Unity**、不要碰 `Temp\UnityLockfile`、不要碰 `Library\`；
- **不要删除 `Temp\obj\` 下 Unity 自有目录**（`Assembly-CSharp`、`Assembly-CSharp-firstpass`、`Assembly-CSharp-Editor`），只清理由验收脚本产生的 `*-Verify*` 子目录；
- 不要动 `Assets/` 下任何文件（本轮不需要改任何游戏代码）；
- 不要 git add / commit。

## 要做的三件事

### 1. 给 `.dsh-scratch/codex-review/verify-build.ps1` 补两处

**(a) 补 `finally` 清理**：该脚本会在仓库根目录生成临时项目文件 `Assembly-CSharp-Verify.csproj`、`Assembly-CSharp-Editor-Verify.csproj`（放根目录是故意的，`Assets\...` 相对路径才有效），但**没有删除步骤**，这两个文件现在还躺在仓库根目录。请让脚本无论成功失败都在最后删除它们（并保持"失败时也删"）。

**(b) 把中间产物也重定向出 `Temp\`**：脚本目前只传了 `-p:OutputPath=...`，而 Unity 生成的 csproj 里写死了 `BaseIntermediateOutputPath = Temp\obj\$(MSBuildProjectName)`，导致 `Temp\obj\Assembly-CSharp-Verify\`、`Temp\obj\Assembly-CSharp-Editor-Verify\` 被创建。请额外传 `-p:BaseIntermediateOutputPath=...`（指向 `.dsh-scratch/verify/obj/$(MSBuildProjectName)` 之类），使该脚本**从此完全不写 `Temp\`**。注意：`BaseIntermediateOutputPath` 会传播到 ProjectReference，确认 `Assembly-CSharp-Verify` / `Assembly-CSharp-Editor-Verify` / `Assembly-CSharp-firstpass` 的中间产物都落在 `.dsh-scratch` 下。

### 2. 给 `.dsh-scratch/codex-review/verify-diff.ps1` 补日志落盘

该脚本的审计结果（新增文件 / 越界嫌疑 / 状态变化 / 被删除 / 红线检查 / git 索引检查）**目前只在 stdout，没有任何落盘**，导致结果不可复查。请把完整输出同时写入固定文件（建议 `.dsh-scratch/codex-review/evidence/verify-diff-latest.txt`，并在文件头写入运行时间与被比较的基线文件路径）。

**同时修一个真实缺陷**：该脚本用 `git status --porcelain` 的**状态码**做比较，因此**检测不到"某一轮又修改了同一个文件"**（比如第 2 轮改了 `UINavigator.cs` 而状态码仍是 ` M`）。请改成**内容感知**的比较：至少对"基线中已存在、当前状态码相同"的文件做内容哈希比对（可用 `git hash-object <path>`，或 PowerShell 的 `Get-FileHash`），把"状态码没变但内容变了"的文件也列进"状态变化"。**不要用 git 写操作**（`hash-object` 是只读的，可以用；`git add/commit/stash` 一律禁止）。

### 3. 删除历史遗留 + 建索引

- 删除仓库根目录的 `Assembly-CSharp-Verify.csproj`、`Assembly-CSharp-Editor-Verify.csproj`；
- 删除 `Temp\obj\Assembly-CSharp-Verify\`、`Temp\obj\Assembly-CSharp-Editor-Verify\`（**只删这两个**）；
- 新建 `.dsh-scratch/codex-review/INDEX.md`，写清"哪个脚本 / 哪个产物 → 输出到哪 → 结论在哪"，至少覆盖：`verify-build.ps1`、`verify-diff.ps1`、`impl-brief.md`、`repair-brief.md`、`impl-report.md`（含第 8 节修复）、`acceptance-report.md`、`evidence/`、`console/`、`probes/`，以及 `.dsh-scratch\verify\`、`.dsh-scratch\codex-repair-round2\` 的用途与"可否删除"。

## 验证要求（必须给可复跑证据）

1. **复跑修改后的 `verify-build.ps1`**：必须仍然 PASS（两个程序集 0 错误），并且**证明它不再写 `Temp\`**——给出复跑前后 `Temp\obj` 下 `*Verify*` 目录的存在性检查（应为不存在）、以及 `.dsh-scratch\verify\obj\` 下出现了中间产物的证据；
2. **复跑修改后的 `verify-diff.ps1`**：给出一条命令 + 原始输出的尾部，并证明日志文件已生成、且里面包含"内容变化"检测的结果（例如把 `Assets/A_UI/UI/Package/Navigation/UINavigator.cs` 列为已变化）；
3. 复跑后确认根目录不再残留 `*-Verify.csproj`；
4. 明确区分"已验证 / 未验证"。**不要启动 Unity**；Unity 侧不归你管。

## 不要做的事

- 不要改 `Assets/` 下任何 .cs、不要改 `docs/ui-motion-module-design.md`；
- 不要删 `.dsh-scratch/codex-review/` 下的验收工件（报告、evidence、console、probes），只**新增** `INDEX.md` 与 `evidence/verify-diff-latest.txt`；
- 不要为了让脚本"看起来通过"而放宽判定（内容哈希比较必须真的能发现改动）。

## 交付

把本轮结果**追加**到 `.dsh-scratch/codex-review/impl-report.md` 末尾，标题 `## 9. 验收脚本收尾（第 3 轮）`，包含：每条的改法、原始验证输出、以及任何你不同意或未能验证的点。
