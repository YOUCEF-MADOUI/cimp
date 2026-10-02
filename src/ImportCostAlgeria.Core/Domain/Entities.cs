using System;
using System.Collections.Generic;

namespace ImportCostAlgeria.Core.Domain;

// ============================================================================
// 1. ÉNUMÉRATIONS MÉTIER & RÉGLEMENTAIRES
// ============================================================================

public enum UserRole
{
    Administrateur,
    Utilisateur,
    Consultation
}

public enum IncotermCode
{
    // Actifs en V1
    EXW,
    FOB,
    CFR,
    // Prévus dans l'architecture pour les versions futures
    FCA,
    FAS,
    CIF,
    CPT,
    CIP,
    DAP,
    DPU,
    DDP
}

public enum FeeAllocationMethod
{
    ByValue,        // Par valeur
    ByQuantity,     // Par quantité
    ByWeight,       // Par poids (facultatif en V1)
    ByVolume,       // Par volume (facultatif en V1)
    FixedAmount,    // Montant fixe
    Percentage,     // Pourcentage
    Manual          // Manuelle
}

public enum CustomsValuationMethod
{
    TransactionValue_Art16Ter,          // Méthode principale (Art. 16 ter CDA)
    IdenticalGoods_Art16Quater,         // Marchandises identiques (Art. 16 quater CDA)
    SimilarGoods_Art16Quinquies,        // Marchandises similaires (Art. 16 quinquies CDA)
    DeductiveValue_Art16Sexies,         // Méthode déductive (Art. 16 sexies CDA)
    ComputedValue_Art16Septies,         // Valeur calculée (Art. 16 septies CDA)
    FallbackReasonableMeans_Art16Nonies // Moyens raisonnables / dernier recours (Art. 16 nonies CDA)
}

public enum CustomsAdjustmentTreatment
{
    IncludedInInvoicePrice,     // Déjà inclus dans le prix facturé (ex: Fret en CFR)
    Addition_Art16Octies,       // Éléments à ajouter au prix payé ou à payer (Art. 16 octies §1 CDA)
    Deduction_Art16Ter_Octies3, // Éléments exclus / à déduire sous conditions (Art. 16 ter & 16 octies §3 CDA)
    PostIntroductionExcluded    // Frais encourus après le lieu d'introduction en Algérie (exclus de la VD, inclus au coût de revient)
}

public enum LegalSourceHierarchyLevel : byte
{
    Level1_JournalOfficielJora = 1,
    Level2_DouanesDgdAlces = 2,
    Level3_MinistereFinancesDgi = 3,
    Level4_TextesReglementairesOfficiels = 4,
    Level5_AutresSourcesInstitutionnelles = 5,
    Level6_SourcesSecondairesAideUniquement = 6
}

public enum RegulatoryRuleStatus
{
    Detected,
    AiAnalyzed,
    Proposed,
    AdminVerified,
    PublishedNewVersion,
    Archived,
    Rejected
}

public enum RegulatoryRuleType
{
    CustomsDuty,    // Droit de douane (DD)
    Vat,            // Taxe sur la Valeur Ajoutée (TVA)
    Daps,           // Droit Additionnel Provisoire de Sauvegarde
    Tic,            // Taxe Intérieure de Consommation
    Rdae,           // Redevance / Prélèvement douanier
    SpecificTax,    // Taxes spécifiques / parafiscales
    Exemption       // Exonération / Franchise (ANDI/AAPI, Accords préférentiels...)
}

