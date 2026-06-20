; ============================================================================
;  Playhub - Installer (Inno Setup 6)
;  Tema scuro, accent giallo #FFCB0F, logo Playhub, progress bar gialla,
;  opzioni di scorciatoia, avvio automatico a fine installazione,
;  disinstallazione pulita dal menu Start / "App installate".
;
;  COME COMPILARE:
;   1) Installa Inno Setup 6  ->  https://jrsoftware.org/isdl.php
;   2) Pubblica l'app:  Source\Playhub\publish.bat   (crea dist_publish\)
;   3) Apri questo file con Inno Setup e premi "Compile" (F9).
;      L'installer finito esce in  Installer\Output\Playhub-Setup-x.y.z.exe
;
;  Se cambi versione/percorsi modifica solo i #define qui sotto.
; ============================================================================

#define MyAppName        "Playhub"
#define MyAppVersion     "1.1.0"
#define MyAppPublisher   "Andrea Sgarro (ZazaMastro)"
#define MyAppURL         "https://github.com/LoZazaMastro/Playhub"
#define MyAppExeName     "Playhub.exe"

; Cartella prodotta da publish.bat (relativa a questo .iss).
; Se il tuo eseguibile sta altrove, cambia SOLO questa riga.
#define SourceDir        "..\Source\Playhub\dist_publish"
#define AppIcon          "..\Source\Playhub\Assets\Brand\Playhub.ico"

; Metti a 0 per disattivare il tema scuro (utile solo per debug).
#define DarkTheme        1

