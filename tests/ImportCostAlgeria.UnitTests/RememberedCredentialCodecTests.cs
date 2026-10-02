using System;
using System.Linq;
using System.Text;
using Xunit;
using ImportCostAlgeria.Core.Services;

namespace ImportCostAlgeria.UnitTests;

/// <summary>
/// Revue du 2026-10-02 (demande utilisateur, Section 4 — "Se souvenir de moi") : vérifie la logique PURE
/// d'encodage/décodage de l'identifiant mémorisé (<see cref="RememberedCredentialCodec"/>), indépendamment
/// de l'implémentation Windows DPAPI réelle (<c>ImportCostAlgeria.Presentation.Services.DpapiSecretProtector</c>,
/// non testable depuis ce projet multiplateforme — voir la convention établie pour
/// <see cref="ProfitCalculator"/>/<see cref="CurrencyDisplay"/> : toute logique nécessitant une couverture de
/// test est extraite en méthode statique/pure dans Core.Services). Utilise un <see cref="ISecretProtector"/>
/// factice (transformation réversible simple) à la place de DPAPI pour isoler la logique de sérialisation.
/// </summary>
public sealed class RememberedCredentialCodecTests
{
    /// <summary>
    /// Protecteur de test : NE REPRÉSENTE PAS DPAPI (bien plus faible), seulement un stand-in réversible
    /// permettant de vérifier que <see cref="RememberedCredentialCodec"/> délègue correctement le
    /// chiffrement/déchiffrement et ne manipule le secret en clair qu'en mémoire.
    /// </summary>
    private sealed class FakeXorProtector : ISecretProtector
    {
        private readonly byte _key;
        public FakeXorProtector(byte key) => _key = key;

        public byte[] Protect(byte[] plaintext) => plaintext.Select(b => (byte)(b ^ _key)).ToArray();
        public byte[] Unprotect(byte[] protectedBytes) => protectedBytes.Select(b => (byte)(b ^ _key)).ToArray();
    }

    /// <summary>Protecteur de test qui échoue systématiquement (simule un déchiffrement DPAPI impossible).</summary>
    private sealed class AlwaysFailingProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plaintext) => plaintext;
        public byte[] Unprotect(byte[] protectedBytes) => throw new System.Security.Cryptography.CryptographicException("Simulated DPAPI failure");
    }

    [Fact]
    public void Encode_ThenTryDecode_RoundTrips_UsernameAndPassword()
    {
        var codec = new RememberedCredentialCodec(new FakeXorProtector(0x5A));

        byte[] encoded = codec.Encode("admin", "S3cr3t!Pass");
        var decoded = codec.TryDecode(encoded);

        Assert.NotNull(decoded);
        Assert.Equal("admin", decoded!.Value.Username);
        Assert.Equal("S3cr3t!Pass", decoded.Value.Password);
    }

    [Fact]
    public void Encode_NeverWritesPlainTextPassword_InTheProtectedBytes()
    {
        // Section 4.2 (interdictions explicites) : le résultat d'Encode() doit toujours avoir été
        // transformé par le protecteur — jamais la représentation UTF8 brute du mot de passe en clair.
        const string plainPassword = "MotDePasseTresSecret123";
        var codec = new RememberedCredentialCodec(new FakeXorProtector(0x3C));

        byte[] encoded = codec.Encode("admin", plainPassword);
        string encodedAsText = Encoding.UTF8.GetString(encoded);

        Assert.DoesNotContain(plainPassword, encodedAsText, StringComparison.Ordinal);
    }

    [Fact]
    public void TryDecode_ReturnsNull_WhenBytesAreNullOrEmpty_NeverThrows()
    {
        var codec = new RememberedCredentialCodec(new FakeXorProtector(0x11));

        Assert.Null(codec.TryDecode(null));
        Assert.Null(codec.TryDecode(Array.Empty<byte>()));
    }

    [Fact]
    public void TryDecode_ReturnsNull_WhenProtectorFailsToUnprotect_NeverThrows()
    {
        // Section 4.3 : un mot de passe Windows changé / profil déplacé doit être traité comme
        // "aucun identifiant mémorisé", jamais comme une exception qui remonterait à l'appelant.
        var codec = new RememberedCredentialCodec(new AlwaysFailingProtector());

        var result = codec.TryDecode(new byte[] { 1, 2, 3, 4 });

        Assert.Null(result);
    }

    [Fact]
    public void TryDecode_ReturnsNull_WhenPayloadHasNoSeparator_CorruptedData()
    {
        var protector = new FakeXorProtector(0x77);
        var codec = new RememberedCredentialCodec(protector);

        // Un payload sans le séparateur attendu doit être traité comme corrompu, pas planter.
        byte[] corrupted = protector.Protect(Encoding.UTF8.GetBytes("no-separator-here"));

        Assert.Null(codec.TryDecode(corrupted));
    }

    [Fact]
    public void Encode_Throws_WhenUsernameIsEmpty()
    {
        var codec = new RememberedCredentialCodec(new FakeXorProtector(0x01));
        Assert.Throws<ArgumentException>(() => codec.Encode(string.Empty, "pwd"));
    }

    [Fact]
    public void Encode_ThenTryDecode_RoundTrips_EmptyPassword()
    {
        var codec = new RememberedCredentialCodec(new FakeXorProtector(0x44));

        byte[] encoded = codec.Encode("admin", string.Empty);
        var decoded = codec.TryDecode(encoded);

        Assert.NotNull(decoded);
        Assert.Equal("admin", decoded!.Value.Username);
        Assert.Equal(string.Empty, decoded.Value.Password);
    }
}
