using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.RegulatoryEngine;
using ImportCostAlgeria.CalculationEngine;
using ImportCostAlgeria.AI;
using ImportCostAlgeria.Database;
using ImportCostAlgeria.Database.Repositories;
using ImportCostAlgeria.Database.Security;

namespace ImportCostAlgeria.UnitTests;

/// <summary>
/// Tests ajoutés par l'audit "multi-devises EUR/USD/DZD" (Section 20 du plan d'audit) :
///   - Section 3 : normalisation de la Quotité (QuotityUnit) du taux de change, avant/après correctif.
///   - Section 2  : non-régression du simulateur IA "fret +20 %" (baseLine/simLine).
///   - Section 8  : service centralisé de conversion commerciale (CurrencyConversionService), toutes les
///     paires nécessaires (EUR->USD, USD->EUR, EUR->DZD, USD->DZD) sans jamais mélanger avec le calcul
///     réglementaire.
///   - Section 9  : seuil d'avertissement "⚠️ TAUX MANUEL".
///   - Section 18 : génération sécurisée du mot de passe administrateur initial.
///   - Section 6/14 de l'audit BDD : un nouveau taux publié ne clôture jamais une autre paire de devises.
/// </summary>
public sealed class MultiCurrencyAndSecurityTests
{
    private static readonly LegalSource TestLegalSource = new()
    {
        HierarchyLevel = LegalSourceHierarchyLevel.Level1_JournalOfficielJora,
        OfficialTitle = "Tarif Douanier Officiel Algérie (jeu de test)",
        JoraReference = "JORA N° 1 (TEST)",
        ArticleReference = "Art. 16 ter CDA (TEST)",
        PublicationDate = new DateOnly(2026, 1, 1),
        EffectiveDate = new DateOnly(2026, 1, 1)
    };

    private static ImportCalculationOrchestrator BuildOrchestrator(
        IEnumerable<ExchangeRateRecord> rates,
        IEnumerable<RegulatoryRule> rules)
    {
        var rateProvider = new InMemoryExchangeRateProvider(rates);
        return new ImportCalculationOrchestrator(
            new CurrencyCalculator(rateProvider),
            new CostAllocationEngine(),
            new CustomsValueCalculator(),
            new RegulatoryRuleEngine(new InMemoryRegulatoryRuleRepository(rules)),
            new CurrencyConversionService(rateProvider));
    }

    private static Company BuildCompany() => new()
    {
        Code = "TEST-DZ",
        LegalName = "SARL TEST MULTI-DEVISES",
        IsImportVatNonRecoverable = true
    };

    private static RegulatoryRule BuildCustomsDutyRule(string hsCode, decimal ratePercent) => new()
    {
        Code = $"DD-{hsCode}-2026",
        RegulatoryVersionCode = "2026.01",
        RuleType = RegulatoryRuleType.CustomsDuty,
        TaxCode = "DD",
        TaxNameFr = "Droit de Douane",
        HsCode10 = hsCode,
        RatePercent = ratePercent,
        CalculationBase = TaxableBaseType.CustomsValueDzd,
        ValidFrom = new DateOnly(2026, 1, 1),
        LegalSource = TestLegalSource,
        Status = RegulatoryRuleStatus.PublishedNewVersion
    };

    private static RegulatoryRule BuildVatRule(string hsCode, decimal ratePercent) => new()
    {
        Code = $"TVA-{hsCode}-2026",
        RegulatoryVersionCode = "2026.01",
        RuleType = RegulatoryRuleType.Vat,
        TaxCode = "TVA",
        TaxNameFr = "Taxe sur la Valeur Ajoutée",
        HsCode10 = hsCode,
        RatePercent = ratePercent,
        CalculationBase = TaxableBaseType.CustomsValuePlusDutiesAndTaxesExVatDzd,
        ValidFrom = new DateOnly(2026, 1, 1),
        LegalSource = TestLegalSource,
        Status = RegulatoryRuleStatus.PublishedNewVersion
    };

    // ------------------------------------------------------------------
    // Section 3 : Quotité (QuotityUnit) — taux "pour 1 unité" vs "pour 100 unités"
    // ------------------------------------------------------------------

    [Fact]
    public void ResolveRate_WithQuotityUnitOne_ShouldReturnRateAsIs()
    {
        var rate = new ExchangeRateRecord
        {
            CurrencyCode = "EUR",
            QuoteCurrencyCode = "DZD",
            RateToDzd = 146.50m,
            QuotityUnit = 1,
            ValidFrom = new DateOnly(2026, 1, 1),
            RateType = "OFFICIEL_DOUANE_ALCES",
            SourceName = "Test"
        };
        var calc = new CurrencyCalculator(new InMemoryExchangeRateProvider(new[] { rate }));

        var (effectiveRate, _, anomaly) = calc.ResolveRate("EUR", new DateOnly(2026, 1, 15), null);

        Assert.Equal(146.50m, effectiveRate);
        Assert.Null(anomaly);
    }

    [Fact]
    public void ResolveRate_WithQuotityUnitOneHundred_ShouldDivideByQuotityBeforeUse()
    {
        // Convention JPY-like : certaines devises se cotent "pour 100 unités". Si la base officielle
        // enregistre "100 XXX = 25 000 DZD", le taux économique par unité doit être 250 DZD, pas 25 000.
        var rate = new ExchangeRateRecord
        {
            CurrencyCode = "XXX",
            QuoteCurrencyCode = "DZD",
            RateToDzd = 25000m,
            QuotityUnit = 100,
            ValidFrom = new DateOnly(2026, 1, 1),
            RateType = "OFFICIEL_DOUANE_ALCES",
            SourceName = "Test"
        };
        var calc = new CurrencyCalculator(new InMemoryExchangeRateProvider(new[] { rate }));

        var (effectiveRate, _, anomaly) = calc.ResolveRate("XXX", new DateOnly(2026, 1, 15), null);

        Assert.Equal(250m, effectiveRate);
        Assert.Null(anomaly);
    }

