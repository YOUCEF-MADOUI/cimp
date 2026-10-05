using System;
using System.Collections.Generic;
using System.Linq;
using ImportCostAlgeria.CalculationEngine;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.RegulatoryEngine;

namespace ImportCostAlgeria.AI;

/// <summary>
/// Entrée d'analyse pour la classification SH par IA (Section 16 ; enrichie lors de la revue du
/// 2026-10-01, point 4 : Matière/Fonction/présence d'un document technique viennent s'ajouter, en fin
/// de liste et avec une valeur par défaut, pour ne jamais casser les appels existants).
/// </summary>
public sealed record HsClassificationInput(
    string Reference,
    string Designation,
    string? Description,
    string? OriginCountryIso2,
    string? AdditionalInformation,
    string? Material = null,
    string? Function = null,
    bool HasTechnicalDocumentOrPhoto = false);

/// <summary>
/// Revue du 2026-10-01 (point 4 — Classification IA du code SH) : UN candidat de classification SH parmi
/// (au maximum) les 3 proposés par <see cref="IHsClassificationService.ClassifyCandidates"/>. Chaque
/// candidat reste une PROPOSITION : il ne devient un code SH définitif qu'après validation humaine
/// explicite (voir <see cref="HSClassifierService.ApplyUserDecisionOnImportLine"/>).
/// </summary>
public sealed record HsClassificationCandidate(
    string HsCode10,
    string TariffDescriptionFr,
    string JustificationFr,
    decimal ConfidencePercent,
    string GeneralInterpretiveRuleUsed,
    // Éléments de la saisie utilisateur (mots-clés, matière, fonction...) qui ont conduit à ce classement.
    IReadOnlyList<string> SupportingInformationFr,
    // Codes SH alternatifs plausibles (ambiguïtés douanières réelles connues pour cette famille de produits), avec leur propre justification.
    IReadOnlyList<HsClassificationAlternative> AlternativeCandidates,
    // Informations manquantes qui permettraient de trancher avec un niveau de confiance plus élevé (jamais inventées : énoncées explicitement).
    IReadOnlyList<string> MissingInformationFr);

/// <summary>Un code SH alternatif à un candidat principal, avec sa propre justification (point 4 de la revue).</summary>
public sealed record HsClassificationAlternative(
    string HsCode10,
    string TariffDescriptionFr,
    string JustificationFr);

/// <summary>
/// Revue du 2026-10-01 (point 4 — Classification IA du code SH) : abstraction du service de
/// classification, afin de pouvoir commencer avec un service local (règles/mots-clés, voir
/// <see cref="HSClassifierService"/>) puis connecter ultérieurement un vrai fournisseur IA externe
/// (OpenAI, Azure AI, etc.) SANS changer le reste de l'application ni l'écran de validation humaine.
/// Aucune implémentation de cette interface ne doit jamais valider un code SH de façon définitive :
/// elle ne fait QUE proposer des candidats soumis à validation humaine (voir
/// <see cref="HSClassifierService.ApplyUserDecisionOnImportLine"/>).
/// </summary>
public interface IHsClassificationService
{
    /// <summary>Identifiant de la source/version du service (traçabilité de la proposition — point 4 de la revue).</summary>
    string ServiceNameAndVersion { get; }

    /// <summary>
    /// Propose JUSQU'À 3 codes SH candidats, classés par pertinence décroissante, à partir de la
    /// description produit, de ses caractéristiques disponibles (matière, fonction...), de son origine et
    /// d'éventuelles informations complémentaires. Si les informations fournies sont insuffisantes pour
    /// identifier ne serait-ce qu'un candidat plausible, retourne UN SEUL candidat "INFORMATION NON
    /// DÉTERMINÉE" explicitant les informations manquantes — n'invente JAMAIS de candidats supplémentaires
    /// uniquement pour en afficher 3.
    /// </summary>
    IReadOnlyList<HsClassificationCandidate> ClassifyCandidates(HsClassificationInput input);
}

