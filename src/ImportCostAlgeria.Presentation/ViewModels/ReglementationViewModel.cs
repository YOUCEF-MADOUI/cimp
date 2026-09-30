using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using ImportCostAlgeria.Audit;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Database.Repositories;
using ImportCostAlgeria.Presentation.Infrastructure;
using ImportCostAlgeria.Presentation.Services;
using ImportCostAlgeria.RegulatoryEngine;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>
/// Écran "Réglementation" (Sections 18-21) : consultation/filtrage des règles versionnées et
/// publication d'une nouvelle version (réservée à l'Administrateur) — jamais d'écrasement, toujours
/// fermeture de la période précédente + insertion d'une nouvelle ligne datée et sourcée.
/// </summary>
public sealed class ReglementationViewModel : ObservableObject
{
    private readonly RegulatoryRuleAdminRepository _repository;
    private readonly SessionContext _session;
    private readonly AuditTrailService _audit;

    private string _filterHsCode = string.Empty;
    private string _filterTaxCode = string.Empty;
    private RegulatoryRule? _selected;

    // Champs du formulaire de publication d'une nouvelle version.
    private string _newTaxCode = "DD";
    private RegulatoryRuleType _newRuleType = RegulatoryRuleType.CustomsDuty;
    private string _newHsCode10 = string.Empty;
    private string? _newOriginCountryIso2;
    private decimal _newRatePercent;
    private TaxableBaseType _newCalculationBase = TaxableBaseType.CustomsValueDzd;
    private DateTime _newValidFrom = DateTime.Today;
    private string _newOfficialTitle = string.Empty;
    private string _newJoraReference = string.Empty;
    private string _newArticleReference = string.Empty;
    private LegalSourceHierarchyLevel _newHierarchyLevel = LegalSourceHierarchyLevel.Level1_JournalOfficielJora;

    public ReglementationViewModel(RegulatoryRuleAdminRepository repository, SessionContext session, AuditTrailService audit)
    {
        _repository = repository;
        _session = session;
        _audit = audit;

        AllRules = new ObservableCollection<RegulatoryRule>();
        RuleTypes = Enum.GetValues<RegulatoryRuleType>();
        CalculationBases = Enum.GetValues<TaxableBaseType>();
        HierarchyLevels = Enum.GetValues<LegalSourceHierarchyLevel>();

        RefreshCommand = new RelayCommand(Refresh);
        PublishCommand = new RelayCommand(Publish);

        Refresh();
    }

    public ObservableCollection<RegulatoryRule> AllRules { get; }
    public RegulatoryRuleType[] RuleTypes { get; }
    public TaxableBaseType[] CalculationBases { get; }
    public LegalSourceHierarchyLevel[] HierarchyLevels { get; }

    public string FilterHsCode { get => _filterHsCode; set { SetField(ref _filterHsCode, value); Refresh(); } }
    public string FilterTaxCode { get => _filterTaxCode; set { SetField(ref _filterTaxCode, value); Refresh(); } }

    public RegulatoryRule? Selected { get => _selected; set => SetField(ref _selected, value); }

    public string NewTaxCode { get => _newTaxCode; set => SetField(ref _newTaxCode, value); }
    public RegulatoryRuleType NewRuleType { get => _newRuleType; set => SetField(ref _newRuleType, value); }
    public string NewHsCode10 { get => _newHsCode10; set => SetField(ref _newHsCode10, value); }
    public string? NewOriginCountryIso2 { get => _newOriginCountryIso2; set => SetField(ref _newOriginCountryIso2, value); }
    public decimal NewRatePercent { get => _newRatePercent; set => SetField(ref _newRatePercent, value); }
    public TaxableBaseType NewCalculationBase { get => _newCalculationBase; set => SetField(ref _newCalculationBase, value); }
    public DateTime NewValidFrom { get => _newValidFrom; set => SetField(ref _newValidFrom, value); }
    public string NewOfficialTitle { get => _newOfficialTitle; set => SetField(ref _newOfficialTitle, value); }
    public string NewJoraReference { get => _newJoraReference; set => SetField(ref _newJoraReference, value); }
    public string NewArticleReference { get => _newArticleReference; set => SetField(ref _newArticleReference, value); }
    public LegalSourceHierarchyLevel NewHierarchyLevel { get => _newHierarchyLevel; set => SetField(ref _newHierarchyLevel, value); }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand PublishCommand { get; }

