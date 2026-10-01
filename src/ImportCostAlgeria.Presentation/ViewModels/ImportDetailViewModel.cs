using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using ImportCostAlgeria.AI;
using ImportCostAlgeria.Audit;
using ImportCostAlgeria.CalculationEngine;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Core.Services;
using ImportCostAlgeria.Database.Repositories;
using ImportCostAlgeria.Presentation.Infrastructure;
using ImportCostAlgeria.Presentation.Services;
using ImportCostAlgeria.Presentation.Views;
using ImportCostAlgeria.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>
/// Écran "Détail d'une importation" (Sections 7, 9, 13-17, 22-28) : cœur fonctionnel de l'application —
/// articles, frais, taux de change, calcul complet, anomalies, détail par article, exports.
/// </summary>
public sealed class ImportDetailViewModel : ObservableObject
{
    private readonly ImportOperationRepository _operationRepository;
    private readonly CalculationSnapshotRepository _snapshotRepository;
    private readonly AuditTrailService _audit;
    private readonly SessionContext _session;
    private readonly EngineFactory _engineFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly IncotermDynamicFieldService _incotermFieldService = new();

    private Company _company = null!;
    private ImportOperation _operation = null!;
    private ImportationsViewModel? _parentList;

    private ImportCalculationSummary? _lastSummary;
    private bool _hasBlockingAnomalies;
    private decimal _totalCoutRevientDzd;
    private decimal _totalValeurDouaneDzd;
    private decimal _totalDroitsDouaneDzd;
    private decimal _totalTvaDzd;
    private decimal _totalFraisDzd;
    private string _exchangeRateInfo = string.Empty;
    private string _authorizationExchangeRateInfo = string.Empty;
    private string _authorizationConversionSummary = string.Empty;
    private string _incotermGuidance = string.Empty;
    private string _headerLabel = string.Empty;
    private StandardFeeTemplate? _selectedFeeTemplate;

    public ImportDetailViewModel(
        ImportOperationRepository operationRepository,
        CalculationSnapshotRepository snapshotRepository,
        AuditTrailService audit,
        SessionContext session,
        EngineFactory engineFactory,
        IServiceProvider serviceProvider)
    {
        _operationRepository = operationRepository;
        _snapshotRepository = snapshotRepository;
        _audit = audit;
        _session = session;
        _engineFactory = engineFactory;
        _serviceProvider = serviceProvider;

        Lines = new ObservableCollection<ImportLineRowViewModel>();
        Fees = new ObservableCollection<ImportFee>();
        Anomalies = new ObservableCollection<CalculationAnomaly>();
        FeeTemplates = ImportFeeCatalog.StandardTemplates.ToArray();

        BackCommand = new RelayCommand(() => _parentList?.NavigateBackToList?.Invoke());
        AddLineCommand = new RelayCommand(AddManualLine);
        RemoveLineCommand = new RelayCommand<ImportLineRowViewModel>(RemoveLine);
        ImportExcelCommand = new RelayCommand(OpenExcelWizard);
        CalculateCommand = new RelayCommand(Calculate);
        ExportExcelCommand = new RelayCommand(ExportExcel);
        ExportPdfCommand = new RelayCommand(ExportPdf);
        ConfirmHsCommand = new RelayCommand<ImportLineRowViewModel>(OpenHsConfirmDialog);
        ViewDetailCommand = new RelayCommand<ImportLineRowViewModel>(OpenLineDetailDialog);
        AddFeeCommand = new RelayCommand(AddFeeFromTemplate);
        RemoveFeeCommand = new RelayCommand<ImportFee>(RemoveFee);
    }

