using System;
using System.Collections.Generic;
using System.Linq;
using ImportCostAlgeria.Core.Domain;

namespace ImportCostAlgeria.RegulatoryEngine;

/// <summary>
/// Contexte étendu d'évaluation réglementaire pour la V1.1 :
/// intègre la vérification du transport direct, du certificat d'origine (EUR.1, GZALE, ZLECAf),
/// des décisions d'exonération (AAPI/ANDI, Art. 10 & 11 CTCA) et des quantités physiques.
/// </summary>
public sealed record AdvancedRegulatoryContext(
    string HsCode10,
    string? OriginCountryIso2,
    string ExportShippingCountryIso2,
    DateOnly OperationReferenceDate,
    string RequestedRegimeCode,
    bool HasValidCertificateOfOrigin,
    bool IsDirectTransportVerified,
    bool HasOfficialExemptionDecision,
    string? ExemptionDecisionReference);

public sealed record PreferentialEligibilityResult(
    bool IsEligibleForPreferentialRegime,
    string EffectiveRegimeCode,
    string?FallbackReasonFr);

/// <summary>
/// Représente une proposition de modification réglementaire détectée ou préparée par l'IA (Section 21).
/// Workflow strict :
/// Nouvelle information détectée -> IA analyse -> Proposition -> Administrateur vérifie -> Validation -> Nouvelle version réglementaire.
/// </summary>
public sealed class RegulatoryRuleProposal
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string ProposalCode { get; init; }
    public required string DetectedChangeSummaryFr { get; init; }
    public required string AiAnalysisNotesFr { get; init; }
    public required RegulatoryRuleType RuleType { get; init; }
    public required string TaxCode { get; init; }
    public required string HsCode10 { get; init; }
    public string? OriginCountryIso2 { get; init; }
    public required string CustomsRegimeCode { get; init; }
    public required decimal ProposedRatePercent { get; init; }
    public required TaxableBaseType ProposedCalculationBase { get; init; }
    public required DateOnly ProposedEffectiveFrom { get; init; }
    public required LegalSource ProposedLegalSource { get; init; }
    public Guid? PreviousRuleToSupersedeId { get; init; }
    public RegulatoryRuleStatus WorkflowStatus { get; set; } = RegulatoryRuleStatus.Detected;
    public Guid? VerifiedByAdminUserId { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public string? AdminVerificationComment { get; set; }
}

/// <summary>
/// Comparaison entre deux versions réglementaires pour un même Code SH (V1.1).
/// </summary>
public sealed record RegulatoryRuleVersionDiff(
    string HsCode10,
    string TaxCode,
    string OldVersionCode,
    decimal OldRatePercent,
    DateOnly OldValidFrom,
    DateOnly? OldValidTo,
    string NewVersionCode,
    decimal NewRatePercent,
    DateOnly NewValidFrom,
    decimal DeltaPercentagePoints,
    string LegalSourceReference);

/// <summary>
/// Services réglementaires avancés (V1.1 — Sections 18, 19, 20, 21, 23).
/// </summary>
public sealed class AdvancedRegulatoryService
{
    private readonly IRegulatoryRuleRepository _repository;

