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
    // Revue du 2026-10-01 (point 5 — Droits et taxes par code SH) : une règle réglementaire peut déclarer
    // EXPLICITEMENT qu'une taxe (ex: PRCT, TCS, DAPS) NE S'APPLIQUE PAS à ce code SH/cette origine/cette
    // période — ceci est un FAIT réglementaire sourcé et versionné comme les autres (jamais une absence de
    // données silencieuse). Par défaut (rétrocompatibilité des règles déjà publiées) la taxe est applicable.
    // Voir RegulatoryRuleEngine.ResolveApplicableRules : une règle non applicable n'est JAMAIS incluse dans
    // le calcul (AdditionalTaxRules) mais reste tracée séparément (NonApplicableTaxRules) pour affichage
    // explicite "Non applicable" — à distinguer d'une simple absence de règle ("Donnée manquante").
    public bool IsApplicable { get; init; } = true;
    public string? Condition { get; init; }
    public required DateOnly ValidFrom { get; init; }
    // ValidTo reste modifiable UNIQUEMENT pour "fermer" une période lors de la publication d'une
    // nouvelle version réglementaire (Sections 19 & 21) — jamais pour modifier le taux/la base/la source
    // de la règle elle-même, qui restent immuables une fois publiés (toute évolution = nouvelle ligne).
    public DateOnly? ValidTo { get; set; }
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
    IReadOnlyList<string> WarningsOrMissingInfo,
    // Revue du 2026-10-01 (point 5) : règles officielles en vigueur qui déclarent EXPLICITEMENT qu'une
    // taxe ne s'applique pas (RegulatoryRule.IsApplicable == false) — jamais incluses dans le calcul,
    // mais tracées pour que l'écran puisse afficher "Non applicable" (plutôt qu'un taux à 0 % ou une
    // absence silencieuse de ligne).
    IReadOnlyList<RegulatoryRule> NonApplicableTaxRules);

/// <summary>
/// Revue du 2026-10-01 (point 5 & 6 — Droits et taxes par code SH / Affichage écran Importation) :
/// statut de présence réglementaire d'une taxe donnée pour un code SH/une date donnés, permettant à
/// l'écran de distinguer explicitement 3 cas (jamais une absence silencieuse ni un taux inventé) :
/// - Applicable : une règle officielle en vigueur a été trouvée, taux/base/montant connus ;
/// - NonApplicable : une règle officielle en vigueur déclare EXPLICITEMENT que cette taxe ne s'applique
///   pas à ce code SH (fait réglementaire sourcé, pas une supposition du logiciel) ;
/// - DonneeManquante : aucune règle (ni applicable, ni explicitement non applicable) n'a été publiée —
///   le logiciel n'invente rien et affiche "Donnée réglementaire manquante — validation requise."
/// </summary>
public enum TaxApplicabilityKind
{
    Applicable,
    NonApplicable,
    DonneeManquante
}

