# CIMP — Coût d'Importation Algérie

Application professionnelle de calcul du **coût de revient réel des marchandises importées en Algérie** :
droits de douane, taxes (DAPS, TIC…), TVA à l'importation, valeur en douane, répartition des frais
d'approche, anomalies, et rapports Excel/PDF — sans jamais coder en dur un taux, une exonération ou une
règle douanière.

> ⚠️ **Important** : la présente version livrée et **réellement fonctionnelle** est une application
> web full-stack (Node.js + base de données SQLite persistante), utilisable dès maintenant depuis un
> navigateur. Une architecture C#/.NET 8 complète (voir plus bas) documente également la cible native
> Windows, mais n'a pas pu être compilée ni exécutée dans cet environnement d'exécution (SDK .NET non
> installable — accès réseau restreint, voir section **Limitations**).

---

## 1. Ce que l'application permet de faire aujourd'hui (testé de bout en bout)

1. Créer une entreprise (raison sociale, NIF, régime TVA import récupérable/non récupérable).
2. Créer un dossier d'importation (numéro, date, fournisseur, pays d'expédition, pays d'origine par
   défaut, devise, Incoterm `EXW`/`FOB`/`CFR`, port, transport, n° facture).
3. Importer un vrai fichier **.xlsx / .csv** fournisseur, avec détection automatique des colonnes
   connues et mapping manuel guidé pour les colonnes non reconnues.
4. Confirmer le mapping et l'enregistrer comme **modèle réutilisable** pour ce fournisseur (reconnu
   automatiquement au prochain import du même type de fichier).
5. Gérer le catalogue produits et les articles du dossier (ajout manuel ou via Excel).
6. Faire proposer un **Code SH à 10 chiffres par l'IA**, avec confirmation humaine obligatoire
   (`CONFIRMER` / `MODIFIER` / `REFUSER`) — l'IA ne modifie jamais un code SH définitif seule.
7. Ajouter autant de frais que nécessaire (fret, assurance, frais portuaires, THC, magasinage, transit,
   transport local, frais bancaires…), chacun avec sa devise, sa méthode de répartition parmi les 7
   méthodes prévues, et ses drapeaux d'inclusion (valeur en douane / coût de revient).
8. Gérer les **règles réglementaires versionnées** (Code SH, taxe, taux, régime, dates, source
   juridique) et les **taux de change officiels** — sans qu'aucun taux ne soit jamais codé en dur : si
   une règle manque, le calcul est **bloqué** avec le message `INFORMATION NON DÉTERMINÉE`. Seul un
   utilisateur au rôle **Administrateur** peut publier une règle ou un taux (HTTP 403 sinon), et
   l'historique complet des versions successives (taux, dates, source) reste consultable par Code SH.
9. Lancer le calcul complet : valeur en douane, droit de douane, taxes additionnelles, TVA, coût
   douanier total, coût de revient économique par ligne et consolidé, coût unitaire.
10. Visualiser les anomalies (`INFO`, `AVERTISSEMENT`, `ERREUR`, `BLOCAGE`), notamment la comparaison
    automatique **taux Excel vs taux réglementaire** (le taux réglementaire est toujours prioritaire) et
    l'alerte **⚠️ TAUX MANUEL** en cas de taux de change saisi manuellement.
11. Exporter un **rapport Excel réel** à 5 feuilles (`DETAIL_ARTICLES`, `RECAPITULATIF`, `FRAIS`,
    `TAXES`, `CONTROLES`) et un **rapport PDF réel**, générés dynamiquement à partir du dernier calcul.
12. Fermer l'application et la rouvrir : toutes les données (entreprises, dossiers, lignes, frais,
    règles, calculs, audit, utilisateurs) sont conservées dans une base **SQLite persistante sur disque**.
