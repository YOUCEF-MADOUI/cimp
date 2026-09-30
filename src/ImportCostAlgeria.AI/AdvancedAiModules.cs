using System;
using System.Collections.Generic;
using System.Linq;
using ImportCostAlgeria.CalculationEngine;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.RegulatoryEngine;

namespace ImportCostAlgeria.AI;

/// <summary>
/// Entrée d'analyse pour la classification SH par IA (Section 16).
/// </summary>
public sealed record HsClassificationInput(
    string Reference,
    string Designation,
    string? Description,
    string? OriginCountryIso2,
    string? AdditionalInformation);

/// <summary>
/// Service de classification SH par IA avec interdiction absolue de modifier automatiquement
/// le code SH définitif sans validation explicite de l'utilisateur (Section 16 & 39).
/// </summary>
public sealed class HSClassifierService
{
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
        if (q.Contains("DIFFÉRENT") || q.Contains("DIFFERENT") || q.Contains("EXCEL"))
        {
            var diffs = currentCalculation.LineResults
                .Where(l => l.CustomsOutcome.ExcelVsRegulatoryComparison == DutyComparisonStatus.Difference)
                .ToList();

            statements.Add(new AssistantTaggedStatement(
                DataOriginTag.CalculDuLogiciel,
                "CALCUL DU LOGICIEL",
                $"Analyse comparative effectuée sur {currentCalculation.LineResults.Count} ligne(s) : {diffs.Count} article(s) présentent une différence entre le droit Excel et le droit réglementaire."));

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
                    $"Ligne {d.LineNumber} (Code SH {srcLine.HsCodeConfirmed10}, Origine {srcLine.OriginCountryIso2}) : Taux réglementaire applicable au {operation.ReferenceDate:dd/MM/yyyy} = {d.CustomsOutcome.CustomsDutyRatePercent:F2} % (Art. 103 Code des Douanes)."));
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
                var baseLine = currentCalculation.LineResults.First(x => x.LineNumber == simLine.LineNumber);
                statements.Add(new AssistantTaggedStatement(
                    DataOriginTag.CalculDuLogiciel,
                    "CALCUL DU LOGICIEL",
                    $"Article {simLine.ProductReference} : Le coût de revient unitaire passerait de {baseLine.EconomicOutcome.UnitCostOfGoodsDzd:N2} DZD à {simLine.EconomicOutcome.UnitCostOfGoodsDzd:N2} DZD / unité."));
            }
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
        var line1 = currentCalculation.LineResults.First();
        statements.Add(new AssistantTaggedStatement(
            DataOriginTag.DonneeOfficielle,
            "DONNÉE OFFICIELLE",
            $"Pour l'article {line1.ProductReference}, le droit de douane de {line1.CustomsOutcome.CustomsDutyRatePercent:F2} % et la TVA de {line1.CustomsOutcome.VatRatePercent:F2} % découlent de la réglementation douanière en vigueur au {operation.ReferenceDate:dd/MM/yyyy} (Art. 16 ter, 16 octies et 103 du Code des Douanes ; Art. 19 du CTCA)."));
        statements.Add(new AssistantTaggedStatement(
            DataOriginTag.CalculDuLogiciel,
            "CALCUL DU LOGICIEL",
            $"Coût douanier total (Droits & Taxes) = {currentCalculation.TotalDutiesAndTaxesDzd:N2} DZD | Coût de revient économique réel total = {currentCalculation.TotalRealCostOfGoodsDzd:N2} DZD."));

        return new RegulatoryAssistantResponse(questionFr, statements);
    }
}
