; Inno Setup script. Build with scripts\build.ps1 (passes /DAppVersion, /DSourceDir and /DOutputDir).
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#define AppName "Ambient Sound Mixer"
#define AppExe "AmbientSoundMixer.exe"

[Setup]
; Keep AppId constant across versions so upgrades replace the old install in place.
AppId={{6B0E5C1A-3D2F-4B8E-9A47-5E1C7D9F2A10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Ambient Sound Mixer contributors
VersionInfoVersion={#AppVersion}
VersionInfoCompany=Ambient Sound Mixer contributors
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=AmbientSoundMixer-Setup-{#AppVersion}
SetupIconFile=..\src\AmbientSoundMixer.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
// Settings and presets live in %APPDATA%\AmbientSoundMixer and are never touched by installs or upgrades.
// On uninstall the user is asked, and the default answer keeps them.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Remove the optional "start with Windows" entry the app may have created.
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'AmbientSoundMixer');
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\AmbientSoundMixer');
    if DirExists(DataDir) and (not UninstallSilent) then
    begin
      if MsgBox('Also delete your settings and saved presets?' + #13#10 + #13#10 +
                DataDir + #13#10 + #13#10 +
                'Your own audio files are never deleted.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
