using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Database.Repositories;
using ImportCostAlgeria.Presentation.Infrastructure;
using ImportCostAlgeria.Presentation.Services;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>
/// Écran de connexion (Sections 22 & 32 — comptes utilisateurs et rôles ; Section 4 de la revue du
/// 2026-10-02 — "Se souvenir de moi"). Le mot de passe en clair ne transite JAMAIS au-delà de
/// <see cref="TryLogin"/> et de <see cref="RememberedLoginStore"/> (chiffrement DPAPI) : il n'est ni stocké
/// sur une propriété de ce ViewModel, ni journalisé/audité.
/// </summary>
public sealed class LoginViewModel : ObservableObject
{
    private readonly UserRepository _userRepository;
    private readonly RememberedLoginStore _rememberedLoginStore;

    private string _username = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _rememberMe;

    public LoginViewModel(UserRepository userRepository, RememberedLoginStore rememberedLoginStore)
    {
        _userRepository = userRepository;
        _rememberedLoginStore = rememberedLoginStore;
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

    /// <summary>Section 4.1 : case à cocher "Se souvenir de moi".</summary>
    public bool RememberMe
    {
        get => _rememberMe;
        set => SetField(ref _rememberMe, value);
    }

    public AppUser? AuthenticatedUser { get; private set; }

    /// <summary>
    /// Tente de pré-remplir Username/RememberMe et retourne le mot de passe mémorisé (ou null) depuis le
    /// stockage sécurisé (Section 4.3 : "au prochain lancement, préremplir le compte et/ou se connecter
    /// automatiquement"). Ne journalise jamais le mot de passe retourné.
    /// </summary>
    public string? TryLoadRememberedCredential()
    {
        var remembered = _rememberedLoginStore.TryLoad();
        if (remembered == null)
            return null;

        Username = remembered.Value.Username;
        RememberMe = true;
        return remembered.Value.Password;
    }

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
            // Section 4.3 : un identifiant mémorisé qui ne fonctionne plus (mot de passe changé) doit être
            // invalidé, jamais représenté indéfiniment à l'utilisateur comme "toujours mémorisé".
            if (RememberMe)
            {
                _rememberedLoginStore.Clear();
            }
            return false;
        }

        // Section 4.1/4.3 : mémorise (ou supprime) le secret selon l'état de la case à cocher, UNIQUEMENT
        // après une authentification réussie — jamais avant, jamais pour des identifiants invalides.
        if (RememberMe)
        {
            _rememberedLoginStore.Save(Username, plainTextPassword);
        }
        else
        {
            _rememberedLoginStore.Clear();
        }

        AuthenticatedUser = user;
        ErrorMessage = string.Empty;
        return true;
    }
}
