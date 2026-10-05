using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.RegulatoryEngine;
using ImportCostAlgeria.CalculationEngine;

namespace ImportCostAlgeria.UnitTests;

/// <summary>
/// Tests de régression — revue du 2026-10-05 ("Refonte logique/UX du Droit de Douane (DD) : Excel
/// prioritaire par défaut, DD IA en comparaison/override").
///
/// REMPLACE intégralement l'ancien fichier <c>ExcelDutyFallbackConfirmationTests.cs</c> (supprimé par
/// cette revue) : son unique mécanisme testé — confirmation OBLIGATOIRE ligne par ligne via
/// <see cref="ImportLine.UserConfirmedExcelDutyFallback"/> avant que le Droit de Douane (DD) Excel ne soit
/// utilisé, avec priorité SYSTÉMATIQUE de la règle réglementaire officielle sur un taux Excel confirmé
/// différent — est explicitement INVERSÉ par cette revue : le DD Excel (donnée métier déclarée par
/// l'utilisateur dans son fichier) est désormais utilisé PAR DÉFAUT dès qu'il existe, SANS confirmation,
/// et ne cède la priorité au DD "IA" (proposition automatique du moteur réglementaire à partir du Code SH)
/// que si l'utilisateur coche explicitement "Forcer DD IA" (<see cref="ImportLine.ForceAiDutyRate"/>) sur
/// la ligne concernée. L'ancien test
/// <c>OfficialRegulatoryDutyRule_ShouldAlwaysTakePriority_OverAConfirmedButDifferentExcelDutyRate</c>,
/// dont l'assertion contredit directement la nouvelle règle métier, n'a donc PAS été conservé tel quel —
/// voir <see cref="ExcelDutyRate_ShouldTakePriority_OverADifferentAiProposedRate_WhenNotForced"/> et
/// <see cref="AiProposedRate_ShouldBeUsed_WhenForceAiDutyRateIsExplicitlyActivated"/> ci-dessous pour son
/// remplacement direct sous la nouvelle logique.
///
/// Nouvel ordre de priorité exercé par ces tests (voir <see cref="ImportCalculationOrchestrator"/>) :
///   1. DD IA forcé explicitement par l'utilisateur (ForceAiDutyRate=true ET une proposition IA existe) ;
///   2. DD Excel fourni dans le fichier (prioritaire par défaut dès qu'il existe, SANS confirmation) ;
///   3. DD IA proposé automatiquement (aucun DD Excel disponible) ;
///   4. taux DD par défaut de l'importation (non-régression, comportement inchangé) ;
///   5. 0 / NON DÉTERMINÉ (non-régression, comportement inchangé).
/// </summary>
public sealed class ExcelDutyPriorityTests
{
    private static readonly LegalSource TestLegalSource = new()
    {
        HierarchyLevel = LegalSourceHierarchyLevel.Level1_JournalOfficielJora,
        OfficialTitle = "Tarif Douanier Officiel Algérie (jeu de test — priorité DD Excel / DD IA)",
        JoraReference = "JORA N° 1 (TEST DD PRIORITY)",
        ArticleReference = "Art. 16 ter CDA (TEST)",
        PublicationDate = new DateOnly(2026, 1, 1),
        EffectiveDate = new DateOnly(2026, 1, 1)
    };

    private static readonly Guid AdminId = Guid.NewGuid();
    private const string HsCodeNoRule = "1111111111";
    private const string HsCodeWithRule = "2222222222";

    private static RegulatoryRule BuildAiDutyRule(decimal ratePercent, string hsCode = HsCodeWithRule) => new()
    {
        Code = $"DD-TEST-{hsCode}-{ratePercent}",
        RegulatoryVersionCode = "2026.01",
        RuleType = RegulatoryRuleType.CustomsDuty,
        TaxCode = "DD",
        TaxNameFr = "Droit de Douane",
        HsCode10 = hsCode,
        RatePercent = ratePercent,
        CalculationBase = TaxableBaseType.CustomsValueDzd,
        ValidFrom = new DateOnly(2026, 1, 1),
        LegalSource = TestLegalSource,
        Status = RegulatoryRuleStatus.PublishedNewVersion,
        ValidatedByAdminUserId = AdminId
    };

