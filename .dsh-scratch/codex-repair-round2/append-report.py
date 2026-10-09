from pathlib import Path
import hashlib, json

out = Path(__file__).resolve().parent
root = out.parents[1]
report = root / '.dsh-scratch/codex-review/impl-report.md'
original = report.read_bytes()
assert '## 8. 验收后修复（第 2 轮）'.encode('utf-8') not in original
(out / 'before/impl-report.md').write_bytes(original)
body = '''

## 8. 验收后修复（第 2 轮）

### 8.1 范围与问题处理

本轮按 repair-brief.md 修复三类问题。仅修改三份源码：
`Assets/Editor/UINavigationQueueTests.cs`、`Assets/A_UI/UI/Package/Navigation/UINavigator.cs`、
`Assets/A_UI/UI/Motion/Integration/UIPanelMotion.cs`。没有改设计文档、其他禁改源码、public API、场景或预制体，也没有新增 P1+ 功能。
验收目录只按简报明确要求追加本章节；原报告字节前缀保持不变，其余验收工件不变。
本轮脚本、探针、构建输出、控制台副本和证据均在 `.dsh-scratch/codex-repair-round2/`。

1. **问题 1：五处损坏文本已修复。** 两条 LogAssert 期望逐字匹配运行时已有中文日志；未改日志迁就测试。
   三处注释分别解释丢回调后检查 IsDone、异常后结束转移防止阻塞队列/输入恢复、instant 写入配方终态。
   **有损通道的根因未确证。** 检查留存 write-*.py 可见：write-tests.py:93/257、write-navigation.py:77/131/337、write-motion.py:4 已显式使用 UTF-8 写源码。
   因而没有证据支持“这些源码由 open(..., 'w') 的默认编码写成问号”这一具体归因；损坏可能发生在后续补丁或传输通道，但没有原始写入链路证据，不能断言。
   本轮中文通过 apply_patch 写入；验证脚本读写明确指定 UTF-8，落盘后额外检查完整中文日志相等、连续问号零匹配。
   后续同样应对脚本输入/管道和文件输出均显式使用 UTF-8，禁止有损替换，并在落盘后做精确文本检查；仅编译不能发现字符串语义损坏。
2. **问题 2：恢复 Push 可接受域。** 根因是把树成员资格从受限父子校验扩大成了所有目标的前置条件，且提交栈时又做了一次同样的拦截；上一轮报告遗漏披露此行为变化。
   ValidateTarget 仅在 `current != null && current != target && restrictToDirectChildren` 时同时要求目标登记、current 节点存在及直接子关系；其余路径只拒绝空目标。
   校验仍由 Execute 在出队时读取最新 Current 执行。Push/Replace 共用这一校验，故两者提交均允许出队时未登记的合法面板；同时记录 targetWasRegistered，保留已登记目标在转移期间被 RemoveNode 后不再提交入栈的既有保护。
   新增参数化测试覆盖空栈+限制开、空栈+限制关、非空栈+限制关，断言游离面板真正成为 Current、打开一次、事件一次、队列空闲且没有偷偷登记。
   通过测试内反射设置私有序列化字段，不新增 public/internal 运行时设置入口。
   另补出队校验测试：page3 在入队时为 page0 的子节点，出队时 Current 已为 page1，因此拒绝 page3，并继续执行 page2。
3. **问题 3：采用改名方案。** 原测试只覆盖旧句柄完成失效和面板保持打开，却在名字中承诺位置连续性。
   改名为 `ReopeningDuringExitIgnoresOldCompletionAndStaysOpen`，保留所有原断言。假面板始终构建 zero→one 路径，不代表 UIPanelMotion 的位置接管，故没有添加会误导验收的位置断言。

未采纳/不同意：不采纳未经证实的“Python 默认文件编码”根因结论；不采纳将必须登记进树作为默认规则。问题 3 按简报允许的二选一选择改名。没有放宽现有断言。

### 8.2 编译与运行证据

复跑入口（从项目根执行；不要执行 prepare.py 覆盖本轮初始快照）：

```powershell
python .dsh-scratch/codex-repair-round2/verify-build.py
python .dsh-scratch/codex-repair-round2/audit.py
```

verify-build.py 沿用上一轮方法，但使用独立的 `*-RepairRound2.csproj`，补齐 16 份运行时源文件与两份 Editor 测试文件的 Compile 项，重定向所有构建输出及项目引用。
逐文件在 scratch 副本首行注入 #error：运行时预期 16 错误、Editor 预期 2 错误；随后恢复 Compile 指向正式源码并成功编译。
未给 Assets 中的源码注入错误；finally 仅删除本轮生成的三个根目录项目副本，原 csproj（包括验收方已有 Verify 副本）保持不变。
最终源码 SHA-256 清单为 `evidence/compiled-sources.json`；变更全文为 `evidence/changes.diff`（Python difflib 生成，没有调用 git）。
运行时最终构建为 0 错误、3 个原有警告；Editor 最终构建为 0 错误、0 警告。

以下粘贴本轮最终验证命令及完整原始输出（EXIT_CODE 为脚本附加的进程退出码）：
'''
logs = [
    ('运行时逐文件编译反证（预期失败）', 'runtime-probe-failed.txt'),
    ('运行时恢复正式源码（通过）', 'runtime-final-passed.txt'),
    ('Editor 逐文件编译反证（含本轮修改的 UINavigationQueueTests）', 'editor-probe-failed.txt'),
    ('Editor 恢复正式源码（通过）', 'editor-final-passed.txt'),
    ('指定范围连续三个问号搜索：0 条；rg 无匹配退出码为 1，标准输出为空', 'question-search.txt'),
    ('控制台复跑：与原验收目录 Program.cs/csproj 字节相同的独立副本，直接编译正式源文件', 'console-run.txt'),
    ('编码调查：通过 subprocess 参数数组执行，下面为参数及原始输出', 'encoding-evidence.txt'),
    ('源码及保护范围审计（命令：python .dsh-scratch/codex-repair-round2/audit.py）', 'scope-audit.txt')]
