# 独立验收：把 Codex 新增的 .cs 真正纳入编译范围后再构建
#
# 背景（这是本仓库最容易出现的"假通过"）：
#   Assembly-CSharp.csproj / Assembly-CSharp-Editor.csproj 是 Unity 生成的，
#   用显式 <Compile Include="..."> 列表。新增文件不会自动进去，
#   直接 dotnet build 会静默跳过新文件 → "编译通过"是假的。
#
# 本脚本：复制 csproj 为 *-Verify.csproj，补上磁盘上存在但列表里没有的 .cs，
#         再构建副本。产物输出到 .dsh-scratch/verify/{bin,obj}，不写 Temp。
#
# 用法：pwsh -File .dsh-scratch/codex-review/verify-build.ps1

$ErrorActionPreference = 'Stop'
$Root = 'E:\Projects_Unity\MiHoYo'
Set-Location $Root

$Scratch = Join-Path $Root '.dsh-scratch\verify'
$Bin     = Join-Path $Scratch 'bin'
$Obj     = Join-Path $Scratch 'obj'
$VerifyProjects = @('Assembly-CSharp-Verify.csproj', 'Assembly-CSharp-Editor-Verify.csproj', 'Assembly-CSharp-firstpass-Verify.csproj')
New-Item -ItemType Directory -Force -Path $Bin | Out-Null

