using System;
using System.Collections.Generic;
using System.Linq;
using ImportCostAlgeria.Core.Domain;

namespace ImportCostAlgeria.Core.Services;

/// <summary>
/// Configuration d'un champ dynamique affiché selon l'Incoterm sélectionné (Sections 7 & 8).
/// Ne jamais appliquer automatiquement une règle Incoterm non documentée par la configuration métier.
/// </summary>
public sealed record DynamicIncotermFieldSpec(
    IncotermCode Incoterm,
    string FeeCategoryCode,
    string LabelFr,
    bool IsRequiredForCustomsValuation,
    CustomsAdjustmentTreatment DefaultCustomsTreatment,
    bool DefaultIncludeInCustomsValue,
    bool DefaultIncludeInCostOfGoods,
    AnomalySeverity SeverityIfMissing,
    string LegalBasisArticleFr);

/// <summary>
/// Service de résolution des champs dynamiques selon l'Incoterm (Sections 7 & 8).
/// V1 : EXW, FOB, CFR. Architecture prête pour FCA, FAS, CIF, CPT, CIP, DAP, DPU, DDP.
/// </summary>
public sealed class IncotermDynamicFieldService
{
    private static readonly IReadOnlyList<DynamicIncotermFieldSpec> ConfiguredRules = new[]
    {
        // EXW : Prix marchandise + Transport intérieur pays exportateur + Frais export + Manutention/chargement + Fret international + Assurance
        new DynamicIncotermFieldSpec(
            IncotermCode.EXW, "TRANSPORT_INTERIEUR_EXPORT", "Transport intérieur pays exportateur",
            true, CustomsAdjustmentTreatment.Addition_Art16Octies, true, true, AnomalySeverity.Erreur,
            "Art. 16 octies § 1 e) i) du Code des Douanes Algérien"),
        new DynamicIncotermFieldSpec(
            IncotermCode.EXW, "FRAIS_EXPORT", "Frais export (dédouanement export)",
            true, CustomsAdjustmentTreatment.Addition_Art16Octies, true, true, AnomalySeverity.Erreur,
            "Art. 16 octies § 1 e) ii) du Code des Douanes Algérien"),
        new DynamicIncotermFieldSpec(
            IncotermCode.EXW, "MANUTENTION_EXPORT", "Manutention / chargement export",
            true, CustomsAdjustmentTreatment.Addition_Art16Octies, true, true, AnomalySeverity.Erreur,
            "Art. 16 octies § 1 e) ii) du Code des Douanes Algérien"),
        new DynamicIncotermFieldSpec(
            IncotermCode.EXW, "FRET_INTERNATIONAL", "Fret international",
            true, CustomsAdjustmentTreatment.Addition_Art16Octies, true, true, AnomalySeverity.Erreur,
            "Art. 16 octies § 1 e) i) du Code des Douanes Algérien"),
        new DynamicIncotermFieldSpec(
            IncotermCode.EXW, "ASSURANCE", "Assurance transport international",
            true, CustomsAdjustmentTreatment.Addition_Art16Octies, true, true, AnomalySeverity.Avertissement,
            "Art. 16 octies § 1 e) i) du Code des Douanes Algérien"),

        // FOB : Prix FOB + Fret international + Assurance
        new DynamicIncotermFieldSpec(
            IncotermCode.FOB, "FRET_INTERNATIONAL", "Fret international",
            true, CustomsAdjustmentTreatment.Addition_Art16Octies, true, true, AnomalySeverity.Erreur,
            "Art. 16 octies § 1 e) i) du Code des Douanes Algérien"),
        new DynamicIncotermFieldSpec(
            IncotermCode.FOB, "ASSURANCE", "Assurance transport international",
            true, CustomsAdjustmentTreatment.Addition_Art16Octies, true, true, AnomalySeverity.Avertissement,
            "Art. 16 octies § 1 e) i) du Code des Douanes Algérien"),

        // CFR : Prix CFR (fret déjà inclus) + Assurance
        new DynamicIncotermFieldSpec(
            IncotermCode.CFR, "ASSURANCE", "Assurance transport international",
            true, CustomsAdjustmentTreatment.Addition_Art16Octies, true, true, AnomalySeverity.Avertissement,
            "Art. 16 octies § 1 e) i) du Code des Douanes Algérien")
    };

