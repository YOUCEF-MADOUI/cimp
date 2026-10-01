using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Database.Repositories;
using ImportCostAlgeria.Presentation.Infrastructure;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>
/// Écran de changement de mot de passe obligatoire (Section 18 de l'audit — sécurité) : affiché
/// systématiquement après la première connexion avec le mot de passe administrateur initial généré
/// aléatoirement (<see cref="AppUser.MustChangePasswordOnNextLogin"/>). Peut aussi être ouvert
/// volontairement par n'importe quel utilisateur depuis l'écran "Utilisateurs".
/// Le mot de passe saisi n'est jamais journalisé (aucun appel d'audit ne transporte sa valeur en clair).
/// </summary>
public sealed class ChangePasswordViewModel : ObservableObject
{
    private readonly UserRepository _userRepository;
    private readonly AppUser _user;

    private string _errorMessage = string.Empty;

    public ChangePasswordViewModel(UserRepository userRepository, AppUser user)
    {
        _userRepository = userRepository;
        _user = user;
    }

    public bool IsMandatory => _user.MustChangePasswordOnNextLogin;

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetField(ref _errorMessage, value);
    }

    /// <summary>Retourne true si le changement a réussi.</summary>
    public bool TryChangePassword(string newPassword, string confirmPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            ErrorMessage = "Le nouveau mot de passe doit contenir au moins 8 caractères.";
            return false;
        }

        if (newPassword != confirmPassword)
        {
            ErrorMessage = "La confirmation ne correspond pas au nouveau mot de passe.";
            return false;
        }

        _userRepository.ChangePassword(_user.Id, newPassword);
        _user.MustChangePasswordOnNextLogin = false;
        ErrorMessage = string.Empty;
        return true;
    }
}