function Get-CompileList([string]$csproj) {
    $xml = Get-Content $csproj -Raw
    [regex]::Matches($xml, '<Compile Include="([^"]+)"') |
        ForEach-Object { $_.Groups[1].Value.Replace('\', '/') } | Sort-Object -Unique
}

function Get-DiskFiles([string[]]$roots, [switch]$EditorOnly) {
    $out = @()
    foreach ($r in $roots) {
        $p = Join-Path $Root $r
        if (-not (Test-Path $p)) { continue }
        $out += Get-ChildItem -Path $p -Recurse -Filter *.cs -File | ForEach-Object {
            $rel = $_.FullName.Substring($Root.Length + 1).Replace('\', '/')
            $isEditor = ($rel -split '/') -contains 'Editor'
            if ($EditorOnly) { if ($isEditor) { $rel } } else { if (-not $isEditor) { $rel } }
        }
    }
    $out | Where-Object { $_ } | Sort-Object -Unique
}

function New-VerifyProject {
    param(
        [string]$SourceCsproj,
        [string]$OutCsproj,
        [string[]]$ScanRoots,
        [switch]$EditorOnly,
        [switch]$RepointProjectRefs
    )

    $listed  = Get-CompileList $SourceCsproj
    $onDisk  = Get-DiskFiles -roots $ScanRoots -EditorOnly:$EditorOnly
    $missing = $onDisk | Where-Object { $listed -notcontains $_ }

    $xml = Get-Content $SourceCsproj -Raw
    if ($RepointProjectRefs) {
        foreach ($name in @('Assembly-CSharp', 'Assembly-CSharp-firstpass')) {
            $xml = $xml.Replace(('Include="{0}.csproj"' -f $name), ('Include="{0}-Verify.csproj"' -f $name))
        }
    }
    # CLI global properties do not expand $(MSBuildProjectName). Each disposable
    # project must evaluate its own directory, including the firstpass reference.
    $xml = $xml.Replace('<Project>', '<Project TreatAsLocalProperty="BaseIntermediateOutputPath">')
    $projectObj = Join-Path $Obj ([IO.Path]::GetFileNameWithoutExtension($OutCsproj))
    $xml = [regex]::Replace($xml, '<BaseIntermediateOutputPath>[^<]*</BaseIntermediateOutputPath>',
        ('<BaseIntermediateOutputPath>{0}\</BaseIntermediateOutputPath>' -f $projectObj))
    if ($missing.Count -gt 0) {
        $items = ($missing | ForEach-Object { "    <Compile Include=`"$($_ -replace '/','\' )`" />" }) -join "`r`n"
        $xml = $xml.Replace('</Project>', "<ItemGroup>`r`n$items`r`n  </ItemGroup>`r`n</Project>")
    }
    Set-Content -Path $OutCsproj -Value $xml -Encoding UTF8
    return $missing
}

function Invoke-VerifyBuild([string]$Project) {
    # Windows PowerShell 5.1 wraps native stderr as ErrorRecord. Check the native
    # exit code, rather than aborting on a warning or blank stderr line.
    $ErrorActionPreference = 'Continue'
    $output = & dotnet build $Project --nologo -v m -m:1 -p:BuildInParallel=false -p:RestoreDisableParallel=true "-p:OutputPath=$Bin" "-p:BaseIntermediateOutputPath=$Obj\" 2>&1
    return [pscustomobject]@{ Output = $output; Exit = $LASTEXITCODE }
}

$results = @()
$failed = $false
try {
# Original Unity-generated projects stay untouched; firstpass also uses a copy.
$null = New-VerifyProject -SourceCsproj 'Assembly-CSharp-firstpass.csproj' `
    -OutCsproj 'Assembly-CSharp-firstpass-Verify.csproj' -ScanRoots @()

# ---- 运行时程序集 ----
$missingRt = New-VerifyProject -SourceCsproj 'Assembly-CSharp.csproj' `
    -OutCsproj 'Assembly-CSharp-Verify.csproj' `
    -ScanRoots @('Assets/A_UI/UI/Motion', 'Assets/A_UI/UI/Package') -RepointProjectRefs

$buildRt = Invoke-VerifyBuild 'Assembly-CSharp-Verify.csproj'
$outRt = $buildRt.Output
$codeRt = $buildRt.Exit
$results += [pscustomobject]@{ Assembly = 'Assembly-CSharp'; Exit = $codeRt; MissingAdded = $missingRt.Count; Missing = ($missingRt -join ', ') }

# ---- Editor 程序集（含 Editor 测试） ----
$missingEd = New-VerifyProject -SourceCsproj 'Assembly-CSharp-Editor.csproj' `
    -OutCsproj 'Assembly-CSharp-Editor-Verify.csproj' `
    -ScanRoots @('Assets/Editor', 'Assets/A_UI/UI/Motion', 'Assets/A_UI/UI/Package') `
    -EditorOnly -RepointProjectRefs

$buildEd = Invoke-VerifyBuild 'Assembly-CSharp-Editor-Verify.csproj'
$outEd = $buildEd.Output
$codeEd = $buildEd.Exit
$results += [pscustomobject]@{ Assembly = 'Assembly-CSharp-Editor'; Exit = $codeEd; MissingAdded = $missingEd.Count; Missing = ($missingEd -join ', ') }

# ---- 报告 ----
"================ 编译范围补齐情况 ================"
$results | Format-List

"================ 运行时程序集构建输出（尾部） ================"
$outRt | Select-Object -Last 20
($_ = $outRt | Out-String) | Set-Content -Path (Join-Path $Scratch 'build-runtime.log') -Encoding UTF8

"================ Editor 程序集构建输出（尾部） ================"
$outEd | Select-Object -Last 20
($_ = $outEd | Out-String) | Set-Content -Path (Join-Path $Scratch 'build-editor.log') -Encoding UTF8

"================ 结论 ================"
if ($codeRt -eq 0 -and $codeEd -eq 0) { "PASS: 两个程序集都编译通过（新文件已纳入编译范围）" }
else { $failed = $true; "FAIL: runtime=$codeRt editor=$codeEd —— 详见上面的错误行" }
} finally {
    foreach ($project in $VerifyProjects) {
        $path = Join-Path $Root $project
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
    'CLEANUP: 三个临时 Verify csproj 已删除（成功/失败均执行）'
}
if ($failed) { exit 1 }
