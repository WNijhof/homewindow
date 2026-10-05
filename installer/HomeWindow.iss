; Inno Setup script for HomeWindow. Build it with scripts\build-installer.ps1, which publishes the
; app first and passes these defines:
;   AppVersion     version number, e.g. 0.1.0
;   SourceDir      folder with the published app
;   SelfContained  1 when the app carries its own .NET runtime, 0 when the PC needs .NET 10

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif
#ifndef SelfContained
  #define SelfContained "0"
#endif

#define AppName "HomeWindow"
#define AppExe "HomeWindow.exe"

[Setup]
; Keep this id: Windows uses it to recognise an update of an earlier installation
AppId={{6B3F2A71-0C4E-4E7B-9A55-2D8C1F4B7E19}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=WNijhof
AppPublisherURL=https://github.com/WNijhof/homewindow
AppSupportURL=https://github.com/WNijhof/homewindow/blob/main/docs/HANDLEIDING.md
AppCopyright=Copyright 2026 WNijhof
; Version information on the setup itself; a setup without it looks suspicious to virus scanners
VersionInfoVersion={#AppVersion}
VersionInfoCompany=WNijhof
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoProductTextVersion={#AppVersion}
; Installs for the current user, without administrator rights; the user may choose all users instead
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=Output
OutputBaseFilename=HomeWindow-Setup-{#AppVersion}
SetupIconFile=..\HomeWindow\Assets\HomeWindow.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=auto

[Languages]
; English first: Inno Setup falls back to the first language when Windows is in neither (German, French, ...)
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "nl"; MessagesFile: "compiler:Languages\Dutch.isl"

[CustomMessages]
nl.AutoStart=HomeWindow starten wanneer ik me aanmeld bij Windows
en.AutoStart=Start HomeWindow when I sign in to Windows
nl.Launch=HomeWindow nu starten
en.Launch=Start HomeWindow now
nl.Manual=Handleiding
en.Manual=Manual
nl.NeedsDotNet=HomeWindow heeft de .NET 10 Desktop Runtime nodig, en die staat niet op deze pc.%n%nWil je de downloadpagina openen? Installeer daar de ".NET Desktop Runtime 10" voor x64 en start deze installatie daarna opnieuw.
en.NeedsDotNet=HomeWindow needs the .NET 10 Desktop Runtime, which is not on this PC.%n%nOpen the download page? Install the ".NET Desktop Runtime 10" for x64 there and run this setup again.
nl.RemoveSettings=Ook je HomeWindow-instellingen verwijderen (Homeys, API-keys, favorieten)?
en.RemoveSettings=Also remove your HomeWindow settings (Homeys, API keys, favourites)?

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[UninstallDelete]
; Setups that HomeWindow downloaded to update itself
Type: filesandordirs; Name: "{localappdata}\{#AppName}\Updates"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; The same value HomeWindow sets itself under Settings → Start with Windows
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "HomeWindow"; ValueData: """{app}\{#AppExe}"" --tray"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent
; After an automatic update (HomeWindow runs the setup silently with /RESTART=1)
Filename: "{app}\{#AppExe}"; Parameters: "--tray"; Flags: nowait; Check: ShouldRestart

[Code]
const
  EVENT_MODIFY_STATE = $0002;
  // The names HomeWindow (App.xaml.cs, InstanceName) and the demo give their mutex and quit signal
  Instance = '{#AppName}-7d1c5e2a';
  Running = '{#AppName}-7d1c5e2a,{#AppName}-7d1c5e2a-demo';

function OpenEvent(Access: Cardinal; Inherit: Boolean; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Event: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

procedure SignalQuit(Name: String);
var
  Event: THandle;
begin
  Event := OpenEvent(EVENT_MODIFY_STATE, False, Name + '-quit');
  if Event <> 0 then
  begin
    SetEvent(Event);
    CloseHandle(Event);
  end;
end;

// HomeWindow keeps running in the tray; stop it so its files can be replaced or removed.
// It is asked to close itself first; only one that does not respond is ended with taskkill.
procedure StopHomeWindow();
var
  Code, Waited: Integer;
begin
  SignalQuit(Instance);
  SignalQuit(Instance + '-demo');
  Waited := 0;
  while CheckForMutexes(Running) and (Waited < 5000) do
  begin
    Sleep(100);
    Waited := Waited + 100;
  end;
  // Also versions before 0.4.1, which have no quit signal
  if CheckForMutexes(Running) then
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, Code);
  // Windows releases the files a moment after the process has ended
  Sleep(500);
end;

function ShouldRestart(): Boolean;
begin
  Result := WizardSilent() and (ExpandConstant('{param:restart|0}') = '1');
end;

function HasDesktopRuntime(): Boolean;
var
  Search: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), Search) then
  begin
    Result := True;
    FindClose(Search);
  end;
end;

function InitializeSetup(): Boolean;
var
  Code: Integer;
begin
  Result := True;
  if ('{#SelfContained}' = '0') and not HasDesktopRuntime() then
  begin
    if MsgBox(CustomMessage('NeedsDotNet'), mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, Code);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopHomeWindow();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopHomeWindow();
    // Also when start-up was switched on in HomeWindow itself rather than in this setup
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'HomeWindow');
  end;
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent() then
  begin
    if MsgBox(CustomMessage('RemoveSettings'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\HomeWindow'), True, True, True);
  end;
end;