    [Fact]
    public void ResolveRate_ManualRate_ShouldBeComparedAgainstUnitNormalizedOfficialRate_NotRawRate()
    {
        // Bug corrigé (Section 3 de l'audit) : avant correctif, la comparaison manuel/officiel utilisait le
        // taux officiel BRUT (25 000) au lieu du taux par unité (250), ce qui déclenchait une fausse alerte
        // "TAUX MANUEL" même quand l'utilisateur saisissait exactement le même taux économique (250).
        var officialRate = new ExchangeRateRecord
        {
            CurrencyCode = "XXX",
            QuoteCurrencyCode = "DZD",
            RateToDzd = 25000m,
            QuotityUnit = 100,
            ValidFrom = new DateOnly(2026, 1, 1),
            RateType = "OFFICIEL_DOUANE_ALCES",
            SourceName = "Test"
        };
        var calc = new CurrencyCalculator(new InMemoryExchangeRateProvider(new[] { officialRate }));

        // Taux manuel saisi par l'utilisateur en "unité par unité" (250), identique au taux officiel normalisé.
        var (effectiveRate, _, noAnomaly) = calc.ResolveRate("XXX", new DateOnly(2026, 1, 15), 250m);
        Assert.Equal(250m, effectiveRate);
        Assert.Null(noAnomaly);

        // Taux manuel réellement différent (300 au lieu de 250) doit toujours déclencher l'avertissement.
        var (_, _, warning) = calc.ResolveRate("XXX", new DateOnly(2026, 1, 15), 300m);
        Assert.NotNull(warning);
        Assert.Contains("TAUX MANUEL", warning!.MessageFr);
    }

    [Fact]
    public void ResolveRate_ManualRateDifference_BelowThreshold_ShouldNotWarn_AboveThreshold_ShouldWarn()
    {
        var official = new ExchangeRateRecord
        {
            CurrencyCode = "EUR",
            QuoteCurrencyCode = "DZD",
            RateToDzd = 146.50m,
            QuotityUnit = 1,
            ValidFrom = new DateOnly(2026, 1, 1),
            RateType = "OFFICIEL_DOUANE_ALCES",
            SourceName = "Test"
        };
        var calc = new CurrencyCalculator(new InMemoryExchangeRateProvider(new[] { official }));

        // Écart relatif ~0,27 % (< 0,5 %) : ne doit pas déclencher l'avertissement (simple écart d'arrondi).
        var (_, _, belowThreshold) = calc.ResolveRate("EUR", new DateOnly(2026, 1, 15), 146.90m);
        Assert.Null(belowThreshold);

        // Écart relatif > 0,5 % : doit déclencher l'avertissement "⚠️ TAUX MANUEL".
        var (_, _, aboveThreshold) = calc.ResolveRate("EUR", new DateOnly(2026, 1, 15), 150.00m);
        Assert.NotNull(aboveThreshold);
        Assert.Equal("MANUAL_EXCHANGE_RATE_DIFF", aboveThreshold!.AnomalyCode);
    }

