; Inno Setup script for Planner
; ------------------------------------------------------------
; 1) Publish the app first (from the repo root):
;      cd Planner.App
;      dotnet publish -c Release
;    This produces Planner.App\bin\Release\net8.0-windows\win-x64\publish\Planner.App.exe
;
; 2) Install Inno Setup (free): https://jrsoftware.org/isdl.php
;
; 3) Compile this script, either:
;      - open installer\Planner.iss in the Inno Setup Compiler and click Build > Compile, or
;      - from a command line: ISCC.exe installer\Planner.iss
;
; Output: installer\Output\PlannerSetup-<version>.exe
;
; Note: ArchitecturesAllowed/ArchitecturesInstallIn64BitMode use "x64compatible",
; which needs Inno Setup 6.3+. On older Inno Setup versions, replace both with "x64".
; ------------------------------------------------------------

#define MyAppName "Planner"
#define MyAppPublisher "Planner"
#define MyAppExeName "Planner.App.exe"
#define MyAppPublishDir "..\Planner.App\bin\Release\net8.0-windows\win-x64\publish"
#define MyAppVersion GetVersionNumbersString(MyAppPublishDir + "\" + MyAppExeName)

[Setup]
; Keep this GUID stable across releases so Inno Setup recognizes upgrades correctly.
AppId={{59071895-E14D-42AC-ACBB-F30D7C391EF1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
; Lets the installer run either per-user (no admin/UAC needed) or per-machine;
; the user is asked, or you can force it with /CURRENTUSER or /ALLUSERS on the command line.
PrivilegesRequiredOverridesAllowed=commandline dialog
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=Output
OutputBaseFilename=PlannerSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\Planner.App\app.ico
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupicon"; Description: "Запускать Planner при входе в Windows"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyAppPublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

; Note: uninstall intentionally does NOT delete %LocalAppData%\Planner (your database
; and settings), so reinstalling later won't lose your data. To wipe it on uninstall too,
; add an [UninstallDelete] section for "{localappdata}\Planner" (type: filesandordirs).
