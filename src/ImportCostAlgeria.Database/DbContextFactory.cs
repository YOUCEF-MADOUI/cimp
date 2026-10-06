using System;
using System.Data;
using System.Data.Common;
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

        // Section 19 de l'audit : voir ApplyLightweightSchemaUpgrades ci-dessous — ajoute sans risque les
        // colonnes introduites par cette version (multi-devises, sécurité mot de passe) à une base SQLite
        // déjà existante, sans jamais toucher aux données déjà présentes.
        ApplyLightweightSchemaUpgrades(context);

        string? initialUsername = null;
        string? initialPassword = null;

        if (!context.Users.Any())
        {
            // Compte administrateur technique de démarrage (PAS une donnée réglementaire officielle ou
            // fictive : uniquement un compte applicatif nécessaire pour accéder à l'application au tout
            // premier lancement). Le mot de passe généré est affiché une seule fois à l'écran de
            // connexion et doit être changé par l'utilisateur.
            initialUsername = "admin";
            // Section 18 de l'audit : plus aucun mot de passe fixe codé en dur. Généré aléatoirement via
            // RandomNumberGenerator (voir PasswordHasher.GenerateRandomPassword), affiché UNE SEULE fois à
            // l'écran "Premier démarrage" (jamais journalisé), et son utilisation force un changement de
            // mot de passe obligatoire à la prochaine connexion (MustChangePasswordOnNextLogin).
            initialPassword = PasswordHasher.GenerateRandomPassword();
            var (hash, salt) = PasswordHasher.HashNewPassword(initialPassword);

            context.Users.Add(new AppUser
            {
                Username = initialUsername,
                DisplayName = "Administrateur Initial",
                PasswordHash = hash,
                PasswordSalt = salt,
                Role = UserRole.Administrateur,
                IsActive = true,
                MustChangePasswordOnNextLogin = true
            });

            context.SaveChanges();
        }

        return (created, initialUsername, initialPassword);
    }

    /// <summary>
    /// Palliatif léger et NON destructif à l'absence de véritables migrations EF Core (Section 19 de
    /// l'audit) : <see cref="DbContext.Database"/>.<c>EnsureCreated()</c> ne modifie jamais le schéma d'une
    /// base déjà existante, donc une colonne ajoutée à une entité par une nouvelle version de
    /// l'application (ex: <c>ExchangeRateRecord.QuoteCurrencyCode</c>, <c>ImportOperation.AuthorizationCurrencyCode</c>,
    /// <c>AppUser.MustChangePasswordOnNextLogin</c>) resterait invisible pour une base SQLite créée avec une
    /// version antérieure du modèle, provoquant une erreur SQL au premier accès.
    /// Cette méthode ajoute UNIQUEMENT les colonnes manquantes (ALTER TABLE ... ADD COLUMN), sans jamais
    /// modifier ou supprimer une colonne/table/ligne existante : aucune donnée déjà saisie n'est perdue.
    /// Ce n'est délibérément PAS un remplacement des migrations EF Core standard : dès que le SDK .NET est
    /// disponible sur le poste de développement, la démarche recommandée (documentée dans le README) est
    /// d'exécuter "dotnet ef migrations add" pour obtenir un historique de migrations versionné classique —
    /// ce palliatif reste nécessaire uniquement pour ne jamais casser une base déjà déployée en attendant.
    /// Ne s'applique qu'au fournisseur SQLite (fournisseur par défaut de l'application).
    /// </summary>
    public static void ApplyLightweightSchemaUpgrades(ImportCostDbContext context)
    {
        if (!context.Database.IsSqlite())
            return;

        DbConnection connection = context.Database.GetDbConnection();
        bool wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed) connection.Open();

        try
        {
            AddColumnIfMissing(connection, "ExchangeRates", "QuoteCurrencyCode", "TEXT NOT NULL DEFAULT 'DZD'");
            AddColumnIfMissing(connection, "ImportOperations", "AuthorizationCurrencyCode", "TEXT NOT NULL DEFAULT 'USD'");
            // Correction 2026-10-02 (demande utilisateur — "ne jamais saisir directement EUR -> USD") :
            // remplace l'ancienne colonne ManualAuthorizationExchangeRateOverride (taux croisé direct), qui
            // n'est plus utilisée par le code (reste en base sur une installation existante, inoffensive,
            // jamais relue). La nouvelle colonne porte un taux réglementaire manuel PAR DEVISE (symétrique à
            // ManualExchangeRateOverride), jamais un taux croisé.
            AddColumnIfMissing(connection, "ImportOperations", "ManualAuthorizationCurrencyRateToDzd", "TEXT NULL");
            AddColumnIfMissing(connection, "Users", "MustChangePasswordOnNextLogin", "INTEGER NOT NULL DEFAULT 0");
            // Revue du 2026-10-01 (point 5 — Droits et taxes par code SH) : RegulatoryRule.IsApplicable.
            // DEFAULT 1 (true) préserve le comportement de toutes les règles déjà publiées (applicables).
            AddColumnIfMissing(connection, "RegulatoryRules", "IsApplicable", "INTEGER NOT NULL DEFAULT 1");

            // Revue du 2026-10-02 (cas de référence D10 réel) : nouvelles colonnes NULLABLE — une base déjà
            // déployée reste parfaitement utilisable sans elles (comportement par défaut inchangé tant que
            // l'utilisateur ne les renseigne pas explicitement).
            AddColumnIfMissing(connection, "ImportOperations", "PurchaseCountryIso2", "TEXT NULL");
            AddColumnIfMissing(connection, "ImportOperations", "ManualPrctRatePercent", "TEXT NULL");
            AddColumnIfMissing(connection, "ImportOperations", "UserConfirmedManualPrct", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(connection, "ImportOperations", "ManualTcsRatePercent", "TEXT NULL");
            AddColumnIfMissing(connection, "ImportOperations", "UserConfirmedManualTcs", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(connection, "ImportLines", "ManualVatRatePercent", "TEXT NULL");
            AddColumnIfMissing(connection, "ImportLines", "UserConfirmedManualVatRate", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(connection, "ImportLines", "VatExemptionReasonFr", "TEXT NULL");

            // Revue du 2026-10-02 (CORRECTION URGENTE — "ne plus bloquer le calcul faute de RegulatoryRule") :
            // TAUX DE TAXES PAR DÉFAUT de l'importation. DEFAULT NOT NULL avec les mêmes valeurs initiales
            // que le modèle métier (voir ImportOperation) afin que les lignes DÉJÀ existantes dans une base
            // déployée conservent exactement le même comportement de calcul qu'avant cette correction tant
            // que l'utilisateur ne modifie pas explicitement ces valeurs.
            AddColumnIfMissing(connection, "ImportOperations", "UseDefaultRatesWhenRuleMissing", "INTEGER NOT NULL DEFAULT 1");
            AddColumnIfMissing(connection, "ImportOperations", "DefaultDdRatePercent", "TEXT NOT NULL DEFAULT '0'");
            AddColumnIfMissing(connection, "ImportOperations", "DefaultCsRatePercent", "TEXT NOT NULL DEFAULT '3.0'");
            AddColumnIfMissing(connection, "ImportOperations", "DefaultPrctRatePercent", "TEXT NOT NULL DEFAULT '2.0'");
            AddColumnIfMissing(connection, "ImportOperations", "DefaultTvaRatePercent", "TEXT NOT NULL DEFAULT '19.0'");
            AddColumnIfMissing(connection, "ImportOperations", "DefaultTcsRatePercent", "TEXT NOT NULL DEFAULT '0'");
            AddColumnIfMissing(connection, "ImportOperations", "DefaultRpsAmountDzd", "TEXT NOT NULL DEFAULT '0'");

            // Revue du 2026-10-02 (REFONTE INTERFACE, Section 15) : prix de vente unitaire en DA, colonne
            // NULLABLE — une base déjà déployée reste parfaitement utilisable sans elle (aucun bénéfice
            // affiché tant que l'utilisateur n'a rien saisi, jamais une valeur inventée).
            AddColumnIfMissing(connection, "ImportLines", "SalePriceDzd", "TEXT NULL");

            // Correction 2026-10-02 (Étape 2 — "Persistance des calculs après fermeture de CIMP") : empreinte
            // des données d'entrée de chaque instantané de calcul sauvegardé (CalculationSnapshotRow),
            // DEFAULT '' pour les lignes déjà existantes dans une base déployée (sera simplement considérée
            // "non comparable" -> traitée comme potentiellement obsolète au prochain chargement, jamais
            // une erreur).
            AddColumnIfMissing(connection, "CalculationSnapshots", "InputHash", "TEXT NOT NULL DEFAULT ''");

            // Revue du 2026-10-05 ("DD Excel prioritaire par défaut") : nouvelle case "Forcer DD IA" par
            // ligne (ImportLine.ForceAiDutyRate) permettant à l'utilisateur de remplacer explicitement le
            // Droit de Douane Excel (prioritaire par défaut depuis cette revue) par la proposition DD IA.
            // DEFAULT 0 pour les lignes déjà existantes dans une base déployée : comportement inchangé pour
            // tout import déjà saisi (le DD Excel reste utilisé, comme si la case n'avait jamais été cochée).
            AddColumnIfMissing(connection, "ImportLines", "ForceAiDutyRate", "INTEGER NOT NULL DEFAULT 0");

            // Tâche #21, point 5 (2026-10-06 — "Retirer la TCS de l'écran V1, exposer la CS comme taxe de
            // solidarité utilisateur") : nouvelles colonnes NULLABLE/DEFAULT 0 — une base déjà déployée reste
            // parfaitement utilisable sans elles (aucune confirmation manuelle CS tant que l'utilisateur ne
            // l'a pas explicitement saisie, comportement inchangé : règle officielle puis taux CS par défaut
            // de l'importation, exactement comme avant cette tâche). Les anciennes colonnes
            // ManualTcsRatePercent/UserConfirmedManualTcs ci-dessus sont CONSERVÉES TELLES QUELLES (aucune
            // suppression, aucun renommage) pour ne jamais corrompre/perdre une confirmation manuelle TCS
            // déjà enregistrée sur une installation existante — elles ne sont simplement plus exposées dans
            // l'écran V1 (voir ImportDetailView.xaml / ImportDetailViewModel).
            AddColumnIfMissing(connection, "ImportOperations", "ManualCsRatePercent", "TEXT NULL");
            AddColumnIfMissing(connection, "ImportOperations", "UserConfirmedManualCs", "INTEGER NOT NULL DEFAULT 0");
        }
        finally
        {
            if (wasClosed) connection.Close();
        }
    }

    private static bool ColumnExists(DbConnection connection, string table, string column)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info('{table}')";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            // PRAGMA table_info renvoie les colonnes (cid, name, type, notnull, dflt_value, pk) :
            // 'name' est à l'index 1.
            string existingColumnName = reader.GetString(1);
            if (string.Equals(existingColumnName, column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void AddColumnIfMissing(DbConnection connection, string table, string column, string columnDefinitionSql)
    {
        if (ColumnExists(connection, table, column))
            return;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {columnDefinitionSql}";
        cmd.ExecuteNonQuery();
    }
}