public enum TaxableBaseType
{
    CustomsValueDzd,                            // Valeur en douane (assiette DD, DAPS, CS...)
    CustomsValuePlusDutiesAndTaxesExVatDzd,     // Art. 19 CTCA : Valeur en douane + Tous droits et taxes hors TVA
    PhysicalQuantityOrWeight,                   // Assiette spécifique (kg, litre, unité)
    /// <summary>
    /// Revue du 2026-10-02 (cas de référence D10 réel) : Valeur en douane + taxes calculées AVANT la TVA
    /// (ex: CS) + TVA elle-même. Observée pour le PRCT sur le D10 de référence (SARL HYMA TRADE) :
    /// assiette article 1 = 2 079 857 (VD) + 62 395,71 (CS) + 407 028,01 (TVA) = 2 549 280,72, taxée à 2 %.
    /// Une règle déclarant cette assiette est nécessairement calculée APRÈS la TVA par
    /// ImportCalculationOrchestrator (voir le passage "taxes après TVA" de ExecuteCalculation) — jamais
    /// imposée universellement à toutes les taxes additionnelles (chaque RegulatoryRule choisit librement
    /// son CalculationBase parmi les valeurs de cet enum).
    /// </summary>
    CustomsValuePlusPriorTaxesPlusVatDzd
}

public enum DutyComparisonStatus
{
    Match,                                  // ✓ Correspondance (Droit Excel == Droit réglementaire)
    Difference,                             // ⚠️ DIFFÉRENCE (Droit Excel != Droit réglementaire)
    RegulatoryNotFoundPendingConfirmation,  // INFORMATION NON DÉTERMINÉE -> Attente confirmation utilisateur
    ExcelFallbackConfirmedByUser,           // Taux Excel utilisé après confirmation explicite et tracée
    /// <summary>
    /// Revue du 2026-10-02 (correction urgente — ne plus bloquer le calcul faute de RegulatoryRule) :
    /// aucune règle réglementaire ni taux Excel confirmé n'étaient disponibles pour cet article, mais
    /// l'importation autorise l'usage des taux de taxes PAR DÉFAUT (<see cref="ImportOperation.UseDefaultRatesWhenRuleMissing"/>)
    /// — le taux appliqué est donc <see cref="ImportOperation.DefaultDdRatePercent"/>, jamais présenté
    /// comme un taux réglementaire confirmé : un AVERTISSEMENT (jamais un blocage) est systématiquement
    /// généré et le résultat doit être vérifié avant toute utilisation définitive.
    /// </summary>
    DefaultImportRateUsed
}

public enum AiProposalDecision
{
    NotApplicable,
    PendingUserValidation, // Proposition IA en attente : ne modifie jamais automatiquement le code SH définitif
    ConfirmedByUser,       // [CONFIRMER]
    ModifiedByUser,        // [MODIFIER]
    RejectedByUser         // [REFUSER]
}

public enum AnomalySeverity
{
    Info,
    Avertissement,
    Erreur,
    Blocage
}

public enum DataOriginTag
{
    DonneeOfficielle,
    DonneeUtilisateur,
    CalculDuLogiciel,
    PropositionIa,
    /// <summary>
    /// Revue du 2026-10-02 (correction urgente — ne plus bloquer le calcul faute de RegulatoryRule) :
    /// valeur issue des TAUX DE TAXES PAR DÉFAUT configurés au niveau de l'importation
    /// (<see cref="ImportOperation.DefaultDdRatePercent"/>, <see cref="ImportOperation.DefaultCsRatePercent"/>,
    /// <see cref="ImportOperation.DefaultPrctRatePercent"/>, <see cref="ImportOperation.DefaultTvaRatePercent"/>,
    /// <see cref="ImportOperation.DefaultTcsRatePercent"/>), utilisée UNIQUEMENT lorsqu'aucune règle
    /// réglementaire officielle n'a été trouvée ET que <see cref="ImportOperation.UseDefaultRatesWhenRuleMissing"/>
    /// est actif. Statut affiché à l'écran : "VALEUR PAR DÉFAUT (IMPORTATION) — NON VÉRIFIÉE".
    /// </summary>
    ValeurParDefautImportation,
    /// <summary>
    /// Revue du 2026-10-02 : une règle réglementaire officielle en vigueur déclare EXPLICITEMENT que
    /// cette taxe ne s'applique pas à ce code SH/cette origine/cette période (fait réglementaire sourcé,
    /// jamais une supposition du logiciel) — à distinguer d'une absence de donnée.
    /// </summary>
    NonApplicable
}

