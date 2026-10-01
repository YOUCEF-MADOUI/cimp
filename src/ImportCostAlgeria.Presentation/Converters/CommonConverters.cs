using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ImportCostAlgeria.Presentation.Converters;

/// <summary>Convertit null/non-null en Visibility (masque un panneau de détail tant qu'aucun élément n'est sélectionné).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value == null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Convertit un booléen en Visibility (Visible si vrai).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Convertit un booléen en Visibility inversée (Visible si faux) — ex : bannière "aucune anomalie".</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Colore un libellé de sévérité d'anomalie (INFO/AVERTISSEMENT/ERREUR/BLOCAGE).</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        string severity = value?.ToString() ?? string.Empty;
        return severity switch
        {
            "Blocage" or "BLOCAGE" => new SolidColorBrush(Color.FromRgb(0xF8, 0xD7, 0xDA)),
            "Erreur" or "ERREUR" => new SolidColorBrush(Color.FromRgb(0xFC, 0xE8, 0xCB)),
            "Avertissement" or "AVERTISSEMENT" => new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xCD)),
            _ => new SolidColorBrush(Colors.White)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Convertit entre System.DateOnly (modèle métier) et System.DateTime? (attendu par DatePicker WPF).</summary>
public sealed class DateOnlyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateOnly d ? d.ToDateTime(TimeOnly.MinValue) : (object?)null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateTime dt ? DateOnly.FromDateTime(dt) : DateOnly.FromDateTime(DateTime.Today);
}

/// <summary>Convertit une valeur numérique en Visibility (Visible si count &gt; 0).</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Revue du 2026-10-01 (point 3 — Saisie des taux de change) : convertit entre un TextBox (saisie libre)
/// et une propriété <see cref="decimal"/> ou <see cref="decimal"/>? en acceptant INDIFFÉREMMENT la
/// virgule (notation française, ex. "1,17" ou "152,1552") ou le point (notation anglo-saxonne, ex.
/// "1.17" ou "152.1552") comme séparateur décimal — quelle que soit la culture régionale effective de
/// la machine sur laquelle CIMP s'exécute (on ne dépend donc jamais, pour la saisie, de la culture
/// courante du thread, qui peut varier d'un poste à l'autre). Utilisé pour :
///   - le taux réglementaire (Réglementation -&gt; Taux %) ;
///   - le taux commercial (Taux de change -&gt; Nouveau taux vers DZD) ;
///   - le taux manuel (Importation -&gt; Taux de change manuel, Taux commercial manuel) ;
///   - tout autre champ de taux/montant décimal concerné.
/// Ne convertit JAMAIS la valeur en double/float : uniquement <see cref="decimal.TryParse(string, NumberStyles, IFormatProvider, out decimal)"/>,
/// qui préserve une précision décimale exacte (indispensable pour des montants monétaires).
/// </summary>
public sealed class FlexibleDecimalConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            null => string.Empty,
            decimal dec => dec.ToString("G29", CultureInfo.CurrentCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string? text = value as string;
        bool targetIsNullable = Nullable.GetUnderlyingType(targetType) != null;

        if (string.IsNullOrWhiteSpace(text))
            return targetIsNullable ? null : (object)DependencyProperty.UnsetValue;

        // Logique d'analyse centralisée et testée indépendamment de WPF (voir
        // ImportCostAlgeria.Core.Services.FlexibleDecimalParser + les tests unitaires associés).
        if (ImportCostAlgeria.Core.Services.FlexibleDecimalParser.TryParse(text, out decimal result))
            return result;

        // Saisie non interprétable comme nombre (virgule ou point) : on ne doit JAMAIS inventer une
        // valeur (ni 0, ni arrondi silencieux) — on rejette la mise à jour de la source (WPF affiche
        // alors un retour de validation standard et conserve la dernière valeur valide côté modèle).
        return DependencyProperty.UnsetValue;
    }
}
