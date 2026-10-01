using System;
using System.Globalization;

namespace ImportCostAlgeria.Core.Services;

/// <summary>
/// Revue du 2026-10-01 (point 3 — Saisie des taux de change) : analyse d'une saisie décimale libre en
/// acceptant INDIFFÉREMMENT la virgule (notation française, ex. "1,17" ou "152,1552") ou le point
/// (notation anglo-saxonne, ex. "1.17" ou "152.1552") comme séparateur décimal — quelle que soit la
/// culture régionale effective de la machine sur laquelle CIMP s'exécute. Un seul endroit centralise
/// cette règle (utilisé par le convertisseur WPF <c>FlexibleDecimalConverter</c> ET testé ici
/// indépendamment de toute dépendance WPF, puisque ce projet Core est multiplateforme).
///
/// Champs concernés (tous les "champs de taux") : taux réglementaire (Réglementation -&gt; Taux %), taux
/// commercial (Taux de change -&gt; Nouveau taux vers DZD), taux manuel (Importation -&gt; Taux de change
/// manuel / Taux commercial manuel), taux de droit de douane issu d'Excel, etc.
///
/// Ne convertit JAMAIS la valeur en double/float : uniquement <see cref="decimal.TryParse(string, NumberStyles, IFormatProvider, out decimal)"/>,
/// qui préserve une précision décimale exacte (indispensable pour des montants monétaires et des taux).
/// </summary>
public static class FlexibleDecimalParser
{
    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");

    private const NumberStyles ParseStyles =
        NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    /// <summary>
    /// Essaie d'interpréter <paramref name="text"/> comme un nombre decimal, en acceptant à la fois la
    /// virgule et le point comme séparateur décimal (jamais de séparateur de milliers, pour éviter toute
    /// ambiguïté entre les deux notations). Exemples acceptés : "1,17", "1.17", "152,1552", "152.1552".
    /// </summary>
    public static bool TryParse(string? text, out decimal result)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            result = 0m;
            return false;
        }

        string trimmed = text.Trim();

        // 1) Essai avec la virgule comme séparateur décimal (notation française).
        if (decimal.TryParse(trimmed, ParseStyles, FrenchCulture, out result))
            return true;

        // 2) Essai avec le point comme séparateur décimal (notation invariante/anglo-saxonne).
        if (decimal.TryParse(trimmed, ParseStyles, CultureInfo.InvariantCulture, out result))
            return true;

        result = 0m;
        return false;
    }
}