public sealed record TaxApplicabilityStatus(
    string TaxCode,
    string TaxNameFr,
    TaxApplicabilityKind Kind,
    RegulatoryRule? Rule)
{
    public decimal? RatePercent => Kind == TaxApplicabilityKind.Applicable ? Rule?.RatePercent : null;

    public string DisplayStatusFr => Kind switch
    {
        TaxApplicabilityKind.Applicable => $"Applicable ({Rule!.RatePercent:N2} %)",
        TaxApplicabilityKind.NonApplicable => "Non applicable",
        _ => "Donnée réglementaire manquante — validation requise"
    };
}

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
                WarningsOrMissingInfo: new[] { "Code SH absent : impossible de déterminer les droits et taxes réglementaires." },
                NonApplicableTaxRules: Array.Empty<RegulatoryRule>());
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
                WarningsOrMissingInfo: messages,
                NonApplicableTaxRules: Array.Empty<RegulatoryRule>());
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

        // Pour chaque autre taxe (PRCT, TCS, DAPS, TIC, RDAE...), on retient la règle officielle la plus
        // pertinente pour ce code SH/cette origine/cette date. Revue du 2026-10-01 (point 5) : cette règle
        // peut déclarer la taxe soit APPLICABLE (taux > 0 ou non, peu importe — le taux lui-même n'est
        // jamais inventé), soit EXPLICITEMENT NON APPLICABLE (RegulatoryRule.IsApplicable == false) : dans
        // ce 2e cas elle est exclue du calcul (AdditionalTaxRules) mais tracée séparément pour affichage.
        var bestRulePerOtherTaxCode = validOfficialRules
            .Where(r => r.RuleType is not RegulatoryRuleType.CustomsDuty and not RegulatoryRuleType.Vat and not RegulatoryRuleType.Exemption)
            .GroupBy(r => r.TaxCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(r => r.OriginCountryIso2 != null ? 1 : 0)
                .ThenBy(r => (int)r.LegalSource.HierarchyLevel)
                .ThenByDescending(r => r.Priority)
                .ThenByDescending(r => r.ValidFrom)
                .First())
            .ToList();

        var additionalTaxRules = bestRulePerOtherTaxCode.Where(r => r.IsApplicable).ToList();
        var nonApplicableTaxRules = bestRulePerOtherTaxCode.Where(r => !r.IsApplicable).ToList();

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
            WarningsOrMissingInfo: messages,
            NonApplicableTaxRules: nonApplicableTaxRules);
    }

    /// <summary>
    /// Revue du 2026-10-01 (point 5 & 6) : construit, pour une liste de codes de taxes "à toujours
    /// vérifier" sur l'écran (ex: PRCT, TCS, DAPS), le statut réglementaire de chacune — Applicable,
    /// explicitement NonApplicable, ou DonneeManquante — à partir du résultat déjà résolu par
    /// <see cref="ResolveApplicableRules"/>. Ne recalcule RIEN et n'invente AUCUN taux : se contente de
    /// classer les taxes déjà résolues (ou leur absence) pour un affichage explicite côté écran
    /// Importation, conformément à l'exigence de ne jamais confondre "non applicable" et "donnée absente".
    /// </summary>
    public static IReadOnlyList<TaxApplicabilityStatus> BuildStandardTaxApplicabilityReport(
        RegulatoryResolutionOutcome outcome,
        IReadOnlyList<(string TaxCode, string TaxNameFr)> standardTaxCodesToCheck)
    {
        var report = new List<TaxApplicabilityStatus>();

        foreach (var (taxCode, taxNameFr) in standardTaxCodesToCheck)
        {
            var applicableRule = outcome.AdditionalTaxRules
                .FirstOrDefault(r => string.Equals(r.TaxCode, taxCode, StringComparison.OrdinalIgnoreCase));
            if (applicableRule != null)
            {
                report.Add(new TaxApplicabilityStatus(taxCode, taxNameFr, TaxApplicabilityKind.Applicable, applicableRule));
                continue;
            }

            var nonApplicableRule = outcome.NonApplicableTaxRules
                .FirstOrDefault(r => string.Equals(r.TaxCode, taxCode, StringComparison.OrdinalIgnoreCase));
            if (nonApplicableRule != null)
            {
                report.Add(new TaxApplicabilityStatus(taxCode, taxNameFr, TaxApplicabilityKind.NonApplicable, nonApplicableRule));
                continue;
            }

            report.Add(new TaxApplicabilityStatus(taxCode, taxNameFr, TaxApplicabilityKind.DonneeManquante, null));
        }

        return report;
    }

    /// <summary>
    /// Liste des codes de taxes additionnelles "standard" à toujours présenter sur l'écran Importation
    /// (point 6 de la revue du 2026-10-01), au-delà de DD et TVA qui disposent déjà de leur propre
    /// affichage dédié. Ceci ne contient AUCUN taux : uniquement des codes et libellés, pour savoir QUOI
    /// vérifier dans les règles réglementaires publiées — les taux/applicabilité réels viennent toujours
    /// exclusivement de <see cref="RegulatoryRule"/> (jamais codés en dur ici).
    /// </summary>
    public static readonly IReadOnlyList<(string TaxCode, string TaxNameFr)> StandardAdditionalTaxCodes = new[]
    {
        ("CS", "Contribution de Solidarité (CS)"),
        ("PRCT", "Précompte à l'importation (PRCT)"),
        ("TCS", "Taxe de Contribution de Solidarité (TCS)"),
        ("DAPS", "Droit Additionnel Provisoire de Sauvegarde (DAPS)")
    };

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
