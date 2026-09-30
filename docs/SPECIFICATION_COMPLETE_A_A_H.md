# SPÉCIFICATION D'ARCHITECTURE ET DE MODÉLISATION (TÂCHE PRÉLIMINAIRE — SECTION 45 : A À H)
## Projet : ImportCost Algeria — Logiciel Professionnel de Calcul du Coût de Revient des Marchandises Importées en Algérie

---

## 0. CADRE JURIDIQUE ALGÉRIEN ET DOCUMENTATION DES FORMULES RÉGLEMENTAIRES

Conformément à la **Section 45**, chaque formule réglementaire structurante du moteur est rattachée à sa base juridique officielle algérienne :

### Formule 1 — Conversion en Dinars Algériens (DZD) des éléments exprimés en devises
```text
FORMULE            : Valeur_DZD = Arrondi_DZD( Valeur_Devise × (Taux_Change_Officiel_ALCES / Quotité_Devise) )
BASE JURIDIQUE     : Code des Douanes Algérien — Conversion des monnaies étrangères pour la détermination de la valeur en douane
SOURCE             : Journal Officiel de la République Algérienne (JORA n° 11 du 19 février 2017) & Portail officiel ALCES (Direction Générale des Douanes) / Banque d'Algérie
ARTICLE            : Article 16 decies et Article 103 de la Loi n° 79-07 du 21 juillet 1979 modifiée et complétée par la Loi n° 98-10 du 22 août 1998 et la Loi n° 17-04 du 16 février 2017
DATE D'APPLICATION : En vigueur à la date d'enregistrement de la déclaration en détail (Art. 16 decies § a & Art. 103 CDA)
VERSION            : CDA-2017.04 / ALCES
```

### Formule 2 — Détermination de la Valeur en Douane (Méthode principale : Valeur Transactionnelle)
```text
FORMULE            : Valeur_En_Douane_DZD = Prix_Effectivement_Payé_Ou_À_Payer_DZD
                                            + Adjonctions_Réglementaires_DZD (Art. 16 octies § 1 : transport, assurance, chargement/manutention jusqu'au lieu d'introduction dans le territoire douanier algérien, commissions de vente, emballages, apports...)
                                            - Déductions_Réglementaires_DZD (Art. 16 ter & Art. 16 octies § 3 : frais de transport/assurance postérieurs à l'arrivée au lieu d'introduction en Algérie, droits et taxes d'importation, commissions d'achat, rabais/remises/escomptes acquis avant dédouanement, sous réserve d'être distincts et quantifiables)
BASE JURIDIQUE     : Code des Douanes Algérien (accord relatif à la mise en œuvre de l'Article VII du GATT / OMC transposé en droit algérien)
SOURCE             : Journal Officiel de la République Algérienne (JORA n° 61 du 23 août 1998 — Loi n° 98-10 ; JORA n° 11 du 19 février 2017 — Loi n° 17-04) & Guide de la Valeur en Douane (DGD)
ARTICLE            : Articles 16, 16 bis, 16 ter (principe et déductions), 16 quater à 16 nonies (méthodes de substitution), et 16 octies § 1 e) et § 3 (adjonctions et exclusions au lieu d'introduction)
DATE D'APPLICATION : Loi n° 98-10 du 22/08/1998 consolidée par la Loi n° 17-04 du 16/02/2017
VERSION            : CDA-VAL-2017.1
```

### Formule 3 — Liquidation du Droit de Douane (DD) et principe de date d'effet
```text
FORMULE            : Droit_De_Douane_DZD = Arrondi_DZD( Valeur_En_Douane_DZD × Taux_DD_Applicable(Code_SH_10, Origine, Régime, Date_Référence) )
BASE JURIDIQUE     : Code des Douanes Algérien et Tarif Douanier à 10 chiffres (Système Harmonisé OMD 6 chiffres + 4 chiffres sous-positions nationales algériennes)
SOURCE             : Journal Officiel de la République Algérienne (Lois de Finances) & Tarif Douanier DGD / ALCES
ARTICLE            : Articles 6, 7, 9, 10, 14, 15 et 103 du Code des Douanes Algérien
DATE D'APPLICATION : Taux en vigueur à la date d'enregistrement de la déclaration en détail (Art. 103 CDA)
VERSION            : Versionnée dynamiquement dans [reg].[RegulatoryRules]
```

