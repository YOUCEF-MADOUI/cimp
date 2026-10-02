using System;
using System.Collections.Generic;
using System.Linq;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.RegulatoryEngine;

namespace ImportCostAlgeria.CalculationEngine;

// ============================================================================
// 1. MODÈLES DE RÉSULTATS DU MOTEUR DE CALCUL (SECTIONS 25, 26, 27, 28, 40)
// ============================================================================

public sealed record CalculationAnomaly(
    AnomalySeverity Severity,
    string AnomalyCode,
    string MessageFr,
    int? LineNumber = null,
    string? ExpectedValue = null,
    string? ActualValue = null);

public sealed record AppliedTaxBreakdown(
    string TaxCode,
    string TaxNameFr,
    decimal TaxableBaseDzd,
    decimal RatePercent,
    decimal TaxAmountDzd,
    bool IsNonRecoverable,
    string RegulatoryRuleCode,
    string LegalArticleReference,
    string JoraReference,
    string RegulatoryVersionCode,
    DataOriginTag OriginTag);

public sealed record FeeAllocationTrace(
    Guid FeeId,
    string FeeName,
    FeeAllocationMethod MethodUsed,
    decimal ShareRatio,
    decimal AllocatedAmountDzd,
    bool IncludedInCustomsValue,
    bool IncludedInCostOfGoods);

/// <summary>
/// Résultat 1 (Section 25) : Résultat Douanier et Fiscal séparé du coût économique.
/// </summary>
public sealed record LineCustomsResult(
    decimal CustomsValueDzd,
    decimal CustomsDutyRatePercent,
    decimal CustomsDutyAmountDzd,
    DutyComparisonStatus ExcelVsRegulatoryComparison,
    string ComparisonLabelFr,
    IReadOnlyList<AppliedTaxBreakdown> AdditionalTaxes,
    decimal TotalAdditionalTaxesDzd,
    decimal VatTaxableBaseDzd,
    decimal VatRatePercent,
    decimal ImportVatAmountDzd,
    decimal TotalDutiesAndTaxesDzd,
    decimal CustomsValuePlusDutiesAndTaxesDzd,
    // Section 17 de l'audit : traçabilité de la source légale du droit de douane et de la TVA appliqués,
    // issue de la règle réglementaire effectivement résolue (jamais une référence générique codée en dur).
    // Null lorsque le taux appliqué n'a pas de règle réglementaire officielle correspondante (ex: taux
    // Excel confirmé par l'utilisateur en l'absence de règle officielle) — dans ce cas, l'appelant doit
    // afficher "INFORMATION NON DÉTERMINÉE" plutôt que d'inventer une citation.
    string? CustomsDutyLegalArticleReference = null,
    string? CustomsDutyJoraReference = null,
    string? CustomsDutyRegulatoryVersionCode = null,
    string? VatLegalArticleReference = null,
    string? VatJoraReference = null,
    string? VatRegulatoryVersionCode = null,
    // Revue du 2026-10-01 (point 5 & 6 — Droits et taxes par code SH / Affichage écran Importation) :
    // statut explicite (Applicable / Non applicable / Donnée manquante) des taxes additionnelles
    // "standard" (PRCT, TCS, DAPS) pour ce code SH/cette date, construit UNIQUEMENT à partir des règles
    // réglementaires réellement résolues (RegulatoryRuleEngine.BuildStandardTaxApplicabilityReport) —
    // jamais un taux inventé ni une absence silencieuse. Liste vide si le code SH n'a pas pu être résolu.
    IReadOnlyList<TaxApplicabilityStatus>? StandardTaxApplicability = null,
    // Revue du 2026-10-02 (Section 12 — TVA ne doit jamais rester silencieusement à 0) : origine du taux
    // de TVA réellement appliqué (VatRatePercent) — DonneeOfficielle (règle réglementaire), DonneeUtilisateur
    // (taux confirmé manuellement en l'absence de règle) ou CalculDuLogiciel (aucune règle ET aucune
    // confirmation : VatRatePercent reste à 0 mais ne doit JAMAIS être présenté comme définitif — la ligne
    // est de toute façon déjà bloquante, voir ImportCalculationSummary.Anomalies).
    DataOriginTag VatRateOriginTag = DataOriginTag.CalculDuLogiciel);

/// <summary>
/// Résultat 2 (Section 26) : Coût d'acquisition et Coût de revient économique réel.
/// </summary>
public sealed record LineEconomicCostResult(
    decimal PurchaseValueCurrency,
    decimal PurchaseValueDzd,
    decimal AllocatedCustomsIncludedFeesDzd,
    decimal AllocatedLocalAndPostCustomsFeesDzd,
    decimal TotalAllocatedFeesDzd,
    decimal CustomsDutyDzd,
    decimal NonRecoverableAdditionalTaxesDzd,
    decimal NonRecoverableImportVatDzd,
    decimal AcquisitionCostExVatDzd,
    decimal RealCostOfGoodsTotalDzd,
    decimal UnitCostOfGoodsDzd,
    decimal LandedCostCoefficient);

public sealed record LineFullCalculationResult(
    int LineNumber,
    string ProductReference,
    string Designation,
    decimal Quantity,
    string CurrencyCode,
    decimal AppliedExchangeRateToDzd,
    LineCustomsResult CustomsOutcome,
    LineEconomicCostResult EconomicOutcome,
    IReadOnlyList<FeeAllocationTrace> FeeAllocations,
    // Section 12 du plan multi-devises : conversion COMMERCIALE (jamais réglementaire) de cette ligne vers
    // la devise d'autorisation d'importation (ex: EUR -> USD). Null lorsque la devise de la ligne est déjà
    // la devise d'autorisation (aucune conversion à afficher, Section 15 : "ne pas afficher inutilement
    // une conversion"). Ne participe JAMAIS à CustomsOutcome / EconomicOutcome (Section 14 & 22).
    LineCommercialConversion? AuthorizationConversion = null);

/// <summary>
/// Conversion commerciale d'une ligne vers la devise d'autorisation d'importation (Section 12). Valeurs
/// purement informatives/traçabilité, jamais réinjectées dans le calcul douanier.
/// </summary>
public sealed record LineCommercialConversion(
    string AuthorizationCurrencyCode,
    decimal AuthorizationUnitPrice,
    decimal AuthorizationTotalAmount,
    decimal EffectiveRate,
    bool IsManualRate);

public sealed record ImportCalculationSummary(
    Guid ImportOperationId,
    string ImportNumber,
    DateOnly ReferenceDate,
    bool IsSimulation,
    // 11 résultats exigés par la Section 1 :
    decimal TotalPurchaseValueMainCurrency,
    decimal TotalPurchaseValueDzd,
    decimal TotalCustomsValueDzd,
    decimal TotalCustomsDutyDzd,
    decimal TotalAdditionalTaxesDzd,
    decimal TotalImportVatDzd,
    decimal TotalDutiesAndTaxesDzd,      // Section 25 : Total Droits et Taxes
    decimal TotalImportFeesDzd,          // Section 1.7 : Frais liés à l'importation
    decimal TotalAcquisitionCostExVatDzd,// Section 1.8 : Coût d'acquisition HT
    decimal TotalRealCostOfGoodsDzd,     // Section 1.9 & 1.11 : Coût de revient réel total de l'importation
    IReadOnlyList<LineFullCalculationResult> LineResults, // Section 1.10 : Coût unitaire par article
    IReadOnlyList<CalculationAnomaly> Anomalies,
    bool HasBlockingAnomalies,
    string MandatoryLegalDisclaimerFr,
    // Section 6, 11 & 13 du plan multi-devises : conversion COMMERCIALE globale de la facture (devise
    // originale -> devise de l'autorisation d'importation, ex: EUR -> USD). Null lorsque la devise
    // d'autorisation est identique à la devise de la facture (aucune conversion nécessaire). Strictement
    // distincte de la conversion réglementaire vers DZD déjà portée par les champs ci-dessus — jamais
    // mélangée avec eux (Section 14 & 22).
    CommercialAuthorizationConversion? CommercialAuthorizationConversion = null);

/// <summary>
/// Conversion commerciale globale de la facture vers la devise de l'autorisation d'importation
/// (Section 5, 6, 9 & 13 du plan multi-devises). Toujours accompagnée de la traçabilité du taux utilisé
/// (officiel ou manuel, source, date) — jamais une simple valeur numérique opaque (Section 7).
/// </summary>
public sealed record CommercialAuthorizationConversion(
    string OriginalCurrencyCode,
    decimal OriginalTotalAmount,
    string AuthorizationCurrencyCode,
    decimal AuthorizationTotalAmount,
    decimal EffectiveRate,
    bool IsManualRate,
    string? OfficialRateSourceName,
    DateOnly? OfficialRateValidFrom,
    string RateTypeLabelFr);

