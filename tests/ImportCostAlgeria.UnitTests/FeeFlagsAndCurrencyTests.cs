using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.RegulatoryEngine;
using ImportCostAlgeria.CalculationEngine;

namespace ImportCostAlgeria.UnitTests;

/// <summary>
/// Revue du 2026-10-02 (demande utilisateur — "Corrections fonctionnelles importation, frais, devises, CFR,
/// notifications et calculs"). Couvre explicitement les 14 points de tests exigés (Section 20) :
///   - Devise d'un frais (DA / EUR / USD) convertie correctement avant intégration (Section 2/5).
///   - "Inclure dans la valeur en douane" / "Inclure dans le coût de revient" : deux bascules INDÉPENDANTES
///     qui doivent chacune produire un effet réel, sans jamais compter un montant deux fois (Section 3).
///   - Le fret international ajouté sous Incoterm CFR, si l'utilisateur coche explicitement "Inclure dans
///     la valeur en douane", doit RÉELLEMENT être ajouté (régression du bug exact signalé, Section 5).
///   - CFR sans assurance = aucune anomalie MISSING_INSURANCE (Section 6, test complémentaire à
///     V1CompleteTestSuite.Incoterms_EXW_FOB_CFR_ShouldEnforceDynamicRequiredFields).
///   - Chacune des 5 méthodes de répartition V1 produit un montant RÉELLEMENT différent et correct
///     (Section 4), avec les exemples chiffrés exacts fournis par l'utilisateur.
///   - Le prix unitaire converti en devise d'autorisation (PU $) reste strictement la conversion du prix
///     d'achat ORIGINAL, jamais dérivé du coût de revient (Section 10/11), avec l'exemple exact fourni.
/// </summary>
public sealed class FeeFlagsAndCurrencyTests
{
    private static Company BuildCompany() => new()
    {
        Code = "TEST-DZ",
        LegalName = "SARL TEST FRAIS",
        IsImportVatNonRecoverable = true
    };

    private static ImportCalculationOrchestrator BuildOrchestrator(IEnumerable<ExchangeRateRecord> rates)
    {
        var rateProvider = new InMemoryExchangeRateProvider(rates);
        return new ImportCalculationOrchestrator(
            new CurrencyCalculator(rateProvider),
            new CostAllocationEngine(),
            new CustomsValueCalculator(),
            new RegulatoryRuleEngine(new InMemoryRegulatoryRuleRepository(Array.Empty<RegulatoryRule>())),
            new CurrencyConversionService(rateProvider));
    }

    private static ImportLine SingleLine(decimal quantity, decimal unitPrice, string currency = "DZD", int lineNumber = 1, string productReference = "ART-01") => new()
    {
        LineNumber = lineNumber,
        ProductReference = productReference,
        Designation = "Article de test",
        Quantity = quantity,
        UnitPurchasePrice = unitPrice,
        CurrencyCode = currency,
        OriginCountryIso2 = "FR"
    };

    private static ImportOperation BuildOperation(
        string mainCurrency,
        IncotermCode incoterm,
        IReadOnlyList<ImportLine> lines,
        IReadOnlyList<ImportFee> fees) => new()
    {
        CompanyId = Guid.NewGuid(),
        ImportNumber = "IMP-FEE-TEST",
        ReferenceDate = new DateOnly(2026, 1, 15),
        SupplierName = "Fournisseur Test",
        ExportShippingCountryIso2 = "FR",
        DefaultOriginCountryIso2 = "FR",
        MainCurrencyCode = mainCurrency,
        Incoterm = incoterm,
        ArrivalPortOrBorder = "Port d'Alger",
        TransportMode = "Maritime",
        Lines = lines.ToList(),
        Fees = fees.ToList()
    };

    private static readonly ExchangeRateRecord EurToDzd = new()
    {
        CurrencyCode = "EUR", QuoteCurrencyCode = "DZD", RateToDzd = 150m, QuotityUnit = 1,
        ValidFrom = new DateOnly(2026, 1, 1), RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "Test"
    };

    private static readonly ExchangeRateRecord UsdToDzd = new()
    {
        CurrencyCode = "USD", QuoteCurrencyCode = "DZD", RateToDzd = 135m, QuotityUnit = 1,
        ValidFrom = new DateOnly(2026, 1, 1), RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "Test"
    };

