using System;
using System.Collections.Generic;
using System.Linq;
using ImportCostAlgeria.Core.Domain;

namespace ImportCostAlgeria.RegulatoryEngine;

/// <summary>
/// Représente une source juridique officielle algérienne hiérarchisée (Section 20).
/// </summary>
public sealed class LegalSource
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required LegalSourceHierarchyLevel HierarchyLevel { get; init; }
    public required string OfficialTitle { get; init; }
    public required string JoraReference { get; init; }
    public required string ArticleReference { get; init; }
    public required DateOnly PublicationDate { get; init; }
    public required DateOnly EffectiveDate { get; init; }
    public bool IsOfficialBinding => HierarchyLevel != LegalSourceHierarchyLevel.Level6_SourcesSecondairesAideUniquement;
}

/// <summary>
/// Règle réglementaire versionnée et datée (Sections 18, 19, 20, 21).
/// Aucune règle fiscale ou douanière n'est codée en dur dans le programme.
/// </summary>
public sealed class RegulatoryRule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Code { get; init; }
    public required string RegulatoryVersionCode { get; init; }
    public Guid? SupersedesRuleId { get; init; }
    public required RegulatoryRuleType RuleType { get; init; }
    public required string TaxCode { get; init; } // DD, TVA, DAPS, TIC, RDAE...
    public required string TaxNameFr { get; init; }
    public required string HsCode10 { get; init; }
    public string? OriginCountryIso2 { get; init; } // null = Toutes origines (Droit commun)
    public string CustomsRegimeCode { get; init; } = "DROIT_COMMUN_4000";
    public required decimal RatePercent { get; init; }
    public required TaxableBaseType CalculationBase { get; init; }
    public string? Condition { get; init; }
    public required DateOnly ValidFrom { get; init; }
    public DateOnly? ValidTo { get; init; }
    public int Priority { get; init; } = 100;
    public required LegalSource LegalSource { get; init; }
    public required RegulatoryRuleStatus Status { get; set; }
    public bool IsProposedByAi { get; init; }
    public Guid? ValidatedByAdminUserId { get; set; }
    public DateTime? ValidatedAtUtc { get; set; }
}

public sealed record RegulatoryLookupQuery(
    string HsCode10,
    string? OriginCountryIso2,
    DateOnly OperationReferenceDate,
    string CustomsRegimeCode);

public sealed record RegulatoryResolutionOutcome(
    bool IsDetermined,
    string StatusMessage,
    RegulatoryRule? CustomsDutyRule,
    RegulatoryRule? VatRule,
    IReadOnlyList<RegulatoryRule> AdditionalTaxRules,
    IReadOnlyList<string> WarningsOrMissingInfo);

public interface IRegulatoryRuleRepository
{
    IReadOnlyList<RegulatoryRule> GetCandidateRules(string hsCode10);
}

/// <summary>
/// Moteur de résolution réglementaire déterministe (Sections 18, 19, 20, 21, 41).
/// Respecte :
/// - Le versionnage temporel à la date de référence (Art. 103 du Code des Douanes) ;
/// - La hiérarchie des sources officielles (JORA > DGD/ALCES > Min. Finances...) ;
/// - L'interdiction absolue d'appliquer une règle non publiée ou issue d'une source secondaire ;
/// - L'interdiction absolue d'inventer un taux ("INFORMATION NON DÉTERMINÉE" si introuvable).
/// </summary>
public sealed class RegulatoryRuleEngine
{
    private readonly IRegulatoryRuleRepository _repository;

