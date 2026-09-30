; Inno Setup script — builds LiveLinguist-fr-Setup.exe (app + native libs + model).
; Per-user install (no admin prompt), Start-menu + desktop shortcut.

[Setup]
AppName=Live Linguist (Français)
AppVersion=0.8
AppPublisher=Live Linguist
DefaultDirName={autopf}\LiveLinguist
DefaultGroupName=Live Linguist
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=dist
OutputBaseFilename=LiveLinguist-fr-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Files]
; The self-contained app + native llama DLLs (assembled by CI into bundle\LiveLinguist)
Source: "..\bundle\LiveLinguist\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
; The model, placed next to the exe so the app finds it automatically: the fine-tuned
; Qwen3-0.6B (about 3x faster than the 1.7B it replaces on a laptop CPU)
Source: "..\model\ll-fr-0.6b-v2-Q4_K_M.gguf"; DestDir: "{app}"; Flags: ignoreversion
; Whisper STT model for the "Réunion / vidéo" (loopback) audio source
Source: "..\model\ggml-small-q5_1.bin"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; Upgrading from v0.7 or earlier: the 1.7B is no longer used, so free its 1.1 GB.
Type: files; Name: "{app}\qwen3-1.7b-easylang-fr-Q4_K_M.gguf"

[Icons]
Name: "{group}\Live Linguist"; Filename: "{app}\LiveLinguistWinUI.exe"
Name: "{autodesktop}\Live Linguist"; Filename: "{app}\LiveLinguistWinUI.exe"

[Run]
Filename: "{app}\LiveLinguistWinUI.exe"; Description: "Launch Live Linguist now"; Flags: nowait postinstall skipifsilent