// ============================================================================
// 2. SOUS-MOTEURS SPÉCIALISÉS (SÉPARÉS DE TOUTE INTERFACE GRAPHIQUE)
// ============================================================================

public interface IExchangeRateProvider
{
    /// <summary>Taux réglementaire/douanier historique : <paramref name="currencyCode"/> -&gt; DZD.</summary>
    ExchangeRateRecord? GetRegulatoryRate(string currencyCode, DateOnly referenceDate);

    /// <summary>
    /// Taux historique entre deux devises quelconques (Section 6 du plan multi-devises), ex: EUR -&gt; USD.
    /// Implémentation par défaut : déléguée à <see cref="GetRegulatoryRate"/> lorsque la devise de cotation
    /// demandée est "DZD" (comportement strictement identique à avant l'ajout du multi-devises, donc sans
    /// aucune régression pour le code existant) ; retourne null si aucun taux n'est trouvé pour une autre
    /// devise de cotation et que l'implémentation ne la gère pas explicitement (voir
    /// ImportCostAlgeria.Database.Repositories.EfExchangeRateProvider pour l'implémentation réelle sur base).
    /// </summary>
    ExchangeRateRecord? GetRate(string fromCurrencyCode, string toCurrencyCode, DateOnly referenceDate) =>
        string.Equals(toCurrencyCode, "DZD", StringComparison.OrdinalIgnoreCase)
            ? GetRegulatoryRate(fromCurrencyCode, referenceDate)
            : null;
}

/// <summary>
/// Normalisation et comparaison d'un taux de change officiel/manuel, centralisées (Section 8 du plan
/// multi-devises : "ne duplique pas les formules de conversion dans plusieurs ViewModels/classes") afin
/// que <see cref="CurrencyCalculator"/> (conversion réglementaire vers DZD) et
/// <see cref="CurrencyConversionService"/> (conversion commerciale entre devises quelconques, ex: EUR -&gt;
/// USD) appliquent exactement la même règle de normalisation par quotité et le même seuil d'alerte
/// "⚠️ TAUX MANUEL", quelle que soit la devise de cotation cible.
/// </summary>
public static class ExchangeRateNormalization
{
    /// <summary>
    /// Seuil d'écart relatif (Section 9) au-delà duquel un taux manuel saisi par l'utilisateur, différent
    /// du taux officiel enregistré pour la même devise/période, déclenche un avertissement "⚠️ TAUX
    /// MANUEL". Volontairement non nul (et non une égalité stricte) afin de ne jamais déclencher une
    /// fausse alerte sur un simple écart d'arrondi négligeable entre deux représentations du même taux.
    /// Valeur actuelle : 0,5 % d'écart relatif — seuil conservateur documenté, ajustable ici en un seul
    /// endroit si l'application doit un jour exposer ce réglage à l'utilisateur.
    /// </summary>
    public const decimal ManualRateDifferenceWarningThresholdPercent = 0.5m;

    /// <summary>
    /// Ramène un taux officiel publié "pour <see cref="ExchangeRateRecord.QuotityUnit"/> unités" à un taux
    /// "pour 1 unité", c'est-à-dire la même convention que celle utilisée par tous les taux saisis
    /// manuellement dans l'application (Section 3 de l'audit : les deux chemins doivent représenter la
    /// même unité économique).
    /// </summary>
    public static decimal ToUnitRate(ExchangeRateRecord official) =>
        official.QuotityUnit == 0 ? 0m : official.RateToDzd / official.QuotityUnit;

    /// <summary>
    /// Résout le taux effectif à utiliser pour une conversion (devise source -&gt; devise cible quelconque),
    /// en appliquant systématiquement la normalisation par quotité ci-dessus et en générant les anomalies
    /// appropriées (taux manquant = BLOCAGE, taux manuel différent du taux officiel au-delà du seuil =
    /// AVERTISSEMENT "⚠️ TAUX MANUEL").
    /// </summary>
    public static (decimal EffectiveRate, ExchangeRateRecord? OfficialRecord, CalculationAnomaly? Anomaly) Resolve(
        ExchangeRateRecord? official,
        decimal? manualOverrideRate,
        string fromCurrencyCode,
        string toCurrencyCode,
        DateOnly referenceDate,
        string missingRateAnomalyCode,
        string manualDiffAnomalyCode,
        int? lineNumber = null)
    {
        if (manualOverrideRate.HasValue && manualOverrideRate.Value > 0m)
        {
            if (official != null)
            {
                decimal officialUnitRate = ToUnitRate(official);
                decimal relativeDiffPercent = officialUnitRate == 0m
                    ? 100m
                    : Math.Abs((manualOverrideRate.Value - officialUnitRate) / officialUnitRate) * 100m;

                if (relativeDiffPercent > ManualRateDifferenceWarningThresholdPercent)
                {
                    var warning = new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        manualDiffAnomalyCode,
                        $"⚠️ TAUX MANUEL : Le taux {fromCurrencyCode}→{toCurrencyCode} utilisé ({manualOverrideRate.Value:F4}) diffère de {relativeDiffPercent:F2} % du taux officiel enregistré ({officialUnitRate:F4} - {official.SourceName}).",
                        LineNumber: lineNumber,
                        ExpectedValue: officialUnitRate.ToString("F4"),
                        ActualValue: manualOverrideRate.Value.ToString("F4"));
                    return (manualOverrideRate.Value, official, warning);
                }
            }

            return (manualOverrideRate.Value, official, null);
        }

        if (official == null)
        {
            var blocking = new CalculationAnomaly(
                AnomalySeverity.Blocage,
                missingRateAnomalyCode,
                $"⚠️ Taux de change absent pour la conversion {fromCurrencyCode}→{toCurrencyCode} à la date de référence {referenceDate:dd/MM/yyyy}.",
                LineNumber: lineNumber);
            return (0m, null, blocking);
        }

        return (ToUnitRate(official), official, null);
    }
}

