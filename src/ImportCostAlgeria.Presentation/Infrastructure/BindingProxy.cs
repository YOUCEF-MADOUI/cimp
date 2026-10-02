using System.Windows;

namespace ImportCostAlgeria.Presentation.Infrastructure;

/// <summary>
/// Revue du 2026-10-02 (REFONTE INTERFACE, Section 10/11) : <see cref="DataGridColumn"/> (en-tête ET
/// visibilité) ne fait PAS partie de l'arbre visuel WPF (c'est un objet logique, pas un
/// <see cref="FrameworkElement"/>) — un binding direct <c>{Binding ...}</c> sur
/// <see cref="DataGridColumn.Visibility"/> ou <see cref="DataGridColumn.Header"/> ne peut donc pas
/// résoudre son <c>DataContext</c> par héritage normal. <see cref="BindingProxy"/> est le contournement
/// WPF standard : un objet <see cref="Freezable"/> (qui, lui, hérite correctement du DataContext une fois
/// placé en resource avec un binding explicite) exposant la valeur via <see cref="Data"/>, que les colonnes
/// peuvent alors référencer via <c>{Binding Data.XXX, Source={StaticResource Proxy}}</c>.
/// </summary>
public sealed class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new PropertyMetadata(null));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
