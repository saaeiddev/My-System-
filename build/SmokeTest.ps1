$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
New-Item artifacts/smoke -ItemType Directory -Force | Out-Null
$destination=Join-Path $env:LOCALAPPDATA 'Programs/My System CI'
$installer=Start-Process (Resolve-Path artifacts/MySystem-Setup.exe) -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/DIR=`"$destination`"",'/LOG=artifacts/smoke/install.log') -Wait -PassThru
if ($installer.ExitCode -ne 0) { throw "Installer failed: $($installer.ExitCode)" }
$executable=Join-Path $destination 'MySystem.exe'
if (-not(Test-Path $executable)) { throw 'Installed executable missing' }
$app=Start-Process $executable -PassThru
Start-Sleep -Seconds 15
$app.Refresh()
if ($app.HasExited) { throw "Application exited during launch: $($app.ExitCode)" }
if ($app.MainWindowHandle -eq 0) { throw 'Application did not create a native window' }
$measurements=[ordered]@{timestamp=[DateTimeOffset]::UtcNow;processId=$app.Id;window=$app.MainWindowHandle.ToInt64();workingSetBytes=$app.WorkingSet64;cpuSeconds=$app.TotalProcessorTime.TotalSeconds;environment='GitHub Windows runner, no physical GPU qualification';durationSeconds=15}
$measurements|ConvertTo-Json|Set-Content artifacts/smoke/launch.json
$app.CloseMainWindow()|Out-Null
if (-not $app.WaitForExit(10000)) { $app.Kill(); throw 'Application did not shut down cleanly' }
$uninstall=Start-Process (Join-Path $destination 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru
if ($uninstall.ExitCode -ne 0) { throw "Uninstall failed: $($uninstall.ExitCode)" }
if (Test-Path $executable) { throw 'Application remains after uninstall' }
'Installation, native window launch, clean shutdown and uninstallation passed on the Windows runner.'|Set-Content artifacts/smoke/result.txt
