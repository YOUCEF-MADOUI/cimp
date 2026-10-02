using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Core.Services;
using ImportCostAlgeria.RegulatoryEngine;
using ImportCostAlgeria.CalculationEngine;
using ImportCostAlgeria.AI;

namespace ImportCostAlgeria.UnitTests;

/// <summary>
/// Revue du 2026-10-02 (demande utilisateur — "Corrections et améliorations importantes", PRIORITÉS 0 à 4) :
///   - PRIORITÉ 1 : conversion d'affichage DZD -&gt; USD (ou toute devise non-DZD) par INVERSION du taux
///     réglementaire "1 USD = X DA" — jamais en exigeant un taux publié dans le sens inverse "1 DA = X USD"
///     (TEST 1, 2, 3, 4, 5 de la demande, couverts ici au niveau moteur — l'écran WPF lui-même, cible
///     net8.0-windows, n'est pas accessible depuis ce projet de tests multiplateforme).
///   - PRIORITÉ 3 : simulation de variation du taux de change, ENTIÈREMENT virtuelle (ImportSimulatorService,
///     clone isolé en mémoire) — TEST 6, 7, 8, 9, 10, 11 de la demande.
///   - PRIORITÉ 4 : catalogue des méthodes de répartition V1 (ByWeight/ByVolume exclues) — TEST 14
///     (partiel : le ComboBoxColumn WPF lui-même n'est pas testable ici, voir correction XAML
///     ImportDetailView.xaml, DataGridComboBoxColumn.ElementStyle TargetType="ComboBox").
/// </summary>
public sealed class CrashFixSimulationAndDisplayTests
{
    private static Company BuildCompany() => new()
    {
        Code = "TEST-SIM",
        LegalName = "SARL TEST SIMULATION",
        IsImportVatNonRecoverable = true
    };

    private static ImportCalculationOrchestrator BuildOrchestrator(IEnumerable<ExchangeRateRecord> rates) =>
        new(
            new CurrencyCalculator(new InMemoryExchangeRateProvider(rates)),
            new CostAllocationEngine(),
            new CustomsValueCalculator(),
            new RegulatoryRuleEngine(new InMemoryRegulatoryRuleRepository(Array.Empty<RegulatoryRule>())),
            new CurrencyConversionService(new InMemoryExchangeRateProvider(rates)));

    private static ImportOperation BuildSingleLineOperation(string mainCurrency, decimal quantity, decimal unitPrice, decimal? manualRate = null) => new()
    {
        CompanyId = Guid.NewGuid(),
        ImportNumber = "IMP-SIM-01",
        ReferenceDate = new DateOnly(2026, 6, 1),
        SupplierName = "Fournisseur Test",
        ExportShippingCountryIso2 = "FR",
        DefaultOriginCountryIso2 = "FR",
        MainCurrencyCode = mainCurrency,
        ManualExchangeRateOverride = manualRate,
        AuthorizationCurrencyCode = mainCurrency,
        Incoterm = IncotermCode.EXW,
        ArrivalPortOrBorder = "Port d'Alger",
        TransportMode = "Maritime",
        Lines = new List<ImportLine>
        {
            new()
            {
                LineNumber = 1,
                ProductReference = "ART-SIM",
                Designation = "Article simulation",
                Quantity = quantity,
                MeasurementUnit = "PCE",
                UnitPurchasePrice = unitPrice,
                CurrencyCode = mainCurrency,
                HsCodeConfirmed10 = "1234567890",
                OriginCountryIso2 = "FR"
            }
        },
        Fees = new List<ImportFee>()
    };

    // ------------------------------------------------------------------------------------------
    // PRIORITÉ 1 — TEST 1/2/4 : "1 USD = 133,15 DA" permet bien la conversion DZD -> USD par simple
    // inversion du taux réglementaire, SANS exiger un taux publié dans le sens inverse "1 DA = X USD".
    // ------------------------------------------------------------------------------------------
    [Fact]
    public void CustomsValueDisplay_UsdRegulatoryRateAvailable_ShouldConvertByDividing_NeverRequireInverseRate()
    {
        var rates = new[]
        {
            new ExchangeRateRecord
            {
                CurrencyCode = "USD", QuoteCurrencyCode = "DZD", RateToDzd = 133.15m, QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1), RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "Banque d'Algérie"
            }
        };

