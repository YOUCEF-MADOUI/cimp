using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.RegulatoryEngine;

namespace ImportCostAlgeria.Database;

/// <summary>
/// Journal d'audit persisté (Sections 34 & 39). Table EF-friendly indépendante du record immuable
/// <see cref="ImportCostAlgeria.Audit.AuditLogEntry"/> exposé par la couche métier ; la correspondance
/// est assurée par ImportCostAlgeria.Database.Repositories.EfAuditLogStore.
/// Une ligne d'audit n'est jamais modifiée ni supprimée par l'application (aucune méthode Update/Delete
/// n'est exposée sur le DbSet correspondant dans les dépôts).
/// </summary>
public sealed class AuditLogRow
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid UserId { get; set; }
    public string UserDisplayName { get; set; } = string.Empty;
    public string ActionCategory { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public Guid? ImportOperationId { get; set; }
    public Guid? ProductId { get; set; }
    public string? RegulatoryRuleCode { get; set; }
    public string? LegalSourceReference { get; set; }
    public string? RegulatoryVersionCode { get; set; }
}

/// <summary>
/// Modèle de mapping Excel sauvegardé et réutilisable par fournisseur (Sections 4, 5 & 31), persisté.
/// </summary>
public sealed class MappingTemplateRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string HeaderSignatureHash { get; set; } = string.Empty;
    /// <summary>Sérialisation JSON de la correspondance {LettreColonne -> ChampCanonique}.</summary>
    public string ColumnMapJson { get; set; } = "{}";
}

/// <summary>
/// Instantané immuable d'un calcul exécuté (Section 3, 19, 34) pour l'historisation (écran "Historique
/// des calculs"). Le résultat complet (ImportCalculationSummary) est stocké sérialisé en JSON afin de
/// ne jamais perdre de traçabilité, y compris après une évolution ultérieure de la réglementation.
/// </summary>
public sealed class CalculationSnapshotRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid ImportOperationId { get; set; }
    public string ImportNumber { get; set; } = string.Empty;
    public DateTime ExecutedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid ExecutedByUserId { get; set; }
    public string RegulatoryVersionCode { get; set; } = string.Empty;
    public decimal TotalRealCostOfGoodsDzd { get; set; }
    public decimal TotalCustomsDutyDzd { get; set; }
    public decimal TotalImportVatDzd { get; set; }
    public int AnomalyCount { get; set; }
    public bool HasBlockingAnomalies { get; set; }
    public string CalculationResultJson { get; set; } = "{}";
    /// <summary>
    /// Correction 2026-10-02 (Étape 2 — "Persistance des calculs après fermeture de CIMP") : empreinte des
    /// données d'ENTRÉE utilisées pour produire ce résultat (<see cref="CalculationInputHasher"/>), afin de
    /// détecter au rechargement si l'importation a été modifiée depuis ce calcul (CalculationStatus =
    /// Obsolète) sans avoir à tout recalculer pour le savoir.
    /// </summary>
    public string InputHash { get; set; } = string.Empty;
}

/// <summary>
/// Contexte Entity Framework Core 8 (Sections 2, 18, 32).
/// Fournisseur par défaut : SQLite (fichier local %LOCALAPPDATA%\CIMP\cimp.db), voir DbContextFactory ;
/// SQL Server reste disponible via la chaîne de connexion pour les entreprises qui en disposent déjà.
/// Applique un filtre global d'isolation multi-entreprise (Tenant CompanyId) afin que les données d'une
/// entreprise ne soient jamais visibles par une autre (Section 32).
/// </summary>
public sealed class ImportCostDbContext : DbContext
{
    private readonly Guid? _currentCompanyId;

    /// <summary>
    /// Constructeur "toutes entreprises" utilisé pour les écrans transverses (liste des entreprises,
    /// administration, audit global). Le filtre multi-entreprise est alors désactivé.
    /// </summary>
    public ImportCostDbContext(DbContextOptions<ImportCostDbContext> options)
        : this(options, null)
    {
    }