    private static ImportFee BuildFee(
        decimal amount,
        string currency,
        bool includeInCustomsValue,
        bool includeInCostOfGoods,
        FeeAllocationMethod method = FeeAllocationMethod.FixedAmount,
        CustomsAdjustmentTreatment treatment = CustomsAdjustmentTreatment.Addition_Art16Octies,
        string categoryCode = "AUTRES_FRAIS") => new()
    {
        FeeCategoryCode = categoryCode,
        FeeName = "Frais test",
        Amount = amount,
        CurrencyCode = currency,
        AllocationMethod = method,
        IncludeInCustomsValue = includeInCustomsValue,
        CustomsTreatment = treatment,
        IncludeInCostOfGoods = includeInCostOfGoods
    };

    // --------------------------------------------------------------------------------------
    // Section 3.5 / 20 : devise du frais (DA, EUR, USD) + inclusion valeur en douane / coût de revient.
    // --------------------------------------------------------------------------------------

    [Fact]
    public void Fee_InDzd_IncludedInCustomsValue_IncreasesCustomsValueByExactAmount_NoConversion()
    {
        var operation = BuildOperation("DZD", IncotermCode.FOB,
            new[] { SingleLine(1m, 10_000m) },
            new[] { BuildFee(2_000m, "DZD", includeInCustomsValue: true, includeInCostOfGoods: false) });

        var summary = BuildOrchestrator(Array.Empty<ExchangeRateRecord>()).ExecuteCalculation(BuildCompany(), operation);
        var line = summary.LineResults.Single();

        Assert.Equal(12_000m, line.CustomsOutcome.CustomsValueDzd); // 10 000 + 2 000 (déjà en DZD, aucune conversion).
    }

    [Fact]
    public void Fee_InEur_IncludedInCustomsValue_ConvertsBeforeAddingToCustomsValue()
    {
        var operation = BuildOperation("DZD", IncotermCode.FOB,
            new[] { SingleLine(1m, 10_000m) },
            new[] { BuildFee(100m, "EUR", includeInCustomsValue: true, includeInCostOfGoods: false) });

        var summary = BuildOrchestrator(new[] { EurToDzd }).ExecuteCalculation(BuildCompany(), operation);
        var line = summary.LineResults.Single();

        // 100 EUR x 150 DA = 15 000 DA ajoutés à la valeur en douane (10 000 + 15 000 = 25 000).
        Assert.Equal(25_000m, line.CustomsOutcome.CustomsValueDzd);
    }

    [Fact]
    public void Fee_InUsd_IncludedInCustomsValue_ConvertsBeforeAddingToCustomsValue()
    {
        var operation = BuildOperation("DZD", IncotermCode.FOB,
            new[] { SingleLine(1m, 10_000m) },
            new[] { BuildFee(100m, "USD", includeInCustomsValue: true, includeInCostOfGoods: false) });

        var summary = BuildOrchestrator(new[] { UsdToDzd }).ExecuteCalculation(BuildCompany(), operation);
        var line = summary.LineResults.Single();

        // 100 USD x 135 DA = 13 500 DA ajoutés à la valeur en douane (10 000 + 13 500 = 23 500).
        Assert.Equal(23_500m, line.CustomsOutcome.CustomsValueDzd);
    }

    [Fact]
    public void Fee_ExcludedFromCustomsValue_IncludedInCostOfGoodsOnly_NeverRaisesCustomsValue_ButRaisesCostOfGoods()
    {
        // Exemple explicite de la demande (Section 3) : un frais de transit coché UNIQUEMENT "coût de
        // revient" ne doit JAMAIS augmenter la valeur en douane, mais DOIT augmenter le coût de revient.
        var operation = BuildOperation("DZD", IncotermCode.FOB,
            new[] { SingleLine(1m, 10_000m) },
            new[] { BuildFee(1_500m, "DZD", includeInCustomsValue: false, includeInCostOfGoods: true, categoryCode: "TRANSIT") });

        var summary = BuildOrchestrator(Array.Empty<ExchangeRateRecord>()).ExecuteCalculation(BuildCompany(), operation);
        var line = summary.LineResults.Single();

        Assert.Equal(10_000m, line.CustomsOutcome.CustomsValueDzd); // inchangée : le frais N'EST PAS dans la valeur en douane.
        Assert.Equal(1_500m, line.EconomicOutcome.TotalAllocatedFeesDzd); // mais IL EST dans le coût de revient.
        Assert.True(line.EconomicOutcome.RealCostOfGoodsTotalDzd > 10_000m);
    }

