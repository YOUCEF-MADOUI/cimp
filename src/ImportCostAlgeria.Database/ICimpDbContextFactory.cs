using System;

namespace ImportCostAlgeria.Database;

/// <summary>
/// Fabrique de contextes EF Core à durée de vie courte (motif "Unit of Work par opération"),
/// recommandé pour les applications WPF afin d'éviter les problèmes de suivi de changements
/// (change tracking) sur un DbContext partagé trop longtemps.
/// </summary>
public interface ICimpDbContextFactory
{
    /// <summary>Contexte "toutes entreprises" (écrans transverses : liste des entreprises, utilisateurs,
    /// réglementation, taux de change, audit global). Le filtre multi-entreprise est désactivé.</summary>
    ImportCostDbContext CreateGlobal();

    /// <summary>Contexte filtré pour une entreprise donnée (Section 32 — isolation stricte multi-entreprise).</summary>
    ImportCostDbContext CreateForCompany(Guid companyId);
}

public sealed class CimpDbContextFactory : ICimpDbContextFactory
{
    private readonly DatabaseProviderKind _provider;
    private readonly string? _connectionString;

    public CimpDbContextFactory(DatabaseProviderKind provider = DatabaseProviderKind.Sqlite, string? connectionString = null)
    {
        _provider = provider;
        _connectionString = connectionString;
    }

    public ImportCostDbContext CreateGlobal() =>
        new(DbContextFactory.BuildOptions(_provider, _connectionString), null);

    public ImportCostDbContext CreateForCompany(Guid companyId) =>
        new(DbContextFactory.BuildOptions(_provider, _connectionString), companyId);
}
