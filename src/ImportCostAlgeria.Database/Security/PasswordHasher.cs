using System;
using System.Security.Cryptography;
using System.Text;

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

    // Jeu de caractères volontairement sans caractères ambigus (0/O, 1/l/I) pour un mot de passe initial
    // lisible/saisissable manuellement à l'écran de connexion (Section 18 — sécurité).
    private const string UpperChars = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string LowerChars = "abcdefghijkmnpqrstuvwxyz";
    private const string DigitChars = "23456789";
    private const string SymbolChars = "!@#$%*?-_";

    /// <summary>
    /// Génère un mot de passe aléatoire cryptographiquement sûr (Section 18 — jamais de mot de passe codé
    /// en dur) via <see cref="RandomNumberGenerator"/>, garantissant au moins un caractère de chaque
    /// catégorie (majuscule, minuscule, chiffre, symbole) pour satisfaire les règles de complexité
    /// habituelles. N'est JAMAIS journalisé : seul l'appelant (écran "Premier démarrage") peut l'afficher
    /// une unique fois à l'utilisateur.
    /// </summary>
    public static string GenerateRandomPassword(int length = 14)
    {
        if (length < 8) length = 8;

        const string allChars = UpperChars + LowerChars + DigitChars + SymbolChars;
        var builder = new StringBuilder(length);

        // Garantit la présence d'au moins un caractère de chaque catégorie.
        builder.Append(PickRandomChar(UpperChars));
        builder.Append(PickRandomChar(LowerChars));
        builder.Append(PickRandomChar(DigitChars));
        builder.Append(PickRandomChar(SymbolChars));

        for (int i = builder.Length; i < length; i++)
        {
            builder.Append(PickRandomChar(allChars));
        }

        // Mélange Fisher-Yates avec RandomNumberGenerator pour ne pas laisser les 4 premiers caractères
        // toujours dans le même ordre de catégories.
        char[] chars = builder.ToString().ToCharArray();
        for (int i = chars.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    private static char PickRandomChar(string charset) =>
        charset[RandomNumberGenerator.GetInt32(charset.Length)];

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