/// <summary>
/// Arrondi monétaire générique PAR DEVISE (correction du 2026-10-01, point 2 de l'audit) : distinct de
/// <see cref="CurrencyCalculator.RoundDzd"/>, qui reste nommément et sémantiquement réservé aux montants
/// RÉELLEMENT EXPRIMÉS EN DZD (calcul réglementaire/douanier). Un montant EUR ou USD (conversion
/// commerciale, montant original de facture, valeur de l'autorisation d'importation) ne doit jamais être
/// arrondi par une fonction dont le nom indique explicitement du DZD — même si, pour les devises
/// actuellement gérées (EUR/USD/DZD), le nombre de décimales appliqué est identique aujourd'hui, les deux
/// usages doivent rester conceptuellement et nommément séparés pour ne jamais être confondus, et pour que
/// l'ajout futur d'une devise à nombre de décimales différent (ex: une devise sans sous-unité) n'oblige
/// pas à toucher au calcul réglementaire. Centralisé ICI (moteur de calcul) : ne jamais dupliquer une
/// règle d'arrondi dans un ViewModel ou un générateur de rapport.
/// </summary>
public static class CurrencyRounding
{
    // Nombre de décimales standard par devise (convention ISO 4217). Par défaut 2 décimales, qui couvre
    // toutes les devises actuellement gérées par l'application (EUR, USD, DZD). Reste extensible à une
    // devise future sans sous-unité (ex: 0 décimale) sans impacter les autres devises.
    private static readonly IReadOnlyDictionary<string, int> DecimalPlacesByCurrency =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["DZD"] = 2,
            ["EUR"] = 2,
            ["USD"] = 2
        };

    /// <summary>Arrondit <paramref name="amount"/> selon le nombre de décimales standard de <paramref name="currencyCode"/>.</summary>
    public static decimal Round(decimal amount, string? currencyCode)
    {
        int decimals = !string.IsNullOrWhiteSpace(currencyCode) && DecimalPlacesByCurrency.TryGetValue(currencyCode, out int configuredDecimals)
            ? configuredDecimals
            : 2; // Valeur par défaut prudente pour toute devise non encore répertoriée explicitement.

        return Math.Round(amount, decimals, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// Convertisseur de devises versionné avec contrôle ALCES et alerte Taux Manuel (Sections 13 & 14).
/// Base juridique : Art. 16 decies du Code des Douanes (Loi n° 17-04 du 16 février 2017, JORA n° 11).
/// Usage EXCLUSIF : conversion réglementaire/douanière d'une devise vers DZD (alimente la valeur en
/// douane, les droits et les taxes). Pour toute conversion commerciale entre deux devises quelconques
/// (ex: EUR -&gt; USD, Section 6 du plan multi-devises), voir <see cref="CurrencyConversionService"/> —
/// les deux classes partagent la même logique de normalisation via <see cref="ExchangeRateNormalization"/>
/// pour ne jamais dupliquer une formule de conversion (Section 8).
/// </summary>
public sealed class CurrencyCalculator
{
    private readonly IExchangeRateProvider _rateProvider;

    public CurrencyCalculator(IExchangeRateProvider rateProvider)
    {
        _rateProvider = rateProvider ?? throw new ArgumentNullException(nameof(rateProvider));
    }

    public (decimal EffectiveRateToDzd, ExchangeRateRecord? OfficialRecord, CalculationAnomaly? Anomaly) ResolveRate(
        string currencyCode,
        DateOnly referenceDate,
        decimal? manualOverrideRate)
    {
        if (string.Equals(currencyCode, "DZD", StringComparison.OrdinalIgnoreCase))
        {
            return (1.0m, null, null);
        }

        var official = _rateProvider.GetRegulatoryRate(currencyCode, referenceDate);

        return ExchangeRateNormalization.Resolve(
            official,
            manualOverrideRate,
            currencyCode,
            "DZD",
            referenceDate,
            missingRateAnomalyCode: "MISSING_EXCHANGE_RATE",
            manualDiffAnomalyCode: "MANUAL_EXCHANGE_RATE_DIFF");
    }

    /// <summary>
    /// Arrondi RÉSERVÉ aux montants réellement exprimés en DZD (valeur en douane, droits, taxes, coût de
    /// revient réel...). Pour un montant dans une autre devise (EUR, USD...), utiliser
    /// <see cref="CurrencyRounding.Round"/> à la place (point 2 de l'audit du 2026-10-01).
    /// </summary>
    public static decimal RoundDzd(decimal amount) => CurrencyRounding.Round(amount, "DZD");
}

/// <summary>
/// Service CENTRALISÉ de conversion entre devises quelconques (Section 8 du plan multi-devises) :
/// "Convert(amount, fromCurrency, toCurrency, rate)". Couvre notamment la conversion COMMERCIALE
/// (ex: EUR -&gt; USD pour la valeur de l'autorisation d'importation, Section 5 & 13) et reste
/// architecturalement extensible à toute autre paire de devises (USD -&gt; EUR, DZD -&gt; USD...).
/// Ne doit JAMAIS être utilisé pour la conversion réglementaire/douanière vers DZD qui reste la
/// responsabilité exclusive de <see cref="CurrencyCalculator"/> (Section 14 & 22 : "ne pas mélanger
/// conversion commerciale et conversion réglementaire").
/// </summary>
public sealed class CurrencyConversionService
{
    private readonly IExchangeRateProvider _rateProvider;

    public CurrencyConversionService(IExchangeRateProvider rateProvider)
    {
        _rateProvider = rateProvider ?? throw new ArgumentNullException(nameof(rateProvider));
    }

    /// <summary>Résout le taux effectif pour convertir <paramref name="fromCurrencyCode"/> vers <paramref name="toCurrencyCode"/>.</summary>
    public (decimal EffectiveRate, ExchangeRateRecord? OfficialRecord, CalculationAnomaly? Anomaly) ResolveCrossRate(
        string fromCurrencyCode,
        string toCurrencyCode,
        DateOnly referenceDate,
        decimal? manualOverrideRate)
    {
        if (string.Equals(fromCurrencyCode, toCurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            return (1.0m, null, null);
        }

        var official = _rateProvider.GetRate(fromCurrencyCode, toCurrencyCode, referenceDate);

        return ExchangeRateNormalization.Resolve(
            official,
            manualOverrideRate,
            fromCurrencyCode,
            toCurrencyCode,
            referenceDate,
            missingRateAnomalyCode: "MISSING_COMMERCIAL_EXCHANGE_RATE",
            manualDiffAnomalyCode: "MANUAL_COMMERCIAL_EXCHANGE_RATE_DIFF");
    }

    /// <summary>
    /// Convertit un montant d'une devise vers une autre en appliquant le taux résolu (officiel ou manuel).
    /// Ne modifie jamais la valeur/devise d'origine fournie par l'appelant (Section 4 : "ne jamais
    /// remplacer la devise originale de la facture par une conversion") — se contente de retourner le
    /// résultat converti à côté de l'original.
    /// </summary>
    public CurrencyConversionOutcome Convert(
        decimal amount,
        string fromCurrencyCode,
        string toCurrencyCode,
        DateOnly referenceDate,
        decimal? manualOverrideRate)
    {
        var (rate, official, anomaly) = ResolveCrossRate(fromCurrencyCode, toCurrencyCode, referenceDate, manualOverrideRate);
        // Correction (point 2 de l'audit) : le montant converti est exprimé en toCurrencyCode (ex: USD),
        // jamais en DZD — il doit donc être arrondi selon la devise CIBLE, pas via RoundDzd.
        decimal convertedAmount = CurrencyRounding.Round(amount * rate, toCurrencyCode);

        return new CurrencyConversionOutcome(
            OriginalAmount: amount,
            FromCurrencyCode: fromCurrencyCode,
            ToCurrencyCode: toCurrencyCode,
            EffectiveRate: rate,
            ConvertedAmount: convertedAmount,
            IsManualRate: manualOverrideRate.HasValue && manualOverrideRate.Value > 0m,
            OfficialRecord: official,
            Anomaly: anomaly);
    }
}

/// <summary>Résultat traçable d'une conversion commerciale entre deux devises (Section 7 & 8).</summary>
public sealed record CurrencyConversionOutcome(
    decimal OriginalAmount,
    string FromCurrencyCode,
    string ToCurrencyCode,
    decimal EffectiveRate,
    decimal ConvertedAmount,
    bool IsManualRate,
    ExchangeRateRecord? OfficialRecord,
    CalculationAnomaly? Anomaly);

/// <summary>
/// Moteur de répartition configurable des frais (Sections 11 & 12).
/// Supporte : Par valeur, Par quantité, Par poids, Par volume, Montant fixe, Pourcentage, Manuelle.
/// </summary>
public sealed class CostAllocationEngine
{
    public IReadOnlyDictionary<Guid, FeeAllocationTrace> AllocateFeeAcrossLines(
        ImportFee fee,
        decimal feeAmountDzd,
        IReadOnlyList<(ImportLine Line, decimal LinePurchaseDzd)> linesWithPurchaseDzd,
        List<CalculationAnomaly> anomalies)
    {
        var result = new Dictionary<Guid, FeeAllocationTrace>();
        if (linesWithPurchaseDzd.Count == 0 || feeAmountDzd == 0m)
        {
            return result;
        }

        decimal[] weights = new decimal[linesWithPurchaseDzd.Count];

        switch (fee.AllocationMethod)
        {
            case FeeAllocationMethod.ByValue:
                for (int i = 0; i < linesWithPurchaseDzd.Count; i++)
                    weights[i] = linesWithPurchaseDzd[i].LinePurchaseDzd;
                break;

            case FeeAllocationMethod.ByQuantity:
                for (int i = 0; i < linesWithPurchaseDzd.Count; i++)
                    weights[i] = linesWithPurchaseDzd[i].Line.Quantity;
                break;

            case FeeAllocationMethod.ByWeight:
                if (linesWithPurchaseDzd.Any(x => !x.Line.LineGrossWeightKg.HasValue || x.Line.LineGrossWeightKg.Value <= 0m))
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Blocage,
                        "MISSING_WEIGHT_FOR_WEIGHT_ALLOCATION",
                        $"⚠️ Le frais '{fee.FeeName}' est configuré pour une répartition PAR POIDS, mais le poids est absent sur une ou plusieurs lignes."));
                    return result;
                }
                for (int i = 0; i < linesWithPurchaseDzd.Count; i++)
                    weights[i] = linesWithPurchaseDzd[i].Line.LineGrossWeightKg!.Value;
                break;

            case FeeAllocationMethod.ByVolume:
                if (linesWithPurchaseDzd.Any(x => !x.Line.LineVolumeM3.HasValue || x.Line.LineVolumeM3.Value <= 0m))
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Blocage,
                        "MISSING_VOLUME_FOR_VOLUME_ALLOCATION",
                        $"⚠️ Le frais '{fee.FeeName}' est configuré pour une répartition PAR VOLUME, mais le volume est absent sur une ou plusieurs lignes."));
                    return result;
                }
                for (int i = 0; i < linesWithPurchaseDzd.Count; i++)
                    weights[i] = linesWithPurchaseDzd[i].Line.LineVolumeM3!.Value;
                break;

            case FeeAllocationMethod.FixedAmount:
            case FeeAllocationMethod.Percentage:
                for (int i = 0; i < linesWithPurchaseDzd.Count; i++)
                    weights[i] = 1m;
                break;

            case FeeAllocationMethod.Manual:
                for (int i = 0; i < linesWithPurchaseDzd.Count; i++)
                {
                    var line = linesWithPurchaseDzd[i].Line;
                    weights[i] = line.ManualFeeAllocationsDzd.TryGetValue(fee.Id, out decimal manualVal) ? manualVal : 0m;
                }
                break;
        }

        decimal totalWeight = weights.Sum();
        if (totalWeight <= 0m)
        {
            anomalies.Add(new CalculationAnomaly(
                AnomalySeverity.Erreur,
                "INCONSISTENT_DATA",
                $"⚠️ Base de répartition nulle pour le frais '{fee.FeeName}' (méthode {fee.AllocationMethod})."));
            return result;
        }

        decimal runningAllocated = 0m;
        for (int i = 0; i < linesWithPurchaseDzd.Count; i++)
        {
            var line = linesWithPurchaseDzd[i].Line;
            decimal ratio = weights[i] / totalWeight;
            decimal allocated = (i == linesWithPurchaseDzd.Count - 1)
                ? CurrencyCalculator.RoundDzd(feeAmountDzd - runningAllocated)
                : CurrencyCalculator.RoundDzd(feeAmountDzd * ratio);

            runningAllocated += allocated;

            result[line.Id] = new FeeAllocationTrace(
                FeeId: fee.Id,
                FeeName: fee.FeeName,
                MethodUsed: fee.AllocationMethod,
                ShareRatio: Math.Round(ratio, 8, MidpointRounding.AwayFromZero),
                AllocatedAmountDzd: allocated,
                IncludedInCustomsValue: fee.IncludeInCustomsValue,
                IncludedInCostOfGoods: fee.IncludeInCostOfGoods);
        }

        return result;
    }
}

