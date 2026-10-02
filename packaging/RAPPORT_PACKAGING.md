# CIMP — Rapport de préparation V1 publiable (sans abonnement)

Commit : `ea59309` (sur `arena/01a0f335-cimp`), au-dessus de `094c12a`.

## ⚠️ Limitation d'environnement — à lire en premier

Ce travail a été fait dans un **sandbox Linux sans SDK .NET installé et sans accès réseau sortant**
(confirmé à nouveau : `dotnet` introuvable, `apt-get`/téléchargement NuGet impossibles). Il est donc
**impossible d'exécuter réellement** `dotnet build`, `dotnet test`, `dotnet publish`, de lancer
`CIMP.exe`, de créer le ZIP final, de calculer un SHA-256 réel, ou d'exécuter Inno Setup (ISCC.exe)
depuis cet environnement : Inno Setup et la compilation XAML→BAML sont de toute façon des outils
**Windows uniquement**, même avec un accès réseau.

Ce qui a donc été livré dans ce tour :
1. Un **audit complet** du dépôt (lecture, pas d'exécution) — rien trouvé à corriger.
2. Les **scripts et profils prêts à l'emploi** pour que VOUS exécutiez vous-même, sur votre PC Windows
   avec le SDK .NET 8, exactement le pipeline demandé (Étapes 2 à 12) en une seule commande.
3. Les **métadonnées de version/produit** et le **README utilisateur final**.

**Aucune étape ne doit être annoncée "terminée" tant que vous n'avez pas confirmé le résultat réel
de `scripts\publish-release.ps1` sur votre machine.**

## Comment produire le package réel (à faire sur votre PC Windows)

```powershell
cd D:\code\CIMP\YOUCEF-MADOUI\cimp
git pull
powershell -ExecutionPolicy Bypass -File scripts\publish-release.ps1
```

Ce script exécute, dans l'ordre, exactement les étapes demandées : `dotnet clean` → `dotnet restore` →
`dotnet test` (arrêt immédiat si un test échoue, pas de publication dans ce cas) → `dotnet build -c
Release` (arrêt si erreur) → `dotnet publish -c Release -r win-x64 --self-contained true` → vérifie que
`CIMP.exe` existe réellement → copie dans un dossier propre `CIMP-1.0.0-Windows-x64` + `README.txt` →
crée `CIMP-1.0.0-Windows-x64.zip` → calcule et écrit le vrai SHA-256 dans `SHA256.txt` → tente de générer
l'installateur Inno Setup si `ISCC.exe` est détecté (sinon vous indique comment l'installer).

Résultat attendu dans `..\CIMP-Release\` (à côté du dépôt) :
```
CIMP-Release\
  CIMP-1.0.0-Windows-x64\      (dossier à tester directement : CIMP-1.0.0-Windows-x64\CIMP.exe)
  CIMP-1.0.0-Windows-x64.zip
  SHA256.txt
  CIMP-1.0.0-Setup-x64.exe     (si Inno Setup est installé)
