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
        GetRate(currencyCode, "DZD", referenceDate);

    public ExchangeRateRecord? GetRate(string fromCurrencyCode, string toCurrencyCode, DateOnly referenceDate) =>
        _rates.FirstOrDefault(r =>
            string.Equals(r.CurrencyCode, fromCurrencyCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.QuoteCurrencyCode, toCurrencyCode, StringComparison.OrdinalIgnoreCase) &&
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
        var rateProvider = new InMemoryExchangeRateProvider(rates);
        return new ImportCalculationOrchestrator(
            new CurrencyCalculator(rateProvider),
            new CostAllocationEngine(),
            new CustomsValueCalculator(),
            new RegulatoryRuleEngine(new InMemoryRegulatoryRuleRepository(rules)),
            new CurrencyConversionService(rateProvider));
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

        // Revue du 2026-10-02 (demande utilisateur, Section 6 — "CFR ne doit plus demander d'assurance
        // manquante") : CORRECTION du comportement attendu par ce test. Selon les Incoterms 2020, le
        // vendeur n'a PAS l'obligation de souscrire une assurance pour l'acheteur en CFR (contrairement à
        // CIF) — l'ancienne assertion "CFR_MISSING_INSURANCE doit être levée" reposait sur une mauvaise
        // interprétation de la règle et a été explicitement signalée comme un bug par l'utilisateur. Ce
        // test vérifie maintenant l'inverse : l'absence d'assurance en CFR ne doit JAMAIS lever d'anomalie
        // (ni CFR_MISSING_INSURANCE, ni aucune autre anomalie liée à l'assurance).
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
        Assert.DoesNotContain(anomalies, a => a.AnomalyCode == "CFR_MISSING_INSURANCE");
        Assert.DoesNotContain(anomalies, a => a.AnomalyCode.Contains("INSURANCE", StringComparison.OrdinalIgnoreCase));
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
                    ExcelDutyRatePercent = 10.0m, // Différent de la proposition DD IA / réglementaire (15 %)
                    // Revue du 2026-10-05 ("DD Excel prioritaire par défaut") : le DD Excel (10 %) est
                    // désormais prioritaire par défaut sur la proposition DD IA (15 %) — pour préserver EXACTEMENT
                    // ce cas de test historique (golden master vérifié "à la centime" avec le DD IA/réglementaire
                    // de 15 % effectivement appliqué), "Forcer DD IA" est explicitement activé ici.
                    ForceAiDutyRate = true,
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
                    ExcelDutyRatePercent = 5.0m,  // Correspondance avec la proposition DD IA / réglementaire (5 %)
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

        // Revue du 2026-10-05 ("DD Excel prioritaire par défaut") : PROD-A a explicitement "Forcer DD IA"
        // activé ci-dessus (voir commentaire sur ExcelDutyRatePercent) afin que le taux de 15 % (proposition
        // DD IA / réglementaire) reste celui effectivement appliqué, préservant EXACTEMENT tous les montants
        // "à la centime" de ce golden master — d'où le nouveau statut AiForcedByUserOverridingExcel (et non
        // plus l'ancien "Difference", qui n'est plus jamais produit par le moteur de calcul). PROD-B n'a pas
        // "Forcer DD IA" : son DD Excel (5 %) est utilisé par défaut et correspond à la proposition DD IA
        // (5 %) — d'où ExcelPriorityMatchesAi (résultat numérique strictement identique à l'ancien "Match").
        Assert.Equal(DutyComparisonStatus.AiForcedByUserOverridingExcel, lineA.CustomsOutcome.ExcelVsRegulatoryComparison);
        Assert.Equal(DutyComparisonStatus.ExcelPriorityMatchesAi, lineB.CustomsOutcome.ExcelVsRegulatoryComparison);
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

    // Revue du 2026-10-01 (point 5 — Droits et taxes par code SH, test matrice C) :
    // un code SH disposant de règles officielles DD + PRCT + TCS + TVA doit exposer les quatre,
    // chacune avec son propre taux/base/montant — jamais un taux PRCT=2%/TCS=3% codé en dur.
    [Fact]
    public void RegulatoryEngine_ShouldExposeDdPrctTcsVat_WhenAllFourAreOfficiallyPublished()
    {
        const string hsCode = "8708.99.90.00";
        var rules = new[]
        {
            new RegulatoryRule { Code = "DD-1", RegulatoryVersionCode = "2026.01", RuleType = RegulatoryRuleType.CustomsDuty, TaxCode = "DD", TaxNameFr = "Droit de Douane", HsCode10 = hsCode, RatePercent = 15.0m, CalculationBase = TaxableBaseType.CustomsValueDzd, ValidFrom = new DateOnly(2026, 1, 1), LegalSource = OfficialJoraSource, Status = RegulatoryRuleStatus.PublishedNewVersion, ValidatedByAdminUserId = AdminId },
            new RegulatoryRule { Code = "CS-1", RegulatoryVersionCode = "2026.01", RuleType = RegulatoryRuleType.SpecificTax, TaxCode = "CS", TaxNameFr = "Contribution de Solidarité", HsCode10 = hsCode, RatePercent = 3.0m, CalculationBase = TaxableBaseType.CustomsValueDzd, ValidFrom = new DateOnly(2026, 1, 1), LegalSource = OfficialJoraSource, Status = RegulatoryRuleStatus.PublishedNewVersion, ValidatedByAdminUserId = AdminId },
            new RegulatoryRule { Code = "PRCT-1", RegulatoryVersionCode = "2026.01", RuleType = RegulatoryRuleType.SpecificTax, TaxCode = "PRCT", TaxNameFr = "Précompte à l'importation", HsCode10 = hsCode, RatePercent = 2.0m, CalculationBase = TaxableBaseType.CustomsValueDzd, ValidFrom = new DateOnly(2026, 1, 1), LegalSource = OfficialJoraSource, Status = RegulatoryRuleStatus.PublishedNewVersion, ValidatedByAdminUserId = AdminId },
            new RegulatoryRule { Code = "TCS-1", RegulatoryVersionCode = "2026.01", RuleType = RegulatoryRuleType.SpecificTax, TaxCode = "TCS", TaxNameFr = "Taxe de Contribution de Solidarité", HsCode10 = hsCode, RatePercent = 2.0m, CalculationBase = TaxableBaseType.CustomsValueDzd, ValidFrom = new DateOnly(2026, 1, 1), LegalSource = OfficialJoraSource, Status = RegulatoryRuleStatus.PublishedNewVersion, ValidatedByAdminUserId = AdminId },
            new RegulatoryRule { Code = "TVA-1", RegulatoryVersionCode = "2026.01", RuleType = RegulatoryRuleType.Vat, TaxCode = "TVA", TaxNameFr = "Taxe sur la Valeur Ajoutée", HsCode10 = hsCode, RatePercent = 19.0m, CalculationBase = TaxableBaseType.CustomsValuePlusDutiesAndTaxesExVatDzd, ValidFrom = new DateOnly(2026, 1, 1), LegalSource = OfficialJoraSource, Status = RegulatoryRuleStatus.PublishedNewVersion, ValidatedByAdminUserId = AdminId }
        };

        var engine = new RegulatoryRuleEngine(new InMemoryRegulatoryRuleRepository(rules));
        var outcome = engine.ResolveApplicableRules(new RegulatoryLookupQuery(hsCode, "CN", new DateOnly(2026, 6, 15), "DROIT_COMMUN_4000"));

        Assert.True(outcome.IsDetermined);
        Assert.Equal(15.0m, outcome.CustomsDutyRule!.RatePercent);
        Assert.Equal(19.0m, outcome.VatRule!.RatePercent);
        // Revue du 2026-10-02 (cas D10) : CS a rejoint PRCT et TCS comme taxe additionnelle "standard".
        Assert.Equal(3, outcome.AdditionalTaxRules.Count);
        Assert.Contains(outcome.AdditionalTaxRules, r => r.TaxCode == "CS" && r.RatePercent == 3.0m);
        Assert.Contains(outcome.AdditionalTaxRules, r => r.TaxCode == "PRCT" && r.RatePercent == 2.0m);
        Assert.Contains(outcome.AdditionalTaxRules, r => r.TaxCode == "TCS" && r.RatePercent == 2.0m);

        var report = RegulatoryRuleEngine.BuildStandardTaxApplicabilityReport(outcome, RegulatoryRuleEngine.StandardAdditionalTaxCodes);
        Assert.Equal(4, report.Count); // CS, PRCT, TCS, DAPS
        Assert.Equal(TaxApplicabilityKind.Applicable, report.Single(r => r.TaxCode == "CS").Kind);
        Assert.Equal(TaxApplicabilityKind.Applicable, report.Single(r => r.TaxCode == "PRCT").Kind);
        Assert.Equal(TaxApplicabilityKind.Applicable, report.Single(r => r.TaxCode == "TCS").Kind);
        Assert.Equal(TaxApplicabilityKind.DonneeManquante, report.Single(r => r.TaxCode == "DAPS").Kind);
        Assert.Equal("Donnée réglementaire manquante — validation requise", report.Single(r => r.TaxCode == "DAPS").DisplayStatusFr);
    }

    // Revue du 2026-10-01 (point 5, test matrice C) : une taxe EXPLICITEMENT déclarée non applicable par
    // une règle officielle (RegulatoryRule.IsApplicable = false) n'est JAMAIS incluse dans le calcul —
    // mais elle doit être signalée "Non applicable", à distinguer d'une simple absence de donnée.
    [Fact]
    public void RegulatoryEngine_ShouldNeverCalculateAnExplicitlyNonApplicableTax()
    {
        const string hsCode = "1234.56.78.90";
        var rules = new[]
        {
            new RegulatoryRule { Code = "DD-X", RegulatoryVersionCode = "2026.01", RuleType = RegulatoryRuleType.CustomsDuty, TaxCode = "DD", TaxNameFr = "Droit de Douane", HsCode10 = hsCode, RatePercent = 5.0m, CalculationBase = TaxableBaseType.CustomsValueDzd, ValidFrom = new DateOnly(2026, 1, 1), LegalSource = OfficialJoraSource, Status = RegulatoryRuleStatus.PublishedNewVersion, ValidatedByAdminUserId = AdminId },
            new RegulatoryRule { Code = "TVA-X", RegulatoryVersionCode = "2026.01", RuleType = RegulatoryRuleType.Vat, TaxCode = "TVA", TaxNameFr = "Taxe sur la Valeur Ajoutée", HsCode10 = hsCode, RatePercent = 19.0m, CalculationBase = TaxableBaseType.CustomsValuePlusDutiesAndTaxesExVatDzd, ValidFrom = new DateOnly(2026, 1, 1), LegalSource = OfficialJoraSource, Status = RegulatoryRuleStatus.PublishedNewVersion, ValidatedByAdminUserId = AdminId },
            new RegulatoryRule { Code = "DAPS-X", RegulatoryVersionCode = "2026.01", RuleType = RegulatoryRuleType.Daps, TaxCode = "DAPS", TaxNameFr = "Droit Additionnel Provisoire de Sauvegarde", HsCode10 = hsCode, RatePercent = 0m, IsApplicable = false, CalculationBase = TaxableBaseType.CustomsValueDzd, ValidFrom = new DateOnly(2026, 1, 1), LegalSource = OfficialJoraSource, Status = RegulatoryRuleStatus.PublishedNewVersion, ValidatedByAdminUserId = AdminId }
        };

        var engine = new RegulatoryRuleEngine(new InMemoryRegulatoryRuleRepository(rules));
        var outcome = engine.ResolveApplicableRules(new RegulatoryLookupQuery(hsCode, "FR", new DateOnly(2026, 6, 15), "DROIT_COMMUN_4000"));

        // La DAPS ne doit JAMAIS apparaître dans AdditionalTaxRules (donc jamais calculée/sommée).
        Assert.DoesNotContain(outcome.AdditionalTaxRules, r => r.TaxCode == "DAPS");
        Assert.Contains(outcome.NonApplicableTaxRules, r => r.TaxCode == "DAPS");

        var report = RegulatoryRuleEngine.BuildStandardTaxApplicabilityReport(outcome, RegulatoryRuleEngine.StandardAdditionalTaxCodes);
        var dapsStatus = report.Single(r => r.TaxCode == "DAPS");
        Assert.Equal(TaxApplicabilityKind.NonApplicable, dapsStatus.Kind);
        Assert.Equal("Non applicable", dapsStatus.DisplayStatusFr);
        Assert.Null(dapsStatus.RatePercent); // Jamais de taux affiché pour une taxe non applicable.

        // PRCT et TCS, pour lesquels aucune règle n'a jamais été publiée pour ce code SH : donnée manquante,
        // jamais un taux inventé (ni 2 %, ni 3 %, ni aucune autre valeur).
        Assert.Equal(TaxApplicabilityKind.DonneeManquante, report.Single(r => r.TaxCode == "PRCT").Kind);
        Assert.Equal(TaxApplicabilityKind.DonneeManquante, report.Single(r => r.TaxCode == "TCS").Kind);
    }

    // Revue du 2026-10-01 (point 5, test matrice C) : en l'absence TOTALE de règle officielle pour un code
    // SH, le moteur ne doit jamais inventer de taux — DD et TVA doivent rester "INFORMATION NON
    // DÉTERMINÉE" et les 3 taxes standard doivent toutes être signalées "Donnée réglementaire manquante".
    [Fact]
    public void RegulatoryEngine_ShouldNeverInventARate_WhenNoRegulatoryDataExistsAtAllForTheHsCode()
    {
        var engine = new RegulatoryRuleEngine(new InMemoryRegulatoryRuleRepository(Array.Empty<RegulatoryRule>()));
        var outcome = engine.ResolveApplicableRules(new RegulatoryLookupQuery("0000.00.00.00", "FR", new DateOnly(2026, 6, 15), "DROIT_COMMUN_4000"));

        Assert.False(outcome.IsDetermined);
        Assert.Null(outcome.CustomsDutyRule);
        Assert.Null(outcome.VatRule);
        Assert.Empty(outcome.AdditionalTaxRules);
        Assert.Contains(outcome.WarningsOrMissingInfo, m => m.Contains("INFORMATION NON DÉTERMINÉE"));

        var report = RegulatoryRuleEngine.BuildStandardTaxApplicabilityReport(outcome, RegulatoryRuleEngine.StandardAdditionalTaxCodes);
        Assert.All(report, r => Assert.Equal(TaxApplicabilityKind.DonneeManquante, r.Kind));
        Assert.All(report, r => Assert.Equal("Donnée réglementaire manquante — validation requise", r.DisplayStatusFr));
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
