# 独立验收：审计 Codex 到底动了哪些文件，是否越界
#
# 用法：pwsh -File .dsh-scratch/codex-review/verify-diff.ps1
#
# 基线是派活前抓的快照（.dsh-scratch/codex-review/baseline-status.txt）。
# 工作区在派活前本来就是脏的（451 条既有改动），所以只看"相对基线的增量"。

param(
    [string]$BaselinePath = '.dsh-scratch\codex-review\baseline-status.txt',
    [string]$BaselineHashesPath = '.dsh-scratch\codex-review\evidence\before-hashes.json',
    [string]$CaptureBaselinePath = ''
)

$ErrorActionPreference = 'Stop'
$Root = 'E:\Projects_Unity\MiHoYo'
Set-Location $Root
$LogPath = Join-Path $Root '.dsh-scratch\codex-review\evidence\verify-diff-latest.txt'
New-Item -ItemType Directory -Force -Path (Split-Path $LogPath) | Out-Null

function Convert-GitPath([string]$Path) {
    if ($Path -match ' -> ') { $Path = ($Path -split ' -> ')[-1] }
    if ($Path.StartsWith('"')) {
        # Old snapshots may contain Git's UTF-8 octal quoting of Chinese names.
        $Path = [regex]::Replace($Path, '(?:\\[0-7]{3})+', {
            param($m)
            [byte[]]$bytes = [regex]::Matches($m.Value, '\\([0-7]{3})') | ForEach-Object {
                [Convert]::ToByte($_.Groups[1].Value, 8)
            }
            [Text.Encoding]::UTF8.GetString($bytes)
        })
        $Path = ConvertFrom-Json $Path
    }
    return $Path
}

