using ImportCostAlgeria.AI;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Presentation.Infrastructure;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>
/// Boîte de dialogue de confirmation d'une proposition IA de code SH (Section 16 & 39) :
/// l'utilisateur doit explicitement CONFIRMER, MODIFIER ou REFUSER — jamais d'auto-validation.
/// </summary>
public sealed class HsConfirmDialogViewModel : ObservableObject
{
    private string _modifiedHsCode = string.Empty;

    public HsConfirmDialogViewModel(HsClassificationAiProposal proposal)
    {
        Proposal = proposal;
        _modifiedHsCode = proposal.ProposedHsCode10;
    }

    public HsClassificationAiProposal Proposal { get; }

    public string ModifiedHsCode
    {
        get => _modifiedHsCode;
        set => SetField(ref _modifiedHsCode, value);
    }

    public AiProposalDecision Decision { get; private set; } = AiProposalDecision.PendingUserValidation;

    public void Confirmer() => Decision = AiProposalDecision.ConfirmedByUser;
    public void Modifier() => Decision = AiProposalDecision.ModifiedByUser;
    public void Refuser() => Decision = AiProposalDecision.RejectedByUser;
}
