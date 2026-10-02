using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.RegulatoryEngine;
using ImportCostAlgeria.CalculationEngine;

namespace ImportCostAlgeria.UnitTests;

/// <summary>
/// Revue du 2026-10-02 : cas de référence OBLIGATOIRE basé sur un D10 réel (liquidation douanière
/// algérienne) fourni par l'utilisateur :
///   - Importateur : SARL HYMA TRADE — Fournisseur : ZELOSO SUSTENTAVEL LDA
///   - Pays d'achat : Portugal (PT) — Origine marchandise : Allemagne (DE) — Provenance : France (FR)
///   - Incoterm CFR, devise EUR, PTFN = 63 865,29 EUR, Fret = 690 EUR (déjà inclus dans le prix CFR,
///     jamais ajouté une seconde fois), Assurance = 0 EUR, taux douanier = 150,7166.
///   - 5 articles, Valeur en douane totale déclarée = 9 625 557 DZD.
///   - DD = 0 %, CS = 3 % (assiette = Valeur douane), TVA = 19 % (assiette = Valeur douane + DD + CS),
///     PRCT = 2 % (assiette = Valeur douane + CS + TVA), RPS = 2 500 DZD (redevance forfaitaire).
///   - Récapitulatif observé sur le D10 : RPS 2 500 + PRCT 235 960 + CS 288 766 + TVA 1 883 721 + DD 0
///     = 2 410 947 DZD.
/// Les 5 lignes ci-dessous reconstituent, à partir des 5 valeurs en douane PAR ARTICLE publiées sur le
/// D10 (2 079 857 / 3 690 748 / 1 962 480 / 1 756 715 / 135 757 DZD), le prix unitaire EUR correspondant
/// (valeur en douane / taux, arrondi à 2 décimales) : le moteur n'est JAMAIS nourri avec un total déjà
/// calculé — il recalcule intégralement Valeur en douane -> CS -> TVA -> PRCT à partir du prix et du taux,
/// exactement comme en production. De légers écarts d'arrondi (quelques dizaines de centimes, jamais plus
/// de quelques dizaines de DZD sur les totaux) sont normaux et acceptés (tolérances raisonnables).
/// </summary>
public sealed class D10ReferenceCaseTests
{
    private static readonly LegalSource D10LegalSource = new()
    {
        HierarchyLevel = LegalSourceHierarchyLevel.Level1_JournalOfficielJora,
        OfficialTitle = "Tarif Douanier Officiel Algérie — Accord préférentiel Algérie-UE (jeu de test D10)",
        JoraReference = "JORA N° 1 (TEST D10)",
        ArticleReference = "Art. 16 ter CDA / Accord d'association Algérie-UE (TEST)",
        PublicationDate = new DateOnly(2026, 1, 1),
        EffectiveDate = new DateOnly(2026, 1, 1)
    };

    private static readonly Guid AdminId = Guid.NewGuid();

    // Les 5 codes SH du D10 (deux lignes — PISTON et SIGMENTS — partagent le même code SH 8409993000).
    private const string HsPiston = "8409993000";
    private const string HsSigments = "8409993000";
    private const string HsCoussinetSansPalier = "8483302900";
    private const string HsCoussinetAvecPalier = "8483302300";
    private const string HsJointSpi = "8487909000";