/// <summary>
/// Service de classification SH par IA avec interdiction absolue de modifier automatiquement
/// le code SH définitif sans validation explicite de l'utilisateur (Section 16 & 39).
///
/// Revue du 2026-10-01 (point 4) : implémente désormais <see cref="IHsClassificationService"/> pour
/// exposer une classification à PLUSIEURS candidats (<see cref="ClassifyCandidates"/>), en plus de la
/// méthode historique <see cref="ProposeHsCode"/> (un seul candidat) conservée telle quelle pour ne pas
/// modifier le comportement déjà testé et utilisé par <see cref="RegulatoryAssistantEngine"/>. Reste un
/// service purement LOCAL à base de règles/mots-clés (aucun appel réseau, aucune dépendance à un
/// fournisseur IA externe) : un futur fournisseur réel n'aurait qu'à implémenter la même interface.
/// </summary>
public sealed class HSClassifierService : IHsClassificationService
{
    /// <inheritdoc />
    public string ServiceNameAndVersion => "CIMP — Classification SH locale à base de règles (mots-clés), v1";

    private static readonly IReadOnlyList<(string[] Keywords, string HsCode10, string TariffDescFr, decimal Confidence, string RgiRule, string Justification)> KnowledgeBase = new[]
    {
        (
            new[] { "ENGINE MOUNTING", "SUPPORT MOTEUR", "SILENTBLOC MOTEUR", "MOUNTING" },
            "8708.99.90.00",
            "Autres parties et accessoires des véhicules automobiles des n°s 87.01 à 87.05",
            87.0m,
            "RGI 1 et RGI 6 (Section XVII, Chapitre 87, Note 2 et 3)",
            "Pièce mécanique antivibratoire identifiable comme étant exclusivement ou principalement destinée aux véhicules automobiles du Chapitre 87."
        ),
        (
            new[] { "FILTRE HYDRAULIQUE", "HYDRAULIC FILTER", "FILTRE HUILE", "OIL FILTER" },
            "8421.29.90.00",
            "Appareils pour la filtration ou l'épuration des liquides — Autres",
            91.5m,
            "RGI 1 et RGI 6 (Section XVI, Chapitre 84, Position 84.21)",
            "Appareil de filtration pour fluides hydrauliques industriels relevant spécifiquement de la position 84.21."
        ),
        (
            new[] { "ROULEMENT A BILLES", "BALL BEARING", "ROULEMENT" },
            "8482.10.00.00",
            "Roulements à billes",
            94.0m,
            "RGI 1 (Section XVI, Chapitre 84, Position 84.82)",
            "Les roulements à billes sont dénommés spécifiquement à la sous-position 8482.10 indépendamment de la machine de destination (Note 2 Section XVI)."
        )
    };

    /// <summary>
    /// Codes SH alternatifs PLAUSIBLES et réellement documentés en pratique douanière pour chaque famille
    /// de produits de <see cref="KnowledgeBase"/> (jamais inventés arbitrairement) : utilisés uniquement
    /// par <see cref="ClassifyCandidates"/> pour enrichir l'affichage à l'utilisateur de "codes alternatifs"
    /// et des informations manquantes qui permettraient de trancher.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string HsCode10, string TariffDescFr, string JustificationFr)[]> AlternativesByPrimaryHsCode =
        new Dictionary<string, (string, string, string)[]>
        {
            ["8708.99.90.00"] = new[]
            {
                ("4016.99.90.00", "Autres ouvrages en caoutchouc vulcanisé non durci",
                 "Si la pièce est composée PRINCIPALEMENT de caoutchouc (et non de métal), le classement peut basculer du Chapitre 87 vers le Chapitre 40 : la composition matière exacte doit être confirmée avant validation définitive.")
            },
            ["8421.29.90.00"] = new[]
            {
                ("8421.99.00.00", "Parties d'appareils pour la filtration ou l'épuration des liquides ou des gaz",
                 "S'il s'agit uniquement d'un élément/cartouche filtrant destiné à être inséré dans un appareil existant (et non de l'appareil de filtration complet), le classement correct est généralement une PARTIE (84.21.99) et non l'appareil lui-même (84.21.29).")
            },
            ["8482.10.00.00"] = new[]
            {
                ("8483.20.00.00", "Paliers avec roulements incorporés",
                 "Si la pièce livrée est un PALIER complet incluant déjà le roulement intégré (et pas seulement le roulement nu), le classement correct est la position 84.83 (paliers) et non 84.82 (roulements seuls).")
            }
        };