13. Gérer les **utilisateurs et leurs rôles** (Administrateur / Utilisateur / Consultation) et rattacher
    automatiquement chaque référence Excel déjà connue au **catalogue produit** de l'entreprise (Code SH
    et origine auto-complétés), avec proposition d'ajout au catalogue pour les nouvelles références.
14. **Modifier** une entreprise, un produit, un article ou un frais déjà enregistré directement depuis
    l'interface (bouton « Modifier » sur chaque ligne, formulaire pré-rempli), puis **recalculer** le
    dossier — aucune édition de fichier ou de base de données n'est requise.
15. **Comprendre le détail de chaque calcul** : sur l'écran de résultats, le bouton « Voir le détail »
    de chaque article déplie la formation complète du montant (prix fournisseur converti, frais répartis
    un par un, valeur en douane, droit de douane avec sa source légale, chaque taxe additionnelle avec sa
    base et sa référence réglementaire, TVA, puis coût de revient unitaire final).

Un script de test de bout en bout automatisé (`tests/e2e/run_e2e_workflow_test.sh`) valide l'intégralité
de ce pipeline (**16 vérifications, toutes au vert**), y compris le refus de publication réglementaire
par un utilisateur non-Administrateur.

---

## 2. Installation et lancement

Prérequis : **Node.js ≥ 20** (utilise `node:sqlite`, disponible nativement — aucune base de données
externe à installer).

```bash
cd src/ImportCostAlgeria.Presentation/WebPreview
npm install         # installe express, multer, xlsx (SheetJS), pdfkit
node server.js       # démarre le serveur sur http://0.0.0.0:3000
```

Ouvrez ensuite `http://localhost:3000` dans votre navigateur. Au premier lancement, la base de données
`data/cimp.db` est créée automatiquement (vide) ; créez votre première entreprise depuis l'onglet
**Entreprises**.

### Lancer le test de bout en bout

```bash
# Le serveur doit déjà tourner sur http://localhost:3000
bash tests/e2e/run_e2e_workflow_test.sh
```

---

## 3. Premier import — procédure pas à pas

1. Onglet **Entreprises** → créer votre entreprise.
2. Onglet **Importations** → créer un dossier (numéro, date, fournisseur, pays d'expédition, devise,
   Incoterm).
3. Ouvrir le dossier → étape **3. Import Excel** → sélectionner votre fichier `.xlsx`/`.csv`.
4. Vérifier/corriger le mapping des colonnes proposé automatiquement, cocher « Enregistrer ce mapping
   comme modèle », valider.
5. Étape **2. Articles** → pour chaque ligne sans Code SH, cliquer **IA Code SH** puis confirmer ou
   corriger la proposition.
6. Onglet **Réglementation** → publier les règles (DD, TVA, DAPS…) pour les Codes SH concernés, avec
   leur source juridique. Onglet **Taux de change** → publier le taux officiel de la devise utilisée.
7. Étape **4. Frais** → ajouter fret, assurance, frais portuaires, transit… avec leur méthode de
   répartition.
8. Étape **5. Calcul & Résultats** → cliquer **Calculer le dossier**. Consulter le résumé, le détail par
   article et les anomalies.
9. Télécharger le **rapport Excel** et/ou le **rapport PDF** directement depuis l'écran de résultats.

---

## 4. Architecture technique de l'application fonctionnelle

```
src/ImportCostAlgeria.Presentation/WebPreview/
├── server.js                 # API REST Express (toutes les routes CRUD + calcul + exports)
├── lib/
│   ├── db.js                 # Persistance SQLite (node:sqlite) + migrations + audit
│   ├── regulatoryEngine.js   # Règles versionnées, jamais écrasées, jamais inventées
│   ├── calculationEngine.js  # Valeur en douane, DD/DAPS/TIC/TVA, répartition des frais, anomalies
│   ├── excelEngine.js        # Parsing .xlsx/.csv réel, synonymes, signature de mapping
│   ├── reportingEngine.js    # Génération Excel (5 feuilles) et PDF réels
│   └── aiHsClassifier.js     # Proposition de Code SH avec confirmation humaine obligatoire
└── public/
    ├── index.html            # Coquille de l'application (sidebar + zone de contenu)
    └── app.js                # Logique front-end (vanilla JS) : toutes les vues et l'assistant Excel
```

