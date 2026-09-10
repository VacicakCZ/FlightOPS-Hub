; FlightOps Hub installer (Inno Setup 6) - per-user, no admin required.
; Packages the framework-dependent `dotnet publish` output from ..\publish\
; (see the .NET rewrite plan/memory for the publish command; the GitHub
; Actions release workflow runs it the same way). Framework-dependent, not
; self-contained - keeps the installer around 5-10MB instead of ~55MB by
; not bundling the whole .NET/WPF runtime; this installer instead detects
; and, if missing, downloads+installs the .NET Desktop Runtime itself (see
; the [Code] section below) - a deliberate size-vs-dependency tradeoff the
; app's own repo owner chose explicitly.
;
; Local test build:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" flightops_hub.iss
; CI passes the real version: ISCC.exe /DMyAppVersion=3.0.0 flightops_hub.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-local"
#endif

#define MyAppName "FlightOps Hub"
#define MyAppPublisher "FlightOps Hub"
#define MyAppURL "https://github.com/VacicakCZ/FlightOPS-Hub"
#define MyAppExeName "FlightOpsHub.exe"

[Setup]
; Fixed GUID - do not regenerate; Inno uses this to recognize upgrades of
; the same product across versions.
AppId={{F4E5FE9C-ACC0-4117-86AC-3B88195093DF}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
; {autopf} resolves to Program Files under admin installs and to
; %LocalAppData%\Programs under PrivilegesRequired=lowest - this is the
; documented Inno pattern for a true no-admin per-user install (see the
; .NET rewrite plan's approved installer decision).
DefaultDirName={autopf}\FlightOpsHub
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=FlightOpsHub-Setup-{#MyAppVersion}
SetupIconFile=..\..\OIG3.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
; Matches the app's own 5 UI languages. Czech/German/Spanish ship with
; Inno Setup itself ("compiler:Languages\..."); Chinese Simplified doesn't
; (dropped from jrsoftware/issrc's own Unofficial folder at some point) -
; ChineseSimplified.isl here is vendored from the actively-maintained
; https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation,
; targeting Inno Setup 6.5.0+ (this project builds with 6.7.3). Inno only
; shows a language-picker page at all when more than one is listed here.
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "czech"; MessagesFile: "compiler:Languages\Czech.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The framework-dependent publish output - FlightOpsHub's own managed
; assemblies plus the WebView2 loader and the bundled frontend/data/
; OIG3.ico (see FlightOpsHub.App.csproj's CopyFrontendFilesPublish
; target) - NOT the .NET/WPF runtime itself, downloaded separately below
; if missing.
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[UninstallDelete]
; "Launch at Windows startup" (Settings tab) creates this shortcut itself
; at runtime (StartupShortcut.cs), not through this installer's own
; [Icons] tracking - clean it up here too, or it would survive an
; uninstall as a broken shortcut pointing at a now-removed exe.
Type: files; Name: "{userstartup}\FlightOps Hub.lnk"

[Run]
; Interactive installs (the normal wizard) offer the usual "Launch"
; checkbox. The in-app self-update flow (UpdateService.InstallAndExit)
; always runs this installer with /VERYSILENT - for that path the second
; entry below launches the app unconditionally, since there's no wizard
; page left to offer a checkbox on.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Flags: nowait skipifnotsilent

[Code]
var
  DotNetDownloadPage: TDownloadWizardPage;
  NeedsDotNetRuntime: Boolean;
  DotNetRuntimeDownloaded: Boolean;

const
  DotNetRuntimeUrl = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';
  DotNetRuntimeFileName = 'windowsdesktop-runtime-win-x64.exe';

// Best-effort heads-up if the Evergreen WebView2 Runtime looks absent -
// almost every Windows 10/11 machine already has it via Edge, so this is
// a warning, not a blocker (bundling/chaining the ~2MB bootstrapper is a
// reasonable future addition if this ever turns out to bite real users).
function IsWebView2RuntimeInstalled(): Boolean;
var
  Version: String;
begin
  Result :=
    RegQueryStringValue(HKLM64, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) or
    RegQueryStringValue(HKCU64, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version);
end;

// The framework-dependent publish output needs the .NET 10 Desktop
// Runtime (WPF + WinForms) present on the machine - unlike WebView2, this
// does NOT ship with Windows by default, so unlike the WebView2 check
// above this one actually offers to fetch and install it, not just warn.
// Detected by looking for any 10.x shared-framework folder, the same
// thing `dotnet --list-runtimes` reads, without depending on dotnet.exe
// being on PATH.
function IsDotNetDesktopRuntimeInstalled(): Boolean;
var
  FindRec: TFindRec;
  BasePath: String;
begin
  Result := False;
  BasePath := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if DirExists(BasePath) and FindFirst(BasePath + '\10.*', FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

procedure InitializeWizard;
begin
  NeedsDotNetRuntime := not IsDotNetDesktopRuntimeInstalled();
  DotNetDownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and NeedsDotNetRuntime then
  begin
    DotNetDownloadPage.Clear;
    DotNetDownloadPage.Add(DotNetRuntimeUrl, DotNetRuntimeFileName, '');
    DotNetDownloadPage.Show;
    try
      try
        DotNetDownloadPage.Download;
        DotNetRuntimeDownloaded := True;
      except
        if not DotNetDownloadPage.AbortedByUser then
          SuppressibleMsgBox('Could not download the .NET Desktop Runtime (no internet connection?). FlightOps Hub won''t start without it - install it manually afterwards from https://dotnet.microsoft.com/download/dotnet/10.0', mbError, MB_OK, IDOK);
      end;
    finally
      DotNetDownloadPage.Hide;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep = ssPostInstall) and DotNetRuntimeDownloaded then
  begin
    // /passive - a progress UI, no prompts of its own beyond Windows' own
    // UAC consent (the runtime installer needs admin rights even though
    // this app's own install doesn't - a real, if usually minor, gap in
    // the "no admin required" promise for a machine that has neither the
    // runtime nor an admin-capable account; see the .NET rewrite memory).
    Exec(ExpandConstant('{tmp}\' + DotNetRuntimeFileName), '/install /passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
  end;

  if (CurStep = ssPostInstall) and (not IsWebView2RuntimeInstalled()) then
  begin
    MsgBox('FlightOps Hub uses the Microsoft Edge WebView2 Runtime, which this installer couldn''t detect on this PC. It ships with Windows 10/11 and Edge on almost all systems, but if FlightOps Hub fails to start, install it from https://developer.microsoft.com/microsoft-edge/webview2/', mbInformation, MB_OK);
  end;
end;