/// <summary>
/// Libellé FR du statut d'une donnée fiscale/réglementaire appliquée (Section 11 de la correction du
/// 2026-10-02) : REGLEMENTAIRE / DEFAULT_IMPORT / MANUEL / NON_APPLICABLE / NON_DETERMINE. Centralisé ICI
/// pour que l'écran et les rapports affichent toujours exactement le même libellé pour la même origine.
/// </summary>
public static class DataOriginTagLabels
{
    public static string ToStatusLabelFr(this DataOriginTag tag) => tag switch
    {
        DataOriginTag.DonneeOfficielle => "RÉGLEMENTAIRE",
        DataOriginTag.ValeurParDefautImportation => "VALEUR PAR DÉFAUT (IMPORTATION)",
        DataOriginTag.DonneeUtilisateur => "MANUEL",
        DataOriginTag.NonApplicable => "NON APPLICABLE",
        DataOriginTag.PropositionIa => "PROPOSITION IA",
        DataOriginTag.CalculDuLogiciel => "NON DÉTERMINÉ",
        _ => "NON DÉTERMINÉ"
    };
}

// ============================================================================
// 2. VALUE OBJECT MONÉTAIRE (SECTION 13)
// ============================================================================

public readonly record struct Money(decimal Amount, string CurrencyCode)
{
    public static Money Zero(string currencyCode = "DZD") => new(0m, currencyCode);
}

// ============================================================================
// 3. ENTITÉS CORE (MULTI-ENTREPRISE, PRODUITS, IMPORTATIONS, FRAIS)
// ============================================================================

public sealed class Company
{
    public Guid Id { get; init; } = Guid.NewGuid();
    // Section 5 (écran "Entreprises") : les champs ci-dessous sont modifiables depuis l'interface
    // (formulaire d'édition d'entreprise). Seul l'identifiant technique Id reste immuable.
    public required string Code { get; set; }
    public required string LegalName { get; set; }
    public string? NIF { get; set; }
    public string? NIS { get; set; }
    public string? RC { get; set; }
    public string DefaultFunctionalCurrency { get; set; } = "DZD";
    /// <summary>
    /// Section 24 : Par défaut true dans le modèle métier du projet (TVA d'importation non récupérable,
    /// donc intégrée au coût de revient). Paramétrable pour évolution future selon le régime comptable.
    /// </summary>
    public bool IsImportVatNonRecoverable { get; set; } = true;
}

