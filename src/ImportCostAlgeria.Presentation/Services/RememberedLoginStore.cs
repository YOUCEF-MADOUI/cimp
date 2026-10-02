using System;
using System.IO;
using System.Security.Cryptography;
using ImportCostAlgeria.Core.Services;

namespace ImportCostAlgeria.Presentation.Services;

/// <summary>
/// Revue du 2026-10-02 (demande utilisateur, Section 4 — "Se souvenir de moi") : implémentation Windows du
/// chiffrement "au repos" via DPAPI (<see cref="ProtectedData"/>), liée au compte Windows de l'utilisateur
/// courant (<see cref="DataProtectionScope.CurrentUser"/>) — solution PRIORITAIRE demandée (Section 4.2).
/// Un autre utilisateur Windows, ou le même profil copié sur une autre machine, ne peut PAS déchiffrer ce
/// secret : <see cref="ProtectedData.Unprotect"/> échoue alors proprement (traité comme "aucun identifiant
/// mémorisé" par <see cref="RememberedCredentialCodec"/>, jamais comme une erreur).
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    // Entropie supplémentaire propre à CIMP : un fichier chiffré par DPAPI pour une AUTRE application du
    // même utilisateur Windows ne peut pas être déchiffré par erreur ici (et inversement).
    private static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("CIMP.RememberedLogin.v1");

    public byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] protectedBytes) =>
        ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
}

/// <summary>
/// Persistance sur disque (profil utilisateur Windows, <c>%LOCALAPPDATA%\CIMP\remembered_login.dat</c>) de
/// l'identifiant mémorisé "Se souvenir de moi" (Section 4 de la demande utilisateur). Le fichier ne contient
/// QUE le résultat de <see cref="RememberedCredentialCodec.Encode"/> (déjà chiffré par DPAPI) — jamais de
/// JSON/texte en clair, jamais écrit dans les appsettings ni la base de données (Section 4.2, interdictions
/// explicites). N'écrit jamais le mot de passe dans un journal/trace/Debug.WriteLine/Audit (Section 4.4).
/// </summary>
public sealed class RememberedLoginStore
{
    private readonly RememberedCredentialCodec _codec;
    private readonly string _filePath;

    public RememberedLoginStore() : this(new RememberedCredentialCodec(new DpapiSecretProtector()), DefaultFilePath())
    {
    }

    /// <summary>Constructeur interne utilisé par les tests (protecteur/chemin de fichier substituables).</summary>
    internal RememberedLoginStore(RememberedCredentialCodec codec, string filePath)
    {
        _codec = codec;
        _filePath = filePath;
    }

    private static string DefaultFilePath()
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CIMP");
        return Path.Combine(folder, "remembered_login.dat");
    }

    /// <summary>
    /// Enregistre (de façon chiffrée) l'identifiant à mémoriser. N'écrit et ne journalise JAMAIS le mot de
    /// passe en clair ailleurs que dans la variable locale `protectedBytes` transmise directement au disque.
    /// </summary>
    public void Save(string username, string plainTextPassword)
    {
        byte[] protectedBytes = _codec.Encode(username, plainTextPassword);
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllBytes(_filePath, protectedBytes);
    }

    /// <summary>
    /// Relit l'identifiant mémorisé, si présent et déchiffrable. Retourne <c>null</c> dans tous les autres
    /// cas (aucun fichier, fichier corrompu, secret Windows changé depuis) — jamais d'exception.
    /// </summary>
    public (string Username, string Password)? TryLoad()
    {
        try
        {
            if (!File.Exists(_filePath))
                return null;

            byte[] protectedBytes = File.ReadAllBytes(_filePath);
            return _codec.TryDecode(protectedBytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Supprime le secret mémorisé (Section 4.1 : case décochée -&gt; suppression ; Section 4.3 : mot de
    /// passe changé -&gt; invalidation de l'ancien secret).
    /// </summary>
    public void Clear()
    {
        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        catch
        {
            // La suppression d'un fichier de cache local ne doit jamais faire planter l'application.
        }
    }
}
