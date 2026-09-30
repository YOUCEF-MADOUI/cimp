using System.Collections.Generic;
using System.Linq;
using ImportCostAlgeria.CalculationEngine;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Presentation.Infrastructure;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>
/// Ligne de la grille "Articles" (Section 7) : encapsule l'entité éditable <see cref="ImportLine"/> et,
/// une fois un calcul exécuté, les résultats en lecture seule (valeur en douane, coût douanier, coût de
/// revient, coût unitaire, état/anomalies) — Section 25-28.
/// </summary>
public sealed class ImportLineRowViewModel : ObservableObject
{
    public ImportLine Line { get; }

    public ImportLineRowViewModel(ImportLine line)
    {
        Line = line;
    }

    public string ProductReference
    {
        get => Line.ProductReference;
        set { Line.ProductReference = value; OnPropertyChanged(); }
    }

    public string Designation
    {
        get => Line.Designation;
        set { Line.Designation = value; OnPropertyChanged(); }
    }

    public decimal Quantity
    {
        get => Line.Quantity;
        set { Line.Quantity = value; OnPropertyChanged(); }
    }

    public decimal UnitPurchasePrice
    {
        get => Line.UnitPurchasePrice;
        set { Line.UnitPurchasePrice = value; OnPropertyChanged(); }
    }

    public string CurrencyCode
    {
        get => Line.CurrencyCode;
        set { Line.CurrencyCode = value; OnPropertyChanged(); }
    }

    public string? OriginCountryIso2
    {
        get => Line.OriginCountryIso2;
        set { Line.OriginCountryIso2 = value; OnPropertyChanged(); }
    }

    public string? HsCodeConfirmed10
    {
        get => Line.HsCodeConfirmed10;
        set { Line.HsCodeConfirmed10 = value; OnPropertyChanged(); }
    }

    public decimal? ExcelDutyRatePercent
    {
        get => Line.ExcelDutyRatePercent;
        set { Line.ExcelDutyRatePercent = value; OnPropertyChanged(); }
    }

    public decimal? LineGrossWeightKg
    {
        get => Line.LineGrossWeightKg;
        set { Line.LineGrossWeightKg = value; OnPropertyChanged(); }
    }

    public decimal? LineVolumeM3
    {
        get => Line.LineVolumeM3;
        set { Line.LineVolumeM3 = value; OnPropertyChanged(); }
    }

    public string AiHsSummary => Line.AiHsDecision switch
    {
        AiProposalDecision.PendingUserValidation => $"Proposition IA en attente : {Line.AiProposedHsCode10}",
        AiProposalDecision.ConfirmedByUser => "Confirmé par l'utilisateur",
        AiProposalDecision.ModifiedByUser => "Modifié par l'utilisateur",
        AiProposalDecision.RejectedByUser => "Proposition IA refusée",
        _ => "—"
    };

    private LineFullCalculationResult? _result;
    public LineFullCalculationResult? Result
    {
        get => _result;
        set
        {
            if (SetField(ref _result, value))
            {
                OnPropertyChanged(nameof(ValeurDouaniereDzd));
                OnPropertyChanged(nameof(DroitDouaneDzd));
                OnPropertyChanged(nameof(TvaDzd));
                OnPropertyChanged(nameof(CoutRevientDzd));
                OnPropertyChanged(nameof(CoutUnitaireDzd));
                OnPropertyChanged(nameof(EtatLabel));
            }
        }
    }

    public List<CalculationAnomaly> AnomaliesForLine { get; set; } = new();

    public void RaiseEtatChanged() => OnPropertyChanged(nameof(EtatLabel));

    public void OnHsChanged()
    {
        OnPropertyChanged(nameof(HsCodeConfirmed10));
        OnPropertyChanged(nameof(AiHsSummary));
    }

    public decimal? ValeurDouaniereDzd => Result?.CustomsOutcome.CustomsValueDzd;
    public decimal? DroitDouaneDzd => Result?.CustomsOutcome.CustomsDutyAmountDzd;
    public decimal? TvaDzd => Result?.CustomsOutcome.ImportVatAmountDzd;
    public decimal? CoutRevientDzd => Result?.EconomicOutcome.RealCostOfGoodsTotalDzd;
    public decimal? CoutUnitaireDzd => Result?.EconomicOutcome.UnitCostOfGoodsDzd;

    public string EtatLabel
    {
        get
        {
            if (Result == null) return "Non calculé";
            bool hasBlocking = AnomaliesForLine.Any(a => a.Severity == AnomalySeverity.Blocage);
            if (hasBlocking) return "🚫 BLOQUÉ";
            if (AnomaliesForLine.Count > 0) return $"⚠️ {AnomaliesForLine.Count} anomalie(s)";
            return "✓ OK";
        }
    }
}