    [Fact]
    public void Fee_IncludedInBothCustomsValueAndCostOfGoods_AffectsBoth_WithoutDoubleCountingTheRawAmount()
    {
        // Exemple explicite de la demande (Section 3) : le FRET coché dans les deux cases doit affecter les
        // deux (valeur en douane ET coût de revient), sans compter deux fois le MONTANT BRUT du frais.
        var operation = BuildOperation("DZD", IncotermCode.FOB,
            new[] { SingleLine(1m, 10_000m) },
            new[] { BuildFee(2_000m, "DZD", includeInCustomsValue: true, includeInCostOfGoods: true, categoryCode: "FRET_INTERNATIONAL") });

        var summary = BuildOrchestrator(Array.Empty<ExchangeRateRecord>()).ExecuteCalculation(BuildCompany(), operation);
        var line = summary.LineResults.Single();

        Assert.Equal(12_000m, line.CustomsOutcome.CustomsValueDzd); // Valeur en douane AUGMENTÉE.
        // Le frais lui-même n'apparaît qu'UNE fois dans les frais alloués au coût de revient (jamais 4 000).
        Assert.Equal(2_000m, line.EconomicOutcome.TotalAllocatedFeesDzd);
    }

    [Fact]
    public void Fee_ExcludedFromBoth_HasNoEffectAtAllOnCustomsValueOrCostOfGoods()
    {
        var operation = BuildOperation("DZD", IncotermCode.FOB,
            new[] { SingleLine(1m, 10_000m) },
            new[] { BuildFee(5_000m, "DZD", includeInCustomsValue: false, includeInCostOfGoods: false) });

        var summary = BuildOrchestrator(Array.Empty<ExchangeRateRecord>()).ExecuteCalculation(BuildCompany(), operation);
        var line = summary.LineResults.Single();

        Assert.Equal(10_000m, line.CustomsOutcome.CustomsValueDzd);
        Assert.Equal(0m, line.EconomicOutcome.TotalAllocatedFeesDzd);
    }

    [Fact]
    public void FretInternational_UnderCfr_ExplicitlyIncludedByUser_IsActuallyAddedToCustomsValue()
    {
        // RÉGRESSION EXACTE du bug signalé (Section 5) : "Le FRET_INTERNATIONAL ne rentre actuellement pas
        // dans la valeur en douane même lorsque 'Inclus valeur en douane' = coché.". Reproduit le scénario
        // réel : sous Incoterm CFR, ImportDetailViewModel.AddFeeFromTemplate crée par défaut le fret avec
        // CustomsTreatment=IncludedInInvoicePrice (déjà dans le prix) ; si l'utilisateur coche malgré tout
        // "Inclure dans la valeur en douane" (FeeRowViewModel.IncludeInCustomsValue bascule aussi le
        // traitement vers Addition_Art16Octies, voir ce fichier), le montant DOIT être ajouté.
        var fretDejaInclus = BuildFee(
            1_800m, "EUR",
            includeInCustomsValue: false,
            includeInCostOfGoods: true,
            method: FeeAllocationMethod.ByValue,
            treatment: CustomsAdjustmentTreatment.IncludedInInvoicePrice,
            categoryCode: "FRET_INTERNATIONAL");

        // Simule exactement ce que fait FeeRowViewModel.IncludeInCustomsValue quand l'utilisateur coche la case.
        fretDejaInclus.CustomsTreatment = CustomsAdjustmentTreatment.Addition_Art16Octies;
        fretDejaInclus.IncludeInCustomsValue = true;

        var operation = BuildOperation("EUR", IncotermCode.CFR,
            new[] { SingleLine(1m, 10_000m, "EUR") },
            new[] { fretDejaInclus });

        var summary = BuildOrchestrator(new[] { EurToDzd }).ExecuteCalculation(BuildCompany(), operation);
        var line = summary.LineResults.Single();

        // Valeur d'achat : 10 000 EUR x 150 = 1 500 000 DA. Fret : 1 800 EUR x 150 = 270 000 DA ajoutés.
        Assert.Equal(1_770_000m, line.CustomsOutcome.CustomsValueDzd);
    }

    // --------------------------------------------------------------------------------------
    // Section 4 / 20 : chaque méthode de répartition produit un montant réellement différent et correct.
    // --------------------------------------------------------------------------------------

    [Fact]
    public void AllocationMethod_ByValue_SplitsProportionallyToPurchaseValue()
    {
        // Exemple exact de la demande : A = 1 000 €, B = 3 000 €, frais = 400 € -> A = 100, B = 300.
        var lineA = SingleLine(1m, 1_000m, "DZD", lineNumber: 1, productReference: "A");
        var lineB = SingleLine(1m, 3_000m, "DZD", lineNumber: 2, productReference: "B");
        var fee = BuildFee(400m, "DZD", includeInCustomsValue: false, includeInCostOfGoods: true, method: FeeAllocationMethod.ByValue);

        var operation = BuildOperation("DZD", IncotermCode.FOB, new[] { lineA, lineB }, new[] { fee });
        var summary = BuildOrchestrator(Array.Empty<ExchangeRateRecord>()).ExecuteCalculation(BuildCompany(), operation);

        var resultA = summary.LineResults.Single(l => l.ProductReference == "A");
        var resultB = summary.LineResults.Single(l => l.ProductReference == "B");

        Assert.Equal(100m, resultA.FeeAllocations.Single().AllocatedAmountDzd);
        Assert.Equal(300m, resultB.FeeAllocations.Single().AllocatedAmountDzd);
    }

