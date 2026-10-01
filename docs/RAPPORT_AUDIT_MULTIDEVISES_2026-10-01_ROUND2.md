# Rapport — 2ᵉ revue de l'audit multi-devises EUR/USD/DZD (CIMP)

**Date** : 2026-10-01
**Branche** : `arena/01a0f335-cimp`
**Commit de référence pour ce round** : `e28b5bf` (après `408dba0`, fin du 1ᵉʳ round)
**Portée** : corrections ciblées sur les 14 points de la 2ᵉ revue, sans redémarrage du projet, sans
changement d'architecture générale, sans suppression de fonctionnalité existante.

> ⚠️ Ce rapport ne déclare PAS le projet "terminé". Il documente précisément ce qui a été corrigé, ce qui a
> été vérifié sans modification de code (car déjà correct), et ce qui reste strictement impossible à exécuter
> dans ce bac à sable (build/test réels sous Visual Studio / dotnet Windows).

---

## 1. Corrections effectuées (code modifié)

### 1.1 Section 1 — Gabarit du message IA "fret +20 %"
**Fichier** : `src/ImportCostAlgeria.AI/AdvancedAiModules.cs`

Avant : un message en une seule phrase ("...passerait de X DZD à Y DZD / unité.").
Après : gabarit EXACT à 4 lignes, conforme à la demande :

```
Article <réf> (coût de revient unitaire) :
Situation actuelle : X DZD
Après +20 % de fret : Y DZD
Différence : Z DZD
Variation : N %
```

- `Situation actuelle` = `baseLine.EconomicOutcome.UnitCostOfGoodsDzd` (calcul réel, `currentCalculation`,
  JAMAIS modifié).
- `Après +20 % de fret` = `simLine.EconomicOutcome.UnitCostOfGoodsDzd` (clone simulé,
  `sim.SimulatedCalculation`).
- `Différence` = `CurrencyCalculator.RoundDzd(après - avant)`.
- `Variation` = pourcentage calculé à partir de la différence, arrondi à 2 décimales.
- Confirmation : la direction avant/après était **déjà correcte** avant cette revue (pas de bug d'inversion
  `simLine` utilisé des deux côtés) — seul le **format du message** ne respectait pas le gabarit demandé. Le
  code contient désormais un commentaire explicite rappelant cette règle pour éviter toute régression future.

### 1.2 Section 2 — Séparation de l'arrondi commercial et de l'arrondi DZD
**Fichier** : `src/ImportCostAlgeria.CalculationEngine/ImportCalculationOrchestrator.cs`

- Nouvelle classe **`CurrencyRounding`** (fonction générique `Round(decimal amount, string? currencyCode)`),
  centralisée dans le moteur de calcul, avec une table de décimales par devise (EUR=2, USD=2, DZD=2,
  extensible). Aucune règle d'arrondi n'est dupliquée ailleurs.
- `CurrencyCalculator.RoundDzd(amount)` délègue maintenant à `CurrencyRounding.Round(amount, "DZD")` — son
  usage reste **exclusivement réservé** aux montants réellement exprimés en DZD.
