using System.Collections.Generic;
using Xunit;
using ImportCostAlgeria.Core.Services;

namespace ImportCostAlgeria.UnitTests;

/// <summary>
/// Revue du 2026-10-02 (demande utilisateur, Section 5 — menu "Édition") : vérifie que
/// <see cref="UndoRedoManager"/> constitue un VRAI mécanisme d'historique (restitue exactement l'ancienne
/// valeur d'un champ, pas un simple rechargement global), conformément à l'exigence explicite
/// "Ne pas créer un faux bouton 'Annuler' qui recharge simplement la page."
/// </summary>
public sealed class UndoRedoManagerTests
{
    [Fact]
    public void RecordFieldChange_ThenUndo_RestoresExactPreviousValue()
    {
        decimal price = 100m;
        var manager = new UndoRedoManager();

        decimal oldValue = price;
        price = 150m; // modification déjà appliquée par l'appelant (ex: setter du ViewModel)
        manager.RecordFieldChange("Prix d'achat", v => price = v, oldValue, price);

        Assert.True(manager.CanUndo);
        manager.Undo();

        Assert.Equal(100m, price);
        Assert.False(manager.CanUndo);
        Assert.True(manager.CanRedo);
    }

    [Fact]
    public void Undo_ThenRedo_ReappliesExactNewValue()
    {
        decimal quantity = 10m;
        var manager = new UndoRedoManager();

        manager.RecordFieldChange("Quantité", v => quantity = v, oldValue: 10m, newValue: 25m);
        quantity = 25m;

        manager.Undo();
        Assert.Equal(10m, quantity);

        manager.Redo();
        Assert.Equal(25m, quantity);
        Assert.True(manager.CanUndo);
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public void NewRecord_AfterUndo_ClearsRedoStack()
    {
        decimal value = 1m;
        var manager = new UndoRedoManager();

        manager.RecordFieldChange("A", v => value = v, 1m, 2m);
        value = 2m;
        manager.Undo(); // value = 1, Redo disponible
        Assert.True(manager.CanRedo);

        manager.RecordFieldChange("B", v => value = v, 1m, 3m);
        value = 3m;

        // Une nouvelle action après un Undo doit invalider l'ancien "futur" (comportement standard
        // Undo/Redo : on ne peut plus "Redo" vers un état qui n'a plus de sens après une nouvelle action).
        Assert.False(manager.CanRedo);
        Assert.True(manager.CanUndo);
    }

    [Fact]
    public void MultipleUndo_RestoresValuesInReverseOrder()
    {
        var history = new List<string> { "initial" };
        var manager = new UndoRedoManager();

        void SetValue(string v)
        {
            history.Add(v);
        }

        manager.RecordFieldChange("1", v => SetValue(v), "initial", "step1");
        manager.RecordFieldChange("2", v => SetValue(v), "step1", "step2");
        manager.RecordFieldChange("3", v => SetValue(v), "step2", "step3");

        manager.Undo(); // revient à step2
        manager.Undo(); // revient à step1
        manager.Undo(); // revient à initial

        Assert.Equal("initial", history[^1]);
        Assert.False(manager.CanUndo);
        Assert.True(manager.CanRedo);
    }

    [Fact]
    public void Undo_WithEmptyStack_DoesNothing_NeverThrows()
    {
        var manager = new UndoRedoManager();
        manager.Undo();
        manager.Redo();
        Assert.False(manager.CanUndo);
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public void Clear_RemovesAllHistory_BothStacks()
    {
        decimal value = 1m;
        var manager = new UndoRedoManager();
        manager.RecordFieldChange("A", v => value = v, 1m, 2m);
        value = 2m;
        manager.Undo();

        Assert.True(manager.CanRedo);

        manager.Clear();

        Assert.False(manager.CanUndo);
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public void Undo_DoesNotReRecordItselfAsANewUndoableAction()
    {
        // Si Undo() était ré-enregistré comme une nouvelle action, CanRedo deviendrait systématiquement
        // faux après un Undo (la pile Redo serait aussitôt effacée par le "faux" nouvel enregistrement).
        decimal value = 1m;
        var manager = new UndoRedoManager();
        manager.RecordFieldChange("A", v => value = v, 1m, 2m);
        value = 2m;

        manager.Undo();

        Assert.True(manager.CanRedo);
    }

    [Fact]
    public void StateChanged_IsRaised_OnRecordUndoRedoAndClear()
    {
        int raisedCount = 0;
        var manager = new UndoRedoManager();
        manager.StateChanged += (_, __) => raisedCount++;

        decimal value = 1m;
        manager.RecordFieldChange("A", v => value = v, 1m, 2m);
        value = 2m;
        Assert.Equal(1, raisedCount);

        manager.Undo();
        Assert.Equal(2, raisedCount);

        manager.Redo();
        Assert.Equal(3, raisedCount);

        manager.Clear();
        Assert.Equal(4, raisedCount);
    }

    [Fact]
    public void NextUndoRedoDescriptions_ExposeHumanReadableLabels()
    {
        decimal value = 1m;
        var manager = new UndoRedoManager();
        manager.RecordFieldChange("Prix de vente", v => value = v, 1m, 2m);
        value = 2m;

        Assert.Equal("Prix de vente", manager.NextUndoDescriptionFr);
        Assert.Null(manager.NextRedoDescriptionFr);

        manager.Undo();

        Assert.Null(manager.NextUndoDescriptionFr);
        Assert.Equal("Prix de vente", manager.NextRedoDescriptionFr);
    }
}