    public static bool IsSupportedInV1(IncotermCode code) =>
        code is IncotermCode.EXW or IncotermCode.FOB or IncotermCode.CFR;

    public IReadOnlyList<DynamicIncotermFieldSpec> GetRequiredDynamicFields(IncotermCode incoterm) =>
        ConfiguredRules.Where(r => r.Incoterm == incoterm).ToList();
}

/// <summary>
/// Catalogue des 19 types de frais d'importation standards + création de frais personnalisés (Section 9).
/// Revue du 2026-10-02 (cas de référence D10 réel) : "RPS" (Redevance de Prestation de Service, 2 500 DZD
/// sur le D10 SARL HYMA TRADE) rejoint ce catalogue — RÉUTILISE le modèle de frais existant (montant fixe,
/// réparti également entre les articles) plutôt que de créer un second système de "frais réglementaires"
/// parallèle : la RPS n'est pas une taxe assise sur la valeur en douane (donc jamais dans RegulatoryRule),
/// mais une redevance forfaitaire post-dédouanement, exactement le cas d'usage pour lequel
/// FeeAllocationMethod.FixedAmount existe déjà.
/// </summary>
public sealed record StandardFeeTemplate(
    string CategoryCode,
    string DefaultLabelFr,
    FeeAllocationMethod SuggestedAllocationMethod,
    bool DefaultIncludeInCustomsValue,
    CustomsAdjustmentTreatment DefaultCustomsTreatment,
    bool DefaultIncludeInCostOfGoods,
    // Revue du 2026-10-02 (demande utilisateur, Section 2 — "restaurer le choix de devise des frais" +
    // règle de pré-sélection) : true = frais normalement payé EN ALGÉRIE (pré-sélectionné en DA/DZD —
    // transport port -> entrepôt, manutention locale, magasinage, transit, frais bancaires, RPS...) ;
    // false = frais normalement payé DANS LE PAYS D'EXPÉDITION (pré-sélectionné dans la devise de la
    // facture/importation — fret international, frais export, assurance...). Reste TOUJOURS modifiable
    // par l'utilisateur ensuite (voir FeeRowViewModel.CurrencyCode) : une simple PROPOSITION par défaut,
    // jamais une conversion automatique forcée de tous les frais vers une même devise.
    bool DefaultIsLocalCurrency = true);

