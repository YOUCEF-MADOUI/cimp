using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.RegulatoryEngine;
using ImportCostAlgeria.CalculationEngine;

namespace ImportCostAlgeria.UnitTests;

/// <summary>
/// Tests de régression — correction du 2026-10-05 (Bug 2 de la revue : "le Droit de Douane (DD) importé
/// depuis Excel est correctement affiché dans la colonne 'Droit Excel %' mais n'est jamais utilisé par le
/// moteur de calcul : la colonne DD réelle et le Total DD restent figés à 0").
///
/// Cause identifiée (confirmée par lecture de code, PAS par supposition) : <see
/// cref="ImportCalculationOrchestrator"/> implémentait déjà, AVANT cette correction, EXACTEMENT la bonne
/// règle de résolution (1. règle réglementaire officielle si elle existe, 2. sinon taux Excel SI
/// <see cref="ImportLine.UserConfirmedExcelDutyFallback"/> est vrai, 3. sinon taux par défaut de
/// l'importation, 4. sinon 0/NON DÉTERMINÉ) — mais <see cref="ImportLine.UserConfirmedExcelDutyFallback"/>
/// n'était exposé NULLE PART dans l'interface (ImportLineRowViewModel/ImportDetailViewModel/
/// ImportDetailView.xaml), restait donc TOUJOURS à "false" par défaut, et le repli Excel n'était par
/// conséquent JAMAIS atteignable en pratique, quoi que fasse l'utilisateur. La correction ajoute
/// uniquement l'interface manquante (case à cocher par ligne + confirmation globale pour toute
/// l'importation) ; AUCUNE ligne de <see cref="ImportCalculationOrchestrator"/> n'a eu besoin d'être
/// modifiée pour la résolution du taux elle-même (seule une nouvelle anomalie d'avertissement a été
/// ajoutée, voir le dernier test ci-dessous).
///
/// Ces tests exercent directement le moteur de calcul (couche testable, cross-platform) en reproduisant
/// EXACTEMENT ce que fait désormais l'interface une fois la correction appliquée : positionner
/// ImportLine.UserConfirmedExcelDutyFallback = true (soit via la case par ligne, soit via le bouton de
/// confirmation globale "Utiliser le Droit de douane provenant du fichier Excel pour toute
/// l'importation" — ImportDetailViewModel.ConfirmUseExcelDutyForAllLinesCommand, non testable ici
/// directement car situé dans le projet WPF ImportCostAlgeria.Presentation, non référencé par ce
/// projet de tests cross-platform, conformément à l'architecture déjà en place pour le reste de la couche
/// de présentation).
/// </summary>
public sealed class ExcelDutyFallbackConfirmationTests
{
    private static readonly LegalSource TestLegalSource = new()
    {
        HierarchyLevel = LegalSourceHierarchyLevel.Level1_JournalOfficielJora,
        OfficialTitle = "Tarif Douanier Officiel Algérie (jeu de test Bug 2 - repli DD Excel)",
        JoraReference = "JORA N° 1 (TEST BUG2)",
        ArticleReference = "Art. 16 ter CDA (TEST)",
        PublicationDate = new DateOnly(2026, 1, 1),
        EffectiveDate = new DateOnly(2026, 1, 1)
    };

