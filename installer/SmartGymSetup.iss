; Script de instalador para Smart Gym (smart-gym-dotnet)
; Basado en GymSetup.iss de SmartGymCenter (predecesor .NET 8), adaptado a la
; estructura actual: .NET 10, exe "Smart Gym.exe", publisher Cuber.
; Requiere Inno Setup 6.x o superior.
;
; Uso:
;   1. dotnet publish SmartGym.App -c Release -r win-x64 --self-contained true
;   2. Compilar este script (ISCC.exe installer\SmartGymSetup.iss)
; El instalador empaqueta la carpeta publish completa (self-contained: no
; requiere .NET en la máquina destino).

#define MyAppName "Smart Gym"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Cuber"
#define MyAppExeName "Smart Gym.exe"
; Ruta de publish relativa a la raíz del repo (este script vive en installer\)
#define PublishDir "..\SmartGym.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"

[Setup]
; AppId nuevo: producto distinto de SmartGymCenter (no hereda instalación vieja)
AppId={{2C917F36-9464-4484-B65D-7B6200F0EBC5}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}

; --- Directorio de instalación por defecto ---
; {autopf} detecta automáticamente Program Files (x86) o Program Files
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes

; --- Estética del instalador ---
; Ícono del setup: el mismo logos.ico embebido en el exe
SetupIconFile=..\SmartGym.App\Resources\AppIcon\logos.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

; --- Salida del instalador ---
OutputDir=output
OutputBaseFilename=Instalador_SmartGym_v{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern

; --- Arquitectura (Importante para MAUI x64) ---
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; El ejecutable y el resto de la carpeta publish (DLLs, wwwroot, runtime
; self-contained) — el asterisco incluye todo, recursivo.
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Crea acceso directo en el Menú Inicio
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
; Crea acceso directo en el Escritorio (si el usuario lo marcó)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Ejecutar al finalizar la instalación
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
