using System;
using System.Security.Cryptography;

namespace ImportCostAlgeria.Database.Security;

/// <summary>
/// Hachage de mots de passe par PBKDF2/SHA-256 (Section 32 — comptes utilisateurs).
/// Aucun mot de passe n'est jamais stocké ou journalisé en clair. Utilise exclusivement les classes du
/// framework .NET (System.Security.Cryptography) : aucune dépendance externe supplémentaire nécessaire.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 100_000;

    public static (string HashBase64, string SaltBase64) HashNewPassword(string plainTextPassword)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(plainTextPassword, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(string plainTextPassword, string expectedHashBase64, string saltBase64)
    {
        byte[] salt = Convert.FromBase64String(saltBase64);
        byte[] expectedHash = Convert.FromBase64String(expectedHashBase64);
        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(plainTextPassword, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
