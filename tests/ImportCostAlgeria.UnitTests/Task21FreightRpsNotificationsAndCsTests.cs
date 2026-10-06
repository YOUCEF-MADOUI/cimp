using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.RegulatoryEngine;
using ImportCostAlgeria.CalculationEngine;

namespace ImportCostAlgeria.UnitTests;

/// <summary>
/// Tests de non-régression dédiés à la Tâche #21 (2026-10-06), portant sur 3 des 5 points de la demande
/// vérifiables par des tests automatisés purs (moteur de calcul / services indépendants de l'UI WPF, le
/// projet ImportCostAlgeria.Presentation — net8.0-windows/WPF — n'étant délibérément PAS référencé par ce
/// projet de tests) :
///
///   1) Les 4 cas du fret international ("valeur en douane" / "coût de revient"), REJOUÉS avec un montant
///      RÉEL fourni par l'utilisateur (1 845 EUR, converti en DZD), en complément des 4 scénarios déjà
///      couverts avec des montants DZD arbitraires par Task20WeightWarningAndFreightDoubleCountingTests
///      (fichier INTENTIONNELLEMENT non modifié ici, conformément à la consigne de ne pas toucher aux
///      tests déjà validés de la Tâche #20/#20.1).
///   2) RPS : absence de double comptage économique avec un cas réel chiffré (RPS = 8 000 DA, Fret =
///      281 762,13 DA) — vérifie explicitement TotalRpsDzd (via les allocations du frais RPS),
///      TotalFraisDzd (TotalImportFeesDzd), le "Total Dédouanement" (DD+CS+TVA+PRCT+TCS+RPS, formule de
///      ImportDetailViewModel.ApplySummaryToUi) et TotalRealCostOfGoodsDzd : le RPS apparaît légitimement
///      dans PLUSIEURS indicateurs DISTINCTS, mais n'est additionné qu'UNE SEULE fois dans le total
///      économique réel (coût de revient).
///   4) Filtrage des notifications ("Voir les détails") par groupe sélectionné — teste directement
///      AnomalyGroupingService.FilterByAnomalyCode (méthode pure utilisée par
///      ImportDetailViewModel.RefreshFilteredAnomalies), sans modifier AnomalyGroupingService.GroupByCode.
///   5) Remplacement du champ manuel TCS par un champ manuel CS dans la cascade de calcul (priorité : règle
///      officielle &gt; CS explicitement non applicable &gt; CS manuel confirmé pour l'import &gt; taux CS par
///      défaut &gt; non déterminé), confirmation UNE SEULE fois par import (jamais par article), et
///      indépendance totale vis-à-vis de l'ancien champ manuel TCS (conservé uniquement pour compatibilité
///      SQLite historique, jamais un second champ CS concurrent).
///
/// IMPORTANT : aucun de ces tests ne modifie ni ne contourne la priorité DD Excel/IA (Tâche #19), la
/// correction MISSING_WEIGHT (Tâche #20) ni les tests Task20WeightWarningAndFreightDoubleCountingTests
/// (Tâche #20.1) — tous réutilisent exclusivement les taux PAR DÉFAUT de l'importation (aucune
/// RegulatoryRule fournie), scénario déjà couvert et inchangé depuis la Tâche #19.
/// </summary>
public sealed class Task21FreightRpsNotificationsAndCsTests
{
    private static Company BuildCompany() => new()
    {
        Code = "TEST-T21",
        LegalName = "SARL TEST TÂCHE 21",
        IsImportVatNonRecoverable = true
    };

    private static ImportCalculationOrchestrator BuildOrchestrator(IEnumerable<ExchangeRateRecord>? rates = null)
    {
        var rateProvider = new InMemoryExchangeRateProvider(rates ?? Array.Empty<ExchangeRateRecord>());
        return new ImportCalculationOrchestrator(
            new CurrencyCalculator(rateProvider),
            new CostAllocationEngine(),
            new CustomsValueCalculator(),
            new RegulatoryRuleEngine(new InMemoryRegulatoryRuleRepository(Array.Empty<RegulatoryRule>())),
            new CurrencyConversionService(rateProvider));
    }

