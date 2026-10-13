param([string]$Repository = 'Drowndreamer/Translate')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = & (Join-Path $PSScriptRoot 'get-version.ps1')
$tag = 'v' + $version
if ($env:GITHUB_ACTIONS -eq 'true' -and $env:GITHUB_REF -ne ('refs/tags/' + $tag)) { throw 'Only the matching version tag can publish a release.' }
$tagCommit = & git -C $root rev-parse ($tag + '^{commit}')
if ($LASTEXITCODE -ne 0) { throw 'Version tag does not exist locally.' }
$head = & git -C $root rev-parse HEAD
if ($tagCommit -ne $head) { throw 'Version tag does not point at this checkout.' }
$assetsDir = Join-Path $root ('artifacts\releases\' + $version)
$assets = @((Join-Path $assetsDir ('Translate-Setup-' + $version + '.exe')), (Join-Path $assetsDir ('Translate-Portable-' + $version + '-win-x64.zip')), (Join-Path $assetsDir 'SHA256SUMS.txt'))
foreach ($asset in $assets) { if (!(Test-Path -LiteralPath $asset)) { throw ('Missing release asset: ' + $asset) } }
$checksumLines = @(Get-Content -LiteralPath $assets[2])
if ($checksumLines.Count -ne 2) { throw 'Checksum file must list exactly the installer and portable ZIP.' }
foreach ($asset in $assets[0..1]) {
    $expectedLine = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($asset)
    if ($checksumLines -cnotcontains $expectedLine) { throw ('Checksum mismatch: ' + [IO.Path]::GetFileName($asset)) }
}
& (Join-Path $PSScriptRoot 'verify-secrets.ps1') -Root $assetsDir -ForDistribution
# 任何已有 Release 都不覆盖；网络/权限错误由 create 再次明确报告。
& gh release view $tag --repo $Repository --json tagName 1>$null 2>$null
if ($LASTEXITCODE -eq 0) { throw ('Release already exists. Do not overwrite it; publish a new patch version or manually inspect an unfinished draft: ' + $tag) }
$changelog = Get-Content -LiteralPath (Join-Path $root 'CHANGELOG.md') -Raw
$section = [regex]::Match($changelog, '(?ms)^## \[' + [regex]::Escape($version) + '\].*?(?=^## |\z)').Value
if (!$section) { throw 'Current version is missing from CHANGELOG.md.' }
$notes = Join-Path $assetsDir 'release-notes.md'
$section += "

下载 Translate-Setup-$version.exe 安装，或完整解压便携 ZIP 后运行 Translate.exe。首次使用需要填写自己的 API Key。安装包未代码签名。选中文字会发送到用户配置的大模型服务。
"
[IO.File]::WriteAllText($notes, $section, [Text.UTF8Encoding]::new($false))
& gh release create $tag @assets --repo $Repository --verify-tag --draft --title ('Translate ' + $version) --notes-file $notes
if ($LASTEXITCODE -ne 0) { throw 'Release creation/upload failed. A draft may remain; inspect it before retrying.' }
& gh release edit $tag --repo $Repository --draft=false --latest
if ($LASTEXITCODE -ne 0) { throw 'Assets uploaded but publishing the draft failed.' }
