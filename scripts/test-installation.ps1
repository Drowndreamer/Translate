param([string]$IsccPath = $env:ISCC_PATH)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = & (Join-Path $PSScriptRoot 'get-version.ps1')
$assets = Join-Path $root ('artifacts\releases\' + $version)
$qa = Join-Path $root 'artifacts\qa-open-source'
$installDir = [IO.Path]::GetFullPath((Join-Path $qa ('installation-test-' + [guid]::NewGuid().ToString('N'))))
$allowed = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (!$installDir.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe installation test directory.' }
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A4D4B3CB-929F-44B1-B59E-5CF981751C22}_is1'
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Translate.lnk'
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Translate\Translate.lnk'
if ((Test-Path -LiteralPath $registry) -or (Test-Path -LiteralPath $installDir) -or (Test-Path -LiteralPath $desktop) -or (Test-Path -LiteralPath $startMenu)) { throw 'An installation/shortcut already exists. This test will not overwrite it.' }
if (Get-Process Translate -ErrorAction SilentlyContinue) { throw 'Exit Translate through its tray before installation testing. Existing running instances are not stopped by this script.' }
$settings = Join-Path $env:LOCALAPPDATA 'Translate\settings.json'
function Settings-Hash { if (Test-Path -LiteralPath $settings) { (Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash } else { '' } }
$before = Settings-Hash
New-Item -ItemType Directory -Path $qa -Force | Out-Null
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TranslateInstallNative {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
}
'@
$count = 0
function Check([bool]$Passed, [string]$Name) { if (!$Passed) { throw ('FAIL: ' + $Name) }; $script:count++; Write-Host ('PASS: ' + $Name) }
function Run-Setup([string]$File, [string[]]$Arguments) {
    $p = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    $limit = [DateTime]::UtcNow.AddSeconds(90)
    while (!$p.HasExited -and [DateTime]::UtcNow -lt $limit) { Start-Sleep -Milliseconds 200; $p.Refresh() }
    if (!$p.HasExited) { throw 'Setup timed out; inspect the test installer before cleanup.' }
    Check ($p.ExitCode -eq 0) ('installer/uninstaller exits successfully: ' + [IO.Path]::GetFileName($File))
}
function Launch-App {
    $p = Start-Process -FilePath (Join-Path $installDir 'Translate.exe') -WindowStyle Hidden -PassThru
    $limit = [DateTime]::UtcNow.AddSeconds(12)
    do { Start-Sleep -Milliseconds 100; $p.Refresh() } while (!$p.HasExited -and $p.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $limit)
    Check (!$p.HasExited -and $p.MainWindowHandle -ne 0) 'installed app starts and shows a window'
    return $p
}
$common = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CLOSEAPPLICATIONS', '/NORESTARTAPPLICATIONS', ('/DIR="' + $installDir + '"'))
try {
    Run-Setup (Join-Path $assets ('Translate-Setup-' + $version + '.exe')) ($common + @('/TASKS=', ('/LOG="' + (Join-Path $qa 'install.log') + '"')))
    Check ((Get-ItemProperty -LiteralPath $registry).InstallLocation.TrimEnd('\') -eq $installDir) 'per-user installation registered at isolated directory'
    Check (!(Test-Path -LiteralPath ('HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A4D4B3CB-929F-44B1-B59E-5CF981751C22}_is1'))) 'no machine-wide uninstall registration'
    Check (Test-Path -LiteralPath $startMenu) 'Start menu shortcut created'
    Check (!(Test-Path -LiteralPath $desktop)) 'desktop shortcut is optional and initially absent'
    $shell = New-Object -ComObject WScript.Shell
    Check ($shell.CreateShortcut($startMenu).TargetPath -eq (Join-Path $installDir 'Translate.exe')) 'Start menu shortcut targets installed executable'
    Check ((Test-Path -LiteralPath (Join-Path $installDir 'coreclr.dll')) -and (Test-Path -LiteralPath (Join-Path $installDir 'PresentationFramework.dll'))) 'installed payload includes .NET and WPF runtime'
    Check ((Settings-Hash) -eq $before) 'initial installation preserves user configuration'
    $app = Launch-App
    $hwnd = $app.MainWindowHandle
    [void][TranslateInstallNative]::SetForegroundWindow($hwnd)
    Start-Sleep -Milliseconds 500
    Add-Type -AssemblyName System.Drawing
    $previousDpi = [TranslateInstallNative]::SetThreadDpiAwarenessContext([IntPtr](-4))
    $bounds = [TranslateInstallNative+Rect]::new()
    [void][TranslateInstallNative]::GetWindowRect($hwnd, [ref]$bounds)
    $image = [Drawing.Bitmap]::new($bounds.Right-$bounds.Left-24, $bounds.Bottom-$bounds.Top-14)
    $graphics = [Drawing.Graphics]::FromImage($image)
    $graphics.CopyFromScreen($bounds.Left+12, $bounds.Top+2, 0, 0, $image.Size)
    $image.Save((Join-Path $qa 'installed-main.png'), [Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $image.Dispose(); [void][TranslateInstallNative]::SetThreadDpiAwarenessContext($previousDpi)
    [void][TranslateInstallNative]::PostMessage($hwnd, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 400; $app.Refresh()
    Check (!$app.HasExited -and ![TranslateInstallNative]::IsWindowVisible($hwnd)) 'closing main window leaves installed app in tray'
    $duplicate = Start-Process -FilePath (Join-Path $installDir 'Translate.exe') -WindowStyle Hidden -PassThru
    Check ($duplicate.WaitForExit(8000)) 'duplicate installed app exits'
    Start-Sleep -Milliseconds 300
    Check ([TranslateInstallNative]::IsWindowVisible($hwnd)) 'duplicate launch reopens original instance'
    # 只模拟下一个安装包版本，使用同一已验证的 payload，测试安装器覆盖升级逻辑。
    $next = [Version]$version
    $nextVersion = '{0}.{1}.{2}' -f $next.Major,$next.Minor,($next.Build+1)
    $compileLog = Join-Path $qa 'upgrade-compiler.txt'
    & $IsccPath '/Q' ('/DAppVersion=' + $nextVersion) ('/DPublishDir=' + (Join-Path $assets 'portable\Translate')) ('/DOutputDir=' + $qa) (Join-Path $root 'installer\Translate.iss') *> $compileLog
    if ($LASTEXITCODE -ne 0) { throw 'Test upgrade installer compilation failed.' }
    Run-Setup (Join-Path $qa ('Translate-Setup-' + $nextVersion + '.exe')) ($common + @('/TASKS=desktopicon', ('/LOG="' + (Join-Path $qa 'upgrade.log') + '"')))
    $app.Refresh()
    Check $app.HasExited 'upgrade closes running installed app through Restart Manager'
    Check ((Get-ItemProperty -LiteralPath $registry).DisplayVersion -eq $nextVersion) 'same AppId updates existing uninstall entry'
    Check ((Settings-Hash) -eq $before) 'upgrade preserves user configuration'
    Check (Test-Path -LiteralPath $desktop) 'optional desktop shortcut created when selected'
    Check ($shell.CreateShortcut($desktop).TargetPath -eq (Join-Path $installDir 'Translate.exe')) 'desktop shortcut targets installed executable'
    $updated = Launch-App
    $stopInstalled = Start-Process -FilePath (Join-Path $installDir 'Translate.exe') -ArgumentList '--shutdown' -WindowStyle Hidden -PassThru
    Check ($stopInstalled.WaitForExit(10000) -and $stopInstalled.ExitCode -eq 0 -and $updated.WaitForExit(1000)) 'shutdown command exits matching installed copy'
    $portableExe = Join-Path $assets 'portable\Translate\Translate.exe'
    $portable = Start-Process -FilePath $portableExe -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 700
    $wrongCopy = Start-Process -FilePath (Join-Path $installDir 'Translate.exe') -ArgumentList '--shutdown' -WindowStyle Hidden -PassThru
    Check ($wrongCopy.WaitForExit(10000) -and $wrongCopy.ExitCode -eq 1 -and !$portable.HasExited) 'installed shutdown cannot close a different portable directory'
    $stopPortable = Start-Process -FilePath $portableExe -ArgumentList '--shutdown' -WindowStyle Hidden -PassThru
    Check ($stopPortable.WaitForExit(10000) -and $stopPortable.ExitCode -eq 0 -and $portable.WaitForExit(1000)) 'portable copy also exits through its matching path'
    $updated = Launch-App
    Run-Setup (Join-Path $installDir 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $qa 'uninstall.log') + '"'))
    Check ($updated.WaitForExit(8000)) 'uninstall exits running installed copy'
    Check (!(Test-Path -LiteralPath (Join-Path $installDir 'Translate.exe'))) 'uninstall removes installed executable'
    Check (!(Test-Path -LiteralPath $registry)) 'uninstall removes application registration'
    Check (!(Test-Path -LiteralPath $startMenu) -and !(Test-Path -LiteralPath $desktop)) 'uninstall removes installed shortcuts'
    Check ((Settings-Hash) -eq $before) 'uninstall preserves user configuration'
    Write-Output ('Completed: ' + $count + ' installation checks.')
} finally {
    # 不自动删除失败时的注册表、快捷方式或仍在运行的程序；保留诊断供人工审查。
    if ((Settings-Hash) -ne $before) { Write-Error 'User configuration changed unexpectedly.' }
}
