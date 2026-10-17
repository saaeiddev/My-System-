#define MyAppVersion "0.1.0"
[Setup]
AppId={{CF998497-5C79-46EC-BECF-E40D8A956EF3}
AppName=My System
AppVersion={#MyAppVersion}
AppPublisher=saaeiddev
AppPublisherURL=https://github.com/saaeiddev/My-System-
DefaultDirName={localappdata}\Programs\My System
DefaultGroupName=My System
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=..\artifacts
OutputBaseFilename=MySystem-Setup
SetupIconFile=..\assets\icons\MySystem.ico
UninstallDisplayIcon={app}\MySystem.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
CloseApplications=yes
[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
[Files]
Source: "..\artifacts\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
[Icons]
Name: "{group}\My System"; Filename: "{app}\MySystem.exe"
Name: "{autodesktop}\My System"; Filename: "{app}\MySystem.exe"; Tasks: desktopicon
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "MySystem"; Flags: dontcreatekey uninsdeletevalue
[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing Microsoft WebView2 Runtime (requires internet)…"; Check: NeedsWebView2; Flags: waituntilterminated
Filename: "{app}\MySystem.exe"; Description: "Launch My System"; Flags: nowait postinstall skipifsilent
[Code]
function NeedsWebView2: Boolean;
var Version: String;
begin
  Result := True;
  if RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
    if (Version <> '') and (Version <> '0.0.0.0') then Result := False;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
    if (Version <> '') and (Version <> '0.0.0.0') then Result := False;
end;
