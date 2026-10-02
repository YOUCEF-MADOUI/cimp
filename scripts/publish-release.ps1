#requires -Version 5.1
<#
.SYNOPSIS
    Pipeline complet de preparation de la version V1 publiable de CIMP (SANS abonnement, SANS
    licence, SANS activation) : build Release -> tests -> publication self-contained win-x64 ->
    dossier propre -> ZIP -> SHA-256.

.DESCRIPTION
    Ce script DOIT etre execute sous Windows, avec le SDK .NET 8 complet installe (Visual Studio
    2022, ou juste le SDK .NET 8), car :
      - le projet WPF (ImportCostAlgeria.Presentation) cible net8.0-windows et necessite la
        compilation XAML -> BAML, qui n'existe que sous Windows ;
      - la publication self-contained win-x64 telecharge le runtime pack Windows x64 depuis
        NuGet.org, ce qui necessite un acces reseau.
    Il ne peut donc PAS etre execute dans le sandbox Linux utilise pour developper CIMP (ni SDK
    .NET, ni acces reseau la-bas) - voir packaging/RAPPORT_PACKAGING.md pour le detail de cette
    limitation.

    Ce script NE MODIFIE AUCUNE fonctionnalite metier. Il se contente d'automatiser exactement les
    commandes "dotnet" standard que vous pouvez aussi taper a la main.

    Note technique (volontairement respectee dans tout ce fichier) : aucune continuation de ligne
    par backtick (`) n'est utilisee nulle part, et aucune chaine n'utilise d'echappement par
    backtick. Les appels a des executables externes avec plusieurs arguments (dotnet, ISCC.exe)
    passent par un tableau PowerShell "splatte" avec @(...) / @variable, ce qui est a la fois plus
    lisible et beaucoup plus robuste (la continuation par backtick est cassee silencieusement par
    le moindre espace resuduel apres le backtick, ce qui a cause une version precedente invalide de
    ce fichier).

.EXAMPLE
    cd D:\code\CIMP\YOUCEF-MADOUI\cimp
    powershell -ExecutionPolicy Bypass -File scripts\publish-release.ps1

.EXAMPLE
    # Pour sauter les tests (deconseille, uniquement pour un essai rapide) :
    powershell -ExecutionPolicy Bypass -File scripts\publish-release.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "",
    [switch]$SkipTests,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $RepoRoot

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path (Split-Path $RepoRoot -Parent) "CIMP-Release"
}

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host ("==== " + $Message + " ====") -ForegroundColor Cyan
}

function Fail {
    param([string]$Message)
    Write-Host ""
    Write-Host ("ECHEC : " + $Message) -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------------------
# 0. Verifications prealables
# ---------------------------------------------------------------------------
Write-Step "Verification de l'environnement"

$dotnetCmd = Get-Command "dotnet" -ErrorAction SilentlyContinue
if (-not $dotnetCmd) {
    Fail "Le SDK .NET n'est pas installe ou n'est pas dans le PATH. Installez le SDK .NET 8 depuis https://dotnet.microsoft.com/download/dotnet/8.0 puis relancez ce script."
}

$dotnetVersion = (& dotnet --version)
Write-Host ("SDK .NET detecte : " + $dotnetVersion)
if (-not $dotnetVersion.StartsWith("8.")) {
    Write-Host ("ATTENTION : SDK .NET " + $dotnetVersion + " detecte (8.x attendu). La publication peut echouer si le SDK 8 n'est pas egalement installe en parallele.") -ForegroundColor Yellow
}

$solution = Join-Path $RepoRoot "ImportCostAlgeria.sln"
$presentationCsproj = Join-Path $RepoRoot "src\ImportCostAlgeria.Presentation\ImportCostAlgeria.Presentation.csproj"
$testProject = Join-Path $RepoRoot "tests\ImportCostAlgeria.UnitTests\ImportCostAlgeria.UnitTests.csproj"
$readmeSource = Join-Path $RepoRoot "packaging\README.txt"

if (-not (Test-Path $solution)) { Fail ("Introuvable : " + $solution) }
if (-not (Test-Path $presentationCsproj)) { Fail ("Introuvable : " + $presentationCsproj) }
if (-not (Test-Path $readmeSource)) { Fail ("Introuvable : " + $readmeSource) }

$PackageName = "CIMP-" + $Version + "-Windows-x64"
$PublishOutDir = Join-Path $RepoRoot ("src\ImportCostAlgeria.Presentation\bin\Release\net8.0-windows\" + $Runtime + "\publish")
$CleanPackageDir = Join-Path $OutputRoot $PackageName
$ZipPath = Join-Path $OutputRoot ($PackageName + ".zip")
$Sha256Path = Join-Path $OutputRoot "SHA256.txt"

if (-not (Test-Path $OutputRoot)) {
    New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
}

# ---------------------------------------------------------------------------
# 1. Nettoyage
# ---------------------------------------------------------------------------
Write-Step "dotnet clean (solution complete)"
& dotnet clean $solution -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { Fail ("dotnet clean a echoue (code " + $LASTEXITCODE + ").") }

Write-Step "dotnet restore (solution complete)"
& dotnet restore $solution | Out-Host
if ($LASTEXITCODE -ne 0) { Fail ("dotnet restore a echoue (code " + $LASTEXITCODE + ").") }

# ---------------------------------------------------------------------------
# 2. Tests (0 echec exige avant toute publication)
# ---------------------------------------------------------------------------
if (-not $SkipTests) {
    Write-Step "dotnet test (0 echec exige)"
    $testArgs = @(
        "test"
        $testProject
        "-c"
        "Release"
        "--logger"
        "trx;LogFileName=TestResults.trx"
    )
    & dotnet @testArgs | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Fail "Au moins un test a echoue. Corrigez le code puis relancez ce script. La publication N'A PAS ete creee (regle : ne jamais publier si dotnet test echoue)."
    }
    Write-Host "Tous les tests sont PASSES." -ForegroundColor Green
}
else {
    Write-Host "Tests SAUTES (-SkipTests) : deconseille pour une publication reelle." -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# 3. Build Release (solution complete, detecte toute erreur de compilation)
# ---------------------------------------------------------------------------
Write-Step "dotnet build -c Release (solution complete, 0 erreur exigee)"
& dotnet build $solution -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { Fail ("dotnet build Release a echoue (code " + $LASTEXITCODE + ").") }

# ---------------------------------------------------------------------------
# 4. Publication self-contained win-x64 (seul le projet WPF est publie ainsi)
# ---------------------------------------------------------------------------
Write-Step ("dotnet publish -c Release -r " + $Runtime + " --self-contained true")
if (Test-Path $PublishOutDir) {
    Remove-Item $PublishOutDir -Recurse -Force
}

$publishArgs = @(
    "publish"
    $presentationCsproj
    "-c"
    "Release"
    "-r"
    $Runtime
    "--self-contained"
    "true"
    ("-p:Version=" + $Version)
    "-p:PublishSingleFile=false"
    "-p:PublishTrimmed=false"
    "-o"
    $PublishOutDir
)
& dotnet @publishArgs | Out-Host
if ($LASTEXITCODE -ne 0) { Fail ("dotnet publish a echoue (code " + $LASTEXITCODE + ").") }

$exePath = Join-Path $PublishOutDir "CIMP.exe"
if (-not (Test-Path $exePath)) {
    Fail ("CIMP.exe est introuvable dans " + $PublishOutDir + " apres la publication. Publication consideree comme un ECHEC (ne pas annoncer de succes sans ce fichier).")
}
Write-Host ("CIMP.exe genere : " + $exePath) -ForegroundColor Green

# ---------------------------------------------------------------------------
# 5. Dossier de sortie propre (Etape 5 : ne garder que le necessaire)
# ---------------------------------------------------------------------------
Write-Step "Preparation du dossier de distribution propre"
if (Test-Path $CleanPackageDir) {
    Remove-Item $CleanPackageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $CleanPackageDir -Force | Out-Null

Copy-Item (Join-Path $PublishOutDir "*") $CleanPackageDir -Recurse -Force

# Retrait defensif des fichiers de developpement qui ne doivent jamais atteindre l'utilisateur
# final, meme si DebugType=embedded (voir le .csproj) evite normalement la generation de .pdb
# separes.
Get-ChildItem $CleanPackageDir -Recurse -Include "*.pdb" -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

Get-ChildItem $CleanPackageDir -Recurse -Include "*.xml" -ErrorAction SilentlyContinue |
    Where-Object { -not $_.Name.StartsWith("CIMP.") } |
    Remove-Item -Force -ErrorAction SilentlyContinue

Copy-Item $readmeSource (Join-Path $CleanPackageDir "README.txt") -Force

Write-Host "Contenu final :" -ForegroundColor Green
Get-ChildItem $CleanPackageDir | Select-Object Name, Length | Format-Table -AutoSize | Out-Host

$totalSizeBytes = (Get-ChildItem $CleanPackageDir -Recurse | Measure-Object -Property Length -Sum).Sum
$totalSizeMb = [math]::Round(($totalSizeBytes / 1MB), 1)
Write-Host ("Taille totale du dossier publie : " + $totalSizeMb + " MB") -ForegroundColor Green

# ---------------------------------------------------------------------------
# 6. ZIP
# ---------------------------------------------------------------------------
Write-Step "Creation du ZIP"
if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}
Compress-Archive -Path $CleanPackageDir -DestinationPath $ZipPath -CompressionLevel Optimal

$zipSizeBytes = (Get-Item $ZipPath).Length
$zipSizeMb = [math]::Round(($zipSizeBytes / 1MB), 1)
Write-Host ("ZIP cree : " + $ZipPath + " (" + $zipSizeMb + " MB)") -ForegroundColor Green

# ---------------------------------------------------------------------------
# 7. SHA-256
# ---------------------------------------------------------------------------
Write-Step "Calcul du SHA-256"
$hash = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash
$sha256Lines = @(
    ($PackageName + ".zip")
    ("SHA-256 : " + $hash)
)
$sha256Lines | Out-File -FilePath $Sha256Path -Encoding utf8
Write-Host ("SHA-256 (" + $PackageName + ".zip) : " + $hash) -ForegroundColor Green
Write-Host ("Ecrit dans : " + $Sha256Path)

# ---------------------------------------------------------------------------
# 8. Installateur Inno Setup (optionnel : necessite Inno Setup installe separement)
# ---------------------------------------------------------------------------
if (-not $SkipInstaller) {
    Write-Step "Installateur Inno Setup (optionnel)"

    $isccPath = $null
    $isccCmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($isccCmd) {
        $isccPath = $isccCmd.Source
    }
    else {
        $programFilesX86 = [System.Environment]::GetEnvironmentVariable("ProgramFiles(x86)")
        if ($programFilesX86) {
            $candidate = Join-Path $programFilesX86 "Inno Setup 6\ISCC.exe"
            if (Test-Path $candidate) {
                $isccPath = $candidate
            }
        }
    }

    if ($isccPath) {
        $issScript = Join-Path $RepoRoot "packaging\installer\CIMP.iss"
        $isccArgs = @(
            $issScript
            ("/DMyAppVersion=" + $Version)
            ("/DMySourceDir=" + $CleanPackageDir)
            ("/DMyOutputDir=" + $OutputRoot)
        )
        & $isccPath @isccArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Host ("La generation de l'installateur Inno Setup a echoue (code " + $LASTEXITCODE + "). Le ZIP reste disponible et valide.") -ForegroundColor Yellow
        }
        else {
            $setupExePath = Join-Path $OutputRoot ("CIMP-" + $Version + "-Setup-x64.exe")
            Write-Host ("Installateur genere : " + $setupExePath) -ForegroundColor Green
        }
    }
    else {
        Write-Host "Inno Setup (ISCC.exe) introuvable : installateur NON genere." -ForegroundColor Yellow
        Write-Host "Installez Inno Setup depuis https://jrsoftware.org/isinfo.php puis relancez ce script, ou executez manuellement la commande suivante :" -ForegroundColor Yellow
        $manualCommand = 'ISCC.exe "' + (Join-Path $RepoRoot "packaging\installer\CIMP.iss") + '" /DMyAppVersion=' + $Version + ' /DMySourceDir="' + $CleanPackageDir + '" /DMyOutputDir="' + $OutputRoot + '"'
        Write-Host ("  " + $manualCommand)
    }
}

Write-Step "Termine"
Write-Host ("Dossier publie : " + $CleanPackageDir)
Write-Host ("ZIP            : " + $ZipPath)
Write-Host ("SHA-256        : " + $Sha256Path)
