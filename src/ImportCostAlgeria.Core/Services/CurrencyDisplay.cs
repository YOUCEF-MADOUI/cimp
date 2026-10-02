using System;
using System.Globalization;

namespace ImportCostAlgeria.Core.Services;

/// <summary>
/// Revue du 2026-10-02 (REFONTE INTERFACE — Section 6/7/13) : centralise la règle d'AFFICHAGE des
/// montants monétaires dans toute l'interface CIMP — jamais le code interne (qui continue d'utiliser
/// "DZD" partout, Section 6 : "le code interne peut continuer à utiliser DZD si nécessaire").
///
/// Règles :
///   - DZD s'affiche "DA" (jamais "DZD") à l'écran ;
///   - EUR s'affiche avec le symbole "€" ;
///   - USD s'affiche avec le symbole "$" ;
///   - toute autre devise affiche son code ISO tel quel (repli raisonnable, jamais un symbole inventé) ;
///   - le symbole est toujours placé APRÈS le montant (ex: "2 500,00 DA", "319,56 $"), jamais une colonne
///     de devise séparée lorsqu'elle est redondante avec le symbole déjà affiché ;
///   - 2 décimales pour EUR/USD/DA (Section 23), séparateur décimal "," et séparateur de milliers " "
///     (culture FIXE fr-FR, jamais la culture régionale Windows du poste — même principe que
///     <see cref="FlexibleDecimalParser"/>/FlexibleDecimalConverter, pour un rendu reproductible).
///
/// Ne modifie JAMAIS la valeur numérique interne (toujours <see cref="decimal"/>, jamais double/float) :
/// une PURE fonction de présentation.
/// </summary>
public static class CurrencyDisplay
{
    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Symbole d'affichage pour un code devise ISO (jamais inventé pour une devise inconnue : repli sur le code lui-même).</summary>
    public static string SymbolFor(string? currencyCode) => currencyCode?.Trim().ToUpperInvariant() switch
    {
        "DZD" => "DA",
        "EUR" => "€",
        "USD" => "$",
        null or "" => string.Empty,
        var other => other
    };

    /// <summary>Formate un montant avec son symbole de devise après le nombre (ex: "2 500,00 DA").</summary>
    public static string Format(decimal amount, string? currencyCode)
    {
        string symbol = SymbolFor(currencyCode);
        string number = amount.ToString("N2", DisplayCulture);
        return string.IsNullOrEmpty(symbol) ? number : $"{number} {symbol}";
    }

    /// <summary>Surcharge pour montant optionnel (ex : une conversion commerciale non encore calculée) — retourne une chaîne vide plutôt qu'un "0,00" trompeur.</summary>
    public static string Format(decimal? amount, string? currencyCode) =>
        amount.HasValue ? Format(amount.Value, currencyCode) : string.Empty;
}
