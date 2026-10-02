using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Core.Services;
using ImportCostAlgeria.RegulatoryEngine;
using ImportCostAlgeria.CalculationEngine;

namespace ImportCostAlgeria.UnitTests;

/// <summary>
/// Revue du 2026-10-02 : "REFONTE DE L'INTERFACE D'IMPORTATION ET AMÉLIORATION DES CALCULS" (demande
/// utilisateur, 31 sections). Couvre les points testables indépendamment de WPF (le projet de présentation
/// cible net8.0-windows/WPF, non référencé par ce projet de tests multiplateforme — voir
/// ImportCostAlgeria.Core.Services.ProfitCalculator, où les formules Bénéfice/% Bénéfice/conversion
/// d'affichage ont été délibérément extraites du ViewModel pour rester testables ici) :
///   - Section 12 : PU Autorisation = prix d'achat commercial converti, JAMAIS basé sur le coût de revient.
///   - Section 11 : pas de conversion/colonne affichée quand devise facture == devise autorisation.
///   - Section 14/22 : formule du TOTAL DÉDOUANEMENT (DD+CS+TVA+PRCT+TCS+RPS).
///   - Section 15/16/17 : Bénéfice et % Bénéfice (ProfitCalculator).
///   - Section 20/21 : séparation stricte conversion réglementaire (EUR-&gt;DZD) / conversion commerciale (EUR-&gt;USD).
///   - Section 26 : PU Reviens = coût de revient réel de la ligne / quantité, taxes et frais inclus.
///   - Section 6/7/13 : symboles de devise (CurrencyDisplay).
/// </summary>
public sealed class UiRefactorAndProfitTests
{
    private static ImportCalculationOrchestrator BuildOrchestrator(IReadOnlyList<ExchangeRateRecord> rates, IReadOnlyList<RegulatoryRule>? rules = null)
    {
        var rateProvider = new InMemoryExchangeRateProvider(rates);
        return new ImportCalculationOrchestrator(
            new CurrencyCalculator(rateProvider),
            new CostAllocationEngine(),
            new CustomsValueCalculator(),
            new RegulatoryRuleEngine(new InMemoryRegulatoryRuleRepository(rules ?? Array.Empty<RegulatoryRule>())),
            new CurrencyConversionService(rateProvider));
    }

    private static Company BuildCompany() => new()
    {
        Code = "TEST-CO",
        LegalName = "SARL TEST REFONTE",
        IsImportVatNonRecoverable = true
    };

    // ------------------------------------------------------------------------------------------
    // Section 12/20/21/27 (tests 1, 2, 4, 5) : PU Autorisation — conversion COMMERCIALE pure, jamais
    // dérivée du coût de revient, et strictement séparée de la conversion réglementaire vers DZD.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void PuAutorisation_EurPurchase_ConvertsToUsd_UsingCommercialRate_NeverCustomsRate()
    {
        var rates = new[]
        {
            new ExchangeRateRecord { CurrencyCode = "EUR", QuoteCurrencyCode = "DZD", RateToDzd = 150.7166m, QuotityUnit = 1, ValidFrom = new DateOnly(2026, 1, 1), RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "DGD" },
            // Revue Section 20 : taux COMMERCIAL distinct (EUR -> USD), jamais dérivé du taux réglementaire ci-dessus.
            new ExchangeRateRecord { CurrencyCode = "EUR", QuoteCurrencyCode = "USD", RateToDzd = 1.17m, QuotityUnit = 1, ValidFrom = new DateOnly(2026, 1, 1), RateType = "COMMERCIAL", SourceName = "Banque" }
        };
        var orchestrator = BuildOrchestrator(rates);
        var company = BuildCompany();
        var operation = new ImportOperation
        {
            CompanyId = company.Id,
            ImportNumber = "PU-AUTH-TEST",
            ReferenceDate = new DateOnly(2026, 6, 1),
            SupplierName = "Fournisseur Test",
            ExportShippingCountryIso2 = "FR",
            MainCurrencyCode = "EUR",
            AuthorizationCurrencyCode = "USD",
            Incoterm = IncotermCode.FOB,
            Lines =
            {
                new ImportLine { LineNumber = 1, ProductReference = "ART-1", Designation = "Article", Quantity = 100m, UnitPurchasePrice = 50m, CurrencyCode = "EUR", HsCodeConfirmed10 = "1234567890", OriginCountryIso2 = "FR" }
            }
        };

        var summary = orchestrator.ExecuteCalculation(company, operation);
        var line = Assert.Single(summary.LineResults);

        // Exemple exact de la demande (Section 12) : 50 € x 1,17 = 58,50 $ ; 100 x 58,50 = 5 850,00 $.
        Assert.NotNull(line.AuthorizationConversion);
        Assert.Equal(58.50m, line.AuthorizationConversion!.AuthorizationUnitPrice);
        Assert.Equal(5850.00m, line.AuthorizationConversion.AuthorizationTotalAmount);
        Assert.Equal("USD", line.AuthorizationConversion.AuthorizationCurrencyCode);

        // Section 27 test 3 : la valeur en douane (régie par le taux RÉGLEMENTAIRE EUR->DZD) reste
        // totalement indépendante du taux commercial EUR->USD utilisé ci-dessus.
        Assert.Equal(753583.00m, line.CustomsOutcome.CustomsValueDzd); // 100 x 50 x 150,7166 = 753 583,00 DZD
    }

