using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Database.Security;

namespace ImportCostAlgeria.Database;

public enum DatabaseProviderKind
{
    Sqlite,
    SqlServer
}

/// <summary>
/// Point d'entrée unique de configuration/initialisation de la base de données (Priorité 2 du plan de
/// finalisation Windows : "Connexion base de données").
/// Par défaut, utilise un fichier SQLite local (%LOCALAPPDATA%\CIMP\cimp.db) : l'application fonctionne
/// donc immédiatement après un F5 dans Visual Studio, sans installation préalable d'un serveur de base
/// de données. Un administrateur qui dispose déjà d'un serveur SQL Server peut basculer vers celui-ci en
/// modifiant simplement la chaîne de connexion (voir App.config / appsettings applicatif).
/// </summary>
public static class DbContextFactory
{
    public static string GetDefaultSqliteFilePath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string folder = Path.Combine(localAppData, "CIMP");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "cimp.db");
    }

    public static DbContextOptions<ImportCostDbContext> BuildOptions(
        DatabaseProviderKind provider = DatabaseProviderKind.Sqlite,
        string? explicitConnectionString = null)
    {
        var builder = new DbContextOptionsBuilder<ImportCostDbContext>();

        switch (provider)
        {
            case DatabaseProviderKind.Sqlite:
                string sqlitePath = explicitConnectionString ?? $"Data Source={GetDefaultSqliteFilePath()}";
                builder.UseSqlite(sqlitePath.Contains("Data Source", StringComparison.OrdinalIgnoreCase)
                    ? sqlitePath
                    : $"Data Source={sqlitePath}");
                break;

            case DatabaseProviderKind.SqlServer:
                if (string.IsNullOrWhiteSpace(explicitConnectionString))
                    throw new ArgumentException("Une chaîne de connexion SQL Server explicite est obligatoire.");
                builder.UseSqlServer(explicitConnectionString);
                break;
        }

        return builder.Options;
    }

    /// <summary>
    /// Crée (si nécessaire) et initialise proprement la base de données au premier démarrage
    /// (Priorité 2, exigence : "l'application doit pouvoir initialiser sa base proprement").
    /// Utilise EnsureCreated() plutôt que des migrations EF précompilées afin de garantir un
    /// fonctionnement immédiat sans dépendance à l'outil "dotnet-ef" côté poste utilisateur.
    /// Un administrateur qui souhaite gérer des migrations incrémentales classiques peut à tout moment
    /// exécuter "dotnet ef migrations add InitialCreate" une fois Visual Studio et le SDK installés :
    /// le modèle ci-dessus (ImportCostDbContext.OnModelCreating) est entièrement compatible avec les
    /// migrations EF Core standard.
    /// Ne sème JAMAIS de taux ou de règle réglementaire fictive (Section 25/41) : seul un compte
    /// administrateur technique initial est créé, clairement documenté comme tel.
    /// </summary>
    public static (bool DatabaseWasJustCreated, string? InitialAdminUsername, string? InitialAdminPassword) EnsureDatabaseReadyWithSeed(
        ImportCostDbContext context)
    {
        bool created = context.Database.EnsureCreated();

        string? initialUsername = null;
        string? initialPassword = null;

        if (!context.Users.Any())
        {
            // Compte administrateur technique de démarrage (PAS une donnée réglementaire officielle ou
            // fictive : uniquement un compte applicatif nécessaire pour accéder à l'application au tout
            // premier lancement). Le mot de passe généré est affiché une seule fois à l'écran de
            // connexion et doit être changé par l'utilisateur.
            initialUsername = "admin";
            initialPassword = GenerateInitialPassword();
            var (hash, salt) = PasswordHasher.HashNewPassword(initialPassword);

            context.Users.Add(new AppUser
            {
                Username = initialUsername,
                DisplayName = "Administrateur Initial",
                PasswordHash = hash,
                PasswordSalt = salt,
                Role = UserRole.Administrateur,
                IsActive = true
            });

            context.SaveChanges();
        }

        return (created, initialUsername, initialPassword);
    }

    private static string GenerateInitialPassword()
    {
        // Mot de passe initial simple et mémorisable pour le tout premier démarrage local (poste de
        // développement / démonstration) ; l'écran "Utilisateurs" permet de le changer immédiatement.
        return "Cimp@2026!";
    }
}