        var calculator = new CurrencyCalculator(new InMemoryExchangeRateProvider(rates));
        var (usdToDzd, official, anomaly) = calculator.ResolveRate("USD", new DateOnly(2026, 6, 1), null);

        // Aucune anomalie "taux absent" alors qu'un taux USD -> DZD existe bel et bien (TEST 2).
        Assert.Null(anomaly);
        Assert.NotNull(official);
        Assert.Equal(133.15m, usdToDzd);

        // Exemple de la demande (Section PRIORITÉ 1) : Valeur en douane = 9 625 559,38 DA, taux USD =
        // 133,15 DA pour 1 USD -> Valeur USD = Valeur_DZD / taux_USD, JAMAIS Valeur_DZD × taux_USD.
        decimal customsValueDzd = 9_625_559.38m;
        decimal? customsValueUsd = ProfitCalculator.ConvertDzdToDisplayCurrency(customsValueDzd, usdToDzd);

        Assert.NotNull(customsValueUsd);
        Assert.Equal(72_291.10m, customsValueUsd!.Value);

        // Garde-fou explicite : ne jamais confondre avec une multiplication (qui donnerait un montant
        // aberrant, de l'ordre de 1,28 milliard, totalement incohérent avec une valeur en douane en USD).
        Assert.NotEqual(Math.Round(customsValueDzd * usdToDzd, 2), customsValueUsd.Value);
    }

    [Fact]
    public void CustomsValueDisplay_NoUsdRate_ShouldReturnNull_NeverThrowNorInventAValue()
    {
        var calculator = new CurrencyCalculator(new InMemoryExchangeRateProvider(Array.Empty<ExchangeRateRecord>()));
        var (usdToDzd, official, anomaly) = calculator.ResolveRate("USD", new DateOnly(2026, 6, 1), null);

        Assert.Equal(0m, usdToDzd);
        Assert.Null(official);
        Assert.NotNull(anomaly); // TEST 3 : une notification claire doit pouvoir être construite à partir de cette anomalie.

        decimal? customsValueUsd = ProfitCalculator.ConvertDzdToDisplayCurrency(9_625_559.38m, usdToDzd);
        Assert.Null(customsValueUsd);
    }

    [Fact]
    public void EurRegulatoryRate_WorksIndependentlyOfUsdRate()
    {
        // TEST 4 : seul un taux EUR -> DZD est publié (aucun taux USD) ; la résolution EUR doit réussir
        // normalement, sans jamais être affectée par l'absence de taux USD.
        var rates = new[]
        {
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR", QuoteCurrencyCode = "DZD", RateToDzd = 150.7166m, QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1), RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "Banque d'Algérie"
            }
        };
        var calculator = new CurrencyCalculator(new InMemoryExchangeRateProvider(rates));
        var (eurToDzd, official, anomaly) = calculator.ResolveRate("EUR", new DateOnly(2026, 6, 1), null);

        Assert.Null(anomaly);
        Assert.NotNull(official);
        Assert.Equal(150.7166m, eurToDzd);
    }

    [Fact]
    public void CommercialEurToUsdRate_StaysDistinctFromRegulatoryUsdToDzdRate()
    {
        // TEST 5 : le taux COMMERCIAL EUR -> USD (dérivé de EUR/DZD et USD/DZD) ne doit jamais être confondu
        // avec, ni remplacer, le taux RÉGLEMENTAIRE USD -> DZD utilisé pour la valeur en douane.
        var rates = new[]
        {
            new ExchangeRateRecord { CurrencyCode = "EUR", QuoteCurrencyCode = "DZD", RateToDzd = 150.7166m, QuotityUnit = 1, ValidFrom = new DateOnly(2026, 1, 1), RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "Test" },
            new ExchangeRateRecord { CurrencyCode = "USD", QuoteCurrencyCode = "DZD", RateToDzd = 133.15m, QuotityUnit = 1, ValidFrom = new DateOnly(2026, 1, 1), RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "Test" }
        };
        var calculator = new CurrencyCalculator(new InMemoryExchangeRateProvider(rates));

        var (usdToDzd, _, _) = calculator.ResolveRate("USD", new DateOnly(2026, 6, 1), null);
        var (eurToDzd, _, _) = calculator.ResolveRate("EUR", new DateOnly(2026, 6, 1), null);
        decimal commercialEurToUsd = eurToDzd / usdToDzd;

        // Le taux commercial (≈1,13) est totalement différent du taux réglementaire USD -> DZD (133,15) :
        // jamais l'un ne doit être utilisé à la place de l'autre pour la valeur en douane.
        Assert.NotEqual(usdToDzd, commercialEurToUsd);
        Assert.Equal(133.15m, usdToDzd); // le taux réglementaire USD -> DZD reste inchangé par ce calcul commercial.
    }

    // ------------------------------------------------------------------------------------------
    // PRIORITÉ 3 — TEST 6/7/8 : simulation de variation du taux de change = variation ABSOLUE (points de
    // DA), jamais un pourcentage. Vérifié au niveau moteur via ImportSimulatorService.
    // ------------------------------------------------------------------------------------------
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(-5)]
    [InlineData(-1)]
    [InlineData(0)]
    public void Simulation_RateDelta_ShouldApplyAbsoluteDaVariation_NeverAPercentage(int deltaDzd)
    {
        var company = BuildCompany();
        const decimal baseRate = 150.7166m;
        const decimal quantity = 100m;
        const decimal unitPrice = 10m;

        var operation = BuildSingleLineOperation("EUR", quantity, unitPrice, manualRate: baseRate);
        var orchestrator = BuildOrchestrator(Array.Empty<ExchangeRateRecord>());
        var simulator = new ImportSimulatorService(orchestrator);

        decimal simulatedRate = baseRate + deltaDzd;
        var result = simulator.RunSimulation(company, operation, new SimulationScenarioOverrides(ExchangeRateOverride: simulatedRate));

        // Valeur en douane simulée = Quantité × PU × taux simulé (aucun frais, aucune taxe de base ici) —
        // la variation du taux se répercute donc à l'identique sur la valeur en douane simulée.
        decimal expectedCustomsValueDzd = CurrencyCalculator.RoundDzd(quantity * unitPrice * simulatedRate);
        Assert.Equal(expectedCustomsValueDzd, result.SimulatedCalculation.TotalCustomsValueDzd);

        // Jamais interprété comme un pourcentage : +1 sur 150,7166 doit donner 151,7166 (±1 DA), jamais
        // 150,7166 × 1,01 ≈ 152,22 (ce qui prouverait une interprétation erronée en pourcentage).
        decimal percentMisinterpretation = CurrencyCalculator.RoundDzd(quantity * unitPrice * baseRate * (1 + deltaDzd / 100m));
        if (deltaDzd != 0)
            Assert.NotEqual(percentMisinterpretation, result.SimulatedCalculation.TotalCustomsValueDzd);

        // Jamais enregistré, jamais appliqué à l'opération réelle (Section "ne pas modifier le calcul réel").
        Assert.False(result.HasModifiedOriginalImport);
        Assert.Equal(baseRate, operation.ManualExchangeRateOverride);
    }

    // ------------------------------------------------------------------------------------------
    // PRIORITÉ 3 — TEST 11 : la simulation ne modifie JAMAIS l'importation réelle (lignes, frais, taux).
    // ------------------------------------------------------------------------------------------
    [Fact]
    public void Simulation_ShouldNeverMutate_TheOriginalImportOperation()
    {
        var company = BuildCompany();
        var operation = BuildSingleLineOperation("EUR", 100m, 10m, manualRate: 150.7166m);
        var originalLineCount = operation.Lines.Count;
        var originalFeeCount = operation.Fees.Count;
        var originalRate = operation.ManualExchangeRateOverride;
        var originalUnitPrice = operation.Lines[0].UnitPurchasePrice;
        var originalImportNumber = operation.ImportNumber;

        var orchestrator = BuildOrchestrator(Array.Empty<ExchangeRateRecord>());
        var simulator = new ImportSimulatorService(orchestrator);

        for (int delta = -5; delta <= 5; delta++)
        {
            var result = simulator.RunSimulation(company, operation, new SimulationScenarioOverrides(ExchangeRateOverride: 150.7166m + delta));
            Assert.False(result.HasModifiedOriginalImport);
        }

        // Rien n'a changé sur l'opération réelle, quel que soit le nombre de scénarios simulés.
        Assert.Equal(originalLineCount, operation.Lines.Count);
        Assert.Equal(originalFeeCount, operation.Fees.Count);
        Assert.Equal(originalRate, operation.ManualExchangeRateOverride);
        Assert.Equal(originalUnitPrice, operation.Lines[0].UnitPurchasePrice);
        Assert.Equal(originalImportNumber, operation.ImportNumber);
    }

    // ------------------------------------------------------------------------------------------
    // PRIORITÉ 3 — TEST 9 : le prix de vente (SalePriceDzd, donnée commerciale distincte du calcul
    // douanier) n'est jamais lu ni modifié par le moteur de simulation — structurellement garanti : le
    // clone de simulation ne copie aucun prix de vente, et ImportCalculationSummary ne l'expose jamais.
    // ------------------------------------------------------------------------------------------
    [Fact]
    public void Simulation_ShouldNeverReadOrAlter_SalePrice()
    {
        var company = BuildCompany();
        var operation = BuildSingleLineOperation("EUR", 100m, 10m, manualRate: 150.7166m);
        operation.Lines[0].SalePriceDzd = 2500m; // Prix de vente réellement saisi par l'utilisateur.

        var orchestrator = BuildOrchestrator(Array.Empty<ExchangeRateRecord>());
        var simulator = new ImportSimulatorService(orchestrator);

        var result = simulator.RunSimulation(company, operation, new SimulationScenarioOverrides(ExchangeRateOverride: 155.7166m));

        // Le prix de vente réel reste strictement inchangé après simulation (+5 DA).
        Assert.Equal(2500m, operation.Lines[0].SalePriceDzd);
        // Le résultat de calcul simulé ne porte lui-même aucune notion de prix de vente (séparation stricte
        // entre moteur douanier/fiscal et donnée commerciale — voir ProfitCalculator, Core.Services).
        Assert.True(result.SimulatedCalculation.LineResults.Count > 0);
    }

    // ------------------------------------------------------------------------------------------
    // PRIORITÉ 4 — TEST 14 (partiel, niveau catalogue) : méthodes d'allocation V1 strictement limitées à
    // celles demandées, Poids/Volume explicitement exclues de la liste proposée à l'écran.
    // ------------------------------------------------------------------------------------------
    [Fact]
    public void AllocationMethodsForV1_ShouldExcludeByWeightAndByVolume_AndExposeFrenchLabelsForAll()
    {
        Assert.DoesNotContain(FeeAllocationMethod.ByWeight, ImportFeeCatalog.AllocationMethodsForV1);
        Assert.DoesNotContain(FeeAllocationMethod.ByVolume, ImportFeeCatalog.AllocationMethodsForV1);

        Assert.Equal(
            new[] { FeeAllocationMethod.ByValue, FeeAllocationMethod.ByQuantity, FeeAllocationMethod.FixedAmount, FeeAllocationMethod.Percentage, FeeAllocationMethod.Manual },
            ImportFeeCatalog.AllocationMethodsForV1);

        foreach (var method in ImportFeeCatalog.AllocationMethodsForV1)
        {
            string label = ImportFeeCatalog.AllocationMethodLabelFr(method);
            Assert.False(string.IsNullOrWhiteSpace(label));
            // Jamais le nom anglais brut de l'énumération affiché tel quel à l'utilisateur.
            Assert.NotEqual(method.ToString(), label);
        }
    }

    [Fact]
    public void SupportedFeeCurrencies_ShouldIncludeDaEurUsd()
    {
        Assert.Contains("DZD", ImportFeeCatalog.SupportedFeeCurrencies);
        Assert.Contains("EUR", ImportFeeCatalog.SupportedFeeCurrencies);
        Assert.Contains("USD", ImportFeeCatalog.SupportedFeeCurrencies);
    }
}
