using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Core.Services;
using ImportCostAlgeria.Presentation.Infrastructure;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>
/// Ligne de la grille "Frais" (Section 9) : encapsule l'entité éditable <see cref="ImportFee"/> — même
/// principe que <see cref="ImportLineRowViewModel"/> pour les articles. Introduite lors de la revue du
/// 2026-10-02 (Section 5.1 — menu "Édition"/Annuler-Rétablir) pour permettre l'enregistrement des
/// modifications de frais (montant, méthode de répartition, inclusion valeur en douane/coût de revient)
/// dans <see cref="UndoRedoManager"/>, chose impossible tant que <c>Fees</c> exposait directement
/// <see cref="ImportFee"/> (simple POCO sans notification de changement) aux bindings WPF.
/// </summary>
public sealed class FeeRowViewModel : ObservableObject
{
    private readonly UndoRedoManager? _undoRedo;

    public ImportFee Fee { get; }

    public FeeRowViewModel(ImportFee fee, UndoRedoManager? undoRedo = null)
    {
        Fee = fee;
        _undoRedo = undoRedo;
    }

    public string FeeCategoryCode
    {
        get => Fee.FeeCategoryCode;
        set { Fee.FeeCategoryCode = value; OnPropertyChanged(); }
    }

    public string FeeName
    {
        get => Fee.FeeName;
        set { Fee.FeeName = value; OnPropertyChanged(); }
    }

    /// <summary>Section 5.1 (exemple explicite : "modification d'un frais") : montant du frais, undo-able.</summary>
    public decimal Amount
    {
        get => Fee.Amount;
        set
        {
            decimal oldValue = Fee.Amount;
            if (oldValue == value) return;
            Fee.Amount = value;
            OnPropertyChanged();
            _undoRedo?.RecordFieldChange($"Montant du frais '{Fee.FeeName}'", v => { Fee.Amount = v; OnPropertyChanged(nameof(Amount)); }, oldValue, value);
        }
    }

    public string CurrencyCode
    {
        get => Fee.CurrencyCode;
        set { Fee.CurrencyCode = value; OnPropertyChanged(); }
    }

    /// <summary>Section 5.1 (exemple explicite : "changement de méthode de répartition"), undo-able.</summary>
    public FeeAllocationMethod AllocationMethod
    {
        get => Fee.AllocationMethod;
        set
        {
            var oldValue = Fee.AllocationMethod;
            if (oldValue == value) return;
            Fee.AllocationMethod = value;
            OnPropertyChanged();
            _undoRedo?.RecordFieldChange($"Méthode de répartition du frais '{Fee.FeeName}'", v => { Fee.AllocationMethod = v; OnPropertyChanged(nameof(AllocationMethod)); }, oldValue, value);
        }
    }

    /// <summary>Section 3.1 (case indépendante "Inclure dans la valeur en douane"), undo-able.</summary>
    public bool IncludeInCustomsValue
    {
        get => Fee.IncludeInCustomsValue;
        set
        {
            bool oldValue = Fee.IncludeInCustomsValue;
            if (oldValue == value) return;
            Fee.IncludeInCustomsValue = value;
            OnPropertyChanged();
            _undoRedo?.RecordFieldChange($"Inclusion valeur en douane du frais '{Fee.FeeName}'", v => { Fee.IncludeInCustomsValue = v; OnPropertyChanged(nameof(IncludeInCustomsValue)); }, oldValue, value);
        }
    }

    /// <summary>Section 3.1 (case indépendante "Inclure dans le coût de revient"), undo-able.</summary>
    public bool IncludeInCostOfGoods
    {
        get => Fee.IncludeInCostOfGoods;
        set
        {
            bool oldValue = Fee.IncludeInCostOfGoods;
            if (oldValue == value) return;
            Fee.IncludeInCostOfGoods = value;
            OnPropertyChanged();
            _undoRedo?.RecordFieldChange($"Inclusion coût de revient du frais '{Fee.FeeName}'", v => { Fee.IncludeInCostOfGoods = v; OnPropertyChanged(nameof(IncludeInCostOfGoods)); }, oldValue, value);
        }
    }
}