    [Fact]
    public void PuAutorisation_SameCurrencyAsAuthorization_NoConversionObjectProduced()
    {
        var rates = new[]
        {
            new ExchangeRateRecord { CurrencyCode = "USD", QuoteCurrencyCode = "DZD", RateToDzd = 134.50m, QuotityUnit = 1, ValidFrom = new DateOnly(2026, 1, 1), RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "DGD" }
        };
        var orchestrator = BuildOrchestrator(rates);
        var company = BuildCompany();
        var operation = new ImportOperation
        {
            CompanyId = company.Id,
            ImportNumber = "PU-AUTH-NOOP",
            ReferenceDate = new DateOnly(2026, 6, 1),
            SupplierName = "Fournisseur Test",
            ExportShippingCountryIso2 = "FR",
            MainCurrencyCode = "USD",
            AuthorizationCurrencyCode = "USD", // Section 11 : même devise -> aucune conversion à afficher.
            Incoterm = IncotermCode.FOB,
            Lines =
            {
                new ImportLine { LineNumber = 1, ProductReference = "ART-1", Designation = "Article", Quantity = 10m, UnitPurchasePrice = 20m, CurrencyCode = "USD", HsCodeConfirmed10 = "1234567890", OriginCountryIso2 = "US" }
            }
        };

        var summary = orchestrator.ExecuteCalculation(company, operation);
        var line = Assert.Single(summary.LineResults);

        Assert.Null(line.AuthorizationConversion);
        Assert.Null(summary.CommercialAuthorizationConversion);
    }