    private static ImportLine BuildLine(decimal quantity, decimal unitPrice, string currency, int lineNumber, string productReference) => new()
    {
        LineNumber = lineNumber,
        ProductReference = productReference,
        Designation = "Article de test Tâche 21",
        Quantity = quantity,
        UnitPurchasePrice = unitPrice,
        CurrencyCode = currency,
        OriginCountryIso2 = "FR"
    };

    private static ImportOperation BuildOperation(
        IReadOnlyList<ImportLine> lines, IReadOnlyList<ImportFee> fees,
        decimal? manualCsRatePercent = null, bool userConfirmedManualCs = false,
        decimal? manualTcsRatePercent = null, bool userConfirmedManualTcs = false) => new()
    {
        CompanyId = Guid.NewGuid(),
        ImportNumber = "IMP-T21-TEST",
        ReferenceDate = new DateOnly(2026, 1, 15),
        SupplierName = "Fournisseur Test Tâche 21",
        ExportShippingCountryIso2 = "FR",
        DefaultOriginCountryIso2 = "FR",
        MainCurrencyCode = "DZD",
        Incoterm = IncotermCode.FOB,
        ArrivalPortOrBorder = "Port d'Alger",
        TransportMode = "Maritime",
        Lines = lines.ToList(),
        Fees = fees.ToList(),
        ManualCsRatePercent = manualCsRatePercent,
        UserConfirmedManualCs = userConfirmedManualCs,
        ManualTcsRatePercent = manualTcsRatePercent,
        UserConfirmedManualTcs = userConfirmedManualTcs
    };

    private static decimal SumTax(ImportCalculationSummary summary, string taxCode) => summary.LineResults
        .SelectMany(l => l.CustomsOutcome.AdditionalTaxes)
        .Where(t => string.Equals(t.TaxCode, taxCode, StringComparison.OrdinalIgnoreCase))
        .Sum(t => t.TaxAmountDzd);

