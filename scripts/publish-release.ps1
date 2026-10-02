#requires -Version 5.1
<#
.SYNOPSIS
    Pipeline complet de préparation de la version V1 publiable de CIMP (SANS abonnement, SANS licence,
    SANS activation) : build Release -> tests -> publication self-contained win-x64 -> dossier propre
    -> ZIP -> SHA-256.

.DESCRIPTION
    Ce script DOIT être exécuté sous Windows, avec le SDK .NET 8 complet installé (Visual Studio 2022
    ou juste le SDK .NET 8), car :
      - le projet WPF (ImportCostAlgeria.Presentation) cible net8.0-windows et nécessite la compilation
        XAML -> BAML, qui n'existe que sous Windows ;
      - la publication self-contained win-x64 télécharge le runtime pack Windows x64 depuis NuGet.org,
        ce qui nécessite un accès réseau.
    Il ne peut donc PAS être exécuté dans le sandbox Linux utilisé pour développer CIMP (ni SDK .NET, ni
    accès réseau là-bas) — voir le rapport de packaging pour le détail de cette limitation.

    Ce script NE MODIFIE AUCUNE fonctionnalité métier. Il se contente d'automatiser exactement les
    commandes "dotnet" standard que vous pouvez aussi taper à la main.

.EXAMPLE
    cd D:\code\CIMP\YOUCEF-MADOUI\cimp
    powershell -ExecutionPolicy Bypass -File scripts\publish-release.ps1

.EXAMPLE
    # Pour sauter les tests (déconseillé, uniquement pour un essai rapide) :
    powershell -ExecutionPolicy Bypass -File scripts\publish-release.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "$PSScriptRoot\..\..\CIMP-Release",
    [switch]$SkipTests,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path "$PSScriptRoot\.."
Set-Location $RepoRoot

function Write-Step($msg) {
    Write-Host ""
    Write-Host "==== $msg ====" -ForegroundColor Cyan
}

function Fail($msg) {
    Write-Host ""
    Write-Host "ÉCHEC : $msg" -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------------------
# 0. Vérifications préalables
# ---------------------------------------------------------------------------
Write-Step "Vérification de l'environnement"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail "Le SDK .NET n'est pas installé ou n'est pas dans le PATH. Installez le SDK .NET 8 depuis https://dotnet.microsoft.com/download/dotnet/8.0 puis relancez ce script."
}

$dotnetVersion = (dotnet --version)
Write-Host "SDK .NET détecté : $dotnetVersion"
if (-not $dotnetVersion.StartsWith("8.")) {
    Write-Host "ATTENTION : SDK .NET $dotnetVersion détecté (8.x attendu). La publication peut échouer si le SDK 8 n'est pas également installé en parallèle." -ForegroundColor Yellow
}

$solution = Join-Path $RepoRoot "ImportCostAlgeria.sln"
$presentationCsproj = Join-Path $RepoRoot "src\ImportCostAlgeria.Presentation\ImportCostAlgeria.Presentation.csproj"
if (-not (Test-Path $presentationCsproj)) { Fail "Introuvable : $presentationCsproj" }

$PackageName = "CIMP-$Version-Windows-x64"
$PublishOutDir = Join-Path $RepoRoot "src\ImportCostAlgeria.Presentation\bin\Release\net8.0-windows\$Runtime\publish"
$CleanPackageDir = Join-Path $OutputRoot $PackageName
$ZipPath = Join-Path $OutputRoot "$PackageName.zip"
$Sha256Path = Join-Path $OutputRoot "SHA256.txt"

# ---------------------------------------------------------------------------
# 1. Nettoyage
# ---------------------------------------------------------------------------
Write-Step "dotnet clean (solution complète)"
dotnet clean $solution -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { Fail "dotnet clean a échoué (code $LASTEXITCODE)." }

Write-Step "dotnet restore (solution complète)"
dotnet restore $solution | Out-Host
if ($LASTEXITCODE -ne 0) { Fail "dotnet restore a échoué (code $LASTEXITCODE)." }

