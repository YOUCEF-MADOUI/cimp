using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ImportCostAlgeria.CalculationEngine;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Database.Security;
using ImportCostAlgeria.RegulatoryEngine;

namespace ImportCostAlgeria.Database.Repositories;

/// <summary>CRUD Entreprises (Section 5 — écran "Entreprises").</summary>
public sealed class CompanyRepository
{
    private readonly ICimpDbContextFactory _factory;
    public CompanyRepository(ICimpDbContextFactory factory) => _factory = factory;

    public List<Company> GetAll()
    {
        using var ctx = _factory.CreateGlobal();
        return ctx.Companies.AsNoTracking().OrderBy(c => c.LegalName).ToList();
    }

    public Company? GetById(Guid id)
    {
        using var ctx = _factory.CreateGlobal();
        return ctx.Companies.AsNoTracking().FirstOrDefault(c => c.Id == id);
    }

    public void Save(Company company)
    {
        company.Code = company.Code.Trim().ToUpperInvariant();
        using var ctx = _factory.CreateGlobal();
        bool exists = ctx.Companies.Any(c => c.Id == company.Id);
        if (exists)
            ctx.Companies.Update(company);
        else
            ctx.Companies.Add(company);
        ctx.SaveChanges();
    }
}

/// <summary>CRUD Produits / catalogue entreprise (Section 31).</summary>
public sealed class ProductRepository
{
    private readonly ICimpDbContextFactory _factory;
    public ProductRepository(ICimpDbContextFactory factory) => _factory = factory;

    public List<Product> GetForCompany(Guid companyId)
    {
        using var ctx = _factory.CreateForCompany(companyId);
        return ctx.Products.AsNoTracking().Where(p => p.CompanyId == companyId).OrderBy(p => p.Reference).ToList();
    }

    public void Save(Product product)
    {
        using var ctx = _factory.CreateForCompany(product.CompanyId);
        bool exists = ctx.Products.Any(p => p.Id == product.Id);
        if (exists)
            ctx.Products.Update(product);
        else
            ctx.Products.Add(product);
        ctx.SaveChanges();
    }
}

/// <summary>
/// CRUD Importations, y compris lignes et frais (Sections 5, 7 & 9).
/// Utilise une stratégie "remplacement des lignes/frais enfants" simple et robuste (ExecuteDelete +
/// réinsertion), adaptée aux volumes typiques d'une importation (quelques dizaines à quelques centaines
/// de lignes), afin d'éviter les pièges classiques de suivi de changement EF Core sur des DbContext à
/// courte durée de vie.
/// </summary>
public sealed class ImportOperationRepository
{
    private readonly ICimpDbContextFactory _factory;
    public ImportOperationRepository(ICimpDbContextFactory factory) => _factory = factory;

    public List<ImportOperation> GetForCompany(Guid companyId)
    {
        using var ctx = _factory.CreateForCompany(companyId);
        return ctx.ImportOperations.AsNoTracking()
            .Where(o => o.CompanyId == companyId)
            .OrderByDescending(o => o.ReferenceDate)
            .ToList();
    }

    public ImportOperation? GetById(Guid companyId, Guid importOperationId)
    {
        using var ctx = _factory.CreateForCompany(companyId);
        return ctx.ImportOperations.AsNoTracking().FirstOrDefault(o => o.Id == importOperationId);
    }

    public void SaveOperation(ImportOperation operation)
    {
        operation.MainCurrencyCode = operation.MainCurrencyCode.Trim().ToUpperInvariant();
        operation.ExportShippingCountryIso2 = operation.ExportShippingCountryIso2.Trim().ToUpperInvariant();
        foreach (var line in operation.Lines)
            line.CurrencyCode = line.CurrencyCode.Trim().ToUpperInvariant();
        foreach (var fee in operation.Fees)
            fee.CurrencyCode = fee.CurrencyCode.Trim().ToUpperInvariant();

        using var ctx = _factory.CreateForCompany(operation.CompanyId);
        var existing = ctx.ImportOperations.IgnoreAutoIncludes().FirstOrDefault(o => o.Id == operation.Id);

        if (existing == null)
        {
            ctx.ImportOperations.Add(operation);
        }
        else
        {
            ctx.Entry(existing).CurrentValues.SetValues(operation);
            ctx.ImportLines.Where(l => EF.Property<Guid>(l, "ImportOperationId") == operation.Id).ExecuteDelete();
            ctx.ImportFees.Where(f => EF.Property<Guid>(f, "ImportOperationId") == operation.Id).ExecuteDelete();

            foreach (var line in operation.Lines)
            {
                ctx.ImportLines.Add(line);
                ctx.Entry(line).Property("ImportOperationId").CurrentValue = operation.Id;
            }
            foreach (var fee in operation.Fees)
            {
                ctx.ImportFees.Add(fee);
                ctx.Entry(fee).Property("ImportOperationId").CurrentValue = operation.Id;
            }
        }

        ctx.SaveChanges();
    }