/// <summary>
/// Taux de change versionné et historisé (Sections 13 &amp; 14), aujourd'hui utilisé pour deux usages
/// clairement séparés (Section 6 du plan "multi-devises") :
///   - <b>Taux réglementaire/douanier</b> : <see cref="CurrencyCode"/> -&gt; DZD (<see cref="QuoteCurrencyCode"/>
///     reste à sa valeur par défaut "DZD"), consommé exclusivement par le moteur de calcul douanier
///     (<see cref="ImportCostAlgeria.CalculationEngine.CurrencyCalculator"/>) — c'est la seule conversion qui
///     alimente la valeur en douane, les droits et les taxes.
///   - <b>Taux commercial (cross-rate)</b> : <see cref="CurrencyCode"/> -&gt; <see cref="QuoteCurrencyCode"/>
///     différent de DZD (ex: EUR -&gt; USD), consommé par
///     <see cref="ImportCostAlgeria.CalculationEngine.CurrencyConversionService"/> pour calculer la valeur
///     de l'autorisation d'importation (Section 13) — cette conversion n'entre JAMAIS dans le calcul
///     douanier/réglementaire (Section 14 & 22 : "ne pas mélanger conversion commerciale et réglementaire").
/// Les deux usages partagent la même table/le même historique versionné et le même mécanisme de
/// publication "jamais d'écrasement" (voir ExchangeRateAdminRepository.PublishNewRate), ce qui évite de
/// créer un second système concurrent pour les taux commerciaux (Section 7).
/// </summary>
public sealed class ExchangeRateRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>Devise de base du taux (ex: EUR, USD) — "1 unité de CurrencyCode = RateToDzd / QuotityUnit unités de QuoteCurrencyCode".</summary>
    public required string CurrencyCode { get; init; }
    /// <summary>
    /// Devise de cotation du taux. Par défaut "DZD" (taux réglementaire/douanier classique, seul cas qui
    /// existait avant l'ajout de la gestion multi-devises). Une valeur différente de "DZD" (ex: "USD")
    /// représente un taux commercial cross-rate (ex: EUR coté en USD), qui ne doit jamais être utilisé
    /// pour un calcul douanier.
    /// </summary>
    public string QuoteCurrencyCode { get; init; } = "DZD";
    /// <summary>
    /// Montant en <see cref="QuoteCurrencyCode"/> correspondant à <see cref="QuotityUnit"/> unités de
    /// <see cref="CurrencyCode"/> (nom historique conservé pour compatibilité : représente la cotation
    /// brute telle que publiée par la source, pas nécessairement déjà ramenée à 1 unité).
    /// </summary>
    public required decimal RateToDzd { get; init; }
    /// <summary>
    /// Quotité : nombre d'unités de <see cref="CurrencyCode"/> auxquelles correspond <see cref="RateToDzd"/>.
    /// Convention des cotations de change (notamment Banque d'Algérie) : certaines devises sont publiées
    /// "pour 100 unités" ou "pour 1000 unités" afin de conserver suffisamment de précision décimale.
    /// Exemple : RateToDzd = 134.5678 avec QuotityUnit = 100 signifie "100 unités de CurrencyCode valent
    /// 134.5678 unités de QuoteCurrencyCode", soit un taux unitaire de 134.5678 / 100 = 1.345678.
    /// Pour EUR/USD/DZD en pratique, QuotityUnit vaut 1 (devises à valeur unitaire "normale"), mais le
    /// champ reste disponible pour toute devise future nécessitant une cotation pour plusieurs unités.
    /// Le taux manuel saisi par un utilisateur (<see cref="ImportOperation.ManualExchangeRateOverride"/>,
    /// <see cref="ImportOperation.ManualAuthorizationCurrencyRateToDzd"/>) est TOUJOURS exprimé "pour 1
    /// unité" (convention de l'écran de saisie) : toute comparaison entre un taux manuel et un taux
    /// officiel doit donc impérativement normaliser le taux officiel par sa quotité avant de comparer,
    /// afin que les deux valeurs représentent la même unité économique (voir
    /// <see cref="ImportCostAlgeria.CalculationEngine.CurrencyCalculator.ResolveRate"/>).
    /// </summary>
    public int QuotityUnit { get; init; } = 1;
    public required DateOnly ValidFrom { get; init; }
    // ValidTo reste modifiable UNIQUEMENT pour "fermer" une période lors de la publication d'une
    // nouvelle version (Section 14 & 19) — jamais pour modifier le taux lui-même, qui reste immuable.
    public DateOnly? ValidTo { get; set; }
    public required string RateType { get; init; } // OFFICIEL_DOUANE_ALCES, BANQUE_ALGERIE, MANUEL
    public required string SourceName { get; init; }
    public DateTime RetrievedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class Product
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid CompanyId { get; init; }
    public required string Reference { get; set; }
    public required string Designation { get; set; }
    public string? RegulatoryDesignation { get; set; }
    public string? ConfirmedHsCode10 { get; set; }
    public string? HabitualOriginCountryIso2 { get; set; }
    public string MeasurementUnit { get; set; } = "U";
    public decimal? HabitualDutyRatePercent { get; set; } // Indicatif uniquement (Section 31)
    public decimal? HabitualVatRatePercent { get; set; }
    public decimal? UnitGrossWeightKg { get; set; }
    public decimal? UnitVolumeM3 { get; set; }
    public string? Notes { get; set; }
}