    public void Initialize(Company company, ImportOperation operation, ImportationsViewModel parentList)
    {
        _company = company;
        _operation = operation;
        _parentList = parentList;

        HeaderLabel = $"Importation {operation.ImportNumber} — {operation.SupplierName} — Incoterm {operation.Incoterm}";

        Lines.Clear();
        foreach (var line in operation.Lines.OrderBy(l => l.LineNumber))
            Lines.Add(new ImportLineRowViewModel(line));

        Fees.Clear();
        foreach (var fee in operation.Fees)
            Fees.Add(fee);

        Anomalies.Clear();
        _lastSummary = null;
        HasBlockingAnomalies = false;

        RefreshExchangeRateInfo();
        RefreshAuthorizationExchangeRateInfo();
        RefreshIncotermGuidance();
    }

    public string HeaderLabel { get => _headerLabel; private set => SetField(ref _headerLabel, value); }

    public ObservableCollection<ImportLineRowViewModel> Lines { get; }
    public ObservableCollection<ImportFee> Fees { get; }
    public ObservableCollection<CalculationAnomaly> Anomalies { get; }
    public StandardFeeTemplate[] FeeTemplates { get; }
    public FeeAllocationMethod[] FeeAllocationMethods { get; } = Enum.GetValues<FeeAllocationMethod>();

    public StandardFeeTemplate? SelectedFeeTemplate
    {
        get => _selectedFeeTemplate;
        set => SetField(ref _selectedFeeTemplate, value);
    }

    public bool HasBlockingAnomalies { get => _hasBlockingAnomalies; private set => SetField(ref _hasBlockingAnomalies, value); }
    public decimal TotalCoutRevientDzd { get => _totalCoutRevientDzd; private set => SetField(ref _totalCoutRevientDzd, value); }
    public decimal TotalValeurDouaneDzd { get => _totalValeurDouaneDzd; private set => SetField(ref _totalValeurDouaneDzd, value); }
    public decimal TotalDroitsDouaneDzd { get => _totalDroitsDouaneDzd; private set => SetField(ref _totalDroitsDouaneDzd, value); }
    public decimal TotalTvaDzd { get => _totalTvaDzd; private set => SetField(ref _totalTvaDzd, value); }
    public decimal TotalFraisDzd { get => _totalFraisDzd; private set => SetField(ref _totalFraisDzd, value); }
    public string ExchangeRateInfo { get => _exchangeRateInfo; private set => SetField(ref _exchangeRateInfo, value); }
    public string IncotermGuidance { get => _incotermGuidance; private set => SetField(ref _incotermGuidance, value); }

    /// <summary>Section 11 du plan multi-devises : résumé affiché "Devise facture / Taux / Montant facture / Montant autorisation".</summary>
    public string AuthorizationExchangeRateInfo { get => _authorizationExchangeRateInfo; private set => SetField(ref _authorizationExchangeRateInfo, value); }
    public string AuthorizationConversionSummary { get => _authorizationConversionSummary; private set => SetField(ref _authorizationConversionSummary, value); }

