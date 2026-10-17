param([string]$Configuration='Release',[switch]$SkipInstaller)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Check-Exit { if ($LASTEXITCODE -ne 0) { throw "Command exited with $LASTEXITCODE" } }
Push-Location src/MySystem.Rendering
npm ci
Check-Exit
npm test
Check-Exit
npm run build
Check-Exit
Pop-Location
dotnet test tests/MySystem.Tests/MySystem.Tests.csproj -c $Configuration --logger 'trx;LogFileName=unit-tests.trx' --results-directory artifacts/tests
Check-Exit
dotnet publish src/MySystem.App/MySystem.App.csproj -c $Configuration -r win-x64 -p:Platform=x64 --self-contained true -o artifacts/app
Check-Exit
New-Item artifacts/app/Tools -ItemType Directory -Force | Out-Null
$present='artifacts/app/Tools/PresentMon.exe'
Invoke-WebRequest 'https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe' -OutFile $present
if ((Get-FileHash $present -Algorithm SHA256).Hash -ne 'B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF') { throw 'PresentMon SHA256 mismatch' }
Copy-Item README.md artifacts/app/
Copy-Item documentation artifacts/app/ -Recurse -Force
Compress-Archive -Path artifacts/app/* -DestinationPath artifacts/MySystem-win-x64.zip -Force
if (-not $SkipInstaller) {
    Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile artifacts/MicrosoftEdgeWebview2Setup.exe
    $signature=Get-AuthenticodeSignature artifacts/MicrosoftEdgeWebview2Setup.exe
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') { throw 'WebView2 bootstrapper signature verification failed' }
    $iscc="${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $iscc)) { throw 'Install Inno Setup 6 before building the installer.' }
    & $iscc installer/MySystem.iss
    Check-Exit
}
Get-ChildItem artifacts/*.exe,artifacts/*.zip | ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)" } | Set-Content artifacts/SHA256SUMS.txt
