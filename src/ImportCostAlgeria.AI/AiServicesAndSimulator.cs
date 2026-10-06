using System;
using System.Collections.Generic;
using System.Linq;
using ImportCostAlgeria.CalculationEngine;
using ImportCostAlgeria.Core.Domain;

namespace ImportCostAlgeria.AI;

/// <summary>
/// Proposition de classification SH générée par l'IA (Section 16).
/// Ne modifie JAMAIS automatiquement le code SH définitif : exige [CONFIRMER], [MODIFIER] ou [REFUSER].
/// </summary>
public sealed record HsClassificationAiProposal(
    Guid ProposalId,
    string ProductReference,
    string ProductDesignation,
    string? OriginCountryIso2,
    string ProposedHsCode10,
    string ProposedTariffDescriptionFr,
    decimal ConfidencePercent,
    string JustificationFr,
    string GeneralInterpretiveRuleUsed,
    AiProposalDecision DecisionStatus,
    DataOriginTag DataTag = DataOriginTag.PropositionIa);

/// <summary>
/// Segment de réponse de l'Assistant IA distinguant obligatoirement l'origine de chaque donnée (Section 29).
/// </summary>
public sealed record AssistantTaggedStatement(
    DataOriginTag Tag,
    string LabelFr,
    string ContentFr);

public sealed record RegulatoryAssistantResponse(
    string UserQuestion,
    IReadOnlyList<AssistantTaggedStatement> Statements);

/// <summary>
/// Paramètres de simulation non destructifs (Section 30).
/// </summary>
public sealed record SimulationScenarioOverrides(
    decimal? UnitPriceFactorPercent = null,     // Ex: -10% sur le prix FOB
    decimal? FreightFactorPercent = null,       // Ex: +20% sur le fret
    decimal? InsuranceOverrideAmount = null,
    decimal? ExchangeRateOverride = null,
    IncotermCode? IncotermOverride = null,
    decimal? QuantityOverrideForFirstLine = null,
    decimal? TargetSellingPriceUnitDzd = null);

public sealed record SimulationExecutionResult(
    ImportCalculationSummary SimulatedCalculation,
    decimal? EstimatedUnitMarginDzd,
    decimal? EstimatedMarginPercent,
    bool HasModifiedOriginalImport);

public sealed class ImportSimulatorService
{
    private readonly ImportCalculationOrchestrator _orchestrator;