    private static void AssertFreightNeverBecomesAnArticle(ImportOperation operationBefore, ImportCalculationSummary summary, int expectedLineCount)
    {
        Assert.Equal(expectedLineCount, operationBefore.Lines.Count);
        Assert.DoesNotContain(summary.LineResults, l =>
            string.Equals(l.ProductReference, "Fret maritime international (réel)", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expectedLineCount, summary.LineResults.Count);
    }

    // ====================================================================================
    // Point 1 : 4 cas du fret international, montant RÉEL fourni par l'utilisateur (1 845 EUR).
    //
    // Jeu de données commun : 1 article de 732 475,00 DA (purchase), taux EUR -> DZD = 145,0000 (taux de
    // test propre à ce fichier, choisi volontairement rond pour permettre une vérification manuelle exacte
    // de chaque montant), fret de 1 845 EUR = 267 525,00 DA réparti "Par valeur" (ByValue, un seul article
    // -> 100 % du montant). Aucune règle réglementaire -> taux PAR DÉFAUT de l'importation (DD 0 %, CS 3 %,
    // PRCT 2 %, TVA 19 %), Incoterm FOB (fret international requis, conformément à Art. 16 octies CDA).
    // ====================================================================================

    private static readonly ExchangeRateRecord EurToDzdRate = new()
    {
        CurrencyCode = "EUR",
        QuoteCurrencyCode = "DZD",
        RateToDzd = 145.0000m,
        QuotityUnit = 1,
        ValidFrom = new DateOnly(2026, 1, 1),
        RateType = "OFFICIEL_DOUANE_ALCES",
        SourceName = "Test Tâche 21 (taux rond pour vérification manuelle exacte)"
    };

    private static ImportFee BuildRealFreightFee(bool includeInCustomsValue, bool includeInCostOfGoods) => new()
    {
        FeeCategoryCode = "FRET_INTERNATIONAL",
        FeeName = "Fret maritime international (réel)",
        Amount = 1_845m,
        CurrencyCode = "EUR",
        AllocationMethod = FeeAllocationMethod.ByValue,
        IncludeInCustomsValue = includeInCustomsValue,
        IncludeInCostOfGoods = includeInCostOfGoods,
        CustomsTreatment = CustomsAdjustmentTreatment.Addition_Art16Octies
    };

    private static ImportOperation BuildRealFreightOperation(bool includeInCustomsValue, bool includeInCostOfGoods)
    {
        var line = BuildLine(1m, 732_475.00m, "DZD", 1, "ART-FRET-REEL");
        var fee = BuildRealFreightFee(includeInCustomsValue, includeInCostOfGoods);
        return BuildOperation(new[] { line }, new[] { fee });
    }

    [Fact]
    public void Freight_CustomsOnly_CasA_RaisesCustomsValue_NeverDuplicatedAsCostOfGoodsFee()
    {
        // Cas A : ☑ valeur en douane / ☐ coût de revient. 732 475,00 + 267 525,00 (1 845 EUR x 145) = 1 000 000,00.
        var operation = BuildRealFreightOperation(includeInCustomsValue: true, includeInCostOfGoods: false);
        var summary = BuildOrchestrator(new[] { EurToDzdRate }).ExecuteCalculation(BuildCompany(), operation);

        Assert.False(summary.HasBlockingAnomalies);
        Assert.Equal(1_000_000.00m, summary.TotalCustomsValueDzd);
        Assert.Equal(0m, summary.TotalCustomsDutyDzd);
        Assert.Equal(30_000.00m, SumTax(summary, "CS"));
        Assert.Equal(195_700.00m, summary.TotalImportVatDzd);
        Assert.Equal(24_514.00m, SumTax(summary, "PRCT"));
        // Le fret N'EST PAS ajouté une seconde fois comme frais de revient distinct (case décochée).
        Assert.Equal(0m, summary.TotalImportFeesDzd);
        Assert.Equal(982_689.00m, summary.TotalRealCostOfGoodsDzd);

        decimal totalDedouanement = summary.TotalCustomsDutyDzd + SumTax(summary, "CS") + summary.TotalImportVatDzd + SumTax(summary, "PRCT");
        Assert.Equal(250_214.00m, totalDedouanement);

        AssertFreightNeverBecomesAnArticle(operation, summary, expectedLineCount: 1);
    }

    [Fact]
    public void Freight_CostOnly_CasB_NeverAffectsCustomsValue_AddedOnceToCostOfGoods()
    {
        // Cas B : ☐ valeur en douane / ☑ coût de revient. La valeur en douane reste 732 475,00 (achat seul).
        var operation = BuildRealFreightOperation(includeInCustomsValue: false, includeInCostOfGoods: true);
        var summary = BuildOrchestrator(new[] { EurToDzdRate }).ExecuteCalculation(BuildCompany(), operation);

        Assert.False(summary.HasBlockingAnomalies);
        Assert.Equal(732_475.00m, summary.TotalCustomsValueDzd);
        Assert.Equal(0m, summary.TotalCustomsDutyDzd);
        Assert.Equal(21_974.25m, SumTax(summary, "CS"));
        Assert.Equal(143_345.36m, summary.TotalImportVatDzd);
        Assert.Equal(17_955.89m, SumTax(summary, "PRCT"));
        // Le fret (267 525,00) est pris en compte EXACTEMENT UNE FOIS dans le coût de revient.
        Assert.Equal(267_525.00m, summary.TotalImportFeesDzd);
        Assert.Equal(1_183_275.50m, summary.TotalRealCostOfGoodsDzd);

        decimal totalDedouanement = summary.TotalCustomsDutyDzd + SumTax(summary, "CS") + summary.TotalImportVatDzd + SumTax(summary, "PRCT");
        Assert.Equal(183_275.50m, totalDedouanement);

        AssertFreightNeverBecomesAnArticle(operation, summary, expectedLineCount: 1);
    }

    [Fact]
    public void Freight_Both_CasC_SameAmountNeverCountedTwice()
    {
        // Cas C (le cœur de l'audit demandé) : ☑ valeur en douane / ☑ coût de revient. Les 267 525,00 DA
        // (1 845 EUR) ne doivent être comptés QU'UNE SEULE FOIS, même s'ils affectent À LA FOIS la valeur en
        // douane ET le coût de revient.
        var operation = BuildRealFreightOperation(includeInCustomsValue: true, includeInCostOfGoods: true);
        var summary = BuildOrchestrator(new[] { EurToDzdRate }).ExecuteCalculation(BuildCompany(), operation);

        Assert.False(summary.HasBlockingAnomalies);
        // Valeur en douane et taxes dérivées STRICTEMENT IDENTIQUES au Cas A (la case "coût de revient" n'a,
        // par construction, aucun effet sur la valeur en douane ni sur les droits/taxes qui en découlent).
        Assert.Equal(1_000_000.00m, summary.TotalCustomsValueDzd);
        Assert.Equal(0m, summary.TotalCustomsDutyDzd);
        Assert.Equal(30_000.00m, SumTax(summary, "CS"));
        Assert.Equal(195_700.00m, summary.TotalImportVatDzd);
        Assert.Equal(24_514.00m, SumTax(summary, "PRCT"));

        // Preuve explicite de l'absence de double comptage : "Frais alloués" reste le montant BRUT du fret
        // (267 525,00), EXACTEMENT comme au Cas B — jamais 535 050,00 (ce qui serait le cas si le moteur
        // l'ajoutait une deuxième fois en plus de son inclusion dans la valeur en douane).
        Assert.Equal(267_525.00m, summary.TotalImportFeesDzd);

        // Coût de revient = Cas A (982 689,00) + EXACTEMENT 267 525,00 (le fret, une seule fois en plus, via
        // le coût de revient) = 1 250 214,00. Jamais 1 517 739,00 (double ajout du fret).
        Assert.Equal(1_250_214.00m, summary.TotalRealCostOfGoodsDzd);
        Assert.NotEqual(982_689.00m + 267_525.00m + 267_525.00m, summary.TotalRealCostOfGoodsDzd);

        var line = Assert.Single(summary.LineResults);
        var allocation = Assert.Single(line.FeeAllocations, a => a.FeeName == "Fret maritime international (réel)");
        Assert.Equal(267_525.00m, allocation.AllocatedAmountDzd);
        Assert.Equal(267_525.00m, line.EconomicOutcome.AllocatedCustomsIncludedFeesDzd);
        Assert.Equal(0m, line.EconomicOutcome.AllocatedLocalAndPostCustomsFeesDzd);
        Assert.Equal(267_525.00m, line.EconomicOutcome.TotalAllocatedFeesDzd); // Jamais 535 050,00.

        AssertFreightNeverBecomesAnArticle(operation, summary, expectedLineCount: 1);
    }

    [Fact]
    public void Freight_Neither_CasD_HasNoEconomicEffect()
    {
        // Cas D : ☐ valeur en douane / ☐ coût de revient -> AUCUN effet économique du fret.
        var operation = BuildRealFreightOperation(includeInCustomsValue: false, includeInCostOfGoods: false);
        var summary = BuildOrchestrator(new[] { EurToDzdRate }).ExecuteCalculation(BuildCompany(), operation);

        Assert.False(summary.HasBlockingAnomalies);
        Assert.Equal(732_475.00m, summary.TotalCustomsValueDzd);
        Assert.Equal(0m, summary.TotalCustomsDutyDzd);
        Assert.Equal(21_974.25m, SumTax(summary, "CS"));
        Assert.Equal(143_345.36m, summary.TotalImportVatDzd);
        Assert.Equal(17_955.89m, SumTax(summary, "PRCT"));
        Assert.Equal(0m, summary.TotalImportFeesDzd);
        // Identique au Cas B, MOINS le fret (267 525,00).
        Assert.Equal(915_750.50m, summary.TotalRealCostOfGoodsDzd);

        // Même preuve d'absence d'effet qu'au Cas 4 de Task20WeightWarningAndFreightDoubleCountingTests
        // (Tâche #20.1) : au plus une trace technique d'allocation, jamais un effet (ni valeur en douane, ni
        // coût de revient) — Assert.DoesNotContain prouve une ABSENCE D'EFFET, jamais une absence de trace.
        var line = Assert.Single(summary.LineResults);
        var freightTraces = line.FeeAllocations.Where(a => a.FeeName == "Fret maritime international (réel)").ToList();
        Assert.True(freightTraces.Count <= 1, "Au plus une trace d'allocation par (frais, ligne) est attendue.");
        Assert.DoesNotContain(line.FeeAllocations, a =>
            a.FeeName == "Fret maritime international (réel)" && (a.IncludedInCustomsValue || a.IncludedInCostOfGoods));

        AssertFreightNeverBecomesAnArticle(operation, summary, expectedLineCount: 1);
    }

    // ====================================================================================
    // Point 2 : RPS — audit du double comptage avec un cas réel chiffré (RPS = 8 000 DA, Fret = 281 762,13 DA).
    // ====================================================================================

    [Fact]
    public void RPS_NoDoubleCounting_AppearsInDistinctIndicatorsButOnlyOnceInRealCostOfGoods()
    {
        var line = BuildLine(1m, 1_500_000.00m, "DZD", 1, "ART-RPS");
        var freightFee = new ImportFee
        {
            FeeCategoryCode = "FRET_INTERNATIONAL",
            FeeName = "Fret maritime international",
            Amount = 281_762.13m,
            CurrencyCode = "DZD",
            AllocationMethod = FeeAllocationMethod.ByValue,
            IncludeInCustomsValue = true,
            IncludeInCostOfGoods = true,
            CustomsTreatment = CustomsAdjustmentTreatment.Addition_Art16Octies
        };
        var rpsFee = new ImportFee
        {
            FeeCategoryCode = "RPS",
            FeeName = "Redevance de Prestation de Service (RPS)",
            Amount = 8_000.00m,
            CurrencyCode = "DZD",
            AllocationMethod = FeeAllocationMethod.FixedAmount,
            IncludeInCustomsValue = false,
            IncludeInCostOfGoods = true
        };
        var operation = BuildOperation(new[] { line }, new[] { freightFee, rpsFee });

        var summary = BuildOrchestrator().ExecuteCalculation(BuildCompany(), operation);

        Assert.False(summary.HasBlockingAnomalies);
        Assert.Equal(1_781_762.13m, summary.TotalCustomsValueDzd); // 1 500 000,00 + 281 762,13 (fret, valeur en douane).
        Assert.Equal(53_452.86m, SumTax(summary, "CS"));
        Assert.Equal(348_690.85m, summary.TotalImportVatDzd);
        Assert.Equal(43_678.12m, SumTax(summary, "PRCT"));

        // Indicateur 1 (équivalent ImportDetailViewModel.TotalRpsDzd, Section 9) : somme des allocations du
        // frais RPS — 8 000,00, strictement isolée.
        var rpsFeeIds = new HashSet<Guid> { rpsFee.Id };
        decimal totalRpsDzd = summary.LineResults
            .SelectMany(l => l.FeeAllocations)
            .Where(a => rpsFeeIds.Contains(a.FeeId))
            .Sum(a => a.AllocatedAmountDzd);
        Assert.Equal(8_000.00m, totalRpsDzd);

        // Indicateur 2 (équivalent ImportDetailViewModel.TotalFraisDzd = summary.TotalImportFeesDzd,
        // "Frais alloués") : INCLUT le RPS (8 000,00) + le fret (281 762,13) = 289 762,13 — le RPS en FAIT
        // PARTIE, il n'est pas une addition séparée.
        Assert.Equal(289_762.13m, summary.TotalImportFeesDzd);
        Assert.Equal(281_762.13m, summary.TotalImportFeesDzd - totalRpsDzd); // Part du fret isolée par soustraction.

        // Indicateur 3 (équivalent ImportDetailViewModel.TotalDedouanementDzd, Section 9 : "DD + CS + TVA +
        // PRCT + TCS + RPS") : DISTINCT de "Frais alloués" ci-dessus — RPS y apparaît À NOUVEAU, légitimement
        // (indicateur différent), jamais sommé avec "Frais alloués" nulle part dans le modèle/l'UI.
        decimal totalDedouanementCore = summary.TotalCustomsDutyDzd + SumTax(summary, "CS") + summary.TotalImportVatDzd + SumTax(summary, "PRCT") + SumTax(summary, "TCS");
        decimal totalDedouanementWithRps = totalDedouanementCore + totalRpsDzd;
        Assert.Equal(445_821.83m, totalDedouanementCore);
        Assert.Equal(453_821.83m, totalDedouanementWithRps);
        Assert.NotEqual(summary.TotalImportFeesDzd, totalDedouanementWithRps); // Deux indicateurs réellement distincts.

        // Indicateur 4 — LE SEUL total économique réel agrégeant tout (COÛT DE REVIENT) : le RPS n'y est
        // compté QU'UNE SEULE FOIS, jamais une seconde fois en plus de son inclusion dans "Frais alloués".
        Assert.Equal(2_235_583.96m, summary.TotalRealCostOfGoodsDzd);

        // Preuve explicite anti-régression : si le RPS était (à tort) compté une deuxième fois quelque part
        // dans le coût de revient réel, celui-ci vaudrait TotalRealCostOfGoodsDzd + 8 000,00 — ce n'est
        // JAMAIS le cas ici (la valeur réellement calculée ci-dessus, 2 235 583,96, est strictement
        // inférieure à cette hypothèse de double comptage).
        decimal realCostIfRpsWereDoubleCounted = summary.TotalRealCostOfGoodsDzd + totalRpsDzd;
        Assert.NotEqual(realCostIfRpsWereDoubleCounted, summary.TotalRealCostOfGoodsDzd);

        // Traçabilité mathématique complète (purchase + frais alloués (RPS inclus) + DD + CS + TCS + PRCT + TVA) :
        decimal reconstructedRealCost = line.Quantity * line.UnitPurchasePrice
            + summary.TotalImportFeesDzd
            + summary.TotalCustomsDutyDzd
            + SumTax(summary, "CS") + SumTax(summary, "TCS") + SumTax(summary, "PRCT")
            + summary.TotalImportVatDzd;
        Assert.Equal(summary.TotalRealCostOfGoodsDzd, reconstructedRealCost);
    }

    // ====================================================================================
    // Point 4 : filtrage des notifications ("Voir les détails") par groupe sélectionné
    // (AnomalyGroupingService.FilterByAnomalyCode — méthode pure utilisée par
    // ImportDetailViewModel.RefreshFilteredAnomalies). AnomalyGroupingService.GroupByCode n'est PAS modifié.
    // ====================================================================================

    private static CalculationAnomaly MakeAnomaly(string code, int lineNumber, AnomalySeverity severity = AnomalySeverity.Avertissement) =>
        new(severity, code, $"Message de test pour {code} (ligne {lineNumber}).", LineNumber: lineNumber);

    [Fact]
    public void NotificationDetails_FilterBySelectedGroup_NoSelection_ReturnsAllAnomalies()
    {
        var anomalies = new List<CalculationAnomaly>
        {
            MakeAnomaly("EXCEL_DUTY_RATE_SUSPICIOUSLY_LOW", 1),
            MakeAnomaly("EXCEL_DUTY_RATE_SUSPICIOUSLY_LOW", 2),
            MakeAnomaly("CS_DEFAULT_RATE_USED", 3)
        };

        // Aucune sélection (code null) -> "Voir les détails" affiche TOUTES les anomalies (comportement
        // d'origine, inchangé).
        var filtered = AnomalyGroupingService.FilterByAnomalyCode(anomalies, selectedAnomalyCode: null);

        Assert.Equal(3, filtered.Count);
        Assert.Same(anomalies, filtered); // Aucune copie/filtrage inutile lorsqu'il n'y a rien à filtrer.
    }

    [Fact]
    public void NotificationDetails_FilterBySelectedGroup_WithSelection_ReturnsOnlyMatchingCode()
    {
        // Exemple directement repris de la demande utilisateur : 8 occurrences de
        // EXCEL_DUTY_RATE_SUSPICIOUSLY_LOW parmi d'autres anomalies -> la sélection de ce groupe dans
        // AnomalyGroups ne doit laisser apparaître QUE ces 8 occurrences dans le détail.
        var anomalies = new List<CalculationAnomaly>();
        for (int i = 1; i <= 8; i++)
            anomalies.Add(MakeAnomaly("EXCEL_DUTY_RATE_SUSPICIOUSLY_LOW", i));
        anomalies.Add(MakeAnomaly("CS_DEFAULT_RATE_USED", 9));
        anomalies.Add(MakeAnomaly("PRCT_DEFAULT_RATE_USED", 10));

        // AnomalyGroupingService.GroupByCode (INCHANGÉ) permet de retrouver le groupe correspondant, comme
        // le ferait réellement la sélection d'une ligne de AnomalyGroups dans l'écran Notifications.
        var groups = AnomalyGroupingService.GroupByCode(anomalies);
        var selectedGroup = Assert.Single(groups, g => g.AnomalyCode == "EXCEL_DUTY_RATE_SUSPICIOUSLY_LOW");
        Assert.Equal(8, selectedGroup.OccurrenceCount);

        var filtered = AnomalyGroupingService.FilterByAnomalyCode(anomalies, selectedGroup.AnomalyCode);

        Assert.Equal(8, filtered.Count);
        Assert.All(filtered, a => Assert.Equal("EXCEL_DUTY_RATE_SUSPICIOUSLY_LOW", a.AnomalyCode));
        Assert.DoesNotContain(filtered, a => a.AnomalyCode == "CS_DEFAULT_RATE_USED");
        Assert.DoesNotContain(filtered, a => a.AnomalyCode == "PRCT_DEFAULT_RATE_USED");

        // La liste BRUTE d'origine reste intégralement disponible/inchangée (rien n'est supprimé) — seule
        // une PROJECTION filtrée est retournée.
        Assert.Equal(10, anomalies.Count);
    }

    [Fact]
    public void NotificationDetails_FilterBySelectedGroup_UnknownCode_ReturnsEmpty_NeverThrows()
    {
        var anomalies = new List<CalculationAnomaly> { MakeAnomaly("CS_DEFAULT_RATE_USED", 1) };

        var filtered = AnomalyGroupingService.FilterByAnomalyCode(anomalies, "CODE_INEXISTANT");

        Assert.Empty(filtered);
    }

    // ====================================================================================
    // Point 5 : la confirmation manuelle CS remplace l'ancienne confirmation manuelle TCS dans l'écran V1,
    // sans créer de second champ CS concurrent, sans casser le champ historique TCS, et UNE SEULE fois par
    // import (jamais par article).
    // ====================================================================================

    [Fact]
    public void ManualCsRate_ReplacesManualTcsUi_FeedsTheSameCsCalculation_OncePerImportNotPerLine()
    {
        var lineA = BuildLine(1m, 100_000m, "DZD", 1, "ART-A");
        var lineB = BuildLine(1m, 200_000m, "DZD", 2, "ART-B");

        // UNE SEULE confirmation, au niveau de l'ImportOperation (jamais par ImportLine) : 7,5 % CS manuel.
        var operation = BuildOperation(
            new[] { lineA, lineB }, Array.Empty<ImportFee>(),
            manualCsRatePercent: 7.5m, userConfirmedManualCs: true);

        var summary = BuildOrchestrator().ExecuteCalculation(BuildCompany(), operation);

        // Les DEUX lignes reçoivent le MÊME taux CS manuel (7,5 %), à partir d'une seule confirmation import,
        // jamais une saisie répétée par article.
        var resultA = summary.LineResults.Single(l => l.ProductReference == "ART-A");
        var resultB = summary.LineResults.Single(l => l.ProductReference == "ART-B");
        var csA = resultA.CustomsOutcome.AdditionalTaxes.Single(t => t.TaxCode == "CS");
        var csB = resultB.CustomsOutcome.AdditionalTaxes.Single(t => t.TaxCode == "CS");
        Assert.Equal(7.5m, csA.RatePercent);
        Assert.Equal(7.5m, csB.RatePercent);
        Assert.Equal(7_500.00m, csA.TaxAmountDzd);   // 7,5 % x 100 000.
        Assert.Equal(15_000.00m, csB.TaxAmountDzd);  // 7,5 % x 200 000.
        Assert.Equal("MANUEL", csA.RegulatoryRuleCode);
        Assert.Equal(DataOriginTag.DonneeUtilisateur, csA.OriginTag);
        Assert.Contains(summary.Anomalies, a => a.AnomalyCode == "CS_MANUAL_RATE_USED" && a.LineNumber == 1);
        Assert.Contains(summary.Anomalies, a => a.AnomalyCode == "CS_MANUAL_RATE_USED" && a.LineNumber == 2);

        // Sans confirmation manuelle CS, c'est le taux CS PAR DÉFAUT de l'importation qui s'applique
        // (comportement préexistant, inchangé) — preuve que le nouveau champ ne fait que s'INSÉRER dans la
        // cascade existante, sans la casser.
        var operationWithoutManualCs = BuildOperation(new[] { lineA, lineB }, Array.Empty<ImportFee>());
        var summaryWithoutManualCs = BuildOrchestrator().ExecuteCalculation(BuildCompany(), operationWithoutManualCs);
        var csADefault = summaryWithoutManualCs.LineResults.Single(l => l.ProductReference == "ART-A")
            .CustomsOutcome.AdditionalTaxes.Single(t => t.TaxCode == "CS");
        Assert.Equal(3.0m, csADefault.RatePercent); // DefaultCsRatePercent (valeur par défaut du modèle).
        Assert.Equal("DEFAULT_IMPORT", csADefault.RegulatoryRuleCode);
    }

    [Fact]
    public void ManualCsRate_IsIndependentFromLegacyManualTcsField_NoCompetingSecondCsField()
    {
        var line = BuildLine(1m, 100_000m, "DZD", 1, "ART-A");

        // Les DEUX champs manuels (CS, nouveau — et TCS, historique conservé pour compatibilité SQLite) sont
        // renseignés SIMULTANÉMENT avec des taux DIFFÉRENTS : ils doivent alimenter deux taxes DISTINCTES
        // (TaxCode "CS" et TaxCode "TCS"), sans jamais se substituer l'un à l'autre ni se mélanger — preuve
        // qu'il ne s'agit pas d'un second champ CS concurrent.
        var operation = BuildOperation(
            new[] { line }, Array.Empty<ImportFee>(),
            manualCsRatePercent: 7.5m, userConfirmedManualCs: true,
            manualTcsRatePercent: 99m, userConfirmedManualTcs: true);

        var summary = BuildOrchestrator().ExecuteCalculation(BuildCompany(), operation);

        var result = Assert.Single(summary.LineResults);
        var cs = result.CustomsOutcome.AdditionalTaxes.Single(t => t.TaxCode == "CS");
        var tcs = result.CustomsOutcome.AdditionalTaxes.Single(t => t.TaxCode == "TCS");

        Assert.Equal(7.5m, cs.RatePercent);
        Assert.Equal(7_500.00m, cs.TaxAmountDzd);
        Assert.Equal(99m, tcs.RatePercent);     // Le champ TCS historique continue de fonctionner isolément...
        Assert.Equal(99_000.00m, tcs.TaxAmountDzd);
        Assert.NotEqual(cs.TaxAmountDzd, tcs.TaxAmountDzd); // ...jamais mélangé avec le nouveau champ CS.
    }
}