    public HsClassificationAiProposal ProposeHsCode(HsClassificationInput input)
    {
        string haystack = $"{input.Reference} {input.Designation} {input.Description} {input.AdditionalInformation}".ToUpperInvariant();

        foreach (var entry in KnowledgeBase)
        {
            if (entry.Keywords.Any(k => haystack.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                return new HsClassificationAiProposal(
                    ProposalId: Guid.NewGuid(),
                    ProductReference: input.Reference,
                    ProductDesignation: input.Designation,
                    OriginCountryIso2: input.OriginCountryIso2,
                    ProposedHsCode10: entry.HsCode10,
                    ProposedTariffDescriptionFr: entry.TariffDescFr,
                    ConfidencePercent: entry.Confidence,
                    JustificationFr: entry.Justification,
                    GeneralInterpretiveRuleUsed: entry.RgiRule,
                    DecisionStatus: AiProposalDecision.PendingUserValidation,
                    DataTag: DataOriginTag.PropositionIa);
            }
        }

        return new HsClassificationAiProposal(
            ProposalId: Guid.NewGuid(),
            ProductReference: input.Reference,
            ProductDesignation: input.Designation,
            OriginCountryIso2: input.OriginCountryIso2,
            ProposedHsCode10: "INFORMATION NON DÉTERMINÉE",
            ProposedTariffDescriptionFr: "Description technique insuffisante pour proposer une sous-position à 10 chiffres avec certitude.",
            ConfidencePercent: 0m,
            JustificationFr: "Conformément à la Section 41, aucun code SH n'est inventé en l'absence d'éléments techniques suffisants.",
            GeneralInterpretiveRuleUsed: "RGI 1 — Information complémentaire requise",
            DecisionStatus: AiProposalDecision.PendingUserValidation,
            DataTag: DataOriginTag.PropositionIa);
    }

    /// <inheritdoc />
    public IReadOnlyList<HsClassificationCandidate> ClassifyCandidates(HsClassificationInput input)
    {
        string haystack = $"{input.Reference} {input.Designation} {input.Description} {input.Material} {input.Function} {input.AdditionalInformation}"
            .ToUpperInvariant();

        // On choisit l'entrée de la base de connaissances dont le PLUS GRAND NOMBRE de mots-clés
        // correspond réellement à la saisie (jamais un candidat choisi arbitrairement) : en cas d'égalité,
        // on conserve l'ordre déclaratif de la base de connaissances.
        var scoredEntries = KnowledgeBase
            .Select(entry => new
            {
                Entry = entry,
                MatchedKeywords = entry.Keywords.Where(k => haystack.Contains(k, StringComparison.OrdinalIgnoreCase)).ToArray()
            })
            .Where(x => x.MatchedKeywords.Length > 0)
            .OrderByDescending(x => x.MatchedKeywords.Length)
            .ToList();

        var missingInfoIfAny = BuildMissingInformation(input);

        if (scoredEntries.Count == 0)
        {
            // Aucun mot-clé reconnu : conformément à la Section 41 et au point 4 de la revue du
            // 2026-10-01, on NE FABRIQUE PAS 3 candidats arbitraires — un seul candidat "INFORMATION NON
            // DÉTERMINÉE" est retourné, avec la liste complète des informations manquantes.
            return new[]
            {
                new HsClassificationCandidate(
                    HsCode10: "INFORMATION NON DÉTERMINÉE",
                    TariffDescriptionFr: "Description technique insuffisante pour proposer une sous-position à 10 chiffres avec certitude.",
                    JustificationFr: "Conformément à la Section 41, aucun code SH n'est inventé en l'absence d'éléments techniques suffisants.",
                    ConfidencePercent: 0m,
                    GeneralInterpretiveRuleUsed: "RGI 1 — Information complémentaire requise",
                    SupportingInformationFr: Array.Empty<string>(),
                    AlternativeCandidates: Array.Empty<HsClassificationAlternative>(),
                    MissingInformationFr: missingInfoIfAny.Count > 0
                        ? missingInfoIfAny
                        : new[] { "Description détaillée du produit (matière, fonction, mode de fonctionnement) absente ou insuffisamment précise." })
            };
        }

        var best = scoredEntries[0];
        var supportingInfo = BuildSupportingInformation(input, best.MatchedKeywords);
        var alternatives = AlternativesByPrimaryHsCode.TryGetValue(best.Entry.HsCode10, out var alts)
            ? alts
            : Array.Empty<(string HsCode10, string TariffDescFr, string JustificationFr)>();

        var candidates = new List<HsClassificationCandidate>
        {
            new(
                HsCode10: best.Entry.HsCode10,
                TariffDescriptionFr: best.Entry.TariffDescFr,
                JustificationFr: best.Entry.Justification,
                ConfidencePercent: best.Entry.Confidence,
                GeneralInterpretiveRuleUsed: best.Entry.RgiRule,
                SupportingInformationFr: supportingInfo,
                AlternativeCandidates: alternatives.Select(a => new HsClassificationAlternative(a.HsCode10, a.TariffDescFr, a.JustificationFr)).ToArray(),
                MissingInformationFr: missingInfoIfAny)
        };

        // Les codes alternatifs documentés pour la famille de produits identifiée deviennent eux-mêmes des
        // candidats à part entière (2e et 3e candidat), avec une confiance réduite reflétant qu'ils ne
        // sont retenus que si une caractéristique précise (matière, nature exacte de la pièce...) diffère
        // de l'hypothèse par défaut — jamais 3 candidats sans rapport entre eux.
        foreach (var alt in alternatives.Take(2))
        {
            decimal altConfidence = Math.Max(best.Entry.Confidence - 35m, 10m);
            candidates.Add(new HsClassificationCandidate(
                HsCode10: alt.HsCode10,
                TariffDescriptionFr: alt.TariffDescFr,
                JustificationFr: alt.JustificationFr,
                ConfidencePercent: altConfidence,
                GeneralInterpretiveRuleUsed: best.Entry.RgiRule,
                SupportingInformationFr: supportingInfo,
                AlternativeCandidates: Array.Empty<HsClassificationAlternative>(),
                MissingInformationFr: missingInfoIfAny));
        }

        return candidates.Take(3).ToList();
    }

    /// <summary>Formule, en français, les éléments de la saisie qui ont concrètement conduit au classement (jamais une justification générique non vérifiable).</summary>
    private static List<string> BuildSupportingInformation(HsClassificationInput input, string[] matchedKeywords)
    {
        var items = new List<string>
        {
            $"Mot(s)-clé(s) reconnu(s) dans la désignation/description : {string.Join(", ", matchedKeywords)}."
        };

        if (!string.IsNullOrWhiteSpace(input.Material))
            items.Add($"Matière déclarée : {input.Material}.");
        if (!string.IsNullOrWhiteSpace(input.Function))
            items.Add($"Fonction déclarée : {input.Function}.");
        if (!string.IsNullOrWhiteSpace(input.OriginCountryIso2))
            items.Add($"Origine déclarée : {input.OriginCountryIso2}.");
        if (!string.IsNullOrWhiteSpace(input.Description))
            items.Add($"Description complémentaire fournie : {input.Description}.");
        if (input.HasTechnicalDocumentOrPhoto)
            items.Add("Une fiche technique ou une photo a été jointe au dossier.");

        return items;
    }

    /// <summary>Énonce explicitement ce qui manque pour fiabiliser le classement — jamais une estimation inventée à la place.</summary>
    private static List<string> BuildMissingInformation(HsClassificationInput input)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(input.Material))
            missing.Add("Composition matière (métal, caoutchouc, plastique, composite...) non précisée : déterminante pour écarter certaines ambiguïtés de classement (ex. Chapitre 40 vs Chapitre 87/84).");
        if (string.IsNullOrWhiteSpace(input.Function))
            missing.Add("Fonction précise de la pièce (ce à quoi elle sert exactement, sur quel ensemble elle se monte) non précisée.");
        if (!input.HasTechnicalDocumentOrPhoto)
            missing.Add("Aucune fiche technique ni photo fournie : un document technique (plan, fiche produit, photo) permettrait de confirmer ce classement avec un niveau de confiance plus élevé.");