public sealed class ImportOperation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid CompanyId { get; init; }
    // Section 5 (écran "Importations") : l'en-tête d'une importation reste modifiable tant que le
    // calcul définitif n'a pas été exporté, afin de permettre la correction d'une saisie.
    public required string ImportNumber { get; set; }
    public required DateOnly ReferenceDate { get; set; }
    public required string SupplierName { get; set; }
    /// <summary>
    /// Revue du 2026-10-02 (cas de référence D10 réel) : pays d'achat / du fournisseur qui a émis la
    /// facture (ex: Portugal dans le D10 SARL HYMA TRADE), STRICTEMENT distinct du pays d'origine de la
    /// marchandise (<see cref="DefaultOriginCountryIso2"/>/<see cref="ImportLine.OriginCountryIso2"/>, ex:
    /// Allemagne) et du pays de provenance/premier destin (<see cref="ExportShippingCountryIso2"/>, ex:
    /// France). Purement informatif/traçabilité (facture, rapports) : n'est JAMAIS lu par
    /// RegulatoryRuleEngine ni par ImportCalculationOrchestrator pour déterminer une règle douanière — seul
    /// le pays d'ORIGINE réel de la marchandise peut ouvrir droit à un tarif préférentiel.
    /// </summary>
    public string? PurchaseCountryIso2 { get; set; }
    public string? DefaultOriginCountryIso2 { get; set; }
    /// <summary>
    /// Section 6 : Le pays d'expédition est saisi UNE SEULE FOIS au niveau de l'importation.
    /// </summary>
    public required string ExportShippingCountryIso2 { get; set; }
    /// <summary>
    /// Devise originale de la facture fournisseur (Section 4 — "ne jamais remplacer la devise originale").
    /// Reste la devise de référence pour toutes les lignes/frais saisis dans cette opération.
    /// </summary>
    public required string MainCurrencyCode { get; set; }
    /// <summary>
    /// Taux de change MANUEL réglementaire/douanier (<see cref="MainCurrencyCode"/> -&gt; DZD), saisi "pour
    /// 1 unité de devise". Alimente directement le calcul douanier (valeur en douane, droits, taxes) via
    /// <see cref="ImportCostAlgeria.CalculationEngine.CurrencyCalculator.ResolveRate"/>. Ne concerne QUE la
    /// conversion réglementaire : voir <see cref="ManualAuthorizationCurrencyRateToDzd"/> pour le taux
    /// réglementaire de la devise d'autorisation d'importation (Section 6 & 14).
    /// </summary>
    public decimal? ManualExchangeRateOverride { get; set; }
    /// <summary>
    /// Devise dans laquelle l'autorisation d'importation (domiciliation bancaire) doit être exprimée
    /// (Section 13). Par défaut "USD" conformément au besoin fonctionnel actuel ; reste modifiable pour
    /// rester extensible à d'autres devises d'autorisation. Lorsque différente de
    /// <see cref="MainCurrencyCode"/>, une conversion commerciale (Section 5 & 6), strictement distincte de
    /// la conversion réglementaire vers DZD, est calculée par
    /// <see cref="ImportCostAlgeria.CalculationEngine.CurrencyConversionService"/> et exposée séparément
    /// dans <see cref="ImportCostAlgeria.CalculationEngine.ImportCalculationSummary.CommercialAuthorizationConversion"/>.
    /// </summary>
    public string AuthorizationCurrencyCode { get; set; } = "USD";
    /// <summary>
    /// Correction 2026-10-02 (demande utilisateur — "NE PLUS JAMAIS saisir directement un taux EUR → USD") :
    /// REMPLACE l'ancien champ ManualAuthorizationExchangeRateOverride, qui représentait à tort un taux
    /// croisé DIRECT <see cref="MainCurrencyCode"/> -&gt; <see cref="AuthorizationCurrencyCode"/> (ex: EUR -&gt;
    /// USD) saisissable par l'utilisateur — c'est exactement ce mécanisme qui permettait d'injecter par
    /// erreur un ordre de grandeur DZD (ex: ~133) à la place d'un véritable taux EUR→USD (~1,13), produisant
    /// des montants USD complètement faux (ex: 16,69 € interprétés comme "2 228,45 $" au lieu de ~18,89 $).
    /// Ce nouveau champ a une sémantique strictement symétrique à <see cref="ManualExchangeRateOverride"/>,
    /// mais pour <see cref="AuthorizationCurrencyCode"/> au lieu de <see cref="MainCurrencyCode"/> : un taux
    /// RÉGLEMENTAIRE manuel "1 [AuthorizationCurrencyCode] = X DA", utilisé UNIQUEMENT si aucun taux
    /// réglementaire officiel n'est encore publié pour cette devise à la date de référence. Le taux
    /// commercial <see cref="MainCurrencyCode"/> -&gt; <see cref="AuthorizationCurrencyCode"/> (ex: EUR -&gt;
    /// USD) n'est plus JAMAIS saisi directement : il est TOUJOURS dérivé mathématiquement par
    /// ImportCalculationOrchestrator via la formule (MainCurrencyCode -&gt; DZD) / (AuthorizationCurrencyCode
    /// -&gt; DZD) — voir <see cref="ImportCostAlgeria.CalculationEngine.CurrencyCalculator.ResolveRate"/>,
    /// appelé séparément pour chacune des deux devises. Null = utiliser le taux réglementaire officiel
    /// enregistré pour <see cref="AuthorizationCurrencyCode"/> (si disponible).
    /// </summary>
    public decimal? ManualAuthorizationCurrencyRateToDzd { get; set; }
    /// <summary>
    /// Revue du 2026-10-02 (Section 15 — "PRCT introuvable → demande UNE FOIS pour l'import, pas par
    /// article") : taux PRCT confirmé manuellement par l'utilisateur pour TOUTE cette importation,
    /// utilisé UNIQUEMENT pour les lignes dont le code SH ne dispose d'AUCUNE règle réglementaire PRCT
    /// publiée (ni applicable, ni explicitement non applicable). Ne modifie jamais la base réglementaire
    /// permanente — reste une valeur propre à cette importation, tracée comme
    /// <see cref="DataOriginTag.DonneeUtilisateur"/> dans le calcul.
    /// </summary>
    public decimal? ManualPrctRatePercent { get; set; }
    public bool UserConfirmedManualPrct { get; set; }
    /// <summary>Même principe que <see cref="ManualPrctRatePercent"/>, pour la Taxe de Contribution de Solidarité (TCS).</summary>
    public decimal? ManualTcsRatePercent { get; set; }
    public bool UserConfirmedManualTcs { get; set; }

    // ------------------------------------------------------------------------------------------
    // Revue du 2026-10-02 (correction urgente — "ne plus bloquer le calcul faute de RegulatoryRule") :
    // TAUX DE TAXES PAR DÉFAUT de l'importation. Utilisés UNIQUEMENT lorsqu'aucune RegulatoryRule
    // officielle en vigueur n'a pu être résolue pour une ligne (statut "DEFAULT_IMPORT", jamais présenté
    // comme un taux réglementaire confirmé — toujours accompagné d'un avertissement traçable). Ne
    // modifient JAMAIS la base réglementaire permanente (RegulatoryRule) : ce sont des paramètres de
    // CETTE importation uniquement, permettant au moteur de calculer au lieu de rester bloqué en
    // attendant la publication/validation de toutes les règles officielles. Le Droit de Douane (DD) reste
    // néanmoins résolu ARTICLE PAR ARTICLE (jamais une valeur globale imposée à toute l'importation) :
    // seule la VALEUR DE REPLI par défaut (DefaultDdRatePercent) est commune, chaque article pouvant
    // malgré tout disposer de sa propre règle officielle ou de son propre taux Excel confirmé.
    // ------------------------------------------------------------------------------------------
    /// <summary>
    /// Active (par défaut) l'utilisation des taux ci-dessous lorsqu'une RegulatoryRule officielle est
    /// introuvable pour un article. Si désactivé, une taxe sans règle officielle (ni confirmation
    /// manuelle) reste à 0 avec le statut "NON DÉTERMINÉ" (toujours un avertissement, jamais un blocage).
    /// </summary>
    public bool UseDefaultRatesWhenRuleMissing { get; set; } = true;
    /// <summary>Droit de Douane (DD) par défaut, appliqué ARTICLE PAR ARTICLE à défaut de règle officielle ou de taux Excel confirmé. Valeur initiale : 0 %.</summary>
    public decimal DefaultDdRatePercent { get; set; } = 0m;
    /// <summary>Contribution de Solidarité (CS) par défaut (assiette : Valeur en douane). Valeur initiale : 3 %.</summary>
    public decimal DefaultCsRatePercent { get; set; } = 3.0m;
    /// <summary>Précompte à l'importation (PRCT) par défaut (assiette : Valeur en douane + DD + CS + TVA). Valeur initiale : 2 %.</summary>
    public decimal DefaultPrctRatePercent { get; set; } = 2.0m;
    /// <summary>TVA à l'importation par défaut (assiette : Valeur en douane + DD + CS). Valeur initiale : 19 %.</summary>
    public decimal DefaultTvaRatePercent { get; set; } = 19.0m;
    /// <summary>
    /// Taxe de Contribution de Solidarité (TCS) par défaut. Valeur initiale : 0 % — AUCUN taux TCS
    /// réglementaire n'est actuellement inventé par le logiciel ; ce paramètre reste modifiable par
    /// importation (ex: 5 %), auquel cas le moteur recalcule automatiquement toutes les lignes.
    /// </summary>
    public decimal DefaultTcsRatePercent { get; set; } = 0m;
    /// <summary>
    /// Montant RPS (Redevance de Prestation de Service) suggéré par défaut pour les NOUVELLES
    /// importations (DZD, montant fixe — jamais multiplié par le nombre d'articles). Valeur initiale : 0.
    /// Purement indicatif : le montant réellement appliqué reste celui du frais RPS ajouté à l'écran
    /// "Frais" (méthode de répartition Montant fixe), jamais recalculé automatiquement ici.
    /// </summary>
    public decimal DefaultRpsAmountDzd { get; set; } = 0m;

    public required IncotermCode Incoterm { get; set; }
    public string CustomsRegimeCode { get; set; } = "DROIT_COMMUN_4000";
    public CustomsValuationMethod ValuationMethod { get; set; } = CustomsValuationMethod.TransactionValue_Art16Ter;
    public required string ArrivalPortOrBorder { get; set; }
    public required string TransportMode { get; set; }
    public bool IsSimulation { get; set; }
    public List<ImportLine> Lines { get; init; } = new();
    public List<ImportFee> Fees { get; init; } = new();
}

