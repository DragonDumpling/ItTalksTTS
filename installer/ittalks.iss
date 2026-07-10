; Inno Setup 6 — install published ItTalksTTS binaries.
; Prerequisite: publish the solution, e.g.
;   dotnet publish ..\src\ItTalksTTS.App\ItTalksTTS.App.csproj -c Release -r win-x64 --self-contained true -o ..\publish\App
;   dotnet publish ..\src\ItTalksTTS.McpServer\ItTalksTTS.McpServer.csproj -c Release -r win-x64 --self-contained true -o ..\publish\Mcp

#define MyAppName "ItTalksTTS"
#define MyAppVersion "0.3.1"
#define MyAppPublisher "ItTalksTTS"
#define MyAppExeName "ItTalksTTS.exe"
#define PublishRoot "..\publish"

[Setup]
AppId={{A7B2F0C1-4E5D-4B2A-9C3E-1D2E3F4A5B6C}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=no
OutputDir=..\release
OutputBaseFilename=ItTalksTTS-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
CloseApplications=force
RestartApplications=yes
SetupIconFile=..\src\ItTalksTTS.App\Assets\ittalks.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[CustomMessages]
english.FirstRunNote=On first launch, ItTalksTTS will download Kokoro voice models automatically (internet required). This may take a few minutes.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Messages]
english.WelcomeLabel2=This will install [name/ver] on your computer.%n%n%english.FirstRunNote%

[Files]
Source: "{#PublishRoot}\App\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishRoot}\Mcp\ItTalksTTS.McpServer.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "/installCursorHooks"; StatusMsg: "Installing Cursor hooks..."; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
