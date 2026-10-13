$ErrorActionPreference = 'Stop'
$empty = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\scanner-empty'
New-Item -ItemType Directory -Path $empty -Force | Out-Null
. (Join-Path $PSScriptRoot 'verify-secrets.ps1') -Root $empty -ForDistribution
# 仅在内存中生成可辨识的假凭据，测试数据不会写进包或日志。
$fake = 'sk-' + ('x' * 32)
foreach ($kind in @('ascii','utf16','odd-utf16','boundary')) {
    if ($kind -eq 'ascii') { $bytes = [Text.Encoding]::ASCII.GetBytes($fake) }
    elseif ($kind -eq 'utf16') { $bytes = [Text.Encoding]::Unicode.GetBytes($fake) }
    elseif ($kind -eq 'odd-utf16') { $bytes = [byte[]](@(0) + [Text.Encoding]::Unicode.GetBytes($fake)) }
    else {
        $bytes = [byte[]]::new(1048576 + 60)
        $tokenBytes = [Text.Encoding]::ASCII.GetBytes($fake)
        [Array]::Copy($tokenBytes,0,$bytes,1048568,$tokenBytes.Length)
    }
    $violations.Clear()
    $memory = [IO.MemoryStream]::new($bytes)
    try { Test-Stream $memory 'memory-only-probe' } finally { $memory.Dispose() }
    if ($violations.Count -ne 1) { throw ('Scanner missed ' + $kind) }
    Write-Output ('PASS: scanner detects ' + $kind + ' fixture.')
}
$violations.Clear(); Test-DistributionName 'settings.json'
if ($violations.Count -ne 1) { throw 'Scanner accepted distributed user settings.' }
Write-Output 'PASS: scanner rejects distributed user settings.'
