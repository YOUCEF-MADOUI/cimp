using System.Collections.ObjectModel;
using System.Linq;
using ImportCostAlgeria.CalculationEngine;

namespace ImportCostAlgeria.Presentation.ViewModels;

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
    }

    public ImportLineRowViewModel Row { get; }
    public LineFullCalculationResult Result { get; }

    public ObservableCollection<AppliedTaxBreakdown> TaxesAppliquees { get; }
    public ObservableCollection<FeeAllocationTrace> FraisAlloues { get; }
    public ObservableCollection<CalculationAnomaly> Anomalies { get; }

    public string ComparaisonDroitExcelVsReglementaire => Result.CustomsOutcome.ComparisonLabelFr;
}
