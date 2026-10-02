using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ImportCostAlgeria.CalculationEngine;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.RegulatoryEngine;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>
/// Ligne d'affichage "DD / PRCT / TCS / TVA / DAPS" de l'écran Importation (point 6 de la revue du
/// 2026-10-01) : réunit, pour chaque taxe standard, son statut réglementaire explicite (Applicable /
/// Non applicable / Donnée manquante) et, si applicable, le taux/la base/le montant réellement calculés
/// (<see cref="AppliedTaxBreakdown"/>). Ne contient AUCUNE donnée inventée : purement une projection
/// d'affichage de ce que le moteur réglementaire a déjà résolu.
/// </summary>
public sealed class StandardTaxDisplayRowViewModel
{
    public StandardTaxDisplayRowViewModel(TaxApplicabilityStatus status, AppliedTaxBreakdown? computedBreakdown)
    {
        TaxCode = status.TaxCode;
        TaxNameFr = status.TaxNameFr;
        Kind = status.Kind;
        StatusLabelFr = status.DisplayStatusFr;
        TaxableBaseDzd = computedBreakdown?.TaxableBaseDzd;
        RatePercent = computedBreakdown?.RatePercent;
        TaxAmountDzd = computedBreakdown?.TaxAmountDzd;
        LegalArticleReference = computedBreakdown?.LegalArticleReference;
        RegulatoryVersionCode = computedBreakdown?.RegulatoryVersionCode;
    }

    public string TaxCode { get; }
    public string TaxNameFr { get; }
    public TaxApplicabilityKind Kind { get; }
    public string StatusLabelFr { get; }
    public decimal? TaxableBaseDzd { get; }
    public decimal? RatePercent { get; }
    public decimal? TaxAmountDzd { get; }
    public string? LegalArticleReference { get; }
    public string? RegulatoryVersionCode { get; }

    public bool EstApplicable => Kind == TaxApplicabilityKind.Applicable;
    public bool EstDonneeManquante => Kind == TaxApplicabilityKind.DonneeManquante;
}

/// <summary>
/// "Voir le détail" d'un article après calcul (Section 27) : décomposition additive complète
/// (valeur en douane, droit de douane, taxes additionnelles, TVA, frais alloués, coût de revient,
/// coût unitaire) avec citation des sources légales de chaque taxe appliquée.
/// </summary>
public sealed class LineDetailDialogViewModel
{
    public LineDetailDialogViewModel(ImportLineRowViewModel row)
    {
        Row = row;
        Result = row.Result!;

        TaxesAppliquees = new ObservableCollection<AppliedTaxBreakdown>(Result.CustomsOutcome.AdditionalTaxes);
        FraisAlloues = new ObservableCollection<FeeAllocationTrace>(Result.FeeAllocations);
        Anomalies = new ObservableCollection<CalculationAnomaly>(row.AnomaliesForLine);

        // Revue du 2026-10-01 (point 5 & 6) : DD, PRCT, TCS, TVA, DAPS toujours affichés explicitement,
        // avec leur statut réel (Applicable / Non applicable / Donnée manquante) — jamais une absence
        // silencieuse de ligne ni un taux à 0 % inventé.
        var standardStatuses = Result.CustomsOutcome.StandardTaxApplicability ?? new List<TaxApplicabilityStatus>();
        TaxesStandardAvecApplicabilite = new ObservableCollection<StandardTaxDisplayRowViewModel>(
            standardStatuses.Select(status =>
            {
                var breakdown = Result.CustomsOutcome.AdditionalTaxes
                    .FirstOrDefault(t => string.Equals(t.TaxCode, status.TaxCode, System.StringComparison.OrdinalIgnoreCase));
                return new StandardTaxDisplayRowViewModel(status, breakdown);
            }));
    }

    public ImportLineRowViewModel Row { get; }
    public LineFullCalculationResult Result { get; }

    public ObservableCollection<AppliedTaxBreakdown> TaxesAppliquees { get; }
    public ObservableCollection<StandardTaxDisplayRowViewModel> TaxesStandardAvecApplicabilite { get; }
    public ObservableCollection<FeeAllocationTrace> FraisAlloues { get; }
    public ObservableCollection<CalculationAnomaly> Anomalies { get; }

    public string ComparaisonDroitExcelVsReglementaire => Result.CustomsOutcome.ComparisonLabelFr;

    /// <summary>Statut d'applicabilité du Droit de Douane, pour affichage "Non applicable" / "INFORMATION NON DÉTERMINÉE" plutôt qu'un taux à 0 % silencieux.</summary>
    public string DroitDeDouaneStatutFr =>
        Result.CustomsOutcome.CustomsDutyLegalArticleReference != null
            ? $"Applicable ({Result.CustomsOutcome.CustomsDutyRatePercent:N2} %)"
            : ComparaisonDroitExcelVsReglementaire;

    /// <summary>
    /// Statut d'applicabilité de la TVA à l'importation, même logique que pour le Droit de Douane. Revue du
    /// 2026-10-02 (Section 12) : distingue désormais explicitement un taux confirmé MANUELLEMENT (en
    /// l'absence de règle officielle) d'une règle officielle réelle — jamais un simple "0 %" silencieux.
    /// </summary>
    public string TvaStatutFr
    {
        get
        {
            if (Result.CustomsOutcome.VatLegalArticleReference != null)
                return $"Applicable ({Result.CustomsOutcome.VatRatePercent:N2} %)";

            if (Result.CustomsOutcome.VatRateOriginTag == DataOriginTag.DonneeUtilisateur)
            {
                return Result.CustomsOutcome.VatRatePercent == 0m
                    ? $"⚠️ Exonération de TVA confirmée manuellement (motif : {Row.VatExemptionReasonFr ?? "non précisé"})"
                    : $"⚠️ Taux de TVA saisi manuellement ({Result.CustomsOutcome.VatRatePercent:N2} %) — aucune règle officielle trouvée";
            }

            return "Donnée réglementaire manquante — validation requise";
        }
    }
}
