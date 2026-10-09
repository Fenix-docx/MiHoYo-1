# P1 独立验收：把 Codex 的改动与我 13:57 拍的快照逐文件比对
#
# 为什么不用 verify-diff.ps1：它的基线是两天前的 git 状态快照，
# 而 Motion 目录整个是 ?? 未入库状态、期间还有用户的手动改动，git 状态码完全不可靠。
# 本脚本改用**内容哈希**对快照做逐文件比对，能精确回答：
#   1) Codex 新建了什么、改了什么、删了什么
#   2) 简报里"不该动"的文件有没有被动（未授权改动）
#   3) 每个被修改的既有文件，是否按协议留下了"修改前"备份，且备份内容 == 我快照时的内容
#
# 用法: & 'E:\Projects_Unity\MiHoYo\.dsh-scratch\codex-review\verify-p1.ps1'

$ErrorActionPreference = 'Stop'
$Root = 'E:\Projects_Unity\MiHoYo'
Set-Location $Root

$SnapRoot = Get-ChildItem '.dsh-scratch\snapshots' -Directory | Sort-Object Name -Descending | Select-Object -First 1
$Snap = Join-Path $SnapRoot.FullName 'Motion'
$Backups = '.dsh-scratch\backups\p1'
$Live = 'Assets\A_UI\UI\Motion'

# 简报允许改动的既有文件（其余既有文件若被改 = 需要解释）
$allowed = @(
    'Path\PathSegment.cs',      # A: 段类型补全
    'Tracks\MotionTrack.cs',    # C: 接链条
    'MotionPlan.cs',            # C: 接链条
    'Core\MotionMath.cs',       # B: 弧长/LUT 辅助
    'Core\MotionTypes.cs'       # 可能需要的类型补充
)

function Get-Hashes([string]$base) {
    $map = @{}
    if (-not (Test-Path $base)) { return $map }
    Get-ChildItem $base -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring((Resolve-Path $base).Path.Length).TrimStart('\')
        $map[$rel] = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
    }
    return $map
}

$snapH = Get-Hashes $Snap
$liveH = Get-Hashes $Live

"快照基线: $($SnapRoot.Name)  ($($snapH.Count) 个文件)"
"实时目录: $Live  ($($liveH.Count) 个文件)"
""

$added    = $liveH.Keys | Where-Object { -not $snapH.ContainsKey($_) } | Sort-Object
$modified = $liveH.Keys | Where-Object { $snapH.ContainsKey($_) -and $snapH[$_] -ne $liveH[$_] } | Sort-Object
$deleted  = $snapH.Keys | Where-Object { -not $liveH.ContainsKey($_) } | Sort-Object

"================ 新建（$($added.Count)）================"
$added | ForEach-Object { "  + $_" }

"================ 修改（$($modified.Count)）================"
foreach ($m in $modified) {
    $tag = if ($allowed -contains $m) { 'OK 允许' } else { '!! 未授权改动' }
    $bak = Join-Path $Backups $m
    $bakState = if (-not (Test-Path $bak)) { '缺备份' }
                elseif ((Get-FileHash $bak -Algorithm SHA256).Hash -eq $snapH[$m]) { '备份==快照 OK' }
                else { '备份!=快照 (需解释)' }
    "  M {0,-34} [{1}]  备份: {2}" -f $m, $tag, $bakState
}

"================ 删除（$($deleted.Count)）================"
if ($deleted) { $deleted | ForEach-Object { "  - $_" } } else { "  (无)" }

""
"================ 用户今天(10/9)改过的文件是否被保住 ================"
# 这些文件不在 P1 允许改动清单里：内容应当与快照逐字节相同
$userTouched = @('Integration\UIPanelMotion.cs','Tracks\AnchorResolver.cs','Core\MotionSystem.cs',
                 'Core\MotionClock.cs','Core\MotionHandle.cs','Bind\RectBinder.cs','Integration\MotionOwnership.cs')
foreach ($f in $userTouched) {
    if (-not $liveH.ContainsKey($f)) { "  ? $f 已不存在" ; continue }
    if ($snapH[$f] -eq $liveH[$f]) { "  ✓ $f 未被改动（保住了）" }
    else { "  X $f 被改动了 → 需要 Codex 解释（简报里它不在允许改动清单）" }
}

""
"================ 汇总 ================"
$unauthorized = $modified | Where-Object { $allowed -notcontains $_ }
$noBackup = $modified | Where-Object { -not (Test-Path (Join-Path $Backups $_)) }
if ($unauthorized) { "  未授权改动: $($unauthorized.Count) 个 -> $($unauthorized -join ', ')" } else { "  未授权改动: 0" }
if ($noBackup)     { "  缺备份: $($noBackup.Count) 个 -> $($noBackup -join ', ')" } else { "  缺备份: 0" }
if ($deleted)      { "  删除: $($deleted.Count) 个 -> $($deleted -join ', ')" } else { "  删除: 0" }