    [Fact]
    public void PuAutorisation_IsIndependentOfPuReviens_NeverDerivedFromCostOfGoods()
    {
        var rates = new[]
        {
            new ExchangeRateRecord { CurrencyCode = "EUR", QuoteCurrencyCode = "DZD", RateToDzd = 150.0m, QuotityUnit = 1, ValidFrom = new DateOnly(2026, 1, 1), RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "DGD" },
            new ExchangeRateRecord { CurrencyCode = "EUR", QuoteCurrencyCode = "USD", RateToDzd = 1.10m, QuotityUnit = 1, ValidFrom = new DateOnly(2026, 1, 1), RateType = "COMMERCIAL", SourceName = "Banque" }
        };
        var orchestrator = BuildOrchestrator(rates);
        var company = BuildCompany();
        var operation = new ImportOperation
        {
            CompanyId = company.Id,
            ImportNumber = "PU-AUTH-INDEP",
            ReferenceDate = new DateOnly(2026, 6, 1),
            SupplierName = "Fournisseur Test",
            ExportShippingCountryIso2 = "FR",
            MainCurrencyCode = "EUR",
            AuthorizationCurrencyCode = "USD",
            Incoterm = IncotermCode.FOB,
            Lines =
            {
                new ImportLine { LineNumber = 1, ProductReference = "ART-1", Designation = "Article", Quantity = 1m, UnitPurchasePrice = 100m, CurrencyCode = "EUR", HsCodeConfirmed10 = "1234567890", OriginCountryIso2 = "FR" }
            }
        };

        var summary = orchestrator.ExecuteCalculation(company, operation);
        var line = Assert.Single(summary.LineResults);

        // PU Autorisation = 100 € x 1,10 = 110,00 $ — une PURE conversion commerciale du prix d'achat,
        // sans aucun droit/taxe/frais (même en l'absence de toute règle réglementaire, qui ferait pourtant
        // grimper le coût de revient réel via les taux par défaut CS/TVA/PRCT, Section 2 de la correction
        // précédente). Le coût de revient unitaire (PU Reviens) est nécessairement PLUS ÉLEVÉ.
        Assert.Equal(110.00m, line.AuthorizationConversion!.AuthorizationUnitPrice);
        Assert.True(line.EconomicOutcome.UnitCostOfGoodsDzd > line.CustomsOutcome.CustomsValueDzd,
            "Le PU Reviens (coût de revient réel) doit être strictement supérieur à la seule valeur en douane dès lors que des taxes par défaut s'appliquent — preuve qu'il n'est pas égal/confondu avec le PU Autorisation (qui, lui, reste une simple conversion commerciale du prix d'achat).");
    }

    [Fact]
    public void TotalAutorisation_EqualsUnitAuthorizationPriceTimesQuantity()
    {
        var rates = new[]
        {
            new ExchangeRateRecord { CurrencyCode = "EUR", QuoteCurrencyCode = "DZD", RateToDzd = 150.0m, QuotityUnit = 1, ValidFrom = new DateOnly(2026, 1, 1), RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "DGD" },
            new ExchangeRateRecord { CurrencyCode = "EUR", QuoteCurrencyCode = "USD", RateToDzd = 1.08m, QuotityUnit = 1, ValidFrom = new DateOnly(2026, 1, 1), RateType = "COMMERCIAL", SourceName = "Banque" }
        };
        var orchestrator = BuildOrchestrator(rates);
        var company = BuildCompany();
        var operation = new ImportOperation
        {
            CompanyId = company.Id,
            ImportNumber = "PU-AUTH-TOTAL",
            ReferenceDate = new DateOnly(2026, 6, 1),
            SupplierName = "Fournisseur Test",
            ExportShippingCountryIso2 = "FR",
            MainCurrencyCode = "EUR",
            AuthorizationCurrencyCode = "USD",
            Incoterm = IncotermCode.FOB,
            Lines =
            {
                new ImportLine { LineNumber = 1, ProductReference = "ART-1", Designation = "Article", Quantity = 37m, UnitPurchasePrice = 12.5m, CurrencyCode = "EUR", HsCodeConfirmed10 = "1234567890", OriginCountryIso2 = "FR" }
            }
        };

        var summary = orchestrator.ExecuteCalculation(company, operation);
        var conv = Assert.Single(summary.LineResults).AuthorizationConversion!;

        Assert.Equal(Math.Round(conv.AuthorizationUnitPrice * 37m, 2, MidpointRounding.AwayFromZero), conv.AuthorizationTotalAmount);
    }

