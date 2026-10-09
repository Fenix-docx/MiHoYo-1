from pathlib import Path
import hashlib
import subprocess

root = Path(__file__).resolve().parents[2]
report = root / '.dsh-scratch/codex-review/impl-report.md'
prefix = report.read_bytes()[:58225]
assert hashlib.sha256(prefix).hexdigest() == '097214a59d445fc0668efbb37451a834a28785a759538022bdbdcf92fed1462c'

def read(relative):
    data = (root / relative).read_bytes()
    return data.decode('utf-16' if data.startswith((b'\xff\xfe', b'\xfe\xff')) else 'utf-8-sig').rstrip()

build = read('.dsh-scratch/verify/cleanup-build-transcript.txt')
failure = read('.dsh-scratch/verify/cleanup-failure-transcript.txt')
diff_path = '.dsh-scratch/codex-review/evidence/verify-diff-latest.txt'
diff = read(diff_path).splitlines()
test = read('.dsh-scratch/verify/cleanup-audit-selftest/result.txt').splitlines()
fixture = next(i for i, line in enumerate(test) if line.startswith('  M .dsh-scratch/verify/cleanup-audit-selftest/fixture.txt '))
preservation = subprocess.run(['python', str(root / '.dsh-scratch/verify/cleanup-preservation-check.py')],
                             cwd=root, capture_output=True, text=True, check=True).stdout.rstrip()