public sealed class ImportLine
{
    public Guid Id { get; init; } = Guid.NewGuid();
    // Section 7 (table "Articles") : toutes les colonnes métier restent éditables depuis la grille
    // Windows (DataGrid) — référence, désignation, quantité, prix, devise...
    public required int LineNumber { get; set; }
    public Guid? ProductId { get; set; }
    public required string ProductReference { get; set; }
    public required string Designation { get; set; }
    public required decimal Quantity { get; set; }
    public string MeasurementUnit { get; set; } = "U";
    public required decimal UnitPurchasePrice { get; set; }
    public required string CurrencyCode { get; set; }
    public string? HsCodeConfirmed10 { get; set; }
    public string? AiProposedHsCode10 { get; set; }
    public AiProposalDecision AiHsDecision { get; set; } = AiProposalDecision.NotApplicable;
    /// <summary>
    /// Section 6 : Le pays d'origine reste disponible au niveau de chaque produit/ligne.
    /// </summary>
    public string? OriginCountryIso2 { get; set; }
    public decimal? ExcelDutyRatePercent { get; set; }
    public bool UserConfirmedExcelDutyFallback { get; set; }
    /// <summary>
    /// Revue du 2026-10-02 (Section 12 — "La TVA reste à 0 et il n'est pas possible de la saisir") : taux
    /// de TVA saisi/confirmé MANUELLEMENT par l'utilisateur, utilisé UNIQUEMENT lorsqu'aucune
    /// <see cref="ImportCostAlgeria.RegulatoryEngine.RegulatoryRule"/> officielle de type TVA n'a été
    /// trouvée pour cette ligne (jamais pour remplacer une règle officielle existante — la règle
    /// réglementaire reste toujours prioritaire, comme pour <see cref="ExcelDutyRatePercent"/>/DD). Reste
    /// null tant que l'utilisateur n'a rien saisi : dans ce cas, en l'absence de règle officielle, le
    /// moteur de calcul NE DOIT JAMAIS afficher silencieusement 0 % — il bloque explicitement
    /// ("TVA non déterminée") jusqu'à confirmation.
    /// </summary>
    public decimal? ManualVatRatePercent { get; set; }
    /// <summary>
    /// Vrai uniquement après confirmation EXPLICITE de l'utilisateur d'utiliser <see cref="ManualVatRatePercent"/>
    /// (y compris pour confirmer un taux de 0 %, auquel cas <see cref="VatExemptionReasonFr"/> doit documenter
    /// le motif d'exonération). Tant que ce indicateur reste faux, un taux manuel saisi mais non confirmé
    /// n'est jamais appliqué au calcul.
    /// </summary>
    public bool UserConfirmedManualVatRate { get; set; }
    /// <summary>
    /// Motif d'exonération de TVA documenté par l'utilisateur lorsque <see cref="ManualVatRatePercent"/> = 0 %
    /// est confirmé manuellement (Section 12 : "demander explicitement s'il s'agit d'une exonération").
    /// </summary>
    public string? VatExemptionReasonFr { get; set; }
    public decimal? LineGrossWeightKg { get; set; }
    public decimal? LineVolumeM3 { get; set; }
    /// <summary>
    /// Revue du 2026-10-02 (REFONTE INTERFACE — Section 15/16/17) : prix de vente unitaire saisi par
    /// l'utilisateur, EXPRIMÉ EN DA (DZD), utilisé UNIQUEMENT pour calculer le bénéfice/marge d'affichage
    /// (<c>Bénéfice = Prix de vente - PU Reviens</c>, <c>% Bénéfice = Bénéfice / PU Reviens × 100</c>).
    /// Ne participe JAMAIS au calcul douanier/fiscal (valeur en douane, droits, taxes, coût de revient) :
    /// strictement une donnée commerciale a posteriori, purement informative pour l'utilisateur.
    /// </summary>
    public decimal? SalePriceDzd { get; set; }
    public Dictionary<Guid, decimal> ManualFeeAllocationsDzd { get; init; } = new();
}

