using System;

namespace ImportCostAlgeria.Core.Services;

/// <summary>
/// Revue du 2026-10-02 (REFONTE INTERFACE — Sections 15/16/17/26) : formules du bénéfice commercial
/// (Prix de vente vs. coût de revient réel), centralisées ICI (et non dans le code-behind / ViewModel WPF
/// — Section 30 : "les calculs doivent rester dans les services/moteurs appropriés") afin de rester
/// testables indépendamment de WPF (le projet de présentation cible net8.0-windows/WPF, non accessible
/// depuis le projet de tests multiplateforme). Utilise exclusivement <see cref="decimal"/> (jamais
/// double/float — Section 23).
///
/// IMPORTANT : ces formules sont PUREMENT commerciales/informatives — elles n'interviennent JAMAIS dans le
/// calcul douanier/fiscal (valeur en douane, droits, taxes, coût de revient), qui reste strictement du
/// ressort de <c>ImportCalculationOrchestrator</c> (ImportCostAlgeria.CalculationEngine).
/// </summary>
public static class ProfitCalculator
{
    /// <summary>Bénéfice unitaire = Prix de vente unitaire - PU Reviens (coût de revient réel par unité).</summary>
    public static decimal ComputeProfit(decimal salePriceDzd, decimal unitCostOfGoodsDzd) =>
        salePriceDzd - unitCostOfGoodsDzd;

    /// <summary>
    /// % Bénéfice = Bénéfice / PU Reviens × 100 — marge calculée par rapport au COÛT DE REVIENT (jamais par
    /// rapport au prix de vente). Retourne null si le coût de revient unitaire est nul (division par zéro
    /// jamais silencieuse ni remplacée par une valeur inventée).
    /// </summary>
    public static decimal? ComputeProfitPercent(decimal profitDzd, decimal unitCostOfGoodsDzd) =>
        unitCostOfGoodsDzd == 0m
            ? null
            : Math.Round(profitDzd / unitCostOfGoodsDzd * 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Revue du 2026-10-02 (Section 8 — affichage de la valeur en douane dans une devise choisie, SANS
    /// jamais modifier le calcul réglementaire qui reste en DZD) : convertit un montant DZD vers la devise
    /// d'affichage choisie à partir du taux "1 devise = X DZD" déjà résolu (le même type de taux que celui
    /// utilisé pour le calcul douanier réglementaire — jamais le taux commercial d'autorisation). Retourne
    /// null si aucun taux n'est disponible (jamais un repli silencieux sur 0 ou sur le montant DZD brut).
    /// </summary>
    public static decimal? ConvertDzdToDisplayCurrency(decimal amountDzd, decimal? rateCurrencyToDzd) =>
        (rateCurrencyToDzd.HasValue && rateCurrencyToDzd.Value > 0m)
            ? Math.Round(amountDzd / rateCurrencyToDzd.Value, 2, MidpointRounding.AwayFromZero)
            : null;
}
