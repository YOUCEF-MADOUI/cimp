using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Core.Services;
using ImportCostAlgeria.RegulatoryEngine;
using ImportCostAlgeria.CalculationEngine;
using ImportCostAlgeria.ExcelEngine;
using ImportCostAlgeria.Reporting;
using ImportCostAlgeria.AI;

namespace ImportCostAlgeria.UnitTests;

public sealed class InMemoryExchangeRateProvider : IExchangeRateProvider
{
    private readonly List<ExchangeRateRecord> _rates;
    public InMemoryExchangeRateProvider(IEnumerable<ExchangeRateRecord> rates) => _rates = rates.ToList();

    public ExchangeRateRecord? GetRegulatoryRate(string currencyCode, DateOnly referenceDate) =>
        _rates.FirstOrDefault(r =>
            string.Equals(r.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase) &&
            r.ValidFrom <= referenceDate &&
            (!r.ValidTo.HasValue || r.ValidTo.Value >= referenceDate));
}

public sealed class InMemoryRegulatoryRuleRepository : IRegulatoryRuleRepository
{
    private readonly List<RegulatoryRule> _rules;
    public InMemoryRegulatoryRuleRepository(IEnumerable<RegulatoryRule> rules) => _rules = rules.ToList();

    public IReadOnlyList<RegulatoryRule> GetCandidateRules(string hsCode10) =>
        _rules.Where(r => RegulatoryRuleEngine.NormalizeHsCode(r.HsCode10) == hsCode10).ToList();
}

/// <summary>
/// Suite complète de tests unitaires et d'intégration couvrant les 14 exigences de la Section 42.
/// </summary>
public sealed class V1CompleteTestSuite
{
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly LegalSource OfficialJoraSource = new()
    {
        HierarchyLevel = LegalSourceHierarchyLevel.Level1_JournalOfficielJora,
        OfficialTitle = "Loi de Finances / Tarif Douanier Officiel Algérie",
        JoraReference = "JORA N° 88",
        ArticleReference = "Art. 16 ter & 16 octies CDA / Art. 19 CTCA",
        PublicationDate = new DateOnly(2025, 12, 30),
        EffectiveDate = new DateOnly(2026, 1, 1)
    };

    private static ImportCalculationOrchestrator BuildOrchestrator(
        IEnumerable<ExchangeRateRecord> rates,
        IEnumerable<RegulatoryRule> rules)
    {
        return new ImportCalculationOrchestrator(
            new CurrencyCalculator(new InMemoryExchangeRateProvider(rates)),
            new CostAllocationEngine(),
            new CustomsValueCalculator(),
            new RegulatoryRuleEngine(new InMemoryRegulatoryRuleRepository(rules)));
    }

    // 1. Test Conversion Devises & Détection Taux Manuel (Sections 13, 14, 42)
    [Fact]
    public void CurrencyConversion_ShouldUseOfficialAlcesRate_AndWarnOnManualRateOverride()
    {
        var rates = new[]
        {
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR",
                RateToDzd = 146.50m,
                ValidFrom = new DateOnly(2026, 9, 1),
                ValidTo = new DateOnly(2026, 9, 30),
                RateType = "OFFICIEL_DOUANE_ALCES",
                SourceName = "Portail officiel ALCES"
            }
        };
        var calc = new CurrencyCalculator(new InMemoryExchangeRateProvider(rates));

        var (officialRate, _, noAnomaly) = calc.ResolveRate("EUR", new DateOnly(2026, 9, 15), null);
        Assert.Equal(146.50m, officialRate);
        Assert.Null(noAnomaly);