    public bool IsManualRateMode
    {
        get => _operation?.ManualExchangeRateOverride.HasValue ?? false;
        set
        {
            if (!value) _operation.ManualExchangeRateOverride = null;
            else _operation.ManualExchangeRateOverride ??= 0m;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ManualRateValue));
        }
    }

    public decimal? ManualRateValue
    {
        get => _operation?.ManualExchangeRateOverride;
        set { _operation.ManualExchangeRateOverride = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Section 13 du plan multi-devises : devise de l'autorisation d'importation (USD par défaut).
    /// Strictement distincte de la devise réglementaire (toujours DZD pour le calcul douanier).
    /// </summary>
    public string AuthorizationCurrencyCode
    {
        get => _operation?.AuthorizationCurrencyCode ?? "USD";
        set
        {
            if (_operation == null) return;
            _operation.AuthorizationCurrencyCode = string.IsNullOrWhiteSpace(value) ? "USD" : value.Trim().ToUpperInvariant();
            OnPropertyChanged();
            RefreshAuthorizationExchangeRateInfo();
        }
    }

    /// <summary>Taux commercial MANUEL (devise facture -> devise d'autorisation), distinct du taux réglementaire manuel.</summary>
    public bool IsManualAuthorizationRateMode
    {
        get => _operation?.ManualAuthorizationExchangeRateOverride.HasValue ?? false;
        set
        {
            if (!value) _operation.ManualAuthorizationExchangeRateOverride = null;
            else _operation.ManualAuthorizationExchangeRateOverride ??= 0m;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ManualAuthorizationRateValue));
            RefreshAuthorizationExchangeRateInfo();
        }
    }

    public decimal? ManualAuthorizationRateValue
    {
        get => _operation?.ManualAuthorizationExchangeRateOverride;
        set { _operation.ManualAuthorizationExchangeRateOverride = value; OnPropertyChanged(); RefreshAuthorizationExchangeRateInfo(); }
    }

    public RelayCommand BackCommand { get; }
    public RelayCommand AddLineCommand { get; }
    public RelayCommand<ImportLineRowViewModel> RemoveLineCommand { get; }
    public RelayCommand ImportExcelCommand { get; }
    public RelayCommand CalculateCommand { get; }
    public RelayCommand ExportExcelCommand { get; }
    public RelayCommand ExportPdfCommand { get; }
    public RelayCommand<ImportLineRowViewModel> ConfirmHsCommand { get; }
    public RelayCommand<ImportLineRowViewModel> ViewDetailCommand { get; }
    public RelayCommand AddFeeCommand { get; }
    public RelayCommand<ImportFee> RemoveFeeCommand { get; }

    private void RefreshExchangeRateInfo()
    {
        if (string.Equals(_operation.MainCurrencyCode, "DZD", StringComparison.OrdinalIgnoreCase))
        {
            ExchangeRateInfo = "Devise principale = DZD : aucune conversion nécessaire.";
            return;
        }

        var provider = _engineFactory.CreateExchangeRateProvider();
        var official = provider.GetRegulatoryRate(_operation.MainCurrencyCode, _operation.ReferenceDate);
        ExchangeRateInfo = official == null
            ? $"INFORMATION NON DÉTERMINÉE : aucun taux officiel enregistré pour {_operation.MainCurrencyCode} au {_operation.ReferenceDate:dd/MM/yyyy}."
            : $"Taux officiel enregistré : 1 {_operation.MainCurrencyCode} = {official.RateToDzd:F4} DZD ({official.SourceName}, valide depuis le {official.ValidFrom:dd/MM/yyyy}).";
    }

    /// <summary>
    /// Section 11 du plan multi-devises : affiche clairement la devise de la facture, le taux commercial
    /// (officiel ou manuel) vers la devise de l'autorisation d'importation, et le montant équivalent.
    /// Jamais mélangé avec <see cref="RefreshExchangeRateInfo"/> (conversion réglementaire vers DZD).
    /// </summary>
    private void RefreshAuthorizationExchangeRateInfo()
    {
        if (_operation == null) return;

        if (string.Equals(_operation.AuthorizationCurrencyCode, _operation.MainCurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            AuthorizationExchangeRateInfo = $"Devise de l'autorisation = devise de la facture ({_operation.MainCurrencyCode}) : aucune conversion commerciale nécessaire.";
            return;
        }

        if (_operation.ManualAuthorizationExchangeRateOverride.HasValue && _operation.ManualAuthorizationExchangeRateOverride.Value > 0m)
        {
            AuthorizationExchangeRateInfo = $"⚠️ TAUX MANUEL : 1 {_operation.MainCurrencyCode} = {_operation.ManualAuthorizationExchangeRateOverride.Value:F4} {_operation.AuthorizationCurrencyCode} (saisi par l'utilisateur).";
            return;
        }

        var conversionService = _engineFactory.CreateCommercialConversionService();
        var (rate, official, _) = conversionService.ResolveCrossRate(
            _operation.MainCurrencyCode, _operation.AuthorizationCurrencyCode, _operation.ReferenceDate, null);

        AuthorizationExchangeRateInfo = official == null
            ? $"INFORMATION NON DÉTERMINÉE : aucun taux commercial officiel enregistré pour {_operation.MainCurrencyCode} → {_operation.AuthorizationCurrencyCode} au {_operation.ReferenceDate:dd/MM/yyyy}. Publiez-le dans l'écran \"Taux de change\" ou saisissez un taux manuel."
            : $"Taux commercial officiel : 1 {_operation.MainCurrencyCode} = {rate:F4} {_operation.AuthorizationCurrencyCode} ({official.SourceName}, valide depuis le {official.ValidFrom:dd/MM/yyyy}).";
    }

    private void RefreshIncotermGuidance()
    {
        var fields = _incotermFieldService.GetRequiredDynamicFields(_operation.Incoterm);
        IncotermGuidance = fields.Count == 0
            ? "Aucune exigence spécifique supplémentaire pour cet Incoterm."
            : "Frais à vérifier pour l'Incoterm " + _operation.Incoterm + " : " +
              string.Join(" ; ", fields.Select(f => $"{f.LabelFr} ({f.LegalBasisArticleFr})"));
    }

    private void AddManualLine()
    {
        int nextNumber = Lines.Count == 0 ? 1 : Lines.Max(l => l.Line.LineNumber) + 1;
        var line = new ImportLine
        {
            LineNumber = nextNumber,
            ProductReference = $"ART-{nextNumber:D3}",
            Designation = "Nouvel article",
            Quantity = 1m,
            UnitPurchasePrice = 0m,
            CurrencyCode = _operation.MainCurrencyCode,
            OriginCountryIso2 = _operation.DefaultOriginCountryIso2
        };
        Lines.Add(new ImportLineRowViewModel(line));
    }

    private void RemoveLine(ImportLineRowViewModel? row)
    {
        if (row == null) return;
        _session.RequireNotConsultation("supprimer une ligne d'article");
        Lines.Remove(row);
    }

    private void AddFeeFromTemplate()
    {
        var template = SelectedFeeTemplate ?? ImportFeeCatalog.StandardTemplates[0];
        Fees.Add(new ImportFee
        {
            FeeCategoryCode = template.CategoryCode,
            FeeName = template.DefaultLabelFr,
            Amount = 0m,
            CurrencyCode = _operation.MainCurrencyCode,
            AllocationMethod = template.SuggestedAllocationMethod,
            IncludeInCustomsValue = template.DefaultIncludeInCustomsValue,
            CustomsTreatment = template.DefaultCustomsTreatment,
            IncludeInCostOfGoods = template.DefaultIncludeInCostOfGoods
        });
    }

    private void RemoveFee(ImportFee? fee)
    {
        if (fee == null) return;
        _session.RequireNotConsultation("supprimer un frais");
        Fees.Remove(fee);
    }

    private void OpenExcelWizard()
    {
        var wizardVm = _serviceProvider.GetRequiredService<ExcelImportWizardViewModel>();
        wizardVm.Initialize(_company, _operation);
        var window = new ExcelImportWizardWindow { DataContext = wizardVm, Owner = Application.Current.MainWindow };

        if (window.ShowDialog() == true && wizardVm.ImportedLines.Count > 0)
        {
            int nextNumber = Lines.Count == 0 ? 1 : Lines.Max(l => l.Line.LineNumber) + 1;
            foreach (var line in wizardVm.ImportedLines)
            {
                line.LineNumber = nextNumber++;
                Lines.Add(new ImportLineRowViewModel(line));
            }

            _audit.RecordAction(_company.Id, _session.CurrentUser?.Id ?? Guid.Empty, _session.CurrentUser?.DisplayName ?? "Inconnu",
                "EXCEL_IMPORT", "IMPORT_LINES_FROM_EXCEL",
                newValue: $"{wizardVm.ImportedLines.Count} ligne(s) importée(s)", importOperationId: _operation.Id);
        }
    }

    private void OpenHsConfirmDialog(ImportLineRowViewModel? row)
    {
        if (row == null) return;
        _session.RequireNotConsultation("confirmer un code SH proposé par l'IA");

        var classifier = _engineFactory.CreateHsClassifier();
        var baseInput = new HsClassificationInput(
            row.ProductReference, row.Designation, null, row.OriginCountryIso2, null);

        // Revue du 2026-10-01 (point 4) : jusqu'à 3 candidats sont désormais proposés (au lieu d'un seul),
        // chacun avec justification, niveau de confiance, codes alternatifs et informations manquantes.
        var initialCandidates = classifier.ClassifyCandidates(baseInput);

        row.Line.AiProposedHsCode10 = initialCandidates.FirstOrDefault()?.HsCode10;
        row.Line.AiHsDecision = AiProposalDecision.PendingUserValidation;

        var dialogVm = new HsConfirmDialogViewModel(
            classifier, baseInput, initialCandidates, row.ProductReference, row.Designation, row.OriginCountryIso2);
        var window = new HsConfirmDialog { DataContext = dialogVm, Owner = Application.Current.MainWindow };

        if (window.ShowDialog() == true)
        {
            var proposal = dialogVm.BuildProposalForSelectedCandidate();
            classifier.ApplyUserDecisionOnImportLine(row.Line, proposal, dialogVm.Decision, dialogVm.ModifiedHsCode);
            OnPropertyChanged(nameof(Lines));
            row.OnHsChanged();

            // Trace complète de la validation (point 4 de la revue) : proposition IA + justification,
            // date (TimestampUtc, automatique), source/version du service, utilisateur ayant validé
            // (UserId/UserDisplayName), et code finalement retenu (NewValue).
            _audit.RecordAction(_company.Id, _session.CurrentUser?.Id ?? Guid.Empty, _session.CurrentUser?.DisplayName ?? "Inconnu",
                "HS_CLASSIFICATION", $"AI_HS_{dialogVm.Decision}".ToUpperInvariant(),
                oldValue: $"Proposé par IA : {proposal.ProposedHsCode10} (confiance {proposal.ConfidencePercent:F0} %) — Justification : {proposal.JustificationFr}",
                newValue: row.Line.HsCodeConfirmed10,
                importOperationId: _operation.Id,
                regulatoryRuleCode: proposal.ProposedHsCode10,
                legalSourceReference: classifier.ServiceNameAndVersion,
                regulatoryVersionCode: classifier.ServiceNameAndVersion);
        }
    }

    private void OpenLineDetailDialog(ImportLineRowViewModel? row)
    {
        if (row?.Result == null)
        {
            MessageBox.Show("Veuillez exécuter le calcul avant de consulter le détail de cet article.", "CIMP", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new LineDetailDialog { DataContext = new LineDetailDialogViewModel(row), Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }

    private void Calculate()
    {
        try
        {
            _session.RequireNotConsultation("exécuter le calcul du coût d'importation");

            _operation.Lines.Clear();
            _operation.Lines.AddRange(Lines.Select(r => r.Line));
            _operation.Fees.Clear();
            _operation.Fees.AddRange(Fees);

            var orchestrator = _engineFactory.CreateOrchestrator();
            var summary = orchestrator.ExecuteCalculation(_company, _operation);
            _lastSummary = summary;

            foreach (var row in Lines)
            {
                row.Result = summary.LineResults.FirstOrDefault(l => l.LineNumber == row.Line.LineNumber);
                row.AnomaliesForLine = summary.Anomalies.Where(a => a.LineNumber == row.Line.LineNumber).ToList();
                row.RaiseEtatChanged();
            }

            Anomalies.Clear();
            foreach (var a in summary.Anomalies.OrderByDescending(a => a.Severity))
                Anomalies.Add(a);

            HasBlockingAnomalies = summary.HasBlockingAnomalies;
            TotalCoutRevientDzd = summary.TotalRealCostOfGoodsDzd;
            TotalValeurDouaneDzd = summary.TotalCustomsValueDzd;
            TotalDroitsDouaneDzd = summary.TotalCustomsDutyDzd;
            TotalTvaDzd = summary.TotalImportVatDzd;
            TotalFraisDzd = summary.TotalImportFeesDzd;

            _operationRepository.SaveOperation(_operation);

            string versionLabel = summary.LineResults
                .SelectMany(l => l.CustomsOutcome.AdditionalTaxes.Select(t => t.RegulatoryVersionCode))
                .Concat(new[] { "N/A" })
                .Distinct()
                .First();

            _snapshotRepository.SaveSnapshot(_company.Id, _session.CurrentUser?.Id ?? Guid.Empty, versionLabel, summary);

            _audit.RecordAction(_company.Id, _session.CurrentUser?.Id ?? Guid.Empty, _session.CurrentUser?.DisplayName ?? "Inconnu",
                "CALCULATION", "EXECUTE_CALCULATION",
                newValue: $"{summary.TotalRealCostOfGoodsDzd:N2} DZD — {summary.Anomalies.Count} anomalie(s)",
                importOperationId: _operation.Id);

            RefreshExchangeRateInfo();
            RefreshAuthorizationExchangeRateInfo();

            var commercialConversion = summary.CommercialAuthorizationConversion;
            AuthorizationConversionSummary = commercialConversion == null
                ? string.Empty
                : $"Montant facture : {commercialConversion.OriginalTotalAmount:N2} {commercialConversion.OriginalCurrencyCode}  →  Montant autorisation : {commercialConversion.AuthorizationTotalAmount:N2} {commercialConversion.AuthorizationCurrencyCode} (taux {commercialConversion.EffectiveRate:F4}, {commercialConversion.RateTypeLabelFr})";

            MessageBox.Show(
                summary.HasBlockingAnomalies
                    ? "Calcul exécuté avec des anomalies BLOQUANTES : le coût de revient ne peut pas être considéré comme définitif tant qu'elles ne sont pas résolues."
                    : $"Calcul exécuté avec succès.\nCoût total de revient : {summary.TotalRealCostOfGoodsDzd:N2} DZD.",
                "CIMP — Résultat du calcul", MessageBoxButton.OK,
                summary.HasBlockingAnomalies ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "CIMP — Action refusée", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportExcel()
    {
        if (_lastSummary == null)
        {
            MessageBox.Show("Veuillez d'abord exécuter le calcul.", "CIMP", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = $"Rapport_{_operation.ImportNumber}.xlsx",
            Filter = "Classeur Excel (*.xlsx)|*.xlsx"
        };
        if (dialog.ShowDialog() != true) return;

        var reportBuilder = _engineFactory.CreateReportBuilder();
        var model = reportBuilder.BuildDynamicFiveSheetExcelReport(_company, _operation, _lastSummary, "Voir feuille TAXES pour le détail par ligne");
        ExcelWorkbookWriter.WriteToFile(model, dialog.FileName);

        _audit.RecordAction(_company.Id, _session.CurrentUser?.Id ?? Guid.Empty, _session.CurrentUser?.DisplayName ?? "Inconnu",
            "EXPORT", "EXPORT_EXCEL", newValue: dialog.FileName, importOperationId: _operation.Id);

        MessageBox.Show($"Export Excel généré :\n{dialog.FileName}", "CIMP", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExportPdf()
    {
        if (_lastSummary == null)
        {
            MessageBox.Show("Veuillez d'abord exécuter le calcul.", "CIMP", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = $"Rapport_{_operation.ImportNumber}.pdf",
            Filter = "Document PDF (*.pdf)|*.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        var reportBuilder = _engineFactory.CreateReportBuilder();
        var model = reportBuilder.BuildDynamicPdfReport(_company, _operation, _lastSummary, "Voir feuille TAXES pour le détail par ligne");
        PdfReportWriter.WriteToFile(model, dialog.FileName);

        _audit.RecordAction(_company.Id, _session.CurrentUser?.Id ?? Guid.Empty, _session.CurrentUser?.DisplayName ?? "Inconnu",
            "EXPORT", "EXPORT_PDF", newValue: dialog.FileName, importOperationId: _operation.Id);

        MessageBox.Show($"Export PDF généré :\n{dialog.FileName}", "CIMP", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