- **4 usages corrigés** qui arrondissaient à tort un montant EUR/USD via `RoundDzd` :
  1. `CurrencyConversionService.Convert` — le montant converti est désormais arrondi selon la devise
     **cible** (`toCurrencyCode`), pas selon DZD.
  2. `purchaseCurrency` (montant ligne dans la devise d'origine de la facture, ex: EUR) — arrondi selon
     `line.CurrencyCode`.
  3. `lineAuthorizationTotal` (montant d'autorisation par ligne, ex: USD) — arrondi selon
     `operation.AuthorizationCurrencyCode`.
  4. `totalAuthorizationAmount` (montant d'autorisation agrégé) — arrondi selon
     `operation.AuthorizationCurrencyCode`.
- **Fichier `src/ImportCostAlgeria.Reporting/ExcelAndPdfReportGenerator.cs`** : 2 usages de
  `Math.Round(x, 2, ...)` dupliquant localement la règle d'arrondi DZD ont été remplacés par un appel à
  `CurrencyCalculator.RoundDzd(...)`, pour que le moteur de calcul reste la seule source de vérité de la
  règle d'arrondi (aucune duplication, y compris côté rapports).
- **Vérification explicite (grep exhaustif)** : aucun ViewModel (`src/ImportCostAlgeria.Presentation/ViewModels/*.cs`)
  ne contient de `Math.Round` / `RoundDzd` / `CurrencyRounding` — toutes les valeurs affichées (ex:
  `AuthorizationUnitPrice`, `AuthorizationTotalAmount`, `AuthorizationCurrencyLabel`) sont de simples
  propriétés calculées en lecture seule qui exposent directement le résultat déjà arrondi par le moteur de
  calcul. Aucune règle d'arrondi n'existe en double dans la couche présentation.

---

## 2. Points vérifiés SANS modification de code (déjà conformes)

| # | Point de la revue | Constat |
|---|---|---|
| 3 | EUR→USD bout en bout (facture → taux → USD → autorisation) | Chaîne déjà cohérente moteur/modèle/ViewModel/rapport (voir tests ajoutés §3). |
| 4 | Ne jamais chaîner EUR→USD→DZD | `CalculateLineCustomsValueDzd` utilise exclusivement `linePurchaseValueDzd`, lui-même issu de `line.CurrencyCode` → taux réglementaire direct (`_currencyCalculator.ResolveRate`). Le service commercial (`CurrencyConversionService`) est un chemin de code totalement distinct, jamais invoqué dans le calcul douanier. |
| 7 | Quotité (QuotityUnit) sur taux réglementaire ET commercial | `ExchangeRateNormalization.ToUnitRate` est **partagé** par `CurrencyCalculator.ResolveRate` (réglementaire) et `CurrencyConversionService.ResolveCrossRate` (commercial) — la normalisation `100 XXX = Y DZD → 1 XXX = Y/100` s'applique identiquement aux deux chemins. Test dédié ajouté (§3) pour le chemin commercial. |
| 8 | Rapports Excel/PDF : Devise facture / Montant original / Taux / Montant autorisation / Valeur douanière séparés | Déjà implémenté (round 1) : `ExcelReportFinancialSummaryRow` + `PdfReportWriter` affichent `DeviseOriginale`, `MontantOriginal`, `DeviseAutorisation`, `TauxChangeAutorisation`, `MontantAutorisation` à côté — jamais à la place — de `TotalValeurDouaneDzd`. Aucune régression introduite par les corrections d'arrondi (mêmes valeurs numériques pour EUR/USD/DZD à 2 décimales). |
| 9 | Inspection WPF (champs morts, doubles conversions, libellés erronés) | Inspection ciblée de `ImportDetailView.xaml`, `ImportationsView.xaml`, `TauxDeChangeView.xaml` : tous les `Command="{Binding XCommand}"` correspondent à une commande réellement implémentée dans le ViewModel associé (vérifié par grep croisé). Aucune commande orpheline trouvée. Les libellés distinguent explicitement taux réglementaire (`ExchangeRateInfo`) et taux commercial (`AuthorizationExchangeRateInfo`), avec alerte `⚠️ TAUX MANUEL` déjà en place des deux côtés. |
| 10 | Modèle de données (pas de propriété redondante) | `LineFullCalculationResult` / `LineEconomicCostResult` / `LineCommercialConversion` / `CommercialAuthorizationConversion` couvrent déjà, sans doublon : Devise originale (`CurrencyCode`), Montant original (`PurchaseValueCurrency`), Devise autorisation (`AuthorizationCurrencyCode`), Taux commercial (`EffectiveRate`), Montant autorisation (`AuthorizationTotalAmount`), Taux réglementaire (`AppliedExchangeRateToDzd`), Montant DZD (`PurchaseValueDzd`). Aucune propriété ajoutée — vérification uniquement. |

---

## 3. Tests ajoutés (`tests/ImportCostAlgeria.UnitTests/MultiCurrencyAndSecurityTests.cs`)

Tous les scénarios numériques EXACTS demandés par la revue sont désormais couverts par un test dédié :

1. **`EurToUsd_ExactScenario_10000Eur_At_1_17_ShouldEqual_11700Usd`**
   10 000 EUR × 1,17 = **11 700 USD** — et confirmation explicite que `OriginalAmount` (10 000 EUR) reste
   toujours récupérable tel quel.
2. **`FullCalculation_ProductLevel_Qty100_PU50Eur_Rate1_17_ShouldProduce_5000Eur_And_5850Usd`**
   Qté=100, PU=50 EUR, taux=1,17 → Total EUR = **5 000**, PU USD = **58,50**, Total USD = **5 850**
   (vérifié à la fois au niveau ligne et au niveau agrégé facture).
3. **`ManualAuthorizationRate_10000Eur_OfficialVsManual_ShouldApply1_20_NotOfficial1_17`**
   Taux officiel 1,17 / taux manuel 1,20 → 10 000 EUR × 1,20 = **12 000 USD**, alerte `⚠️ TAUX MANUEL`
   levée, et le taux officiel (1,17) reste interrogeable séparément et intact.
4. **`RateHistorization_EurToUsd_ImportDated01Oct_ShouldKeep1_17_EvenAfter1_19PublishedFor15Oct`**
   Publication 01/10/2026 → 1,17 puis 15/10/2026 → 1,19 : un calcul référencé au 01/10 reste **figé à
   1,17** même après la publication du 15/10 (re-vérifié deux fois dans le test, avant et après coup pour
   écarter tout effet de bord).
5. **`CurrencyConversionService_QuotityUnit100_ShouldNormalizeToPerUnitRate_ForCommercialConversion`**
   Quotité=100 appliquée à une conversion **commerciale** (pas seulement réglementaire).
6. **`FullCalculation_DirectDzdInvoice_ShouldNotProduceUselessUsdConversion`**
   Facture directement en DZD → aucune conversion USD générée (`CommercialAuthorizationConversion == null`).
7. Renforcement de **`AiFreightSimulation_PlusTwentyPercent_...`** : assertions explicites sur les 4 lignes
   du gabarit (`Situation actuelle :`, `Après +20 % de fret :`, `Différence :`, `Variation :`) et
   `Assert.NotEqual(beforeUnitCost, afterUnitCost)` pour garantir que les deux valeurs diffèrent réellement.

Tests déjà existants (round 1) qui couvrent aussi des exigences de ce round et restent valides sans
modification : conversion EUR→USD indépendante du taux DZD, taux manuel avec seuil d'avertissement,
absence de conversion inutile quand facture déjà dans la devise d'autorisation (cas USD→USD), historisation
ne clôturant pas une paire de devises différente, Quotité=1 et Quotité=100 côté réglementaire.

---

## 4. Résultats `dotnet restore / build / test`

| Étape | Statut |
|---|---|
| `dotnet restore` | **NON DISPONIBLE** |
| `dotnet build` | **NON DISPONIBLE** |
| `dotnet test` | **NON DISPONIBLE** |

**Raison** (reconfirmée ce round, aucun changement depuis le 1ᵉʳ round) : ce bac à sable ne dispose d'aucun
SDK .NET installé et d'aucun accès réseau sortant (`apt-get`, `curl` vers `dot.net`, `dotnetcli.*`,
`deb.debian.org`, `google.com` : tous échouent sans réponse ou "Connection failed"). Il est donc
**techniquement impossible** d'exécuter un build/test réel ici. Les corrections ont été validées
uniquement par **relecture statique rigoureuse du code** et par **raisonnement manuel exhaustif** sur
chaque test ajouté (valeurs attendues recalculées à la main et comparées ligne à ligne à la logique du
moteur de calcul). Cela ne constitue PAS une preuve d'exécution réelle — seul un `dotnet test` sous
Visual Studio / Windows avec le SDK installé peut confirmer que ces tests passent effectivement.

---

## 5. Fichiers modifiés (ce round)

- `src/ImportCostAlgeria.AI/AdvancedAiModules.cs` — gabarit du message IA fret +20 % (Section 1).
- `src/ImportCostAlgeria.CalculationEngine/ImportCalculationOrchestrator.cs` — `CurrencyRounding`
  générique + 4 corrections d'arrondi par devise (Section 2).
- `src/ImportCostAlgeria.Reporting/ExcelAndPdfReportGenerator.cs` — suppression de 2 duplications locales
  de la règle d'arrondi DZD (Section 2, extension aux rapports).
- `tests/ImportCostAlgeria.UnitTests/MultiCurrencyAndSecurityTests.cs` — 6 nouveaux tests + renforcement
  d'un test existant (Sections 1, 3, 5, 6, 7, 11).
- `docs/RAPPORT_AUDIT_MULTIDEVISES_2026-10-01_ROUND2.md` — ce rapport.

**Aucun autre fichier n'a été modifié** : Excel import, mapping, gestion société/produit, Incoterms,
moteur réglementaire, audit, rôles utilisateurs, WebPreview n'ont pas été touchés, conformément à la
consigne explicite de ne pas élargir le périmètre.

---

## 6. Points nécessitant encore Visual Studio / Windows

1. **Compilation réelle (`dotnet build`) du projet WPF** (`ImportCostAlgeria.Presentation`,
   `net8.0-windows`) — impossible dans ce bac à sable Linux sans SDK ni réseau. À faire sous Visual Studio
   Windows avec le SDK .NET 8.
2. **Exécution réelle de `dotnet test`** sur `tests/ImportCostAlgeria.UnitTests` (xUnit) — notamment les 6
   nouveaux tests de ce round — pour confirmer qu'ils passent effectivement (la logique a été vérifiée
   manuellement mais jamais exécutée par un compilateur/runtime réel).
3. **Vérification visuelle de l'écran "Taux de change & Incoterm"** (onglet autorisation d'importation) en
   conditions réelles WPF (rendu XAML, visibilité des `CheckBox`/`TextBlock` conditionnels) — l'inspection
   faite ici est uniquement textuelle (lecture du XAML), pas un rendu réel.
