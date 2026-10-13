param([string]$Destination = (Join-Path $env:LOCALAPPDATA 'TranslateBuildTools\InnoSetup'))
$ErrorActionPreference = 'Stop'
# 固定官方编译器与下载哈希，升级工具时同时审查版本和哈希。
$uri = 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe'
$expectedHash = '0362A383ED217D4C4239B5933866DD96D3EB2102737DA92F80F6057A4B40DF2F'
$Destination = [IO.Path]::GetFullPath($Destination)
$compiler = Join-Path $Destination 'ISCC.exe'
if (Test-Path -LiteralPath $compiler) {
    $toolVersion = & $compiler --version
    if ($toolVersion -match '7[.]1[.]0') { return $compiler }
    throw 'Destination has another Inno version. Choose an empty tool directory.'
}
$download = Join-Path ([IO.Path]::GetTempPath()) ('Translate-Inno-' + [guid]::NewGuid().ToString('N') + '.exe')
try {
    Invoke-WebRequest -Uri $uri -OutFile $download
    if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Inno download checksum mismatch.' }
    if ((Get-AuthenticodeSignature -LiteralPath $download).Status -ne 'Valid') { throw 'Inno compiler installer signature is not valid.' }
    $process = Start-Process -FilePath $download -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', ('/DIR="' + $Destination + '"'), '/TASKS=') -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $compiler)) { throw 'Inno compiler setup failed.' }
    $compiler
} finally { if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download -Force } }