    public void Delete(Guid companyId, Guid importOperationId)
    {
        using var ctx = _factory.CreateForCompany(companyId);
        var existing = ctx.ImportOperations.FirstOrDefault(o => o.Id == importOperationId);
        if (existing != null)
        {
            ctx.ImportOperations.Remove(existing); // Cascade -> supprime aussi Lines/Fees (Section 32).
            ctx.SaveChanges();
        }
    }
}

/// <summary>CRUD Utilisateurs / Rôles (Section 22 & 32).</summary>
public sealed class UserRepository
{
    private readonly ICimpDbContextFactory _factory;
    public UserRepository(ICimpDbContextFactory factory) => _factory = factory;

    public List<AppUser> GetAll()
    {
        using var ctx = _factory.CreateGlobal();
        return ctx.Users.AsNoTracking().OrderBy(u => u.Username).ToList();
    }

    public AppUser? FindByUsername(string username)
    {
        using var ctx = _factory.CreateGlobal();
        return ctx.Users.AsNoTracking().FirstOrDefault(u => u.Username.ToLower() == username.Trim().ToLower());
    }

    public AppUser CreateUser(string username, string displayName, string plainTextPassword, UserRole role)
    {
        using var ctx = _factory.CreateGlobal();
        if (ctx.Users.Any(u => u.Username.ToLower() == username.Trim().ToLower()))
            throw new InvalidOperationException($"Le nom d'utilisateur '{username}' existe déjà.");

        var (hash, salt) = PasswordHasher.HashNewPassword(plainTextPassword);
        var user = new AppUser
        {
            Username = username.Trim(),
            DisplayName = displayName.Trim(),
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = role,
            IsActive = true
        };
        ctx.Users.Add(user);
        ctx.SaveChanges();
        return user;
    }

    public void SetActive(Guid userId, bool isActive)
    {
        using var ctx = _factory.CreateGlobal();
        var user = ctx.Users.FirstOrDefault(u => u.Id == userId);
        if (user == null) return;
        user.IsActive = isActive;
        ctx.SaveChanges();
    }

    public AppUser? TryAuthenticate(string username, string plainTextPassword)
    {
        var user = FindByUsername(username);
        if (user == null || !user.IsActive) return null;
        return PasswordHasher.Verify(plainTextPassword, user.PasswordHash, user.PasswordSalt) ? user : null;
    }

    /// <summary>
    /// Section 18 (sécurité) : changement de mot de passe explicite par l'utilisateur lui-même (notamment
    /// obligatoire pour le compte Administrateur initial, voir <see cref="AppUser.MustChangePasswordOnNextLogin"/>).
    /// Le nouveau mot de passe n'est jamais journalisé (aucun appel d'audit ne transporte sa valeur).
    /// </summary>
    public void ChangePassword(Guid userId, string newPlainTextPassword)
    {
        if (string.IsNullOrWhiteSpace(newPlainTextPassword) || newPlainTextPassword.Length < 8)
            throw new InvalidOperationException("Le nouveau mot de passe doit contenir au moins 8 caractères.");

        using var ctx = _factory.CreateGlobal();
        var user = ctx.Users.FirstOrDefault(u => u.Id == userId)
            ?? throw new InvalidOperationException("Utilisateur introuvable.");

        var (hash, salt) = PasswordHasher.HashNewPassword(newPlainTextPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.MustChangePasswordOnNextLogin = false;
        ctx.SaveChanges();
    }
}

/// <summary>
/// Publication versionnée des règles réglementaires (Sections 18-21 & 39).
/// Ne modifie JAMAIS une règle existante en place (taux, base de calcul, source légale...) :
/// ferme la période de validité de l'ancienne règle (ValidTo = nouvelle_date - 1 jour) et insère une
/// toute nouvelle ligne. Réservé au rôle Administrateur.
/// </summary>
public sealed class RegulatoryRuleAdminRepository
{
    private readonly ICimpDbContextFactory _factory;
    public RegulatoryRuleAdminRepository(ICimpDbContextFactory factory) => _factory = factory;

