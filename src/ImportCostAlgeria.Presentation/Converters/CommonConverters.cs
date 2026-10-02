using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ImportCostAlgeria.Core.Services;

namespace ImportCostAlgeria.Presentation.Converters;

/// <summary>
/// Revue du 2026-10-02 (REFONTE INTERFACE — Section 6/7/13) : convertit un montant <see cref="decimal"/>
/// (ou <see cref="decimal"/>?) en texte affichant le symbole de devise APRÈS le nombre (ex : "2 500,00 DA",
/// "319,56 $"), via <see cref="CurrencyDisplay"/> (seule source de vérité du mapping code -&gt; symbole).
/// Le <c>ConverterParameter</c> fournit le code devise FIXE lorsque la colonne entière utilise toujours la
/// même devise (ex : "DZD" pour un total en DA) — pour une devise qui varie ligne par ligne (ex : prix
/// d'achat dans la devise propre à chaque article), utiliser <see cref="MoneyWithDynamicCurrencyConverter"/>
/// (MultiBinding montant + code devise de la ligne).
/// </summary>
public sealed class MoneyWithFixedCurrencyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        string currencyCode = parameter?.ToString() ?? "DZD";
        return value switch
        {
            decimal dec => CurrencyDisplay.Format(dec, currencyCode),
            null => string.Empty,
            _ => string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Revue du 2026-10-02 (REFONTE INTERFACE — Section 7/13) : variante MultiBinding de
/// <see cref="MoneyWithFixedCurrencyConverter"/> pour les colonnes où la devise varie par ligne (ex :
/// "Prix achat" dans le tableau des articles — chaque article peut avoir sa propre devise de facturation).
/// Attend exactement deux valeurs liées : [0] = montant (decimal/decimal?), [1] = code devise (string).
/// </summary>
public sealed class MoneyWithDynamicCurrencyConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return string.Empty;
        string? currencyCode = values[1] as string;
        return values[0] switch
        {
            decimal dec => CurrencyDisplay.Format(dec, currencyCode),
            null => string.Empty,
            _ => string.Empty
        };
    }

    public object?[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Convertit un code devise ISO (ex : "EUR") en son symbole d'affichage CIMP (ex : "€") — voir <see cref="CurrencyDisplay"/>.</summary>
public sealed class CurrencyCodeToSymbolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        CurrencyDisplay.SymbolFor(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

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
///
/// Revue du 2026-10-01 (correction urgente — la virgule restait mal acceptée malgré la première
/// correction) : DEUX causes supplémentaires, propres au binding WPF, empêchaient en pratique la saisie
/// de "152,1552" même si <see cref="ImportCostAlgeria.Core.Services.FlexibleDecimalParser"/> analysait
/// déjà correctement la virgule :
///   1) Le formatage d'affichage (ci-dessous) utilisait <see cref="CultureInfo.CurrentCulture"/>, c'est-à-dire
///      la culture régionale EFFECTIVE DE WINDOWS sur le poste de l'utilisateur. Or les champs de taux
///      étaient liés avec <c>UpdateSourceTrigger=PropertyChanged</c> : CHAQUE frappe met donc à jour la
///      propriété decimal source, qui déclenche aussitôt <see cref="System.ComponentModel.INotifyPropertyChanged"/>,
///      ce qui fait que WPF réinjecte IMMÉDIATEMENT dans le TextBox le texte reformaté par <see cref="Convert"/>
///      — à CHAQUE caractère tapé. Si la culture Windows de la machine n'utilise pas la virgule comme
///      séparateur décimal (ex. anglais), WPF remplaçait alors sous les yeux de l'utilisateur la virgule
///      qu'il venait de taper par un point, rendant la poursuite de la saisie (ex. passer de "152,1" à
///      "152,15") pratiquement impossible. On ne dépend donc plus JAMAIS de la culture Windows courante
///      pour l'affichage : une culture fixe (fr-FR) est utilisée, indépendamment du poste.
///   2) Les bindings des champs de taux manuel sont passés de <c>UpdateSourceTrigger=PropertyChanged</c>
///      à <c>UpdateSourceTrigger=LostFocus</c> (voir les vues XAML concernées) : la propriété decimal
///      n'est donc mise à jour, et le texte reformaté, qu'une seule fois lorsque l'utilisateur quitte le
///      champ (geste de "validation"), jamais pendant la frappe elle-même — exactement le comportement
///      explicitement autorisé par la revue ("le taux affiché peut être normalisé après validation").
/// </summary>
public sealed class FlexibleDecimalConverter : IValueConverter
{
    /// <summary>
    /// Culture FIXE utilisée pour l'affichage (jamais <see cref="CultureInfo.CurrentCulture"/>, qui varie
    /// selon la configuration régionale Windows du poste) : garantit un rendu prévisible (virgule
    /// française) quel que soit l'ordinateur sur lequel CIMP s'exécute.
    /// </summary>
    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("fr-FR");

    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            null => string.Empty,
            decimal dec => dec.ToString("G29", DisplayCulture),
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
