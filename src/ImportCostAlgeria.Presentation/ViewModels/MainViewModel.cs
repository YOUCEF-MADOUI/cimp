using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Database.Repositories;
using ImportCostAlgeria.Presentation.Infrastructure;
using ImportCostAlgeria.Presentation.Services;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>
/// ViewModel de la coquille applicative (Section 37 — menu principal) : navigation entre écrans et
/// sélection de l'entreprise active (Section 32 — isolation multi-entreprise).
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly CompanyRepository _companyRepository;
    private readonly SessionContext _session;
    private readonly DashboardScreenViewModel _dashboardVm;
    private readonly EntreprisesViewModel _entreprisesVm;
    private readonly ImportationsViewModel _importationsVm;
    private readonly ReglementationViewModel _reglementationVm;
    private readonly TauxDeChangeViewModel _tauxDeChangeVm;
    private readonly AuditLogViewModel _auditLogVm;
    private readonly ParametresViewModel _parametresVm;

    private object? _currentView;
    private Company? _selectedCompany;
    private string _selectedMenu = "TABLEAU DE BORD";

    public MainViewModel(
        CompanyRepository companyRepository,
        SessionContext session,
        DashboardScreenViewModel dashboardVm,
        EntreprisesViewModel entreprisesVm,
        ImportationsViewModel importationsVm,
        ReglementationViewModel reglementationVm,
        TauxDeChangeViewModel tauxDeChangeVm,
        AuditLogViewModel auditLogVm,
        ParametresViewModel parametresVm)
    {
        _companyRepository = companyRepository;
        _session = session;
        _dashboardVm = dashboardVm;
        _entreprisesVm = entreprisesVm;
        _importationsVm = importationsVm;
        _reglementationVm = reglementationVm;
        _tauxDeChangeVm = tauxDeChangeVm;
        _auditLogVm = auditLogVm;
        _parametresVm = parametresVm;

        Companies = new ObservableCollection<Company>();
        RefreshCompanies();

        ShowDashboardCommand = new RelayCommand(ShowDashboard);
        ShowEntreprisesCommand = new RelayCommand(ShowEntreprises);
        ShowImportationsCommand = new RelayCommand(ShowImportations, () => SelectedCompany != null);
        ShowReglementationCommand = new RelayCommand(ShowReglementation);
        ShowTauxDeChangeCommand = new RelayCommand(ShowTauxDeChange);
        ShowAuditCommand = new RelayCommand(ShowAudit);
        ShowParametresCommand = new RelayCommand(ShowParametres, () => SelectedCompany != null);
        QuitCommand = new RelayCommand(() => Application.Current.Shutdown());

        _importationsVm.NavigateToDetail = vm => CurrentView = vm;
        _importationsVm.NavigateBackToList = () => CurrentView = _importationsVm;
        _entreprisesVm.CompanySaved = RefreshCompanies;

        ShowDashboard();
    }

    public SessionContext Session => _session;

    public ObservableCollection<Company> Companies { get; }

    public Company? SelectedCompany
    {
        get => _selectedCompany;
        set
        {
            if (SetField(ref _selectedCompany, value))
            {
                _session.CurrentCompany = value;
                if (value != null)
                {
                    _importationsVm.LoadForCompany(value);
                    _dashboardVm.LoadForCompany(value);
                    _parametresVm.LoadForCompany(value);
                }
            }
        }
    }

    public object? CurrentView
    {
        get => _currentView;
        set => SetField(ref _currentView, value);
    }

    public string SelectedMenu
    {
        get => _selectedMenu;
        set => SetField(ref _selectedMenu, value);
    }

    public RelayCommand ShowDashboardCommand { get; }
    public RelayCommand ShowEntreprisesCommand { get; }
    public RelayCommand ShowImportationsCommand { get; }
    public RelayCommand ShowReglementationCommand { get; }
    public RelayCommand ShowTauxDeChangeCommand { get; }
    public RelayCommand ShowAuditCommand { get; }
    public RelayCommand ShowParametresCommand { get; }
    public RelayCommand QuitCommand { get; }

    public void RefreshCompanies()
    {
        Companies.Clear();
        foreach (var c in _companyRepository.GetAll())
            Companies.Add(c);

        if (SelectedCompany == null && Companies.Count > 0)
            SelectedCompany = Companies.First();
    }

    private void ShowDashboard() { SelectedMenu = "TABLEAU DE BORD"; CurrentView = _dashboardVm; }
    private void ShowEntreprises() { SelectedMenu = "ENTREPRISES"; CurrentView = _entreprisesVm; }
    private void ShowImportations() { SelectedMenu = "IMPORTATIONS"; CurrentView = _importationsVm; }
    private void ShowReglementation() { SelectedMenu = "RÉGLEMENTATION"; CurrentView = _reglementationVm; }
    private void ShowTauxDeChange() { SelectedMenu = "TAUX DE CHANGE"; CurrentView = _tauxDeChangeVm; }
    private void ShowAudit() { SelectedMenu = "JOURNAL D'AUDIT"; CurrentView = _auditLogVm; }
    private void ShowParametres() { SelectedMenu = "PARAMÈTRES & UTILISATEURS"; CurrentView = _parametresVm; }
}
