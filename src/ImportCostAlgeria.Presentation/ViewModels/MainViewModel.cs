using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Database.Repositories;
using ImportCostAlgeria.Presentation.Infrastructure;
using ImportCostAlgeria.Presentation.Services;
using ImportCostAlgeria.Presentation.Views;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>
/// ViewModel de la coquille applicative (Section 37 — menu principal). Revue du 2026-10-02 (REFONTE
/// INTERFACE, Sections 1-3) : l'ancienne navigation par volet gauche (boutons radio) est SUPPRIMÉE — le
/// contenu principal affiche directement l'écran "Importations" au démarrage (et la fiche détaillée d'une
/// importation lorsqu'on l'ouvre), tandis que les écrans auparavant accessibles par le volet gauche
/// (Entreprises, Réglementation, Taux de change, Journal d'audit, Paramètres &amp; Utilisateurs) restent
/// TOUS accessibles — sans aucune perte de fonctionnalité — depuis le nouveau menu "Paramètres", chacun
/// ouvert dans une fenêtre dédiée (<see cref="ChildScreenWindow"/>). Le "Tableau de bord" n'est plus
/// exposé dans l'interface (demande explicite de la Section 1) ; son ViewModel/Vue restent présents dans
/// le code (aucune suppression de fonctionnalité métier) mais ne sont plus atteignables depuis le menu.
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

        NewImportCommand = new RelayCommand(() => _importationsVm.NewCommand.Execute(null), () => SelectedCompany != null && _importationsVm.NewCommand.CanExecute(null));
        RefreshImportationsCommand = new RelayCommand(() => { if (SelectedCompany != null) _importationsVm.LoadForCompany(SelectedCompany); });
        OpenEntreprisesCommand = new RelayCommand(() => OpenChildWindow(_entreprisesVm, "CIMP — Entreprises"));
        OpenParametresCommand = new RelayCommand(() => OpenChildWindow(_parametresVm, "CIMP — Utilisateurs, rôles & sécurité"), () => SelectedCompany != null);
        OpenTauxDeChangeCommand = new RelayCommand(() => OpenChildWindow(_tauxDeChangeVm, "CIMP — Taux de change"));
        OpenReglementationCommand = new RelayCommand(() => OpenChildWindow(_reglementationVm, "CIMP — Paramètres fiscaux & réglementation"));
        OpenAuditCommand = new RelayCommand(() => OpenChildWindow(_auditLogVm, "CIMP — Journal d'audit"));
        ShowAboutCommand = new RelayCommand(ShowAbout);
        QuitCommand = new RelayCommand(() => Application.Current.Shutdown());

        _importationsVm.NavigateToDetail = vm => CurrentView = vm;
        _importationsVm.NavigateBackToList = () => CurrentView = _importationsVm;
        _entreprisesVm.CompanySaved = RefreshCompanies;

        // Section 3 de la refonte : l'écran de démarrage est désormais directement "Importations"
        // (l'ancien "Tableau de bord" n'est plus affiché nulle part dans l'interface).
        ShowImportations();
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

    public RelayCommand NewImportCommand { get; }
    public RelayCommand RefreshImportationsCommand { get; }
    public RelayCommand OpenEntreprisesCommand { get; }
    public RelayCommand OpenParametresCommand { get; }
    public RelayCommand OpenTauxDeChangeCommand { get; }
    public RelayCommand OpenReglementationCommand { get; }
    public RelayCommand OpenAuditCommand { get; }
    public RelayCommand ShowAboutCommand { get; }
    public RelayCommand QuitCommand { get; }

    public void RefreshCompanies()
    {
        Companies.Clear();
        foreach (var c in _companyRepository.GetAll())
            Companies.Add(c);

        if (SelectedCompany == null && Companies.Count > 0)
            SelectedCompany = Companies.First();
    }

    private void ShowImportations() => CurrentView = _importationsVm;

    /// <summary>
    /// Revue du 2026-10-02 (REFONTE INTERFACE, Section 2) : ouvre un écran auparavant accessible par
    /// l'ancienne navigation gauche dans une fenêtre MODALE dédiée (même convention que les autres boîtes
    /// de dialogue de l'application — ExcelImportWizardWindow, ChangePasswordWindow...), sans jamais
    /// perdre le contenu actuellement affiché dans la fenêtre principale (Importations ou fiche détaillée).
    /// </summary>
    private void OpenChildWindow(object viewModel, string title)
    {
        var window = new ChildScreenWindow { DataContext = viewModel, Owner = Application.Current.MainWindow, Title = title };
        window.ShowDialog();
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            "CIMP — Coût d'Importation Maître Pro (Algérie)\n\n" +
            "Calcul de la liquidation douanière, des taxes et du coût de revient réel des importations, " +
            "avec traçabilité réglementaire complète (Journal Officiel, articles de loi).",
            "À propos de CIMP", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