    private static IEnumerable<RegulatoryRule> BuildRulesForHsCode(string hsCode, int seq)
    {
        // Revue du 2026-10-02 (Section 3 & 4) : la règle préférentielle est explicitement rattachée à
        // l'ORIGINE Allemagne (OriginCountryIso2 = "DE") et à l'accord "Algérie-UE" — jamais une
        // supposition générale "produit européen = 0 %". Si le moteur utilisait par erreur le PAYS
        // D'ACHAT (Portugal) comme origine, ces règles spécifiques à "DE" ne correspondraient pas et DD
        // resterait "INFORMATION NON DÉTERMINÉE" (le test échouerait) — ce test sert donc aussi de
        // garde-fou contre la confusion pays d'achat / pays d'origine (Section 3, point CRITIQUE).
        yield return new RegulatoryRule
        {
            Code = $"DD-{hsCode}-{seq}-DE",
            RegulatoryVersionCode = "2026.D10",
            RuleType = RegulatoryRuleType.CustomsDuty,
            TaxCode = "DD",
            TaxNameFr = "Droit de Douane",
            HsCode10 = hsCode,
            OriginCountryIso2 = "DE",
            RatePercent = 0.0m,
            CalculationBase = TaxableBaseType.CustomsValueDzd,
            ValidFrom = new DateOnly(2026, 1, 1),
            LegalSource = D10LegalSource,
            Status = RegulatoryRuleStatus.PublishedNewVersion,
            ValidatedByAdminUserId = AdminId
        };
        yield return new RegulatoryRule
        {
            Code = $"CS-{hsCode}-{seq}-DE",
            RegulatoryVersionCode = "2026.D10",
            RuleType = RegulatoryRuleType.SpecificTax,
            TaxCode = "CS",
            TaxNameFr = "Contribution de Solidarité",
            HsCode10 = hsCode,
            OriginCountryIso2 = "DE",
            RatePercent = 3.0m,
            CalculationBase = TaxableBaseType.CustomsValueDzd,
            ValidFrom = new DateOnly(2026, 1, 1),
            LegalSource = D10LegalSource,
            Status = RegulatoryRuleStatus.PublishedNewVersion,
            ValidatedByAdminUserId = AdminId
        };
        yield return new RegulatoryRule
        {
            Code = $"TVA-{hsCode}-{seq}-DE",
            RegulatoryVersionCode = "2026.D10",
            RuleType = RegulatoryRuleType.Vat,
            TaxCode = "TVA",
            TaxNameFr = "Taxe sur la Valeur Ajoutée",
            HsCode10 = hsCode,
            OriginCountryIso2 = "DE",
            RatePercent = 19.0m,
            CalculationBase = TaxableBaseType.CustomsValuePlusDutiesAndTaxesExVatDzd,
            ValidFrom = new DateOnly(2026, 1, 1),
            LegalSource = D10LegalSource,
            Status = RegulatoryRuleStatus.PublishedNewVersion,
            ValidatedByAdminUserId = AdminId
        };
        yield return new RegulatoryRule
        {
            Code = $"PRCT-{hsCode}-{seq}-DE",
            RegulatoryVersionCode = "2026.D10",
            RuleType = RegulatoryRuleType.SpecificTax,
            TaxCode = "PRCT",
            TaxNameFr = "Précompte à l'importation",
            HsCode10 = hsCode,
            OriginCountryIso2 = "DE",
            RatePercent = 2.0m,
            // Assiette observée sur le D10 de référence : Valeur douane + CS + TVA (calculée après TVA).
            CalculationBase = TaxableBaseType.CustomsValuePlusPriorTaxesPlusVatDzd,
            ValidFrom = new DateOnly(2026, 1, 1),
            LegalSource = D10LegalSource,
            Status = RegulatoryRuleStatus.PublishedNewVersion,
            ValidatedByAdminUserId = AdminId
        };
    }

    private static ImportCalculationOrchestrator BuildOrchestrator(IReadOnlyList<RegulatoryRule> rules)
    {
        var rateProvider = new InMemoryExchangeRateProvider(new[]
        {
            new ExchangeRateRecord
            {
                CurrencyCode = "EUR",
                QuoteCurrencyCode = "DZD",
                RateToDzd = 150.7166m,
                QuotityUnit = 1,
                ValidFrom = new DateOnly(2026, 1, 1),
                RateType = "OFFICIEL_DOUANE_ALCES",
                SourceName = "Portail officiel ALCES - DGD (jeu de test D10)"
            }
        });

        return new ImportCalculationOrchestrator(
            new CurrencyCalculator(rateProvider),
            new CostAllocationEngine(),
            new CustomsValueCalculator(),
            new RegulatoryRuleEngine(new InMemoryRegulatoryRuleRepository(rules)),
            new CurrencyConversionService(rateProvider));
    }