        return missing;
    }

    /// <summary>
    /// Applique la décision explicite de l'utilisateur ([CONFIRMER], [MODIFIER], [REFUSER]) sur une ligne d'importation.
    /// </summary>
    public void ApplyUserDecisionOnImportLine(
        ImportLine targetLine,
        HsClassificationAiProposal proposal,
        AiProposalDecision userDecision,
        string? userModifiedHsCode10 = null)
    {
        switch (userDecision)
        {
            case AiProposalDecision.ConfirmedByUser:
                targetLine.AiProposedHsCode10 = proposal.ProposedHsCode10;
                targetLine.HsCodeConfirmed10 = proposal.ProposedHsCode10;
                targetLine.AiHsDecision = AiProposalDecision.ConfirmedByUser;
                break;

            case AiProposalDecision.ModifiedByUser:
                if (string.IsNullOrWhiteSpace(userModifiedHsCode10))
                    throw new ArgumentException("Le nouveau Code SH saisi par l'utilisateur est obligatoire lors d'une action [MODIFIER].");
                targetLine.AiProposedHsCode10 = proposal.ProposedHsCode10;
                targetLine.HsCodeConfirmed10 = userModifiedHsCode10.Trim();
                targetLine.AiHsDecision = AiProposalDecision.ModifiedByUser;
                break;

            case AiProposalDecision.RejectedByUser:
                targetLine.AiProposedHsCode10 = proposal.ProposedHsCode10;
                targetLine.AiHsDecision = AiProposalDecision.RejectedByUser;
                // Le code SH confirmé n'est pas modifié
                break;

            default:
                throw new InvalidOperationException("Une proposition IA ne peut jamais modifier le code SH définitif sans action [CONFIRMER] ou [MODIFIER].");
        }
    }
}

