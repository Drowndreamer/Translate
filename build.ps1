param(
    [switch]$Package,
    [switch]$CI,
    [string]$DotnetPath = 'dotnet',
    [string]$IsccPath = $env:ISCC_PATH
)
$ErrorActionPreference = 'Stop'
$workspace = $PSScriptRoot
$project = Join-Path $workspace 'src\Translate\Translate.csproj'
$version = & (Join-Path $workspace 'scripts\get-version.ps1')
$dotnet = (Get-Command $DotnetPath -ErrorAction Stop).Source

function Invoke-Dotnet([string[]]$Arguments) {
    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw ('dotnet failed: ' + ($Arguments -join ' ')) }
}

Invoke-Dotnet @('build', $project, '-c', 'Release')
$testMode = if ($CI) { '--ci' } else { '--unit' }
Invoke-Dotnet @('run', '--project', (Join-Path $workspace 'tests\Translate.Tests\Translate.Tests.csproj'), '-c', 'Release', '--', $testMode)

& (Join-Path $workspace 'scripts\test-secrets.ps1')

if ($Package) {
    if (!$IsccPath) {
        $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($command) { $IsccPath = $command.Source }
        else {
            foreach ($key in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 7_is1', 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 7_is1', 'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 7_is1', 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1', 'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1')) {
                $location = (Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue).InstallLocation
                if ($location -and (Test-Path -LiteralPath (Join-Path $location 'ISCC.exe'))) { $IsccPath = Join-Path $location 'ISCC.exe'; break }
            }
        }
    }
    if (!$IsccPath -or !(Test-Path -LiteralPath $IsccPath)) { throw 'Inno Setup is required: $env:ISCC_PATH = & ./scripts/install-inno.ps1; then run ./build.ps1 -Package.' }
    # 独立版本目录，避免覆盖正在使用的便携版本。
    $releaseDir = [IO.Path]::GetFullPath((Join-Path $workspace ('artifacts\releases\' + $version)))
    $publishDir = Join-Path $releaseDir 'portable\Translate'
    if (Test-Path -LiteralPath $releaseDir) {
        $allowedRoot = [IO.Path]::GetFullPath((Join-Path $workspace 'artifacts\releases')) + [IO.Path]::DirectorySeparatorChar
        if (!$releaseDir.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package cleanup target.' }
        # 不清理锁定的安装目录；运行中的此构建会使删除报错并停止。
        Remove-Item -LiteralPath $releaseDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
    Invoke-Dotnet @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $publishDir, '-p:DebugType=None', '-p:DebugSymbols=false')
    foreach ($document in @('使用说明.md', 'LICENSE', 'CHANGELOG.md')) { Copy-Item -LiteralPath (Join-Path $workspace $document) -Destination $publishDir }
    & (Join-Path $workspace 'scripts\verify-secrets.ps1') -Root $publishDir -ForDistribution
    $zip = Join-Path $releaseDir ('Translate-Portable-' + $version + '-win-x64.zip')
    Compress-Archive -LiteralPath $publishDir -DestinationPath $zip -CompressionLevel Optimal
    & $IsccPath '/Q' ('/DAppVersion=' + $version) ('/DPublishDir=' + $publishDir) ('/DOutputDir=' + $releaseDir) (Join-Path $workspace 'installer\Translate.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $installer = Join-Path $releaseDir ('Translate-Setup-' + $version + '.exe')
    & (Join-Path $workspace 'scripts\verify-secrets.ps1') -Root $releaseDir -ForDistribution
    $checksums = foreach ($asset in @($installer, $zip)) {
        (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($asset)
    }
    [IO.File]::WriteAllLines((Join-Path $releaseDir 'SHA256SUMS.txt'), $checksums, [Text.UTF8Encoding]::new($false))
    Write-Output ('Release assets: ' + $releaseDir)
}
& (Join-Path $workspace 'scripts\verify-secrets.ps1') -Root $workspace
