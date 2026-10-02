using System;
using System.Collections.Generic;
using System.Linq;
using ImportCostAlgeria.CalculationEngine;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Core.Services;
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

    /// <summary>
    /// Revue du 2026-10-02 (Section 12 — la TVA ne doit jamais rester silencieusement à 0 %) : taux de TVA
    /// saisi manuellement, utilisé UNIQUEMENT si aucune règle réglementaire officielle n'est trouvée pour
    /// cette ligne (jamais pour remplacer une règle officielle existante — même principe que
    /// <see cref="ExcelDutyRatePercent"/>/DD).
    /// </summary>
    public decimal? ManualVatRatePercent
    {
        get => Line.ManualVatRatePercent;
        set { Line.ManualVatRatePercent = value; OnPropertyChanged(); }
    }

    /// <summary>Confirmation explicite requise avant d'appliquer <see cref="ManualVatRatePercent"/> (y compris pour confirmer une exonération à 0 %).</summary>
    public bool UserConfirmedManualVatRate
    {
        get => Line.UserConfirmedManualVatRate;
        set { Line.UserConfirmedManualVatRate = value; OnPropertyChanged(); }
    }

    /// <summary>Motif d'exonération TVA (obligatoire si un taux manuel de 0 % est confirmé).</summary>
    public string? VatExemptionReasonFr
    {
        get => Line.VatExemptionReasonFr;
        set { Line.VatExemptionReasonFr = value; OnPropertyChanged(); }
    }

    public decimal? LineVolumeM3
    {
        get => Line.LineVolumeM3;
        set { Line.LineVolumeM3 = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Revue du 2026-10-02 (REFONTE INTERFACE, Section 15) : prix de vente unitaire en DA, saisi par
    /// l'utilisateur, purement commercial (ne participe jamais au calcul douanier/fiscal). Toute
    /// modification recalcule immédiatement <see cref="ProfitDzd"/> et <see cref="ProfitPercent"/> — sans
    /// nécessiter un nouveau "Exécuter le calcul complet" (ces deux valeurs sont de simples dérivées
    /// arithmétiques d'un résultat déjà calculé, jamais une donnée réglementaire).
    /// </summary>
    public decimal? SalePriceDzd
    {
        get => Line.SalePriceDzd;
        set
        {
            Line.SalePriceDzd = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProfitDzd));
            OnPropertyChanged(nameof(ProfitPercent));
        }
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
                OnPropertyChanged(nameof(CsDzd));
                OnPropertyChanged(nameof(PrctDzd));
                OnPropertyChanged(nameof(TcsDzd));
                OnPropertyChanged(nameof(TvaDzd));
                OnPropertyChanged(nameof(CoutRevientDzd));
                OnPropertyChanged(nameof(CoutUnitaireDzd));
                OnPropertyChanged(nameof(PuReviensDzd));
                OnPropertyChanged(nameof(ProfitDzd));
                OnPropertyChanged(nameof(ProfitPercent));
                OnPropertyChanged(nameof(EtatLabel));
                OnPropertyChanged(nameof(AuthorizationCurrencyLabel));
                OnPropertyChanged(nameof(AuthorizationUnitPrice));
                OnPropertyChanged(nameof(AuthorizationTotalAmount));
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

    // Revue du 2026-10-02 (REFONTE INTERFACE, Section 18) : CS/PRCT/TCS affichées comme colonnes
    // individuelles dans le tableau des articles (plus de colonne "Autres taxes" agrégée), même source de
    // vérité que le résumé supérieur (AppliedTaxBreakdown réellement résolu par le moteur).
    private decimal? TaxByCode(string code) => Result?.CustomsOutcome.AdditionalTaxes
        .FirstOrDefault(t => string.Equals(t.TaxCode, code, StringComparison.OrdinalIgnoreCase))?.TaxAmountDzd;
    public decimal? CsDzd => TaxByCode("CS");
    public decimal? PrctDzd => TaxByCode("PRCT");
    public decimal? TcsDzd => TaxByCode("TCS");

    private decimal? _rpsAllocatedDzd;
    /// <summary>
    /// Part de la RPS (frais forfaitaire, Section 10 — "ne jamais multiplier par article") allouée à CETTE
    /// ligne, calculée par <see cref="ImportDetailViewModel.Calculate"/> à partir des
    /// <see cref="LineFullCalculationResult.FeeAllocations"/> déjà résolues par le moteur (répartition ÉGALE
    /// entre articles via <c>FeeAllocationMethod.FixedAmount</c> — jamais le montant total dupliqué sur
    /// chaque ligne). Exposé ici en lecture seule depuis l'extérieur pour affichage colonne par colonne.
    /// </summary>
    public decimal? RpsAllocatedDzd { get => _rpsAllocatedDzd; set => SetField(ref _rpsAllocatedDzd, value); }

    /// <summary>Coût de revient TOTAL de la ligne (conservé en interne — Section 26 : "ne pas supprimer la valeur totale").</summary>
    public decimal? CoutRevientDzd => Result?.EconomicOutcome.RealCostOfGoodsTotalDzd;

    /// <summary>
    /// Revue du 2026-10-02 (REFONTE INTERFACE, Section 14/26) : "PU Reviens" = coût de revient réel PAR
    /// UNITÉ (prix d'achat + valeur en douane/droits/CS/PRCT/TCS/DAPS/TVA + frais alloués, le tout divisé
    /// par la quantité) — <see cref="LineEconomicCostResult.UnitCostOfGoodsDzd"/> contenait DÉJÀ exactement
    /// cette valeur unitaire (vérifié dans ImportCalculationOrchestrator : UnitCostOfGoodsDzd =
    /// RealCostOfGoodsTotalDzd / Quantity) ; ceci est donc un simple ALIAS d'affichage plus explicite — le
    /// champ d'origine <see cref="CoutUnitaireDzd"/> reste exposé pour compatibilité.
    /// </summary>
    public decimal? PuReviensDzd => Result?.EconomicOutcome.UnitCostOfGoodsDzd;

    /// <summary>Conservé pour compatibilité ascendante — utiliser <see cref="PuReviensDzd"/> (nom retenu pour la refonte d'interface).</summary>
    public decimal? CoutUnitaireDzd => Result?.EconomicOutcome.UnitCostOfGoodsDzd;

    /// <summary>
    /// Bénéfice unitaire (Section 16) = Prix de vente unitaire (DA, saisi par l'utilisateur) - PU Reviens.
    /// Null tant que le calcul n'a pas été exécuté (PuReviensDzd absent) ou qu'aucun prix de vente n'a été
    /// saisi — jamais une valeur inventée à 0 qui pourrait être confondue avec un bénéfice nul réel.
    /// </summary>
    public decimal? ProfitDzd => (PuReviensDzd.HasValue && SalePriceDzd.HasValue)
        ? ProfitCalculator.ComputeProfit(SalePriceDzd.Value, PuReviensDzd.Value)
        : null;

    /// <summary>
    /// % Bénéfice (Section 17) = Bénéfice / PU Reviens × 100 — marge calculée par rapport au COÛT DE
    /// REVIENT (jamais par rapport au prix de vente). Null si PU Reviens est nul ou indisponible (division
    /// par zéro jamais silencieuse). Formule centralisée et testée dans <see cref="ProfitCalculator"/>.
    /// </summary>
    public decimal? ProfitPercent => (ProfitDzd.HasValue && PuReviensDzd.HasValue)
        ? ProfitCalculator.ComputeProfitPercent(ProfitDzd.Value, PuReviensDzd.Value)
        : null;

    // Section 12 du plan multi-devises : conversion COMMERCIALE (jamais réglementaire) de cette ligne vers
    // la devise de l'autorisation d'importation — vide tant qu'aucune conversion n'est nécessaire/calculée.
    public string? AuthorizationCurrencyLabel => Result?.AuthorizationConversion?.AuthorizationCurrencyCode;
    public decimal? AuthorizationUnitPrice => Result?.AuthorizationConversion?.AuthorizationUnitPrice;
    public decimal? AuthorizationTotalAmount => Result?.AuthorizationConversion?.AuthorizationTotalAmount;

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

