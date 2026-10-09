$ErrorActionPreference = 'Stop'
Set-Location 'E:\Projects_Unity\MiHoYo'
$failureCode = @'
function global:dotnet { $global:LASTEXITCODE = 37; Write-Output 'INJECTED_NATIVE_BUILD_FAILURE: exit=37 (no compiler invoked)' }
& .dsh-scratch/codex-review/verify-build.ps1
'@
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($failureCode))
$output = & powershell -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encoded 2>&1
$code = $LASTEXITCODE
$count = @(Get-ChildItem -LiteralPath . -Filter '*-Verify.csproj' -File).Count
$lines = @($output) + "INJECTED_SCRIPT_EXIT=$code" + "AFTER_FAILURE_ROOT_VERIFY_COUNT=$count"
$lines | Out-String | Set-Content -LiteralPath '.dsh-scratch/verify/cleanup-failure-transcript.txt' -Encoding UTF8
$lines
if ($code -ne 1 -or $count -ne 0) { throw 'Failure-path cleanup did not meet expectations' }
# This check overwrites build logs with synthetic failures. Rerun verify-build.ps1
# afterward to restore real build logs; it never invokes Unity or a compiler here.