    public List<RegulatoryRule> GetAll()
    {
        using var ctx = _factory.CreateGlobal();
        return ctx.RegulatoryRules.AsNoTracking().OrderByDescending(r => r.ValidFrom).ToList();
    }

    public RegulatoryRule PublishNewVersion(
        RegulatoryRule newRule,
        Guid? ruleIdToSupersede,
        Guid adminUserId,
        UserRole executorRole)
    {
        if (executorRole != UserRole.Administrateur)
            throw new InvalidOperationException("Sécurité réglementaire (Section 21 & 39) : Seul un Administrateur peut publier une règle officielle.");

        if (!newRule.LegalSource.IsOfficialBinding)
            throw new InvalidOperationException("Hiérarchie des sources (Section 20) : Une source secondaire (Niveau 6) ne peut jamais être publiée comme règle officielle contraignante.");

        using var ctx = _factory.CreateGlobal();

        if (ruleIdToSupersede.HasValue)
        {
            var old = ctx.RegulatoryRules.FirstOrDefault(r => r.Id == ruleIdToSupersede.Value);
            if (old != null)
            {
                if (newRule.ValidFrom <= old.ValidFrom)
                    throw new InvalidOperationException("Incohérence de versionnage : la date d'effet de la nouvelle règle doit être postérieure à celle de la règle précédente.");

                old.ValidTo = newRule.ValidFrom.AddDays(-1);
                old.Status = RegulatoryRuleStatus.Archived;
            }
        }

        newRule.Status = RegulatoryRuleStatus.PublishedNewVersion;
        newRule.ValidatedByAdminUserId = adminUserId;
        newRule.ValidatedAtUtc = DateTime.UtcNow;

        ctx.RegulatoryRules.Add(newRule);
        ctx.SaveChanges();
        return newRule;
    }
}

/// <summary>
/// Publication versionnée des taux de change (Sections 13 & 14 & 39).
/// Même principe de non-écrasement que les règles réglementaires : ferme l'ancien taux, insère le nouveau.
/// </summary>
public sealed class ExchangeRateAdminRepository
{
    private readonly ICimpDbContextFactory _factory;
    public ExchangeRateAdminRepository(ICimpDbContextFactory factory) => _factory = factory;

    public List<ExchangeRateRecord> GetAll()
    {
        using var ctx = _factory.CreateGlobal();
        return ctx.ExchangeRates.AsNoTracking().OrderByDescending(r => r.ValidFrom).ToList();
    }

    public ExchangeRateRecord PublishNewRate(ExchangeRateRecord newRate, Guid executingUserId, UserRole executorRole)
    {
        if (executorRole != UserRole.Administrateur)
            throw new InvalidOperationException("Seul un Administrateur peut publier un nouveau taux de change officiel.");

        using var ctx = _factory.CreateGlobal();
        string ccy = newRate.CurrencyCode.Trim().ToUpperInvariant();
        // Section 6 du plan multi-devises : une même devise de base (ex: EUR) peut avoir plusieurs
        // historiques indépendants selon la devise de cotation (EUR->DZD réglementaire, EUR->USD
        // commercial). Clôturer uniquement la période ouverte de LA MÊME paire de devises, jamais une
        // autre paire — sinon publier un nouveau taux EUR->USD clôturerait par erreur un taux EUR->DZD.
        string quoteCcy = string.IsNullOrWhiteSpace(newRate.QuoteCurrencyCode) ? "DZD" : newRate.QuoteCurrencyCode.Trim().ToUpperInvariant();

        var currentOpenEnded = ctx.ExchangeRates
            .Where(r => r.CurrencyCode == ccy && r.QuoteCurrencyCode == quoteCcy && r.ValidTo == null)
            .OrderByDescending(r => r.ValidFrom)
            .FirstOrDefault();

        if (currentOpenEnded != null)
        {
            if (newRate.ValidFrom <= currentOpenEnded.ValidFrom)
                throw new InvalidOperationException("La date d'effet du nouveau taux doit être postérieure au taux actuellement en vigueur.");
            currentOpenEnded.ValidTo = newRate.ValidFrom.AddDays(-1);
        }

        ctx.ExchangeRates.Add(newRate);
        ctx.SaveChanges();
        return newRate;
    }
}

/// <summary>Historisation immuable des calculs exécutés (Section 3, 19 & 34).</summary>
public sealed class CalculationSnapshotRepository
{
    private readonly ICimpDbContextFactory _factory;
    public CalculationSnapshotRepository(ICimpDbContextFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public void SaveSnapshot(Guid companyId, Guid userId, string regulatoryVersionCode, ImportCalculationSummary summary, string inputHash = "")
    {
        using var ctx = _factory.CreateGlobal();
        ctx.CalculationSnapshots.Add(new CalculationSnapshotRow
        {
            CompanyId = companyId,
            ImportOperationId = summary.ImportOperationId,
            ImportNumber = summary.ImportNumber,
            ExecutedByUserId = userId,
            RegulatoryVersionCode = regulatoryVersionCode,
            TotalRealCostOfGoodsDzd = summary.TotalRealCostOfGoodsDzd,
            TotalCustomsDutyDzd = summary.TotalCustomsDutyDzd,
            TotalImportVatDzd = summary.TotalImportVatDzd,
            AnomalyCount = summary.Anomalies.Count,
            HasBlockingAnomalies = summary.HasBlockingAnomalies,
            CalculationResultJson = JsonSerializer.Serialize(summary, JsonOptions),
            InputHash = inputHash
        });
        ctx.SaveChanges();
    }

    public List<CalculationSnapshotRow> GetHistory(Guid companyId, Guid importOperationId)
    {
        using var ctx = _factory.CreateGlobal();
        return ctx.CalculationSnapshots.AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.ImportOperationId == importOperationId)
            .OrderByDescending(s => s.ExecutedAtUtc)
            .ToList();
    }