public static class ImportFeeCatalog
{
    public static readonly IReadOnlyList<StandardFeeTemplate> StandardTemplates = new[]
    {
        new StandardFeeTemplate("FRET_INTERNATIONAL",         "Fret international",                     FeeAllocationMethod.ByValue,    true,  CustomsAdjustmentTreatment.Addition_Art16Octies,     true,  DefaultIsLocalCurrency: false),
        new StandardFeeTemplate("ASSURANCE",                  "Assurance",                              FeeAllocationMethod.ByValue,    true,  CustomsAdjustmentTreatment.Addition_Art16Octies,     true,  DefaultIsLocalCurrency: false),
        new StandardFeeTemplate("TRANSPORT_INTERIEUR_EXPORT", "Transport intérieur pays exportateur",   FeeAllocationMethod.ByWeight,   true,  CustomsAdjustmentTreatment.Addition_Art16Octies,     true,  DefaultIsLocalCurrency: false),
        new StandardFeeTemplate("FRAIS_EXPORT",               "Frais export",                           FeeAllocationMethod.ByValue,    true,  CustomsAdjustmentTreatment.Addition_Art16Octies,     true,  DefaultIsLocalCurrency: false),
        new StandardFeeTemplate("MANUTENTION",                "Manutention",                            FeeAllocationMethod.ByQuantity, false, CustomsAdjustmentTreatment.PostIntroductionExcluded, true,  DefaultIsLocalCurrency: true),
        new StandardFeeTemplate("FRAIS_PORTUAIRES",           "Frais portuaires",                       FeeAllocationMethod.ByValue,    false, CustomsAdjustmentTreatment.PostIntroductionExcluded, true,  DefaultIsLocalCurrency: true),
        new StandardFeeTemplate("THC",                        "THC (Terminal Handling Charges)",        FeeAllocationMethod.ByQuantity, false, CustomsAdjustmentTreatment.PostIntroductionExcluded, true,  DefaultIsLocalCurrency: true),
        new StandardFeeTemplate("MAGASINAGE",                 "Magasinage",                             FeeAllocationMethod.ByValue,    false, CustomsAdjustmentTreatment.PostIntroductionExcluded, true,  DefaultIsLocalCurrency: true),
        new StandardFeeTemplate("DEPOTAGE",                   "Dépotage",                               FeeAllocationMethod.ByQuantity, false, CustomsAdjustmentTreatment.PostIntroductionExcluded, true,  DefaultIsLocalCurrency: true),
        new StandardFeeTemplate("TRANSIT",                    "Transit",                                FeeAllocationMethod.ByValue,    false, CustomsAdjustmentTreatment.PostIntroductionExcluded, true,  DefaultIsLocalCurrency: true),
        new StandardFeeTemplate("COMMISSIONNAIRE_DOUANE",     "Commissionnaire en douane (Honoraires)", FeeAllocationMethod.ByValue,    false, CustomsAdjustmentTreatment.PostIntroductionExcluded, true,  DefaultIsLocalCurrency: true),
        new StandardFeeTemplate("TRANSPORT_PORT_ENTREPOT",    "Transport port → entrepôt",              FeeAllocationMethod.ByWeight,   false, CustomsAdjustmentTreatment.PostIntroductionExcluded, true,  DefaultIsLocalCurrency: true),
        new StandardFeeTemplate("FRAIS_BANCAIRES",            "Frais bancaires",                        FeeAllocationMethod.ByValue,    false, CustomsAdjustmentTreatment.PostIntroductionExcluded, true,  DefaultIsLocalCurrency: true),
        new StandardFeeTemplate("DOMICILIATION",              "Domiciliation bancaire",                 FeeAllocationMethod.ByValue,    false, CustomsAdjustmentTreatment.PostIntroductionExcluded, true,  DefaultIsLocalCurrency: true),
        new StandardFeeTemplate("CONTROLE",                   "Contrôle aux frontières",                FeeAllocationMethod.ByValue,    false, CustomsAdjustmentTreatment.PostIntroductionExcluded, true,  DefaultIsLocalCurrency: true),
        new StandardFeeTemplate("INSPECTION",                 "Inspection",                             FeeAllocationMethod.ByValue,    false, CustomsAdjustmentTreatment.PostIntroductionExcluded, true,  DefaultIsLocalCurrency: true),
        new StandardFeeTemplate("CERTIFICATION",              "Certification / Conformité",             FeeAllocationMethod.ByValue,    false, CustomsAdjustmentTreatment.PostIntroductionExcluded, true,  DefaultIsLocalCurrency: true),
        new StandardFeeTemplate("AUTRES_FRAIS",               "Autres frais",                           FeeAllocationMethod.ByValue,    false, CustomsAdjustmentTreatment.PostIntroductionExcluded, true,  DefaultIsLocalCurrency: true),
        new StandardFeeTemplate("RPS",                        "Redevance de Prestation de Service (RPS)", FeeAllocationMethod.FixedAmount, false, CustomsAdjustmentTreatment.PostIntroductionExcluded, true, DefaultIsLocalCurrency: true)
    };

