using System;
using System.Collections.ObjectModel;
using System.Windows;
using ImportCostAlgeria.Audit;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Database.Repositories;
using ImportCostAlgeria.Presentation.Infrastructure;
using ImportCostAlgeria.Presentation.Services;
using ImportCostAlgeria.Presentation.Views;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>
/// Écran "Paramètres &amp; Utilisateurs" (Sections 22, 24 & 32) : bascule TVA non récupérable par
/// entreprise (paramétrable, non récupérable par défaut) et gestion des comptes/rôles applicatifs.
/// </summary>
public sealed class ParametresViewModel : ObservableObject
{
    private readonly CompanyRepository _companyRepository;
    private readonly UserRepository _userRepository;
    private readonly SessionContext _session;
    private readonly AuditTrailService _audit;

    private Company? _company;
    private string _newUsername = string.Empty;
    private string _newDisplayName = string.Empty;
    private string _newPassword = string.Empty;
    private UserRole _newRole = UserRole.Utilisateur;

    public ParametresViewModel(CompanyRepository companyRepository, UserRepository userRepository, SessionContext session, AuditTrailService audit)
    {
        _companyRepository = companyRepository;
        _userRepository = userRepository;
        _session = session;
        _audit = audit;

        Users = new ObservableCollection<AppUser>();
        Roles = Enum.GetValues<UserRole>();

        SaveCompanySettingsCommand = new RelayCommand(SaveCompanySettings, () => _company != null);
        CreateUserCommand = new RelayCommand(CreateUser);
        RefreshUsersCommand = new RelayCommand(RefreshUsers);
        ChangeMyPasswordCommand = new RelayCommand(ChangeMyPassword);

        RefreshUsers();
    }

    public ObservableCollection<AppUser> Users { get; }
    public UserRole[] Roles { get; }

    public bool IsImportVatNonRecoverable
    {
        get => _company?.IsImportVatNonRecoverable ?? true;
        set
        {
            if (_company == null) return;
            _company.IsImportVatNonRecoverable = value;
            OnPropertyChanged();
        }
    }

    public string CompanyLabel => _company == null ? "Aucune entreprise sélectionnée" : $"{_company.Code} — {_company.LegalName}";

    public string NewUsername { get => _newUsername; set => SetField(ref _newUsername, value); }
    public string NewDisplayName { get => _newDisplayName; set => SetField(ref _newDisplayName, value); }
    public string NewPassword { get => _newPassword; set => SetField(ref _newPassword, value); }
    public UserRole NewRole { get => _newRole; set => SetField(ref _newRole, value); }

    public RelayCommand SaveCompanySettingsCommand { get; }
    public RelayCommand CreateUserCommand { get; }
    public RelayCommand RefreshUsersCommand { get; }
    public RelayCommand ChangeMyPasswordCommand { get; }

    public void LoadForCompany(Company company)
    {
        _company = company;
        OnPropertyChanged(nameof(IsImportVatNonRecoverable));
        OnPropertyChanged(nameof(CompanyLabel));
    }

    private void SaveCompanySettings()
    {
        if (_company == null) return;
        try
        {
            _session.RequireNotConsultation("modifier les paramètres de l'entreprise");
            _companyRepository.Save(_company);

            _audit.RecordAction(_company.Id, _session.CurrentUser?.Id ?? Guid.Empty, _session.CurrentUser?.DisplayName ?? "Inconnu",
                "COMPANY_SETTINGS", "UPDATE_VAT_RECOVERABILITY",
                newValue: _company.IsImportVatNonRecoverable ? "TVA non récupérable" : "TVA récupérable");

            MessageBox.Show("Paramètres enregistrés.", "CIMP", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "CIMP — Action refusée", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshUsers()
    {
        Users.Clear();
        foreach (var u in _userRepository.GetAll())
            Users.Add(u);
    }

    /// <summary>
    /// Section 18 (sécurité) : tout utilisateur peut changer volontairement son propre mot de passe à
    /// tout moment, pas uniquement lors du changement obligatoire du mot de passe initial.
    /// </summary>
    private void ChangeMyPassword()
    {
        if (_session.CurrentUser == null) return;

        var vm = new ChangePasswordViewModel(_userRepository, _session.CurrentUser);
        var window = new ChangePasswordWindow { DataContext = vm, Owner = Application.Current.MainWindow };

        if (window.ShowDialog() == true)
        {
            _audit.RecordAction(null, _session.CurrentUser.Id, _session.CurrentUser.DisplayName,
                "USER_MANAGEMENT", "CHANGE_OWN_PASSWORD");
            MessageBox.Show("Mot de passe modifié avec succès.", "CIMP", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CreateUser()
    {
        try
        {
            _session.RequireAdministrator("créer un compte utilisateur");

            if (string.IsNullOrWhiteSpace(NewUsername) || string.IsNullOrWhiteSpace(NewDisplayName) || string.IsNullOrWhiteSpace(NewPassword))
            {
                MessageBox.Show("Identifiant, nom affiché et mot de passe sont obligatoires.", "CIMP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var user = _userRepository.CreateUser(NewUsername, NewDisplayName, NewPassword, NewRole);

            _audit.RecordAction(null, _session.CurrentUser!.Id, _session.CurrentUser.DisplayName,
                "USER_MANAGEMENT", "CREATE_USER", newValue: $"{user.Username} ({SessionContext.RoleLabel(user.Role)})");

            NewUsername = string.Empty;
            NewDisplayName = string.Empty;
            NewPassword = string.Empty;
            RefreshUsers();

            MessageBox.Show("Utilisateur créé avec succès.", "CIMP", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "CIMP — Action refusée", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