/// <summary>
/// Compte utilisateur de l'application Windows (Section 22 & 32).
/// Le mot de passe n'est jamais stocké en clair : seuls le hachage PBKDF2 et le sel sont persistés
/// (voir ImportCostAlgeria.Database.Security.PasswordHasher).
/// </summary>
public sealed class AppUser
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Username { get; set; }
    public required string DisplayName { get; set; }
    public required string PasswordHash { get; set; }
    public required string PasswordSalt { get; set; }
    public required UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// Section 18 (sécurité) : vrai pour le compte Administrateur initial créé automatiquement au premier
    /// démarrage avec un mot de passe aléatoire à usage unique — force un changement de mot de passe
    /// obligatoire à la prochaine connexion avant d'accéder à l'application (voir
    /// ImportCostAlgeria.Presentation.Views.ChangePasswordWindow). Remis à faux dès que l'utilisateur a
    /// changé son mot de passe (voir UserRepository.ChangePassword).
    /// </summary>
    public bool MustChangePasswordOnNextLogin { get; set; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class ImportFee
{
    public Guid Id { get; init; } = Guid.NewGuid();
    // Section 9 (écran "Frais") : chaque frais reste entièrement modifiable (montant, devise, méthode
    // de répartition, inclusion en valeur en douane / coût de revient).
    public required string FeeCategoryCode { get; set; }
    public required string FeeName { get; set; }
    public required decimal Amount { get; set; }
    public required string CurrencyCode { get; set; }
    public required FeeAllocationMethod AllocationMethod { get; set; }
    public required bool IncludeInCustomsValue { get; set; }
    public CustomsAdjustmentTreatment CustomsTreatment { get; set; } = CustomsAdjustmentTreatment.PostIntroductionExcluded;
    public required bool IncludeInCostOfGoods { get; set; }
    public string? LegalBasisReference { get; init; }
}