    [Fact]
    public void D10_PortugalPurchase_GermanyOrigin_CFR_ShouldMatchObservedLiquidation()
    {
        var rules = BuildRulesForHsCode(HsPiston, 1)
            .Concat(BuildRulesForHsCode(HsSigments, 2))
            .Concat(BuildRulesForHsCode(HsCoussinetSansPalier, 3))
            .Concat(BuildRulesForHsCode(HsCoussinetAvecPalier, 4))
            .Concat(BuildRulesForHsCode(HsJointSpi, 5))
            .ToList();

        var company = new Company
        {
            Code = "HYMA-TRADE",
            LegalName = "SARL HYMA TRADE",
            IsImportVatNonRecoverable = true
        };

        var operation = new ImportOperation
        {
            CompanyId = company.Id,
            ImportNumber = "D10-TEST-REF",
            ReferenceDate = new DateOnly(2026, 6, 1),
            SupplierName = "ZELOSO SUSTENTAVEL LDA",
            // Section 3 : pays d'achat (Portugal), STRICTEMENT distinct du pays d'origine (Allemagne, par
            // ligne ci-dessous) et du pays de provenance/premier destin (France) — jamais confondus.
            PurchaseCountryIso2 = "PT",
            ExportShippingCountryIso2 = "FR",
            DefaultOriginCountryIso2 = null, // L'origine réelle (DE) est portée par CHAQUE ligne, pas par défaut.
            MainCurrencyCode = "EUR",
            Incoterm = IncotermCode.CFR,
            ArrivalPortOrBorder = "Port d'Alger",
            TransportMode = "Maritime",
            Lines =
            {
                new ImportLine { LineNumber = 1, ProductReference = "PISTON", Designation = "Piston", Quantity = 1m, UnitPurchasePrice = 13799.79m, CurrencyCode = "EUR", HsCodeConfirmed10 = HsPiston, OriginCountryIso2 = "DE" },
                new ImportLine { LineNumber = 2, ProductReference = "SIGMENTS", Designation = "Jeu de segments", Quantity = 1m, UnitPurchasePrice = 24488.00m, CurrencyCode = "EUR", HsCodeConfirmed10 = HsSigments, OriginCountryIso2 = "DE" },
                new ImportLine { LineNumber = 3, ProductReference = "COUSSINET-SP", Designation = "Coussinet sans palier", Quantity = 1m, UnitPurchasePrice = 13020.99m, CurrencyCode = "EUR", HsCodeConfirmed10 = HsCoussinetSansPalier, OriginCountryIso2 = "DE" },
                new ImportLine { LineNumber = 4, ProductReference = "COUSSINET-AP", Designation = "Coussinet avec palier", Quantity = 1m, UnitPurchasePrice = 11655.75m, CurrencyCode = "EUR", HsCodeConfirmed10 = HsCoussinetAvecPalier, OriginCountryIso2 = "DE" },
                new ImportLine { LineNumber = 5, ProductReference = "JOINT-SPI", Designation = "Joint SPI", Quantity = 1m, UnitPurchasePrice = 900.74m, CurrencyCode = "EUR", HsCodeConfirmed10 = HsJointSpi, OriginCountryIso2 = "DE" }
            },
            Fees =
            {
                // Section 14 & 9 : RPS modélisée via le système de frais existant (montant fixe, répartie
                // également), PAS une taxe assise sur une valeur (donc jamais dans RegulatoryRule).
                new ImportFee
                {
                    FeeCategoryCode = "RPS",
                    FeeName = "Redevance de Prestation de Service (RPS)",
                    Amount = 2500.00m,
                    CurrencyCode = "DZD",
                    AllocationMethod = FeeAllocationMethod.FixedAmount,
                    IncludeInCustomsValue = false,
                    CustomsTreatment = CustomsAdjustmentTreatment.PostIntroductionExcluded,
                    IncludeInCostOfGoods = true
                }
                // AUCUN frais "Fret international" n'est ajouté : en Incoterm CFR, le prix facturé
                // (PTFN, reconstitué ci-dessus ligne par ligne) inclut DÉJÀ le fret jusqu'au point convenu
                // (Section 6, CRITIQUE) — CIMP ne doit JAMAIS calculer PTFN + Fret + Fret une seconde fois.
            }
        };

        var orchestrator = BuildOrchestrator(rules);
        var summary = orchestrator.ExecuteCalculation(company, operation);

        // ------------------------------------------------------------------------------------------
        // 1) Aucune anomalie BLOQUANTE : tous les champs essentiels (DD, CS, TVA, PRCT) sont déterminés
        //    par des règles officielles — seule la TCS (absente du D10, non simulée ici) déclenche des
        //    avertissements "Donnée manquante", jamais un blocage.
        // ------------------------------------------------------------------------------------------
        Assert.False(summary.HasBlockingAnomalies);
        Assert.Contains(summary.Anomalies, a => a.AnomalyCode == "TCS_RATE_NOT_DETERMINED" && a.Severity == AnomalySeverity.Avertissement);

        // ------------------------------------------------------------------------------------------
        // 2) Valeur en douane totale ≈ 9 625 557 DZD (D10 réel), avec une tolérance raisonnable due aux
        //    arrondis par ligne (reconstitution EUR -> DZD à partir des valeurs en douane publiées).
        // ------------------------------------------------------------------------------------------
        AssertWithinTolerance(9_625_557m, summary.TotalCustomsValueDzd, tolerance: 5m);

        // ------------------------------------------------------------------------------------------
        // 3) DD = 0 DZD (taux réglementaire 0 % pour l'origine Allemagne / accord Algérie-UE).
        // ------------------------------------------------------------------------------------------
        Assert.Equal(0m, summary.TotalCustomsDutyDzd);

        // ------------------------------------------------------------------------------------------
        // 4) CS ≈ 288 766,71 DZD (3 % de la valeur en douane) — le total exact recalculé par le moteur
        //    (somme des CS par ligne, assiette = valeur en douane de chaque ligne) est vérifié à l'exact
        //    DZD près, et comparé au résultat publié sur le D10 avec une tolérance raisonnable.
        // ------------------------------------------------------------------------------------------
        decimal totalCs = summary.LineResults.SelectMany(l => l.CustomsOutcome.AdditionalTaxes).Where(t => t.TaxCode == "CS").Sum(t => t.TaxAmountDzd);
        Assert.Equal(288766.68m, totalCs); // Recalcul exact du moteur à partir des lignes ci-dessus.
        AssertWithinTolerance(288_766.71m, totalCs, tolerance: 5m); // Comparaison au D10 réel.

        // ------------------------------------------------------------------------------------------
        // 5) TVA ≈ 1 883 721,48 DZD — assiette Valeur douane + DD + CS (DD = 0 ici).
        // ------------------------------------------------------------------------------------------
        Assert.Equal(1883721.38m, summary.TotalImportVatDzd); // Recalcul exact du moteur.
        AssertWithinTolerance(1_883_721.48m, summary.TotalImportVatDzd, tolerance: 5m);

        // ------------------------------------------------------------------------------------------
        // 6) PRCT ≈ 235 960,88 DZD — assiette Valeur douane + CS + TVA (calculée APRÈS la TVA, comme
        ///    observé sur le D10 : ce n'est PAS un pourcentage de la seule valeur en douane).
        // ------------------------------------------------------------------------------------------
        decimal totalPrct = summary.LineResults.SelectMany(l => l.CustomsOutcome.AdditionalTaxes).Where(t => t.TaxCode == "PRCT").Sum(t => t.TaxAmountDzd);
        Assert.Equal(235960.89m, totalPrct); // Recalcul exact du moteur.
        AssertWithinTolerance(235_960.88m, totalPrct, tolerance: 5m);

        // Vérifie explicitement que l'assiette du PRCT de la ligne 1 est bien VD + CS + TVA (et non
        // seulement la valeur en douane) — signature de l'assiette observée sur le D10 de référence.
        var line1Prct = summary.LineResults.Single(l => l.LineNumber == 1).CustomsOutcome.AdditionalTaxes.Single(t => t.TaxCode == "PRCT");
        var line1 = summary.LineResults.Single(l => l.LineNumber == 1);
        decimal line1Cs = line1.CustomsOutcome.AdditionalTaxes.Single(t => t.TaxCode == "CS").TaxAmountDzd;
        decimal expectedLine1PrctBase = line1.CustomsOutcome.CustomsValueDzd + line1Cs + line1.CustomsOutcome.ImportVatAmountDzd;
        Assert.Equal(expectedLine1PrctBase, line1Prct.TaxableBaseDzd);

        // ------------------------------------------------------------------------------------------
        // 7) RPS = 2 500 DZD exactement (frais à montant fixe, aucun centime perdu dans la répartition).
        // ------------------------------------------------------------------------------------------
        Assert.Equal(2500.00m, summary.TotalImportFeesDzd);
        decimal sumOfRpsAllocations = summary.LineResults.SelectMany(l => l.FeeAllocations).Where(f => f.FeeName.Contains("RPS")).Sum(f => f.AllocatedAmountDzd);
        Assert.Equal(2500.00m, sumOfRpsAllocations);

        // ------------------------------------------------------------------------------------------
        // 8) Total général (DD + CS + TVA + PRCT + RPS) ≈ 2 410 947 DZD, comme sur le récapitulatif D10.
        // ------------------------------------------------------------------------------------------
        decimal grandTotal = summary.TotalDutiesAndTaxesDzd + summary.TotalImportFeesDzd;
        AssertWithinTolerance(2_410_947m, grandTotal, tolerance: 10m);

        // ------------------------------------------------------------------------------------------
        // 9) Aucun double comptage du fret : aucun frais "FRET" n'a été ajouté, et la valeur en douane de
        //    chaque ligne est EXACTEMENT son prix d'achat EUR converti en DZD (aucune addition de fret).
        // ------------------------------------------------------------------------------------------
        Assert.DoesNotContain(operation.Fees, f => f.FeeCategoryCode.Contains("FRET", StringComparison.OrdinalIgnoreCase));
        foreach (var lineResult in summary.LineResults)
        {
            Assert.Equal(lineResult.EconomicOutcome.PurchaseValueDzd, lineResult.CustomsOutcome.CustomsValueDzd);
        }

        // ------------------------------------------------------------------------------------------
        // 10) Le pays d'achat (Portugal) reste disponible pour traçabilité mais n'a jamais été utilisé
        //     comme origine : la résolution réglementaire par ORIGINE (Allemagne) a bien fonctionné pour
        //     les 5 lignes (DD/CS/TVA/PRCT tous déterminés ci-dessus) alors qu'aucune règle n'existe pour
        //     "PT" dans ce jeu de test — la seule façon d'obtenir ces résultats est d'avoir utilisé "DE".
        // ------------------------------------------------------------------------------------------
        Assert.Equal("PT", operation.PurchaseCountryIso2);
        Assert.Equal("FR", operation.ExportShippingCountryIso2);
        Assert.All(operation.Lines, l => Assert.Equal("DE", l.OriginCountryIso2));
    }

    private static void AssertWithinTolerance(decimal expected, decimal actual, decimal tolerance)
    {
        decimal diff = Math.Abs(expected - actual);
        Assert.True(diff <= tolerance, $"Écart trop important : attendu ≈{expected:N2}, obtenu {actual:N2} (écart {diff:N2}, tolérance {tolerance:N2}).");
    }
}
