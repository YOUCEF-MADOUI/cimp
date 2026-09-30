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
    decimal CustomsValuePlusDutiesAndTaxesDzd);

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
    IReadOnlyList<FeeAllocationTrace> FeeAllocations);

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
    string MandatoryLegalDisclaimerFr);

// ============================================================================
// 2. SOUS-MOTEURS SPÉCIALISÉS (SÉPARÉS DE TOUTE INTERFACE GRAPHIQUE)
// ============================================================================

public interface IExchangeRateProvider
{
    ExchangeRateRecord? GetRegulatoryRate(string currencyCode, DateOnly referenceDate);
}

/// <summary>
/// Convertisseur de devises versionné avec contrôle ALCES et alerte Taux Manuel (Sections 13 & 14).
/// Base juridique : Art. 16 decies du Code des Douanes (Loi n° 17-04 du 16 février 2017, JORA n° 11).
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

        if (manualOverrideRate.HasValue && manualOverrideRate.Value > 0m)
        {
            if (official != null && official.RateToDzd != manualOverrideRate.Value)
            {
                var warning = new CalculationAnomaly(
                    AnomalySeverity.Avertissement,
                    "MANUAL_EXCHANGE_RATE_DIFF",
                    $"⚠️ TAUX MANUEL : Le taux utilisé ({manualOverrideRate.Value:F4}) diffère du taux réglementaire enregistré ({official.RateToDzd:F4} - {official.SourceName}).",
                    ExpectedValue: official.RateToDzd.ToString("F4"),
                    ActualValue: manualOverrideRate.Value.ToString("F4"));
                return (manualOverrideRate.Value, official, warning);
            }

            return (manualOverrideRate.Value, official, null);
        }

        if (official == null)
        {
            var blocking = new CalculationAnomaly(
                AnomalySeverity.Blocage,
                "MISSING_EXCHANGE_RATE",
                $"⚠️ Taux de change absent pour la devise '{currencyCode}' à la date de référence {referenceDate:dd/MM/yyyy}.");
            return (0m, null, blocking);
        }

        return (official.RateToDzd / official.QuotityUnit, official, null);
    }

    public static decimal RoundDzd(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}

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

    public ImportCalculationOrchestrator(
        CurrencyCalculator currencyCalculator,
        CostAllocationEngine allocationEngine,
        CustomsValueCalculator customsValueCalculator,
        RegulatoryRuleEngine regulatoryEngine)
    {
        _currencyCalculator = currencyCalculator;
        _allocationEngine = allocationEngine;
        _customsValueCalculator = customsValueCalculator;
        _regulatoryEngine = regulatoryEngine;
    }

    public ImportCalculationSummary ExecuteCalculation(Company company, ImportOperation operation)
    {
        var anomalies = new List<CalculationAnomaly>();

        // 1. Contrôle Incoterm & champs dynamiques (Sections 7, 8, 28)
        _customsValueCalculator.ValidateIncotermRequiredFees(operation, anomalies);

        // 2. Résolution du taux de change principal (Sections 13 & 14)
        var (mainRateToDzd, _, mainRateAnomaly) = _currencyCalculator.ResolveRate(
            operation.MainCurrencyCode,
            operation.ReferenceDate,
            operation.ManualExchangeRateOverride);

        if (mainRateAnomaly != null)
            anomalies.Add(mainRateAnomaly);

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

            decimal purchaseCurrency = CurrencyCalculator.RoundDzd(line.Quantity * line.UnitPurchasePrice);
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

            // 5. Autres taxes et prélèvements applicables (DAPS, TIC, RDAE...)
            var additionalTaxBreakdowns = new List<AppliedTaxBreakdown>();
            foreach (var taxRule in regOutcome.AdditionalTaxRules)
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

            decimal totalAdditionalTaxesDzd = additionalTaxBreakdowns.Sum(t => t.TaxAmountDzd);

            // 6. TVA à l'importation (Art. 19 CTCA : Assiette = Valeur en douane + Droits de douane + Taxes hors TVA)
            decimal vatTaxableBaseDzd = CurrencyCalculator.RoundDzd(customsValueDzd + customsDutyDzd + totalAdditionalTaxesDzd);
            decimal appliedVatRate = regOutcome.VatRule?.RatePercent ?? 0m;
            decimal importVatDzd = CurrencyCalculator.RoundDzd(vatTaxableBaseDzd * (appliedVatRate / 100m));

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
                CustomsValuePlusDutiesAndTaxesDzd: lineCustomsClearedTotalDzd);

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

            lineResults.Add(new LineFullCalculationResult(
                LineNumber: line.LineNumber,
                ProductReference: line.ProductReference,
                Designation: line.Designation,
                Quantity: line.Quantity,
                CurrencyCode: line.CurrencyCode,
                AppliedExchangeRateToDzd: lineRate,
                CustomsOutcome: customsOutcome,
                EconomicOutcome: economicOutcome,
                FeeAllocations: lineAllocations));
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
            MandatoryLegalDisclaimerFr: OfficialLegalDisclaimer);
    }
}
