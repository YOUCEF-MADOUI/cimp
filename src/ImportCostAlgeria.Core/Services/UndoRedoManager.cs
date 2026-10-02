using System;
using System.Collections.Generic;

namespace ImportCostAlgeria.Core.Services;

/// <summary>
/// Une action annulable/rétablissable individuelle (Section 5.1 de la demande utilisateur — "il faut
/// utiliser un mécanisme d'historique adapté au ViewModel/modèle de données", explicitement PAS "un faux
/// bouton Annuler qui recharge simplement la page").
/// </summary>
public interface IUndoableAction
{
    /// <summary>Libellé court destiné à l'utilisateur (ex: pour un futur historique affiché à l'écran).</summary>
    string DescriptionFr { get; }

    /// <summary>Restaure l'état précédent cette action.</summary>
    void Undo();

    /// <summary>Réapplique cette action après un Undo.</summary>
    void Redo();
}

/// <summary>
/// Implémentation générique d'<see cref="IUndoableAction"/> à partir de deux délégués, suffisante pour
/// capturer une modification de champ précise (ancienne valeur -&gt; nouvelle valeur) sans dupliquer tout
/// l'état du ViewModel/modèle.
/// </summary>
public sealed class DelegateUndoableAction : IUndoableAction
{
    private readonly Action _undo;
    private readonly Action _redo;

    public string DescriptionFr { get; }

    public DelegateUndoableAction(string descriptionFr, Action undo, Action redo)
    {
        DescriptionFr = descriptionFr;
        _undo = undo ?? throw new ArgumentNullException(nameof(undo));
        _redo = redo ?? throw new ArgumentNullException(nameof(redo));
    }

    public void Undo() => _undo();
    public void Redo() => _redo();
}

/// <summary>
/// Pile Annuler/Rétablir générique (Section 5.1 de la demande utilisateur). Pure logique métier,
/// indépendante de toute UI/WPF — utilisable depuis n'importe quel ViewModel (actuellement : l'écran
/// "Détail d'une importation", Section 5.1 : "commencer au minimum par l'écran d'importation").
///
/// Usage typique pour un champ simple (voir <see cref="RecordFieldChange{T}"/>) :
/// <code>
/// decimal oldValue = Line.Quantity;
/// Line.Quantity = newValue;
/// undoRedo.RecordFieldChange("Quantité", v =&gt; { Line.Quantity = v; RaiseChanged(); }, oldValue, newValue);
/// </code>
/// </summary>
public sealed class UndoRedoManager
{
    private readonly Stack<IUndoableAction> _undoStack = new();
    private readonly Stack<IUndoableAction> _redoStack = new();

    // Évite qu'un Undo/Redo soit lui-même réenregistré comme une nouvelle action (ce qui casserait la
    // pile Redo et créerait une boucle d'historique infinie).
    private bool _isApplyingHistory;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>Libellé de la prochaine action qui serait annulée/rétablie (utile pour l'UI : "Annuler : Prix d'achat").</summary>
    public string? NextUndoDescriptionFr => _undoStack.Count > 0 ? _undoStack.Peek().DescriptionFr : null;
    public string? NextRedoDescriptionFr => _redoStack.Count > 0 ? _redoStack.Peek().DescriptionFr : null;

    /// <summary>Déclenché après tout changement d'état (Record/Undo/Redo/Clear) — permet à l'UI de rafraîchir CanExecute des commandes.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Enregistre une action déjà exécutée dans la pile Annuler, et vide la pile Rétablir (Section 5.1 : toute nouvelle modification invalide le "futur" précédemment annulé).</summary>
    public void Record(IUndoableAction action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        if (_isApplyingHistory)
            return;

        _undoStack.Push(action);
        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raccourci pour le cas le plus courant (Section 5.1) : une modification de champ simple, capturée
    /// AVANT d'être appliquée. <paramref name="applyValue"/> doit réellement réécrire le champ concerné
    /// (jamais un simple rechargement global de l'écran).
    /// </summary>
    public void RecordFieldChange<T>(string descriptionFr, Action<T> applyValue, T oldValue, T newValue)
    {
        Record(new DelegateUndoableAction(
            descriptionFr,
            undo: () => applyValue(oldValue),
            redo: () => applyValue(newValue)));
    }

    public void Undo()
    {
        if (_undoStack.Count == 0)
            return;

        var action = _undoStack.Pop();
        _isApplyingHistory = true;
        try
        {
            action.Undo();
        }
        finally
        {
            _isApplyingHistory = false;
        }
        _redoStack.Push(action);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0)
            return;

        var action = _redoStack.Pop();
        _isApplyingHistory = true;
        try
        {
            action.Redo();
        }
        finally
        {
            _isApplyingHistory = false;
        }
        _undoStack.Push(action);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Vide complètement l'historique (ex : changement d'importation affichée à l'écran — un Undo ne doit jamais s'appliquer à une AUTRE importation que celle sur laquelle il a été enregistré).</summary>
    public void Clear()
    {
        if (_undoStack.Count == 0 && _redoStack.Count == 0)
            return;

        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