$AuditFailed = $false
$auditOutput = & {
try {
'运行时间: ' + (Get-Date -Format o)
'状态基线: ' + [IO.Path]::GetFullPath($BaselinePath)
'内容哈希基线: ' + [IO.Path]::GetFullPath($BaselineHashesPath)

$baseline = @{}
$baselineHashes = @{}
if (-not (Test-Path -LiteralPath $BaselinePath)) { throw "缺少状态基线: $BaselinePath" }
if ([IO.Path]::GetExtension($BaselinePath) -eq '.json') {
    $snapshot = Get-Content -LiteralPath $BaselinePath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($p in $snapshot.status.PSObject.Properties) { $baseline[$p.Name] = $p.Value }
    foreach ($p in $snapshot.hashes.PSObject.Properties) { $baselineHashes[$p.Name] = $p.Value }
    '内容哈希来源: 状态基线 JSON 内的 hashes（忽略单独哈希参数）'
} else {
    Get-Content -LiteralPath $BaselinePath -Encoding UTF8 | ForEach-Object {
        if ($_ -match '^(.{2}) (.+)$') {
            $status = $Matches[1]; $path = Convert-GitPath $Matches[2]
            $baseline[$path] = $status
        }
    }
    if (Test-Path -LiteralPath $BaselineHashesPath) {
        $hashes = Get-Content -LiteralPath $BaselineHashesPath -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($p in $hashes.PSObject.Properties) { $baselineHashes[$p.Name] = $p.Value }
    }
}

$now = @{}
$statusOutput = & git --no-optional-locks -c core.quotepath=false status --porcelain=v1 -uall
if ($LASTEXITCODE -ne 0) { throw "git status 失败: $LASTEXITCODE" }
$statusOutput | ForEach-Object {
    if ($_ -match '^(.{2}) (.+)$') {
        $status = $Matches[1]; $path = Convert-GitPath $Matches[2]
        $now[$path] = $status
    }
}

$added    = $now.Keys   | Where-Object { -not $baseline.ContainsKey($_) } | Sort-Object
$removed  = $baseline.Keys | Where-Object { -not $now.ContainsKey($_) }      | Sort-Object
$statusChanged = @($now.Keys | Where-Object { $baseline.ContainsKey($_) -and $baseline[$_] -ne $now[$_] })
$currentHashes = @{}
$contentChanged = @()
$missingHashes = @()
foreach ($path in (@($now.Keys) + @($baseline.Keys) + @($baselineHashes.Keys) | Sort-Object -Unique)) {
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $currentHashes[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($baselineHashes.ContainsKey($path)) {
            if ($baselineHashes[$path] -ne $currentHashes[$path]) { $contentChanged += $path }
        } elseif ($baseline.ContainsKey($path)) { $missingHashes += $path }
    } elseif ($baselineHashes.ContainsKey($path)) { $contentChanged += $path }
}
$changed = @(@($statusChanged) + @($contentChanged) | Sort-Object -Unique)

"================ 新增（Codex 新建的文件） ================"
if ($added)   { $added   | ForEach-Object { "  + $_" } } else { "  (无)" }

"================ 新增了但不在预期目录（越界嫌疑） ================"
$expected = @('Assets/A_UI/UI/Motion/', 'Assets/A_UI/UI/Package/', 'Assets/Editor/', '.dsh-scratch/')
$stray = $added | Where-Object { $p = $_; -not ($expected | Where-Object { $p.StartsWith($_) }) }
if ($stray)   { $stray   | ForEach-Object { "  ! $_" } } else { "  (无)" }

"================ 状态变化（既有文件被改动） ================"
if ($changed) {
    $changed | ForEach-Object {
        $reason = if ($contentChanged -contains $_) { '内容变化（SHA256）' } else { '状态码变化' }
        $sameStatus = $baseline.ContainsKey($_) -and $now.ContainsKey($_) -and $baseline[$_] -eq $now[$_]
        "  M $_ [$reason; 状态码相同=$sameStatus]"
        if ($contentChanged -contains $_) {
            "    基线=$($baselineHashes[$_]); 当前=$($currentHashes[$_])"
        }
    }
} else { "  (无)" }
"内容哈希覆盖: $($baselineHashes.Count) 个历史哈希；$($missingHashes.Count) 个既有文件缺少历史哈希，无法判断此前内容变化。"

"================ 被删除的文件 ================"
if ($removed) { $removed | ForEach-Object { "  - $_" } } else { "  (无)" }

"================ 红线检查 ================"
$touched = @($added) + @($changed) + @($removed)
$violations = @()

# 1. 不许碰 Library / Temp / ProjectSettings / .plastic / .meta
foreach ($t in $touched) {
    if ($t -match '^(Library|Temp|obj|ProjectSettings|\.plastic|\.git)/') { $violations += "禁区目录被改动: $t" }
    if ($t -match '\.meta$')                                        { $violations += ".meta 被改动: $t" }
}
# 2. 简报里点名不许改的文件
$forbidden = @(
    'Assets/Scripts/GooeyUIGroup.cs', 'Assets/Scripts/GooeyWarpEffect.cs',
    'Assets/Scripts/SdfBlendField.cs', 'Assets/Scripts/SdfFieldNode.cs', 'Assets/Scripts/SdfUiSpace.cs',
    'Assets/A_UI/UI/Package/UIManager.cs', 'Assets/A_UI/UI/Package/Subpanel.cs',
    'Assets/A_UI/UI/Package/Navigation/UINavButton.cs', 'Assets/A_UI/UI/Package/Navigation/UINavNode.cs',
    'Assets/A_UI/UI/Package/Navigation/UINavigationLocator.cs',
    'docs/ui-motion-module-design.md'
)
foreach ($t in $touched) { if ($forbidden -contains $t) { $violations += "被禁改文件被改动: $t" } }
# 3. 不许 git 操作
$head = git --no-optional-locks log -1 --format='%H %s' 2>&1
if ($LASTEXITCODE -ne 0) { throw "git log 失败: $LASTEXITCODE" }
if ($head -notmatch '^4396265') { $violations += "HEAD 变了（可能被 commit 了）: $head" }

if ($violations) { $violations | ForEach-Object { "  X $_" } } else { "  OK：未发现越界改动" }

"================ git 索引（不应有暂存改动） ================"
$staged = $now.GetEnumerator() | Where-Object { $_.Value[0] -ne ' ' -and $_.Value[0] -ne '?' } | Select-Object -ExpandProperty Key
$stagedNew = $staged | Where-Object { -not $baseline.ContainsKey($_) }
if ($stagedNew) { $stagedNew | ForEach-Object { "  ! 新增且已暂存: $_" } } else { "  OK：Codex 没有 git add 新文件" }
if ($CaptureBaselinePath) {
    if ([IO.Path]::GetFullPath($CaptureBaselinePath) -eq [IO.Path]::GetFullPath($BaselinePath)) {
        throw '不能覆盖正在比较的状态基线'
    }
    @{ capturedAt = (Get-Date -Format o); status = $now; hashes = $currentHashes } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $CaptureBaselinePath -Encoding UTF8
    "已捕获后续内容感知基线: $CaptureBaselinePath"
}
"日志文件: $LogPath"
} catch {
    "ERROR: $($_.Exception.Message)"
    $_ | Out-String
    $script:AuditFailed = $true
}
} 2>&1
# Finish all reads before opening the output log, which is itself an untracked file.
$auditOutput | Out-String -Stream | Tee-Object -FilePath $LogPath
if ($AuditFailed) { exit 1 }
