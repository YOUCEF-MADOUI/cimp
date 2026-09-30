using System;
using Microsoft.EntityFrameworkCore;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.RegulatoryEngine;

namespace ImportCostAlgeria.Database;

/// <summary>
/// Contexte Entity Framework Core 8 pour SQL Server (Sections 2, 18, 32).
/// Applique un filtre global d'isolation multi-entreprise (Tenant CompanyId)
/// afin que les données d'une entreprise ne soient jamais visibles par une autre.
/// </summary>
public sealed class ImportCostDbContext : DbContext
{
    private readonly Guid _currentCompanyId;

    public ImportCostDbContext(DbContextOptions<ImportCostDbContext> options, Guid currentCompanyId)
        : base(options)
    {
        _currentCompanyId = currentCompanyId;
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ImportOperation> ImportOperations => Set<ImportOperation>();
    public DbSet<ImportLine> ImportLines => Set<ImportLine>();
    public DbSet<ImportFee> ImportFees => Set<ImportFee>();
    public DbSet<ExchangeRateRecord> ExchangeRates => Set<ExchangeRateRecord>();
    public DbSet<RegulatoryRule> RegulatoryRules => Set<RegulatoryRule>();
    public DbSet<LegalSource> LegalSources => Set<LegalSource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Company>(b =>
        {
            b.ToTable("Companies", "core");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Code).IsUnique();
        });

        // Isolation stricte multi-entreprise (Section 32) :
        modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("Products", "core");
            b.HasKey(x => x.Id);
            b.HasQueryFilter(p => p.CompanyId == _currentCompanyId);
            b.HasIndex(p => new { p.CompanyId, p.Reference }).IsUnique();
        });

        modelBuilder.Entity<ImportOperation>(b =>
        {
            b.ToTable("ImportOperations", "core");
            b.HasKey(x => x.Id);
            b.HasQueryFilter(op => op.CompanyId == _currentCompanyId);
            b.HasIndex(op => new { op.CompanyId, op.ImportNumber }).IsUnique();
            b.HasMany(op => op.Lines).WithOne().HasForeignKey("ImportOperationId").OnDelete(DeleteBehavior.Cascade);
            b.HasMany(op => op.Fees).WithOne().HasForeignKey("ImportOperationId").OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExchangeRateRecord>(b =>
        {
            b.ToTable("ExchangeRates", "core");
            b.HasKey(x => x.Id);
            b.Property(x => x.RateToDzd).HasPrecision(18, 6);
        });

        modelBuilder.Entity<RegulatoryRule>(b =>
        {
            b.ToTable("RegulatoryRules", "reg");
            b.HasKey(x => x.Id);
            b.Property(x => x.RatePercent).HasPrecision(9, 4);
        });
    }
}