```

## Audit du dépôt (Étape 1) — résultat : RAS

- Tous les `.csproj` inspectés (10 projets) : SDK `Microsoft.NET.Sdk` partout, cibles `net8.0` (moteurs)
  et `net8.0-windows` (WPF) cohérentes ; références de projets cohérentes avec la couche
  Core/RegulatoryEngine/CalculationEngine/ExcelEngine/Audit/AI/Reporting/Database/Presentation.
- **Aucun chemin codé en dur** (`C:\`, `D:\`, `/home/...`) trouvé dans le code source.
- **Aucun secret/mot de passe/clé API** codé en dur : le compte "admin" initial utilise un mot de passe
  **généré aléatoirement** à la première exécution (`PasswordHasher.GenerateRandomPassword`), jamais une
  valeur fixe.
- Base de données : SQLite locale dans `%LOCALAPPDATA%\CIMP\cimp.db`, créée automatiquement
  (`DbContextFactory.EnsureDatabaseReadyWithSeed`) — aucune dépendance à un serveur externe, aucune
  chaîne de connexion à configurer manuellement pour un usage standard.
- **Aucune dépendance à Visual Studio** pour l'exécution (uniquement pour éditer le XAML si besoin) ;
  aucun outil de génération de code nécessitant le SDK de développement au runtime.
- **Aucun système d'abonnement/licence/activation existant** trouvé — rien à retirer (conforme à la
  consigne : ne rien ajouter de tel à ce stade).
- Note légale (à votre arbitrage, pas un bug) : `QuestPDF` (export PDF) est sous licence **Community**
  gratuite sous condition de chiffre d'affaires annuel < 1 M$ — à vérifier si votre usage commercial
  dépasse ce seuil (voir https://www.questpdf.com/license/).

## Corrections de code (Étape 2)

Les 3 bugs XAML/ViewModel historiques mentionnés dans votre demande étaient **déjà corrigés lors d'un
tour précédent** (commits `95ffe70` et `094c12a`, re-vérifiés ligne par ligne dans ce tour) :
1. `ImportDetailViewModel.cs` / `RelayCommand` : toutes les 42 commandes utilisent des group-methods sans
   paramètre optionnel ambigu (les cas ambigus sont enveloppés dans des lambdas `() => Methode()`).
2. `TauxDeChangeView.xaml` : plus aucun `TextWrapping` sur un `Label` (uniquement sur des `TextBlock`).
3. Crash "Frais" (`TargetType 'TextBlock' != 'TextBlockComboBox'`) : la colonne "Méthode de répartition"
   cible désormais `ComboBox` dans `ElementStyle` comme dans `EditingElementStyle`.

Vérifications supplémentaires faites dans **ce** tour :
4. **Bindings OneWay** : toutes les propriétés calculées en lecture seule (`TotalCoutRevientDzd`,
   `ValeurDouaniereDzd`, `DroitDouaneDzd`, `CsDzd`, `TvaDzd`, `PrctDzd`, `ProfitDzd`, `ProfitPercent`,
   `TotalPrixVenteDzd`, `TotalBeneficeDzd`, toutes les propriétés `Simulation*`...) ne sont liées qu'à des
   `TextBlock.Text` (mode `OneWay` par défaut dans WPF) ou à des colonnes `DataGridTextColumn
   IsReadOnly="True"` : aucune ne peut déclencher d'écriture accidentelle vers le ViewModel.
5. Les 8 onglets (Import, Articles, Taux de change, Frais, Taxes, Notifications, Résultats, Rapports)
   ont été relus statiquement (XAML bien formé confirmé par analyse XML, pas de `Style` mal ciblé
   restant) — un test réel de navigation reste à faire par vous sur Windows (voir checklist ci-dessous).

**Correction supplémentaire faite dans ce tour** (hors périmètre strict Étape 2, mais directement liée à
l'Étape 9 "Informations de version") : le dialogue "À propos" (`MainViewModel.ShowAbout`) affichait un
nom de produit légèrement différent ("CIMP — Coût d'Importation Maître Pro") sans numéro de version ; il
affiche maintenant exactement "CIMP — Coût d'Importation Algérie" + la version lue dynamiquement depuis
les métadonnées d'assembly (jamais codée en double).

## Tests (Étape 3)

**NON EXÉCUTÉS dans ce sandbox** (pas de SDK .NET). Note utile : contrairement au projet WPF, le projet
de tests (`tests/ImportCostAlgeria.UnitTests`, cible `net8.0`) et tous les moteurs métier qu'il référence
sont du C# pur **sans dépendance Windows** — `dotnet test` pourrait donc en théorie tourner sur
Linux/macOS également si un SDK .NET était disponible ; seule la **publication du projet WPF** exige
Windows. À exécuter chez vous :
```powershell
dotnet test tests\ImportCostAlgeria.UnitTests\ImportCostAlgeria.UnitTests.csproj
```
Objectif : 0 échec. `scripts\publish-release.ps1` l'exécute automatiquement et **s'arrête sans publier**
si un seul test échoue.

## Build Release / Publication (Étapes 4 à 7)

**NON EXÉCUTÉS dans ce sandbox.** Préparés et vérifiés statiquement :
- `ImportCostAlgeria.Presentation.csproj` cible bien `net8.0-windows`, `OutputType=WinExe`,
  `AssemblyName=CIMP` (déjà en place).
- Nouveau : `Version=1.0.0`, `Product="CIMP — Coût d'Importation Algérie"`, propriétés self-contained
  actives uniquement en publication (`-r win-x64`), `PublishTrimmed=false` (le trimming n'est pas fiable
  avec WPF), `DebugType=embedded` (pas de `.pdb` séparé à nettoyer).
- Nouveau profil `src/ImportCostAlgeria.Presentation/Properties/PublishProfiles/FolderProfile.pubxml`.
- Tous les fichiers `.csproj`/`.pubxml` modifiés ou créés ont été validés comme **XML bien formé**
  (un bug a d'ailleurs été détecté et corrigé pendant cette vérification : un commentaire XML contenant
  `--self-contained` — deux tirets consécutifs sont interdits dans un commentaire XML et auraient
  provoqué une erreur de chargement du projet ; corrigé en `/self-contained` dans le texte du
  commentaire).

## Dossier de sortie / ZIP / README / SHA-256 (Étapes 5, 7, 8, 11)

- `packaging/README.txt` : notice utilisateur finale (configuration, installation, dépannage,
  désinstallation, support) — copiée automatiquement dans le ZIP par le script.
- Le script `publish-release.ps1` retire tout `.pdb` résiduel et se limite au contenu du dossier publié
  + `README.txt` (jamais le code source, jamais `.git`, jamais les fichiers de test).
- `SHA256.txt` est généré avec le **vrai** hash (`Get-FileHash -Algorithm SHA256`) du ZIP réellement
  produit — jamais une valeur inventée (ce sera visible uniquement après exécution chez vous).

## Installateur Inno Setup (Étape 12)

- Inno Setup / `ISCC.exe` **absent de ce sandbox** (confirmé : Linux, outil Windows uniquement) — non
  installé automatiquement, conformément à votre consigne.
- `packaging/installer/CIMP.iss` est prêt : installation dans Program Files, raccourci Menu Démarrer,
  raccourci Bureau optionnel, désinstallation standard, nom "CIMP", version 1.0.0, **aucun système de
  licence/abonnement/activation**. Le script `publish-release.ps1` l'appelle automatiquement s'il détecte
  `ISCC.exe` ; sinon il vous indique la marche à suivre.

## Checklist de test manuel (Étape 6) — à faire par vous sur le ZIP réel

1. Extraire le ZIP sur un **autre dossier** (pas le dépôt Git) pour vérifier l'absence de dépendance
   cachée au chemin du projet Visual Studio.
2. Démarrage de `CIMP.exe`, fenêtre de connexion, création du compte admin.
3. Fenêtre principale, navigation entre TOUS les onglets (Import, Articles, Taux de change, **Frais**,
   Taxes, Notifications, Résultats, Rapports) sans crash.
4. Création/ouverture d'une opération, import Excel, mapping, calcul.
5. Taux de change (manuel + officiel), simulation de variation (-5..+5), frais, taxes, notifications.
6. Export Excel, export PDF, fermeture, réouverture de l'application.

## Rapport final (format demandé — Étape 13)

| # | Point | Résultat |
|---|-------|----------|
| 1 | Build Release | **NON EXÉCUTÉ ICI** (pas de SDK .NET dans ce sandbox) — à exécuter via `scripts\publish-release.ps1` |
| 2 | Tests | **NON EXÉCUTÉS ICI** — idem |
| 3 | Publication self-contained | **NON EXÉCUTÉE ICI** — idem |
| 4 | Fichier EXE créé | **PAS ENCORE** — sera `...\CIMP-Release\CIMP-1.0.0-Windows-x64\CIMP.exe` après exécution du script |
| 5 | Taille du package | Inconnue tant que non généré |
| 6 | ZIP créé | **PAS ENCORE** |
| 7 | Hash SHA-256 | **PAS ENCORE** (ne sera jamais inventé) |
| 8 | Installateur | **PAS ENCORE** (script + .iss prêts, nécessite Inno Setup sur votre PC) |
| 9 | Problèmes restants | (a) Confirmer `dotnet test`/`dotnet build`/`dotnet publish` réels chez vous ; (b) vérifier l'éligibilité de la licence QuestPDF Community selon votre chiffre d'affaires ; (c) tester réellement le ZIP sur un second PC Windows propre (checklist ci-dessus) |
| 10 | Actions recommandées | Exécuter `scripts\publish-release.ps1` sur Windows, me renvoyer la sortie complète ; je corrigerai immédiatement toute erreur réelle rapportée |

Je ne déclare donc PAS cette étape "terminée" : le code, les scripts et la documentation sont prêts et
revus statiquement, mais **le `CIMP.exe` réel n'a pas encore été généré ni vérifié**, exactement comme
demandé.