### Formule 4 — Liquidation du Droit Additionnel Provisoire de Sauvegarde (DAPS) et taxes assimilées
```text
FORMULE            : DAPS_DZD = Arrondi_DZD( Valeur_En_Douane_DZD × Taux_DAPS_Réglementaire(Code_SH_10, Origine, Date_Référence) )
BASE JURIDIQUE     : Institution du Droit Additionnel Provisoire de Sauvegarde applicable à certaines marchandises importées mises à la consommation en Algérie (liquidé et recouvré comme en matière de droit de douane)
SOURCE             : Journal Officiel de la République Algérienne (JORA n° 42 du 15 juillet 2018 — Loi n° 18-13 portant LFC 2018) et arrêtés/listes réglementaires périodiques publiés au JORA
ARTICLE            : Article 2 de la Loi n° 18-13 du 11 juillet 2018 portant Loi de Finances Complémentaire pour 2018
DATE D'APPLICATION : Selon la période de validité de la liste réglementaire publiée au JORA
VERSION            : Versionnée dynamiquement dans [reg].[RegulatoryRules]
```

### Formule 5 — Assiette et liquidation de la TVA à l'importation
```text
FORMULE            : Base_TVA_Importation_DZD = Valeur_En_Douane_DZD + Droit_De_Douane_DZD + Autres_Droits_Et_Taxes_Applicables_Hors_TVA_DZD (DAPS + TIC + RDAE + taxes spécifiques incluses dans l'assiette)
                     TVA_Importation_DZD      = Arrondi_DZD( Base_TVA_Importation_DZD × Taux_TVA_Réglementaire(Code_SH_10, Régime, Date_Référence) )
BASE JURIDIQUE     : Code des Taxes sur le Chiffre d'Affaires (CTCA) — Assiette de la TVA à l'importation (« La base imposable est constituée par la valeur en douane tous droits et taxes inclus, à l'exclusion de la taxe sur la valeur ajoutée »)
SOURCE             : Direction Générale des Impôts (DGI) / Ministère des Finances / JORA (Ordonnance n° 76-102 modifiée et complétée par les Lois de Finances)
ARTICLE            : Article 19 (base imposable à l'importation), Articles 10 et 11 (exonérations à l'importation), Articles 21 et 23 (taux normal et taux réduit), Article 105 (perception comme en matière de douane) du CTCA
DATE D'APPLICATION : Taux en vigueur à la date d'enregistrement de la déclaration en détail
VERSION            : Versionnée dynamiquement dans [reg].[RegulatoryRules]
```

### Formule 6 — Coût d'Acquisition et Coût de Revient Économique Réel (Séparation Coût Douanier / Coût Économique)
```text
FORMULE            : 1) RÉSULTAT DOUANIER :
                        Total_Droits_Et_Taxes_DZD = Droit_De_Douane_DZD + Taxes_Et_Prélèvements_Applicables_DZD + TVA_Importation_DZD
                        Coût_Douanier_Dédouané_DZD = Valeur_En_Douane_DZD + Total_Droits_Et_Taxes_DZD
                     2) RÉSULTAT ÉCONOMIQUE (COÛT DE REVIENT) :
                        Coût_Acquisition_Hors_TVA_DZD = Valeur_Achat_DZD + Frais_Approche_Internationaux_Et_Locaux_Retenus_DZD + Droit_De_Douane_DZD + Taxes_Non_Récupérables_DZD
                        Coût_Total_De_Revient_DZD     = Coût_Acquisition_Hors_TVA_DZD + (Paramètre_TVA_Non_Récupérable ? TVA_Importation_DZD : 0)
                        Coût_Unitaire_De_Revient_DZD  = Coût_Total_De_Revient_Alloué_Ligne_DZD / Quantité_Ligne
BASE JURIDIQUE     : Système Comptable Financier Algérien (SCF) — Évaluation du coût d'entrée des stocks importés + Choix du modèle métier (Section 24 : TVA d'importation intégrée au coût de revient en tant que charge non récupérable par défaut, paramétrable par entreprise)
SOURCE             : Journal Officiel de la République Algérienne (JORA n° 74 du 25 novembre 2007 — Loi n° 07-11 portant SCF ; JORA n° 19 du 25 mars 2009 — Arrêté du 26 juillet 2008)
ARTICLE            : Articles 121-3 et 123-1 de l'Arrêté du 26 juillet 2008 fixant les règles d'évaluation et de comptabilisation du SCF
DATE D'APPLICATION : Permanent (Loi n° 07-11) + Paramètre d'entreprise [IsImportVatNonRecoverable]
VERSION            : SCF-2008 / Métier V1.0
```

