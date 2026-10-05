using System.Collections.Generic;
using System.Linq;
using ImportCostAlgeria.Core.Domain;

namespace ImportCostAlgeria.CalculationEngine;

/// <summary>
/// Regroupement des anomalies AU NIVEAU DE L'IMPORTATION (demande utilisateur, Section 7 — "Notifications"
/// : "Je veux une notification au niveau de l'importation... Le logiciel doit regrouper les anomalies par
/// type. NE PAS afficher 25 fois le même message."). Logique PURE, indépendante de toute UI/WPF, donc
/// testable directement : transforme la liste brute <see cref="CalculationAnomaly"/> (une entrée par
/// article concerné) en une liste compacte d'un résumé par CODE d'anomalie (Section 7 : "Notification =
/// niveau importation, Détails = niveau article" — la liste brute d'origine reste intégralement disponible
/// pour le détail par article, voir <see cref="ImportCalculationSummary.Anomalies"/>, rien n'est supprimé).
/// </summary>
public sealed record AnomalyGroupSummary(
    AnomalySeverity Severity,
    string AnomalyCode,
    string LabelFr,
    int OccurrenceCount,
    int AffectedLineCount);

public static class AnomalyGroupingService
{
    /// <summary>Regroupe les anomalies par code, triées par sévérité décroissante puis par nombre d'occurrences décroissant.</summary>
    public static IReadOnlyList<AnomalyGroupSummary> GroupByCode(IReadOnlyList<CalculationAnomaly> anomalies)
    {
        return anomalies
            .GroupBy(a => a.AnomalyCode)
            .Select(g =>
            {
                var top = g.OrderByDescending(a => a.Severity).First();
                int distinctLines = g.Select(a => a.LineNumber).Where(n => n.HasValue).Distinct().Count();
                int occurrences = g.Count();
                return new AnomalyGroupSummary(
                    Severity: top.Severity,
                    AnomalyCode: g.Key,
                    LabelFr: BuildLabel(g.Key, distinctLines, occurrences),
                    OccurrenceCount: occurrences,
                    AffectedLineCount: distinctLines);
            })
            .OrderByDescending(x => x.Severity)
            .ThenByDescending(x => x.OccurrenceCount)
            .ToList();
    }

    /// <summary>
    /// Libellé FRANÇAIS lisible d'un groupe (ex : "25 articles avec TVA par défaut"), repris le plus
    /// fidèlement possible de l'exemple donné par l'utilisateur. Les codes non reconnus explicitement
    /// reçoivent un libellé générique (jamais une ligne vide ni une exception) afin qu'une future anomalie
    /// ajoutée au moteur de calcul s'affiche toujours correctement sans modification de cette table.
    /// </summary>
    private static string BuildLabel(string anomalyCode, int lineCount, int occurrenceCount)
    {
        string articleWord = lineCount == 1 ? "article" : "articles";
        return anomalyCode switch
        {
            "DD_DEFAULT_RATE_USED" => $"{lineCount} {articleWord} avec Droit de Douane (DD) par défaut",
            "CS_DEFAULT_RATE_USED" => $"{lineCount} {articleWord} avec Contribution de Solidarité (CS) par défaut",
            "TVA_DEFAULT_RATE_USED" => $"{lineCount} {articleWord} avec TVA par défaut",
            "PRCT_DEFAULT_RATE_USED" => $"{lineCount} {articleWord} avec Précompte à l'importation (PRCT) par défaut",
            "TCS_DEFAULT_RATE_USED" => $"{lineCount} {articleWord} avec Taxe de Contribution de Solidarité (TCS) par défaut",
            "MISSING_HS_CODE" => $"{lineCount} {articleWord} sans code SH confirmé",
            "EXCEL_VS_REGULATORY_DUTY_DIFF" => $"{lineCount} {articleWord} avec un taux de Droit de Douane saisi manuellement (Excel)",
            "EXCEL_VS_AI_DUTY_DIFF" => $"{lineCount} {articleWord} avec un Droit de Douane Excel différent de la proposition IA",
            "AI_DUTY_RATE_FORCED_BUT_UNAVAILABLE" => $"{lineCount} {articleWord} avec « Forcer DD IA » coché sans proposition IA disponible",
            "EXCEL_DUTY_RATE_SUSPICIOUSLY_LOW" => $"{lineCount} {articleWord} avec un Droit de Douane Excel anormalement faible (< 1 %)",
            "REGULATORY_RULE_NOT_FOUND" => $"{lineCount} {articleWord} sans règle réglementaire officielle trouvée",
            "MANUAL_VAT_RATE_USED" => $"{lineCount} {articleWord} avec un taux de TVA saisi manuellement",
            "PRCT_MANUAL_RATE_USED" => $"{lineCount} {articleWord} avec un taux PRCT saisi manuellement",
            "TCS_MANUAL_RATE_USED" => $"{lineCount} {articleWord} avec un taux TCS saisi manuellement",
            "CS_RATE_NOT_DETERMINED" => $"{lineCount} {articleWord} avec Contribution de Solidarité (CS) non déterminée",
            "PRCT_RATE_NOT_DETERMINED" => $"{lineCount} {articleWord} avec Précompte (PRCT) non déterminé",
            "TCS_RATE_NOT_DETERMINED" => $"{lineCount} {articleWord} avec Taxe de Contribution de Solidarité (TCS) non déterminée",
            "MANUAL_FEE_ALLOCATION_INCOMPLETE" => $"{occurrenceCount} frais non entièrement réparti(s) (allocation manuelle)",
            "CFR_POSSIBLE_FREIGHT_DOUBLE_COUNT" => "Fret possiblement compté deux fois (Incoterm CFR)",
            "MISSING_ORIGIN" => $"{lineCount} {articleWord} sans pays d'origine",
            "UNCONFIRMED_AI_HS_CODE" => $"{lineCount} {articleWord} avec un code SH proposé par IA non confirmé",
            "VAT_EXEMPTION_REASON_MISSING" => $"{lineCount} {articleWord} en exonération de TVA sans motif précisé",
            "MANUAL_EXCHANGE_RATE_DIFF" => "Taux de change manuel différent du taux officiel",
            "MANUAL_COMMERCIAL_EXCHANGE_RATE_DIFF" => "Taux commercial manuel différent du taux officiel",
            _ when lineCount > 0 => $"{lineCount} {articleWord} concerné(s) : {anomalyCode}",
            _ => $"{occurrenceCount} occurrence(s) : {anomalyCode}"
        };
    }
}