Chaque moteur (`lib/*.js`) est indépendant et sans dépendance à la couche HTTP : `server.js` ne fait
qu'orchestrer les appels, conformément à la séparation stricte des responsabilités demandée
(`Presentation` / `RegulatoryEngine` / `CalculationEngine` / `ExcelEngine` / `Reporting` / `Audit`).

---

## 5. Architecture de référence C# / .NET 8 (spécification complète)

Le projet documente également, et implémente partiellement en C#, l'architecture cible pour une
application Windows native, conforme aux dix modules demandés :

| Module / Projet | Rôle |
| :--- | :--- |
| `src/ImportCostAlgeria.Core` | Entités métier, `Money`, catalogue de frais, champs dynamiques par Incoterm. |
| `src/ImportCostAlgeria.RegulatoryEngine` | Moteur réglementaire versionné, hiérarchie des sources juridiques. |
| `src/ImportCostAlgeria.CalculationEngine` | Conversion devises, valeur en douane, droits/taxes, répartition des frais. |
| `src/ImportCostAlgeria.ExcelEngine` | Détection d'en-têtes, mapping interactif, templates fournisseurs. |
| `src/ImportCostAlgeria.Reporting` | Génération de rapports Excel/PDF. |
| `src/ImportCostAlgeria.Audit` | Journalisation immuable. |
| `src/ImportCostAlgeria.AI` | Classification SH par IA, assistant réglementaire, simulateur. |
| `src/ImportCostAlgeria.Database` | `DbContext` EF Core 8 avec isolation multi-entreprise. |
| `src/ImportCostAlgeria.Presentation` | ViewModels MVVM pour un client natif (WPF / Blazor Desktop). |
| `tests/ImportCostAlgeria.UnitTests` | Suite xUnit (28 tests, Sections 42, V1.1, V1.2). |

Base de données SQL Server complète : `database/01_ImportCostAlgeria_Schema.sql` (21 tables, schémas
`core`, `reg`, `excel`, `calc`, `ai`, `audit`) et `database/02_Seed_Referentiel_Structure.sql`.
Spécification juridique et fonctionnelle complète : `docs/SPECIFICATION_COMPLETE_A_A_H.md`.

Cette base C# **compile conceptuellement** (aucune dépendance externe manquante dans le code écrit) mais
**n'a pas pu être compilée avec `dotnet build`** dans le present environnement (voir Limitations
ci-dessous) ; sa correction et son exécution effective nécessitent un poste disposant du SDK .NET 8.

---

## 5bis. Utilisateurs & Rôles

Un compte **Administrateur Principal** est créé automatiquement au tout premier démarrage (table
`users` vide). Chaque appel API sensible (publication d'une règle réglementaire ou d'un taux de change)
exige un en-tête `X-User-Id` correspondant à un utilisateur existant au rôle `ADMINISTRATEUR` ; à défaut,
l'API répond `403`. L'interface propose un sélecteur « Utilisateur actif » à côté du sélecteur
d'entreprise, et un écran **Utilisateurs & Rôles** pour créer/modifier/supprimer des comptes (le dernier
compte Administrateur ne peut pas être supprimé).

## 6. Règles de conception respectées

* **Aucun taux, aucune exonération, aucun Code SH n'est codé en dur.** Toute donnée réglementaire
  manquante déclenche `INFORMATION NON DÉTERMINÉE` et bloque le calcul jusqu'à saisie contrôlée par un
  utilisateur, avec traçabilité dans le journal d'audit.
