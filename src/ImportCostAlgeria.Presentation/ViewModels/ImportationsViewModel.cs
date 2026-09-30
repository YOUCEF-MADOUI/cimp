using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using ImportCostAlgeria.Audit;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Database.Repositories;
using ImportCostAlgeria.Presentation.Infrastructure;
using ImportCostAlgeria.Presentation.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>Écran "Importations" (Section 5) : liste + création d'en-tête d'importation.</summary>
public sealed class ImportationsViewModel : ObservableObject
{
    private readonly ImportOperationRepository _operationRepository;
    private readonly SessionContext _session;
    private readonly AuditTrailService _audit;
    private readonly IServiceProvider _serviceProvider;

    private Company? _company;
    private ImportOperation? _selected;

    public ImportationsViewModel(
        ImportOperationRepository operationRepository,
        SessionContext session,
        AuditTrailService audit,
        IServiceProvider serviceProvider)
    {
        _operationRepository = operationRepository;
        _session = session;
        _audit = audit;
        _serviceProvider = serviceProvider;

        Operations = new ObservableCollection<ImportOperation>();
        IncotermsV1 = new[] { IncotermCode.EXW, IncotermCode.FOB, IncotermCode.CFR };

        NewCommand = new RelayCommand(CreateNew, () => _company != null);
        SaveCommand = new RelayCommand(Save, () => Selected != null);
        OpenCommand = new RelayCommand(OpenSelected, () => Selected != null);
        DeleteCommand = new RelayCommand(DeleteSelected, () => Selected != null);
    }

    public ObservableCollection<ImportOperation> Operations { get; }
    public IncotermCode[] IncotermsV1 { get; }

    public Action<object>? NavigateToDetail { get; set; }
    public Action? NavigateBackToList { get; set; }

    public ImportOperation? Selected
    {
        get => _selected;
        set => SetField(ref _selected, value);
    }

    public RelayCommand NewCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand DeleteCommand { get; }

    public void LoadForCompany(Company company)
    {
        _company = company;
        Operations.Clear();
        foreach (var op in _operationRepository.GetForCompany(company.Id))
            Operations.Add(op);
    }

    private void CreateNew()
    {
        if (_company == null) return;

        var op = new ImportOperation
        {
            CompanyId = _company.Id,
            ImportNumber = $"IMP-{DateTime.Now:yyyyMMdd-HHmmss}",
            ReferenceDate = DateOnly.FromDateTime(DateTime.Today),
            SupplierName = "Nouveau fournisseur",
            ExportShippingCountryIso2 = "CN",
            MainCurrencyCode = "USD",
            Incoterm = IncotermCode.FOB,
            ArrivalPortOrBorder = "Port d'Alger",
            TransportMode = "Maritime"
        };
        Operations.Add(op);
        Selected = op;
    }

    private void Save()
    {
        if (Selected == null) return;
        try
        {
            _session.RequireNotConsultation("créer/modifier une importation");

            if (string.IsNullOrWhiteSpace(Selected.ImportNumber) || string.IsNullOrWhiteSpace(Selected.SupplierName))
            {
                MessageBox.Show("Le numéro d'importation et le fournisseur sont obligatoires.", "CIMP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _operationRepository.SaveOperation(Selected);
            _audit.RecordAction(
                companyId: Selected.CompanyId,
                userId: _session.CurrentUser?.Id ?? Guid.Empty,
                userDisplayName: _session.CurrentUser?.DisplayName ?? "Inconnu",
                actionCategory: "IMPORT_OPERATION",
                actionName: "CREATE_OR_UPDATE_IMPORT",
                newValue: Selected.ImportNumber,
                importOperationId: Selected.Id);

            if (_company != null) LoadForCompany(_company);
            MessageBox.Show("Importation enregistrée.", "CIMP", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "CIMP — Action refusée", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenSelected()
    {
        if (Selected == null || _company == null) return;

        var detailVm = _serviceProvider.GetRequiredService<ImportDetailViewModel>();
        detailVm.Initialize(_company, Selected, this);
        NavigateToDetail?.Invoke(detailVm);
    }

    private void DeleteSelected()
    {
        if (Selected == null || _company == null) return;
        try
        {
            _session.RequireAdministrator("supprimer une importation");
            if (MessageBox.Show($"Supprimer définitivement l'importation '{Selected.ImportNumber}' ?", "Confirmation",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            _operationRepository.Delete(_company.Id, Selected.Id);
            _audit.RecordAction(_company.Id, _session.CurrentUser?.Id ?? Guid.Empty, _session.CurrentUser?.DisplayName ?? "Inconnu",
                "IMPORT_OPERATION", "DELETE_IMPORT", oldValue: Selected.ImportNumber, importOperationId: Selected.Id);

            LoadForCompany(_company);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "CIMP — Action refusée", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
