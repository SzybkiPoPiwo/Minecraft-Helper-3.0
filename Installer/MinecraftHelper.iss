#define MyAppName "Minecraft Helper"
#define AutoClickerExeName "MinecraftHelper.AutoClicker.exe"
#define MinerExeName "MinecraftHelper.Miner.exe"

#ifndef AppVersion
  #define AppVersion "3.0.3"
#endif

#ifndef AutoClickerPublishDir
  #error "AutoClickerPublishDir define is required."
#endif

#ifndef MinerPublishDir
  #error "MinerPublishDir define is required."
#endif

#ifndef InstallerOutputDir
  #define InstallerOutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{D9C22E2B-2FB1-47F4-8C4B-5AA80A3A3B95}
AppName={#MyAppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}.0
AppPublisher=SzybkiPoPiwo
AppCopyright=Copyright © 2026 SzybkiPoPiwo — GNU GPL 3.0
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#InstallerOutputDir}
OutputBaseFilename=MinecraftHelper-Setup-{#AppVersion}
SetupIconFile=installer.ico
UninstallDisplayIcon={app}\MinecraftHelper.ico
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Wygląd glassmorphism 1:1 z makiety na kanwie. Inno nie rozmywa tła na żywo, więc szklane
; karty są wtopione w obrazy tła z wizard\ (układ w jednostkach obszaru okna 596×432, obrazy
; 2×), a skrypt niżej ustawia na nich kontrolki tak samo, jak na makiecie. Teksty rysuje Inno,
; więc są ostre przy każdej skali ekranu; ikony i przyciski są wyrenderowane w kilku skalach
; (100–300%) i wybierane pod skalę okna, żeby nic nie było rozciągane.
; excludelightcontrols: kontrolki rysuje sam Windows 11 (bez stylu Inno), więc da się ustawić
; kolor pola licencji; przezroczystość napisów i pól wyboru zostaje.
WizardStyle=modern windows11 hidebevels excludelightcontrols
WizardBackColor=#F3F2FC
WizardBackImageFile=wizard\back-inner.png
; Ten sam tekst co ..\LICENSE (GPL-3.0), tylko sformatowany do RTF: nagłówki i akapity
; bez ręcznego łamania linii, czytelne w oknie instalatora.
LicenseFile=license.rtf
; Wbudowane obrazki Inno skrypt ukrywa (zastępują je ikony we właściwej skali).
WizardImageFile=wizard\icon-side-100.png,wizard\icon-side-200.png
WizardSmallImageFile=wizard\icon-head-100.png,wizard\icon-head-200.png
DisableWelcomePage=no
ShowLanguageDialog=auto

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
polish.SetupWindowTitle=Instalator — %1 {#AppVersion}
polish.ClickNext=
english.ClickNext=
polish.ClickFinish=
english.SetupWindowTitle=Setup — %1 {#AppVersion}
english.ClickFinish=
polish.WelcomeLabel1=Witaj w Minecraft Helper {#AppVersion}
polish.WelcomeLabel2=Zainstalujesz dwa programy, razem albo osobno:%n%n•  Auto Clicker — klikanie i szybkie akcje do PVP%n•  Kopacz — automatyczne kopanie z Auto EQ i botem Discord%n%nTwoje ustawienia, bindy i konta z poprzednich wersji zostaną zachowane. Jeśli któryś program jest otwarty, instalator zaproponuje jego zamknięcie.
polish.LicenseLabel3=Minecraft Helper jest wolnym oprogramowaniem na licencji GNU GPL 3.0: możesz go używać, zmieniać i rozpowszechniać, a własna wersja musi mieć otwarty kod na tej samej licencji. Program jest dostarczany bez gwarancji. Pełny tekst licencji (po angielsku) znajduje się poniżej.
polish.FinishedHeadingLabel=Minecraft Helper jest gotowy
polish.FinishedLabel=Programy zostały zainstalowane i znajdziesz je w menu Start.
english.WelcomeLabel1=Welcome to Minecraft Helper {#AppVersion}
english.WelcomeLabel2=Setup will install two apps, together or separately:%n%n•  Auto Clicker — clicking and quick actions for PVP%n•  Kopacz — automatic mining with Auto EQ and a Discord bot%n%nYour settings, binds and accounts from previous versions are kept. If an app is running, Setup will offer to close it.

[CustomMessages]
polish.TutorialOption=Pokaż samouczek przy pierwszym uruchomieniu
polish.TutorialNote=Krótki samouczek w trybie ćwiczeń, osobno w każdym programie. Kliknięcia w nim niczego nie zmieniają.
english.TutorialOption=Show the tutorial on first launch
english.TutorialNote=A short practice-mode tutorial in each app. Clicks in it don't change anything.
polish.SideSubtitle=Auto Clicker · Kopacz
english.SideSubtitle=Auto Clicker · Kopacz
polish.CompClickerName=Auto Clicker
polish.CompClickerNote=Klikanie i szybkie akcje do PVP
polish.CompMinerName=Kopacz
polish.CompMinerNote=Automatyczne kopanie z Auto EQ i botem Discord
english.CompClickerName=Auto Clicker
english.CompClickerNote=Clicking and quick actions for PVP
english.CompMinerName=Kopacz
english.CompMinerNote=Automatic mining with Auto EQ and a Discord bot

; Typy zostają tylko na potrzeby Inno (domyślnie obie aplikacje, „Własny wybór” po zmianie
; zaznaczenia) — pole wyboru typu jest ukryte w skrypcie, wybiera się samymi komponentami.
[Types]
Name: "full"; Description: "Obie aplikacje (zalecane)"
Name: "autoclicker"; Description: "Tylko Auto Clicker"
Name: "miner"; Description: "Tylko Kopacz"
Name: "custom"; Description: "Własny wybór"; Flags: iscustom

[Components]
Name: "autoclicker"; Description: "Auto Clicker — klikanie i szybkie akcje do PVP"; Types: full autoclicker
Name: "miner"; Description: "Kopacz — automatyczne kopanie z Auto EQ i botem Discord"; Types: full miner

[Tasks]
Name: "desktopicons"; Description: "Utwórz skróty na pulpicie"; GroupDescription: "Dodatkowe ikony:"; Flags: unchecked

[Files]
Source: "installer.ico"; DestDir: "{app}"; DestName: "MinecraftHelper.ico"; Flags: ignoreversion
; Tła, ikony i przyciski wyglądu — tylko na czas instalatora, nie trafiają do {app}.
; Skrypt wypakowuje wyłącznie pliki potrzebne dla bieżącego języka i skali ekranu.
Source: "wizard\*.png"; Flags: dontcopy noencryption
Source: "{#AutoClickerPublishDir}\*"; DestDir: "{app}\AutoClicker"; Components: autoclicker; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MinerPublishDir}\*"; DestDir: "{app}\Kopacz"; Components: miner; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: files; Name: "{app}\MinecraftHelper.exe"
Type: files; Name: "{app}\MinecraftHelper.dll"
Type: files; Name: "{app}\MinecraftHelper.deps.json"
Type: files; Name: "{app}\MinecraftHelper.runtimeconfig.json"
Type: filesandordirs; Name: "{app}\AutoClicker"; Check: not WizardIsComponentSelected('autoclicker')
Type: filesandordirs; Name: "{app}\Kopacz"; Check: not WizardIsComponentSelected('miner')

[Icons]
Name: "{autoprograms}\{#MyAppName}\Auto Clicker"; Filename: "{app}\AutoClicker\{#AutoClickerExeName}"; Components: autoclicker
Name: "{autoprograms}\{#MyAppName}\Kopacz"; Filename: "{app}\Kopacz\{#MinerExeName}"; Components: miner
Name: "{autodesktop}\Minecraft Helper Auto Clicker"; Filename: "{app}\AutoClicker\{#AutoClickerExeName}"; Components: autoclicker; Tasks: desktopicons
Name: "{autodesktop}\Minecraft Helper Kopacz"; Filename: "{app}\Kopacz\{#MinerExeName}"; Components: miner; Tasks: desktopicons

[Run]
Filename: "{app}\AutoClicker\{#AutoClickerExeName}"; Description: "Uruchom Auto Clicker"; Components: autoclicker; Flags: nowait postinstall skipifsilent unchecked
Filename: "{app}\Kopacz\{#MinerExeName}"; Description: "Uruchom Kopacza"; Components: miner; Flags: nowait postinstall skipifsilent unchecked

[Code]
const
  LegacyUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{D9C22E2B-2FB1-47F4-8C4B-5AA80A3A3B95}_is1';

  { Układ makiety w jednostkach obszaru okna przy 100% — tak samo zmierzone są obrazy tła. }
  BaseWidth = 596;
  BaseHeight = 432;
  ButtonWidth = 86;
  ButtonHeight = 28;
  ButtonGap = 8;
  ButtonPad = 6;
  BM_CLICK = $00F5;
  EM_SETBKGNDCOLOR = $0443;
  { Kolor panelu licencji (COLORREF #E4E0F6) — taki sam jak ramka wtopiona w back-license.png. }
  LicensePanelColor = $00F6E0E4;
  DWMWA_BORDER_COLOR = 34;
  DWMWA_CAPTION_COLOR = 35;
  DWMWA_TEXT_COLOR = 36;

var
  WelcomeBack, InnerBack, ComponentsBack, ReadyBack, LicenseBack, InstallingBack: TBitmapImage;
  ProgressFill: TBitmapImage;
  ProgressFillMax: Integer;
  CompBoxes: array[0..1] of TNewCheckBox;
  CompSyncing: Boolean;
  ReadyText: TNewStaticText;
  ReadyInfo: String;
  FinishBacks: array[0..2] of TBitmapImage;
  ScaleTag: String;
  ScaleValue: Extended;
  TutorialCheckBox: TNewCheckBox;
  TutorialNote: TNewStaticText;
  HeaderIcon: TBitmapImage;
  SideLabels: array[0..5] of TNewStaticText;
  SideLabelCenters: array[0..5] of Integer;
  SideLabelCount: Integer;
  BackImg, NextImg, CancelImg: TBitmapImage;
  BackFile, NextFile, CancelFile: String;
  ButtonRowTop, ButtonRowRight: Integer;
  NativeButtonsHidden: Boolean;
  SyncTimerId: LongWord;

function DwmSetWindowAttribute(Wnd: HWND; Attribute: Cardinal; var Value: Cardinal; Size: Cardinal): Integer;
  external 'DwmSetWindowAttribute@dwmapi.dll stdcall delayload';
function SetTimer(Wnd: LongWord; IdEvent, Elapse: LongWord; TimerFunc: LongWord): LongWord;
  external 'SetTimer@user32.dll stdcall';
function KillTimer(Wnd: LongWord; IdEvent: LongWord): Boolean;
  external 'KillTimer@user32.dll stdcall';

{ --- Skala i położenie ------------------------------------------------------------------ }

{ Uwaga: w Pascal Script „/” na dwóch liczbach całkowitych dzieli całkowicie — stąd „* 1.0”
  przy każdym dzieleniu, które ma dać ułamek. }

function SX(Value: Integer): Integer;
begin
  Result := Round(Value * 1.0 * WizardForm.ClientWidth / BaseWidth);
end;

function SY(Value: Integer): Integer;
begin
  Result := Round(Value * 1.0 * WizardForm.ClientHeight / BaseHeight);
end;

{ Ikony i przyciski są wyrenderowane w tych skalach; bierzemy najbliższą skali okna. }
procedure PickScale();
var
  Factor: Extended;
begin
  Factor := WizardForm.ClientWidth * 1.0 / BaseWidth;
  if Factor < 1.125 then begin ScaleTag := '100'; ScaleValue := 1.0; end
  else if Factor < 1.375 then begin ScaleTag := '125'; ScaleValue := 1.25; end
  else if Factor < 1.625 then begin ScaleTag := '150'; ScaleValue := 1.5; end
  else if Factor < 1.875 then begin ScaleTag := '175'; ScaleValue := 1.75; end
  else if Factor < 2.125 then begin ScaleTag := '200'; ScaleValue := 2.0; end
  else if Factor < 2.375 then begin ScaleTag := '225'; ScaleValue := 2.25; end
  else if Factor < 2.75 then begin ScaleTag := '250'; ScaleValue := 2.5; end
  else begin ScaleTag := '300'; ScaleValue := 3.0; end;
end;

{ Rozmiar obrazka w pikselach: naturalny, gdy skala okna pokrywa się z wyrenderowaną
  (wtedy nic nie jest rozciągane), inaczej dopasowany. }
function ImageSize(BaseSize: Integer): Integer;
var
  Factor: Extended;
begin
  Factor := WizardForm.ClientWidth * 1.0 / BaseWidth;
  if Abs(Factor - ScaleValue) < 0.04 then
    Result := Round(BaseSize * ScaleValue)
  else
    Result := Round(BaseSize * Factor);
end;

{ Przesunięcie rodziców kontrolki względem okna (strony leżą w notatnikach Inno). }
procedure ParentOffset(Control: TControl; var OffsetX, OffsetY: Integer);
var
  Parent: TWinControl;
begin
  OffsetX := 0;
  OffsetY := 0;
  Parent := Control.Parent;
  while (Parent <> nil) and (Parent <> WizardForm) do
  begin
    OffsetX := OffsetX + Parent.Left;
    OffsetY := OffsetY + Parent.Top;
    Parent := Parent.Parent;
  end;
end;

{ Ustawia kontrolkę w jednostkach makiety liczonych od lewego górnego rogu okna. }
procedure PlaceAt(Control: TControl; X, Y, W, H: Integer);
var
  OffsetX, OffsetY: Integer;
begin
  ParentOffset(Control, OffsetX, OffsetY);
  Control.SetBounds(SX(X) - OffsetX, SY(Y) - OffsetY, SX(W), SY(H));
end;

{ Ustawia lewy górny róg bez zmiany rozmiaru. }
procedure MoveTo(Control: TControl; X, Y: Integer);
var
  OffsetX, OffsetY: Integer;
begin
  ParentOffset(Control, OffsetX, OffsetY);
  Control.Left := SX(X) - OffsetX;
  Control.Top := SY(Y) - OffsetY;
end;

function TempFile(const FileName: String): String;
begin
  Result := ExpandConstant('{tmp}\' + FileName);
  if not FileExists(Result) then
    ExtractTemporaryFile(FileName);
end;

function LoadImage(const FileName: String; Parent: TWinControl): TBitmapImage;
begin
  Result := TBitmapImage.Create(WizardForm);
  Result.Parent := Parent;
  Result.PngImage.LoadFromFile(TempFile(FileName));
end;

function LoadBack(const FileName: String): TBitmapImage;
begin
  Result := TBitmapImage.Create(WizardForm);
  Result.Visible := False;
  Result.PngImage.LoadFromFile(TempFile(FileName));
end;

{ --- Pasek tytułu ----------------------------------------------------------------------- }

{ Windows 11: pasek tytułu w kolorze górnej szyby makiety (na Windows 10 wywołanie po
  prostu nic nie zmienia). }
procedure TintTitleBar();
var
  Color: Cardinal;
begin
  try
    Color := $00FADDE4;
    DwmSetWindowAttribute(WizardForm.Handle, DWMWA_CAPTION_COLOR, Color, 4);
    Color := $002B1A16;
    DwmSetWindowAttribute(WizardForm.Handle, DWMWA_TEXT_COLOR, Color, 4);
    Color := $00F2C6CF;
    DwmSetWindowAttribute(WizardForm.Handle, DWMWA_BORDER_COLOR, Color, 4);
  except
  end;
end;

{ --- Karta z ikoną (powitanie i zakończenie) -------------------------------------------- }

function SideLabel(Page: TWinControl; const Text: String; Size: Integer; Bold: Boolean; Color: TColor; CenterY: Integer): TNewStaticText;
begin
  Result := TNewStaticText.Create(WizardForm);
  Result.Parent := Page;
  Result.AutoSize := True;
  Result.Font.Name := 'Segoe UI';
  Result.Font.Size := Size;
  Result.Font.Color := Color;
  if Bold then
    Result.Font.Style := [fsBold];
  Result.Caption := Text;
  SideLabels[SideLabelCount] := Result;
  SideLabelCenters[SideLabelCount] := CenterY;
  SideLabelCount := SideLabelCount + 1;
end;

{ Wyśrodkowanie w karcie z ikoną; wołane też po pokazaniu strony, bo dopiero wtedy etykiety
  na pewno znają swoją szerokość. }
procedure CenterSideLabels();
var
  I: Integer;
begin
  for I := 0 to SideLabelCount - 1 do
  begin
    MoveTo(SideLabels[I], 94, SideLabelCenters[I]);
    SideLabels[I].Left := SideLabels[I].Left - SideLabels[I].Width div 2;
    SideLabels[I].Top := SideLabels[I].Top - SideLabels[I].Height div 2;
  end;
end;

procedure BuildSideCard(Page: TWinControl);
var
  Icon, Pill: TBitmapImage;
  Size: Integer;
begin
  Icon := LoadImage('icon-side-' + ScaleTag + '.png', Page);
  Icon.Stretch := True;
  MoveTo(Icon, 42, 42);
  Size := ImageSize(104);
  Icon.Width := Size;
  Icon.Height := Size;

  SideLabel(Page, '{#MyAppName}', 11, True, $2B1A16, 166);
  SideLabel(Page, CustomMessage('SideSubtitle'), 9, False, $56403A, 188);

  { Kapsułka wersji jest obrazkiem (styl ignoruje kolor napisu ustawiony w skrypcie). }
  Pill := LoadImage('pill-' + ActiveLanguage + '-' + ScaleTag + '.png', Page);
  Pill.Stretch := True;
  MoveTo(Pill, 54, 337);
  Pill.Width := ImageSize(80);
  Pill.Height := ImageSize(27);
end;

{ --- Przyciski -------------------------------------------------------------------------- }

function ButtonKey(Button: TNewButton): String;
begin
  if Button.Caption = SetupMessage(msgButtonBack) then Result := 'back'
  else if Button.Caption = SetupMessage(msgButtonNext) then Result := 'next'
  else if Button.Caption = SetupMessage(msgButtonInstall) then Result := 'install'
  else if Button.Caption = SetupMessage(msgButtonFinish) then Result := 'finish'
  else if Button.Caption = SetupMessage(msgButtonCancel) then Result := 'cancel'
  else Result := '';
end;

procedure ClickNative(Button: TNewButton);
begin
  if Button.Visible and Button.Enabled then
    SendMessage(Button.Handle, BM_CLICK, 0, 0);
end;

procedure BackImgClick(Sender: TObject); begin ClickNative(WizardForm.BackButton); end;
procedure NextImgClick(Sender: TObject); begin ClickNative(WizardForm.NextButton); end;
procedure CancelImgClick(Sender: TObject); begin ClickNative(WizardForm.CancelButton); end;

function CreateButtonImage(Slot: Integer; OnClick: TNotifyEvent): TBitmapImage;
var
  Left: Integer;
begin
  Result := TBitmapImage.Create(WizardForm);
  Result.Parent := WizardForm;
  Result.Stretch := True;
  Result.Cursor := crHand;
  Result.OnClick := OnClick;
  Result.Visible := False;
  { Sloty od prawej: 0 = Anuluj, 1 = Dalej, 2 = Wstecz — jak na makiecie. }
  Left := ButtonRowRight - SX(ButtonWidth) * (Slot + 1) - SX(ButtonGap) * Slot;
  Result.SetBounds(
    Left - SX(ButtonPad),
    ButtonRowTop - SY(ButtonPad),
    ImageSize(ButtonWidth + ButtonPad * 2),
    ImageSize(ButtonHeight + ButtonPad * 2));
end;

{ Inno rysuje dodane obrazki na białym tle, dlatego przyciski i ikony mają w sobie wycinek tła
  strony (patrz renderer grafik) i muszą leżeć dokładnie w tych miejscach.
  Natywne przyciski zostają (Enter, Esc, logika Inno), tylko poza widokiem — klik w obrazek
  przekazujemy im przez BM_CLICK. Gdyby Inno pokazało przycisk o nieznanym napisie, wracamy
  do natywnych przycisków, żeby nic nie zniknęło. }
procedure ShowNativeButtons(Show: Boolean);
var
  Top: Integer;
begin
  if Show then
    Top := ButtonRowTop + (SY(ButtonHeight) - WizardForm.NextButton.Height) div 2
  else
    Top := WizardForm.ClientHeight + SY(60);
  WizardForm.BackButton.Top := Top;
  WizardForm.NextButton.Top := Top;
  WizardForm.CancelButton.Top := Top;
  NativeButtonsHidden := not Show;
end;

function SyncButton(Img: TBitmapImage; Button: TNewButton; var CurrentFile: String): Boolean;
var
  Key, FileName, State: String;
begin
  Result := True;
  if not Button.Visible then
  begin
    Img.Visible := False;
    Exit;
  end;
  Key := ButtonKey(Button);
  if Key = '' then
  begin
    Result := False;
    Exit;
  end;
  if Button.Enabled then State := 'on' else State := 'off';
  FileName := 'btn-' + ActiveLanguage + '-' + Key + '-' + State + '-' + ScaleTag + '.png';
  if FileName <> CurrentFile then
  begin
    Img.PngImage.LoadFromFile(TempFile(FileName));
    CurrentFile := FileName;
  end;
  if Button.Enabled then Img.Cursor := crHand else Img.Cursor := crDefault;
  Img.Visible := True;
end;

procedure SyncButtons();
var
  AllKnown: Boolean;
begin
  AllKnown := SyncButton(BackImg, WizardForm.BackButton, BackFile);
  AllKnown := SyncButton(NextImg, WizardForm.NextButton, NextFile) and AllKnown;
  AllKnown := SyncButton(CancelImg, WizardForm.CancelButton, CancelFile) and AllKnown;
  if AllKnown <> NativeButtonsHidden then
  begin
    ShowNativeButtons(not AllKnown);
    BackImg.Visible := AllKnown and WizardForm.BackButton.Visible;
    NextImg.Visible := AllKnown and WizardForm.NextButton.Visible;
    CancelImg.Visible := AllKnown and WizardForm.CancelButton.Visible;
  end;
end;

{ Inno zmienia stan przycisków w wielu miejscach (licencja, instalacja), więc obrazki
  dogrywamy co 100 ms. }
procedure SyncProgressFill();
var
  Gauge: TNewProgressBar;
begin
  Gauge := WizardForm.ProgressGauge;
  if (ProgressFill <> nil) and (Gauge.Max > Gauge.Min) then
    ProgressFill.Width := Round(ProgressFillMax * ((Gauge.Position - Gauge.Min) * 1.0) / (Gauge.Max - Gauge.Min));
end;

procedure SyncTimerProc(Arg1, Arg2, Arg3, Arg4: LongWord);
begin
  try
    SyncButtons();
    if WizardForm.CurPageID = wpInstalling then
      SyncProgressFill();
  except
  end;
end;

procedure BuildButtons();
begin
  { Stałe położenie z makiety — pod nie wyrenderowany jest wycinek tła w obrazkach przycisków. }
  ButtonRowRight := SX(584);
  ButtonRowTop := SY(394);
  CancelImg := CreateButtonImage(0, @CancelImgClick);
  NextImg := CreateButtonImage(1, @NextImgClick);
  BackImg := CreateButtonImage(2, @BackImgClick);
  NativeButtonsHidden := False;
  ShowNativeButtons(False);
  SyncButtons();
  SyncTimerId := SetTimer(0, 0, 100, CreateCallback(@SyncTimerProc));
end;

{ --- Układ stron ------------------------------------------------------------------------ }

procedure LayoutWelcomePage();
begin
  PlaceAt(WizardForm.WelcomeLabel1, 206, 30, 358, 30);
  PlaceAt(WizardForm.WelcomeLabel2, 206, 70, 358, 294);
end;

{ Nagłówek stron wewnętrznych leży w szklanej karcie: tytuł, opis, ikona po prawej i kreska
  (w obrazie tła) pod nim; treść strony pod kreską. }
procedure LayoutInnerPages();
var
  Size: Integer;
begin
  WizardForm.WizardSmallBitmapImage.Visible := False;
  WizardForm.MainPanel.Height := SY(80);
  PlaceAt(WizardForm.PageNameLabel, 30, 24, 480, 20);
  PlaceAt(WizardForm.PageDescriptionLabel, 30, 46, 480, 20);
  HeaderIcon := LoadImage('icon-head-' + ScaleTag + '.png', WizardForm.MainPanel);
  HeaderIcon.Stretch := True;
  MoveTo(HeaderIcon, 528, 20);
  Size := ImageSize(38);
  HeaderIcon.Width := Size;
  HeaderIcon.Height := Size;
  PlaceAt(WizardForm.InnerNotebook, 30, 86, 536, 284);
end;

{ Strona komponentów jak na makiecie: zamiast białej listy dwa wiersze w szklanej ramce
  (wtopionej w tło), każdy z polem, pogrubioną nazwą i opisem. Pole typu instalacji i natywna
  lista są ukryte; wybór przekazujemy do Inno przez WizardSelectComponents. }
{ Natywna kontrolka zostaje (Inno może ustawić na niej fokus), tylko poza widokiem strony. }
procedure MoveOutOfView(Control: TControl);
begin
  Control.Top := Control.Parent.Height + SY(200);
end;

procedure CompBoxClick(Sender: TObject);
begin
  if CompSyncing then
    Exit;
  if CompBoxes[0].Checked then WizardSelectComponents('autoclicker') else WizardSelectComponents('!autoclicker');
  if CompBoxes[1].Checked then WizardSelectComponents('miner') else WizardSelectComponents('!miner');
end;

function CompBox(const Caption: String; Y: Integer): TNewCheckBox;
begin
  Result := TNewCheckBox.Create(WizardForm);
  Result.Parent := WizardForm.SelectComponentsPage;
  Result.Caption := Caption;
  Result.Font.Style := [fsBold];
  Result.OnClick := @CompBoxClick;
  PlaceAt(Result, 46, Y, 500, 20);
end;

procedure CompNote(const Caption: String; Y: Integer);
var
  Note: TNewStaticText;
begin
  Note := TNewStaticText.Create(WizardForm);
  Note.Parent := WizardForm.SelectComponentsPage;
  Note.AutoSize := False;
  Note.WordWrap := True;
  Note.Font.Color := $56403A;
  Note.Caption := Caption;
  PlaceAt(Note, 67, Y, 480, 18);
end;

procedure BuildComponentsPage();
begin
  WizardForm.TypesCombo.Visible := False;
  MoveOutOfView(WizardForm.ComponentsList);
  PlaceAt(WizardForm.SelectComponentsLabel, 30, 86, 536, 36);
  CompBoxes[0] := CompBox(CustomMessage('CompClickerName'), 140);
  CompNote(CustomMessage('CompClickerNote'), 162);
  CompBoxes[1] := CompBox(CustomMessage('CompMinerName'), 201);
  CompNote(CustomMessage('CompMinerNote'), 223);
end;

procedure SyncComponentBoxes();
begin
  CompSyncing := True;
  try
    CompBoxes[0].Checked := WizardIsComponentSelected('autoclicker');
    CompBoxes[1].Checked := WizardIsComponentSelected('miner');
  finally
    CompSyncing := False;
  end;
end;

{ Podsumowanie przed instalacją: tekst w szklanej ramce zamiast białego pola, bez „Rodzaju
  instalacji” (pola typu nie ma na stronie). }
function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := '';
  if MemoComponentsInfo <> '' then
    Result := MemoComponentsInfo;
  if MemoTasksInfo <> '' then
  begin
    if Result <> '' then
      Result := Result + NewLine + NewLine;
    Result := Result + MemoTasksInfo;
  end;
  ReadyInfo := Result;
end;

{ Licencja: tekst GPL musi się przewijać, więc pole zostaje, ale bez ramki i w kolorze panelu
  wtopionego w tło — razem wyglądają jak jedna szklana ramka. }
procedure BuildLicensePage();
begin
  PlaceAt(WizardForm.LicenseLabel1, 30, 86, 536, 50);
  WizardForm.LicenseMemo.BorderStyle := bsNone;
  PlaceAt(WizardForm.LicenseMemo, 42, 148, 512, 164);
  PlaceAt(WizardForm.LicenseAcceptedRadio, 30, 330, 536, 18);
  PlaceAt(WizardForm.LicenseNotAcceptedRadio, 30, 350, 536, 18);
end;

procedure TintLicenseMemo();
begin
  WizardForm.LicenseMemo.Color := LicensePanelColor;
  SendMessage(WizardForm.LicenseMemo.Handle, EM_SETBKGNDCOLOR, 0, LicensePanelColor);
end;

{ Pasek postępu jak na makiecie: szklana ścieżka w tle (back-installing.png) i fioletowe
  wypełnienie z obrazka; natywny pasek zostaje poza widokiem. }
procedure BuildInstallingPage();
begin
  PlaceAt(WizardForm.StatusLabel, 30, 86, 536, 18);
  PlaceAt(WizardForm.FilenameLabel, 30, 106, 536, 18);
  ProgressFill := LoadImage('bar-fill.png', WizardForm.InstallingPage);
  ProgressFill.Stretch := True;
  PlaceAt(ProgressFill, 32, 136, 532, 6);
  ProgressFillMax := ProgressFill.Width;
  ProgressFill.Width := 0;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
begin
  if (ProgressFill <> nil) and (MaxProgress > 0) then
    ProgressFill.Width := Round(ProgressFillMax * (CurProgress * 1.0) / MaxProgress);
end;

procedure BuildReadyPage();
begin
  PlaceAt(WizardForm.ReadyLabel, 30, 86, 536, 36);
  ReadyText := TNewStaticText.Create(WizardForm);
  ReadyText.Parent := WizardForm.ReadyPage;
  ReadyText.AutoSize := False;
  ReadyText.WordWrap := True;
  PlaceAt(ReadyText, 46, 142, 504, 214);
end;

{ Pole samouczka na stronie zakończenia: zaznaczone domyślnie, z krótkim opisem pod spodem,
  w wyróżnionej ramce wtopionej w tło. }
procedure CreateTutorialOption();
begin
  TutorialCheckBox := TNewCheckBox.Create(WizardForm);
  TutorialCheckBox.Parent := WizardForm.FinishedPage;
  TutorialCheckBox.Caption := CustomMessage('TutorialOption');
  TutorialCheckBox.Font.Style := [fsBold];
  TutorialCheckBox.Checked := True;

  TutorialNote := TNewStaticText.Create(WizardForm);
  TutorialNote.Parent := WizardForm.FinishedPage;
  TutorialNote.AutoSize := False;
  TutorialNote.WordWrap := True;
  TutorialNote.Font.Color := $56403A;
  TutorialNote.Caption := CustomMessage('TutorialNote');
end;

procedure LayoutFinishedPage();
begin
  PlaceAt(WizardForm.FinishedHeadingLabel, 206, 30, 358, 34);
  PlaceAt(WizardForm.FinishedLabel, 206, 68, 358, 32);
  WizardForm.RunList.MinItemHeight := SY(36);
  PlaceAt(WizardForm.RunList, 212, 102, 348, 72);
  PlaceAt(TutorialCheckBox, 216, 310, 340, 20);
  PlaceAt(TutorialNote, 237, 332, 320, 30);
end;

function FinishBack(): TBitmapImage;
var
  Rows: Integer;
begin
  Rows := 0;
  if WizardForm.RunList.Visible then
    Rows := WizardForm.RunList.Items.Count;
  if Rows > 2 then
    Rows := 2;
  if FinishBacks[Rows] = nil then
    FinishBacks[Rows] := LoadBack('back-finish-' + IntToStr(Rows) + '.png');
  Result := FinishBacks[Rows];
end;

{ Wybór trafia do wspólnego pliku obu programów w AppData; programy odczytają go przy
  pierwszym uruchomieniu. }
procedure SaveTutorialChoice();
var
  Folder, Value: String;
begin
  Folder := ExpandConstant('{userappdata}\Minecraft Helper');
  if not ForceDirectories(Folder) then
    Exit;
  if TutorialCheckBox.Checked then
    Value := 'true'
  else
    Value := 'false';
  SaveStringToFile(
    AddBackslash(Folder) + 'tutorial.json',
    '{ "showOnFirstRun": ' + Value + ' }' + #13#10,
    False);
end;

procedure InitializeWizard();
begin
  PickScale();
  WelcomeBack := LoadBack('back-welcome.png');
  InnerBack := LoadBack('back-inner.png');
  TintTitleBar();
  WizardForm.WizardBitmapImage.Visible := False;
  WizardForm.WizardBitmapImage2.Visible := False;
  BuildSideCard(WizardForm.WelcomePage);
  BuildSideCard(WizardForm.FinishedPage);
  CenterSideLabels();
  LayoutWelcomePage();
  LayoutInnerPages();
  BuildComponentsPage();
  BuildReadyPage();
  BuildLicensePage();
  BuildInstallingPage();
  InstallingBack := LoadBack('back-installing.png');
  LicenseBack := LoadBack('back-license.png');
  ComponentsBack := LoadBack('back-components.png');
  ReadyBack := LoadBack('back-ready.png');
  CreateTutorialOption();
  BuildButtons();
end;

procedure DeinitializeSetup();
begin
  if SyncTimerId <> 0 then
    KillTimer(0, SyncTimerId);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpWelcome then
    WizardSetBackImage([WelcomeBack.PngImage], True, True, 255)
  else if CurPageID = wpFinished then
  begin
    LayoutFinishedPage();
    WizardSetBackImage([FinishBack().PngImage], True, True, 255);
  end
  else if CurPageID = wpSelectComponents then
  begin
    MoveOutOfView(WizardForm.ComponentsList);
    SyncComponentBoxes();
    WizardSetBackImage([ComponentsBack.PngImage], True, True, 255);
  end
  else if CurPageID = wpLicense then
  begin
    TintLicenseMemo();
    WizardSetBackImage([LicenseBack.PngImage], True, True, 255);
  end
  else if CurPageID = wpInstalling then
  begin
    MoveOutOfView(WizardForm.ProgressGauge);
    WizardSetBackImage([InstallingBack.PngImage], True, True, 255);
  end
  else if CurPageID = wpReady then
  begin
    MoveOutOfView(WizardForm.ReadyMemo);
    ReadyText.Caption := ReadyInfo;
    WizardSetBackImage([ReadyBack.PngImage], True, True, 255);
  end
  else
    WizardSetBackImage([InnerBack.PngImage], True, True, 255);

  if (CurPageID = wpWelcome) or (CurPageID = wpFinished) then
    CenterSideLabels();

  SyncButtons();
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssDone then
    SaveTutorialChoice();
end;

function FindLegacyInstallationAtRoot(
  RootKey: Integer;
  var LegacyExePath: String;
  var UninstallerPath: String): Boolean;
var
  InstallLocation: String;
  UninstallCommand: String;
begin
  Result := False;
  if not RegQueryStringValue(RootKey, LegacyUninstallKey, 'InstallLocation', InstallLocation) then
    Exit;

  LegacyExePath := AddBackslash(InstallLocation) + 'MinecraftHelper.exe';
  if not FileExists(LegacyExePath) then
    Exit;

  Result := True;
  UninstallerPath := '';
  if RegQueryStringValue(RootKey, LegacyUninstallKey, 'UninstallString', UninstallCommand) then
    UninstallerPath := RemoveQuotes(Trim(UninstallCommand));

  if (UninstallerPath = '') or (not FileExists(UninstallerPath)) then
    UninstallerPath := AddBackslash(InstallLocation) + 'unins000.exe';
end;

function FindLegacyInstallation(
  var LegacyExePath: String;
  var UninstallerPath: String): Boolean;
var
  DefaultInstallLocation: String;
begin
  Result := FindLegacyInstallationAtRoot(
    HKEY_CURRENT_USER,
    LegacyExePath,
    UninstallerPath);
  if Result then
    Exit;

  Result := FindLegacyInstallationAtRoot(
    HKEY_LOCAL_MACHINE,
    LegacyExePath,
    UninstallerPath);
  if Result then
    Exit;

  { Awaryjne wykrycie instalacji bez prawidłowego wpisu rejestru. }
  DefaultInstallLocation := ExpandConstant('{localappdata}\Programs\{#MyAppName}');
  LegacyExePath := AddBackslash(DefaultInstallLocation) + 'MinecraftHelper.exe';
  if FileExists(LegacyExePath) then
  begin
    UninstallerPath := AddBackslash(DefaultInstallLocation) + 'unins000.exe';
    Result := True;
  end;
end;

function InitializeSetup(): Boolean;
var
  LegacyExePath: String;
  UninstallerPath: String;
  ResultCode: Integer;
begin
  Result := True;
  if not FindLegacyInstallation(LegacyExePath, UninstallerPath) then
    Exit;

  if not WizardSilent then
  begin
    if MsgBox(
      'Wykryto starszą, połączoną wersję Minecraft Helper.' + #13#10 + #13#10 +
      'Nowe wydanie instaluje Auto Clicker i Kopacza jako dwa osobne programy. ' +
      'Przed kontynuowaniem stara wersja musi zostać odinstalowana. ' +
      'Ustawienia zapisane w AppData pozostaną zachowane.' + #13#10 + #13#10 +
      'Czy uruchomić deinstalację starej wersji teraz?',
      mbInformation,
      MB_YESNO or MB_DEFBUTTON2) <> IDYES then
    begin
      Result := False;
      Exit;
    end;
  end;

  if not FileExists(UninstallerPath) then
  begin
    MsgBox(
      'Nie znaleziono deinstalatora starej wersji.' + #13#10 +
      'Odinstaluj Minecraft Helper ręcznie w Ustawieniach Windows, a następnie uruchom ten instalator ponownie.',
      mbError,
      MB_OK);
    Result := False;
    Exit;
  end;

  if (not Exec(
        UninstallerPath,
        '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART',
        '',
        SW_SHOWNORMAL,
        ewWaitUntilTerminated,
        ResultCode)) or (ResultCode <> 0) then
  begin
    MsgBox(
      'Nie udało się odinstalować starej wersji Minecraft Helper.' + #13#10 +
      'Zamknij działający program, odinstaluj go ręcznie i uruchom instalator ponownie.',
      mbError,
      MB_OK);
    Result := False;
    Exit;
  end;

  if FileExists(LegacyExePath) then
  begin
    MsgBox(
      'Stara wersja nadal znajduje się na dysku.' + #13#10 +
      'Zamknij Minecraft Helper lub uruchom ponownie Windows, dokończ deinstalację i spróbuj jeszcze raz.',
      mbError,
      MB_OK);
    Result := False;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectComponents) and
     (not WizardIsComponentSelected('autoclicker')) and
     (not WizardIsComponentSelected('miner')) then
  begin
    MsgBox('Wybierz Auto Clicker, Kopacza albo obie aplikacje.', mbError, MB_OK);
    Result := False;
  end;
end;