    private void Refresh()
    {
        AllRules.Clear();
        var all = _repository.GetAll().AsEnumerable();

        if (!string.IsNullOrWhiteSpace(FilterHsCode))
            all = all.Where(r => r.HsCode10.Contains(FilterHsCode, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(FilterTaxCode))
            all = all.Where(r => r.TaxCode.Contains(FilterTaxCode, StringComparison.OrdinalIgnoreCase));

        foreach (var r in all)
            AllRules.Add(r);
    }

    private void Publish()
    {
        try
        {
            _session.RequireAdministrator("publier une nouvelle version de règle réglementaire");

            if (string.IsNullOrWhiteSpace(NewHsCode10) || string.IsNullOrWhiteSpace(NewOfficialTitle) ||
                string.IsNullOrWhiteSpace(NewJoraReference) || string.IsNullOrWhiteSpace(NewArticleReference))
            {
                MessageBox.Show("Code SH, intitulé officiel, référence JORA et article de loi sont obligatoires : aucune règle ne peut être publiée sans source légale citée.",
                    "CIMP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var validFrom = DateOnly.FromDateTime(NewValidFrom);
            var supersede = AllRules.FirstOrDefault(r =>
                r.HsCode10 == NewHsCode10.Trim() && r.TaxCode == NewTaxCode.Trim() && r.ValidTo == null);

            var legalSource = new LegalSource
            {
                HierarchyLevel = NewHierarchyLevel,
                OfficialTitle = NewOfficialTitle.Trim(),
                JoraReference = NewJoraReference.Trim(),
                ArticleReference = NewArticleReference.Trim(),
                PublicationDate = validFrom,
                EffectiveDate = validFrom
            };

            var newRule = new RegulatoryRule
            {
                Code = $"{NewTaxCode.Trim()}_{NewHsCode10.Trim()}_{validFrom:yyyyMMdd}",
                RegulatoryVersionCode = $"V-{validFrom:yyyyMMdd}",
                RuleType = NewRuleType,
                TaxCode = NewTaxCode.Trim(),
                TaxNameFr = NewRuleType.ToString(),
                HsCode10 = NewHsCode10.Trim(),
                OriginCountryIso2 = string.IsNullOrWhiteSpace(NewOriginCountryIso2) ? null : NewOriginCountryIso2!.Trim(),
                RatePercent = NewRatePercent,
                CalculationBase = NewCalculationBase,
                ValidFrom = validFrom,
                LegalSource = legalSource,
                Status = RegulatoryRuleStatus.PublishedNewVersion,
                ValidatedByAdminUserId = _session.CurrentUser?.Id,
                ValidatedAtUtc = DateTime.UtcNow
            };

            _repository.PublishNewVersion(newRule, supersede?.Id, _session.CurrentUser!.Id, _session.CurrentUser.Role);

            _audit.RecordAction(null, _session.CurrentUser.Id, _session.CurrentUser.DisplayName,
                "REGULATORY_RULE", "PUBLISH_NEW_RULE_VERSION",
                oldValue: supersede == null ? null : $"{supersede.RatePercent}%",
                newValue: $"{newRule.TaxCode} {newRule.HsCode10} = {newRule.RatePercent}% à compter du {validFrom:dd/MM/yyyy}",
                regulatoryRuleCode: newRule.Code, legalSourceReference: $"{newRule.LegalSource.JoraReference} — {newRule.LegalSource.ArticleReference}",
                regulatoryVersionCode: newRule.RegulatoryVersionCode);

            Refresh();
            MessageBox.Show("Nouvelle version de règle publiée avec succès.", "CIMP", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "CIMP — Action refusée", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