    public AdvancedRegulatoryService(IRegulatoryRuleRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// Vérifie les conditions juridiques d'éligibilité aux régimes préférentiels (UE, GZALE, ZLECAf)
    /// et aux régimes d'exonération (AAPI/ANDI, Art. 11 CTCA).
    /// Si les conditions documentaires ou d'expédition directe ne sont pas remplies,
    /// le moteur refuse d'appliquer automatiquement le taux réduit et bascule sur le Droit Commun avec justification.
    /// </summary>
    public PreferentialEligibilityResult EvaluateRegimeEligibility(AdvancedRegulatoryContext context)
    {
        if (string.Equals(context.RequestedRegimeCode, "DROIT_COMMUN_4000", StringComparison.OrdinalIgnoreCase))
        {
            return new PreferentialEligibilityResult(true, "DROIT_COMMUN_4000", null);
        }

        // Cas 1 : Régime préférentiel conventionnel (ex: PREF_UE, PREF_GZALE, PREF_ZLECAF)
        if (context.RequestedRegimeCode.StartsWith("PREF_", StringComparison.OrdinalIgnoreCase))
        {
            if (!context.HasValidCertificateOfOrigin)
            {
                return new PreferentialEligibilityResult(
                    IsEligibleForPreferentialRegime: false,
                    EffectiveRegimeCode: "DROIT_COMMUN_4000",
                    FallbackReasonFr: $"Régime '{context.RequestedRegimeCode}' écarté : Certificat d'origine préférentiel absent. Bascule sur le régime de Droit Commun.");
            }

            if (!context.IsDirectTransportVerified &&
                !string.Equals(context.OriginCountryIso2, context.ExportShippingCountryIso2, StringComparison.OrdinalIgnoreCase))
            {
                return new PreferentialEligibilityResult(
                    IsEligibleForPreferentialRegime: false,
                    EffectiveRegimeCode: "DROIT_COMMUN_4000",
                    FallbackReasonFr: $"Régime '{context.RequestedRegimeCode}' écarté : Le pays d'expédition ({context.ExportShippingCountryIso2}) diffère du pays d'origine ({context.OriginCountryIso2}) sans justification de transport direct. Bascule sur le Droit Commun (Art. 14 & 15 CDA).");
            }

            return new PreferentialEligibilityResult(true, context.RequestedRegimeCode, null);
        }

        // Cas 2 : Régime d'exonération / franchise (ex: ANDI_AAPI, FRANCHISE_ART_11_CTCA)
        if (context.RequestedRegimeCode.Contains("ANDI", StringComparison.OrdinalIgnoreCase) ||
            context.RequestedRegimeCode.Contains("EXONERATION", StringComparison.OrdinalIgnoreCase))
        {
            if (!context.HasOfficialExemptionDecision || string.IsNullOrWhiteSpace(context.ExemptionDecisionReference))
            {
                return new PreferentialEligibilityResult(
                    IsEligibleForPreferentialRegime: false,
                    EffectiveRegimeCode: "DROIT_COMMUN_4000",
                    FallbackReasonFr: $"Exonération '{context.RequestedRegimeCode}' écartée : Décision ou attestation officielle d'exonération non renseignée (Section 41 — Interdiction d'inventer une exonération).");
            }

            return new PreferentialEligibilityResult(true, context.RequestedRegimeCode, null);
        }

        return new PreferentialEligibilityResult(true, context.RequestedRegimeCode, null);
    }

    /// <summary>
    /// Exécute le workflow complet de validation d'une proposition réglementaire IA par un Administrateur (Section 21).
    /// Archive proprement l'ancienne règle (en fermant sa date de fin à J-1 sans l'écraser) et crée la nouvelle version.
    /// </summary>
    public RegulatoryRule ApproveProposalAndCreateNewVersion(
        RegulatoryRuleProposal proposal,
        RegulatoryRule? previousRuleToClose,
        Guid adminUserId,
        UserRole executorRole,
        string newRegulatoryVersionCode,
        string adminComment)
    {
        if (executorRole != UserRole.Administrateur)
        {
            throw new InvalidOperationException("Sécurité réglementaire (Section 21 & 39) : Seul un Administrateur peut valider et publier une nouvelle version réglementaire.");
        }

        if (!proposal.ProposedLegalSource.IsOfficialBinding)
        {
            throw new InvalidOperationException("Hiérarchie des sources (Section 20) : Une information provenant d'une source secondaire (Niveau 6) ne peut jamais remplacer une règle officielle.");
        }

        if (previousRuleToClose != null)
        {
            if (proposal.ProposedEffectiveFrom <= previousRuleToClose.ValidFrom)
            {
                throw new InvalidOperationException("Incohérence de versionnage : La date d'effet de la nouvelle règle doit être postérieure à celle de la règle précédente.");
            }
        }

        proposal.WorkflowStatus = RegulatoryRuleStatus.AdminVerified;
        proposal.VerifiedByAdminUserId = adminUserId;
        proposal.VerifiedAtUtc = DateTime.UtcNow;
        proposal.AdminVerificationComment = adminComment;

        var publishedRule = new RegulatoryRule
        {
            Code = $"{proposal.TaxCode}-{RegulatoryRuleEngine.NormalizeHsCode(proposal.HsCode10)}-{newRegulatoryVersionCode}",
            RegulatoryVersionCode = newRegulatoryVersionCode,
            SupersedesRuleId = previousRuleToClose?.Id ?? proposal.PreviousRuleToSupersedeId,
            RuleType = proposal.RuleType,
            TaxCode = proposal.TaxCode,
            TaxNameFr = proposal.TaxCode switch
            {
                "DD" => "Droit de Douane",
                "TVA" => "Taxe sur la Valeur Ajoutée",
                "DAPS" => "Droit Additionnel Provisoire de Sauvegarde",
                "TIC" => "Taxe Intérieure de Consommation",
                // Revue du 2026-10-01 (point 5) : libellés d'affichage uniquement (aucun taux fixé ici,
                // le taux reste toujours celui de la RegulatoryRule publiée par un Administrateur).
                "PRCT" => "Précompte à l'importation",
                "TCS" => "Taxe de Contribution de Solidarité",
                _ => proposal.TaxCode
            },
            HsCode10 = proposal.HsCode10,
            OriginCountryIso2 = proposal.OriginCountryIso2,
            CustomsRegimeCode = proposal.CustomsRegimeCode,
            RatePercent = proposal.ProposedRatePercent,
            CalculationBase = proposal.ProposedCalculationBase,
            ValidFrom = proposal.ProposedEffectiveFrom,
            ValidTo = null,
            LegalSource = proposal.ProposedLegalSource,
            Status = RegulatoryRuleStatus.PublishedNewVersion,
            IsProposedByAi = true,
            ValidatedByAdminUserId = adminUserId,
            ValidatedAtUtc = DateTime.UtcNow
        };

        proposal.WorkflowStatus = RegulatoryRuleStatus.PublishedNewVersion;
        return publishedRule;
    }

    /// <summary>
    /// Calcule l'historique des évolutions tarifaires d'un Code SH sans écrasement (Section 19).
    /// </summary>
    public IReadOnlyList<RegulatoryRuleVersionDiff> GetVersionHistoryDiffs(string hsCode10, string taxCode)
    {
        string normalized = RegulatoryRuleEngine.NormalizeHsCode(hsCode10);
        var rules = _repository.GetCandidateRules(normalized)
            .Where(r => r.Status == RegulatoryRuleStatus.PublishedNewVersion &&
                        string.Equals(r.TaxCode, taxCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.ValidFrom)
            .ToList();

        var diffs = new List<RegulatoryRuleVersionDiff>();
        for (int i = 1; i < rules.Count; i++)
        {
            var prev = rules[i - 1];
            var curr = rules[i];
            diffs.Add(new RegulatoryRuleVersionDiff(
                HsCode10: hsCode10,
                TaxCode: taxCode,
                OldVersionCode: prev.RegulatoryVersionCode,
                OldRatePercent: prev.RatePercent,
                OldValidFrom: prev.ValidFrom,
                OldValidTo: prev.ValidTo,
                NewVersionCode: curr.RegulatoryVersionCode,
                NewRatePercent: curr.RatePercent,
                NewValidFrom: curr.ValidFrom,
                DeltaPercentagePoints: curr.RatePercent - prev.RatePercent,
                LegalSourceReference: $"{curr.LegalSource.OfficialTitle} ({curr.LegalSource.JoraReference})"));
        }

        return diffs;
    }
}