section = r'''

## 9. 验收脚本收尾（第 3 轮）

本轮依据 cleanup-brief.md 执行，收尾验收方脚本留下的副作用。未启动或停止 Unity，未访问 Library 或 Temp/UnityLockfile，未改 Assets、设计文档或原始 Unity csproj，未执行 git add/commit/stash。Unity batchmode 测试成败不纳入本轮结论。

### 9.1 逐项改法与清理结果

1. **verify-build.ps1**：构建主体加 try/finally，构建返回非零或抛异常都删除临时 Verify csproj；构建失败返回退出码 1。额外传 `-p:BaseIntermediateOutputPath=<scratch>\obj\`，保留 OutputPath 指向 scratch/bin。只读 MSBuild 查询发现，CLI 全局属性中的 `$(MSBuildProjectName)` 不展开，不能直接用它区分目录。因此三个临时副本使用 `TreatAsLocalProperty="BaseIntermediateOutputPath"`，分别指定独立目录；ProjectReference 重定向到副本，firstpass 也使用第三份临时副本并纳入 finally。原始 csproj 不改。三个 obj 目录为 Assembly-CSharp-Verify、Assembly-CSharp-Editor-Verify、Assembly-CSharp-firstpass-Verify。构建串行执行；Windows PowerShell 5.1 的原生 stderr 以实际退出码判定，避免空 stderr/警告触发 Stop，错误仍完整落盘，判定不放宽。
2. **verify-diff.ps1**：六类完整结果同时写 stdout 和固定 evidence/verify-diff-latest.txt，头部含 ISO 时间、状态基线和哈希基线路径。先读完再打开输出日志，解决对自身日志做 Get-FileHash 时的文件锁。内容 SHA256 变化与状态码变化取并集，进入“状态变化”及红线检查，同状态也能检测内容修改。默认使用既有 before-hashes.json 真实历史哈希；支持包含 status/hashes 的 JSON 基线及 -CaptureBaselinePath，不能覆盖正在比较的基线。git 仅调用带 --no-optional-locks 的 status/log，无写操作。旧 Git 中文八进制引用路径正确解码，避免虚假新增/删除。
3. **历史遗留**：开始检查时根目录两份 Verify csproj 存在，现已删除。指定的两个 Temp/obj Verify 目录在开始检查时已不存在，Temp/obj 本身亦不存在，因此没有执行目录删除；没有删除任何 Unity 自有目录。复跑后根目录所有 *-Verify.csproj 数量为 0。
4. **索引**：新增 INDEX.md，覆盖两份脚本、简报、报告（含第 8 节）、evidence/console/probes、verify 与 codex-repair-round2 的输入输出、结论位置和可删除性。既有验收工件未删除或覆盖；新索引及最新 diff 日志放 codex-review，额外复跑辅助脚本和原始验证记录放 .dsh-scratch/verify。

### 9.2 真实构建复跑与 Temp 路径证据

本机没有 pwsh，使用 Windows PowerShell。命令：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .dsh-scratch/codex-review/verify-build.ps1
```

前后检查和中间产物检查可复跑：

```powershell
foreach ($p in @('Temp\obj','Temp\obj\Assembly-CSharp-Verify','Temp\obj\Assembly-CSharp-Editor-Verify')) {
    "$p exists=$(Test-Path -LiteralPath $p)"
}
@(Get-ChildItem -LiteralPath . -Filter '*-Verify.csproj' -File).Count
Get-ChildItem .dsh-scratch/verify/obj -Directory | ForEach-Object {
    $_.FullName
    Get-ChildItem -LiteralPath $_.FullName -File -Filter '*.dll'
}
```

以下为前检查、真实构建 stdout、后检查的原始输出，保存于 cleanup-build-transcript.txt。完整构建日志在 verify/build-runtime.log、build-editor.log。runtime 的 3 个 Slime 既有警告如实保留，两个程序集均 0 错误。MissingAdded=0 表示当前 Unity csproj 已包含扫描范围的源码，不代表跳过这些文件。

```text
''' + build + r'''
```

失败路径可复跑（子进程用函数模拟 dotnet 返回 37，不执行编译器）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .dsh-scratch/verify/cleanup-failure-check.ps1
# 自检会把构建日志写成合成失败；随后恢复真实日志：
powershell -NoProfile -ExecutionPolicy Bypass -File .dsh-scratch/codex-review/verify-build.ps1
```

同一模拟失败场景的原始输出如下。失败检查之后本轮已真实复跑构建，恢复 PASS 和真实日志。

```text
''' + failure + r'''
```

### 9.3 审计日志与同状态内容变化

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .dsh-scratch/codex-review/verify-diff.ps1
Get-Item .dsh-scratch/codex-review/evidence/verify-diff-latest.txt
Get-Content .dsh-scratch/codex-review/evidence/verify-diff-latest.txt -Tail 65
```

复跑退出码 0。固定日志已生成，末次运行文件长度为 ''' + str((root / diff_path).stat().st_size) + r''' 字节。头部原始输出：

```text
''' + '\n'.join(diff[:3]) + r'''
```

原始输出尾部；完整新增列表等见固定日志：

```text
''' + '\n'.join(diff[-65:]) + r'''
```

**审计不是“无红线 PASS”**：旧状态基线以来的 .meta、ProjectSettings、ArtSample 等差异仍如实列出，旧快照不能将它们归因到某个 agent。本轮前后保护范围哈希相同，不能把这些历史差异当作本轮新增改动。没有放宽、抹平或删除审计提示。diff 退出 0 表示审计成功完成，不表示所有审计项通过。

同状态内容比较独立自检只修改 scratch fixture，复跑命令：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .dsh-scratch/verify/cleanup-audit-selftest/run.ps1
# 自检用 JSON 基线；结束后恢复默认历史审计日志：
powershell -NoProfile -ExecutionPolicy Bypass -File .dsh-scratch/codex-review/verify-diff.ps1
```

原始内容变化行，完整输出在 cleanup-audit-selftest/result.txt：

```text
''' + '\n'.join(test[fixture:fixture+2]) + r'''
SELFTEST PASS: scratch fixture ?? -> ??, SHA256 changed; Assets untouched
```

**历史覆盖限制**：baseline-status.txt 只有状态码，不能凭当前内容重建过去哈希。可用历史 SHA256 只有 before-hashes.json 的 4 项；本次 406 个既有文件缺少历史哈希，明确标为无法判断。UINavigator 和 UIbase 均依据真实历史哈希检测出状态码相同但内容变化。下一轮开始前捕获 status/hashes JSON 后，同状态文件即可逐一比较；自检已证实。没有伪造全量旧基线或把证据缺失当成未变化。

### 9.4 保护范围、已验证与未验证

```powershell
python .dsh-scratch/verify/cleanup-preservation-check.py
```

只读脚本固定本轮执行前的文件计数、按路径排序的 SHA256 聚合值与报告旧前缀长度/哈希；报告追加后仍可复跑检查原前缀。原始输出：

```text
''' + preservation + r'''
```

**已验证**：两个程序集真实 dotnet 构建退出 0、0 错误；三个项目的 obj 实际产物分目录落 scratch；复跑前后 Temp/obj 和两处 Verify 目录不存在；根目录不残留 Verify csproj；模拟非零退出仍 finally 清理。早期 Windows PowerShell stderr 抛异常时也已观察到 finally 执行，无临时项目残留。六类审计完整落盘，UINavigator 同状态内容变化被检出，scratch 同状态内容自检 PASS；4,952 个 Assets 文件、135 个既有验收工件、5 个原始 csproj 加设计文档均字节不变，本报告仅追加。

**未验证 / 不同意过度推断**：本轮不执行 Unity EditMode 或视觉验收，不读取运行中 Unity 的测试结果，已有 batchmode 的成败由验收方确认。缺少历史哈希的 406 项不能追溯旧内容变化。旧基线红线提示不能直接当成本轮违规。没有执行系统级文件 I/O 跟踪；不写 Temp 的证据为输出属性隔离、三个 obj 实际产物和要求的前后存在性检查，不能声称做过逐次系统调用跟踪。

报告追加过程中发现 Windows PowerShell 向 Python 传递中文会替换为问号，已改用 UTF-8 文件脚本重写本轮新增段落。原报告前 58,225 字节不变；追加后再执行保护检查确认。
'''

# Repair only the new round-3 suffix; all historical report bytes stay unchanged.
# TreatAsLocalProperty quotes are literal XML, not escaped source syntax.
section = section.replace('TreatAsLocalProperty=\\"BaseIntermediateOutputPath\\"',
                          'TreatAsLocalProperty="BaseIntermediateOutputPath"')
with report.open('r+b') as stream:
    stream.seek(len(prefix))
    stream.truncate()
    stream.write(section.replace('\n', '\r\n').encode('utf-8'))
print('SECTION_9_UTF8_WRITTEN; ORIGINAL_PREFIX_PRESERVED')