[Setup]
AppId={{8F1E7C42-9B3A-4D71-A0E2-AAAA01010101}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup

; Installazione per-utente: niente UAC, si disinstalla pulito dal menu Start.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
DefaultGroupName={#MyAppName}
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
AppMutex=Playhub.SingleInstance

; Solo x64.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Output.
OutputDir=Output
OutputBaseFilename=Playhub-Setup-{#MyAppVersion}
SetupIconFile={#AppIcon}
Compression=lzma2/ultra64
SolidCompression=yes

; Estetica wizard.
WizardStyle=modern
WizardSizePercent=110
ShowLanguageDialog=yes
UsePreviousLanguage=no
DisableWelcomePage=no
WizardImageFile=wizard-banner.bmp
WizardSmallImageFile=wizard-small.bmp
WizardImageStretch=yes

[Languages]
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
it.StartMenuShortcut=Crea una scorciatoia nel menu Start
en.StartMenuShortcut=Create a Start menu shortcut
it.StartupShortcut=Avvia Playhub all'avvio di Windows
en.StartupShortcut=Start Playhub when Windows starts
it.StartupOptions=Opzioni di avvio
en.StartupOptions=Startup options
it.UninstallProgram=Disinstalla %1
en.UninstallProgram=Uninstall %1
it.UninstallOptionsTitle=Opzioni di disinstallazione
en.UninstallOptionsTitle=Uninstall options
it.UninstallOptionsText=Scegli quali componenti rimuovere insieme a Playhub.
en.UninstallOptionsText=Choose which components to remove with Playhub.
it.RemoveUWPHook=Disinstalla anche UWPHook
en.RemoveUWPHook=Also uninstall UWPHook

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}";             GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "startmenu";   Description: "{cm:StartMenuShortcut}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupboot"; Description: "{cm:StartupShortcut}";  GroupDescription: "{cm:StartupOptions}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
; Menu Start (creato salvo l'utente deselezioni il task).
Name: "{group}\{#MyAppName}";              Filename: "{app}\{#MyAppExeName}"; Tasks: startmenu
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; Tasks: startmenu
; Desktop (opzionale).
Name: "{autodesktop}\{#MyAppName}";        Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
; Avvio con Windows (opzionale).
Name: "{userstartup}\{#MyAppName}";        Filename: "{app}\{#MyAppExeName}"; Tasks: startupboot

[Run]
; UWPHook viene installato senza finestre o richieste all'utente. Se è già
; presente, non viene reinstallato; il launcher integrato resta il fallback.
Filename: "{app}\UWPHook\UWPHook-Setup.exe"; Parameters: "/S"; Flags: runhidden waituntilterminated; Check: UWPHookNeedsInstall
; Avvio automatico a fine installazione (casella spuntata di default).
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

; ============================================================================
;  TEMA SCURO + ACCENT GIALLO + PROGRESS BAR GIALLA  (sezione [Code])
; ============================================================================
[Code]
const
  CLR_BG      = $00161414;   // #141416 sfondo (Windows usa il formato BGR)
  CLR_CARD    = $001B1D1F;   // superficie leggermente piu' chiara
  CLR_TEXT    = $00F2F2F2;   // testo chiaro
  CLR_SUBTEXT = $00B8B8B8;   // testo secondario
  CLR_ACCENT  = $000FCBFF;   // #FFCB0F in BGR

  PBM_SETBARCOLOR = $0409;
  PBM_SETBKCOLOR  = $2001;

var
  RemoveUWPHookWithPlayhub: Boolean;

function SendMessage(hWnd: Integer; Msg: LongWord; wParam: Longint; lParam: Longint): Longint;
  external 'SendMessageW@user32.dll stdcall';
function SetWindowTheme(hWnd: HWND; pszSubAppName: WideString; pszSubIdList: WideString): HRESULT;
  external 'SetWindowTheme@uxtheme.dll stdcall';

function UWPHookNeedsInstall: Boolean;
begin
  Result := not FileExists(ExpandConstant('{userappdata}\Briano\UWPHook\UWPHook.exe'));
end;

function InitializeUninstall: Boolean;
var
  OptionsForm: TSetupForm;
  DescriptionLabel: TNewStaticText;
  RemoveUWPHookCheck: TNewCheckBox;
  OkButton: TNewButton;
  CancelButton: TNewButton;
begin
  OptionsForm := CreateCustomForm(ScaleX(460), ScaleY(150), True, False);
  try
    OptionsForm.Caption := ExpandConstant('{cm:UninstallOptionsTitle}');
    OptionsForm.ClientWidth := ScaleX(460);
    OptionsForm.ClientHeight := ScaleY(150);
    OptionsForm.Position := poScreenCenter;

    DescriptionLabel := TNewStaticText.Create(OptionsForm);
    DescriptionLabel.Parent := OptionsForm;
    DescriptionLabel.Caption := ExpandConstant('{cm:UninstallOptionsText}');
    DescriptionLabel.Left := ScaleX(20);
    DescriptionLabel.Top := ScaleY(20);
    DescriptionLabel.Width := OptionsForm.ClientWidth - ScaleX(40);
    DescriptionLabel.AutoSize := False;

    RemoveUWPHookCheck := TNewCheckBox.Create(OptionsForm);
    RemoveUWPHookCheck.Parent := OptionsForm;
    RemoveUWPHookCheck.Caption := ExpandConstant('{cm:RemoveUWPHook}');
    RemoveUWPHookCheck.Left := ScaleX(20);
    RemoveUWPHookCheck.Top := ScaleY(58);
    RemoveUWPHookCheck.Width := OptionsForm.ClientWidth - ScaleX(40);
    RemoveUWPHookCheck.Checked := False;

    OkButton := TNewButton.Create(OptionsForm);
    OkButton.Parent := OptionsForm;
    OkButton.Caption := SetupMessage(msgButtonOK);
    OkButton.ModalResult := mrOk;
    OkButton.Default := True;
    OkButton.Left := OptionsForm.ClientWidth - ScaleX(190);
    OkButton.Top := ScaleY(104);
    OkButton.Width := ScaleX(80);

    CancelButton := TNewButton.Create(OptionsForm);
    CancelButton.Parent := OptionsForm;
    CancelButton.Caption := SetupMessage(msgButtonCancel);
    CancelButton.ModalResult := mrCancel;
    CancelButton.Cancel := True;
    CancelButton.Left := OptionsForm.ClientWidth - ScaleX(100);
    CancelButton.Top := ScaleY(104);
    CancelButton.Width := ScaleX(80);

    Result := OptionsForm.ShowModal = mrOk;
    RemoveUWPHookWithPlayhub := Result and RemoveUWPHookCheck.Checked;
  finally
    OptionsForm.Free;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  UWPHookUninstaller: String;
begin
  if (CurUninstallStep = usPostUninstall) and RemoveUWPHookWithPlayhub then
  begin
    UWPHookUninstaller := ExpandConstant('{userappdata}\Briano\UWPHook\uninstall.exe');
    if FileExists(UWPHookUninstaller) then
      Exec(UWPHookUninstaller, '/S', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure StyleProgressBar;
begin
  // Rimuove lo stile visivo nativo, altrimenti Windows forza la barra verde.
  SetWindowTheme(WizardForm.ProgressGauge.Handle, '', '');
  SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETBKCOLOR, 0, CLR_CARD);
  SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETBARCOLOR, 0, CLR_ACCENT);
end;

procedure DarkenControl(C: TControl); forward;

procedure DarkenChildren(Parent: TWinControl);
var
  i: Integer;
begin
  for i := 0 to Parent.ControlCount - 1 do
    DarkenControl(Parent.Controls[i]);
end;

procedure DarkenControl(C: TControl);
begin
  if C is TNewStaticText then
  begin
    TNewStaticText(C).Font.Color := CLR_TEXT;
    TNewStaticText(C).Color := CLR_BG;
  end
  else if C is TLabel then
    TLabel(C).Font.Color := CLR_TEXT
  else if C is TPanel then
  begin
    TPanel(C).Color := CLR_BG;
    TPanel(C).Font.Color := CLR_TEXT;
  end
  else if C is TNewNotebook then
    TNewNotebook(C).Color := CLR_BG
  else if C is TNewNotebookPage then
    TNewNotebookPage(C).Color := CLR_BG
  else if C is TNewEdit then
  begin
    TNewEdit(C).Color := CLR_CARD;
    TNewEdit(C).Font.Color := CLR_TEXT;
  end
  else if C is TNewMemo then
  begin
    TNewMemo(C).Color := CLR_CARD;
    TNewMemo(C).Font.Color := CLR_TEXT;
  end
  else if C is TRichEditViewer then
  begin
    TRichEditViewer(C).Color := CLR_CARD;
    TRichEditViewer(C).Font.Color := CLR_TEXT;
  end
  else if C is TNewListBox then
  begin
    TNewListBox(C).Color := CLR_CARD;
    TNewListBox(C).Font.Color := CLR_TEXT;
  end
  else if C is TFolderTreeView then
    TFolderTreeView(C).Color := CLR_CARD
  else if C is TNewCheckBox then
  begin
    TNewCheckBox(C).Color := CLR_BG;
    TNewCheckBox(C).Font.Color := CLR_TEXT;
  end
  else if C is TNewRadioButton then
  begin
    TNewRadioButton(C).Color := CLR_BG;
    TNewRadioButton(C).Font.Color := CLR_TEXT;
  end;

  if C is TWinControl then
    DarkenChildren(TWinControl(C));
end;

procedure ApplyDarkTheme;
begin
  WizardForm.Color := CLR_BG;
  WizardForm.MainPanel.Color := CLR_BG;
  WizardForm.InnerNotebook.Color := CLR_BG;
  WizardForm.OuterNotebook.Color := CLR_BG;
  WizardForm.Bevel.Visible := False;

  WizardForm.PageNameLabel.Font.Color := CLR_TEXT;
  WizardForm.PageDescriptionLabel.Font.Color := CLR_SUBTEXT;
  WizardForm.WelcomeLabel1.Font.Color := CLR_TEXT;
  WizardForm.WelcomeLabel2.Font.Color := CLR_SUBTEXT;

  DarkenChildren(WizardForm);
end;

procedure InitializeWizard;
begin
#if DarkTheme == 1
  ApplyDarkTheme;
#endif
end;

procedure CurPageChanged(CurPageID: Integer);
begin
#if DarkTheme == 1
  // Riapplica su ogni pagina: alcuni controlli nascono on-demand.
  DarkenChildren(WizardForm);
  if CurPageID = wpInstalling then
    StyleProgressBar;
#endif
end;

// ============================================================================
//  NOTE
//  - Installazione per-utente (PrivilegesRequired=lowest): nessun prompt UAC,
//    si disinstalla da Start > tasto destro > Disinstalla, o da "App installate".
//  - Per installare per TUTTI gli utenti (Program Files, con UAC) cambia
//    PrivilegesRequired in admin.
//  - La barra di avanzamento e' gialla: lo stile visivo nativo viene rimosso
//    perche' Windows altrimenti forza il verde.
//  - Inno non supporta in modo affidabile il vetro acrilico sui wizard, quindi
//    qui usiamo un tema scuro solido in continuita' con le superfici dell'app.
// ============================================================================