    public RegulatoryRuleEngine(IRegulatoryRuleRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public RegulatoryResolutionOutcome ResolveApplicableRules(RegulatoryLookupQuery query)
    {
        var messages = new List<string>();

        if (string.IsNullOrWhiteSpace(query.HsCode10))
        {
            return new RegulatoryResolutionOutcome(
                IsDetermined: false,
                StatusMessage: "INFORMATION NON DÉTERMINÉE : Code SH absent.",
                CustomsDutyRule: null,
                VatRule: null,
                AdditionalTaxRules: Array.Empty<RegulatoryRule>(),
                WarningsOrMissingInfo: new[] { "Code SH absent : impossible de déterminer les droits et taxes réglementaires." });
        }

        string normalizedHs = NormalizeHsCode(query.HsCode10);
        var allCandidates = _repository.GetCandidateRules(normalizedHs);

        // Sécurité (Sections 20, 21, 39) :
        // Seules les règles PUBLISHED, validées par un Administrateur et provenant d'une source officielle (Niveau 1 à 5) sont éligibles.
        var validOfficialRules = allCandidates
            .Where(r =>
                r.Status == RegulatoryRuleStatus.PublishedNewVersion &&
                r.ValidatedByAdminUserId.HasValue &&
                r.LegalSource.IsOfficialBinding &&
                NormalizeHsCode(r.HsCode10) == normalizedHs &&
                string.Equals(r.CustomsRegimeCode, query.CustomsRegimeCode, StringComparison.OrdinalIgnoreCase) &&
                r.ValidFrom <= query.OperationReferenceDate &&
                (!r.ValidTo.HasValue || r.ValidTo.Value >= query.OperationReferenceDate) &&
                (r.OriginCountryIso2 == null ||
                 string.Equals(r.OriginCountryIso2, query.OriginCountryIso2, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Détection de règles expirées pour information dans le moteur d'anomalies (Section 28)
        bool hadExpiredRules = allCandidates.Any(r =>
            r.Status == RegulatoryRuleStatus.PublishedNewVersion &&
            NormalizeHsCode(r.HsCode10) == normalizedHs &&
            r.ValidTo.HasValue &&
            r.ValidTo.Value < query.OperationReferenceDate);

        if (validOfficialRules.Count == 0)
        {
            if (hadExpiredRules)
            {
                messages.Add($"Règle réglementaire expirée pour le code SH {query.HsCode10} à la date du {query.OperationReferenceDate:dd/MM/yyyy}.");
            }
            messages.Add($"INFORMATION NON DÉTERMINÉE : Aucune règle officielle en vigueur trouvée pour SH={query.HsCode10}, Origine={query.OriginCountryIso2 ?? "N/A"}, Date={query.OperationReferenceDate:dd/MM/yyyy}.");

            return new RegulatoryResolutionOutcome(
                IsDetermined: false,
                StatusMessage: "INFORMATION NON DÉTERMINÉE",
                CustomsDutyRule: null,
                VatRule: null,
                AdditionalTaxRules: Array.Empty<RegulatoryRule>(),
                WarningsOrMissingInfo: messages);
        }

        // Sélection par : 1) Spécificité d'origine (origine exacte prioritaire sur droit commun),
        //                 2) Hiérarchie de la source juridique (1 = JORA plus prioritaire que 5),
        //                 3) Priorité explicite de la règle,
        //                 4) Date d'entrée en vigueur la plus récente.
        RegulatoryRule? SelectBestRule(RegulatoryRuleType type) =>
            validOfficialRules
                .Where(r => r.RuleType == type)
                .OrderByDescending(r => r.OriginCountryIso2 != null ? 1 : 0)
                .ThenBy(r => (int)r.LegalSource.HierarchyLevel)
                .ThenByDescending(r => r.Priority)
                .ThenByDescending(r => r.ValidFrom)
                .FirstOrDefault();

        var dutyRule = SelectBestRule(RegulatoryRuleType.CustomsDuty);
        var vatRule = SelectBestRule(RegulatoryRuleType.Vat);

        var additionalTaxRules = validOfficialRules
            .Where(r => r.RuleType is not RegulatoryRuleType.CustomsDuty and not RegulatoryRuleType.Vat and not RegulatoryRuleType.Exemption)
            .GroupBy(r => r.TaxCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(r => r.OriginCountryIso2 != null ? 1 : 0)
                .ThenBy(r => (int)r.LegalSource.HierarchyLevel)
                .ThenByDescending(r => r.Priority)
                .ThenByDescending(r => r.ValidFrom)
                .First())
            .ToList();

        if (dutyRule == null)
        {
            messages.Add($"INFORMATION NON DÉTERMINÉE : Droit de douane introuvable pour SH {query.HsCode10}.");
        }

        if (vatRule == null)
        {
            messages.Add($"INFORMATION NON DÉTERMINÉE : Taux de TVA réglementaire introuvable pour SH {query.HsCode10}.");
        }

        bool fullyDetermined = dutyRule != null && vatRule != null;

        return new RegulatoryResolutionOutcome(
            IsDetermined: fullyDetermined,
            StatusMessage: fullyDetermined ? "RÈGLES OFFICIELLES DÉTERMINÉES" : "INFORMATION NON DÉTERMINÉE",
            CustomsDutyRule: dutyRule,
            VatRule: vatRule,
            AdditionalTaxRules: additionalTaxRules,
            WarningsOrMissingInfo: messages);
    }

    /// <summary>
    /// Workflow de validation administrative obligatoire (Section 21 & 39).
    /// L'IA ne peut jamais publier automatiquement une règle réglementaire.
    /// </summary>
    public void ValidateAndPublishRuleByAdmin(RegulatoryRule rule, Guid adminUserId, UserRole executorRole)
    {
        if (executorRole != UserRole.Administrateur)
        {
            throw new InvalidOperationException("Sécurité réglementaire : Seul un Administrateur peut valider et publier une règle officielle.");
        }

        if (!rule.LegalSource.IsOfficialBinding)
        {
            throw new InvalidOperationException("Hiérarchie des sources : Une source secondaire (Niveau 6) ne peut jamais remplacer ou publier une règle officielle.");
        }

        rule.ValidatedByAdminUserId = adminUserId;
        rule.ValidatedAtUtc = DateTime.UtcNow;
        rule.Status = RegulatoryRuleStatus.PublishedNewVersion;
    }

    public static string NormalizeHsCode(string rawHs)
    {
        return new string(rawHs.Where(char.IsDigit).ToArray());
    }
}
