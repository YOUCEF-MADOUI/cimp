using System;
using System.Collections.ObjectModel;
using System.Linq;
using ImportCostAlgeria.AI;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Presentation.Infrastructure;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>
/// Boîte de dialogue de confirmation d'une proposition IA de code SH (Section 16 & 39) :
/// l'utilisateur doit explicitement CONFIRMER, MODIFIER ou REFUSER — jamais d'auto-validation.
///
/// Revue du 2026-10-01 (point 4 — Classification IA du code SH) : affiche désormais JUSQU'À 3 candidats
/// (<see cref="Candidates"/>) issus de <see cref="IHsClassificationService.ClassifyCandidates"/>, chacun
/// avec sa justification, son niveau de confiance, ses codes alternatifs et les informations manquantes.
/// L'utilisateur peut préciser matière/fonction/description complémentaire puis relancer la classification
/// (<see cref="RefreshCommand"/>) avant de choisir un candidat (<see cref="SelectedCandidate"/>) et de le
/// confirmer/modifier/refuser — le fonctionnement "IA propose -&gt; utilisateur valide" reste strictement
/// inchangé, seul le nombre de candidats proposés augmente.
/// </summary>
public sealed class HsConfirmDialogViewModel : ObservableObject
{
    private readonly IHsClassificationService _classifier;
    private readonly HsClassificationInput _baseInput;
    private readonly string _productReference;
    private readonly string _productDesignation;
    private readonly string? _originCountryIso2;

    private string _modifiedHsCode = string.Empty;
    private string _additionalDescription = string.Empty;
    private string _material = string.Empty;
    private string _function = string.Empty;
    private bool _hasTechnicalDocument;
    private HsClassificationCandidate? _selectedCandidate;

    public HsConfirmDialogViewModel(
        IHsClassificationService classifier,
        HsClassificationInput baseInput,
        System.Collections.Generic.IReadOnlyList<HsClassificationCandidate> initialCandidates,
        string productReference,
        string productDesignation,
        string? originCountryIso2)
    {
        _classifier = classifier;
        _baseInput = baseInput;
        _productReference = productReference;
        _productDesignation = productDesignation;
        _originCountryIso2 = originCountryIso2;

        Candidates = new ObservableCollection<HsClassificationCandidate>(initialCandidates);
        SelectedCandidate = Candidates.FirstOrDefault();

        RefreshCommand = new RelayCommand(RefreshCandidates);
    }

    /// <summary>Identifiant de la source/version du service de classification (traçabilité — voir le journal d'audit).</summary>
    public string ServiceNameAndVersion => _classifier.ServiceNameAndVersion;

    public string ProductDesignation => _productDesignation;

    /// <summary>Jusqu'à 3 candidats proposés par l'IA, classés par pertinence décroissante.</summary>
    public ObservableCollection<HsClassificationCandidate> Candidates { get; }

    public HsClassificationCandidate? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (SetField(ref _selectedCandidate, value) && value != null)
                ModifiedHsCode = value.HsCode10;
        }
    }

    /// <summary>Description complémentaire facultative, saisie par l'utilisateur pour affiner la classification.</summary>
    public string AdditionalDescription { get => _additionalDescription; set => SetField(ref _additionalDescription, value); }

    /// <summary>Matière du produit (point 4 de la revue — caractéristique déterminante pour la classification SH).</summary>
    public string Material { get => _material; set => SetField(ref _material, value); }

    /// <summary>Fonction du produit (point 4 de la revue — caractéristique déterminante pour la classification SH).</summary>
    public string Function { get => _function; set => SetField(ref _function, value); }

    /// <summary>Indique qu'une fiche technique ou une photo est disponible pour ce produit (information déclarative, non analysée automatiquement).</summary>
    public bool HasTechnicalDocument { get => _hasTechnicalDocument; set => SetField(ref _hasTechnicalDocument, value); }

    public string ModifiedHsCode
    {
        get => _modifiedHsCode;
        set => SetField(ref _modifiedHsCode, value);
    }

    public RelayCommand RefreshCommand { get; }

    public AiProposalDecision Decision { get; private set; } = AiProposalDecision.PendingUserValidation;

    public void Confirmer() => Decision = AiProposalDecision.ConfirmedByUser;
    public void Modifier() => Decision = AiProposalDecision.ModifiedByUser;
    public void Refuser() => Decision = AiProposalDecision.RejectedByUser;

    /// <summary>
    /// Relance la classification IA en tenant compte des caractéristiques précisées par l'utilisateur
    /// (matière, fonction, description complémentaire, document technique disponible) : permet d'affiner
    /// les 3 candidats sans jamais valider automatiquement un code (toujours soumis à confirmation humaine).
    /// </summary>
    private void RefreshCandidates()
    {
        var refinedInput = _baseInput with
        {
            Description = string.IsNullOrWhiteSpace(AdditionalDescription) ? _baseInput.Description : AdditionalDescription,
            Material = string.IsNullOrWhiteSpace(Material) ? null : Material.Trim(),
            Function = string.IsNullOrWhiteSpace(Function) ? null : Function.Trim(),
            HasTechnicalDocumentOrPhoto = HasTechnicalDocument
        };

        var newCandidates = _classifier.ClassifyCandidates(refinedInput);

        Candidates.Clear();
        foreach (var candidate in newCandidates)
            Candidates.Add(candidate);

        SelectedCandidate = Candidates.FirstOrDefault();
    }

    /// <summary>
    /// Construit, à partir du candidat retenu par l'utilisateur, l'objet historique
    /// <see cref="HsClassificationAiProposal"/> attendu par
    /// <see cref="HSClassifierService.ApplyUserDecisionOnImportLine"/> — l'écriture définitive du code SH
    /// (et le journal d'audit associé) reste donc strictement inchangée.
    /// </summary>
    public HsClassificationAiProposal BuildProposalForSelectedCandidate()
    {
        var candidate = SelectedCandidate ?? Candidates.First();
        return new HsClassificationAiProposal(
            ProposalId: Guid.NewGuid(),
            ProductReference: _productReference,
            ProductDesignation: _productDesignation,
            OriginCountryIso2: _originCountryIso2,
            ProposedHsCode10: candidate.HsCode10,
            ProposedTariffDescriptionFr: candidate.TariffDescriptionFr,
            ConfidencePercent: candidate.ConfidencePercent,
            JustificationFr: candidate.JustificationFr,
            GeneralInterpretiveRuleUsed: candidate.GeneralInterpretiveRuleUsed,
            DecisionStatus: AiProposalDecision.PendingUserValidation,
            DataTag: DataOriginTag.PropositionIa);
    }
}