    [Fact]
    public void AllocationMethod_ByQuantity_SplitsProportionallyToQuantity()
    {
        // Exemple exact de la demande : A = 100 u, B = 300 u, frais = 400 -> A = 100, B = 300.
        var lineA = SingleLine(100m, 1m, "DZD", lineNumber: 1, productReference: "A");
        var lineB = SingleLine(300m, 1m, "DZD", lineNumber: 2, productReference: "B");
        var fee = BuildFee(400m, "DZD", includeInCustomsValue: false, includeInCostOfGoods: true, method: FeeAllocationMethod.ByQuantity);

        var operation = BuildOperation("DZD", IncotermCode.FOB, new[] { lineA, lineB }, new[] { fee });
        var summary = BuildOrchestrator(Array.Empty<ExchangeRateRecord>()).ExecuteCalculation(BuildCompany(), operation);

        var resultA = summary.LineResults.Single(l => l.ProductReference == "A");
        var resultB = summary.LineResults.Single(l => l.ProductReference == "B");

        Assert.Equal(100m, resultA.FeeAllocations.Single().AllocatedAmountDzd);
        Assert.Equal(300m, resultB.FeeAllocations.Single().AllocatedAmountDzd);
    }

    [Fact]
    public void AllocationMethod_FixedAmount_SplitsEquallyAcrossLines()
    {
        var lineA = SingleLine(1m, 1_000m, "DZD", lineNumber: 1, productReference: "A");
        var lineB = SingleLine(1m, 9_000m, "DZD", lineNumber: 2, productReference: "B"); // valeur très différente : ne doit RIEN changer en montant fixe.
        var fee = BuildFee(500m, "DZD", includeInCustomsValue: false, includeInCostOfGoods: true, method: FeeAllocationMethod.FixedAmount);

        var operation = BuildOperation("DZD", IncotermCode.FOB, new[] { lineA, lineB }, new[] { fee });
        var summary = BuildOrchestrator(Array.Empty<ExchangeRateRecord>()).ExecuteCalculation(BuildCompany(), operation);

        var resultA = summary.LineResults.Single(l => l.ProductReference == "A");
        var resultB = summary.LineResults.Single(l => l.ProductReference == "B");

        Assert.Equal(250m, resultA.FeeAllocations.Single().AllocatedAmountDzd);
        Assert.Equal(250m, resultB.FeeAllocations.Single().AllocatedAmountDzd);
    }

    [Fact]
    public void AllocationMethod_Percentage_AppliesRateToTotalPurchaseValueBase()
    {
        // Exemple exact de la demande : base = 10 000 DA, 5 % -> 500 DA.
        var line = SingleLine(1m, 10_000m, "DZD");
        var fee = BuildFee(5m, "DZD", includeInCustomsValue: false, includeInCostOfGoods: true, method: FeeAllocationMethod.Percentage);

        var operation = BuildOperation("DZD", IncotermCode.FOB, new[] { line }, new[] { fee });
        var summary = BuildOrchestrator(Array.Empty<ExchangeRateRecord>()).ExecuteCalculation(BuildCompany(), operation);

        Assert.Equal(500m, summary.LineResults.Single().FeeAllocations.Single().AllocatedAmountDzd);
    }

    [Fact]
    public void AllocationMethod_Manual_UsesExactUserAllocation_PerLine()
    {
        var lineA = SingleLine(1m, 1_000m, "DZD", lineNumber: 1, productReference: "A");
        var lineB = SingleLine(1m, 1_000m, "DZD", lineNumber: 2, productReference: "B");
        var fee = BuildFee(1_000m, "DZD", includeInCustomsValue: false, includeInCostOfGoods: true, method: FeeAllocationMethod.Manual);

        // Répartition manuelle délibérément DÉSÉQUILIBRÉE (jamais recalculée/normalisée par le moteur).
        lineA.ManualFeeAllocationsDzd[fee.Id] = 700m;
        lineB.ManualFeeAllocationsDzd[fee.Id] = 300m;

        var operation = BuildOperation("DZD", IncotermCode.FOB, new[] { lineA, lineB }, new[] { fee });
        var summary = BuildOrchestrator(Array.Empty<ExchangeRateRecord>()).ExecuteCalculation(BuildCompany(), operation);

        Assert.Equal(700m, summary.LineResults.Single(l => l.ProductReference == "A").FeeAllocations.Single().AllocatedAmountDzd);
        Assert.Equal(300m, summary.LineResults.Single(l => l.ProductReference == "B").FeeAllocations.Single().AllocatedAmountDzd);
        Assert.DoesNotContain(summary.Anomalies, a => a.AnomalyCode == "MANUAL_FEE_ALLOCATION_INCOMPLETE");
    }

