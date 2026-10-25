; vr-pinball-launcher.iss -- Inno Setup script for the VR Pinball Launcher.
;
; Builds VRPinballLauncher-Setup.exe, which installs the launcher, creates
; Start Menu / Desktop shortcuts, registers an uninstaller, and optionally runs
; install.ps1 to set up VPinballX VR. Touch controls are a separate experimental add-on.
;
; Build it with build-installer.ps1 (repo root), or directly:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" ^
;       /DAppVersion=1.0.0 /DBuildDir=..\Build installer\vr-pinball-launcher.iss
;
; Defines (override with ISCC /D...):
;   AppVersion  -- version shown in the wizard / Add-Remove Programs
;   BuildDir    -- folder with the Unity build output (vr-launch.exe, *_Data, ...)

#ifndef AppVersion
  #define AppVersion "1.2.0"
#endif
#ifndef BuildDir
  #define BuildDir "..\Build"
#endif

#define AppName "VR Pinball Launcher"
#define AppExeName "vr-launch.exe"
#define AppPublisher "VR Pinball Launcher contributors (original: keithbphillips)"
#define AppUrl "https://github.com/dayelostraco/vr-pinball-launcher"

[Setup]
AppId={{8804A507-FC38-44F5-9E05-CF9BF2913F64}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
WizardStyle=modern
; Per-user installation keeps configuration and setup backups writable.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\VR Pinball Launcher
DefaultGroupName=VR Pinball Launcher
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=VRPinballLauncher-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
; Setup.exe's own icon. The installed app and the uninstall entry use the
; icon Unity embeds in vr-launch.exe, so this only covers the installer.
SetupIconFile=vr-pinball-launcher.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "reviewsetup"; Description: "Review an existing VPX installation and check setup"; GroupDescription: "Setup:"
Name: "setupvpxvr"; Description: "Legacy setup: download/configure the GL VR build"; GroupDescription: "Optional installations:"; Flags: unchecked
Name: "setupvpinmame"; Description: "Install/register legacy VPinMAME (needs admin to register)"; GroupDescription: "Optional installations:"; Flags: unchecked

[Files]
; Unity build output. The dev build's launcher-config.json holds machine-specific
; paths, so exclude it and ship a clean default instead (below).
Source: "{#BuildDir}\*"; DestDir: "{app}"; Excludes: "launcher-config.json,ControllerBridge\*,auto_oculus_touch.dll,vJoyInterface.dll"; Flags: recursesubdirs createallsubdirs ignoreversion
; Clean default config -- only on a fresh install, never overwrite a user's edits.
Source: "launcher-config.default.json"; DestDir: "{app}"; DestName: "launcher-config.json"; Flags: onlyifdoesntexist
; Existing user-installed bridges are preserved; fresh installs contain no bridge.
Source: "..\install.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\tools\setup.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "..\tools\SetupCore.psm1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "..\tools\SetupPaths.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "..\tools\SetupBackup.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "..\tools\SetupBackupPage.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "..\tools\VpxBackgroundInput.cs"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "..\tools\install-vpx-background-input.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "..\docs\setup.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\licenses\*"; DestDir: "{app}\licenses"; Flags: recursesubdirs createallsubdirs ignoreversion

Source: "..\tools\TrustedDownloads.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "..\tools\download-lock.json"; DestDir: "{app}\tools"; Flags: ignoreversion

[Icons]
Name: "{group}\VR Pinball Launcher"; Filename: "{app}\{#AppExeName}"
Name: "{group}\VR Pinball Launcher Setup"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File ""{app}\tools\setup.ps1"""; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall VR Pinball Launcher"; Filename: "{uninstallexe}"
Name: "{autodesktop}\VR Pinball Launcher"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File ""{app}\tools\setup.ps1"""; WorkingDir: "{app}"; Flags: waituntilterminated skipifsilent; Tasks: reviewsetup
; Optional post-install setup steps -- only run for ticked tasks. A console
; window is shown so the user can see the VPX download / AutoHotkey installer.
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install.ps1"" -Setup vpx -Yes"; \
  WorkingDir: "{app}"; StatusMsg: "Setting up VPinballX for VR..."; \
  Flags: waituntilterminated; Tasks: setupvpxvr
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install.ps1"" -Setup vpinmame -Yes"; \
  WorkingDir: "{app}"; StatusMsg: "Setting up VPinMAME (ROM emulator)..."; \
  Flags: waituntilterminated; Tasks: setupvpinmame
; Offer to launch when the wizard finishes.
Filename: "{app}\{#AppExeName}"; Description: "Launch VR Pinball Launcher now"; \
  Flags: nowait postinstall skipifsilent
