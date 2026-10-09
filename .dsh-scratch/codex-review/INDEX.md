# UIMotion 验收与修复工件索引

工作目录：`E:\Projects_Unity\MiHoYo`。本目录中的验收报告、evidence、console、probes 均保留，本轮不删除。收尾结论见 `impl-report.md` 第 9 节；第 8 节为第 2 轮修复。

| 脚本 / 工件 | 输入与用途 | 输出到哪 | 结论在哪 / 可否删除 |
|---|---|---|---|
| `verify-build.ps1` | 复制 Unity csproj、补齐磁盘上的 C# 编译项，执行 dotnet 构建 | `.dsh-scratch/verify/bin/`、`obj/<项目名>/`、`build-runtime.log`、`build-editor.log`；根目录三份 `*-Verify.csproj` 仅运行期间存在，finally 删除 | stdout、两份构建日志；本轮证据见报告第 9 节。脚本保留 |
| `verify-diff.ps1` | 只读 git 状态与 HEAD；比较状态及 SHA256 内容 | `evidence/verify-diff-latest.txt`，同时输出 stdout；固定日志每次覆盖 | 六类审计完整输出在该日志；报告第 9 节解释历史红线与哈希覆盖限制。脚本和最新证据保留 |
| `baseline-status.txt` | 派活前的状态码基线，没有全量内容哈希 | 只读输入，不覆盖 | 配合 `evidence/before-hashes.json` 的 4 个历史 SHA256；没有历史哈希的文件无法追溯内容变化。保留 |
| `impl-brief.md` | 初次实现权威任务说明 | 实现结果写入 `impl-report.md` | 对照实现报告前 7 节。保留 |
| `repair-brief.md` | 第 2 轮修复说明 | `.dsh-scratch/codex-repair-round2/` 与报告第 8 节 | `impl-report.md` 第 8 节、`acceptance-report.md` 第 2 轮复验。保留 |
| `cleanup-brief.md` | 第 3 轮验收脚本收尾说明 | 两份 ps1、此 INDEX、最新审计日志与报告第 9 节 | `impl-report.md` 第 9 节。保留 |
| `impl-report.md` | 实现方交付与原始验证证据 | 本文件只追加；第 8 节修复、第 9 节脚本收尾 | 各节的已验证 / 未验证；保留 |
| `acceptance-report.md` | 验收方独立审核与复验 | 本目录 | 验收报告中的结论；Unity 执行状态以验收方新证据为准。保留 |
| `evidence/` | 原始编译、控制台、哈希、失败探针及范围审计；最新 diff 日志 | 本目录 `evidence/` | 对应报告引用；保留，不在本轮清理 |
| `console/` | 独立数学、时钟、队列策略证明，不等同 Unity 生命周期测试 | 此目录自己的 bin/obj 与 evidence 中控制台日志 | `impl-report.md`、`acceptance-report.md`；源码及日志保留，缓存未来确认无运行依赖后可删 |
| `probes/` | 编译覆盖验证副本，不修改 Assets 正式源码 | 此目录及 evidence 的探针失败日志 | 证明正式新增文件纳入编译；保留 |
| `.dsh-scratch/verify/` | 当前 verify-build 的隔离输出；本轮复跑记录与审计自检也放此处 | bin、三个项目独立 obj、两份构建日志、`cleanup-*-transcript.txt`、`cleanup-audit-selftest/` | 报告第 9 节。bin/obj 为可重建缓存，无构建运行时可删；日志及自检证据应先归档。本轮不删 |
| `.dsh-scratch/codex-repair-round2/` | 第 2 轮保护范围快照、修复前副本、编译探针、控制台及原始日志 | before、probes、console、build、evidence、protected-before.json 等 | 报告第 8 节；快照、源码、日志保留。build/bin、build/obj、console/bin、console/obj 为可重建缓存，未来确认无运行依赖后可删；本轮不删 |

## 复跑（无需 Unity）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .dsh-scratch/codex-review/verify-build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .dsh-scratch/codex-review/verify-diff.ps1
```

这台环境没有 `pwsh`，使用 Windows PowerShell。diff 日志带运行时间、状态基线、哈希基线路径。旧基线缺少全量哈希，脚本不会把“无法判断”声称为“无内容变化”。后续轮次开始前，可捕获带 status/hashes 的完整 JSON 基线，再于轮次结束后比较（不可覆盖当前比较的基线）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .dsh-scratch/codex-review/verify-diff.ps1 -CaptureBaselinePath .dsh-scratch/verify/next-round-baseline.json
powershell -NoProfile -ExecutionPolicy Bypass -File .dsh-scratch/codex-review/verify-diff.ps1 -BaselinePath .dsh-scratch/verify/next-round-baseline.json
```

本轮严禁启动 Unity，不访问 Library 或 UnityLockfile，不修改 Assets、设计文档或原 csproj，不执行 git add/commit/stash。只清理指定历史 Verify 项目；Temp/obj 下 Unity 自有目录不在清理范围内。
