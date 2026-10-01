using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.RegulatoryEngine;
using ImportCostAlgeria.CalculationEngine;
using ImportCostAlgeria.AI;
using ImportCostAlgeria.Reporting;

namespace ImportCostAlgeria.UnitTests;

public sealed class V1_1_And_V1_2_Tests
{
    private static readonly LegalSource JoraSource = new()
    {
        HierarchyLevel = LegalSourceHierarchyLevel.Level1_JournalOfficielJora,
        OfficialTitle = "Loi de Finances / Tarif Douanier Officiel",
        JoraReference = "JORA N° 88",
        ArticleReference = "Art. 14, 15, 16 ter, 16 octies & 103 CDA",
        PublicationDate = new DateOnly(2025, 12, 30),
        EffectiveDate = new DateOnly(2026, 1, 1)
    };

    private static readonly LegalSource SecondaryBlogSource = new()
    {
        HierarchyLevel = LegalSourceHierarchyLevel.Level6_SourcesSecondairesAideUniquement,
        OfficialTitle = "Article de blog secondaire non officiel",
        JoraReference = "N/A",
        ArticleReference = "N/A",
        PublicationDate = new DateOnly(2026, 1, 10),
        EffectiveDate = new DateOnly(2026, 1, 10)
    };

    // 1. Test Régimes Préférentiels & Condition de Transport Direct / Certificat d'Origine (V1.1)
    [Fact]
    public void PreferentialRegime_ShouldFallbackToDroitCommun_WhenCertificateOrDirectTransportIsMissing()
    {
        var advService = new AdvancedRegulatoryService(new InMemoryRegulatoryRuleRepository(Array.Empty<RegulatoryRule>()));

        // Origine IT (UE) mais expédié depuis AE (Émirats) sans preuve de transport direct
        var nonDirectCtx = new AdvancedRegulatoryContext(
            HsCode10: "8421.29.90.00",
            OriginCountryIso2: "IT",
            ExportShippingCountryIso2: "AE",
            OperationReferenceDate: new DateOnly(2026, 9, 15),
            RequestedRegimeCode: "PREF_UE",
            HasValidCertificateOfOrigin: true,
            IsDirectTransportVerified: false,
            HasOfficialExemptionDecision: false,
            ExemptionDecisionReference: null);

        var result = advService.EvaluateRegimeEligibility(nonDirectCtx);
        Assert.False(result.IsEligibleForPreferentialRegime);
        Assert.Equal("DROIT_COMMUN_4000", result.EffectiveRegimeCode);
        Assert.Contains("transport direct", result.FallbackReasonFr);
    }

    // 2. Test Sécurité du Workflow de Validation des Règles (Sections 20, 21 & 39)
    [Fact]
    public void RegulatoryProposalWorkflow_ShouldRejectNonAdmin_AndRejectSecondarySource()
    {
        var advService = new AdvancedRegulatoryService(new InMemoryRegulatoryRuleRepository(Array.Empty<RegulatoryRule>()));

        var proposalFromSecondary = new RegulatoryRuleProposal
        {
            ProposalCode = "PROP-2026-01",
            DetectedChangeSummaryFr = "Modification détectée",
            AiAnalysisNotesFr = "Analyse IA",
            RuleType = RegulatoryRuleType.CustomsDuty,
            TaxCode = "DD",
            HsCode10 = "8708.99.90.00",
            CustomsRegimeCode = "DROIT_COMMUN_4000",
            ProposedRatePercent = 12.0m,
            ProposedCalculationBase = TaxableBaseType.CustomsValueDzd,
            ProposedEffectiveFrom = new DateOnly(2027, 1, 1),
            ProposedLegalSource = SecondaryBlogSource
        };

        // Un utilisateur non-administrateur ne peut pas publier une règle
        Assert.Throws<InvalidOperationException>(() =>
            advService.ApproveProposalAndCreateNewVersion(
                proposalFromSecondary, null, Guid.NewGuid(), UserRole.Utilisateur, "2027.01", "Tentative non admin"));

        // Même un administrateur ne peut pas publier une règle issue d'une source secondaire (Niveau 6)
        Assert.Throws<InvalidOperationException>(() =>
            advService.ApproveProposalAndCreateNewVersion(
                proposalFromSecondary, null, Guid.NewGuid(), UserRole.Administrateur, "2027.01", "Tentative source secondaire"));
    }

    // 3. Test Classification SH par IA avec Validation Humaine Obligatoire (Section 16 — V1.2)
    [Fact]
    public void AiHsClassifier_ShouldPropose8708999000ForEngineMounting_AndRequireExplicitUserConfirmation()
    {
        var classifier = new HSClassifierService();
        var proposal = classifier.ProposeHsCode(new HsClassificationInput(
            Reference: "PROD-A",
            Designation: "ENGINE MOUNTING CHINA",
            Description: "Support moteur antivibratoire caoutchouc-métal",
            OriginCountryIso2: "CN",
            AdditionalInformation: null));

        Assert.Equal("8708.99.90.00", proposal.ProposedHsCode10);
        Assert.Equal(87.0m, proposal.ConfidencePercent);
        Assert.Equal(AiProposalDecision.PendingUserValidation, proposal.DecisionStatus);
        Assert.Equal(DataOriginTag.PropositionIa, proposal.DataTag);

        var line = new ImportLine
        {
            LineNumber = 1,
            ProductReference = "PROD-A",
            Designation = "ENGINE MOUNTING CHINA",
            Quantity = 500m,
            UnitPurchasePrice = 10.0m,
            CurrencyCode = "EUR"
        };

        // Tant que l'utilisateur n'a pas confirmé, HsCodeConfirmed10 reste null
        Assert.Null(line.HsCodeConfirmed10);

        // Action explicite [CONFIRMER]
        classifier.ApplyUserDecisionOnImportLine(line, proposal, AiProposalDecision.ConfirmedByUser);
        Assert.Equal("8708.99.90.00", line.HsCodeConfirmed10);
        Assert.Equal(AiProposalDecision.ConfirmedByUser, line.AiHsDecision);
    }