        var (manualRate, _, warningAnomaly) = calc.ResolveRate("EUR", new DateOnly(2026, 9, 15), 150.00m);
        Assert.Equal(150.00m, manualRate);
        Assert.NotNull(warningAnomaly);
        Assert.Equal("MANUAL_EXCHANGE_RATE_DIFF", warningAnomaly!.AnomalyCode);
        Assert.Contains("TAUX MANUEL", warningAnomaly.MessageFr);
    }

    // 2, 3, 4. Tests Incoterms EXW, FOB, CFR et Contrôles d'anomalies (Sections 7, 8, 28, 42)
    [Fact]
    public void Incoterms_EXW_FOB_CFR_ShouldEnforceDynamicRequiredFields()
    {
        var validator = new CustomsValueCalculator();
        var anomalies = new List<CalculationAnomaly>();

        var exwWithoutExportFees = new ImportOperation
        {
            CompanyId = Guid.NewGuid(),
            ImportNumber = "IMP-EXW-01",
            ReferenceDate = new DateOnly(2026, 9, 15),
            SupplierName = "Fournisseur Milan",
            ExportShippingCountryIso2 = "IT",
            MainCurrencyCode = "EUR",
            Incoterm = IncotermCode.EXW,
            ArrivalPortOrBorder = "Port d'Alger",
            TransportMode = "Maritime"
        };

        validator.ValidateIncotermRequiredFees(exwWithoutExportFees, anomalies);
        Assert.Contains(anomalies, a => a.AnomalyCode == "EXW_MISSING_REQUIRED_FEES");

        var cfrWithoutInsurance = new ImportOperation
        {
            CompanyId = Guid.NewGuid(),
            ImportNumber = "IMP-CFR-01",
            ReferenceDate = new DateOnly(2026, 9, 15),
            SupplierName = "Fournisseur Marseille",
            ExportShippingCountryIso2 = "FR",
            MainCurrencyCode = "EUR",
            Incoterm = IncotermCode.CFR,
            ArrivalPortOrBorder = "Port d'Oran",
            TransportMode = "Maritime"
        };

        anomalies.Clear();
        validator.ValidateIncotermRequiredFees(cfrWithoutInsurance, anomalies);
        Assert.Contains(anomalies, a => a.AnomalyCode == "CFR_MISSING_INSURANCE");
    }

    // 5, 6, 7, 9, 10, 13. Test Complet Valeur Transactionnelle, DD, DAPS, TVA, Répartition, Arrondis & Différence Excel/Réglementaire
    [Fact]
    public void FullImportCalculation_ShouldMatchVerifiedSectionHExampleToTheCentime()
    {
        var company = new Company
        {
            Code = "EQUIP-DZ",
            LegalName = "SARL EQUIP-INDUS ALGÉRIE",
            IsImportVatNonRecoverable = true
        };

        var rates = new[]
        {
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR",
                RateToDzd = 146.50m,
                ValidFrom = new DateOnly(2026, 9, 1),
                ValidTo = new DateOnly(2026, 9, 30),
                RateType = "OFFICIEL_DOUANE_ALCES",
                SourceName = "Portail officiel ALCES - DGD"
            }
        };

        var rules = new[]
        {
            new RegulatoryRule
            {
                Code = "DD-8708999000-2026",
                RegulatoryVersionCode = "2026.01",
                RuleType = RegulatoryRuleType.CustomsDuty,
                TaxCode = "DD",
                TaxNameFr = "Droit de Douane",
                HsCode10 = "8708.99.90.00",
                RatePercent = 15.0m,
                CalculationBase = TaxableBaseType.CustomsValueDzd,
                ValidFrom = new DateOnly(2026, 1, 1),
                LegalSource = OfficialJoraSource,
                Status = RegulatoryRuleStatus.PublishedNewVersion,
                ValidatedByAdminUserId = AdminId
            },
            new RegulatoryRule
            {
                Code = "DAPS-8708999000-2026",
                RegulatoryVersionCode = "2026.01",
                RuleType = RegulatoryRuleType.Daps,
                TaxCode = "DAPS",
                TaxNameFr = "Droit Additionnel Provisoire de Sauvegarde",
                HsCode10 = "8708.99.90.00",
                RatePercent = 30.0m,
                CalculationBase = TaxableBaseType.CustomsValueDzd,
                ValidFrom = new DateOnly(2026, 1, 1),
                LegalSource = OfficialJoraSource,
                Status = RegulatoryRuleStatus.PublishedNewVersion,
                ValidatedByAdminUserId = AdminId
            },
            new RegulatoryRule
            {
                Code = "TVA-8708999000-2026",
                RegulatoryVersionCode = "2026.01",
                RuleType = RegulatoryRuleType.Vat,
                TaxCode = "TVA",
                TaxNameFr = "Taxe sur la Valeur Ajoutée",
                HsCode10 = "8708.99.90.00",
                RatePercent = 19.0m,
                CalculationBase = TaxableBaseType.CustomsValuePlusDutiesAndTaxesExVatDzd,
                ValidFrom = new DateOnly(2026, 1, 1),
                LegalSource = OfficialJoraSource,
                Status = RegulatoryRuleStatus.PublishedNewVersion,
                ValidatedByAdminUserId = AdminId
            },
            new RegulatoryRule
            {
                Code = "DD-8421299000-2026",
                RegulatoryVersionCode = "2026.01",
                RuleType = RegulatoryRuleType.CustomsDuty,
                TaxCode = "DD",
                TaxNameFr = "Droit de Douane",
                HsCode10 = "8421.29.90.00",
                RatePercent = 5.0m,
                CalculationBase = TaxableBaseType.CustomsValueDzd,
                ValidFrom = new DateOnly(2026, 1, 1),
                LegalSource = OfficialJoraSource,
                Status = RegulatoryRuleStatus.PublishedNewVersion,
                ValidatedByAdminUserId = AdminId
            },
            new RegulatoryRule
            {
                Code = "TVA-8421299000-2026",
                RegulatoryVersionCode = "2026.01",
                RuleType = RegulatoryRuleType.Vat,
                TaxCode = "TVA",
                TaxNameFr = "Taxe sur la Valeur Ajoutée",
                HsCode10 = "8421.29.90.00",
                RatePercent = 19.0m,
                CalculationBase = TaxableBaseType.CustomsValuePlusDutiesAndTaxesExVatDzd,
                ValidFrom = new DateOnly(2026, 1, 1),
                LegalSource = OfficialJoraSource,
                Status = RegulatoryRuleStatus.PublishedNewVersion,
                ValidatedByAdminUserId = AdminId
            }
        };

        var operation = new ImportOperation
        {
            CompanyId = company.Id,
            ImportNumber = "IMP-2026-0042",
            ReferenceDate = new DateOnly(2026, 9, 15),
            SupplierName = "SHANGHAI INDUSTRIAL PARTS CO.",
            ExportShippingCountryIso2 = "CN",
            MainCurrencyCode = "EUR",
            Incoterm = IncotermCode.FOB,
            ArrivalPortOrBorder = "Port d'Alger",
            TransportMode = "Maritime",
            Lines =
            {
                new ImportLine
                {
                    LineNumber = 1,
                    ProductReference = "PROD-A",
                    Designation = "Support moteur antivibratoire (Engine Mounting)",
                    Quantity = 500m,
                    UnitPurchasePrice = 10.00m,
                    CurrencyCode = "EUR",
                    HsCodeConfirmed10 = "8708.99.90.00",
                    OriginCountryIso2 = "CN",
                    ExcelDutyRatePercent = 10.0m, // Différent du taux réglementaire (15 %)
                    LineGrossWeightKg = 1000m
                },
                new ImportLine
                {
                    LineNumber = 2,
                    ProductReference = "PROD-B",
                    Designation = "Filtre hydraulique haute pression industriel",
                    Quantity = 200m,
                    UnitPurchasePrice = 25.00m,
                    CurrencyCode = "EUR",
                    HsCodeConfirmed10 = "8421.29.90.00",
                    OriginCountryIso2 = "CN",
                    ExcelDutyRatePercent = 5.0m,  // Correspondance avec le taux réglementaire (5 %)
                    LineGrossWeightKg = 600m
                }
            },
            Fees =
            {
                new ImportFee
                {
                    FeeCategoryCode = "FRET_INTERNATIONAL",
                    FeeName = "Fret maritime international",
                    Amount = 1200.00m,
                    CurrencyCode = "EUR",
                    AllocationMethod = FeeAllocationMethod.ByWeight,
                    IncludeInCustomsValue = true,
                    CustomsTreatment = CustomsAdjustmentTreatment.Addition_Art16Octies,
                    IncludeInCostOfGoods = true
                },
                new ImportFee
                {
                    FeeCategoryCode = "ASSURANCE",
                    FeeName = "Assurance transport international",
                    Amount = 150.00m,
                    CurrencyCode = "EUR",
                    AllocationMethod = FeeAllocationMethod.ByValue,
                    IncludeInCustomsValue = true,
                    CustomsTreatment = CustomsAdjustmentTreatment.Addition_Art16Octies,
                    IncludeInCostOfGoods = true
                },
                new ImportFee
                {
                    FeeCategoryCode = "FRAIS_PORTUAIRES",
                    FeeName = "Frais portuaires, THC & Dépotage Port d'Alger",
                    Amount = 85000.00m,
                    CurrencyCode = "DZD",
                    AllocationMethod = FeeAllocationMethod.ByQuantity,
                    IncludeInCustomsValue = false,
                    CustomsTreatment = CustomsAdjustmentTreatment.PostIntroductionExcluded,
                    IncludeInCostOfGoods = true
                },
                new ImportFee
                {
                    FeeCategoryCode = "COMMISSIONNAIRE_DOUANE",
                    FeeName = "Honoraires Commissionnaire en douane & Transit",
                    Amount = 45000.00m,
                    CurrencyCode = "DZD",
                    AllocationMethod = FeeAllocationMethod.ByValue,
                    IncludeInCustomsValue = false,
                    CustomsTreatment = CustomsAdjustmentTreatment.PostIntroductionExcluded,
                    IncludeInCostOfGoods = true
                },
                new ImportFee
                {
                    FeeCategoryCode = "TRANSPORT_PORT_ENTREPOT",
                    FeeName = "Transport Port d'Alger -> Entrepôt Rouiba",
                    Amount = 32000.00m,
                    CurrencyCode = "DZD",
                    AllocationMethod = FeeAllocationMethod.ByWeight,
                    IncludeInCustomsValue = false,
                    CustomsTreatment = CustomsAdjustmentTreatment.PostIntroductionExcluded,
                    IncludeInCostOfGoods = true
                },
                new ImportFee
                {
                    FeeCategoryCode = "FRAIS_BANCAIRES",
                    FeeName = "Frais bancaires & Domiciliation",
                    Amount = 18000.00m,
                    CurrencyCode = "DZD",
                    AllocationMethod = FeeAllocationMethod.ByValue,
                    IncludeInCustomsValue = false,
                    CustomsTreatment = CustomsAdjustmentTreatment.PostIntroductionExcluded,
                    IncludeInCostOfGoods = true
                }
            }
        };

        var orchestrator = BuildOrchestrator(rates, rules);
        var summary = orchestrator.ExecuteCalculation(company, operation);

        Assert.Equal(10000.00m, summary.TotalPurchaseValueMainCurrency);
        Assert.Equal(1465000.00m, summary.TotalPurchaseValueDzd);
        Assert.Equal(1662775.00m, summary.TotalCustomsValueDzd);
        Assert.Equal(168475.01m, summary.TotalCustomsDutyDzd);
        Assert.Equal(256008.75m, summary.TotalAdditionalTaxesDzd);
        Assert.Equal(396579.16m, summary.TotalImportVatDzd);
        Assert.Equal(821062.92m, summary.TotalDutiesAndTaxesDzd);
        Assert.Equal(377775.00m, summary.TotalImportFeesDzd);
        Assert.Equal(2267258.76m, summary.TotalAcquisitionCostExVatDzd);
        Assert.Equal(2663837.92m, summary.TotalRealCostOfGoodsDzd);

        var lineA = summary.LineResults[0];
        var lineB = summary.LineResults[1];

        Assert.Equal(DutyComparisonStatus.Difference, lineA.CustomsOutcome.ExcelVsRegulatoryComparison);
        Assert.Equal(DutyComparisonStatus.Match, lineB.CustomsOutcome.ExcelVsRegulatoryComparison);
        Assert.Equal(3169.38m, lineA.EconomicOutcome.UnitCostOfGoodsDzd);
        Assert.Equal(5395.73m, lineB.EconomicOutcome.UnitCostOfGoodsDzd);
    }

    // 11 & 12. Test Versionnage Temporel & Changement de Réglementation (2025 -> 10%, 2026 -> 15%, 2027 -> 12%)
    [Fact]
    public void RegulatoryVersioning_ShouldResolveExactHistoricalRateForOperationDate()
    {
        var rules = new[]
        {
            new RegulatoryRule
            {
                Code = "DD-SH-2025",
                RegulatoryVersionCode = "2025.01",
                RuleType = RegulatoryRuleType.CustomsDuty,
                TaxCode = "DD",
                TaxNameFr = "Droit de Douane",
                HsCode10 = "8708.99.90.00",
                RatePercent = 10.0m,
                CalculationBase = TaxableBaseType.CustomsValueDzd,
                ValidFrom = new DateOnly(2025, 1, 1),
                ValidTo = new DateOnly(2025, 12, 31),
                LegalSource = OfficialJoraSource,
                Status = RegulatoryRuleStatus.PublishedNewVersion,
                ValidatedByAdminUserId = AdminId
            },
            new RegulatoryRule
            {
                Code = "DD-SH-2026",
                RegulatoryVersionCode = "2026.01",
                RuleType = RegulatoryRuleType.CustomsDuty,
                TaxCode = "DD",
                TaxNameFr = "Droit de Douane",
                HsCode10 = "8708.99.90.00",
                RatePercent = 15.0m,
                CalculationBase = TaxableBaseType.CustomsValueDzd,
                ValidFrom = new DateOnly(2026, 1, 1),
                ValidTo = new DateOnly(2026, 12, 31),
                LegalSource = OfficialJoraSource,
                Status = RegulatoryRuleStatus.PublishedNewVersion,
                ValidatedByAdminUserId = AdminId
            },
            new RegulatoryRule
            {
                Code = "DD-SH-2027",
                RegulatoryVersionCode = "2027.01",
                RuleType = RegulatoryRuleType.CustomsDuty,
                TaxCode = "DD",
                TaxNameFr = "Droit de Douane",
                HsCode10 = "8708.99.90.00",
                RatePercent = 12.0m,
                CalculationBase = TaxableBaseType.CustomsValueDzd,
                ValidFrom = new DateOnly(2027, 1, 1),
                ValidTo = null,
                LegalSource = OfficialJoraSource,
                Status = RegulatoryRuleStatus.PublishedNewVersion,
                ValidatedByAdminUserId = AdminId
            }
        };

        var engine = new RegulatoryRuleEngine(new InMemoryRegulatoryRuleRepository(rules));

        var res2025 = engine.ResolveApplicableRules(new RegulatoryLookupQuery("8708.99.90.00", "CN", new DateOnly(2025, 6, 15), "DROIT_COMMUN_4000"));
        var res2026 = engine.ResolveApplicableRules(new RegulatoryLookupQuery("8708.99.90.00", "CN", new DateOnly(2026, 6, 15), "DROIT_COMMUN_4000"));
        var res2027 = engine.ResolveApplicableRules(new RegulatoryLookupQuery("8708.99.90.00", "CN", new DateOnly(2027, 6, 15), "DROIT_COMMUN_4000"));

        Assert.Equal(10.0m, res2025.CustomsDutyRule!.RatePercent);
        Assert.Equal(15.0m, res2026.CustomsDutyRule!.RatePercent);
        Assert.Equal(12.0m, res2027.CustomsDutyRule!.RatePercent);
    }

    // 14. Test d'intégration Import Excel, Détection "Prix Fournisseur", Modèle FOURNISSEUR_X
    [Fact]
    public void ExcelImport_ShouldRequestMappingForUnrecognizedColumn_ThenAutoRecognizeSavedTemplate()
    {
        Guid companyId = Guid.NewGuid();
        var importer = new ExcelImporterService(new ExcelColumnDetectorAndMapper(), new ProductCatalogService());

        var rawSheet = new RawExcelSheetData(
            FileName: "Facture_Fournisseur_X.xlsx",
            WorksheetName: "Feuil1",
            Headers: new[]
            {
                ("B", 1, "REF"),
                ("C", 2, "DESIGNATION PRODUIT"),
                ("D", 3, "QTE"),
                ("E", 4, "Prix Fournisseur"), // Colonne non reconnue par défaut !
                ("F", 5, "CODE SH")
            },
            DataRowsByColumnLetter: new IReadOnlyDictionary<string, string>[]
            {
                new Dictionary<string, string>
                {
                    ["B"] = "PROD-A",
                    ["C"] = "Engine Mounting China",
                    ["D"] = "500",
                    ["E"] = "10,00",
                    ["F"] = "8708.99.90.00"
                }
            });

        // 1er appel : "Prix Fournisseur" n'est pas reconnu -> demande de mapping interactif
        var firstAttempt = importer.ProcessExcelSheet(companyId, rawSheet, "EUR", "CN");
        Assert.True(firstAttempt.RequiresInteractiveUserMapping);
        Assert.Single(firstAttempt.HeaderAnalysis.UnrecognizedColumnsRequiringUserMapping);
        Assert.Equal("Prix Fournisseur", firstAttempt.HeaderAnalysis.UnrecognizedColumnsRequiringUserMapping[0].RawHeaderText);

        // L'utilisateur mappe "E" -> UnitPurchasePrice et enregistre le modèle "FOURNISSEUR_X"
        importer.SaveUserMappingAsTemplate(
            companyId,
            "FOURNISSEUR_X",
            rawSheet.Headers.Select(h => h.RawHeader).ToList(),
            new Dictionary<string, CanonicalExcelField>
            {
                ["B"] = CanonicalExcelField.ProductReference,
                ["C"] = CanonicalExcelField.Designation,
                ["D"] = CanonicalExcelField.Quantity,
                ["E"] = CanonicalExcelField.UnitPurchasePrice,
                ["F"] = CanonicalExcelField.HsCode
            });

        // 2e importation du même modèle : reconnaissance 100 % automatique sans question
        var secondAttempt = importer.ProcessExcelSheet(companyId, rawSheet, "EUR", "CN");
        Assert.False(secondAttempt.RequiresInteractiveUserMapping);
        Assert.Equal("FOURNISSEUR_X", secondAttempt.HeaderAnalysis.MatchedSavedTemplateName);
        Assert.Single(secondAttempt.ConvertedLines);
        Assert.Equal(10.00m, secondAttempt.ConvertedLines[0].UnitPurchasePrice);
    }
}