---

## A. MODÈLE DE DONNÉES COMPLET

Le modèle de données est organisé en **6 domaines (schémas)** garantissant l'isolation multi-entreprise (`CompanyId`), le versionnage réglementaire sans écrasement et l'auditabilité intégrale :

1. **Domaine `core` (Multi-Entreprise, Utilisateurs, Référentiels, Dossiers d'Importation)** :
   - `Company` : Identité légale algérienne (`NIF`, `NIS`, `RC`, `AI`), devise fonctionnelle (`DZD`), paramètre `IsImportVatNonRecoverable` (`true` par défaut conformément à la Section 24).
   - `User` : Rôles `ADMINISTRATEUR`, `UTILISATEUR`, `CONSULTATION` rattachés à une entreprise.
   - `Country`, `Currency`, `ExchangeRate` (versionné par dates `ValidFrom`/`ValidTo`, type `OFFICIEL_DOUANE_ALCES` / `MANUEL_EXCEPTIONNEL`, source et date de récupération).
   - `Incoterm` & `IncotermFieldRule` : `EXW`, `FOB`, `CFR` actifs en V1 + `FCA`, `FAS`, `CIF`, `CPT`, `CIP`, `DAP`, `DPU`, `DDP` prévus en base ; pilotage dynamique des frais attendus par Incoterm.
   - `Supplier` & `Product` : Base produits réutilisable par entreprise (`Reference`, `Designation`, `ConfirmedHsCode10`, `HabitualOriginCountry`, `MeasurementUnit`, poids/volume facultatifs).
   - `ImportOperation` : Entête du dossier d'importation (`CompanyId`, `ImportNumber`, `ReferenceDate`, `ExportShippingCountryIso2` saisi une seule fois, `DefaultOriginCountryIso2`, `MainCurrencyCode`, `AppliedExchangeRateToDzd`, `IsManualExchangeRate`, `IncotermCode`, `CustomsRegimeCode`, `ArrivalPortOrBorder`, `TransportMode`, `IsSimulation`).
   - `ImportContainer` : Relation `1..N` vers `ImportOperation` (bridée à 1 conteneur/transport global en saisie V1, prête pour le multi-conteneurs en V2 sans migration destructive).
   - `ImportLine` : Lignes d'articles (`LineNumber`, `ProductReference`, `Designation`, `Quantity`, `UnitPurchasePriceCurrency`, `CurrencyCode`, `HsCodeExcel`, `HsCodeConfirmed10`, `AiProposedHsCode10`, `AiHsValidationStatus`, `OriginCountryIso2` par ligne, `ExcelDutyRatePercent`, `RegulatoryDutyRatePercent`, `DutyComparisonStatus`, `LineGrossWeightKg`, `LineVolumeM3`).
   - `ImportFee` : Frais d'importation prédéfinis ou personnalisés (`FeeCategoryCode`, `CustomFeeName`, `AmountInCurrency`, `CurrencyCode`, `AmountDzd`, `AllocationMethod`, `IncludeInCustomsValue`, `CustomsValuationTreatment`, `IncludeInCostOfGoods`).

2. **Domaine `reg` (Moteur Réglementaire Versionné)** :
   - `LegalSource` : Sources juridiques classées selon la hiérarchie stricte de niveau 1 à 6 (Section 20).
   - `RegulatoryVersion` : Enveloppe de version réglementaire (`VersionCode`, `EffectiveFrom`, `EffectiveTo`, `Status`).
   - `HsTariffCode` : Nomenclature tarifaire algérienne à 10 chiffres versionnée.
   - `CustomsRegime` : Régimes douaniers et accords préférentiels.
   - `RegulatoryRule` : Règle fiscale/douanière versionnée (`RuleCode`, `SupersedesRuleId`, `RuleType`, `TaxCode`, `HsCodePattern`, `OriginCountryIso2`, `CustomsRegimeCode`, `RatePercent`, `CalculationBaseCode`, `ConditionExpression`, `ValidFrom`, `ValidTo`, `Priority`, `LegalSourceId`, `LegalArticleRef`, `JoraReference`, `Status`, `ValidatedByAdminId`).

3. **Domaine `excel` (Import Excel & Modèles de Mapping)** :
   - `ColumnHeaderSynonym` : Dictionnaire de synonymes normalisés.
   - `ExcelMappingTemplate` : Modèle de mapping sauvegardé par fournisseur (`FOURNISSEUR_X`, hash de structure d'en-têtes `HeaderSignatureHash`, mapping JSON des colonnes).

4. **Domaine `calc` (Moteur de Calcul, Ventilation & Anomalies)** :
   - `CalculationRun` : En-tête d'exécution de calcul avec les totaux consolidés et la version réglementaire utilisée.
   - `CalculationLineResult` : Résultats détaillés par ligne séparant explicitement le **Coût Douanier** et le **Coût de Revient Économique**.
   - `CalculationLineTaxDetail` : Traçabilité de chaque droit/taxe liquidé par ligne avec la référence de la règle `RegulatoryRuleId` et l'article de loi.
   - `CalculationFeeAllocation` : Historique de répartition de chaque frais par ligne (méthode utilisée, ratio, montant DZD alloué).
   - `CalculationAnomaly` : Anomalies détectées (`INFO`, `AVERTISSEMENT`, `ERREUR`, `BLOCAGE`).

5. **Domaine `ai` (Propositions IA soumises à validation humaine)** :
   - `HsClassificationProposal` : Proposition de code SH par IA avec score de confiance, justification, règle générale d'interprétation (RGI) et décision obligatoire de l'utilisateur (`CONFIRMED`, `MODIFIED`, `REJECTED`).

6. **Domaine `audit` (Traçabilité immuable)** :
   - `AuditLog` : Journalisation de chaque action, changement de règle, saisie de taux manuel, validation SH ou simulation.

---

## B. SCHÉMA SQL

Le script DDL SQL Server complet est disponible dans `database/01_ImportCostAlgeria_Schema.sql` et le script de paramétrage initial dans `database/02_Seed_Referentiel_Structure.sql`.

---

## C. ARCHITECTURE DES PROJETS C#

La solution `ImportCostAlgeria.sln` suit une architecture Clean / Modulaire stricte où `CalculationEngine` et `RegulatoryEngine` ne dépendent d'aucune bibliothèque d'interface graphique :

- `src/ImportCostAlgeria.Core` : Entités métier, Value Objects (`Money`), énumérations et contrats de domaine.
- `src/ImportCostAlgeria.RegulatoryEngine` : Moteur réglementaire versionné, hiérarchie des sources juridiques, workflow de validation administrateur.
- `src/ImportCostAlgeria.CalculationEngine` : `CurrencyCalculator`, `CustomsValueCalculator`, `CostAllocationEngine`, `ImportCalculationOrchestrator` (calcul douanier vs coût de revient économique, contrôle des anomalies).
- `src/ImportCostAlgeria.ExcelEngine` : `ExcelColumnDetectorAndMapper` (normalisation des en-têtes, reconnaissance automatique, invites de mapping interactif, modèles par fournisseur).
- `src/ImportCostAlgeria.Audit` : Service de journalisation immuable des calculs, modifications réglementaires et actions utilisateur.
- `src/ImportCostAlgeria.AI` : Interfaces et services d'analyse assistée (`HSClassifier`, `RegulatoryAssistant`, `ImportSimulator`) avec marquage strict de l'origine des données (`DONNÉE OFFICIELLE`, `DONNÉE UTILISATEUR`, `CALCUL DU LOGICIEL`, `PROPOSITION IA`).
- `src/ImportCostAlgeria.Reporting` : Générateurs d'exports Excel (feuille Détail + feuille `RÉCAPITULATIF`) et rapports PDF avec mention légale obligatoire.
- `src/ImportCostAlgeria.Database` : `DbContext` Entity Framework Core 8 (SQL Server), configurations Fluent API et migrations.
- `src/ImportCostAlgeria.Presentation` : Couche UI Windows (V1) découplée communiquant via les services applicatifs, permettant une transition ultérieure vers Web / SaaS sans modifier les moteurs.