    [Fact]
    public void ResolveRate_EurToDzd_And_UsdToDzd_ShouldUseDistinctIndependentRates()
    {
        var rates = new[]
        {
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR", QuoteCurrencyCode = "DZD", RateToDzd = 146.50m, QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1), RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "Test"
            },
            new ExchangeRateRecord
            {
                CurrencyCode = "USD", QuoteCurrencyCode = "DZD", RateToDzd = 134.20m, QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1), RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "Test"
            }
        };
        var calc = new CurrencyCalculator(new InMemoryExchangeRateProvider(rates));

        var (eurRate, _, _) = calc.ResolveRate("EUR", new DateOnly(2026, 1, 15), null);
        var (usdRate, _, _) = calc.ResolveRate("USD", new DateOnly(2026, 1, 15), null);

        Assert.Equal(146.50m, eurRate);
        Assert.Equal(134.20m, usdRate);
        Assert.NotEqual(eurRate, usdRate); // Jamais le même taux supposé pour deux devises différentes.
    }

    // ------------------------------------------------------------------
    // Section 8 : CurrencyConversionService centralisé — toutes les paires commerciales nécessaires
    // ------------------------------------------------------------------

    [Fact]
    public void CurrencyConversionService_EurToUsd_ShouldUseDedicatedCrossRate_NeverDzdRate()
    {
        var rates = new[]
        {
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR", QuoteCurrencyCode = "DZD", RateToDzd = 146.50m, QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1), RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "Test DZD"
            },
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR", QuoteCurrencyCode = "USD", RateToDzd = 1.08m, QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1), RateType = "COMMERCIAL_AUTORISATION", SourceName = "Test USD"
            }
        };
        var service = new CurrencyConversionService(new InMemoryExchangeRateProvider(rates));

        var outcome = service.Convert(1000m, "EUR", "USD", new DateOnly(2026, 1, 15), null);

        Assert.Equal(1000m, outcome.OriginalAmount); // La devise/montant d'origine n'est jamais remplacé.
        Assert.Equal("EUR", outcome.FromCurrencyCode);
        Assert.Equal("USD", outcome.ToCurrencyCode);
        Assert.Equal(1.08m, outcome.EffectiveRate); // Et surtout pas 146.50 (taux DZD) !
        Assert.Equal(1080m, outcome.ConvertedAmount);
        Assert.False(outcome.IsManualRate);
    }

    [Fact]
    public void CurrencyConversionService_QuotityUnit100_ShouldNormalizeToPerUnitRate_ForCommercialConversion()
    {
        // Exigence explicite de la revue (Section 7) : la normalisation de Quotité (ex: "100 XXX = Y DZD"
        // -> "1 XXX = Y/100 DZD") doit s'appliquer aussi bien au taux réglementaire qu'au taux COMMERCIAL
        // (ex: EUR -> USD), via la même logique partagée (ExchangeRateNormalization.ToUnitRate).
        var rates = new[]
        {
            new ExchangeRateRecord
            {
                CurrencyCode = "XXX", QuoteCurrencyCode = "USD", RateToDzd = 11700m, QuotityUnit = 100,
                ValidFrom = new DateOnly(2026, 1, 1), RateType = "COMMERCIAL_AUTORISATION", SourceName = "Test Quotité"
            }
        };
        var service = new CurrencyConversionService(new InMemoryExchangeRateProvider(rates));

        var outcome = service.Convert(1000m, "XXX", "USD", new DateOnly(2026, 1, 15), null);

        // "100 XXX = 11 700 DZD" doit se normaliser en "1 XXX = 117 (unité de cotation)" avant application.
        Assert.Equal(117m, outcome.EffectiveRate);
        Assert.Equal(117000m, outcome.ConvertedAmount); // 1000 x 117.
    }

    [Fact]
    public void CurrencyConversionService_SameCurrency_ShouldReturnIdentityWithoutRequiringAnyRate()
    {
        var service = new CurrencyConversionService(new InMemoryExchangeRateProvider(Array.Empty<ExchangeRateRecord>()));
        var outcome = service.Convert(500m, "USD", "USD", new DateOnly(2026, 1, 15), null);

        Assert.Equal(1.0m, outcome.EffectiveRate);
        Assert.Equal(500m, outcome.ConvertedAmount);
        Assert.Null(outcome.Anomaly);
    }

    [Fact]
    public void CurrencyConversionService_MissingRate_ShouldSignalAbsence_NeverInventARate()
    {
        var service = new CurrencyConversionService(new InMemoryExchangeRateProvider(Array.Empty<ExchangeRateRecord>()));
        var outcome = service.Convert(500m, "EUR", "USD", new DateOnly(2026, 1, 15), null);

        Assert.Equal(0m, outcome.EffectiveRate);
        Assert.NotNull(outcome.Anomaly);
        Assert.Equal("MISSING_COMMERCIAL_EXCHANGE_RATE", outcome.Anomaly!.AnomalyCode);
    }

    [Fact]
    public void CurrencyConversionService_ManualRate_DifferentFromOfficial_ShouldWarnWithTauxManuelLabel()
    {
        var rates = new[]
        {
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR", QuoteCurrencyCode = "USD", RateToDzd = 1.08m, QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1), RateType = "COMMERCIAL_AUTORISATION", SourceName = "Test USD"
            }
        };
        var service = new CurrencyConversionService(new InMemoryExchangeRateProvider(rates));

        var outcome = service.Convert(1000m, "EUR", "USD", new DateOnly(2026, 1, 15), 1.20m);

        Assert.True(outcome.IsManualRate);
        Assert.NotNull(outcome.Anomaly);
        Assert.Contains("TAUX MANUEL", outcome.Anomaly!.MessageFr);
        Assert.Equal(1200m, outcome.ConvertedAmount); // Le taux manuel est bien appliqué (jamais ignoré).
    }

    // ------------------------------------------------------------------
    // Flux complet EUR (facture) -> USD (autorisation, commercial) -> DZD (régime douanier, réglementaire)
    // sans jamais mélanger les deux conversions (Sections 4, 6, 13, 14 du plan multi-devises).
    // ------------------------------------------------------------------

    [Fact]
    public void FullCalculation_ShouldExposeCommercialUsdConversion_SeparatelyFromRegulatoryDzdValue()
    {
        var company = BuildCompany();
        var rates = new[]
        {
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR", QuoteCurrencyCode = "DZD", RateToDzd = 146.50m, QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1), ValidTo = new DateOnly(2026, 12, 31),
                RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "Portail officiel ALCES"
            },
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR", QuoteCurrencyCode = "USD", RateToDzd = 1.08m, QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1), ValidTo = new DateOnly(2026, 12, 31),
                RateType = "COMMERCIAL_AUTORISATION", SourceName = "BCE (référence commerciale)"
            }
        };

        // Correction (revue Visual Studio du 2026-10-01, 2e analyse) : la véritable cause de l'anomalie
        // BLOQUANTE n'est pas l'Incoterm (FOB puis CFR) mais une donnée de test manquante. Le moteur
        // réglementaire (RegulatoryRuleEngine.ResolveApplicableRules) n'accepte comme "règle officielle
        // valide" que les règles PUBLIÉES ET VALIDÉES PAR UN ADMINISTRATEUR (ValidatedByAdminUserId non
        // nul) — règle métier légitime, identique au principe de validation humaine déjà appliqué aux codes
        // SH, qui n'est PAS modifiée ici. Sans cette validation, dutyRule/vatRule ne sont jamais résolues
        // (IsDetermined = false), ce qui produit l'anomalie BLOQUANTE "REGULATORY_RULE_NOT_FOUND" — et ce,
        // quel que soit l'Incoterm utilisé. Les autres tests de ce fichier ne sont pas affectés par cette
        // même lacune car ils n'asserent pas HasBlockingAnomalies ; seul ce test (qui l'asserte) la révèle.
        var dutyRule = BuildCustomsDutyRule("8482.10.00.00", 15.0m);
        dutyRule.ValidatedByAdminUserId = Guid.NewGuid();
        var vatRule = BuildVatRule("8482.10.00.00", 19.0m);
        vatRule.ValidatedByAdminUserId = Guid.NewGuid();

        var rules = new[] { dutyRule, vatRule };

        var operation = new ImportOperation
        {
            CompanyId = company.Id,
            ImportNumber = "IMP-MULTIDEVISES-01",
            ReferenceDate = new DateOnly(2026, 1, 15),
            SupplierName = "Fournisseur Hambourg",
            ExportShippingCountryIso2 = "DE",
            DefaultOriginCountryIso2 = "DE",
            MainCurrencyCode = "EUR",
            AuthorizationCurrencyCode = "USD",
            // Correction (revue Visual Studio du 2026-10-01) : ce test porte UNIQUEMENT sur la séparation
            // de la conversion commerciale EUR->USD et du calcul réglementaire EUR->DZD — il ne doit pas
            // vérifier, incidemment, les règles de contrôle documentaire propres à l'Incoterm FOB (qui exige
            // un frais FRET_INTERNATIONAL, volontairement absent ici). CFR ne requiert, lui, qu'une simple
            // assurance (avertissement non bloquant), ce qui permet de tester la conversion de devises sans
            // déclencher artificiellement une anomalie bloquante sans rapport avec l'objet du test. La
            // logique métier du moteur de calcul relative aux Incoterms n'est pas modifiée.
            Incoterm = IncotermCode.CFR,
            ArrivalPortOrBorder = "Port d'Alger",
            TransportMode = "Maritime",
            Lines = new List<ImportLine>
            {
                new()
                {
                    LineNumber = 1,
                    ProductReference = "ROUL-001",
                    Designation = "Roulement à billes",
                    Quantity = 100m,
                    MeasurementUnit = "PCE",
                    UnitPurchasePrice = 10m, // 10 EUR/pièce -> 1 000 EUR au total
                    CurrencyCode = "EUR",
                    HsCodeConfirmed10 = "8482.10.00.00",
                    OriginCountryIso2 = "DE"
                }
            },
            Fees = new List<ImportFee>()
        };

        var orchestrator = BuildOrchestrator(rates, rules);
        var summary = orchestrator.ExecuteCalculation(company, operation);

        // La valeur réglementaire/douanière reste calculée en EUR -> DZD, jamais via l'USD.
        Assert.Equal(1000m, summary.TotalPurchaseValueMainCurrency);
        Assert.Equal(146500m, summary.TotalPurchaseValueDzd); // 1000 EUR * 146.50, pas de passage par l'USD.

        // La conversion commerciale d'autorisation (USD) est bien présente, distincte, et n'a pas altéré le DZD.
        Assert.NotNull(summary.CommercialAuthorizationConversion);
        var conv = summary.CommercialAuthorizationConversion!;
        Assert.Equal("EUR", conv.OriginalCurrencyCode);
        Assert.Equal(1000m, conv.OriginalTotalAmount);
        Assert.Equal("USD", conv.AuthorizationCurrencyCode);
        Assert.Equal(1080m, conv.AuthorizationTotalAmount); // 1000 EUR * 1.08
        Assert.False(conv.IsManualRate);

        // La ligne porte elle aussi sa propre conversion commerciale, cohérente avec la quantité/prix unitaire.
        var line = summary.LineResults.Single();
        Assert.NotNull(line.AuthorizationConversion);
        Assert.Equal(1080m, line.AuthorizationConversion!.AuthorizationTotalAmount);
        Assert.Equal(10.80m, line.AuthorizationConversion.AuthorizationUnitPrice); // 1080 / 100 pièces

        // Aucune anomalie bloquante : l'absence éventuelle de taux commercial ne doit jamais bloquer le calcul douanier.
        Assert.False(summary.HasBlockingAnomalies);
    }

    [Fact]
    public void FullCalculation_WhenInvoiceAlreadyInAuthorizationCurrency_ShouldNotProduceUselessConversion()
    {
        var company = BuildCompany();
        var rates = new[]
        {
            new ExchangeRateRecord
            {
                CurrencyCode = "USD", QuoteCurrencyCode = "DZD", RateToDzd = 134.20m, QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1), RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "Test"
            }
        };
        var rules = new[]
        {
            BuildCustomsDutyRule("8482.10.00.00", 15.0m),
            BuildVatRule("8482.10.00.00", 19.0m)
        };

        var operation = new ImportOperation
        {
            CompanyId = company.Id,
            ImportNumber = "IMP-USD-01",
            ReferenceDate = new DateOnly(2026, 1, 15),
            SupplierName = "Fournisseur US",
            ExportShippingCountryIso2 = "US",
            DefaultOriginCountryIso2 = "US",
            MainCurrencyCode = "USD",
            AuthorizationCurrencyCode = "USD", // Facture déjà dans la devise d'autorisation.
            Incoterm = IncotermCode.FOB,
            ArrivalPortOrBorder = "Port d'Alger",
            TransportMode = "Maritime",
            Lines = new List<ImportLine>
            {
                new()
                {
                    LineNumber = 1,
                    ProductReference = "ROUL-002",
                    Designation = "Roulement à billes",
                    Quantity = 50m,
                    MeasurementUnit = "PCE",
                    UnitPurchasePrice = 20m,
                    CurrencyCode = "USD",
                    HsCodeConfirmed10 = "8482.10.00.00",
                    OriginCountryIso2 = "US"
                }
            },
            Fees = new List<ImportFee>()
        };

        var orchestrator = BuildOrchestrator(rates, rules);
        var summary = orchestrator.ExecuteCalculation(company, operation);

        // Section 15 : pas de conversion inutile affichée quand la facture est déjà dans la devise d'autorisation.
        Assert.Null(summary.CommercialAuthorizationConversion);
        Assert.Null(summary.LineResults.Single().AuthorizationConversion);
    }

    // ------------------------------------------------------------------
    // Section 2 : non-régression du simulateur IA "fret +20 %" (baseLine = AVANT, simLine = APRÈS)
    // ------------------------------------------------------------------

    [Fact]
    public void AiFreightSimulation_PlusTwentyPercent_ShouldReportBeforeFromCurrent_AfterFromSimulation()
    {
        var company = BuildCompany();
        var rates = new[]
        {
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR", QuoteCurrencyCode = "DZD", RateToDzd = 146.50m, QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1), RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "Test"
            }
        };
        var rules = new[]
        {
            BuildCustomsDutyRule("8482.10.00.00", 15.0m),
            BuildVatRule("8482.10.00.00", 19.0m)
        };

        var fretFeeId = Guid.NewGuid();
        var operation = new ImportOperation
        {
            CompanyId = company.Id,
            ImportNumber = "IMP-FRET-01",
            ReferenceDate = new DateOnly(2026, 1, 15),
            SupplierName = "Fournisseur Hambourg",
            ExportShippingCountryIso2 = "DE",
            DefaultOriginCountryIso2 = "DE",
            MainCurrencyCode = "EUR",
            Incoterm = IncotermCode.FOB,
            ArrivalPortOrBorder = "Port d'Alger",
            TransportMode = "Maritime",
            Lines = new List<ImportLine>
            {
                new()
                {
                    LineNumber = 1,
                    ProductReference = "ROUL-003",
                    Designation = "Roulement à billes",
                    Quantity = 100m,
                    MeasurementUnit = "PCE",
                    UnitPurchasePrice = 10m,
                    CurrencyCode = "EUR",
                    HsCodeConfirmed10 = "8482.10.00.00",
                    OriginCountryIso2 = "DE"
                }
            },
            Fees = new List<ImportFee>
            {
                new()
                {
                    Id = fretFeeId,
                    FeeCategoryCode = "FRET_INTERNATIONAL",
                    FeeName = "Fret maritime",
                    Amount = 300m,
                    CurrencyCode = "EUR",
                    AllocationMethod = FeeAllocationMethod.ByValue,
                    IncludeInCustomsValue = true,
                    IncludeInCostOfGoods = true
                }
            }
        };

        var orchestrator = BuildOrchestrator(rates, rules);
        var currentCalculation = orchestrator.ExecuteCalculation(company, operation);

        var simulator = new ImportSimulatorService(orchestrator);
        var assistant = new RegulatoryAssistantEngine(simulator, new HSClassifierService());

        var response = assistant.AnswerUserQuery(
            "Quel serait le coût unitaire si le fret augmentait de 20 % ?", company, operation, currentCalculation);

        var calcStatement = response.Statements.Single(s => s.Tag == DataOriginTag.CalculDuLogiciel);

        decimal beforeUnitCost = currentCalculation.LineResults.Single().EconomicOutcome.UnitCostOfGoodsDzd;

        // Non-régression : le simulateur doit produire un coût APRÈS strictement supérieur au coût AVANT
        // (une hausse de fret ne peut jamais faire baisser le coût de revient), et le message doit citer le
        // AVANT réel (issu de currentCalculation / baseLine), pas une valeur simulée des deux côtés.
        Assert.Contains($"{beforeUnitCost:N2} DZD", calcStatement.ContentFr);

        // Recalcule manuellement la simulation pour vérifier la valeur APRÈS indépendamment de l'assistant IA.
        var sim = simulator.RunSimulation(company, operation, new SimulationScenarioOverrides(FreightFactorPercent: +20m));
        decimal afterUnitCost = sim.SimulatedCalculation.LineResults.Single().EconomicOutcome.UnitCostOfGoodsDzd;

        Assert.True(afterUnitCost > beforeUnitCost, "Une hausse du fret de +20% doit augmenter le coût de revient unitaire.");
        Assert.Contains($"{afterUnitCost:N2} DZD", calcStatement.ContentFr);

        // Audit 2026-10-01 (nouveau round, Section 1) : le message doit respecter EXACTEMENT le gabarit demandé
        // "Situation actuelle / Après +20 % de fret / Différence / Variation", et les deux valeurs doivent
        // être explicitement DIFFÉRENTES (jamais simLine utilisé pour les deux côtés de la comparaison).
        Assert.Contains($"Situation actuelle : {beforeUnitCost:N2} DZD", calcStatement.ContentFr);
        Assert.Contains($"Après +20 % de fret : {afterUnitCost:N2} DZD", calcStatement.ContentFr);
        Assert.Contains("Différence :", calcStatement.ContentFr);
        Assert.Contains("Variation :", calcStatement.ContentFr);
        Assert.NotEqual(beforeUnitCost, afterUnitCost); // Exigence explicite : les deux valeurs doivent différer.

        // L'importation réelle ne doit jamais être modifiée par la simulation (Section 30).
        Assert.Equal(300m, operation.Fees.Single().Amount);
    }

    // ------------------------------------------------------------------
    // Produit/Facture : totaux cohérents avec la quantité (Section 12 & 20)
    // ------------------------------------------------------------------

    [Fact]
    public void LineAndInvoiceTotals_ShouldStayConsistentWithQuantityAcrossOriginalAndConvertedCurrencies()
    {
        var company = BuildCompany();
        var rates = new[]
        {
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR", QuoteCurrencyCode = "DZD", RateToDzd = 146.50m, QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1), RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "Test"
            },
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR", QuoteCurrencyCode = "USD", RateToDzd = 1.08m, QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1), RateType = "COMMERCIAL_AUTORISATION", SourceName = "Test"
            }
        };
        var rules = new[]
        {
            BuildCustomsDutyRule("8482.10.00.00", 15.0m),
            BuildVatRule("8482.10.00.00", 19.0m)
        };

        const decimal quantity = 37m;
        const decimal unitPriceEur = 12.345m;

        var operation = new ImportOperation
        {
            CompanyId = company.Id,
            ImportNumber = "IMP-TOTAUX-01",
            ReferenceDate = new DateOnly(2026, 1, 15),
            SupplierName = "Fournisseur Test",
            ExportShippingCountryIso2 = "DE",
            DefaultOriginCountryIso2 = "DE",
            MainCurrencyCode = "EUR",
            AuthorizationCurrencyCode = "USD",
            Incoterm = IncotermCode.FOB,
            ArrivalPortOrBorder = "Port d'Alger",
            TransportMode = "Maritime",
            Lines = new List<ImportLine>
            {
                new()
                {
                    LineNumber = 1,
                    ProductReference = "ART-37",
                    Designation = "Article test quantité impaire",
                    Quantity = quantity,
                    MeasurementUnit = "PCE",
                    UnitPurchasePrice = unitPriceEur,
                    CurrencyCode = "EUR",
                    HsCodeConfirmed10 = "8482.10.00.00",
                    OriginCountryIso2 = "DE"
                }
            },
            Fees = new List<ImportFee>()
        };

        var orchestrator = BuildOrchestrator(rates, rules);
        var summary = orchestrator.ExecuteCalculation(company, operation);
        var line = summary.LineResults.Single();

        decimal expectedOriginalTotal = CurrencyCalculator.RoundDzd(quantity * unitPriceEur);
        Assert.Equal(expectedOriginalTotal, line.EconomicOutcome.PurchaseValueCurrency);

        decimal expectedUsdTotal = CurrencyCalculator.RoundDzd(expectedOriginalTotal * 1.08m);
        Assert.Equal(expectedUsdTotal, line.AuthorizationConversion!.AuthorizationTotalAmount);

        decimal expectedUsdUnit = Math.Round(expectedUsdTotal / quantity, 4, MidpointRounding.AwayFromZero);
        Assert.Equal(expectedUsdUnit, line.AuthorizationConversion.AuthorizationUnitPrice);

        // Le total ligne doit rester reconstructible à partir de quantité × prix unitaire USD (± 1 centime
        // pour l'arrondi), jamais une valeur incohérente avec la quantité réelle.
        decimal reconstructedTotal = Math.Round(line.AuthorizationConversion.AuthorizationUnitPrice * quantity, 2, MidpointRounding.AwayFromZero);
        Assert.True(Math.Abs(reconstructedTotal - line.AuthorizationConversion.AuthorizationTotalAmount) <= 0.05m);
    }

    // ------------------------------------------------------------------
    // Section 18 : génération sécurisée du mot de passe administrateur initial
    // ------------------------------------------------------------------

    [Fact]
    public void GenerateRandomPassword_ShouldNeverReturnTheOldHardcodedPassword()
    {
        for (int i = 0; i < 25; i++)
        {
            string generated = PasswordHasher.GenerateRandomPassword();
            Assert.NotEqual("Cimp@2026!", generated);
        }
    }

    [Fact]
    public void GenerateRandomPassword_ShouldRespectMinimumLengthAndComplexityRules()
    {
        string password = PasswordHasher.GenerateRandomPassword(14);

        Assert.Equal(14, password.Length);
        Assert.Contains(password, c => char.IsUpper(c));
        Assert.Contains(password, c => char.IsLower(c));
        Assert.Contains(password, c => char.IsDigit(c));
        Assert.Contains(password, c => !char.IsLetterOrDigit(c));

        // Pas de caractères ambigus (0/O, 1/l/I) pour une saisie manuelle fiable.
        Assert.DoesNotContain(password, c => c is '0' or 'O' or '1' or 'l' or 'I');
    }

    [Fact]
    public void GenerateRandomPassword_CalledTwice_ShouldProduceDifferentValues()
    {
        string first = PasswordHasher.GenerateRandomPassword();
        string second = PasswordHasher.GenerateRandomPassword();

        Assert.NotEqual(first, second); // Jamais le même mot de passe "aléatoire" généré deux fois de suite.
    }

    [Fact]
    public void HashAndVerify_ShouldRoundTripCorrectly_ForGeneratedPassword()
    {
        string password = PasswordHasher.GenerateRandomPassword();
        var (hash, salt) = PasswordHasher.HashNewPassword(password);

        Assert.True(PasswordHasher.Verify(password, hash, salt));
        Assert.False(PasswordHasher.Verify("mot-de-passe-incorrect", hash, salt));
    }

    // ------------------------------------------------------------------
    // Section 6/14 (BDD) : publier un nouveau taux pour une paire de devises ne doit jamais clôturer
    // l'historique d'une AUTRE paire de devises portant la même devise de base.
    // ------------------------------------------------------------------

    private sealed class SingleFileDbContextFactory : ICimpDbContextFactory, IDisposable
    {
        private readonly string _dbPath;

        public SingleFileDbContextFactory()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"cimp_test_{Guid.NewGuid():N}.db");
        }

        public ImportCostDbContext CreateGlobal() =>
            new(DbContextFactory.BuildOptions(DatabaseProviderKind.Sqlite, $"Data Source={_dbPath}"), null);

        public ImportCostDbContext CreateForCompany(Guid companyId) =>
            new(DbContextFactory.BuildOptions(DatabaseProviderKind.Sqlite, $"Data Source={_dbPath}"), companyId);

        public void Dispose()
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort cleanup */ }
        }
    }

    [Fact]
    public void PublishNewRate_ForDifferentQuoteCurrency_ShouldNotCloseUnrelatedCurrencyPairHistory()
    {
        using var factory = new SingleFileDbContextFactory();
        using (var ctx = factory.CreateGlobal())
        {
            ctx.Database.EnsureCreated();
        }

        var repository = new ExchangeRateAdminRepository(factory);
        var adminUserId = Guid.NewGuid();

        // 1) Publie un taux réglementaire EUR -> DZD.
        var eurToDzd = repository.PublishNewRate(new ExchangeRateRecord
        {
            CurrencyCode = "EUR",
            QuoteCurrencyCode = "DZD",
            RateToDzd = 146.50m,
            QuotityUnit = 1,
            ValidFrom = new DateOnly(2026, 1, 1),
            RateType = "OFFICIEL_DOUANE_ALCES",
            SourceName = "Test DZD"
        }, adminUserId, UserRole.Administrateur);

        // 2) Publie ensuite un taux commercial EUR -> USD (même devise de base, autre devise de cotation).
        repository.PublishNewRate(new ExchangeRateRecord
        {
            CurrencyCode = "EUR",
            QuoteCurrencyCode = "USD",
            RateToDzd = 1.08m,
            QuotityUnit = 1,
            ValidFrom = new DateOnly(2026, 1, 2),
            RateType = "COMMERCIAL_AUTORISATION",
            SourceName = "Test USD"
        }, adminUserId, UserRole.Administrateur);

        var all = repository.GetAll();
        var reloadedEurToDzd = all.Single(r => r.CurrencyCode == "EUR" && r.QuoteCurrencyCode == "DZD");
        var reloadedEurToUsd = all.Single(r => r.CurrencyCode == "EUR" && r.QuoteCurrencyCode == "USD");

        // Le taux EUR->DZD ne doit JAMAIS avoir été clôturé par la publication du taux EUR->USD.
        Assert.Null(reloadedEurToDzd.ValidTo);
        Assert.Null(reloadedEurToUsd.ValidTo);
        Assert.Equal(eurToDzd.Id, reloadedEurToDzd.Id);
    }

    [Fact]
    public void PublishNewRate_ForSameCurrencyPair_ShouldCloseThePreviousOpenPeriod()
    {
        using var factory = new SingleFileDbContextFactory();
        using (var ctx = factory.CreateGlobal())
        {
            ctx.Database.EnsureCreated();
        }

        var repository = new ExchangeRateAdminRepository(factory);
        var adminUserId = Guid.NewGuid();

        repository.PublishNewRate(new ExchangeRateRecord
        {
            CurrencyCode = "EUR",
            QuoteCurrencyCode = "DZD",
            RateToDzd = 146.50m,
            QuotityUnit = 1,
            ValidFrom = new DateOnly(2026, 1, 1),
            RateType = "OFFICIEL_DOUANE_ALCES",
            SourceName = "Ancien taux"
        }, adminUserId, UserRole.Administrateur);

        repository.PublishNewRate(new ExchangeRateRecord
        {
            CurrencyCode = "EUR",
            QuoteCurrencyCode = "DZD",
            RateToDzd = 148.00m,
            QuotityUnit = 1,
            ValidFrom = new DateOnly(2026, 2, 1),
            RateType = "OFFICIEL_DOUANE_ALCES",
            SourceName = "Nouveau taux"
        }, adminUserId, UserRole.Administrateur);

        var all = repository.GetAll().Where(r => r.CurrencyCode == "EUR" && r.QuoteCurrencyCode == "DZD").ToList();
        var oldRate = all.Single(r => r.SourceName == "Ancien taux");
        var newRate = all.Single(r => r.SourceName == "Nouveau taux");

        Assert.Equal(new DateOnly(2026, 1, 31), oldRate.ValidTo); // Clôturé à la veille de la nouvelle date d'effet.
        Assert.Null(newRate.ValidTo); // Le nouveau taux reste ouvert.

        // Historisation : un calcul effectué le 15 janvier doit continuer à utiliser l'ancien taux (146.50),
        // jamais être rétroactivement modifié par la publication du nouveau taux (148.00).
        var calc = new CurrencyCalculator(new InMemoryExchangeRateProvider(all));
        var (historicalRate, _, _) = calc.ResolveRate("EUR", new DateOnly(2026, 1, 15), null);
        Assert.Equal(146.50m, historicalRate);
    }

    // ------------------------------------------------------------------
    // Audit 2026-10-01 (2e round) — Sections 3, 5, 6, 11, 2 : scénarios chiffrés EXACTS exigés par la revue.
    // ------------------------------------------------------------------

    [Fact]
    public void EurToUsd_ExactScenario_10000Eur_At_1_17_ShouldEqual_11700Usd()
    {
        // Exigence explicite de la revue : Facture 10 000 EUR, taux EUR/USD officiel 1,17 -> Montant USD = 11 700.
        var rates = new[]
        {
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR", QuoteCurrencyCode = "USD", RateToDzd = 1.17m, QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1), RateType = "COMMERCIAL_AUTORISATION", SourceName = "Test USD"
            }
        };
        var service = new CurrencyConversionService(new InMemoryExchangeRateProvider(rates));

        var outcome = service.Convert(10000m, "EUR", "USD", new DateOnly(2026, 1, 15), null);

        Assert.Equal(10000m, outcome.OriginalAmount); // Le montant d'origine (10 000 EUR) reste toujours récupérable.
        Assert.Equal("EUR", outcome.FromCurrencyCode);
        Assert.Equal(1.17m, outcome.EffectiveRate);
        Assert.Equal(11700m, outcome.ConvertedAmount); // 10 000 x 1,17 = 11 700 USD.
        Assert.False(outcome.IsManualRate);
    }

    [Fact]
    public void FullCalculation_ProductLevel_Qty100_PU50Eur_Rate1_17_ShouldProduce_5000Eur_And_5850Usd()
    {
        // Exigence explicite de la revue : Qté = 100, PU = 50 EUR, taux = 1,17
        // -> Total EUR = 5 000, PU USD = 58,50, Total USD = 5 850.
        var company = BuildCompany();
        var rates = new[]
        {
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR", QuoteCurrencyCode = "DZD", RateToDzd = 146.50m, QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1), RateType = "OFFICIEL_DOUANE_ALCES", SourceName = "Test DZD"
            },
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR", QuoteCurrencyCode = "USD", RateToDzd = 1.17m, QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1), RateType = "COMMERCIAL_AUTORISATION", SourceName = "Test USD"
            }
        };
        var rules = new[]
        {
            BuildCustomsDutyRule("8482.10.00.00", 15.0m),
            BuildVatRule("8482.10.00.00", 19.0m)
        };

        var operation = new ImportOperation
        {
            CompanyId = company.Id,
            ImportNumber = "IMP-EXACT-01",
            ReferenceDate = new DateOnly(2026, 1, 15),
            SupplierName = "Fournisseur Hambourg",
            ExportShippingCountryIso2 = "DE",
            DefaultOriginCountryIso2 = "DE",
            MainCurrencyCode = "EUR",
            AuthorizationCurrencyCode = "USD",
            Incoterm = IncotermCode.FOB,
            ArrivalPortOrBorder = "Port d'Alger",
            TransportMode = "Maritime",
            Lines = new List<ImportLine>
            {
                new()
                {
                    LineNumber = 1,
                    ProductReference = "ROUL-EXACT",
                    Designation = "Roulement à billes",
                    Quantity = 100m,
                    MeasurementUnit = "PCE",
                    UnitPurchasePrice = 50m,
                    CurrencyCode = "EUR",
                    HsCodeConfirmed10 = "8482.10.00.00",
                    OriginCountryIso2 = "DE"
                }
            },
            Fees = new List<ImportFee>()
        };

        var orchestrator = BuildOrchestrator(rates, rules);
        var summary = orchestrator.ExecuteCalculation(company, operation);

        var line = summary.LineResults.Single();
        Assert.Equal(5000m, line.EconomicOutcome.PurchaseValueCurrency); // 100 x 50 EUR = 5 000 EUR (jamais remplacé).
        Assert.NotNull(line.AuthorizationConversion);
        Assert.Equal(58.50m, line.AuthorizationConversion!.AuthorizationUnitPrice); // 58,50 USD / unité.
        Assert.Equal(5850m, line.AuthorizationConversion.AuthorizationTotalAmount); // 5 000 x 1,17 = 5 850 USD.

        Assert.NotNull(summary.CommercialAuthorizationConversion);
        Assert.Equal(5000m, summary.CommercialAuthorizationConversion!.OriginalTotalAmount);
        Assert.Equal("EUR", summary.CommercialAuthorizationConversion.OriginalCurrencyCode);
        Assert.Equal(5850m, summary.CommercialAuthorizationConversion.AuthorizationTotalAmount);
        Assert.Equal("USD", summary.CommercialAuthorizationConversion.AuthorizationCurrencyCode);
    }

    [Fact]
    public void ManualAuthorizationRate_10000Eur_OfficialVsManual_ShouldApply1_20_NotOfficial1_17()
    {
        // Exigence explicite de la revue : taux officiel 1,17, taux manuel 1,20 saisi par l'utilisateur
        // -> 10 000 EUR x 1,20 = 12 000 USD (le taux manuel doit être appliqué, jamais ignoré), et le taux
        // officiel ne doit jamais être silencieusement écrasé dans l'historique (il reste 1,17 à part).
        var rates = new[]
        {
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR", QuoteCurrencyCode = "USD", RateToDzd = 1.17m, QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1), RateType = "COMMERCIAL_AUTORISATION", SourceName = "Officiel"
            }
        };
        var service = new CurrencyConversionService(new InMemoryExchangeRateProvider(rates));

        var manualOutcome = service.Convert(10000m, "EUR", "USD", new DateOnly(2026, 1, 15), 1.20m);
        Assert.True(manualOutcome.IsManualRate);
        Assert.Contains("TAUX MANUEL", manualOutcome.Anomaly!.MessageFr);
        Assert.Equal(1.20m, manualOutcome.EffectiveRate);
        Assert.Equal(12000m, manualOutcome.ConvertedAmount); // 10 000 x 1,20 = 12 000 USD.

        // Le taux officiel (1,17) reste intact et interrogeable séparément : il n'a jamais été remplacé.
        var officialOutcome = service.Convert(10000m, "EUR", "USD", new DateOnly(2026, 1, 15), null);
        Assert.False(officialOutcome.IsManualRate);
        Assert.Equal(1.17m, officialOutcome.EffectiveRate);
        Assert.Equal(11700m, officialOutcome.ConvertedAmount);
    }

    [Fact]
    public void RateHistorization_EurToUsd_ImportDated01Oct_ShouldKeep1_17_EvenAfter1_19PublishedFor15Oct()
    {
        // Exigence explicite de la revue : 01/10/2026 -> 1,17 ; 15/10/2026 -> 1,19. Une importation datée du
        // 01/10/2026 doit continuer à utiliser 1,17 même après publication du taux du 15/10 (1,19) — jamais
        // de recalcul rétroactif d'une ancienne importation avec un taux nouvellement publié.
        using var factory = new SingleFileDbContextFactory();
        using (var ctx = factory.CreateGlobal())
        {
            ctx.Database.EnsureCreated();
        }

        var repository = new ExchangeRateAdminRepository(factory);
        var adminUserId = Guid.NewGuid();

        repository.PublishNewRate(new ExchangeRateRecord
        {
            CurrencyCode = "EUR",
            QuoteCurrencyCode = "USD",
            RateToDzd = 1.17m,
            QuotityUnit = 1,
            ValidFrom = new DateOnly(2026, 10, 1),
            RateType = "COMMERCIAL_AUTORISATION",
            SourceName = "Taux du 01/10"
        }, adminUserId, UserRole.Administrateur);

        repository.PublishNewRate(new ExchangeRateRecord
        {
            CurrencyCode = "EUR",
            QuoteCurrencyCode = "USD",
            RateToDzd = 1.19m,
            QuotityUnit = 1,
            ValidFrom = new DateOnly(2026, 10, 15),
            RateType = "COMMERCIAL_AUTORISATION",
            SourceName = "Taux du 15/10"
        }, adminUserId, UserRole.Administrateur);

        var all = repository.GetAll().Where(r => r.CurrencyCode == "EUR" && r.QuoteCurrencyCode == "USD").ToList();
        var oldRate = all.Single(r => r.SourceName == "Taux du 01/10");
        Assert.Equal(new DateOnly(2026, 10, 14), oldRate.ValidTo); // Clôturé la veille du nouveau taux.

        var conversionService = new CurrencyConversionService(new InMemoryExchangeRateProvider(all));

        // Une importation datée du 01/10/2026 (avant la publication du 15/10) doit toujours résoudre 1,17.
        var outcomeFor01Oct = conversionService.Convert(10000m, "EUR", "USD", new DateOnly(2026, 10, 1), null);
        Assert.Equal(1.17m, outcomeFor01Oct.EffectiveRate);
        Assert.Equal(11700m, outcomeFor01Oct.ConvertedAmount);

        // Une importation datée du 15/10/2026 ou après doit utiliser le nouveau taux 1,19 — sans jamais
        // modifier rétroactivement le résultat déjà obtenu pour le 01/10.
        var outcomeFor15Oct = conversionService.Convert(10000m, "EUR", "USD", new DateOnly(2026, 10, 15), null);
        Assert.Equal(1.19m, outcomeFor15Oct.EffectiveRate);
        Assert.Equal(11900m, outcomeFor15Oct.ConvertedAmount);

        // Re-vérification explicite de non-régression : le 01/10 reste figé à 1,17 après la publication du 15/10.
        var outcomeFor01OctAgain = conversionService.Convert(10000m, "EUR", "USD", new DateOnly(2026, 10, 1), null);
        Assert.Equal(1.17m, outcomeFor01OctAgain.EffectiveRate);
    }

    [Fact]
    public void FullCalculation_DirectDzdInvoice_ShouldNotProduceUselessUsdConversion()
    {
        // Exigence explicite de la revue (Section 11) : une facture directement en DZD ne doit jamais subir
        // une conversion USD inutile.
        var company = BuildCompany();
        var rules = new[]
        {
            BuildCustomsDutyRule("8482.10.00.00", 15.0m),
            BuildVatRule("8482.10.00.00", 19.0m)
        };

        var operation = new ImportOperation
        {
            CompanyId = company.Id,
            ImportNumber = "IMP-DZD-01",
            ReferenceDate = new DateOnly(2026, 1, 15),
            SupplierName = "Fournisseur local",
            ExportShippingCountryIso2 = "DZ",
            DefaultOriginCountryIso2 = "DZ",
            MainCurrencyCode = "DZD",
            AuthorizationCurrencyCode = "DZD",
            Incoterm = IncotermCode.EXW,
            ArrivalPortOrBorder = "Port d'Alger",
            TransportMode = "Routier",
            Lines = new List<ImportLine>
            {
                new()
                {
                    LineNumber = 1,
                    ProductReference = "ROUL-DZD",
                    Designation = "Roulement à billes",
                    Quantity = 10m,
                    MeasurementUnit = "PCE",
                    UnitPurchasePrice = 1000m,
                    CurrencyCode = "DZD",
                    HsCodeConfirmed10 = "8482.10.00.00",
                    OriginCountryIso2 = "DZ"
                }
            },
            Fees = new List<ImportFee>()
        };

        var orchestrator = BuildOrchestrator(Array.Empty<ExchangeRateRecord>(), rules);
        var summary = orchestrator.ExecuteCalculation(company, operation);

        Assert.Null(summary.CommercialAuthorizationConversion);
        Assert.Null(summary.LineResults.Single().AuthorizationConversion);
        Assert.Equal(10000m, summary.TotalPurchaseValueDzd); // 10 x 1 000 DZD, sans aucun détour par l'EUR ou l'USD.
    }
}