for title, name in logs:
    body += '\n' + title + '\n\n```text\n' + (out / 'evidence' / name).read_text(encoding='utf-8') + '```\n'
body += '''
### 8.3 已验证与未验证

已验证：最终两程序集真实参与编译并通过；18 份源码哈希与编译时一致；两个中文日志期望与代码逐字相同；指定范围连续问号零匹配；原控制台 25 项检查 PASS；禁改源码、设计文档、原 csproj 及验收工件哈希保持（本报告仅追加）。

**Unity Editor 测试：已编译、未执行。** 包括新增 Push 参数化用例、最新 Current 校验及修复日志期望的测试。用户禁止启动 Unity，且已有 Editor 占用，因此不能声称 Unity 测试通过。
控制台仅验证数学、时钟及队列策略，不执行真实 UINavigator、MonoBehaviour 生命周期、看门狗、输入恢复或位置连续性。
本轮没有启动/停止 Unity，没有调用 git，没有写 .meta，没有写 Library/Temp/ProjectSettings/.plastic；构建读取既有依赖引用，输出均在本轮 scratch 下。
'''
with report.open('ab') as stream:
    stream.write(body.encode('utf-8'))
assert report.read_bytes()[:len(original)] == original
hashes = json.loads((out / 'protected-before.json').read_text(encoding='utf-8'))
for name, expected in hashes.items():
    path = root / name
    data = original if path == report else path.read_bytes()
    assert hashlib.sha256(data).hexdigest() == expected, name
result = f'REPORT_APPEND: original {len(original)} bytes preserved; section 8 appended\nPROTECTED_FILES: {len(hashes)-1} other files unchanged\n'
(out / 'evidence/report-append-audit.txt').write_text(result, encoding='utf-8')
print(result)
