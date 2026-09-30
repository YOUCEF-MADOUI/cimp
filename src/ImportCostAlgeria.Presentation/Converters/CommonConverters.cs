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