    public ImportCostDbContext(DbContextOptions<ImportCostDbContext> options, Guid? currentCompanyId)
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
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AuditLogRow> AuditLogRows => Set<AuditLogRow>();
    public DbSet<MappingTemplateRow> MappingTemplates => Set<MappingTemplateRow>();
    public DbSet<CalculationSnapshotRow> CalculationSnapshots => Set<CalculationSnapshotRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Company>(b =>
        {
            b.ToTable("Companies");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.Code).IsRequired().HasMaxLength(64);
            b.Property(x => x.LegalName).IsRequired().HasMaxLength(256);
            b.Property(x => x.DefaultFunctionalCurrency).HasMaxLength(8);
        });

        // Isolation stricte multi-entreprise (Section 32) : filtre appliqué uniquement quand un contexte
        // "entreprise courante" est actif (écrans opérationnels) ; désactivé pour les écrans transverses.
        modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("Products");
            b.HasKey(x => x.Id);
            b.HasQueryFilter(p => _currentCompanyId == null || p.CompanyId == _currentCompanyId);
            b.HasIndex(p => new { p.CompanyId, p.Reference }).IsUnique();
            b.Property(x => x.HabitualDutyRatePercent).HasPrecision(9, 4);
            b.Property(x => x.HabitualVatRatePercent).HasPrecision(9, 4);
        });

        modelBuilder.Entity<ImportOperation>(b =>
        {
            b.ToTable("ImportOperations");
            b.HasKey(x => x.Id);
            b.HasQueryFilter(op => _currentCompanyId == null || op.CompanyId == _currentCompanyId);
            b.HasIndex(op => new { op.CompanyId, op.ImportNumber }).IsUnique();
            b.Property(x => x.ManualExchangeRateOverride).HasPrecision(18, 6);
            b.Property(x => x.AuthorizationCurrencyCode).IsRequired().HasMaxLength(8).HasDefaultValue("USD");
            b.Property(x => x.ManualAuthorizationCurrencyRateToDzd).HasPrecision(18, 6);
            b.Property(x => x.ManualPrctRatePercent).HasPrecision(9, 4);
            b.Property(x => x.ManualTcsRatePercent).HasPrecision(9, 4);
            // Revue du 2026-10-02 (correction urgente) : taux de taxes PAR DÉFAUT de l'importation.
            b.Property(x => x.DefaultDdRatePercent).HasPrecision(9, 4);
            b.Property(x => x.DefaultCsRatePercent).HasPrecision(9, 4);
            b.Property(x => x.DefaultPrctRatePercent).HasPrecision(9, 4);
            b.Property(x => x.DefaultTvaRatePercent).HasPrecision(9, 4);
            b.Property(x => x.DefaultTcsRatePercent).HasPrecision(9, 4);
            b.Property(x => x.DefaultRpsAmountDzd).HasPrecision(18, 4);
            b.HasMany(op => op.Lines).WithOne().HasForeignKey("ImportOperationId").OnDelete(DeleteBehavior.Cascade);
            b.HasMany(op => op.Fees).WithOne().HasForeignKey("ImportOperationId").OnDelete(DeleteBehavior.Cascade);
            b.Navigation(op => op.Lines).AutoInclude();
            b.Navigation(op => op.Fees).AutoInclude();
        });

        modelBuilder.Entity<ImportLine>(b =>
        {
            b.ToTable("ImportLines");
            b.HasKey(x => x.Id);
            b.Property(x => x.Quantity).HasPrecision(18, 4);
            b.Property(x => x.UnitPurchasePrice).HasPrecision(18, 4);
            b.Property(x => x.ExcelDutyRatePercent).HasPrecision(9, 4);
            b.Property(x => x.ManualVatRatePercent).HasPrecision(9, 4);
            b.Property(x => x.LineGrossWeightKg).HasPrecision(18, 4);
            b.Property(x => x.LineVolumeM3).HasPrecision(18, 4);
            // Revue du 2026-10-02 (REFONTE INTERFACE, Section 15) : prix de vente unitaire en DA, purement
            // commercial/informatif (jamais utilisé dans le calcul douanier/fiscal — voir Entities.cs).
            b.Property(x => x.SalePriceDzd).HasPrecision(18, 4);

            // EF Core ne mappe pas nativement un Dictionary<Guid, decimal> : sérialisation JSON dédiée
            // (Section 12 — allocations manuelles de frais par ligne), avec comparateur de valeur pour
            // que le suivi de changements EF détecte correctement les mises à jour.
            var dictionaryComparer = new ValueComparer<Dictionary<Guid, decimal>>(
                (a, c) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(c, (JsonSerializerOptions?)null),
                d => d.Count,
                d => new Dictionary<Guid, decimal>(d));

            b.Property(x => x.ManualFeeAllocationsDzd)
                .HasConversion(
                    d => JsonSerializer.Serialize(d, (JsonSerializerOptions?)null),
                    s => JsonSerializer.Deserialize<Dictionary<Guid, decimal>>(s, (JsonSerializerOptions?)null) ?? new Dictionary<Guid, decimal>())
                .Metadata.SetValueComparer(dictionaryComparer);
        });

        modelBuilder.Entity<ImportFee>(b =>
        {
            b.ToTable("ImportFees");
            b.HasKey(x => x.Id);
            b.Property(x => x.Amount).HasPrecision(18, 4);
        });

        modelBuilder.Entity<ExchangeRateRecord>(b =>
        {
            b.ToTable("ExchangeRates");
            b.HasKey(x => x.Id);
            b.Property(x => x.RateToDzd).HasPrecision(18, 6);
            b.Property(x => x.QuoteCurrencyCode).IsRequired().HasMaxLength(8).HasDefaultValue("DZD");
            // Section 6 du plan multi-devises : l'index doit inclure la devise de cotation car une même
            // devise de base (ex: EUR) peut désormais avoir un historique de taux vers plusieurs devises de
            // cotation distinctes (EUR->DZD réglementaire ET EUR->USD commercial).
            b.HasIndex(x => new { x.CurrencyCode, x.QuoteCurrencyCode, x.ValidFrom });
        });

        modelBuilder.Entity<RegulatoryRule>(b =>
        {
            b.ToTable("RegulatoryRules");
            b.HasKey(x => x.Id);
            b.Property(x => x.RatePercent).HasPrecision(9, 4);
            b.HasIndex(x => new { x.HsCode10, x.TaxCode, x.ValidFrom });

            // La source légale (Section 20) est intrinsèquement liée à sa règle : mappée en tant que
            // type "owned" (même table RegulatoryRules, colonnes préfixées LegalSource_*) plutôt qu'une
            // table séparée, ce qui garantit qu'une règle ne peut jamais exister sans sa source.
            b.OwnsOne(x => x.LegalSource, ls =>
            {
                // La propriété Id du type "owned" LegalSource n'est pas une clé distincte : elle partage
                // la clé de la règle propriétaire (convention EF Core pour les types owned non-collection).
                // Elle est explicitement ignorée en base pour éviter toute ambiguïté de clé au modelBuilding.
                ls.Ignore(l => l.Id);
                ls.Property(l => l.HierarchyLevel).HasColumnName("LegalSource_HierarchyLevel");
                ls.Property(l => l.OfficialTitle).HasColumnName("LegalSource_OfficialTitle");
                ls.Property(l => l.JoraReference).HasColumnName("LegalSource_JoraReference");
                ls.Property(l => l.ArticleReference).HasColumnName("LegalSource_ArticleReference");
                ls.Property(l => l.PublicationDate).HasColumnName("LegalSource_PublicationDate");
                ls.Property(l => l.EffectiveDate).HasColumnName("LegalSource_EffectiveDate");
            });
            b.Navigation(x => x.LegalSource).IsRequired();
        });

        modelBuilder.Entity<AppUser>(b =>
        {
            b.ToTable("Users");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Username).IsUnique();
            b.Property(x => x.Username).IsRequired().HasMaxLength(64);
        });

        modelBuilder.Entity<AuditLogRow>(b =>
        {
            b.ToTable("AuditLog");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.HasIndex(x => x.CompanyId);
            b.HasIndex(x => x.TimestampUtc);
        });

        modelBuilder.Entity<MappingTemplateRow>(b =>
        {
            b.ToTable("MappingTemplates");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.CompanyId, x.HeaderSignatureHash });
        });

        modelBuilder.Entity<CalculationSnapshotRow>(b =>
        {
            b.ToTable("CalculationSnapshots");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.CompanyId, x.ImportOperationId });
            b.Property(x => x.TotalRealCostOfGoodsDzd).HasPrecision(18, 2);
        });
    }
}
