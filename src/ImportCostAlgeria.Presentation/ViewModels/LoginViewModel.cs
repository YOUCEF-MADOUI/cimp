using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Database.Repositories;
using ImportCostAlgeria.Presentation.Infrastructure;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>Écran de connexion (Sections 22 & 32 — comptes utilisateurs et rôles).</summary>
public sealed class LoginViewModel : ObservableObject
{
    private readonly UserRepository _userRepository;

    private string _username = string.Empty;
    private string _errorMessage = string.Empty;

    public LoginViewModel(UserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public string Username
    {
        get => _username;
        set => SetField(ref _username, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value);
    }

    public AppUser? AuthenticatedUser { get; private set; }

    /// <summary>Retourne true si l'authentification réussit (le mot de passe n'est jamais mémorisé côté ViewModel).</summary>
    public bool TryLogin(string plainTextPassword)
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Veuillez saisir un nom d'utilisateur.";
            return false;
        }

        var user = _userRepository.TryAuthenticate(Username, plainTextPassword);
        if (user == null)
        {
            ErrorMessage = "Identifiants incorrects ou compte désactivé.";
            return false;
        }

        AuthenticatedUser = user;
        ErrorMessage = string.Empty;
        return true;
    }
}