    private static ImportCalculationOrchestrator BuildOrchestrator(IReadOnlyList<RegulatoryRule> rules)
    {
        var rateProvider = new InMemoryExchangeRateProvider(new[]
        {
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR",
                QuoteCurrencyCode = "DZD",
                RateToDzd = 150.0m,
                QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1),
                RateType = "OFFICIEL_DOUANE_ALCES",
                SourceName = "Portail officiel ALCES - DGD (jeu de test DD priority)"
            }
        });

        return new ImportCalculationOrchestrator(
            new CurrencyCalculator(rateProvider),
            new CostAllocationEngine(),
            new CustomsValueCalculator(),
            new RegulatoryRuleEngine(new InMemoryRegulatoryRuleRepository(rules)),
            new CurrencyConversionService(rateProvider));
    }

    private static Company BuildCompany() => new()
    {
        Code = "DDPRIO-CO",
        LegalName = "SARL TEST PRIORITE DD EXCEL / DD IA"
    };

    private static ImportOperation BuildOperation(ImportLine line, bool useDefaultRatesWhenRuleMissing) => new()
    {
        CompanyId = Guid.NewGuid(),
        ImportNumber = "DDPRIO-TEST",
        ReferenceDate = new DateOnly(2026, 6, 1),
        SupplierName = "FOURNISSEUR TEST",
        ExportShippingCountryIso2 = "CN",
        MainCurrencyCode = "EUR",
        // Incoterm CFR : aucun frais complémentaire n'est requis (fret déjà inclus dans le prix facturé),
        // ce qui permet de garder un calcul de Valeur en douane simple et prévisible pour ces tests ciblés
        // (prix d'achat x taux de change, sans allocation de frais à vérifier séparément).
        Incoterm = IncotermCode.CFR,
        ArrivalPortOrBorder = "Port d'Alger",
        TransportMode = "Maritime",
        UseDefaultRatesWhenRuleMissing = useDefaultRatesWhenRuleMissing,
        Lines = { line }
    };

    private static ImportLine BuildLine(
        decimal? excelDutyRatePercent,
        bool forceAiDutyRate = false,
        string hsCode = HsCodeWithRule) => new()
    {
        LineNumber = 1,
        ProductReference = "ART-1",
        Designation = "Article de test priorité DD Excel / DD IA",
        Quantity = 10m,
        UnitPurchasePrice = 100m,
        CurrencyCode = "EUR",
        HsCodeConfirmed10 = hsCode,
        OriginCountryIso2 = "CN",
        ExcelDutyRatePercent = excelDutyRatePercent,
        ForceAiDutyRate = forceAiDutyRate
    };

    // Valeur en douane constante pour toutes les lignes ci-dessus : 10 x 100 EUR x 150 DZD/EUR = 150 000 DZD
    // (aucun frais additionnel, Incoterm CFR) — sert de base à tous les calculs attendus.
    private const decimal ExpectedCustomsValueDzd = 150_000m;

    // --------------------------------------------------------------------------------------------------
    // 1. DD Excel = 30 %, DD IA = 5 % -> résultat calculé avec 30 % (DD Excel prioritaire par défaut).
    // --------------------------------------------------------------------------------------------------
    [Fact]
    public void ExcelDutyRate_ShouldTakePriority_OverADifferentAiProposedRate_WhenNotForced()
    {
        var rules = new[] { BuildAiDutyRule(5.0m) };
        var line = BuildLine(excelDutyRatePercent: 30.0m);
        var operation = BuildOperation(line, useDefaultRatesWhenRuleMissing: false);

        var orchestrator = BuildOrchestrator(rules);
        var summary = orchestrator.ExecuteCalculation(BuildCompany(), operation);

        var result = Assert.Single(summary.LineResults);
        Assert.Equal(ExpectedCustomsValueDzd * 0.30m, result.CustomsOutcome.CustomsDutyAmountDzd);
        Assert.Equal(30.0m, result.CustomsOutcome.CustomsDutyRatePercent);
        Assert.Equal(5.0m, result.CustomsOutcome.AiProposedDutyRatePercent);
        Assert.Equal(DutyComparisonStatus.ExcelPriorityDiffersFromAi, result.CustomsOutcome.ExcelVsRegulatoryComparison);
        Assert.Equal(DataOriginTag.DonneeUtilisateur, result.CustomsOutcome.CustomsDutyRateOriginTag);
        Assert.Contains(summary.Anomalies, a => a.AnomalyCode == "EXCEL_VS_AI_DUTY_DIFF");
        // La citation légale ne doit JAMAIS être attribuée à tort à la règle IA/réglementaire ici : ce
        // n'est pas elle qui a été appliquée (voir revue du 2026-10-05, correction de la citation légale).
        Assert.Null(result.CustomsOutcome.CustomsDutyLegalArticleReference);
    }

    // --------------------------------------------------------------------------------------------------
    // 2. DD Excel = 5 %, DD IA = 30 % -> résultat calculé avec 5 % (DD Excel toujours prioritaire).
    // --------------------------------------------------------------------------------------------------
    [Fact]
    public void ExcelDutyRate_ShouldTakePriority_RegardlessOfWhichSideIsHigher()
    {
        var rules = new[] { BuildAiDutyRule(30.0m) };
        var line = BuildLine(excelDutyRatePercent: 5.0m);
        var operation = BuildOperation(line, useDefaultRatesWhenRuleMissing: false);

        var orchestrator = BuildOrchestrator(rules);
        var summary = orchestrator.ExecuteCalculation(BuildCompany(), operation);

        var result = Assert.Single(summary.LineResults);
        Assert.Equal(ExpectedCustomsValueDzd * 0.05m, result.CustomsOutcome.CustomsDutyAmountDzd);
        Assert.Equal(5.0m, result.CustomsOutcome.CustomsDutyRatePercent);
        Assert.Equal(30.0m, result.CustomsOutcome.AiProposedDutyRatePercent);
        Assert.Equal(DutyComparisonStatus.ExcelPriorityDiffersFromAi, result.CustomsOutcome.ExcelVsRegulatoryComparison);
    }

    // --------------------------------------------------------------------------------------------------
    // 3 & 4. Convention de pourcentage ("NE PAS DEVINER") : DD Excel = 0,05 % / 0,15 % utilisé
    // LITTÉRALEMENT, jamais multiplié par 100, même lorsqu'il est prioritaire par défaut (sans
    // confirmation). L'avertissement "taux suspicieusement faible" reste levé, à titre informatif
    // uniquement — la valeur n'est jamais modifiée automatiquement.
    // --------------------------------------------------------------------------------------------------
    [Theory]
    [InlineData(0.05)]
    [InlineData(0.15)]
    public void ExcelDutyRate_WhenSuspiciouslyLow_ShouldBeUsedLiterally_NeverMultipliedByOneHundred(decimal excelRate)
    {
        var rules = new[] { BuildAiDutyRule(5.0m) };
        var line = BuildLine(excelDutyRatePercent: excelRate);
        var operation = BuildOperation(line, useDefaultRatesWhenRuleMissing: false);

        var orchestrator = BuildOrchestrator(rules);
        var summary = orchestrator.ExecuteCalculation(BuildCompany(), operation);

        var result = Assert.Single(summary.LineResults);
        decimal expectedDutyDzd = ExpectedCustomsValueDzd * (excelRate / 100m);
        Assert.Equal(expectedDutyDzd, result.CustomsOutcome.CustomsDutyAmountDzd);
        Assert.Equal(excelRate, result.CustomsOutcome.CustomsDutyRatePercent);
        Assert.Equal(DutyComparisonStatus.ExcelPriorityDiffersFromAi, result.CustomsOutcome.ExcelVsRegulatoryComparison);
        Assert.Contains(summary.Anomalies, a =>
            a.AnomalyCode == "EXCEL_DUTY_RATE_SUSPICIOUSLY_LOW" &&
            a.Severity == AnomalySeverity.Avertissement);
    }

    // --------------------------------------------------------------------------------------------------
    // 5. DD Excel absent, DD IA = 5 % -> résultat calculé avec 5 % (DD IA utilisé faute de DD Excel).
    // --------------------------------------------------------------------------------------------------
    [Fact]
    public void AiProposedRate_ShouldBeUsed_WhenNoExcelDutyRateIsProvided()
    {
        var rules = new[] { BuildAiDutyRule(5.0m) };
        var line = BuildLine(excelDutyRatePercent: null);
        var operation = BuildOperation(line, useDefaultRatesWhenRuleMissing: false);

        var orchestrator = BuildOrchestrator(rules);
        var summary = orchestrator.ExecuteCalculation(BuildCompany(), operation);

        var result = Assert.Single(summary.LineResults);
        Assert.Equal(ExpectedCustomsValueDzd * 0.05m, result.CustomsOutcome.CustomsDutyAmountDzd);
        Assert.Equal(5.0m, result.CustomsOutcome.CustomsDutyRatePercent);
        Assert.Equal(5.0m, result.CustomsOutcome.AiProposedDutyRatePercent);
        Assert.Equal(DutyComparisonStatus.AiProposedRateUsedNoExcelAvailable, result.CustomsOutcome.ExcelVsRegulatoryComparison);
        Assert.Equal(DataOriginTag.DonneeOfficielle, result.CustomsOutcome.CustomsDutyRateOriginTag);
        Assert.NotNull(result.CustomsOutcome.CustomsDutyLegalArticleReference);
    }

    // --------------------------------------------------------------------------------------------------
    // 6. DD Excel = 30 %, DD IA = 5 %, utilisateur active "Forcer DD IA" -> résultat calculé avec 5 %.
    // --------------------------------------------------------------------------------------------------
    [Fact]
    public void AiProposedRate_ShouldBeUsed_WhenForceAiDutyRateIsExplicitlyActivated()
    {
        var rules = new[] { BuildAiDutyRule(5.0m) };
        var line = BuildLine(excelDutyRatePercent: 30.0m, forceAiDutyRate: true);
        var operation = BuildOperation(line, useDefaultRatesWhenRuleMissing: false);

        var orchestrator = BuildOrchestrator(rules);
        var summary = orchestrator.ExecuteCalculation(BuildCompany(), operation);

        var result = Assert.Single(summary.LineResults);
        Assert.Equal(ExpectedCustomsValueDzd * 0.05m, result.CustomsOutcome.CustomsDutyAmountDzd);
        Assert.Equal(5.0m, result.CustomsOutcome.CustomsDutyRatePercent);
        Assert.Equal(DutyComparisonStatus.AiForcedByUserOverridingExcel, result.CustomsOutcome.ExcelVsRegulatoryComparison);
        Assert.Equal(DataOriginTag.DonneeOfficielle, result.CustomsOutcome.CustomsDutyRateOriginTag);
    }

    // --------------------------------------------------------------------------------------------------
    // 7. DD Excel = 30 %, DD IA absent (aucune règle réglementaire DD) -> résultat calculé avec 30 %.
    // --------------------------------------------------------------------------------------------------
    [Fact]
    public void ExcelDutyRate_ShouldBeUsed_WhenNoAiProposalIsAvailable()
    {
        var line = BuildLine(excelDutyRatePercent: 30.0m, hsCode: HsCodeNoRule);
        var operation = BuildOperation(line, useDefaultRatesWhenRuleMissing: false);

        var orchestrator = BuildOrchestrator(Array.Empty<RegulatoryRule>());
        var summary = orchestrator.ExecuteCalculation(BuildCompany(), operation);

        var result = Assert.Single(summary.LineResults);
        Assert.Equal(ExpectedCustomsValueDzd * 0.30m, result.CustomsOutcome.CustomsDutyAmountDzd);
        Assert.Equal(30.0m, result.CustomsOutcome.CustomsDutyRatePercent);
        Assert.Null(result.CustomsOutcome.AiProposedDutyRatePercent);
        Assert.Equal(DutyComparisonStatus.ExcelPriorityNoAiProposalAvailable, result.CustomsOutcome.ExcelVsRegulatoryComparison);
        Assert.Equal(DataOriginTag.DonneeUtilisateur, result.CustomsOutcome.CustomsDutyRateOriginTag);
    }

    // --------------------------------------------------------------------------------------------------
    // 8. Ni DD Excel, ni DD IA -> non-régression : comportement de repli existant inchangé (taux par
    // défaut de l'importation si autorisé, sinon 0 / NON DÉTERMINÉ).
    // --------------------------------------------------------------------------------------------------
    [Fact]
    public void NoExcelDutyRateAndNoAiProposal_ShouldPreserveExistingDefaultRateFallbackBehavior()
    {
        var line = BuildLine(excelDutyRatePercent: null, hsCode: HsCodeNoRule);
        var operation = BuildOperation(line, useDefaultRatesWhenRuleMissing: true); // Comportement d'usine.

        var orchestrator = BuildOrchestrator(Array.Empty<RegulatoryRule>());
        var summary = orchestrator.ExecuteCalculation(BuildCompany(), operation);

        var result = Assert.Single(summary.LineResults);
        Assert.Equal(DutyComparisonStatus.DefaultImportRateUsed, result.CustomsOutcome.ExcelVsRegulatoryComparison);
        Assert.Equal(DataOriginTag.ValeurParDefautImportation, result.CustomsOutcome.CustomsDutyRateOriginTag);
        Assert.Contains(summary.Anomalies, a => a.AnomalyCode == "DD_DEFAULT_RATE_USED");
        Assert.False(summary.HasBlockingAnomalies);
    }

    [Fact]
    public void NoExcelDutyRateAndNoAiProposal_AndDefaultRatesDisabled_ShouldRemainZeroAndNonDetermined()
    {
        var line = BuildLine(excelDutyRatePercent: null, hsCode: HsCodeNoRule);
        var operation = BuildOperation(line, useDefaultRatesWhenRuleMissing: false);

        var orchestrator = BuildOrchestrator(Array.Empty<RegulatoryRule>());
        var summary = orchestrator.ExecuteCalculation(BuildCompany(), operation);

        var result = Assert.Single(summary.LineResults);
        Assert.Equal(0m, result.CustomsOutcome.CustomsDutyAmountDzd);
        Assert.Equal(DutyComparisonStatus.RegulatoryNotFoundPendingConfirmation, result.CustomsOutcome.ExcelVsRegulatoryComparison);
    }

    // --------------------------------------------------------------------------------------------------
    // Cas limite : "Forcer DD IA" coché mais AUCUNE proposition DD IA disponible -> repli sur le DD Excel
    // (si disponible), avec un avertissement explicite — le choix de l'utilisateur n'est jamais ignoré
    // silencieusement.
    // --------------------------------------------------------------------------------------------------
    [Fact]
    public void ForceAiDutyRate_WhenNoAiProposalAvailable_ShouldFallBackToExcelRate_WithExplicitWarning()
    {
        var line = BuildLine(excelDutyRatePercent: 30.0m, forceAiDutyRate: true, hsCode: HsCodeNoRule);
        var operation = BuildOperation(line, useDefaultRatesWhenRuleMissing: false);

        var orchestrator = BuildOrchestrator(Array.Empty<RegulatoryRule>());
        var summary = orchestrator.ExecuteCalculation(BuildCompany(), operation);

        var result = Assert.Single(summary.LineResults);
        Assert.Equal(ExpectedCustomsValueDzd * 0.30m, result.CustomsOutcome.CustomsDutyAmountDzd);
        Assert.Equal(30.0m, result.CustomsOutcome.CustomsDutyRatePercent);
        Assert.Equal(DutyComparisonStatus.AiForcedByUserButNoAiProposalAvailable, result.CustomsOutcome.ExcelVsRegulatoryComparison);
        Assert.Contains(summary.Anomalies, a => a.AnomalyCode == "AI_DUTY_RATE_FORCED_BUT_UNAVAILABLE");
    }

    // --------------------------------------------------------------------------------------------------
    // Cas limite complémentaire : DD Excel == DD IA (identiques) -> aucune alerte de différence, statut
    // de correspondance, montant inchangé (exemple "Ligne 1" fourni par l'utilisateur : DD IA=5 %, DD
    // Excel=5 % -> 5 % utilisé, aucune alerte).
    // --------------------------------------------------------------------------------------------------
    [Fact]
    public void ExcelDutyRate_ShouldMatchAiProposal_AndRaiseNoDifferenceWarning_WhenBothAreEqual()
    {
        var rules = new[] { BuildAiDutyRule(5.0m) };
        var line = BuildLine(excelDutyRatePercent: 5.0m);
        var operation = BuildOperation(line, useDefaultRatesWhenRuleMissing: false);

        var orchestrator = BuildOrchestrator(rules);
        var summary = orchestrator.ExecuteCalculation(BuildCompany(), operation);

        var result = Assert.Single(summary.LineResults);
        Assert.Equal(ExpectedCustomsValueDzd * 0.05m, result.CustomsOutcome.CustomsDutyAmountDzd);
        Assert.Equal(DutyComparisonStatus.ExcelPriorityMatchesAi, result.CustomsOutcome.ExcelVsRegulatoryComparison);
        Assert.DoesNotContain(summary.Anomalies, a => a.AnomalyCode == "EXCEL_VS_AI_DUTY_DIFF");
    }

    // --------------------------------------------------------------------------------------------------
    // 9. Vérification complète : DD, CS, TVA, PRCT, coût de revient et total dédouanement restent tous
    // correctement calculés/cohérents une fois le DD Excel appliqué en priorité (les autres taxes et le
    // coût de revient ne doivent JAMAIS être affectés par cette revue — seule la SOURCE du taux DD change).
    // --------------------------------------------------------------------------------------------------
    [Fact]
    public void FullTaxStackAndRealCostOfGoods_ShouldRemainConsistent_WhenExcelDutyRateIsAppliedByPriority()
    {
        var rules = new List<RegulatoryRule> { BuildAiDutyRule(5.0m) }; // Proposition DD IA = 5 %, ignorée (DD Excel prioritaire).
        rules.Add(new RegulatoryRule
        {
            Code = "CS-TEST-DDPRIO",
            RegulatoryVersionCode = "2026.01",
            RuleType = RegulatoryRuleType.SpecificTax,
            TaxCode = "CS",
            TaxNameFr = "Contribution de Solidarité",
            HsCode10 = HsCodeWithRule,
            RatePercent = 3.0m,
            CalculationBase = TaxableBaseType.CustomsValueDzd,
            ValidFrom = new DateOnly(2026, 1, 1),
            LegalSource = TestLegalSource,
            Status = RegulatoryRuleStatus.PublishedNewVersion,
            ValidatedByAdminUserId = AdminId
        });
        rules.Add(new RegulatoryRule
        {
            Code = "TVA-TEST-DDPRIO",
            RegulatoryVersionCode = "2026.01",
            RuleType = RegulatoryRuleType.Vat,
            TaxCode = "TVA",
            TaxNameFr = "Taxe sur la Valeur Ajoutée",
            HsCode10 = HsCodeWithRule,
            RatePercent = 19.0m,
            CalculationBase = TaxableBaseType.CustomsValuePlusDutiesAndTaxesExVatDzd,
            ValidFrom = new DateOnly(2026, 1, 1),
            LegalSource = TestLegalSource,
            Status = RegulatoryRuleStatus.PublishedNewVersion,
            ValidatedByAdminUserId = AdminId
        });
        rules.Add(new RegulatoryRule
        {
            Code = "PRCT-TEST-DDPRIO",
            RegulatoryVersionCode = "2026.01",
            RuleType = RegulatoryRuleType.SpecificTax,
            TaxCode = "PRCT",
            TaxNameFr = "Précompte à l'importation",
            HsCode10 = HsCodeWithRule,
            RatePercent = 2.0m,
            CalculationBase = TaxableBaseType.CustomsValuePlusPriorTaxesPlusVatDzd,
            ValidFrom = new DateOnly(2026, 1, 1),
            LegalSource = TestLegalSource,
            Status = RegulatoryRuleStatus.PublishedNewVersion,
            ValidatedByAdminUserId = AdminId
        });

        var line = BuildLine(excelDutyRatePercent: 30.0m); // Excel (30 %) prioritaire sur la proposition IA (5 %).
        var operation = BuildOperation(line, useDefaultRatesWhenRuleMissing: false);

        var orchestrator = BuildOrchestrator(rules);
        var summary = orchestrator.ExecuteCalculation(BuildCompany(), operation);

        var result = Assert.Single(summary.LineResults);

        // DD : 150 000 x 30 % = 45 000 DZD (DD Excel, pas la proposition IA à 5 %).
        decimal expectedDutyDzd = ExpectedCustomsValueDzd * 0.30m;
        Assert.Equal(expectedDutyDzd, result.CustomsOutcome.CustomsDutyAmountDzd);

        // CS : 150 000 x 3 % = 4 500 DZD (assiette = valeur en douane, non affectée par la source du DD).
        decimal expectedCsDzd = ExpectedCustomsValueDzd * 0.03m;
        var csTax = result.CustomsOutcome.AdditionalTaxes.Single(t => t.TaxCode == "CS");
        Assert.Equal(expectedCsDzd, csTax.TaxAmountDzd);

        // TVA : assiette = Valeur douane + DD + CS (hors TVA) = 150 000 + 45 000 + 4 500 = 199 500 ; TVA = 19 %.
        decimal expectedVatBaseDzd = ExpectedCustomsValueDzd + expectedDutyDzd + expectedCsDzd;
        decimal expectedVatDzd = CurrencyCalculator.RoundDzd(expectedVatBaseDzd * 0.19m);
        Assert.Equal(expectedVatBaseDzd, result.CustomsOutcome.VatTaxableBaseDzd);
        Assert.Equal(expectedVatDzd, result.CustomsOutcome.ImportVatAmountDzd);

        // PRCT : assiette = Valeur douane + CS + TVA (observée sur le cas D10 de référence) ; PRCT = 2 %.
        decimal expectedPrctBaseDzd = ExpectedCustomsValueDzd + expectedCsDzd + expectedVatDzd;
        decimal expectedPrctDzd = CurrencyCalculator.RoundDzd(expectedPrctBaseDzd * 0.02m);
        var prctTax = result.CustomsOutcome.AdditionalTaxes.Single(t => t.TaxCode == "PRCT");
        Assert.Equal(expectedPrctDzd, prctTax.TaxAmountDzd);

        // Total Droits et Taxes = DD + CS + PRCT + TVA, cohérent avec la somme des composants ci-dessus.
        decimal expectedTotalDutiesAndTaxesDzd = CurrencyCalculator.RoundDzd(expectedDutyDzd + expectedCsDzd + expectedPrctDzd + expectedVatDzd);
        Assert.Equal(expectedTotalDutiesAndTaxesDzd, result.CustomsOutcome.TotalDutiesAndTaxesDzd);
        Assert.Equal(expectedTotalDutiesAndTaxesDzd, summary.TotalDutiesAndTaxesDzd);

        // Coût de revient économique réel de la ligne = Valeur en douane + Total Droits et Taxes (aucun
        // frais additionnel dans ce scénario, Incoterm CFR) — jamais affecté par la SOURCE du taux DD.
        decimal expectedLineTotalDzd = CurrencyCalculator.RoundDzd(ExpectedCustomsValueDzd + expectedTotalDutiesAndTaxesDzd);
        Assert.Equal(expectedLineTotalDzd, result.CustomsOutcome.CustomsValuePlusDutiesAndTaxesDzd);
        Assert.Equal(expectedLineTotalDzd, summary.TotalRealCostOfGoodsDzd);

        // Statut explicite : différence détectée (30 % Excel vs 5 % IA), DD Excel utilisé.
        Assert.Equal(DutyComparisonStatus.ExcelPriorityDiffersFromAi, result.CustomsOutcome.ExcelVsRegulatoryComparison);
    }
}