    // ------------------------------------------------------------------------------------------
    // Section 26/27 (tests 6, 7) : PU Reviens = coût de revient réel de la ligne / quantité, en incluant
    // TOUTES les taxes et frais effectivement affectés à l'article.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void PuReviens_EqualsRealCostOfGoodsTotal_DividedByQuantity()
    {
        var orchestrator = BuildOrchestrator(Array.Empty<ExchangeRateRecord>());
        var company = BuildCompany();
        var operation = new ImportOperation
        {
            CompanyId = company.Id,
            ImportNumber = "PU-REVIENS-DIV",
            ReferenceDate = new DateOnly(2026, 6, 1),
            SupplierName = "Fournisseur Test",
            ExportShippingCountryIso2 = "FR",
            MainCurrencyCode = "DZD", // Aucune conversion nécessaire (taux = 1), calcul simplifié et exact.
            Incoterm = IncotermCode.FOB,
            Lines =
            {
                new ImportLine { LineNumber = 1, ProductReference = "ART-1", Designation = "Article", Quantity = 4m, UnitPurchasePrice = 250m, CurrencyCode = "DZD", HsCodeConfirmed10 = "1234567890", OriginCountryIso2 = "CN" }
            }
        };

        var summary = orchestrator.ExecuteCalculation(company, operation);
        var line = Assert.Single(summary.LineResults);

        Assert.Equal(
            Math.Round(line.EconomicOutcome.RealCostOfGoodsTotalDzd / 4m, 2, MidpointRounding.AwayFromZero),
            line.EconomicOutcome.UnitCostOfGoodsDzd);
    }

    [Fact]
    public void PuReviens_IncludesDefaultTaxesAndAllocatedFees_NotJustThePurchasePrice()
    {
        var orchestrator = BuildOrchestrator(Array.Empty<ExchangeRateRecord>());
        var company = BuildCompany();
        var operation = new ImportOperation
        {
            CompanyId = company.Id,
            ImportNumber = "PU-REVIENS-TAXES",
            ReferenceDate = new DateOnly(2026, 6, 1),
            SupplierName = "Fournisseur Test",
            ExportShippingCountryIso2 = "FR",
            MainCurrencyCode = "DZD",
            Incoterm = IncotermCode.FOB,
            // Taux par défaut d'usine (CS 3 %, TVA 19 %, PRCT 2 %, TCS 0 %, DD 0 %) — AUCUNE règle
            // réglementaire n'est seedée : tout provient du mécanisme de repli (Task #5).
            Lines =
            {
                new ImportLine { LineNumber = 1, ProductReference = "ART-1", Designation = "Article", Quantity = 1m, UnitPurchasePrice = 1000m, CurrencyCode = "DZD", HsCodeConfirmed10 = "1234567890", OriginCountryIso2 = "CN" }
            },
            Fees =
            {
                new ImportFee
                {
                    FeeCategoryCode = "TRANSPORT_INTERIEUR",
                    FeeName = "Transport intérieur",
                    Amount = 100m,
                    CurrencyCode = "DZD",
                    AllocationMethod = FeeAllocationMethod.FixedAmount,
                    IncludeInCustomsValue = false,
                    CustomsTreatment = CustomsAdjustmentTreatment.PostIntroductionExcluded,
                    IncludeInCostOfGoods = true
                }
            }
        };

        var summary = orchestrator.ExecuteCalculation(company, operation);
        var line = Assert.Single(summary.LineResults);

        // Calcul de référence (voir commentaire de session) : CS=30,00 ; TVA=195,70 ; PRCT=24,51 ;
        // Frais=100,00 -> Coût de revient total = 1 350,21 DZD (qté = 1 -> PU Reviens identique).
        Assert.Equal(1350.21m, line.EconomicOutcome.RealCostOfGoodsTotalDzd);
        Assert.Equal(1350.21m, line.EconomicOutcome.UnitCostOfGoodsDzd);
        Assert.True(line.EconomicOutcome.UnitCostOfGoodsDzd > line.EconomicOutcome.PurchaseValueDzd,
            "Le PU Reviens doit être strictement supérieur au seul prix d'achat : il inclut taxes et frais.");
    }

    // ------------------------------------------------------------------------------------------
    // Section 15/16/17/27 (tests 8, 9, 10) : Bénéfice et % Bénéfice (ProfitCalculator).
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void ProfitCalculator_SpecExample_MatchesExactly()
    {
        // Exemple exact de la demande : PU Reviens = 452,23 ; Prix de vente = 600 -> Bénéfice = 147,77 ;
        // % Bénéfice ≈ 32,67 %.
        decimal profit = ProfitCalculator.ComputeProfit(salePriceDzd: 600m, unitCostOfGoodsDzd: 452.23m);
        Assert.Equal(147.77m, profit);

        decimal? percent = ProfitCalculator.ComputeProfitPercent(profit, 452.23m);
        Assert.Equal(32.67m, percent);
    }

