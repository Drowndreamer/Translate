param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [switch]$Staged,
    [switch]$ForDistribution
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$resolvedRoot = [IO.Path]::GetFullPath($Root)
# 仅报告文件名，绝不打印匹配的凭据。
$pattern = [regex]::new('(?<![A-Za-z0-9])(?:sk-[A-Za-z0-9_-]{20,}|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|AKIA[A-Z0-9]{16})', [Text.RegularExpressions.RegexOptions]::Compiled)
$violations = [Collections.Generic.List[string]]::new()
$checked = 0
function Test-Stream([IO.Stream]$Stream, [string]$Label) {
    # 分块扫描 ASCII 与两种对齐的 UTF-16；留足重叠避免跨块漏检。
    $buffer = [byte[]]::new(1048576 + 512)
    $carry = 0
    while (($read = $Stream.Read($buffer, $carry, 1048576)) -gt 0) {
        $length = $carry + $read
        if ($pattern.IsMatch([Text.Encoding]::ASCII.GetString($buffer, 0, $length)) -or
            $pattern.IsMatch([Text.Encoding]::Unicode.GetString($buffer, 0, $length)) -or
            ($length -gt 1 -and $pattern.IsMatch([Text.Encoding]::Unicode.GetString($buffer, 1, $length - 1)))) {
            $violations.Add($Label); return
        }
        $carry = [Math]::Min(512, $length)
        [Array]::Copy($buffer, $length - $carry, $buffer, 0, $carry)
    }
}
function Test-DistributionName([string]$Name) {
    $leaf = [IO.Path]::GetFileName($Name)
    if ($leaf -like 'settings.json*' -or $leaf -like '.env*' -or $leaf -match '\.(pfx|p12|key|log)$') { $violations.Add('User/private file: ' + $Name) }
}
if ($Staged) {
    $names = & git -C $resolvedRoot -c core.quotepath=false diff --cached --name-only --diff-filter=ACMR
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate staged files.' }
    foreach ($name in $names) {
        Test-DistributionName $name
        # 扫描 Git index 中真正将被提交的字节，而不是可能已再次修改的工作区文件。
        $info = [Diagnostics.ProcessStartInfo]::new()
        $info.FileName = (Get-Command git -ErrorAction Stop).Source
        $info.UseShellExecute = $false; $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
        foreach ($argument in @('-C',$resolvedRoot,'cat-file','blob',(':'+$name))) { [void]$info.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($info)
        try {
            Test-Stream $process.StandardOutput.BaseStream ('index:'+$name)
            $process.StandardOutput.BaseStream.CopyTo([IO.Stream]::Null)
            $process.WaitForExit()
            if ($process.ExitCode -ne 0) { throw ('Cannot read staged blob: '+$name) }
            $checked++
        } finally { $process.Dispose() }
    }
    $files = @()
} else {
    $files = @(Get-ChildItem -LiteralPath $resolvedRoot -File -Recurse -Force | Where-Object { $_.FullName -notmatch '[\\/]\.git[\\/]' })
}
foreach ($file in $files) {
    if ($Staged -or $ForDistribution) { Test-DistributionName $file.FullName }
    if ($file.Extension -eq '.zip') {
        $zip = [IO.Compression.ZipFile]::OpenRead($file.FullName)
        try {
            foreach ($entry in $zip.Entries) {
                if ($entry.Length -eq 0) { continue }
                Test-DistributionName $entry.FullName
                $stream = $entry.Open()
                try { Test-Stream $stream ($file.Name + ':' + $entry.FullName); $checked++ }
                finally { $stream.Dispose() }
            }
        } finally { $zip.Dispose() }
    } else {
        $stream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try { Test-Stream $stream $file.FullName; $checked++ } finally { $stream.Dispose() }
    }
}
if ($violations.Count -gt 0) { $violations | ForEach-Object { Write-Output ('Sensitive content/file detected: ' + $_) }; throw 'Credential scan failed.' }
Write-Output ('PASS: credential scan, ' + $checked + ' files and archive entries; no plaintext API tokens or distributed user settings.')
