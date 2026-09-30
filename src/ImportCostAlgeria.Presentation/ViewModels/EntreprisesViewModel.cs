using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using ImportCostAlgeria.Audit;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Database.Repositories;
using ImportCostAlgeria.Presentation.Infrastructure;
using ImportCostAlgeria.Presentation.Services;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>Écran "Entreprises" (Section 5) : CRUD complet, données 100 % réelles (base EF Core).</summary>
public sealed class EntreprisesViewModel : ObservableObject
{
    private readonly CompanyRepository _companyRepository;
    private readonly SessionContext _session;
    private readonly AuditTrailService _audit;
    private Company? _selected;

    public EntreprisesViewModel(CompanyRepository companyRepository, SessionContext session, AuditTrailService audit)
    {
        _companyRepository = companyRepository;
        _session = session;
        _audit = audit;

        Companies = new ObservableCollection<Company>();
        NewCommand = new RelayCommand(CreateNew);
        SaveCommand = new RelayCommand(Save, () => Selected != null);
        RefreshCommand = new RelayCommand(Refresh);

        Refresh();
    }

    public ObservableCollection<Company> Companies { get; }

    public Company? Selected
    {
        get => _selected;
        set => SetField(ref _selected, value);
    }

    public RelayCommand NewCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand RefreshCommand { get; }

    /// <summary>Permet à la coquille principale (MainViewModel) de rafraîchir le sélecteur d'entreprise.</summary>
    public Action? CompanySaved { get; set; }

    public void Refresh()
    {
        Companies.Clear();
        foreach (var c in _companyRepository.GetAll())
            Companies.Add(c);
    }

    private void CreateNew()
    {
        var company = new Company
        {
            Code = $"ENT-{DateTime.Now:yyMMddHHmmss}",
            LegalName = "Nouvelle entreprise",
            DefaultFunctionalCurrency = "DZD",
            IsImportVatNonRecoverable = true
        };
        Companies.Add(company);
        Selected = company;
    }

    private void Save()
    {
        if (Selected == null) return;
        try
        {
            _session.RequireNotConsultation("enregistrer une entreprise");

            if (string.IsNullOrWhiteSpace(Selected.Code) || string.IsNullOrWhiteSpace(Selected.LegalName))
            {
                MessageBox.Show("Le code et la raison sociale sont obligatoires.", "CIMP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _companyRepository.Save(Selected);
            _audit.RecordAction(
                companyId: Selected.Id,
                userId: _session.CurrentUser?.Id ?? Guid.Empty,
                userDisplayName: _session.CurrentUser?.DisplayName ?? "Inconnu",
                actionCategory: "COMPANY",
                actionName: "CREATE_OR_UPDATE_COMPANY",
                newValue: $"{Selected.Code} — {Selected.LegalName}");

            Refresh();
            CompanySaved?.Invoke();
            MessageBox.Show("Entreprise enregistrée.", "CIMP", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "CIMP — Action refusée", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
