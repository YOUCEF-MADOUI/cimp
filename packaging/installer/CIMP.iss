; ===========================================================================
; CIMP — Coût d'Importation Algérie — script d'installation Inno Setup
; ===========================================================================
; Produit l'installateur : CIMP-1.0.0-Setup-x64.exe
;
; PRÉREQUIS (à faire une seule fois, sur une machine Windows) :
;   1. Installer Inno Setup (gratuit) : https://jrsoftware.org/isinfo.php
;   2. Avoir déjà généré le dossier publié self-contained win-x64, par exemple via :
;        powershell -ExecutionPolicy Bypass -File scripts\publish-release.ps1
;      (ce script appelle automatiquement ISCC.exe sur CE fichier si Inno Setup est détecté).
;
; Utilisation manuelle (sans passer par publish-release.ps1) :
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" packaging\installer\CIMP.iss ^
;       /DMyAppVersion=1.0.0 ^
;       /DMySourceDir="D:\code\CIMP\Release\CIMP-1.0.0-win-x64" ^
;       /DMyOutputDir="D:\code\CIMP\Release"
;
; IMPORTANT : cet installateur ne contient AUCUN système de licence, d'abonnement ou d'activation
; (conforme à la demande explicite "préparation V1 publiable SANS abonnement"). Il se contente
; d'installer les fichiers déjà publiés par dotnet publish, de créer des raccourcis, et de permettre
; la désinstallation standard Windows.
; ===========================================================================

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef MySourceDir
  #define MySourceDir "..\..\..\CIMP-Release\CIMP-1.0.0-Windows-x64"
#endif
#ifndef MyOutputDir
  #define MyOutputDir "..\..\..\CIMP-Release"
#endif

#define MyAppName "CIMP"
#define MyAppFullName "CIMP — Coût d'Importation Algérie"
#define MyAppExeName "CIMP.exe"
#define MyAppPublisher "CIMP"

[Setup]
AppId={{8C5E6B8B-2B61-4C7E-9B0C-3E6E6E6F0C01}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppFullName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#MyOutputDir}
OutputBaseFilename=CIMP-{#MyAppVersion}-Setup-x64
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern
; Pas de signature de code à ce stade (V1 d'essai) — Windows SmartScreen peut afficher un avertissement
; la première fois ; voir packaging/README.txt section "Dépannage".
PrivilegesRequired=admin

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis supplémentaires :"; Flags: unchecked

[Files]
; Copie intégrale du dossier déjà publié par "dotnet publish" (CIMP.exe + toutes ses dépendances
; self-contained) — rien n'est reconstruit ici, Inno Setup ne fait qu'empaqueter des fichiers existants.
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Désinstaller {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Lancer {#MyAppName} maintenant"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Ne supprime PAS la base de données utilisateur (%LOCALAPPDATA%\CIMP) à la désinstallation : seules les
; données applicatives installées par ce programme dans {app} sont retirées, jamais les données métier.
Type: filesandordirs; Name: "{app}"
