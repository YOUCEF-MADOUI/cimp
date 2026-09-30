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
    CustomsValueDzd,                            // Valeur en douane (assiette DD, DAPS...)
    CustomsValuePlusDutiesAndTaxesExVatDzd,     // Art. 19 CTCA : Valeur en douane + Tous droits et taxes hors TVA
    PhysicalQuantityOrWeight                    // Assiette spécifique (kg, litre, unité)
}

public enum DutyComparisonStatus
{
    Match,                                  // ✓ Correspondance (Droit Excel == Droit réglementaire)
    Difference,                             // ⚠️ DIFFÉRENCE (Droit Excel != Droit réglementaire)
    RegulatoryNotFoundPendingConfirmation,  // INFORMATION NON DÉTERMINÉE -> Attente confirmation utilisateur
    ExcelFallbackConfirmedByUser            // Taux Excel utilisé après confirmation explicite et tracée
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
    PropositionIa
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
    public required string Code { get; init; }
    public required string LegalName { get; init; }
    public string? NIF { get; init; }
    public string? NIS { get; init; }
    public string? RC { get; init; }
    public string DefaultFunctionalCurrency { get; init; } = "DZD";
    /// <summary>
    /// Section 24 : Par défaut true dans le modèle métier du projet (TVA d'importation non récupérable,
    /// donc intégrée au coût de revient). Paramétrable pour évolution future selon le régime comptable.
    /// </summary>
    public bool IsImportVatNonRecoverable { get; set; } = true;
}

public sealed class ExchangeRateRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string CurrencyCode { get; init; }
    public required decimal RateToDzd { get; init; }
    public int QuotityUnit { get; init; } = 1;
    public required DateOnly ValidFrom { get; init; }
    public DateOnly? ValidTo { get; init; }
    public required string RateType { get; init; } // OFFICIEL_DOUANE_ALCES, BANQUE_ALGERIE, MANUEL
    public required string SourceName { get; init; }
    public DateTime RetrievedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class Product
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid CompanyId { get; init; }
    public required string Reference { get; init; }
    public required string Designation { get; init; }
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
    public required string ImportNumber { get; init; }
    public required DateOnly ReferenceDate { get; init; }
    public required string SupplierName { get; init; }
    public string? DefaultOriginCountryIso2 { get; init; }
    /// <summary>
    /// Section 6 : Le pays d'expédition est saisi UNE SEULE FOIS au niveau de l'importation.
    /// </summary>
    public required string ExportShippingCountryIso2 { get; init; }
    public required string MainCurrencyCode { get; init; }
    public decimal? ManualExchangeRateOverride { get; set; }
    public required IncotermCode Incoterm { get; init; }
    public string CustomsRegimeCode { get; init; } = "DROIT_COMMUN_4000";
    public CustomsValuationMethod ValuationMethod { get; init; } = CustomsValuationMethod.TransactionValue_Art16Ter;
    public required string ArrivalPortOrBorder { get; init; }
    public required string TransportMode { get; init; }
    public bool IsSimulation { get; set; }
    public List<ImportLine> Lines { get; init; } = new();
    public List<ImportFee> Fees { get; init; } = new();
}

public sealed class ImportLine
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required int LineNumber { get; init; }
    public Guid? ProductId { get; set; }
    public required string ProductReference { get; init; }
    public required string Designation { get; init; }
    public required decimal Quantity { get; init; }
    public string MeasurementUnit { get; init; } = "U";
    public required decimal UnitPurchasePrice { get; init; }
    public required string CurrencyCode { get; init; }
    public string? HsCodeConfirmed10 { get; set; }
    public string? AiProposedHsCode10 { get; set; }
    public AiProposalDecision AiHsDecision { get; set; } = AiProposalDecision.NotApplicable;
    /// <summary>
    /// Section 6 : Le pays d'origine reste disponible au niveau de chaque produit/ligne.
    /// </summary>
    public string? OriginCountryIso2 { get; set; }
    public decimal? ExcelDutyRatePercent { get; set; }
    public bool UserConfirmedExcelDutyFallback { get; set; }
    public decimal? LineGrossWeightKg { get; set; }
    public decimal? LineVolumeM3 { get; set; }
    public Dictionary<Guid, decimal> ManualFeeAllocationsDzd { get; init; } = new();
}

public sealed class ImportFee
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string FeeCategoryCode { get; init; }
    public required string FeeName { get; init; }
    public required decimal Amount { get; init; }
    public required string CurrencyCode { get; init; }
    public required FeeAllocationMethod AllocationMethod { get; init; }
    public required bool IncludeInCustomsValue { get; init; }
    public CustomsAdjustmentTreatment CustomsTreatment { get; init; } = CustomsAdjustmentTreatment.PostIntroductionExcluded;
    public required bool IncludeInCostOfGoods { get; init; }
    public string? LegalBasisReference { get; init; }
}
