$ErrorActionPreference = 'Stop'
Set-Location 'E:\Projects_Unity\MiHoYo'
$fixture = '.dsh-scratch/verify/cleanup-audit-selftest/fixture.txt'
$snapshot = '.dsh-scratch/verify/cleanup-audit-selftest/baseline.json'
$transcript = '.dsh-scratch/verify/cleanup-audit-selftest/result.txt'
$script = '.dsh-scratch/codex-review/verify-diff.ps1'
'before' | Set-Content -LiteralPath $fixture -Encoding UTF8
$null = & powershell -NoProfile -ExecutionPolicy Bypass -File $script -CaptureBaselinePath $snapshot
if ($LASTEXITCODE -ne 0) { throw 'Snapshot capture failed' }
'after' | Set-Content -LiteralPath $fixture -Encoding UTF8
$output = & powershell -NoProfile -ExecutionPolicy Bypass -File $script -BaselinePath $snapshot
$code = $LASTEXITCODE
$output | Out-String | Set-Content -LiteralPath $transcript -Encoding UTF8
$expected = "  M $fixture [内容变化（SHA256）; 状态码相同=True]"
if ($code -ne 0 -or -not ($output -contains $expected)) { throw 'Same-status content change was not detected' }
$output | Select-String -SimpleMatch $fixture -Context 0,1
'SELFTEST PASS: scratch fixture ?? -> ??, SHA256 changed; Assets untouched'
'FULL_OUTPUT: ' + $transcript
