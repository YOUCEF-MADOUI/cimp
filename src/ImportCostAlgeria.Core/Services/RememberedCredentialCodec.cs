using System;
using System.Text;

namespace ImportCostAlgeria.Core.Services;

/// <summary>
/// Abstraction du chiffrement "au repos" d'un secret (Section 4.2 de la demande utilisateur — "Se souvenir
/// de moi" : jamais de mot de passe en clair dans un fichier JSON, un appsettings, la base de données ou en
/// dur dans le code). L'implémentation réelle PRIORITAIRE (Windows DPAPI via
/// <c>System.Security.Cryptography.ProtectedData</c>, liée au compte Windows de la session courante — et,
/// à défaut, Windows Credential Manager) vit dans <c>ImportCostAlgeria.Presentation</c> (seul projet
/// dépendant de Windows) : cette interface reste ici, dans Core, STRICTEMENT indépendante de toute API
/// Windows/OS, afin que la logique d'encodage/décodage ci-dessous reste testable sur toute plateforme.
/// </summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] protectedBytes);
}

/// <summary>
/// Logique PURE (sans aucune dépendance Windows/WPF) d'encodage et de décodage d'un identifiant mémorisé
/// ("Se souvenir de moi", Sections 4.1 à 4.4 de la demande utilisateur). Ne manipule le mot de passe en
/// clair qu'EN MÉMOIRE, le temps strictement nécessaire à l'appel de <see cref="ISecretProtector"/> — le
/// tableau d'octets retourné par <see cref="Encode"/> est TOUJOURS le résultat du chiffrement, jamais une
/// valeur en clair. Ne journalise jamais rien (aucun appel de log/trace/audit dans cette classe).
/// </summary>
public sealed class RememberedCredentialCodec
{
    private const char FieldSeparator = '\u0001';
    private readonly ISecretProtector _protector;

    public RememberedCredentialCodec(ISecretProtector protector)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    /// <summary>
    /// Sérialise puis chiffre (utilisateur + mot de passe) en un tableau d'octets prêt à être écrit sur
    /// disque. Le tableau retourné ne contient JAMAIS le mot de passe en clair (il a déjà été chiffré par
    /// <see cref="ISecretProtector.Protect"/> avant d'être retourné).
    /// </summary>
    public byte[] Encode(string username, string plainTextPassword)
    {
        if (string.IsNullOrEmpty(username))
            throw new ArgumentException("Le nom d'utilisateur ne peut pas être vide.", nameof(username));

        string payload = username + FieldSeparator + (plainTextPassword ?? string.Empty);
        byte[] raw = Encoding.UTF8.GetBytes(payload);
        try
        {
            return _protector.Protect(raw);
        }
        finally
        {
            Array.Clear(raw, 0, raw.Length);
        }
    }

    /// <summary>
    /// Déchiffre un identifiant mémorisé précédemment encodé par <see cref="Encode"/>. Retourne
    /// <c>null</c> si le contenu est corrompu, vide, ou ne peut être déchiffré (ex : mot de passe Windows
    /// de la session modifié depuis, profil déplacé vers une autre machine, fichier altéré) — un échec de
    /// déchiffrement est TOUJOURS traité comme "aucun identifiant mémorisé" (Section 4.3), jamais comme une
    /// erreur bloquante qui empêcherait l'affichage normal de l'écran de connexion.
    /// </summary>
    public (string Username, string Password)? TryDecode(byte[]? protectedBytes)
    {
        if (protectedBytes == null || protectedBytes.Length == 0)
            return null;

        try
        {
            byte[] raw = _protector.Unprotect(protectedBytes);
            try
            {
                string payload = Encoding.UTF8.GetString(raw);
                int separatorIndex = payload.IndexOf(FieldSeparator);
                if (separatorIndex < 0)
                    return null;

                string username = payload[..separatorIndex];
                string password = payload[(separatorIndex + 1)..];
                return string.IsNullOrEmpty(username) ? null : (username, password);
            }
            finally
            {
                Array.Clear(raw, 0, raw.Length);
            }
        }
        catch
        {
            // Volontairement silencieux (Section 4.3) : un secret illisible n'est jamais une erreur
            // utilisateur, seulement l'absence d'un identifiant mémorisé valide.
            return null;
        }
    }
}