    // 3bis. Revue du 2026-10-01 (point 4 — Classification IA du code SH, test matrice D) :
    // IHsClassificationService.ClassifyCandidates doit renvoyer plusieurs candidats (code, désignation,
    // justification, confiance, codes alternatifs, informations manquantes), jamais un code définitif.
    [Fact]
    public void AiHsClassifier_ClassifyCandidates_ShouldReturnMultipleCandidatesWithAlternativesAndConfidence()
    {
        IHsClassificationService classifier = new HSClassifierService();

        Assert.False(string.IsNullOrWhiteSpace(classifier.ServiceNameAndVersion));

        var candidates = classifier.ClassifyCandidates(new HsClassificationInput(
            Reference: "PROD-A",
            Designation: "ENGINE MOUNTING CHINA",
            Description: "Support moteur antivibratoire caoutchouc-métal",
            OriginCountryIso2: "CN",
            AdditionalInformation: null));

        // Plusieurs candidats (le meilleur + ses alternatives déclarées), jamais plus de 3.
        Assert.True(candidates.Count >= 2 && candidates.Count <= 3);

        var best = candidates[0];
        Assert.Equal("8708.99.90.00", best.HsCode10);
        Assert.Equal(87.0m, best.ConfidencePercent);
        Assert.False(string.IsNullOrWhiteSpace(best.JustificationFr));
        Assert.False(string.IsNullOrWhiteSpace(best.GeneralInterpretiveRuleUsed));
        Assert.NotEmpty(best.SupportingInformationFr);

        // Le code alternatif connu pour cette ambiguïté douanière (caoutchouc Ch.40 vs pièce Ch.87) doit apparaître.
        var alternative = candidates[1];
        Assert.Equal("4016.99.90.00", alternative.HsCode10);
        Assert.True(alternative.ConfidencePercent < best.ConfidencePercent);
        Assert.False(string.IsNullOrWhiteSpace(alternative.JustificationFr));
    }

    // 3ter. Sans aucune information exploitable, l'IA ne doit jamais inventer un code : elle doit renvoyer
    // une information explicite "non déterminée" avec la liste concrète des informations manquantes.
    [Fact]
    public void AiHsClassifier_ClassifyCandidates_ShouldNeverInventACode_WhenNoKeywordMatches()
    {
        IHsClassificationService classifier = new HSClassifierService();

        var candidates = classifier.ClassifyCandidates(new HsClassificationInput(
            Reference: "PROD-X",
            Designation: "ARTICLE SANS RAPPORT CONNU",
            Description: null,
            OriginCountryIso2: "FR",
            AdditionalInformation: null));

        Assert.Single(candidates);
        Assert.Equal(0m, candidates[0].ConfidencePercent);
        Assert.NotEmpty(candidates[0].MissingInformationFr);
    }

    // 3quater. Validation humaine obligatoire : le code finalement retenu par l'utilisateur (parmi les
    // candidats proposés, ou saisi manuellement) est celui qu'applique ApplyUserDecisionOnImportLine —
    // jamais une auto-sélection par l'IA du meilleur candidat.
    [Fact]
    public void AiHsClassifier_HumanValidation_ShouldApplyUserSelectedCandidate_NotNecessarilyTheBestOne()
    {
        var classifier = new HSClassifierService();
        var candidates = classifier.ClassifyCandidates(new HsClassificationInput(
            Reference: "PROD-A",
            Designation: "ENGINE MOUNTING CHINA",
            Description: "Support moteur antivibratoire caoutchouc-métal",
            OriginCountryIso2: "CN",
            AdditionalInformation: null));

        // L'utilisateur choisit délibérément le 2ème candidat (alternative), pas le premier (le "meilleur").
        var userSelected = candidates[1];
        Assert.NotEqual(candidates[0].HsCode10, userSelected.HsCode10);

        var proposalFromUserSelection = new HsClassificationAiProposal(
            ProposalId: Guid.NewGuid(),
            ProductReference: "PROD-A",
            ProductDesignation: "ENGINE MOUNTING CHINA",
            OriginCountryIso2: "CN",
            ProposedHsCode10: userSelected.HsCode10,
            ProposedTariffDescriptionFr: userSelected.TariffDescriptionFr,
            ConfidencePercent: userSelected.ConfidencePercent,
            JustificationFr: userSelected.JustificationFr,
            GeneralInterpretiveRuleUsed: userSelected.GeneralInterpretiveRuleUsed,
            DecisionStatus: AiProposalDecision.PendingUserValidation,
            DataTag: DataOriginTag.PropositionIa);

        var line = new ImportLine
        {
            LineNumber = 1,
            ProductReference = "PROD-A",
            Designation = "ENGINE MOUNTING CHINA",
            Quantity = 500m,
            UnitPurchasePrice = 10.0m,
            CurrencyCode = "EUR"
        };

        classifier.ApplyUserDecisionOnImportLine(line, proposalFromUserSelection, AiProposalDecision.ConfirmedByUser);

        // Le moteur réglementaire utilisera donc bien le code validé par l'humain (l'alternative), pas le
        // "meilleur" candidat calculé automatiquement par l'IA.
        Assert.Equal(userSelected.HsCode10, line.HsCodeConfirmed10);
        Assert.Equal("4016.99.90.00", line.HsCodeConfirmed10);
    }
}