    /// <summary>
    /// Devises proposées pour un frais (Section 2 de la demande utilisateur — "prévoir une architecture
    /// extensible à d'autres devises") : ajouter une devise supportée se fait UNIQUEMENT ici, sans toucher
    /// à aucun ViewModel ni XAML (la liste est consommée telle quelle par le ComboBox "Devise" de l'écran
    /// Frais). "DZD" reste toujours la première (devise par défaut la plus fréquente pour les frais locaux).
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedFeeCurrencies = new[] { "DZD", "EUR", "USD" };

    /// <summary>
    /// Méthodes de répartition proposées à l'écran en V1 (Section 19) : "Par poids" et "Par volume" restent
    /// disponibles dans FeeAllocationMethod pour une évolution future, mais ne doivent PAS être proposées
    /// dans l'interface actuelle (fonctionnalité abandonnée pour V1, Section 35).
    /// </summary>
    public static readonly IReadOnlyList<FeeAllocationMethod> AllocationMethodsForV1 = new[]
    {
        FeeAllocationMethod.ByValue,
        FeeAllocationMethod.ByQuantity,
        FeeAllocationMethod.FixedAmount,
        FeeAllocationMethod.Percentage,
        FeeAllocationMethod.Manual
    };
}

/// <summary>
/// Proposition issue de la Base Produits réutilisable lors d'une nouvelle importation (Section 31).
/// Workflow : Référence reconnue -> Produit retrouvé -> Données proposées -> Utilisateur confirme.
/// Ne réutilise JAMAIS aveuglément un ancien taux douanier ou TVA.
/// </summary>
public sealed record ProductCatalogRecognitionProposal(
    Guid ProductId,
    string Reference,
    string ProposedDesignation,
    string? ProposedHsCode10,
    string? ProposedOriginCountryIso2,
    string ProposedMeasurementUnit,
    decimal? ProposedUnitGrossWeightKg,
    decimal? HistoricalIndicativeDutyRatePercent,
    bool RequiresUserConfirmation,
    string RegulatorySafetyNoticeFr);

public sealed class ProductCatalogService
{
    private readonly List<Product> _companyProducts = new();

    public void RegisterOrUpdateProduct(Product product)
    {
        _companyProducts.RemoveAll(p =>
            p.CompanyId == product.CompanyId &&
            string.Equals(p.Reference, product.Reference, StringComparison.OrdinalIgnoreCase));
        _companyProducts.Add(product);
    }

    /// <summary>
    /// Recherche un produit dans le référentiel de l'entreprise (isolation stricte par CompanyId — Section 32).
    /// </summary>
    public ProductCatalogRecognitionProposal? TryRecognizeProductForCompany(Guid companyId, string productReference)
    {
        var match = _companyProducts.FirstOrDefault(p =>
            p.CompanyId == companyId &&
            string.Equals(p.Reference.Trim(), productReference.Trim(), StringComparison.OrdinalIgnoreCase));

        if (match == null)
            return null;

        return new ProductCatalogRecognitionProposal(
            ProductId: match.Id,
            Reference: match.Reference,
            ProposedDesignation: match.Designation,
            ProposedHsCode10: match.ConfirmedHsCode10,
            ProposedOriginCountryIso2: match.HabitualOriginCountryIso2,
            ProposedMeasurementUnit: match.MeasurementUnit,
            ProposedUnitGrossWeightKg: match.UnitGrossWeightKg,
            HistoricalIndicativeDutyRatePercent: match.HabitualDutyRatePercent,
            RequiresUserConfirmation: true,
            RegulatorySafetyNoticeFr: "Produit reconnu dans la base entreprise : le Code SH et l'origine sont proposés pour confirmation. Les taux de droits et taxes seront recalculés selon la réglementation en vigueur à la date de la nouvelle opération.");
    }
}