    public ImportSimulatorService(ImportCalculationOrchestrator orchestrator)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    /// <summary>
    /// Exécute une simulation sur un clone isolé en mémoire sans jamais modifier l'importation réelle (Section 30).
    /// </summary>
    public SimulationExecutionResult RunSimulation(
        Company company,
        ImportOperation originalOperation,
        SimulationScenarioOverrides overrides)
    {
        decimal priceMultiplier = overrides.UnitPriceFactorPercent.HasValue
            ? 1m + (overrides.UnitPriceFactorPercent.Value / 100m)
            : 1m;

        decimal freightMultiplier = overrides.FreightFactorPercent.HasValue
            ? 1m + (overrides.FreightFactorPercent.Value / 100m)
            : 1m;

        var clonedOperation = new ImportOperation
        {
            Id = Guid.NewGuid(),
            CompanyId = originalOperation.CompanyId,
            ImportNumber = $"{originalOperation.ImportNumber}-SIM",
            ReferenceDate = originalOperation.ReferenceDate,
            SupplierName = originalOperation.SupplierName,
            PurchaseCountryIso2 = originalOperation.PurchaseCountryIso2,
            DefaultOriginCountryIso2 = originalOperation.DefaultOriginCountryIso2,
            ExportShippingCountryIso2 = originalOperation.ExportShippingCountryIso2,
            MainCurrencyCode = originalOperation.MainCurrencyCode,
            ManualExchangeRateOverride = overrides.ExchangeRateOverride ?? originalOperation.ManualExchangeRateOverride,
            // Correction 2026-10-02 (PRIORITÉ 3 — "Simulation du taux de change") : la devise d'autorisation
            // et son éventuel taux manuel propre à l'importation doivent être reconduits dans le clone de
            // simulation — sinon la conversion commerciale (CommercialAuthorizationConversion) retomberait
            // silencieusement sur la devise par défaut ("USD" sans taux manuel), produisant des résultats de
            // simulation incohérents avec l'opération réelle pour cette partie purement informative.
            AuthorizationCurrencyCode = originalOperation.AuthorizationCurrencyCode,
            ManualAuthorizationCurrencyRateToDzd = originalOperation.ManualAuthorizationCurrencyRateToDzd,
            Incoterm = overrides.IncotermOverride ?? originalOperation.Incoterm,
            CustomsRegimeCode = originalOperation.CustomsRegimeCode,
            // Revue du 2026-10-02 (Section 15) : les confirmations manuelles PRCT/TCS de l'opération réelle
            // doivent être reconduites dans la simulation "what-if" — sinon le simulateur afficherait des
            // anomalies "donnée manquante" absentes de l'opération réelle, pour une simulation censée
            // n'étudier que l'impact du fret/change/quantité, pas celui du régime réglementaire.
            ManualPrctRatePercent = originalOperation.ManualPrctRatePercent,
            UserConfirmedManualPrct = originalOperation.UserConfirmedManualPrct,
            ManualTcsRatePercent = originalOperation.ManualTcsRatePercent,
            UserConfirmedManualTcs = originalOperation.UserConfirmedManualTcs,
            // Tâche #21, point 5 : même raisonnement pour la nouvelle confirmation manuelle CS (écran V1) —
            // doit être reconduite dans le clone de simulation, sinon le simulateur afficherait une CS "par
            // défaut"/"non déterminée" incohérente avec l'opération réelle qui utilise un taux CS confirmé.
            ManualCsRatePercent = originalOperation.ManualCsRatePercent,
            UserConfirmedManualCs = originalOperation.UserConfirmedManualCs,
            // Revue du 2026-10-02 (correction urgente) : les taux PAR DÉFAUT de l'importation réelle sont
            // également reconduits dans la simulation "what-if" — sinon le simulateur afficherait des
            // résultats/anomalies incohérents avec l'opération réelle (ex: TVA/CS/PRCT retombant à 0 ou
            // "non déterminé" dans la simulation alors que l'opération réelle utilise des valeurs par
            // défaut explicitement configurées).
            UseDefaultRatesWhenRuleMissing = originalOperation.UseDefaultRatesWhenRuleMissing,
            DefaultDdRatePercent = originalOperation.DefaultDdRatePercent,
            DefaultCsRatePercent = originalOperation.DefaultCsRatePercent,
            DefaultPrctRatePercent = originalOperation.DefaultPrctRatePercent,
            DefaultTvaRatePercent = originalOperation.DefaultTvaRatePercent,
            DefaultTcsRatePercent = originalOperation.DefaultTcsRatePercent,
            DefaultRpsAmountDzd = originalOperation.DefaultRpsAmountDzd,
            ValuationMethod = originalOperation.ValuationMethod,
            ArrivalPortOrBorder = originalOperation.ArrivalPortOrBorder,
            TransportMode = originalOperation.TransportMode,
            IsSimulation = true,
            Lines = originalOperation.Lines.Select((l, idx) => new ImportLine
            {
                Id = l.Id,
                LineNumber = l.LineNumber,
                ProductId = l.ProductId,
                ProductReference = l.ProductReference,
                Designation = l.Designation,
                Quantity = (idx == 0 && overrides.QuantityOverrideForFirstLine.HasValue)
                    ? overrides.QuantityOverrideForFirstLine.Value
                    : l.Quantity,
                MeasurementUnit = l.MeasurementUnit,
                UnitPurchasePrice = Math.Round(l.UnitPurchasePrice * priceMultiplier, 4, MidpointRounding.AwayFromZero),
                CurrencyCode = l.CurrencyCode,
                HsCodeConfirmed10 = l.HsCodeConfirmed10,
                AiProposedHsCode10 = l.AiProposedHsCode10,
                AiHsDecision = l.AiHsDecision,
                OriginCountryIso2 = l.OriginCountryIso2,
                ExcelDutyRatePercent = l.ExcelDutyRatePercent,
                UserConfirmedExcelDutyFallback = l.UserConfirmedExcelDutyFallback,
                ForceAiDutyRate = l.ForceAiDutyRate,
                ManualVatRatePercent = l.ManualVatRatePercent,
                UserConfirmedManualVatRate = l.UserConfirmedManualVatRate,
                VatExemptionReasonFr = l.VatExemptionReasonFr,
                LineGrossWeightKg = l.LineGrossWeightKg,
                LineVolumeM3 = l.LineVolumeM3
            }).ToList(),
            Fees = originalOperation.Fees.Select(f =>
            {
                decimal newAmount = f.Amount;
                if (string.Equals(f.FeeCategoryCode, "FRET_INTERNATIONAL", StringComparison.OrdinalIgnoreCase))
                {
                    newAmount = Math.Round(f.Amount * freightMultiplier, 2, MidpointRounding.AwayFromZero);
                }
                else if (string.Equals(f.FeeCategoryCode, "ASSURANCE", StringComparison.OrdinalIgnoreCase) && overrides.InsuranceOverrideAmount.HasValue)
                {
                    newAmount = overrides.InsuranceOverrideAmount.Value;
                }

                return new ImportFee
                {
                    Id = f.Id,
                    FeeCategoryCode = f.FeeCategoryCode,
                    FeeName = f.FeeName,
                    Amount = newAmount,
                    CurrencyCode = f.CurrencyCode,
                    AllocationMethod = f.AllocationMethod,
                    IncludeInCustomsValue = f.IncludeInCustomsValue,
                    CustomsTreatment = f.CustomsTreatment,
                    IncludeInCostOfGoods = f.IncludeInCostOfGoods,
                    LegalBasisReference = f.LegalBasisReference
                };
            }).ToList()
        };

        var calc = _orchestrator.ExecuteCalculation(company, clonedOperation);

        decimal? unitMargin = null;
        decimal? marginPct = null;
        if (overrides.TargetSellingPriceUnitDzd.HasValue && calc.LineResults.Count > 0)
        {
            decimal firstUnitCost = calc.LineResults[0].EconomicOutcome.UnitCostOfGoodsDzd;
            unitMargin = overrides.TargetSellingPriceUnitDzd.Value - firstUnitCost;
            marginPct = overrides.TargetSellingPriceUnitDzd.Value > 0m
                ? Math.Round((unitMargin.Value / overrides.TargetSellingPriceUnitDzd.Value) * 100m, 2, MidpointRounding.AwayFromZero)
                : 0m;
        }

        return new SimulationExecutionResult(
            SimulatedCalculation: calc,
            EstimatedUnitMarginDzd: unitMargin,
            EstimatedMarginPercent: marginPct,
            HasModifiedOriginalImport: false);
    }
}