/// <summary>
/// Calculateur de la Valeur en Douane selon les articles 16 bis, 16 ter et 16 octies du Code des Douanes Algérien.
/// Vérifie également la cohérence des frais requis selon l'Incoterm sélectionné (EXW, FOB, CFR en V1).
/// </summary>
public sealed class CustomsValueCalculator
{
    public void ValidateIncotermRequiredFees(ImportOperation operation, List<CalculationAnomaly> anomalies)
    {
        bool hasFreight = operation.Fees.Any(f =>
            string.Equals(f.FeeCategoryCode, "FRET_INTERNATIONAL", StringComparison.OrdinalIgnoreCase) && f.Amount > 0m);
        bool hasInsurance = operation.Fees.Any(f =>
            string.Equals(f.FeeCategoryCode, "ASSURANCE", StringComparison.OrdinalIgnoreCase) && f.Amount > 0m);
        bool hasExwExportFees = operation.Fees.Any(f =>
            (string.Equals(f.FeeCategoryCode, "TRANSPORT_INTERIEUR_EXPORT", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(f.FeeCategoryCode, "FRAIS_EXPORT", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(f.FeeCategoryCode, "MANUTENTION_EXPORT", StringComparison.OrdinalIgnoreCase)) && f.Amount > 0m);

        switch (operation.Incoterm)
        {
            case IncotermCode.EXW:
                if (!hasExwExportFees || !hasFreight)
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Erreur,
                        "EXW_MISSING_REQUIRED_FEES",
                        "⚠️ Incoterm EXW mais frais nécessaires manquants (transport intérieur export / frais export / manutention chargement / fret international requis selon Art. 16 octies §1 e) du Code des Douanes)."));
                }
                if (!hasInsurance)
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        "EXW_MISSING_INSURANCE",
                        "⚠️ Incoterm EXW : Assurance transport international absente."));
                }
                break;

            case IncotermCode.FOB:
                if (!hasFreight)
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Erreur,
                        "FOB_MISSING_FREIGHT",
                        "⚠️ Incoterm FOB mais fret international absent (requis pour déterminer la valeur en douane au lieu d'introduction en Algérie selon Art. 16 octies §1 e) CDA)."));
                }
                if (!hasInsurance)
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        "FOB_MISSING_INSURANCE",
                        "⚠️ Incoterm FOB mais assurance transport international absente."));
                }
                break;

            case IncotermCode.CFR:
                if (!hasInsurance)
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        "CFR_MISSING_INSURANCE",
                        "⚠️ Incoterm CFR mais assurance absente (Art. 16 octies §1 e) du Code des Douanes)."));
                }

                // Revue du 2026-10-02 (cas de référence D10 réel, Section 6 — CRITIQUE : éviter le double
                // comptage du fret). En Incoterm CFR, le prix facturé (PTFN) inclut DÉJÀ le fret jusqu'au
                // point convenu : un frais "FRET_INTERNATIONAL" ajouté EN PLUS avec un traitement
                // "Addition_Art16Octies" additionnerait une seconde fois un fret déjà intégré au prix. Ne
                // jamais corriger silencieusement : on avertit explicitement l'utilisateur pour qu'il
                // confirme si ce fret est réellement un complément non compris dans le prix CFR, ou s'il
                // doit être requalifié en "Déjà inclus dans le prix facturé" (CustomsAdjustmentTreatment
                // .IncludedInInvoicePrice, IncludeInCustomsValue = false).
                foreach (var possibleDoubleCountedFreight in operation.Fees.Where(f =>
                    f.FeeCategoryCode.Contains("FRET", StringComparison.OrdinalIgnoreCase) &&
                    f.Amount > 0m &&
                    f.IncludeInCustomsValue &&
                    f.CustomsTreatment == CustomsAdjustmentTreatment.Addition_Art16Octies))
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        "CFR_POSSIBLE_FREIGHT_DOUBLE_COUNT",
                        $"⚠️ Incoterm CFR : le frais '{possibleDoubleCountedFreight.FeeName}' est ajouté EN PLUS à la valeur en douane (traitement \"Addition\"). En CFR, le prix facturé inclut normalement déjà le fret jusqu'au point convenu — vérifiez qu'il ne s'agit pas d'un double comptage. Si ce fret est déjà compris dans le prix facturé, requalifiez ce frais en \"Déjà inclus dans le prix facturé\" (ne pas l'ajouter à la valeur en douane)."));
                }
                break;
        }
    }

    public decimal CalculateLineCustomsValueDzd(
        decimal linePurchaseValueDzd,
        IReadOnlyList<FeeAllocationTrace> lineFeeAllocations,
        IReadOnlyDictionary<Guid, ImportFee> feeDefinitionsById)
    {
        decimal additions = 0m;
        decimal deductions = 0m;

        foreach (var alloc in lineFeeAllocations)
        {
            if (!feeDefinitionsById.TryGetValue(alloc.FeeId, out var feeDef))
                continue;

            if (feeDef.IncludeInCustomsValue && feeDef.CustomsTreatment == CustomsAdjustmentTreatment.Addition_Art16Octies)
            {
                additions += alloc.AllocatedAmountDzd;
            }
            else if (feeDef.CustomsTreatment == CustomsAdjustmentTreatment.Deduction_Art16Ter_Octies3)
            {
                deductions += alloc.AllocatedAmountDzd;
            }
        }

        return CurrencyCalculator.RoundDzd(linePurchaseValueDzd + additions - deductions);
    }
}

/// <summary>
/// Orchestrateur principal du moteur de calcul (Section 3, 22-28).
/// Strictement indépendant de toute interface graphique (UI).
/// </summary>
public sealed class ImportCalculationOrchestrator
{
    public const string OfficialLegalDisclaimer =
        "Les résultats sont calculés à partir des données saisies, des règles réglementaires enregistrées dans le système et des paramètres sélectionnés. Ils doivent être vérifiés au regard de la déclaration et des documents douaniers officiels applicables à l'opération.";

    private readonly CurrencyCalculator _currencyCalculator;
    private readonly CostAllocationEngine _allocationEngine;
    private readonly CustomsValueCalculator _customsValueCalculator;
    private readonly RegulatoryRuleEngine _regulatoryEngine;
    private readonly CurrencyConversionService _commercialConversionService;