* **Aucune règle n'est jamais écrasée** : publier une nouvelle version ferme automatiquement l'ancienne
  (date de fin = veille de la nouvelle date d'effet) sans supprimer l'historique.
* **Séparation stricte** entre **Valeur / Coût Douanier** et **Coût de Revient Économique** à chaque
  étape du calcul et dans les rapports.
* **TVA import non récupérable par défaut**, paramétrable par entreprise (`is_vat_non_recoverable`).
* **IA jamais définitive** : toute proposition (Code SH) exige une confirmation humaine explicite.
* **Taux de change manuel** toujours comparé au taux officiel avec alerte `⚠️ TAUX MANUEL` et trace
  d'audit (taux officiel, taux utilisé, écart, utilisateur, date).
* **Mention légale obligatoire** intégrée à chaque rapport Excel et PDF généré.

---

## 7. Limitations actuelles et roadmap

### Limitations connues
* Le SDK **.NET 8 n'a pas pu être installé** dans le sandbox d'exécution (les domaines de
  téléchargement Microsoft/.NET sont bloqués en sortie réseau, alors que le registre npm est
  accessible). En conséquence, la partie C#/.NET n'a **pas été compilée ni testée par `dotnet test`**
  dans cette session — elle reste une architecture de référence écrite mais non exécutée. L'application
  réellement testée de bout en bout est la version Node.js/SQLite.
* Pas d'authentification multi-utilisateurs ni de gestion fine des rôles dans l'interface web actuelle
  (un seul utilisateur implicite `admin`) — le modèle de données et l'architecture le permettent, mais
  l'écran de gestion des comptes utilisateurs n'est pas encore construit.
* Incoterms V1 uniquement : `EXW`, `FOB`, `CFR` (l'architecture des frais/valeur en douane est conçue
  pour être étendue à `FCA`, `FAS`, `CIF`, `CPT`, `CIP`, `DAP`, `DPU`, `DDP`).
* Poids/volume restent optionnels : les méthodes de répartition « Par poids »/« Par volume » nécessitent
  que ces champs soient renseignés sur les lignes concernées, sinon l'allocation est bloquée avec un
  message explicite.
* Gestion mono-conteneur (V2 prévue : multi-conteneurs, groupage LCL/FCL).
* Pas de reconnaissance OCR de factures/PDF fournisseur (V2 prévue).

### Roadmap recommandée
1. Compiler et exécuter la solution .NET 8 sur un poste disposant du SDK, puis migrer le moteur métier
   Node.js vers celui-ci si un client Windows natif est requis (le code JS peut servir de spécification
   exécutable pour la portée fonctionnelle exacte).
2. Authentification utilisateurs + rôles (Administrateur / Utilisateur / Consultation) sur l'API REST.
3. Incoterms `CIF`, `FCA`, `CPT`, `CIP`, `DAP`, `DDP` avec déductions Art. 16 octies § 3 CDA.
4. Multi-conteneurs et gestion documentaire (Packing List, B/L, certificats d'origine).
5. Export comptable SCF (écritures d'entrée en stock).

---

## 8. Tests

* **Tests de bout en bout (Node)** : `tests/e2e/run_e2e_workflow_test.sh` — 13 vérifications sur le
  pipeline complet Excel → mapping → règles → frais → calcul → anomalies → exports.
* **Tests unitaires C# (non exécutés faute de SDK)** : `tests/ImportCostAlgeria.UnitTests` — 28 tests
  xUnit couvrant les Sections 42 (V1.0), et les workflows V1.1/V1.2 (régimes préférentiels, workflow de
  validation réglementaire, classification IA).

---

## 9. Avertissement légal

CIMP est un outil d'aide au calcul. Les résultats produits doivent être vérifiés au regard de la
déclaration en détail et des documents douaniers officiels avant toute utilisation comptable, fiscale ou
déclarative. CIMP ne remplace pas l'expertise d'un commissionnaire en douane agréé.
