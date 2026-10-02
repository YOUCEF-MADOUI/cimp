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

            // Revue du 2026-10-02 (demande utilisateur, Sections 3 & 5 — "la case 'Inclure dans la valeur
            // en douane' n'a pas d'effet réel" / "le FRET_INTERNATIONAL ne rentre pas dans la valeur en
            // douane même quand il est coché"). CAUSE RACINE : certains frais (ex : fret international
            // ajouté automatiquement sous Incoterm CFR, voir ImportDetailViewModel.AddFeeFromTemplate) sont
            // créés avec CustomsTreatment = IncludedInInvoicePrice ("déjà compté dans le prix facturé" —
            // protection anti double comptage du cas D10/Section 3.6). CustomsValueCalculator n'ajoute
            // JAMAIS un montant pour ce traitement, même si IncludeInCustomsValue=true. Tant que
            // l'utilisateur laisse la case telle quelle, c'est le comportement voulu (le fret CFR est déjà
            // dans le prix). Mais dès qu'il la COCHE LUI-MÊME explicitement, cela signifie "non, ce frais
            // n'est PAS déjà compris dans le prix facturé, ajoutez-le réellement" — le traitement doit donc
            // être réaligné sur une addition normale, sinon la case cochée resterait sans AUCUN effet
            // (exactement le bug signalé). Les flags IncludeInCustomsValue/IncludeInCostOfGoods restent les
            // SEULS leviers exposés à l'utilisateur ; CustomsTreatment n'est plus qu'un détail interne
            // automatiquement maintenu cohérent avec eux.
            var oldTreatment = Fee.CustomsTreatment;
            var newTreatment = (value && oldTreatment == CustomsAdjustmentTreatment.IncludedInInvoicePrice)
                ? CustomsAdjustmentTreatment.Addition_Art16Octies
                : oldTreatment;

            void Apply(bool include, CustomsAdjustmentTreatment treatment)
            {
                Fee.IncludeInCustomsValue = include;
                Fee.CustomsTreatment = treatment;
                OnPropertyChanged(nameof(IncludeInCustomsValue));
            }

            Apply(value, newTreatment);

            _undoRedo?.Record(new DelegateUndoableAction(
                $"Inclusion valeur en douane du frais '{Fee.FeeName}'",
                undo: () => Apply(oldValue, oldTreatment),
                redo: () => Apply(value, newTreatment)));
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