/// <summary>
/// Moteur de l'Assistant IA Réglementaire et Métier (Section 29).
/// Distingue systématiquement :
/// - DONNÉE OFFICIELLE
/// - DONNÉE UTILISATEUR
/// - CALCUL DU LOGICIEL
/// - PROPOSITION IA
/// </summary>
public sealed class RegulatoryAssistantEngine
{
    private readonly ImportSimulatorService _simulator;
    private readonly HSClassifierService _hsClassifier;

    /// <summary>
    /// Section 17 de l'audit : construit une citation légale à partir de la règle réglementaire
    /// RÉELLEMENT résolue pour le calcul (jamais une référence générique codée en dur). Si aucune règle
    /// officielle n'a été trouvée, l'absence est signalée explicitement plutôt que d'inventer une source.
    /// </summary>
    private static string FormatLegalCitation(string? legalArticleReference, string? joraReference)
    {
        if (string.IsNullOrWhiteSpace(legalArticleReference))
            return "INFORMATION NON DÉTERMINÉE : aucune règle réglementaire officielle associée n'a été identifiée";

        return string.IsNullOrWhiteSpace(joraReference)
            ? legalArticleReference
            : $"{legalArticleReference} — {joraReference}";
    }

    public RegulatoryAssistantEngine(ImportSimulatorService simulator, HSClassifierService hsClassifier)
    {
        _simulator = simulator;
        _hsClassifier = hsClassifier;
    }