    [Fact]
    public void AllocationMethod_Manual_SumNotMatchingTotal_RaisesExactAnomalyMessage()
    {
        var line = SingleLine(1m, 1_000m, "DZD");
        var fee = BuildFee(1_000m, "DZD", includeInCustomsValue: false, includeInCostOfGoods: true, method: FeeAllocationMethod.Manual);
        line.ManualFeeAllocationsDzd[fee.Id] = 600m; // 400 manquants : répartition incomplète.

        var operation = BuildOperation("DZD", IncotermCode.FOB, new[] { line }, new[] { fee });
        var summary = BuildOrchestrator(Array.Empty<ExchangeRateRecord>()).ExecuteCalculation(BuildCompany(), operation);

        Assert.Contains(summary.Anomalies, a =>
            a.AnomalyCode == "MANUAL_FEE_ALLOCATION_INCOMPLETE" &&
            a.MessageFr.Contains("Le montant des frais n'est pas entièrement réparti."));
    }

    // --------------------------------------------------------------------------------------
    // Section 6 / 20 : CFR sans assurance = aucune anomalie.
    // --------------------------------------------------------------------------------------

    [Fact]
    public void Cfr_WithoutInsurance_NeverRaisesAnyInsuranceAnomaly()
    {
        var validator = new CustomsValueCalculator();
        var anomalies = new List<CalculationAnomaly>();
        var operation = BuildOperation("EUR", IncotermCode.CFR, new[] { SingleLine(1m, 1_000m, "EUR") }, Array.Empty<ImportFee>());

        validator.ValidateIncotermRequiredFees(operation, anomalies);

        Assert.DoesNotContain(anomalies, a => a.AnomalyCode.Contains("INSURANCE", StringComparison.OrdinalIgnoreCase));
    }

    // --------------------------------------------------------------------------------------
    // Section 10/11/20 : PU $ = conversion COMMERCIALE du prix d'achat ORIGINAL, jamais du coût de revient.
    // --------------------------------------------------------------------------------------

    [Fact]
    public void AuthorizationConversion_ExactUserExample_Qty200_Pu16_69Eur_Rate1_17_Produces19_53UsdUnit()
    {
        var rates = new[]
        {
            EurToDzd,
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR", QuoteCurrencyCode = "USD", RateToDzd = 1.17m, QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1), RateType = "COMMERCIAL_AUTORISATION", SourceName = "Test"
            }
        };

        var operation = BuildOperation("EUR", IncotermCode.FOB,
            new[] { SingleLine(200m, 16.69m, "EUR") }, Array.Empty<ImportFee>());
        operation.AuthorizationCurrencyCode = "USD";

        var summary = BuildOrchestrator(rates).ExecuteCalculation(BuildCompany(), operation);
        var line = summary.LineResults.Single();

        Assert.NotNull(line.AuthorizationConversion);
        // 16,69 x 1,17 = 19,5273 $ / unité (stocké avec 4 décimales de précision interne ; affiché "19,53 $"
        // une fois arrondi à 2 décimales pour l'écran, comme toute devise — jamais dérivé de la valeur en
        // douane, du coût de revient ou d'un quelconque frais : uniquement Prix d'achat original x taux commercial).
        Assert.Equal(19.5273m, line.AuthorizationConversion!.AuthorizationUnitPrice);
        Assert.Equal(19.53m, Math.Round(line.AuthorizationConversion.AuthorizationUnitPrice, 2, MidpointRounding.AwayFromZero));
        // Total = 200 x 16,69 x 1,17 = 3 905,46 $ = Quantité x PU USD (jamais Quantité x coût de revient).
        Assert.Equal(3_905.46m, line.AuthorizationConversion.AuthorizationTotalAmount);
        Assert.Equal(
            Math.Round(line.AuthorizationConversion.AuthorizationUnitPrice * 200m, 2, MidpointRounding.AwayFromZero),
            line.AuthorizationConversion.AuthorizationTotalAmount);
    }
}
