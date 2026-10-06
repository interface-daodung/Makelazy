; Makelazy installer — Inno Setup 6
; Đóng gói:
;   dotnet build Makelazy.slnx -c Release
;   iscc installer\Makelazy.iss
; Kết quả: dist\Makelazy-Setup-0.1.0.exe
;
; Cài per-user (HKCU, không cần admin). Tự đăng ký Open-With cho Makefile
; lúc cài, tự gỡ lúc uninstall (qua --register/--unregister --silent).

#define MyAppName "Makelazy"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "Makelazy"
#define MyAppExeName "Makelazy.exe"
#define DotNetUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"

[Setup]
AppId={{1D0557CF-B90C-4A01-A804-7382759A4581}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=Makelazy-Setup-{#MyAppVersion}
SetupIconFile=..\MakeLazy.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}

[Files]
Source: "..\src\Makelazy.App\bin\Release\net10.0-windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: desktopicon; Description: "Tạo icon ngoài Desktop"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register --silent"; Flags: runhidden; StatusMsg: "Đang đăng ký Open-With cho Makefile..."
Filename: "{app}\{#MyAppExeName}"; Description: "Mở Makelazy sau khi cài"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister --silent"; Flags: runhidden

[Code]
function HasDotNet10Desktop(): Boolean;
var
  TmpFile: String;
  Outp: AnsiString;
  ExitCode: Integer;
begin
  Result := False;
  TmpFile := ExpandConstant('{tmp}\makelazy_runtimes.txt');
  { dotnet.exe đi kèm .NET Desktop Runtime; vắng mặt = chưa cài }
  if Exec(ExpandConstant('{cmd}'), '/c dotnet --list-runtimes > "' + TmpFile + '" 2>&1',
         '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
  begin
    if LoadStringFromFile(TmpFile, Outp) then
      Result := Pos('Microsoft.WindowsDesktop.App 10.', Outp) > 0;
  end;
  DeleteFile(TmpFile);
end;

function InitializeSetup(): Boolean;
var
  Answer: Integer;
  Err: Integer;
begin
  Result := True;
  if HasDotNet10Desktop() then
    exit;
  Answer := MsgBox(
    'Chưa thấy .NET 10 Desktop Runtime trên máy.' + #13#10 +
    'Makelazy cần nó để chạy.' + #13#10 + #13#10 +
    'Bấm Yes để mở trang tải (Setup sẽ dừng lại).',
    mbConfirmation, MB_YESNO);
  if Answer = IDYES then
    ShellExec('', '{#DotNetUrl}', '', '', SW_SHOW, ewNoWait, Err);
  Result := False;
end;