4. **Test d'intégration bout en bout avec base SQLite réelle** sous Windows pour confirmer la persistance
   des nouveaux champs de conversion commerciale en conditions de production (le test
   `RateHistorization_EurToUsd_...` utilise SQLite via `Microsoft.Data.Sqlite`, dont le comportement exact
   n'a pas pu être vérifié par exécution dans ce bac à sable).

---

## 7. Confirmation explicite demandée par la revue

✅ **Le scénario `10 000 EUR × 1,17 = 11 700 USD` est bien implémenté et testé** :
- Le service centralisé `CurrencyConversionService.Convert(10000m, "EUR", "USD", date, null)` avec un taux
  officiel 1,17 renvoie `ConvertedAmount = 11700m` (arrondi désormais via `CurrencyRounding.Round(..., "USD")`,
  jamais via `RoundDzd`).
- Le test `EurToUsd_ExactScenario_10000Eur_At_1_17_ShouldEqual_11700Usd` vérifie ce résultat exact.
- Le test produit `FullCalculation_ProductLevel_Qty100_PU50Eur_Rate1_17_ShouldProduce_5000Eur_And_5850Usd`
  confirme la cohérence au niveau ligne (100 × 50 EUR = 5 000 EUR, 58,50 USD/unité, 5 850 USD au total).

✅ **Le montant original de 10 000 EUR reste TOUJOURS récupérable tel quel, jamais remplacé par sa valeur
USD convertie** :
- `CurrencyConversionOutcome.OriginalAmount` conserve explicitement le montant source (10 000) à côté du
  montant converti (`ConvertedAmount`) — les deux coexistent dans le même objet retourné, aucun champ n'est
  écrasé.
- Au niveau facture complète, `CommercialAuthorizationConversion.OriginalTotalAmount` (EUR) et
  `CommercialAuthorizationConversion.AuthorizationTotalAmount` (USD) sont deux propriétés **distinctes** du
  même enregistrement — le modèle ne permet structurellement pas à l'un d'écraser l'autre.
- Dans les rapports Excel/PDF, `MontantOriginal`/`DeviseOriginale` (EUR) sont affichés à côté de
  `MontantAutorisation`/`DeviseAutorisation` (USD), jamais l'un à la place de l'autre (vérifié dans
  `ExcelWorkbookWriter.cs` et `PdfReportWriter.cs`).
- Ce comportement est couvert par le test `EurToUsd_ExactScenario_10000Eur_At_1_17_ShouldEqual_11700Usd`
  (`Assert.Equal(10000m, outcome.OriginalAmount)`) et par
  `FullCalculation_ShouldExposeCommercialUsdConversion_SeparatelyFromRegulatoryDzdValue` (round 1, toujours
  valide).

---

## 8. Conclusion de ce round

Les corrections demandées (Sections 1 et 2) ont été appliquées, et les vérifications demandées (Sections 3
à 11) ont été effectuées — soit par confirmation que le code existant était déjà conforme, soit par ajout
de tests couvrant explicitement les scénarios chiffrés exigés. **Le projet n'est pas déclaré "terminé"** :
la validation reste basée sur une relecture statique et un raisonnement manuel des tests, faute de SDK/
réseau dans ce bac à sable ; une exécution réelle de `dotnet build`/`dotnet test` sous Visual Studio Windows
reste nécessaire avant toute mise en production.