    /// <summary>
    /// Correction 2026-10-02 (Étape 2 — "Persistance des calculs après fermeture de CIMP") : dernier
    /// instantané de calcul sauvegardé pour cette importation (ou null si elle n'a jamais été calculée) —
    /// utilisé au rechargement de l'écran Importation pour réafficher immédiatement les résultats déjà
    /// calculés sans forcer un nouveau calcul.
    /// </summary>
    public CalculationSnapshotRow? GetLatest(Guid companyId, Guid importOperationId)
    {
        using var ctx = _factory.CreateGlobal();
        return ctx.CalculationSnapshots.AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.ImportOperationId == importOperationId)
            .OrderByDescending(s => s.ExecutedAtUtc)
            .FirstOrDefault();
    }
}

/// <summary>Agrégation des indicateurs du Tableau de Bord (Section 38), à partir de l'historique réel des calculs.</summary>
public sealed class DashboardRepository
{
    private readonly ICimpDbContextFactory _factory;
    public DashboardRepository(ICimpDbContextFactory factory) => _factory = factory;

    public sealed record DashboardData(
        int NombreImportations,
        int NombreDeProduits,
        decimal CoutTotalDzd,
        decimal TotalDroitsDeDouaneDzd,
        int NombreDAnomaliesBloquantes,
        List<(string ImportNumber, DateOnly ReferenceDate, decimal CoutTotalDzd, bool Bloquee)> DernieresImportations);

    public DashboardData BuildFor(Guid companyId)
    {
        using var ctx = _factory.CreateForCompany(companyId);
        int productCount = ctx.Products.Count(p => p.CompanyId == companyId);

        using var globalCtx = _factory.CreateGlobal();
        var snapshots = globalCtx.CalculationSnapshots.AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .OrderByDescending(s => s.ExecutedAtUtc)
            .ToList();

        // Dernier instantané par importation (évite de compter plusieurs fois un recalcul).
        var latestPerImport = snapshots
            .GroupBy(s => s.ImportOperationId)
            .Select(g => g.OrderByDescending(x => x.ExecutedAtUtc).First())
            .ToList();

        return new DashboardData(
            NombreImportations: latestPerImport.Count,
            NombreDeProduits: productCount,
            CoutTotalDzd: latestPerImport.Sum(s => s.TotalRealCostOfGoodsDzd),
            TotalDroitsDeDouaneDzd: latestPerImport.Sum(s => s.TotalCustomsDutyDzd),
            NombreDAnomaliesBloquantes: latestPerImport.Count(s => s.HasBlockingAnomalies),
            DernieresImportations: latestPerImport
                .OrderByDescending(s => s.ExecutedAtUtc)
                .Take(10)
                .Select(s => (s.ImportNumber, DateOnly.FromDateTime(s.ExecutedAtUtc), s.TotalRealCostOfGoodsDzd, s.HasBlockingAnomalies))
                .ToList());
    }
}