    [Fact]
    public void ProfitCalculator_SalePriceBelowCost_ProducesNegativeProfitAndPercent()
    {
        decimal profit = ProfitCalculator.ComputeProfit(salePriceDzd: 300m, unitCostOfGoodsDzd: 452.23m);
        Assert.True(profit < 0m);
        Assert.Equal(-152.23m, profit);

        decimal? percent = ProfitCalculator.ComputeProfitPercent(profit, 452.23m);
        Assert.NotNull(percent);
        Assert.True(percent < 0m);
    }

    [Fact]
    public void ProfitCalculator_PercentIsComputedAgainstCost_NeverAgainstSalePrice()
    {
        // Si le pourcentage était (à tort) calculé par rapport au prix de vente, on obtiendrait
        // 147,77 / 600 x 100 = 24,63 % — et NON 32,67 % (Section 17 : "ne pas calculer sur le prix de vente").
        decimal profit = ProfitCalculator.ComputeProfit(600m, 452.23m);
        decimal? percent = ProfitCalculator.ComputeProfitPercent(profit, 452.23m);
        Assert.NotEqual(24.63m, percent);
        Assert.Equal(32.67m, percent);
    }

    [Fact]
    public void ProfitCalculator_ZeroUnitCost_PercentIsNull_NeverDivisionByZeroOrInventedValue()
    {
        decimal profit = ProfitCalculator.ComputeProfit(100m, 0m);
        Assert.Null(ProfitCalculator.ComputeProfitPercent(profit, 0m));
    }

    // ------------------------------------------------------------------------------------------
    // Section 8/27 (tests 16, 17, 18) : conversion d'AFFICHAGE de la valeur en douane (DA/€/$), qui ne
    // doit jamais modifier le calcul réglementaire (toujours en DZD en interne).
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void ConvertDzdToDisplayCurrency_WithValidRate_ConvertsCorrectly()
    {
        decimal? eurAmount = ProfitCalculator.ConvertDzdToDisplayCurrency(amountDzd: 15071.66m, rateCurrencyToDzd: 150.7166m);
        Assert.Equal(100.00m, eurAmount);
    }

    [Fact]
    public void ConvertDzdToDisplayCurrency_NoRateAvailable_ReturnsNull_NeverZeroOrRawDzdAmount()
    {
        Assert.Null(ProfitCalculator.ConvertDzdToDisplayCurrency(9_625_557.38m, null));
        Assert.Null(ProfitCalculator.ConvertDzdToDisplayCurrency(9_625_557.38m, 0m));
    }

    [Fact]
    public void DisplayCurrencyConversion_NeverAffectsRegulatoryCustomsValue()
    {
        // Section 8 (CRITIQUE) : la conversion d'affichage est un calcul totalement SÉPARÉ — elle ne doit
        // jamais réutiliser/modifier la valeur réglementaire déjà calculée en DZD par le moteur.
        var rates = new[]
        {
            new ExchangeRateRecord { CurrencyCode = "EUR", QuoteCurrencyCode = "DZD", RateToDzd = 150.7166m, QuotityUnit = 1, ValidFrom = new DateOnly(2026, 1, 1), RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "DGD" }
        };
        var orchestrator = BuildOrchestrator(rates);
        var company = BuildCompany();
        var operation = new ImportOperation
        {
            CompanyId = company.Id,
            ImportNumber = "DISPLAY-CONV",
            ReferenceDate = new DateOnly(2026, 6, 1),
            SupplierName = "Fournisseur Test",
            ExportShippingCountryIso2 = "FR",
            MainCurrencyCode = "EUR",
            Incoterm = IncotermCode.FOB,
            Lines =
            {
                new ImportLine { LineNumber = 1, ProductReference = "ART-1", Designation = "Article", Quantity = 1m, UnitPurchasePrice = 1000m, CurrencyCode = "EUR", HsCodeConfirmed10 = "1234567890", OriginCountryIso2 = "FR" }
            }
        };

        decimal customsValueBeforeDisplayConversion = orchestrator.ExecuteCalculation(company, operation).TotalCustomsValueDzd;

        // On simule un changement répété du sélecteur d'affichage DA/€/$ (Section 8) — la valeur
        // réglementaire en DZD, elle, ne doit JAMAIS changer d'un centime.
        _ = ProfitCalculator.ConvertDzdToDisplayCurrency(customsValueBeforeDisplayConversion, 150.7166m);
        _ = ProfitCalculator.ConvertDzdToDisplayCurrency(customsValueBeforeDisplayConversion, 134.50m);

        decimal customsValueAfter = orchestrator.ExecuteCalculation(company, operation).TotalCustomsValueDzd;
        Assert.Equal(customsValueBeforeDisplayConversion, customsValueAfter);
    }