    public RegulatoryAssistantResponse AnswerUserQuery(
        string questionFr,
        Company company,
        ImportOperation operation,
        ImportCalculationSummary currentCalculation)
    {
        string q = questionFr.ToUpperInvariant();
        var statements = new List<AssistantTaggedStatement>();

        // 1. « Montre-moi tous les articles dont le taux Excel est différent du taux réglementaire. »
        // Revue du 2026-10-05 ("DD Excel prioritaire par défaut") : le statut historique "Difference"
        // (ancienne règle où le réglementaire gagnait toujours) n'est plus jamais produit par le moteur de
        // calcul — le statut équivalent sous la nouvelle logique (Excel prioritaire par défaut, mais
        // différent de la proposition IA) est ExcelPriorityDiffersFromAi. On inclut également le cas
        // AiForcedByUserOverridingExcel (une différence existait, l'utilisateur a choisi la proposition IA)
        // pour que cette requête reste exhaustive.
        if (q.Contains("DIFFÉRENT") || q.Contains("DIFFERENT") || q.Contains("EXCEL"))
        {
            var diffs = currentCalculation.LineResults
                .Where(l => l.CustomsOutcome.ExcelVsRegulatoryComparison == DutyComparisonStatus.ExcelPriorityDiffersFromAi
                         || l.CustomsOutcome.ExcelVsRegulatoryComparison == DutyComparisonStatus.AiForcedByUserOverridingExcel)
                .ToList();

            statements.Add(new AssistantTaggedStatement(
                DataOriginTag.CalculDuLogiciel,
                "CALCUL DU LOGICIEL",
                $"Analyse comparative effectuée sur {currentCalculation.LineResults.Count} ligne(s) : {diffs.Count} article(s) présentent une différence entre le droit Excel et la proposition IA (réglementaire)."));

            foreach (var d in diffs)
            {
                var srcLine = operation.Lines.First(l => l.LineNumber == d.LineNumber);
                statements.Add(new AssistantTaggedStatement(
                    DataOriginTag.DonneeUtilisateur,
                    "DONNÉE UTILISATEUR",
                    $"Ligne {d.LineNumber} ({d.ProductReference} — {d.Designation}) : Taux saisi dans Excel = {srcLine.ExcelDutyRatePercent:F2} %."));
                statements.Add(new AssistantTaggedStatement(
                    DataOriginTag.DonneeOfficielle,
                    "DONNÉE OFFICIELLE",
                    $"Ligne {d.LineNumber} (Code SH {srcLine.HsCodeConfirmed10}, Origine {srcLine.OriginCountryIso2}) : Proposition DD IA (réglementaire) au {operation.ReferenceDate:dd/MM/yyyy} = {d.CustomsOutcome.AiProposedDutyRatePercent:F2} % — taux effectivement appliqué au calcul : {d.CustomsOutcome.CustomsDutyRatePercent:F2} % ({d.CustomsOutcome.ComparisonLabelFr})."));
            }

            return new RegulatoryAssistantResponse(questionFr, statements);
        }

        // 2. « Simule cette importation avec un prix FOB inférieur de 10 %. »
        if (q.Contains("PRIX") && q.Contains("10"))
        {
            var sim = _simulator.RunSimulation(company, operation, new SimulationScenarioOverrides(UnitPriceFactorPercent: -10m));
            statements.Add(new AssistantTaggedStatement(
                DataOriginTag.DonneeUtilisateur,
                "DONNÉE UTILISATEUR",
                "Hypothèse de simulation demandée : baisse de 10 % du prix d'achat fournisseur (sans modifier l'importation réelle)."));
            statements.Add(new AssistantTaggedStatement(
                DataOriginTag.CalculDuLogiciel,
                "CALCUL DU LOGICIEL",
                $"Coût total de revient simulé : {sim.SimulatedCalculation.TotalRealCostOfGoodsDzd:N2} DZD (contre {currentCalculation.TotalRealCostOfGoodsDzd:N2} DZD initialement), soit une économie globale de {(currentCalculation.TotalRealCostOfGoodsDzd - sim.SimulatedCalculation.TotalRealCostOfGoodsDzd):N2} DZD."));
            return new RegulatoryAssistantResponse(questionFr, statements);
        }

        // 3. « Quel serait le coût unitaire si le fret augmentait de 20 % ? »
        if (q.Contains("FRET") && q.Contains("20"))
        {
            var sim = _simulator.RunSimulation(company, operation, new SimulationScenarioOverrides(FreightFactorPercent: +20m));
            statements.Add(new AssistantTaggedStatement(
                DataOriginTag.DonneeUtilisateur,
                "DONNÉE UTILISATEUR",
                "Hypothèse de simulation demandée : hausse de +20 % du fret international."));
            foreach (var simLine in sim.SimulatedCalculation.LineResults)
            {
                // IMPORTANT (correction définitive demandée) : la valeur "AVANT" provient TOUJOURS de
                // baseLine (currentCalculation, calcul réel non modifié) et la valeur "APRÈS" provient
                // TOUJOURS de simLine (sim.SimulatedCalculation, clone simulé) — jamais simLine utilisé
                // pour les deux valeurs.
                var baseLine = currentCalculation.LineResults.First(x => x.LineNumber == simLine.LineNumber);

                decimal situationActuelleDzd = baseLine.EconomicOutcome.UnitCostOfGoodsDzd;
                decimal apresFretDzd = simLine.EconomicOutcome.UnitCostOfGoodsDzd;
                decimal differenceDzd = CurrencyCalculator.RoundDzd(apresFretDzd - situationActuelleDzd);
                decimal variationPercent = situationActuelleDzd == 0m
                    ? 0m
                    : Math.Round((differenceDzd / situationActuelleDzd) * 100m, 2, MidpointRounding.AwayFromZero);

                statements.Add(new AssistantTaggedStatement(
                    DataOriginTag.CalculDuLogiciel,
                    "CALCUL DU LOGICIEL",
                    $"Article {simLine.ProductReference} (coût de revient unitaire) :{Environment.NewLine}" +
                    $"Situation actuelle : {situationActuelleDzd:N2} DZD{Environment.NewLine}" +
                    $"Après +20 % de fret : {apresFretDzd:N2} DZD{Environment.NewLine}" +
                    $"Différence : {differenceDzd:N2} DZD{Environment.NewLine}" +
                    $"Variation : {variationPercent:N2} %"));
            }
            return new RegulatoryAssistantResponse(questionFr, statements);
        }

        // 3bis. « Quelle est la valeur en dollars de cette facture ? » / « Montant en USD ? »
        if ((q.Contains("DOLLAR") || q.Contains("USD") || q.Contains("AUTORISATION")) &&
            (q.Contains("VALEUR") || q.Contains("MONTANT") || q.Contains("COUT") || q.Contains("COÛT") || q.Contains("FACTURE")))
        {
            var conv = currentCalculation.CommercialAuthorizationConversion;
            if (conv == null)
            {
                statements.Add(new AssistantTaggedStatement(
                    DataOriginTag.CalculDuLogiciel,
                    "CALCUL DU LOGICIEL",
                    string.Equals(operation.MainCurrencyCode, operation.AuthorizationCurrencyCode, StringComparison.OrdinalIgnoreCase)
                        ? $"La facture est déjà exprimée dans la devise d'autorisation ({operation.AuthorizationCurrencyCode}) : aucune conversion commerciale n'est nécessaire. Montant facture = {currentCalculation.TotalPurchaseValueMainCurrency:N2} {operation.MainCurrencyCode}."
                        : "INFORMATION NON DÉTERMINÉE : aucun taux de change commercial n'est enregistré pour cette conversion. Veuillez publier le taux correspondant dans l'écran \"Taux de change\" avant de poser cette question."));
                return new RegulatoryAssistantResponse(questionFr, statements);
            }

            statements.Add(new AssistantTaggedStatement(
                DataOriginTag.DonneeOfficielle,
                "DONNÉE OFFICIELLE",
                $"Taux {conv.OriginalCurrencyCode}/{conv.AuthorizationCurrencyCode} utilisé : {conv.EffectiveRate:F4} ({conv.RateTypeLabelFr}{(conv.OfficialRateSourceName != null ? $" - {conv.OfficialRateSourceName}" : string.Empty)})."));
            statements.Add(new AssistantTaggedStatement(
                DataOriginTag.CalculDuLogiciel,
                "CALCUL DU LOGICIEL",
                $"Montant facture : {conv.OriginalTotalAmount:N2} {conv.OriginalCurrencyCode} × {conv.EffectiveRate:F4} = {conv.AuthorizationTotalAmount:N2} {conv.AuthorizationCurrencyCode} (valeur de l'autorisation d'importation). Pour mémoire, la valeur en douane réglementaire reste calculée séparément en DZD à partir de la devise d'origine ({conv.OriginalCurrencyCode}), jamais à partir de ce montant {conv.AuthorizationCurrencyCode} (Section 14 : conversion commerciale et conversion réglementaire ne sont jamais mélangées)."));
            return new RegulatoryAssistantResponse(questionFr, statements);
        }

        // 3ter. « Quel est le coût total en euros ? » (ou toute devise = la devise originale de la facture)
        if ((q.Contains("EN EUROS") || q.Contains("EUR") || q.Contains("DEVISE D'ORIGINE") || q.Contains("DEVISE ORIGINALE")) &&
            (q.Contains("COUT") || q.Contains("COÛT") || q.Contains("VALEUR") || q.Contains("MONTANT")))
        {
            statements.Add(new AssistantTaggedStatement(
                DataOriginTag.DonneeUtilisateur,
                "DONNÉE UTILISATEUR",
                $"Devise originale de la facture : {operation.MainCurrencyCode}."));
            statements.Add(new AssistantTaggedStatement(
                DataOriginTag.CalculDuLogiciel,
                "CALCUL DU LOGICIEL",
                $"Montant total de la facture dans sa devise originale : {currentCalculation.TotalPurchaseValueMainCurrency:N2} {operation.MainCurrencyCode}. Ce montant reste la référence commerciale (Section 4 : la devise originale n'est jamais remplacée par une conversion) ; le coût de revient économique réel calculé par le logiciel ({currentCalculation.TotalRealCostOfGoodsDzd:N2} DZD) intègre en plus les droits, taxes et frais réglementaires qui ne peuvent être déterminés qu'en DZD."));
            return new RegulatoryAssistantResponse(questionFr, statements);
        }

        // 4. « Quel est le code SH proposé pour ce produit ? »
        if (q.Contains("CODE SH") || q.Contains("SH PROPOSÉ"))
        {
            var firstLine = operation.Lines.First();
            var proposal = _hsClassifier.ProposeHsCode(new HsClassificationInput(
                firstLine.ProductReference, firstLine.Designation, null, firstLine.OriginCountryIso2, null));

            statements.Add(new AssistantTaggedStatement(
                DataOriginTag.PropositionIa,
                "PROPOSITION IA",
                $"Pour '{firstLine.Designation}' ({firstLine.ProductReference}), le Code SH proposé est {proposal.ProposedHsCode10} ({proposal.ProposedTariffDescriptionFr}) avec un niveau de confiance de {proposal.ConfidencePercent:F0} % selon {proposal.GeneralInterpretiveRuleUsed}. Justification : {proposal.JustificationFr} [Validation utilisateur requise]."));
            return new RegulatoryAssistantResponse(questionFr, statements);
        }

        // 5. « Pourquoi le droit de douane de cet article est de 15 % ? » ou question générale sur le calcul
        // Revue du 2026-10-05 ("DD Excel prioritaire par défaut") : le taux effectivement appliqué
        // (CustomsDutyRatePercent) peut désormais provenir du DD Excel (DonneeUtilisateur), d'une proposition
        // IA/réglementaire (DonneeOfficielle), du taux par défaut de l'importation (ValeurParDefautImportation)
        // ou rester non déterminé (CalculDuLogiciel) — le tag ET le libellé affichés doivent refléter
        // dynamiquement CustomsDutyRateOriginTag plutôt que de présenter à tort toute valeur comme une
        // "DONNÉE OFFICIELLE" (ancien comportement, incorrect lorsque le DD Excel l'emporte).
        var line1 = currentCalculation.LineResults.First();
        (DataOriginTag dutyTag, string dutyTagLabel) = line1.CustomsOutcome.CustomsDutyRateOriginTag switch
        {
            DataOriginTag.DonneeOfficielle => (DataOriginTag.DonneeOfficielle, "DONNÉE OFFICIELLE"),
            DataOriginTag.DonneeUtilisateur => (DataOriginTag.DonneeUtilisateur, "DONNÉE UTILISATEUR"),
            DataOriginTag.ValeurParDefautImportation => (DataOriginTag.ValeurParDefautImportation, "VALEUR PAR DÉFAUT (IMPORTATION)"),
            _ => (DataOriginTag.CalculDuLogiciel, "CALCUL DU LOGICIEL")
        };
        string dutyExplanationFr = line1.CustomsOutcome.CustomsDutyRateOriginTag == DataOriginTag.DonneeOfficielle
            ? $"le droit de douane de {line1.CustomsOutcome.CustomsDutyRatePercent:F2} % ({FormatLegalCitation(line1.CustomsOutcome.CustomsDutyLegalArticleReference, line1.CustomsOutcome.CustomsDutyJoraReference)}) découle de la proposition IA/réglementaire"
            : $"le droit de douane de {line1.CustomsOutcome.CustomsDutyRatePercent:F2} % provient de : {line1.CustomsOutcome.ComparisonLabelFr}";
        statements.Add(new AssistantTaggedStatement(
            dutyTag,
            dutyTagLabel,
            $"Pour l'article {line1.ProductReference}, {dutyExplanationFr} ; la TVA de {line1.CustomsOutcome.VatRatePercent:F2} % ({FormatLegalCitation(line1.CustomsOutcome.VatLegalArticleReference, line1.CustomsOutcome.VatJoraReference)}) découle de la réglementation douanière en vigueur au {operation.ReferenceDate:dd/MM/yyyy}."));
        statements.Add(new AssistantTaggedStatement(
            DataOriginTag.CalculDuLogiciel,
            "CALCUL DU LOGICIEL",
            $"Coût douanier total (Droits & Taxes) = {currentCalculation.TotalDutiesAndTaxesDzd:N2} DZD | Coût de revient économique réel total = {currentCalculation.TotalRealCostOfGoodsDzd:N2} DZD."));

        return new RegulatoryAssistantResponse(questionFr, statements);
    }
}
