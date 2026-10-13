$ErrorActionPreference = 'Stop'
[xml]$props = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be major.minor.patch in Directory.Build.props.' }
$version