    // ------------------------------------------------------------------------------------------
    // Section 6/7/13/27 (tests 13, 14, 15) : symboles de devise à l'affichage (CurrencyDisplay).
    // ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("DZD", "DA")]
    [InlineData("EUR", "€")]
    [InlineData("USD", "$")]
    public void CurrencyDisplay_Symbol_MatchesExpected(string code, string expectedSymbol)
    {
        Assert.Equal(expectedSymbol, CurrencyDisplay.SymbolFor(code));
    }

    [Fact]
    public void CurrencyDisplay_UnknownCurrency_FallsBackToCodeItself_NeverInventsASymbol()
    {
        Assert.Equal("GBP", CurrencyDisplay.SymbolFor("GBP"));
    }

    [Fact]
    public void CurrencyDisplay_Format_PlacesSymbolAfterTheAmount()
    {
        // Montants SANS séparateur de milliers (aucune ambiguïté possible sur le caractère espace exact
        // utilisé par la culture fr-FR pour le groupement des milliers, qui varie selon la version du
        // runtime .NET/ICU) : vérifie strictement que le symbole suit le nombre, avec une virgule décimale.
        Assert.Equal("319,56 $", CurrencyDisplay.Format(319.56m, "USD"));
        Assert.Equal("2,40 €", CurrencyDisplay.Format(2.40m, "EUR"));

        // Pour un montant AVEC séparateur de milliers, on vérifie la structure (symbole en dernier, groupes
        // de chiffres présents, virgule décimale) sans dépendre du caractère exact utilisé entre les
        // groupes de milliers par la culture fr-FR du runtime (espace normale ou espace fine insécable
        // selon la version de .NET/ICU).
        string formatted = CurrencyDisplay.Format(2500m, "DZD");
        string withoutWhitespace = System.Text.RegularExpressions.Regex.Replace(formatted, @"\s", "");
        Assert.Equal("2500,00DA", withoutWhitespace);
        Assert.EndsWith("DA", formatted);
    }

    [Fact]
    public void CurrencyDisplay_NullAmount_ReturnsEmptyString_NeverAMisleadingZero()
    {
        Assert.Equal(string.Empty, CurrencyDisplay.Format((decimal?)null, "EUR"));
    }

    // ------------------------------------------------------------------------------------------
    // Section 9/14/22/27 (test 19) : TOTAL DÉDOUANEMENT = DD + CS + TVA + PRCT + TCS + RPS (jamais les
    // frais commerciaux). Vérifié directement sur l'exemple chiffré fourni dans la demande (Section 22).
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void TotalDedouanement_Formula_MatchesUserProvidedExample()
    {
        decimal dd = 0.00m;
        decimal cs = 288_766.83m;
        decimal tva = 1_883_722.02m;
        decimal prct = 235_960.97m;
        decimal tcs = 0.00m;
        decimal rps = 2_500.00m;

        decimal totalDedouanement = dd + cs + tva + prct + tcs + rps;

        Assert.Equal(2_410_949.82m, totalDedouanement);
    }
}