    private static readonly Guid AdminId = Guid.NewGuid();
    private const string HsCodeNoRule = "1111111111";
    private const string HsCodeWithRule = "2222222222";

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
                SourceName = "Portail officiel ALCES - DGD (jeu de test Bug 2)"
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
        Code = "BUG2-CO",
        LegalName = "SARL TEST REPLI DD EXCEL"
    };

    private static ImportOperation BuildOperation(ImportLine line, bool useDefaultRatesWhenRuleMissing) => new()
    {
        CompanyId = Guid.NewGuid(),
        ImportNumber = "BUG2-TEST",
        ReferenceDate = new DateOnly(2026, 6, 1),
        SupplierName = "FOURNISSEUR TEST",
        ExportShippingCountryIso2 = "CN",
        MainCurrencyCode = "EUR",
        // Incoterm CFR : aucun frais complémentaire n'est requis (fret déjà inclus dans le prix facturé),
        // ce qui permet de garder un calcul de Valeur en douane simple et prévisible pour ces tests
        // ciblés (prix d'achat x taux de change, sans allocation de frais à vérifier séparément).
        Incoterm = IncotermCode.CFR,
        ArrivalPortOrBorder = "Port d'Alger",
        TransportMode = "Maritime",
        UseDefaultRatesWhenRuleMissing = useDefaultRatesWhenRuleMissing,
        Lines = { line }
    };

    [Fact]
    public void ExcelDutyRate_ShouldBeIgnored_WhenNotExplicitlyConfirmedByUser_EvenThoughItWasCorrectlyImported()
    {
        // Reproduction exacte du bug signalé : la colonne Excel contient bien un Droit Excel (15 %), mais
        // l'utilisateur n'a PAS confirmé son utilisation (UserConfirmedExcelDutyFallback reste à sa valeur
        // d'usine "false" — exactement l'état dans lequel elle restait TOUJOURS coincée avant la
        // correction de l'interface). Taux par défaut désactivé pour isoler sans ambiguïté le repli Excel.
        var line = new ImportLine
        {
            LineNumber = 1,
            ProductReference = "ART-1",
            Designation = "Article sans règle réglementaire DD",
            Quantity = 10m,
            UnitPurchasePrice = 100m,
            CurrencyCode = "EUR",
            HsCodeConfirmed10 = HsCodeNoRule,
            OriginCountryIso2 = "CN",
            ExcelDutyRatePercent = 15.0m,
            UserConfirmedExcelDutyFallback = false
        };
        var operation = BuildOperation(line, useDefaultRatesWhenRuleMissing: false);

        var orchestrator = BuildOrchestrator(Array.Empty<RegulatoryRule>());
        var summary = orchestrator.ExecuteCalculation(BuildCompany(), operation);

        var result = Assert.Single(summary.LineResults);
        Assert.Equal(0m, result.CustomsOutcome.CustomsDutyAmountDzd);
        Assert.Equal(0m, summary.TotalCustomsDutyDzd);
        Assert.Equal(DutyComparisonStatus.RegulatoryNotFoundPendingConfirmation, result.CustomsOutcome.ExcelVsRegulatoryComparison);
    }

    [Fact]
    public void ExcelDutyRate_WhenExplicitlyConfirmedByUser_AndNoRegulatoryRuleExists_ShouldBeUsed_ForLineAndTotals()
    {
        // Même donnée Excel que le test précédent, mais cette fois CONFIRMÉE par l'utilisateur — exactement
        // ce que permet désormais la nouvelle case à cocher par ligne / le nouveau bouton de confirmation
        // globale ImportDetailViewModel.ConfirmUseExcelDutyForAllLinesCommand.
        var line = new ImportLine
        {
            LineNumber = 1,
            ProductReference = "ART-1",
            Designation = "Article sans règle réglementaire DD",
            Quantity = 10m,
            UnitPurchasePrice = 100m,
            CurrencyCode = "EUR",
            HsCodeConfirmed10 = HsCodeNoRule,
            OriginCountryIso2 = "CN",
            ExcelDutyRatePercent = 15.0m,
            UserConfirmedExcelDutyFallback = true
        };
        var operation = BuildOperation(line, useDefaultRatesWhenRuleMissing: false);

        var orchestrator = BuildOrchestrator(Array.Empty<RegulatoryRule>());
        var summary = orchestrator.ExecuteCalculation(BuildCompany(), operation);

        var result = Assert.Single(summary.LineResults);

        // Valeur en douane = 10 x 100 EUR x 150 DZD/EUR = 150 000 DZD (aucun frais additionnel ici).
        decimal expectedCustomsValueDzd = 150_000m;
        Assert.Equal(expectedCustomsValueDzd, result.CustomsOutcome.CustomsValueDzd);

        decimal expectedDutyDzd = expectedCustomsValueDzd * 0.15m; // 15 % -> 22 500 DZD
        Assert.Equal(expectedDutyDzd, result.CustomsOutcome.CustomsDutyAmountDzd);
        Assert.Equal(DutyComparisonStatus.ExcelFallbackConfirmedByUser, result.CustomsOutcome.ExcelVsRegulatoryComparison);
        Assert.Contains("Taux Excel", result.CustomsOutcome.ComparisonLabelFr);

        // Le montant se répercute bien sur le Total DD ET sur le Total Droits et Taxes ("Total
        // dédouanement") de l'importation — pas seulement sur la ligne isolée.
        Assert.Equal(expectedDutyDzd, summary.TotalCustomsDutyDzd);
        Assert.True(summary.TotalDutiesAndTaxesDzd >= expectedDutyDzd);

        // Aucun avertissement "taux suspicieusement faible" pour un taux réaliste (15 %).
        Assert.DoesNotContain(summary.Anomalies, a => a.AnomalyCode == "EXCEL_DUTY_RATE_SUSPICIOUSLY_LOW");
    }

    [Fact]
    public void OfficialRegulatoryDutyRule_ShouldAlwaysTakePriority_OverAConfirmedButDifferentExcelDutyRate()
    {
        // Exigence explicite de la correction : "une règle réglementaire officielle, si elle existe, reste
        // prioritaire" — même si l'utilisateur a confirmé un taux Excel différent.
        var rules = new[]
        {
            new RegulatoryRule
            {
                Code = "DD-TEST-BUG2",
                RegulatoryVersionCode = "2026.01",
                RuleType = RegulatoryRuleType.CustomsDuty,
                TaxCode = "DD",
                TaxNameFr = "Droit de Douane",
                HsCode10 = HsCodeWithRule,
                RatePercent = 30.0m,
                CalculationBase = TaxableBaseType.CustomsValueDzd,
                ValidFrom = new DateOnly(2026, 1, 1),
                LegalSource = TestLegalSource,
                Status = RegulatoryRuleStatus.PublishedNewVersion,
                ValidatedByAdminUserId = AdminId
            }
        };

        var line = new ImportLine
        {
            LineNumber = 1,
            ProductReference = "ART-2",
            Designation = "Article avec règle réglementaire DD officielle",
            Quantity = 10m,
            UnitPurchasePrice = 100m,
            CurrencyCode = "EUR",
            HsCodeConfirmed10 = HsCodeWithRule,
            OriginCountryIso2 = "CN",
            ExcelDutyRatePercent = 10.0m,             // Différent du taux réglementaire (30 %)
            UserConfirmedExcelDutyFallback = true      // Confirmé malgré tout par l'utilisateur
        };
        var operation = BuildOperation(line, useDefaultRatesWhenRuleMissing: true);

        var orchestrator = BuildOrchestrator(rules);
        var summary = orchestrator.ExecuteCalculation(BuildCompany(), operation);

        var result = Assert.Single(summary.LineResults);
        decimal expectedCustomsValueDzd = 150_000m;
        decimal expectedDutyDzd = expectedCustomsValueDzd * 0.30m; // La règle officielle (30 %) l'emporte.
        Assert.Equal(expectedDutyDzd, result.CustomsOutcome.CustomsDutyAmountDzd);
        Assert.Equal(DutyComparisonStatus.Difference, result.CustomsOutcome.ExcelVsRegulatoryComparison);
        Assert.Contains(summary.Anomalies, a => a.AnomalyCode == "EXCEL_VS_REGULATORY_DUTY_DIFF");
    }

    [Fact]
    public void NoExcelDutyRateAtAll_WithNoRegulatoryRule_ShouldPreserveExistingDefaultRateFallbackBehavior()
    {
        // Non-régression explicite : un article SANS aucun Droit Excel importé (ExcelDutyRatePercent =
        // null) doit continuer à suivre exactement le comportement de repli déjà existant (taux par défaut
        // de l'importation, jamais de blocage) — cette correction ne doit rien changer à ce cas.
        var line = new ImportLine
        {
            LineNumber = 1,
            ProductReference = "ART-3",
            Designation = "Article sans Droit Excel importé",
            Quantity = 10m,
            UnitPurchasePrice = 100m,
            CurrencyCode = "EUR",
            HsCodeConfirmed10 = HsCodeNoRule,
            OriginCountryIso2 = "CN",
            ExcelDutyRatePercent = null,
            UserConfirmedExcelDutyFallback = false
        };
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
    public void ExcelDutyRate_WhenSuspiciouslyLow_ShouldStillBeAppliedLiterally_ButRaiseAnExplicitWarning()
    {
        // Vérification explicite de la convention de pourcentage (demande "NE PAS DEVINER", Bug 2) : CIMP
        // n'effectue AUCUNE normalisation automatique d'une valeur Excel du type "0.05". Elle reste
        // interprétée TELLE QUELLE, en points de pourcentage directs (0.05 => 0,05 %, PAS 5 %), exactement
        // comme pour n'importe quelle autre valeur de ce champ (voir ImportLine.ExcelDutyRatePercent et
        // V1CompleteTestSuite, qui utilisent déjà ce champ en points de pourcentage directs : 10.0m = 10 %,
        // 5.0m = 5 %). En l'absence de toute preuve contraire dans le code existant, cette convention est
        // CONSERVÉE (pas de "correction" silencieuse potentiellement erronée) — mais un avertissement
        // explicite et traçable est désormais levé pour alerter l'utilisateur d'une valeur anormalement
        // faible, probablement issue d'une fraction Excel ("0.05") non convertie avant l'import.
        var line = new ImportLine
        {
            LineNumber = 1,
            ProductReference = "ART-4",
            Designation = "Article avec taux Excel suspect (fraction non convertie ?)",
            Quantity = 10m,
            UnitPurchasePrice = 100m,
            CurrencyCode = "EUR",
            HsCodeConfirmed10 = HsCodeNoRule,
            OriginCountryIso2 = "CN",
            ExcelDutyRatePercent = 0.05m,
            UserConfirmedExcelDutyFallback = true
        };
        var operation = BuildOperation(line, useDefaultRatesWhenRuleMissing: false);

        var orchestrator = BuildOrchestrator(Array.Empty<RegulatoryRule>());
        var summary = orchestrator.ExecuteCalculation(BuildCompany(), operation);

        var result = Assert.Single(summary.LineResults);
        decimal expectedCustomsValueDzd = 150_000m;
        decimal expectedDutyDzd = expectedCustomsValueDzd * 0.0005m; // 0,05 % littéral, PAS 5 %.
        Assert.Equal(expectedDutyDzd, result.CustomsOutcome.CustomsDutyAmountDzd);
        Assert.Equal(DutyComparisonStatus.ExcelFallbackConfirmedByUser, result.CustomsOutcome.ExcelVsRegulatoryComparison);

        Assert.Contains(summary.Anomalies, a =>
            a.AnomalyCode == "EXCEL_DUTY_RATE_SUSPICIOUSLY_LOW" &&
            a.Severity == AnomalySeverity.Avertissement);
    }

    [Fact]
    public void ExcelDutyRate_WhenValueIsZeroPointFifteen_AndConfirmedByUser_ShouldBeUsedLiterally_NotMultipliedByOneHundred()
    {
        // Même vérification explicite que ci-dessus, avec la DEUXIÈME valeur d'exemple donnée dans le
        // signalement du Bug 2 ("DD / 0.05 / 0.15") : 0.15 doit être utilisé TEL QUEL (0,15 %), jamais
        // transformé en 15 % — aucune preuve dans le code existant ne justifierait une telle conversion
        // (voir le commentaire détaillé du test précédent). Ce taux restant < 1 point de pourcentage,
        // l'avertissement EXCEL_DUTY_RATE_SUSPICIOUSLY_LOW doit également être levé, comme pour 0.05.
        var line = new ImportLine
        {
            LineNumber = 1,
            ProductReference = "ART-5",
            Designation = "Article avec taux Excel 0.15 (deuxième exemple du signalement)",
            Quantity = 10m,
            UnitPurchasePrice = 100m,
            CurrencyCode = "EUR",
            HsCodeConfirmed10 = HsCodeNoRule,
            OriginCountryIso2 = "CN",
            ExcelDutyRatePercent = 0.15m,
            UserConfirmedExcelDutyFallback = true
        };
        var operation = BuildOperation(line, useDefaultRatesWhenRuleMissing: false);

        var orchestrator = BuildOrchestrator(Array.Empty<RegulatoryRule>());
        var summary = orchestrator.ExecuteCalculation(BuildCompany(), operation);

        var result = Assert.Single(summary.LineResults);
        decimal expectedCustomsValueDzd = 150_000m;
        decimal expectedDutyDzd = expectedCustomsValueDzd * 0.0015m; // 0,15 % littéral, PAS 15 %.
        Assert.Equal(expectedDutyDzd, result.CustomsOutcome.CustomsDutyAmountDzd);
        Assert.Equal(expectedDutyDzd, summary.TotalCustomsDutyDzd);
        Assert.Equal(DutyComparisonStatus.ExcelFallbackConfirmedByUser, result.CustomsOutcome.ExcelVsRegulatoryComparison);

        Assert.Contains(summary.Anomalies, a =>
            a.AnomalyCode == "EXCEL_DUTY_RATE_SUSPICIOUSLY_LOW" &&
            a.Severity == AnomalySeverity.Avertissement);
    }
}