# ---------------------------------------------------------------------------
# 2. Tests (0 échec exigé avant toute publication)
# ---------------------------------------------------------------------------
if (-not $SkipTests) {
    Write-Step "dotnet test (0 échec exigé)"
    $testProject = Join-Path $RepoRoot "tests\ImportCostAlgeria.UnitTests\ImportCostAlgeria.UnitTests.csproj"
    dotnet test $testProject -c Release --logger "trx;LogFileName=TestResults.trx" | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Fail "Au moins un test a échoué (code $LASTEXITCODE). Corrigez le code puis relancez ce script. La publication N'A PAS été créée (conformément à la règle : ne jamais publier si dotnet test échoue)."
    }
    Write-Host "Tous les tests sont PASSÉS." -ForegroundColor Green
} else {
    Write-Host "Tests SAUTÉS (-SkipTests) — déconseillé pour une publication réelle." -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# 3. Build Release (solution complète, détecte toute erreur de compilation)
# ---------------------------------------------------------------------------
Write-Step "dotnet build -c Release (solution complète, 0 erreur exigée)"
dotnet build $solution -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { Fail "dotnet build Release a échoué (code $LASTEXITCODE)." }

# ---------------------------------------------------------------------------
# 4. Publication self-contained win-x64 (seul le projet WPF est publié ainsi)
# ---------------------------------------------------------------------------
Write-Step "dotnet publish -c Release -r $Runtime --self-contained true"
if (Test-Path $PublishOutDir) { Remove-Item $PublishOutDir -Recurse -Force }

dotnet publish $presentationCsproj `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:Version=$Version `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -o $PublishOutDir | Out-Host
if ($LASTEXITCODE -ne 0) { Fail "dotnet publish a échoué (code $LASTEXITCODE)." }

$exePath = Join-Path $PublishOutDir "CIMP.exe"
if (-not (Test-Path $exePath)) {
    Fail "CIMP.exe est introuvable dans $PublishOutDir après la publication. Publication considérée comme un ÉCHEC (ne pas annoncer de succès sans ce fichier)."
}
Write-Host "CIMP.exe généré : $exePath" -ForegroundColor Green

# ---------------------------------------------------------------------------
# 5. Dossier de sortie propre (Étape 5 — ne garder que le nécessaire)
# ---------------------------------------------------------------------------
Write-Step "Préparation du dossier de distribution propre"
if (Test-Path $CleanPackageDir) { Remove-Item $CleanPackageDir -Recurse -Force }
New-Item -ItemType Directory -Path $CleanPackageDir -Force | Out-Null

Copy-Item "$PublishOutDir\*" $CleanPackageDir -Recurse -Force

# Retrait défensif des fichiers de développement qui ne doivent jamais atteindre l'utilisateur final,
# même si DebugType=embedded (voir .csproj) évite normalement la génération de .pdb séparés.
Get-ChildItem $CleanPackageDir -Recurse -Include "*.pdb" | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem $CleanPackageDir -Recurse -Include "*.xml" | Where-Object { $_.Name -notmatch "^CIMP\." } | Remove-Item -Force -ErrorAction SilentlyContinue

Copy-Item (Join-Path $RepoRoot "packaging\README.txt") (Join-Path $CleanPackageDir "README.txt") -Force

Write-Host "Contenu final :" -ForegroundColor Green
Get-ChildItem $CleanPackageDir | Select-Object Name, Length | Format-Table -AutoSize | Out-Host

$totalSizeMb = [math]::Round(((Get-ChildItem $CleanPackageDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB), 1)
Write-Host "Taille totale du dossier publié : $totalSizeMb MB" -ForegroundColor Green

# ---------------------------------------------------------------------------
# 6. ZIP
# ---------------------------------------------------------------------------
Write-Step "Création du ZIP"
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path $CleanPackageDir -DestinationPath $ZipPath -CompressionLevel Optimal
$zipSizeMb = [math]::Round(((Get-Item $ZipPath).Length / 1MB), 1)
Write-Host "ZIP créé : $ZipPath ($zipSizeMb MB)" -ForegroundColor Green

# ---------------------------------------------------------------------------
# 7. SHA-256
# ---------------------------------------------------------------------------
Write-Step "Calcul du SHA-256"
$hash = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash
"$PackageName.zip`r`nSHA-256 : $hash" | Out-File -FilePath $Sha256Path -Encoding utf8
Write-Host "SHA-256 ($PackageName.zip) : $hash" -ForegroundColor Green
Write-Host "Écrit dans : $Sha256Path"

# ---------------------------------------------------------------------------
# 8. Installateur Inno Setup (optionnel — nécessite Inno Setup installé séparément)
# ---------------------------------------------------------------------------
if (-not $SkipInstaller) {
    Write-Step "Installateur Inno Setup (optionnel)"
    $iscc = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if (-not $iscc) {
        $commonPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        if (Test-Path $commonPath) { $iscc = Get-Item $commonPath }
    }
    if ($iscc) {
        $issScript = Join-Path $RepoRoot "packaging\installer\CIMP.iss"
        & $iscc.Path $issScript "/DMyAppVersion=$Version" "/DMySourceDir=$CleanPackageDir" "/DMyOutputDir=$OutputRoot"
        if ($LASTEXITCODE -ne 0) {
            Write-Host "La génération de l'installateur Inno Setup a échoué (code $LASTEXITCODE) — le ZIP reste disponible et valide." -ForegroundColor Yellow
        } else {
            Write-Host "Installateur généré dans $OutputRoot\CIMP-$Version-Setup-x64.exe" -ForegroundColor Green
        }
    } else {
        Write-Host "Inno Setup (ISCC.exe) introuvable : installateur NON généré. Installez Inno Setup (https://jrsoftware.org/isinfo.php) puis relancez avec -SkipInstaller:`$false, ou exécutez manuellement :" -ForegroundColor Yellow
        Write-Host "  ISCC.exe `"$RepoRoot\packaging\installer\CIMP.iss`" /DMyAppVersion=$Version /DMySourceDir=`"$CleanPackageDir`" /DMyOutputDir=`"$OutputRoot`""
    }
}

Write-Step "Terminé"
Write-Host "Dossier publié : $CleanPackageDir"
Write-Host "ZIP            : $ZipPath"
Write-Host "SHA-256        : $Sha256Path"
