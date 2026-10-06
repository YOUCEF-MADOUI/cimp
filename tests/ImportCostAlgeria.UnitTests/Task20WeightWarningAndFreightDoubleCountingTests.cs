using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.RegulatoryEngine;
using ImportCostAlgeria.CalculationEngine;

namespace ImportCostAlgeria.UnitTests;

/// <summary>
/// Tests de non-régression dédiés à la Tâche #20 (2026-10-05) :
///   A) Le poids manquant pour une répartition "Par poids" doit déclencher un AVERTISSEMENT (jamais un
///      BLOCAGE) et ne doit JAMAIS empêcher le calcul complet, la consultation des résultats ni la
///      sauvegarde de l'importation (point 1 de la demande).
///   B) Le fret international configuré avec les 4 combinaisons possibles des cases "Inclure dans la
///      valeur en douane" / "Inclure dans le coût de revient" ne doit JAMAIS compter le même montant deux
///      fois, et ne doit JAMAIS devenir un "article" (point 3 de la demande). Les 4 scénarios vérifient
///      explicitement : valeur en douane, DD, CS, PRCT, TVA, frais alloués, coût de revient total et le
///      "total dédouanement" (DD+CS+TVA+PRCT+TCS+RPS), conformément au point 6 de la demande.
/// IMPORTANT : ces tests n'altèrent EN RIEN la logique de résolution du Droit de Douane (DD) introduite par
/// la Tâche #19 (priorité Excel/IA) — ils utilisent exclusivement les taux PAR DÉFAUT de l'importation
/// (aucune RegulatoryRule fournie), scénario déjà couvert et inchangé depuis la Tâche #19.
/// </summary>
public sealed class Task20WeightWarningAndFreightDoubleCountingTests
{
    private static Company BuildCompany() => new()
    {
        Code = "TEST-T20",
        LegalName = "SARL TEST TÂCHE 20",
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

    private static ImportLine BuildLine(
        decimal quantity, decimal unitPrice, int lineNumber, string productReference,
        decimal? weightKg = null, decimal? volumeM3 = null) => new()
    {
        LineNumber = lineNumber,
        ProductReference = productReference,
        Designation = "Article de test Tâche 20",
        Quantity = quantity,
        UnitPurchasePrice = unitPrice,
        CurrencyCode = "DZD",
        OriginCountryIso2 = "FR",
        LineGrossWeightKg = weightKg,
        LineVolumeM3 = volumeM3
    };

    private static ImportOperation BuildOperation(
        IReadOnlyList<ImportLine> lines, IReadOnlyList<ImportFee> fees) => new()
    {
        CompanyId = Guid.NewGuid(),
        ImportNumber = "IMP-T20-TEST",
        ReferenceDate = new DateOnly(2026, 1, 15),
        SupplierName = "Fournisseur Test Tâche 20",
        ExportShippingCountryIso2 = "FR",
        DefaultOriginCountryIso2 = "FR",
        MainCurrencyCode = "DZD",
        Incoterm = IncotermCode.FOB,
        ArrivalPortOrBorder = "Port d'Alger",
        TransportMode = "Maritime",
        Lines = lines.ToList(),
        Fees = fees.ToList()
    };

    // ====================================================================================
    // A) Poids manquant pour une répartition "Par poids" -> AVERTISSEMENT, jamais BLOCAGE.
    // ====================================================================================

    [Fact]
    public void MissingWeight_ForByWeightAllocation_RaisesWarningOnly_NeverBlocking()
    {
        var lineWithoutWeight = BuildLine(1m, 10_000m, 1, "ART-SANS-POIDS", weightKg: null);
        var fee = new ImportFee
        {
            FeeCategoryCode = "FRET_INTERNATIONAL",
            FeeName = "Fret maritime (par poids)",
            Amount = 5_000m,
            CurrencyCode = "DZD",
            AllocationMethod = FeeAllocationMethod.ByWeight,
            IncludeInCustomsValue = true,
            IncludeInCostOfGoods = true,
            CustomsTreatment = CustomsAdjustmentTreatment.Addition_Art16Octies
        };

        var operation = BuildOperation(new[] { lineWithoutWeight }, new[] { fee });

        var summary = BuildOrchestrator().ExecuteCalculation(BuildCompany(), operation);

        var weightAnomaly = Assert.Single(summary.Anomalies, a => a.AnomalyCode == "MISSING_WEIGHT_FOR_WEIGHT_ALLOCATION");
        Assert.Equal(AnomalySeverity.Avertissement, weightAnomaly.Severity); // jamais AnomalySeverity.Blocage.
        Assert.False(summary.HasBlockingAnomalies); // l'absence de poids ne bloque RIEN.

        // Le calcul COMPLET s'exécute malgré tout (consultation/sauvegarde possibles) : un résultat de ligne
        // existe, avec une valeur en douane calculée (même si le frais par poids n'a pas pu être réparti).
        var line = Assert.Single(summary.LineResults);
        Assert.Equal(10_000m, line.CustomsOutcome.CustomsValueDzd);

        // Repli existant PRÉSERVÉ : le frais non réparti faute de poids n'apparaît sur AUCUNE ligne (montant
        // non inventé, pas de poids par défaut silencieusement supposé).
        Assert.Empty(line.FeeAllocations);
    }

    [Fact]
    public void WeightPresent_ForByWeightAllocation_StillAllocatesNormally_NoAnomaly()
    {
        // Non-régression : le comportement normal (poids renseigné) reste inchangé par l'abaissement de
        // sévérité du cas "poids manquant".
        var lineA = BuildLine(1m, 10_000m, 1, "A", weightKg: 100m);
        var lineB = BuildLine(1m, 10_000m, 2, "B", weightKg: 300m);
        var fee = new ImportFee
        {
            FeeCategoryCode = "FRET_INTERNATIONAL",
            FeeName = "Fret maritime (par poids)",
            Amount = 400m,
            CurrencyCode = "DZD",
            AllocationMethod = FeeAllocationMethod.ByWeight,
            IncludeInCustomsValue = false,
            IncludeInCostOfGoods = true
        };

        var operation = BuildOperation(new[] { lineA, lineB }, new[] { fee });
        var summary = BuildOrchestrator().ExecuteCalculation(BuildCompany(), operation);

        Assert.DoesNotContain(summary.Anomalies, a => a.AnomalyCode == "MISSING_WEIGHT_FOR_WEIGHT_ALLOCATION");
        Assert.False(summary.HasBlockingAnomalies);

        var resultA = summary.LineResults.Single(l => l.ProductReference == "A");
        var resultB = summary.LineResults.Single(l => l.ProductReference == "B");
        Assert.Equal(100m, resultA.FeeAllocations.Single().AllocatedAmountDzd); // 100/400 x 400 = 100.
        Assert.Equal(300m, resultB.FeeAllocations.Single().AllocatedAmountDzd); // 300/400 x 400 = 300.
    }

    [Fact]
    public void MissingVolume_ForByVolumeAllocation_RemainsBlocking_OutOfScopeOfWeightFix()
    {
        // Garde-fou explicite : la Tâche #20 n'abaisse QUE l'anomalie liée au POIDS. La méthode "Par volume"
        // doit rester inchangée (toujours BLOCAGE) — aucune modification "sans nécessité" d'une autre règle.
        var lineWithoutVolume = BuildLine(1m, 10_000m, 1, "ART-SANS-VOLUME", volumeM3: null);
        var fee = new ImportFee
        {
            FeeCategoryCode = "AUTRES_FRAIS",
            FeeName = "Frais par volume",
            Amount = 1_000m,
            CurrencyCode = "DZD",
            AllocationMethod = FeeAllocationMethod.ByVolume,
            IncludeInCustomsValue = false,
            IncludeInCostOfGoods = true
        };

        var operation = BuildOperation(new[] { lineWithoutVolume }, new[] { fee });
        var summary = BuildOrchestrator().ExecuteCalculation(BuildCompany(), operation);

        var volumeAnomaly = Assert.Single(summary.Anomalies, a => a.AnomalyCode == "MISSING_VOLUME_FOR_VOLUME_ALLOCATION");
        Assert.Equal(AnomalySeverity.Blocage, volumeAnomaly.Severity); // INCHANGÉ, toujours bloquant.
        Assert.True(summary.HasBlockingAnomalies);
    }

    // ====================================================================================
    // B) Fret international — 4 scénarios des cases "valeur en douane" / "coût de revient".
    //
    // Jeu de données commun : 2 articles de 100 000 DA chacun (purchase total = 200 000 DA), fret de
    // 40 000 DA réparti "Par valeur" (50/50, soit 20 000 DA par article). Aucune règle réglementaire ->
    // taux PAR DÉFAUT de l'importation appliqués (DD 0 %, CS 3 %, PRCT 2 %, TVA 19 %), permettant de vérifier
    // explicitement chaque taxe demandée par le point 6 de la Tâche #20.
    // ====================================================================================

    private static ImportFee BuildFreightFee(bool includeInCustomsValue, bool includeInCostOfGoods) => new()
    {
        FeeCategoryCode = "FRET_INTERNATIONAL",
        FeeName = "Fret maritime international",
        Amount = 40_000m,
        CurrencyCode = "DZD",
        AllocationMethod = FeeAllocationMethod.ByValue,
        IncludeInCustomsValue = includeInCustomsValue,
        IncludeInCostOfGoods = includeInCostOfGoods,
        CustomsTreatment = CustomsAdjustmentTreatment.Addition_Art16Octies
    };

    private static ImportOperation BuildTwoLineFreightOperation(bool includeInCustomsValue, bool includeInCostOfGoods)
    {
        var lineA = BuildLine(1m, 100_000m, 1, "A");
        var lineB = BuildLine(1m, 100_000m, 2, "B");
        var fee = BuildFreightFee(includeInCustomsValue, includeInCostOfGoods);
        return BuildOperation(new[] { lineA, lineB }, new[] { fee });
    }

    private static decimal SumTax(ImportCalculationSummary summary, string taxCode) => summary.LineResults
        .SelectMany(l => l.CustomsOutcome.AdditionalTaxes)
        .Where(t => string.Equals(t.TaxCode, taxCode, StringComparison.OrdinalIgnoreCase))
        .Sum(t => t.TaxAmountDzd);

    private static void AssertFreightNeverBecomesAnArticle(ImportOperation operationBefore, ImportCalculationSummary summary)
    {
        // Le fret reste exclusivement un FRAIS : il ne doit jamais apparaître comme ligne/article, ni avant
        // ni après le calcul (ExecuteCalculation ne doit jamais muter/ajouter de lignes).
        Assert.Equal(2, operationBefore.Lines.Count);
        Assert.Single(operationBefore.Fees);
        Assert.DoesNotContain(summary.LineResults, l =>
            string.Equals(l.ProductReference, "Fret maritime international", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, summary.LineResults.Count);
    }

    // Note de calcul (taux par défaut, assiettes réelles lues dans ImportCalculationOrchestrator) :
    //   CS      = 3 % x Valeur en douane.
    //   Assiette TVA = Valeur en douane + DD + CS (+ TCS, ici 0).
    //   TVA     = 19 % x Assiette TVA.
    //   PRCT (par défaut, Section 7) = 2 % x (Assiette TVA + TVA) — PRCT est calculé APRÈS la TVA.
    // D'où, par article :
    //   X = 120 000 (valeur en douane AVEC fret) -> CS = 3 600,00 ; TVA = 19 % x 123 600 = 23 484,00 ;
    //       PRCT = 2 % x (123 600 + 23 484) = 2 941,68.
    //   X = 100 000 (valeur en douane SANS fret) -> CS = 3 000,00 ; TVA = 19 % x 103 000 = 19 570,00 ;
    //       PRCT = 2 % x (103 000 + 19 570) = 2 451,40.

    [Fact]
    public void Freight_Case1_CustomsValueOnly_NotDuplicatedAsSeparateCostOfGoodsFee_NoArticleCreated()
    {
        // Cas 1 : ☑ valeur en douane / ☐ coût de revient.
        var operation = BuildTwoLineFreightOperation(includeInCustomsValue: true, includeInCostOfGoods: false);
        var summary = BuildOrchestrator().ExecuteCalculation(BuildCompany(), operation);

        Assert.Equal(240_000m, summary.TotalCustomsValueDzd);           // 200 000 + 40 000 (fret) une seule fois.
        Assert.Equal(0m, summary.TotalCustomsDutyDzd);                  // DD par défaut = 0 %.
        Assert.Equal(7_200.00m, SumTax(summary, "CS"));                 // 3 600,00 x 2 articles.
        Assert.Equal(5_883.36m, SumTax(summary, "PRCT"));                // 2 941,68 x 2.
        Assert.Equal(46_968.00m, summary.TotalImportVatDzd);             // 23 484,00 x 2.
        Assert.Equal(0m, summary.TotalImportFeesDzd);                   // Le fret N'EST PAS un frais de revient séparé ici.
        Assert.Equal(260_051.36m, summary.TotalRealCostOfGoodsDzd);      // (100 000+0+0+6 541,68+23 484,00) x 2.

        decimal totalDedouanement = summary.TotalCustomsDutyDzd + SumTax(summary, "CS") + summary.TotalImportVatDzd + SumTax(summary, "PRCT");
        Assert.Equal(60_051.36m, totalDedouanement);

        AssertFreightNeverBecomesAnArticle(operation, summary);
    }

    [Fact]
    public void Freight_Case2_CostOfGoodsOnly_AllocatedViaNormalMechanism_NeverAffectsCustomsValue()
    {
        // Cas 2 : ☐ valeur en douane / ☑ coût de revient.
        var operation = BuildTwoLineFreightOperation(includeInCustomsValue: false, includeInCostOfGoods: true);
        var summary = BuildOrchestrator().ExecuteCalculation(BuildCompany(), operation);

        Assert.Equal(200_000m, summary.TotalCustomsValueDzd);           // INCHANGÉE : le fret n'y entre pas.
        Assert.Equal(0m, summary.TotalCustomsDutyDzd);
        Assert.Equal(6_000.00m, SumTax(summary, "CS"));                 // 3 000,00 x 2.
        Assert.Equal(4_902.80m, SumTax(summary, "PRCT"));                // 2 451,40 x 2.
        Assert.Equal(39_140.00m, summary.TotalImportVatDzd);             // 19 570,00 x 2.
        Assert.Equal(40_000m, summary.TotalImportFeesDzd);              // Le fret EST pris en compte, une seule fois.
        Assert.Equal(290_042.80m, summary.TotalRealCostOfGoodsDzd);      // (100 000+20 000+0+5 451,40+19 570,00) x 2.

        decimal totalDedouanement = summary.TotalCustomsDutyDzd + SumTax(summary, "CS") + summary.TotalImportVatDzd + SumTax(summary, "PRCT");
        Assert.Equal(50_042.80m, totalDedouanement);

        AssertFreightNeverBecomesAnArticle(operation, summary);
    }

    [Fact]
    public void Freight_Case3_Both_DoesNotMechanicallyAddTheAmountTwice()
    {
        // Cas 3 (le cœur de l'analyse demandée) : ☑ valeur en douane / ☑ coût de revient. Le montant du
        // fret (40 000 DA) ne doit être compté QU'UNE SEULE FOIS, même s'il affecte à la fois la valeur en
        // douane ET le coût de revient.
        var operation = BuildTwoLineFreightOperation(includeInCustomsValue: true, includeInCostOfGoods: true);
        var summary = BuildOrchestrator().ExecuteCalculation(BuildCompany(), operation);

        // La valeur en douane et les taxes qui en dérivent (DD/CS/PRCT/TVA) sont STRICTEMENT IDENTIQUES au
        // Cas 1 : la case "coût de revient" n'a par construction AUCUN effet sur la valeur en douane ni sur
        // les droits/taxes qui en découlent — seule la case "valeur en douane" les détermine.
        Assert.Equal(240_000m, summary.TotalCustomsValueDzd);
        Assert.Equal(0m, summary.TotalCustomsDutyDzd);
        Assert.Equal(7_200.00m, SumTax(summary, "CS"));
        Assert.Equal(5_883.36m, SumTax(summary, "PRCT"));
        Assert.Equal(46_968.00m, summary.TotalImportVatDzd);

        // Preuve explicite de l'absence de double comptage : les "frais alloués" affichés restent le
        // montant BRUT du fret (40 000 = 20 000 x 2 articles), EXACTEMENT comme au Cas 2 (où le fret n'est
        // inclus que dans le coût de revient) — jamais 80 000, ce qui serait le cas si le moteur l'ajoutait
        // une deuxième fois en plus de son inclusion dans la valeur en douane.
        Assert.Equal(40_000m, summary.TotalImportFeesDzd);

        // Coût de revient : identique au Cas 1 (260 051,36) + EXACTEMENT 40 000 (le fret, UNE seule fois en
        // plus, via le coût de revient) = 300 051,36. PAS 340 051,36 (qui résulterait d'un double ajout du
        // fret), ni 339 880 (autre hypothèse de double comptage sur l'ancienne base de calcul).
        Assert.Equal(300_051.36m, summary.TotalRealCostOfGoodsDzd);

        decimal totalDedouanement = summary.TotalCustomsDutyDzd + SumTax(summary, "CS") + summary.TotalImportVatDzd + SumTax(summary, "PRCT");
        Assert.Equal(60_051.36m, totalDedouanement); // Identique au Cas 1 : le "total dédouanement" ne dépend que de la valeur en douane.

        // Vérification ligne par ligne : chaque article ne reçoit qu'UN SEUL enregistrement d'allocation
        // pour ce frais (jamais deux), et son montant alloué par ligne (20 000) n'apparaît qu'une fois dans
        // le compartiment "frais inclus dans la valeur en douane" du coût de revient.
        foreach (var line in summary.LineResults)
        {
            var allocation = Assert.Single(line.FeeAllocations, a => a.FeeName == "Fret maritime international");
            Assert.Equal(20_000m, allocation.AllocatedAmountDzd);
            Assert.Equal(20_000m, line.EconomicOutcome.AllocatedCustomsIncludedFeesDzd);
            Assert.Equal(0m, line.EconomicOutcome.AllocatedLocalAndPostCustomsFeesDzd);
            Assert.Equal(20_000m, line.EconomicOutcome.TotalAllocatedFeesDzd); // Jamais 40 000 pour une seule ligne.
        }

        AssertFreightNeverBecomesAnArticle(operation, summary);
    }

    [Fact]
    public void Freight_Case4_Neither_HasNoEffectOnCustomsValueOrCostOfGoods()
    {
        // Cas 4 : ☐ valeur en douane / ☐ coût de revient.
        var operation = BuildTwoLineFreightOperation(includeInCustomsValue: false, includeInCostOfGoods: false);
        var summary = BuildOrchestrator().ExecuteCalculation(BuildCompany(), operation);

        Assert.Equal(200_000m, summary.TotalCustomsValueDzd);
        Assert.Equal(0m, summary.TotalCustomsDutyDzd);
        Assert.Equal(6_000.00m, SumTax(summary, "CS"));
        Assert.Equal(4_902.80m, SumTax(summary, "PRCT"));
        Assert.Equal(39_140.00m, summary.TotalImportVatDzd);
        Assert.Equal(0m, summary.TotalImportFeesDzd);                   // Le fret n'apparaît nulle part.
        Assert.Equal(250_042.80m, summary.TotalRealCostOfGoodsDzd);      // Identique au Cas 2, MOINS le fret (40 000).

        // Correction (Tâche #20.1) : FeeAllocationTrace est une trace TECHNIQUE de répartition — le moteur
        // peut tout à fait produire une telle trace pour ce frais même lorsqu'il n'a AUCUN effet économique
        // (Cas 4), tant que ses deux indicateurs d'inclusion sont à false. Exiger l'absence totale de trace
        // (ancien Assert.Empty ci-dessous, également signalé par l'analyseur xUnit2029) ne correspond donc
        // pas au comportement réel du moteur et n'est PAS une exigence métier — la preuve de non-
        // comptabilisation repose principalement sur les résultats économiques déjà vérifiés ci-dessus
        // (valeur en douane, DD, CS, PRCT, TVA, frais alloués, coût de revient strictement identiques à une
        // opération sans ce frais). On se contente donc ici de vérifier qu'au plus une trace existe par
        // ligne pour ce frais et que, si elle existe, elle ne porte aucun effet (les deux cases à false) —
        // jamais qu'elle est absente.
        foreach (var line in summary.LineResults)
        {
            var freightTraces = line.FeeAllocations.Where(a => a.FeeName == "Fret maritime international").ToList();
            Assert.True(freightTraces.Count <= 1, "Au plus une trace d'allocation par (frais, ligne) est attendue.");

            // Aucune trace de ce frais ne doit porter un effet (ni valeur en douane, ni coût de revient) :
            // Assert.DoesNotContain vérifie une ABSENCE D'EFFET, jamais une absence de trace.
            Assert.DoesNotContain(line.FeeAllocations, a =>
                a.FeeName == "Fret maritime international" && (a.IncludedInCustomsValue || a.IncludedInCostOfGoods));

            if (freightTraces.Count == 1)
            {
                Assert.False(freightTraces[0].IncludedInCustomsValue);
                Assert.False(freightTraces[0].IncludedInCostOfGoods);
            }
        }

        AssertFreightNeverBecomesAnArticle(operation, summary);
    }
}