    public ImportCalculationOrchestrator(
        CurrencyCalculator currencyCalculator,
        CostAllocationEngine allocationEngine,
        CustomsValueCalculator customsValueCalculator,
        RegulatoryRuleEngine regulatoryEngine,
        CurrencyConversionService commercialConversionService)
    {
        _currencyCalculator = currencyCalculator;
        _allocationEngine = allocationEngine;
        _customsValueCalculator = customsValueCalculator;
        _regulatoryEngine = regulatoryEngine;
        _commercialConversionService = commercialConversionService;
    }

    public ImportCalculationSummary ExecuteCalculation(Company company, ImportOperation operation)
    {
        var anomalies = new List<CalculationAnomaly>();

        // 1. Contrôle Incoterm & champs dynamiques (Sections 7, 8, 28)
        _customsValueCalculator.ValidateIncotermRequiredFees(operation, anomalies);

        // 2. Résolution du taux de change principal RÉGLEMENTAIRE (Sections 13 & 14) : devise facture -> DZD.
        // C'est la SEULE conversion qui alimente la valeur en douane / droits / taxes ci-dessous.
        var (mainRateToDzd, _, mainRateAnomaly) = _currencyCalculator.ResolveRate(
            operation.MainCurrencyCode,
            operation.ReferenceDate,
            operation.ManualExchangeRateOverride);

        if (mainRateAnomaly != null)
            anomalies.Add(mainRateAnomaly);

        // 2bis. Résolution du taux de change COMMERCIAL (Section 6 & 14 du plan multi-devises) : devise
        // facture -> devise de l'autorisation d'importation (ex: EUR -> USD). Totalement indépendant de la
        // conversion réglementaire ci-dessus (Section 22 : "ne pas mélanger"). N'alimente JAMAIS
        // CustomsOutcome / EconomicOutcome : reste une information séparée, affichée et exportée à part.
        bool needsAuthorizationConversion = !string.IsNullOrWhiteSpace(operation.AuthorizationCurrencyCode)
            && !string.Equals(operation.AuthorizationCurrencyCode, operation.MainCurrencyCode, StringComparison.OrdinalIgnoreCase);

        decimal authorizationRate = 1.0m;
        bool authorizationRateIsManual = false;
        ExchangeRateRecord? authorizationOfficialRate = null;

        if (needsAuthorizationConversion)
        {
            var (rate, official, commercialAnomaly) = _commercialConversionService.ResolveCrossRate(
                operation.MainCurrencyCode,
                operation.AuthorizationCurrencyCode,
                operation.ReferenceDate,
                operation.ManualAuthorizationExchangeRateOverride);

            authorizationRate = rate;
            authorizationOfficialRate = official;
            authorizationRateIsManual = operation.ManualAuthorizationExchangeRateOverride.HasValue
                && operation.ManualAuthorizationExchangeRateOverride.Value > 0m;

            if (commercialAnomaly != null)
            {
                // La conversion commerciale est informative : une absence de taux ne doit jamais bloquer le
                // calcul douanier réel (seul un taux réglementaire manquant le peut, Section 2bis/22).
                anomalies.Add(commercialAnomaly.Severity == AnomalySeverity.Blocage
                    ? commercialAnomaly with { Severity = AnomalySeverity.Avertissement }
                    : commercialAnomaly);
            }
        }

        // 3. Calcul de la valeur d'achat par ligne (en devise et convertie en DZD)
        var linesWithPurchaseDzd = new List<(ImportLine Line, decimal LinePurchaseCurrency, decimal LinePurchaseDzd, decimal LineRate)>();
        foreach (var line in operation.Lines.OrderBy(l => l.LineNumber))
        {
            if (string.IsNullOrWhiteSpace(line.OriginCountryIso2) && string.IsNullOrWhiteSpace(operation.DefaultOriginCountryIso2))
            {
                anomalies.Add(new CalculationAnomaly(
                    AnomalySeverity.Erreur,
                    "MISSING_ORIGIN",
                    $"⚠️ Origine absente pour l'article '{line.ProductReference}' (Ligne {line.LineNumber}).",
                    LineNumber: line.LineNumber));
            }

            if (line.AiHsDecision == AiProposalDecision.PendingUserValidation && !string.IsNullOrWhiteSpace(line.AiProposedHsCode10))
            {
                anomalies.Add(new CalculationAnomaly(
                    AnomalySeverity.Blocage,
                    "UNCONFIRMED_AI_HS_CODE",
                    $"⚠️ Code SH proposé par IA ({line.AiProposedHsCode10}) non confirmé par l'utilisateur pour l'article '{line.ProductReference}'.",
                    LineNumber: line.LineNumber));
            }

            var (lineRate, _, lineRateAnomaly) = string.Equals(line.CurrencyCode, operation.MainCurrencyCode, StringComparison.OrdinalIgnoreCase)
                ? (mainRateToDzd, null, null)
                : _currencyCalculator.ResolveRate(line.CurrencyCode, operation.ReferenceDate, null);

            if (lineRateAnomaly != null)
                anomalies.Add(lineRateAnomaly with { LineNumber = line.LineNumber });

            // Correction (point 2 de l'audit) : purchaseCurrency est exprimé dans la devise D'ORIGINE de la
            // ligne (line.CurrencyCode, ex: EUR/USD), jamais en DZD à ce stade — seul purchaseDzd (après
            // application du taux réglementaire) est réellement un montant DZD.
            decimal purchaseCurrency = CurrencyRounding.Round(line.Quantity * line.UnitPurchasePrice, line.CurrencyCode);
            decimal purchaseDzd = CurrencyCalculator.RoundDzd(purchaseCurrency * lineRate);
            linesWithPurchaseDzd.Add((line, purchaseCurrency, purchaseDzd, lineRate));
        }

        // 4. Conversion et répartition de chaque frais selon sa propre méthode (Sections 9 & 11)
        var feeDefinitionsById = operation.Fees.ToDictionary(f => f.Id);
        var allocationsByLineId = operation.Lines.ToDictionary(l => l.Id, _ => new List<FeeAllocationTrace>());

        var purchasePairs = linesWithPurchaseDzd.Select(x => (x.Line, x.LinePurchaseDzd)).ToList();
        foreach (var fee in operation.Fees)
        {
            var (feeRate, _, feeRateAnomaly) = string.Equals(fee.CurrencyCode, operation.MainCurrencyCode, StringComparison.OrdinalIgnoreCase)
                ? (mainRateToDzd, null, null)
                : _currencyCalculator.ResolveRate(fee.CurrencyCode, operation.ReferenceDate, null);

            if (feeRateAnomaly != null)
                anomalies.Add(feeRateAnomaly);

            decimal feeDzd = CurrencyCalculator.RoundDzd(fee.Amount * feeRate);
            var perLineAlloc = _allocationEngine.AllocateFeeAcrossLines(fee, feeDzd, purchasePairs, anomalies);
            foreach (var kvp in perLineAlloc)
            {
                allocationsByLineId[kvp.Key].Add(kvp.Value);
            }
        }

        // 5. Calcul ligne par ligne : Valeur en douane, Droits, Taxes, TVA, Coût douanier et Coût de revient (Sections 22-27)
        var lineResults = new List<LineFullCalculationResult>();

        foreach (var (line, purchaseCurrency, purchaseDzd, lineRate) in linesWithPurchaseDzd)
        {
            var lineAllocations = allocationsByLineId[line.Id];
            decimal customsValueDzd = _customsValueCalculator.CalculateLineCustomsValueDzd(
                purchaseDzd,
                lineAllocations,
                feeDefinitionsById);

            string? effectiveOrigin = line.OriginCountryIso2 ?? operation.DefaultOriginCountryIso2;
            var regOutcome = _regulatoryEngine.ResolveApplicableRules(new RegulatoryLookupQuery(
                HsCode10: line.HsCodeConfirmed10 ?? string.Empty,
                OriginCountryIso2: effectiveOrigin,
                OperationReferenceDate: operation.ReferenceDate,
                CustomsRegimeCode: operation.CustomsRegimeCode));

            foreach (var warn in regOutcome.WarningsOrMissingInfo)
            {
                anomalies.Add(new CalculationAnomaly(
                    regOutcome.IsDetermined ? AnomalySeverity.Avertissement : AnomalySeverity.Blocage,
                    string.IsNullOrWhiteSpace(line.HsCodeConfirmed10) ? "MISSING_HS_CODE" : "REGULATORY_RULE_NOT_FOUND",
                    $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : {warn}",
                    LineNumber: line.LineNumber));
            }

            // Comparaison Droit Excel vs Droit Réglementaire (Section 17)
            decimal effectiveDutyRate = 0m;
            DutyComparisonStatus comparisonStatus = DutyComparisonStatus.Match;
            string comparisonLabel = "✓ Correspondance";

            if (regOutcome.CustomsDutyRule != null)
            {
                effectiveDutyRate = regOutcome.CustomsDutyRule.RatePercent;
                if (line.ExcelDutyRatePercent.HasValue)
                {
                    if (line.ExcelDutyRatePercent.Value == effectiveDutyRate)
                    {
                        comparisonStatus = DutyComparisonStatus.Match;
                        comparisonLabel = $"✓ Correspondance ({effectiveDutyRate:F2} %)";
                    }
                    else
                    {
                        comparisonStatus = DutyComparisonStatus.Difference;
                        comparisonLabel = $"⚠️ DIFFÉRENCE (Droit Excel : {line.ExcelDutyRatePercent.Value:F2} % / Droit réglementaire : {effectiveDutyRate:F2} %)";
                        anomalies.Add(new CalculationAnomaly(
                            AnomalySeverity.Avertissement,
                            "EXCEL_VS_REGULATORY_DUTY_DIFF",
                            $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Droit Excel ({line.ExcelDutyRatePercent.Value:F2} %) différent du droit réglementaire officiel ({effectiveDutyRate:F2} %). Le taux réglementaire est appliqué.",
                            LineNumber: line.LineNumber,
                            ExpectedValue: $"{effectiveDutyRate:F2}%",
                            ActualValue: $"{line.ExcelDutyRatePercent.Value:F2}%"));
                    }
                }
            }
            else if (line.ExcelDutyRatePercent.HasValue && line.UserConfirmedExcelDutyFallback)
            {
                effectiveDutyRate = line.ExcelDutyRatePercent.Value;
                comparisonStatus = DutyComparisonStatus.ExcelFallbackConfirmedByUser;
                comparisonLabel = $"⚠️ Taux Excel ({effectiveDutyRate:F2} %) utilisé sur confirmation explicite de l'utilisateur";
            }
            else
            {
                comparisonStatus = DutyComparisonStatus.RegulatoryNotFoundPendingConfirmation;
                comparisonLabel = "INFORMATION NON DÉTERMINÉE (Confirmation utilisateur requise)";
            }

            // 4. Droit de douane (DD) = Valeur en douane × Taux DD
            decimal customsDutyDzd = CurrencyCalculator.RoundDzd(customsValueDzd * (effectiveDutyRate / 100m));

            // 5. Taxes additionnelles AVANT TVA (CS, DAPS, TIC, RDAE...) : assiette Valeur douane, ou
            // Valeur douane + DD, selon la règle (comportement inchangé). Revue du 2026-10-02 (cas de
            // référence D10 réel) : une règle dont l'assiette est explicitement
            // TaxableBaseType.CustomsValuePlusPriorTaxesPlusVatDzd (ex: PRCT) a besoin du montant de TVA,
            // pas encore connu ici — elle est donc calculée PLUS BAS, après la TVA (point 6bis).
            var beforeVatTaxRules = regOutcome.AdditionalTaxRules
                .Where(r => r.CalculationBase != TaxableBaseType.CustomsValuePlusPriorTaxesPlusVatDzd)
                .ToList();
            var afterVatTaxRules = regOutcome.AdditionalTaxRules
                .Where(r => r.CalculationBase == TaxableBaseType.CustomsValuePlusPriorTaxesPlusVatDzd)
                .ToList();

            var additionalTaxBreakdowns = new List<AppliedTaxBreakdown>();
            foreach (var taxRule in beforeVatTaxRules)
            {
                decimal taxBase = taxRule.CalculationBase == TaxableBaseType.CustomsValueDzd
                    ? customsValueDzd
                    : customsValueDzd + customsDutyDzd;
                decimal taxAmount = CurrencyCalculator.RoundDzd(taxBase * (taxRule.RatePercent / 100m));

                additionalTaxBreakdowns.Add(new AppliedTaxBreakdown(
                    TaxCode: taxRule.TaxCode,
                    TaxNameFr: taxRule.TaxNameFr,
                    TaxableBaseDzd: taxBase,
                    RatePercent: taxRule.RatePercent,
                    TaxAmountDzd: taxAmount,
                    IsNonRecoverable: true,
                    RegulatoryRuleCode: taxRule.Code,
                    LegalArticleReference: taxRule.LegalSource.ArticleReference,
                    JoraReference: taxRule.LegalSource.JoraReference,
                    RegulatoryVersionCode: taxRule.RegulatoryVersionCode,
                    OriginTag: DataOriginTag.DonneeOfficielle));
            }

            // Revue du 2026-10-01 (point 5 & 6) : statut explicite des taxes additionnelles "standard"
            // (CS, PRCT, TCS, DAPS) pour affichage sur l'écran Importation — construit uniquement à partir
            // du résultat déjà résolu ci-dessus (regOutcome), sans recalcul ni invention de taux.
            var standardTaxApplicability = RegulatoryRuleEngine.BuildStandardTaxApplicabilityReport(
                regOutcome, RegulatoryRuleEngine.StandardAdditionalTaxCodes);

            bool HasOfficialOutcomeFor(string taxCode) => standardTaxApplicability.Any(s =>
                string.Equals(s.TaxCode, taxCode, StringComparison.OrdinalIgnoreCase) && s.Kind != TaxApplicabilityKind.DonneeManquante);

            // Revue du 2026-10-02 (Section 15) : TCS confirmée MANUELLEMENT UNE SEULE FOIS pour toute
            // l'importation (operation.ManualTcsRatePercent), appliquée uniquement si aucune règle officielle
            // (applicable ou explicitement non applicable) n'existe pour ce code SH — jamais pour écraser
            // une règle officielle. Base conservatrice (Valeur douane) faute de preuve réglementaire d'une
            // autre assiette pour la TCS dans le cas de référence D10.
            if (!HasOfficialOutcomeFor("TCS"))
            {
                if (operation.UserConfirmedManualTcs && operation.ManualTcsRatePercent.HasValue)
                {
                    decimal manualTcsBase = customsValueDzd;
                    decimal manualTcsAmount = CurrencyCalculator.RoundDzd(manualTcsBase * (operation.ManualTcsRatePercent.Value / 100m));
                    additionalTaxBreakdowns.Add(new AppliedTaxBreakdown(
                        TaxCode: "TCS",
                        TaxNameFr: "Taxe de Contribution de Solidarité (TCS)",
                        TaxableBaseDzd: manualTcsBase,
                        RatePercent: operation.ManualTcsRatePercent.Value,
                        TaxAmountDzd: manualTcsAmount,
                        IsNonRecoverable: true,
                        RegulatoryRuleCode: "MANUEL",
                        LegalArticleReference: "Taux saisi manuellement par l'utilisateur (aucune règle officielle publiée)",
                        JoraReference: "N/A",
                        RegulatoryVersionCode: "MANUEL",
                        OriginTag: DataOriginTag.DonneeUtilisateur));
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        "TCS_MANUAL_RATE_USED",
                        $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Taux TCS ({operation.ManualTcsRatePercent.Value:F2} %) saisi manuellement pour cette importation, en l'absence de règle officielle.",
                        LineNumber: line.LineNumber));
                }
                else
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        "TCS_RATE_NOT_DETERMINED",
                        $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Donnée réglementaire manquante pour la Taxe de Contribution de Solidarité (TCS) — confirmez un taux pour cette importation (écran Importation) ou laissez non déterminé.",
                        LineNumber: line.LineNumber));
                }
            }

            decimal totalBeforeVatAdditionalTaxesDzd = additionalTaxBreakdowns.Sum(t => t.TaxAmountDzd);

            // 6. TVA à l'importation (Art. 19 CTCA : Assiette = Valeur en douane + Droits de douane + Taxes hors TVA)
            decimal vatTaxableBaseDzd = CurrencyCalculator.RoundDzd(customsValueDzd + customsDutyDzd + totalBeforeVatAdditionalTaxesDzd);
            decimal appliedVatRate;
            DataOriginTag vatRateOriginTag;
            if (regOutcome.VatRule != null)
            {
                appliedVatRate = regOutcome.VatRule.RatePercent;
                vatRateOriginTag = DataOriginTag.DonneeOfficielle;
            }
            else if (line.UserConfirmedManualVatRate && line.ManualVatRatePercent.HasValue)
            {
                // Revue du 2026-10-02 (Section 12) : taux de TVA confirmé manuellement en l'absence de règle
                // officielle — jamais un 0 % silencieux : tracé comme donnée utilisateur et toujours accompagné
                // d'une anomalie explicite (motif d'exonération exigé si le taux confirmé est 0 %).
                appliedVatRate = line.ManualVatRatePercent.Value;
                vatRateOriginTag = DataOriginTag.DonneeUtilisateur;

                if (appliedVatRate == 0m && string.IsNullOrWhiteSpace(line.VatExemptionReasonFr))
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        "VAT_EXEMPTION_REASON_MISSING",
                        $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Exonération de TVA (0 %) confirmée sans motif précisé — renseignez le motif d'exonération.",
                        LineNumber: line.LineNumber));
                }
                else if (appliedVatRate == 0m)
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Info,
                        "VAT_MANUAL_EXEMPTION_CONFIRMED",
                        $"ℹ️ Ligne {line.LineNumber} ({line.ProductReference}) : Exonération de TVA confirmée manuellement — motif : {line.VatExemptionReasonFr}.",
                        LineNumber: line.LineNumber));
                }
                else
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        "MANUAL_VAT_RATE_USED",
                        $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Taux de TVA ({appliedVatRate:F2} %) saisi manuellement, en l'absence de règle officielle.",
                        LineNumber: line.LineNumber));
                }
            }
            else
            {
                // Aucune règle officielle ni confirmation manuelle : la ligne reste BLOQUANTE (voir la
                // boucle regOutcome.WarningsOrMissingInfo ci-dessus -> AnomalySeverity.Blocage
                // "TVA réglementaire introuvable") — ce 0 % n'est JAMAIS un résultat définitif à afficher
                // comme tel (voir LineDetailDialogViewModel.TvaStatutFr côté IHM).
                appliedVatRate = 0m;
                vatRateOriginTag = DataOriginTag.CalculDuLogiciel;
            }

            decimal importVatDzd = CurrencyCalculator.RoundDzd(vatTaxableBaseDzd * (appliedVatRate / 100m));

            // 6bis. Taxes additionnelles APRÈS TVA — assiette Valeur douane + taxes avant TVA + TVA, observée
            // pour le PRCT sur le D10 de référence (SARL HYMA TRADE). Calculées ICI car leur assiette a
            // besoin de importVatDzd, désormais connu.
            foreach (var taxRule in afterVatTaxRules)
            {
                decimal taxBase = CurrencyCalculator.RoundDzd(customsValueDzd + totalBeforeVatAdditionalTaxesDzd + importVatDzd);
                decimal taxAmount = CurrencyCalculator.RoundDzd(taxBase * (taxRule.RatePercent / 100m));

                additionalTaxBreakdowns.Add(new AppliedTaxBreakdown(
                    TaxCode: taxRule.TaxCode,
                    TaxNameFr: taxRule.TaxNameFr,
                    TaxableBaseDzd: taxBase,
                    RatePercent: taxRule.RatePercent,
                    TaxAmountDzd: taxAmount,
                    IsNonRecoverable: true,
                    RegulatoryRuleCode: taxRule.Code,
                    LegalArticleReference: taxRule.LegalSource.ArticleReference,
                    JoraReference: taxRule.LegalSource.JoraReference,
                    RegulatoryVersionCode: taxRule.RegulatoryVersionCode,
                    OriginTag: DataOriginTag.DonneeOfficielle));
            }

            // Revue du 2026-10-02 (Section 15) : PRCT confirmé MANUELLEMENT UNE SEULE FOIS pour toute
            // l'importation, avec la même assiette (après TVA) que la règle officielle observée sur le D10.
            if (!HasOfficialOutcomeFor("PRCT"))
            {
                if (operation.UserConfirmedManualPrct && operation.ManualPrctRatePercent.HasValue)
                {
                    decimal manualPrctBase = CurrencyCalculator.RoundDzd(customsValueDzd + totalBeforeVatAdditionalTaxesDzd + importVatDzd);
                    decimal manualPrctAmount = CurrencyCalculator.RoundDzd(manualPrctBase * (operation.ManualPrctRatePercent.Value / 100m));
                    additionalTaxBreakdowns.Add(new AppliedTaxBreakdown(
                        TaxCode: "PRCT",
                        TaxNameFr: "Précompte à l'importation (PRCT)",
                        TaxableBaseDzd: manualPrctBase,
                        RatePercent: operation.ManualPrctRatePercent.Value,
                        TaxAmountDzd: manualPrctAmount,
                        IsNonRecoverable: true,
                        RegulatoryRuleCode: "MANUEL",
                        LegalArticleReference: "Taux saisi manuellement par l'utilisateur (aucune règle officielle publiée)",
                        JoraReference: "N/A",
                        RegulatoryVersionCode: "MANUEL",
                        OriginTag: DataOriginTag.DonneeUtilisateur));
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        "PRCT_MANUAL_RATE_USED",
                        $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Taux PRCT ({operation.ManualPrctRatePercent.Value:F2} %) saisi manuellement pour cette importation, en l'absence de règle officielle.",
                        LineNumber: line.LineNumber));
                }
                else
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        "PRCT_RATE_NOT_DETERMINED",
                        $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Donnée réglementaire manquante pour le Précompte à l'importation (PRCT) — confirmez un taux pour cette importation (écran Importation) ou laissez non déterminé.",
                        LineNumber: line.LineNumber));
                }
            }

            decimal totalAdditionalTaxesDzd = additionalTaxBreakdowns.Sum(t => t.TaxAmountDzd);

            // Résultat 1 (Section 25) : Coût / Total Droits et Taxes douaniers
            decimal totalLineDutiesAndTaxesDzd = CurrencyCalculator.RoundDzd(customsDutyDzd + totalAdditionalTaxesDzd + importVatDzd);
            decimal lineCustomsClearedTotalDzd = CurrencyCalculator.RoundDzd(customsValueDzd + totalLineDutiesAndTaxesDzd);

            var customsOutcome = new LineCustomsResult(
                CustomsValueDzd: customsValueDzd,
                CustomsDutyRatePercent: effectiveDutyRate,
                CustomsDutyAmountDzd: customsDutyDzd,
                ExcelVsRegulatoryComparison: comparisonStatus,
                ComparisonLabelFr: comparisonLabel,
                AdditionalTaxes: additionalTaxBreakdowns,
                TotalAdditionalTaxesDzd: totalAdditionalTaxesDzd,
                VatTaxableBaseDzd: vatTaxableBaseDzd,
                VatRatePercent: appliedVatRate,
                ImportVatAmountDzd: importVatDzd,
                TotalDutiesAndTaxesDzd: totalLineDutiesAndTaxesDzd,
                CustomsValuePlusDutiesAndTaxesDzd: lineCustomsClearedTotalDzd,
                // Section 17 de l'audit : citation légale issue de la règle réglementaire RÉELLEMENT
                // résolue pour cette ligne (jamais une référence générique codée en dur) ; null si aucune
                // règle officielle n'a été trouvée (ex: taux Excel confirmé par défaut de règle officielle).
                CustomsDutyLegalArticleReference: regOutcome.CustomsDutyRule?.LegalSource.ArticleReference,
                CustomsDutyJoraReference: regOutcome.CustomsDutyRule?.LegalSource.JoraReference,
                CustomsDutyRegulatoryVersionCode: regOutcome.CustomsDutyRule?.RegulatoryVersionCode,
                VatLegalArticleReference: regOutcome.VatRule?.LegalSource.ArticleReference,
                VatJoraReference: regOutcome.VatRule?.LegalSource.JoraReference,
                VatRegulatoryVersionCode: regOutcome.VatRule?.RegulatoryVersionCode,
                StandardTaxApplicability: standardTaxApplicability,
                VatRateOriginTag: vatRateOriginTag);

            // Résultat 2 (Section 26) : Coût d'acquisition et Coût de revient économique réel
            decimal feesInCustomsValueDzd = lineAllocations
                .Where(a => a.IncludedInCustomsValue && a.IncludedInCostOfGoods)
                .Sum(a => a.AllocatedAmountDzd);

            decimal localFeesInCostOfGoodsDzd = lineAllocations
                .Where(a => !a.IncludedInCustomsValue && a.IncludedInCostOfGoods)
                .Sum(a => a.AllocatedAmountDzd);

            decimal totalAllocatedFeesForCostDzd = CurrencyCalculator.RoundDzd(feesInCustomsValueDzd + localFeesInCostOfGoodsDzd);

            // Coût d'acquisition hors TVA = Prix d'achat DZD + Frais d'approche retenus + DD + Taxes non récupérables
            decimal acquisitionCostExVatDzd = CurrencyCalculator.RoundDzd(
                purchaseDzd + totalAllocatedFeesForCostDzd + customsDutyDzd + totalAdditionalTaxesDzd);

            // Section 24 & 26 : La TVA d'importation est traitée comme non récupérable dans ce modèle métier (intégrée au coût de revient)
            decimal nonRecoverableVatDzd = company.IsImportVatNonRecoverable ? importVatDzd : 0m;
            decimal realCostOfGoodsTotalDzd = CurrencyCalculator.RoundDzd(acquisitionCostExVatDzd + nonRecoverableVatDzd);
            decimal unitCostOfGoodsDzd = CurrencyCalculator.RoundDzd(realCostOfGoodsTotalDzd / line.Quantity);
            decimal landedMultiplier = purchaseDzd > 0m
                ? Math.Round(realCostOfGoodsTotalDzd / purchaseDzd, 6, MidpointRounding.AwayFromZero)
                : 0m;

            var economicOutcome = new LineEconomicCostResult(
                PurchaseValueCurrency: purchaseCurrency,
                PurchaseValueDzd: purchaseDzd,
                AllocatedCustomsIncludedFeesDzd: feesInCustomsValueDzd,
                AllocatedLocalAndPostCustomsFeesDzd: localFeesInCostOfGoodsDzd,
                TotalAllocatedFeesDzd: totalAllocatedFeesForCostDzd,
                CustomsDutyDzd: customsDutyDzd,
                NonRecoverableAdditionalTaxesDzd: totalAdditionalTaxesDzd,
                NonRecoverableImportVatDzd: nonRecoverableVatDzd,
                AcquisitionCostExVatDzd: acquisitionCostExVatDzd,
                RealCostOfGoodsTotalDzd: realCostOfGoodsTotalDzd,
                UnitCostOfGoodsDzd: unitCostOfGoodsDzd,
                LandedCostCoefficient: landedMultiplier);

            // Section 12 du plan multi-devises : conversion COMMERCIALE (jamais réglementaire) de cette
            // ligne vers la devise d'autorisation d'importation, pour affichage/export uniquement.
            LineCommercialConversion? lineAuthorizationConversion = null;
            if (needsAuthorizationConversion && !string.Equals(line.CurrencyCode, operation.AuthorizationCurrencyCode, StringComparison.OrdinalIgnoreCase))
            {
                decimal effectiveLineAuthorizationRate = authorizationRate;
                bool lineRateIsManual = authorizationRateIsManual;

                if (!string.Equals(line.CurrencyCode, operation.MainCurrencyCode, StringComparison.OrdinalIgnoreCase))
                {
                    // Cas rare : une ligne dans une devise différente de la devise principale de l'opération.
                    var (lineAuthRate, _, lineAuthAnomaly) = _commercialConversionService.ResolveCrossRate(
                        line.CurrencyCode, operation.AuthorizationCurrencyCode, operation.ReferenceDate, null);
                    effectiveLineAuthorizationRate = lineAuthRate;
                    lineRateIsManual = false;
                    if (lineAuthAnomaly != null)
                        anomalies.Add((lineAuthAnomaly with { Severity = AnomalySeverity.Avertissement, LineNumber = line.LineNumber }));
                }

                // Correction (point 2 de l'audit) : le montant d'autorisation est exprimé dans
                // operation.AuthorizationCurrencyCode (ex: USD), jamais en DZD — arrondi selon cette devise.
                decimal lineAuthorizationTotal = CurrencyRounding.Round(purchaseCurrency * effectiveLineAuthorizationRate, operation.AuthorizationCurrencyCode);
                decimal lineAuthorizationUnit = line.Quantity == 0m
                    ? 0m
                    : Math.Round(lineAuthorizationTotal / line.Quantity, 4, MidpointRounding.AwayFromZero);

                lineAuthorizationConversion = new LineCommercialConversion(
                    AuthorizationCurrencyCode: operation.AuthorizationCurrencyCode,
                    AuthorizationUnitPrice: lineAuthorizationUnit,
                    AuthorizationTotalAmount: lineAuthorizationTotal,
                    EffectiveRate: effectiveLineAuthorizationRate,
                    IsManualRate: lineRateIsManual);
            }

            lineResults.Add(new LineFullCalculationResult(
                LineNumber: line.LineNumber,
                ProductReference: line.ProductReference,
                Designation: line.Designation,
                Quantity: line.Quantity,
                CurrencyCode: line.CurrencyCode,
                AppliedExchangeRateToDzd: lineRate,
                CustomsOutcome: customsOutcome,
                EconomicOutcome: economicOutcome,
                FeeAllocations: lineAllocations,
                AuthorizationConversion: lineAuthorizationConversion));
        }

        // Section 5, 6, 9, 11 & 13 du plan multi-devises : conversion commerciale globale de la facture
        // (somme des lignes dans la devise principale) vers la devise de l'autorisation d'importation.
        CommercialAuthorizationConversion? commercialAuthorizationConversion = null;
        if (needsAuthorizationConversion)
        {
            decimal totalMainCurrencyAmount = lineResults
                .Where(l => string.Equals(l.CurrencyCode, operation.MainCurrencyCode, StringComparison.OrdinalIgnoreCase))
                .Sum(l => l.EconomicOutcome.PurchaseValueCurrency);

            // Correction (point 2 de l'audit) : montant agrégé exprimé en operation.AuthorizationCurrencyCode
            // (ex: USD), jamais en DZD.
            decimal totalAuthorizationAmount = CurrencyRounding.Round(totalMainCurrencyAmount * authorizationRate, operation.AuthorizationCurrencyCode);

            commercialAuthorizationConversion = new CommercialAuthorizationConversion(
                OriginalCurrencyCode: operation.MainCurrencyCode,
                OriginalTotalAmount: totalMainCurrencyAmount,
                AuthorizationCurrencyCode: operation.AuthorizationCurrencyCode,
                AuthorizationTotalAmount: totalAuthorizationAmount,
                EffectiveRate: authorizationRate,
                IsManualRate: authorizationRateIsManual,
                OfficialRateSourceName: authorizationOfficialRate?.SourceName,
                OfficialRateValidFrom: authorizationOfficialRate?.ValidFrom,
                RateTypeLabelFr: authorizationRateIsManual ? "⚠️ TAUX MANUEL" : "Taux officiel enregistré");
        }

        return new ImportCalculationSummary(
            ImportOperationId: operation.Id,
            ImportNumber: operation.ImportNumber,
            ReferenceDate: operation.ReferenceDate,
            IsSimulation: operation.IsSimulation,
            TotalPurchaseValueMainCurrency: lineResults.Sum(l => l.EconomicOutcome.PurchaseValueCurrency),
            TotalPurchaseValueDzd: lineResults.Sum(l => l.EconomicOutcome.PurchaseValueDzd),
            TotalCustomsValueDzd: lineResults.Sum(l => l.CustomsOutcome.CustomsValueDzd),
            TotalCustomsDutyDzd: lineResults.Sum(l => l.CustomsOutcome.CustomsDutyAmountDzd),
            TotalAdditionalTaxesDzd: lineResults.Sum(l => l.CustomsOutcome.TotalAdditionalTaxesDzd),
            TotalImportVatDzd: lineResults.Sum(l => l.CustomsOutcome.ImportVatAmountDzd),
            TotalDutiesAndTaxesDzd: lineResults.Sum(l => l.CustomsOutcome.TotalDutiesAndTaxesDzd),
            TotalImportFeesDzd: lineResults.Sum(l => l.EconomicOutcome.TotalAllocatedFeesDzd),
            TotalAcquisitionCostExVatDzd: lineResults.Sum(l => l.EconomicOutcome.AcquisitionCostExVatDzd),
            TotalRealCostOfGoodsDzd: lineResults.Sum(l => l.EconomicOutcome.RealCostOfGoodsTotalDzd),
            LineResults: lineResults,
            Anomalies: anomalies,
            HasBlockingAnomalies: anomalies.Any(a => a.Severity == AnomalySeverity.Blocage),
            MandatoryLegalDisclaimerFr: OfficialLegalDisclaimer,
            CommercialAuthorizationConversion: commercialAuthorizationConversion);
    }
}
