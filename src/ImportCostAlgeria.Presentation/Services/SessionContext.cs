using System;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Presentation.Infrastructure;

namespace ImportCostAlgeria.Presentation.Services;

/// <summary>
/// Contexte de session applicative (Sections 22 & 32) : utilisateur connecté et entreprise courante.
/// Utilisé pour l'enforcement des rôles (ADMINISTRATEUR / UTILISATEUR / CONSULTATION) dans toute
/// l'interface, et pour l'isolation stricte multi-entreprise des écrans opérationnels.
/// </summary>
public sealed class SessionContext : ObservableObject
{
    private AppUser? _currentUser;
    private Company? _currentCompany;

    public AppUser? CurrentUser
    {
        get => _currentUser;
        set
        {
            if (SetField(ref _currentUser, value))
            {
                OnPropertyChanged(nameof(IsAdministrator));
                OnPropertyChanged(nameof(IsConsultationOnly));
                OnPropertyChanged(nameof(DisplayLabel));
            }
        }
    }

    public Company? CurrentCompany
    {
        get => _currentCompany;
        set => SetField(ref _currentCompany, value);
    }

    public bool IsAdministrator => _currentUser?.Role == UserRole.Administrateur;

    public bool IsConsultationOnly => _currentUser?.Role == UserRole.Consultation;

    public string DisplayLabel => _currentUser == null
        ? "Non connecté"
        : $"{_currentUser.DisplayName} ({RoleLabel(_currentUser.Role)})";

    public static string RoleLabel(UserRole role) => role switch
    {
        UserRole.Administrateur => "Administrateur",
        UserRole.Utilisateur => "Utilisateur",
        UserRole.Consultation => "Consultation",
        _ => role.ToString()
    };

    public void RequireAdministrator(string actionDescriptionFr)
    {
        if (!IsAdministrator)
            throw new InvalidOperationException($"Action réservée à un Administrateur : {actionDescriptionFr}.");
    }

    public void RequireNotConsultation(string actionDescriptionFr)
    {
        if (IsConsultationOnly)
            throw new InvalidOperationException($"Le rôle Consultation est en lecture seule : {actionDescriptionFr} est impossible.");
    }
}
