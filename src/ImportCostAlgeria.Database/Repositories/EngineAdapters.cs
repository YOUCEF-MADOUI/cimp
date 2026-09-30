using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ImportCostAlgeria.Audit;
using ImportCostAlgeria.CalculationEngine;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.ExcelEngine;
using ImportCostAlgeria.RegulatoryEngine;

namespace ImportCostAlgeria.Database.Repositories;

/// <summary>
/// Adaptateur EF Core de <see cref="IRegulatoryRuleRepository"/> (Sections 18-21).
/// Aucune règle n'est jamais inventée ni codée en dur : uniquement lecture depuis la table
/// RegulatoryRules, alimentée exclusivement via l'écran "Réglementation" (publication administrateur).
/// </summary>
public sealed class EfRegulatoryRuleRepository : IRegulatoryRuleRepository
{
    private readonly ICimpDbContextFactory _factory;
    public EfRegulatoryRuleRepository(ICimpDbContextFactory factory) => _factory = factory;

    public IReadOnlyList<RegulatoryRule> GetCandidateRules(string hsCode10)
    {
        using var ctx = _factory.CreateGlobal();
        return ctx.RegulatoryRules.AsNoTracking()
            .ToList()
            .Where(r => RegulatoryRuleEngine.NormalizeHsCode(r.HsCode10) == hsCode10)
            .ToList();
    }
}

/// <summary>Adaptateur EF Core de <see cref="IExchangeRateProvider"/> (Sections 13 & 14).</summary>
public sealed class EfExchangeRateProvider : IExchangeRateProvider
{
    private readonly ICimpDbContextFactory _factory;
    public EfExchangeRateProvider(ICimpDbContextFactory factory) => _factory = factory;

    public ExchangeRateRecord? GetRegulatoryRate(string currencyCode, DateOnly referenceDate)
    {
        using var ctx = _factory.CreateGlobal();
        string normalized = currencyCode.Trim().ToUpperInvariant();
        return ctx.ExchangeRates.AsNoTracking()
            .Where(r => r.CurrencyCode == normalized
                        && r.ValidFrom <= referenceDate
                        && (r.ValidTo == null || r.ValidTo >= referenceDate))
            .OrderByDescending(r => r.ValidFrom)
            .FirstOrDefault();
    }
}

/// <summary>
/// Adaptateur EF Core de <see cref="IAuditLogStore"/> (Sections 34 & 39).
/// Aucune méthode de modification/suppression n'est exposée : une ligne d'audit persistée est
/// définitive.
/// </summary>
public sealed class EfAuditLogStore : IAuditLogStore
{
    private readonly ICimpDbContextFactory _factory;
    public EfAuditLogStore(ICimpDbContextFactory factory) => _factory = factory;

    public AuditLogEntry Append(AuditLogEntry entryWithoutSequence)
    {
        using var ctx = _factory.CreateGlobal();
        var row = new AuditLogRow
        {
            TimestampUtc = entryWithoutSequence.TimestampUtc,
            CompanyId = entryWithoutSequence.CompanyId,
            UserId = entryWithoutSequence.UserId,
            UserDisplayName = entryWithoutSequence.UserDisplayName,
            ActionCategory = entryWithoutSequence.ActionCategory,
            ActionName = entryWithoutSequence.ActionName,
            OldValue = entryWithoutSequence.OldValue,
            NewValue = entryWithoutSequence.NewValue,
            ImportOperationId = entryWithoutSequence.ImportOperationId,
            ProductId = entryWithoutSequence.ProductId,
            RegulatoryRuleCode = entryWithoutSequence.RegulatoryRuleCode,
            LegalSourceReference = entryWithoutSequence.LegalSourceReference,
            RegulatoryVersionCode = entryWithoutSequence.RegulatoryVersionCode
        };
        ctx.AuditLogRows.Add(row);
        ctx.SaveChanges();
        return entryWithoutSequence with { SequenceNumber = row.Id };
    }

    public IReadOnlyList<AuditLogEntry> GetForCompany(Guid? companyId)
    {
        using var ctx = _factory.CreateGlobal();
        return ctx.AuditLogRows.AsNoTracking()
            .Where(r => companyId == null || r.CompanyId == companyId || r.CompanyId == null)
            .OrderByDescending(r => r.TimestampUtc)
            .ToList()
            .Select(ToEntry)
            .ToList();
    }

    public IReadOnlyList<AuditLogEntry> GetAll()
    {
        using var ctx = _factory.CreateGlobal();
        return ctx.AuditLogRows.AsNoTracking()
            .OrderByDescending(r => r.TimestampUtc)
            .ToList()
            .Select(ToEntry)
            .ToList();
    }

    private static AuditLogEntry ToEntry(AuditLogRow r) => new(
        SequenceNumber: r.Id,
        TimestampUtc: r.TimestampUtc,
        CompanyId: r.CompanyId,
        UserId: r.UserId,
        UserDisplayName: r.UserDisplayName,
        ActionCategory: r.ActionCategory,
        ActionName: r.ActionName,
        OldValue: r.OldValue,
        NewValue: r.NewValue,
        ImportOperationId: r.ImportOperationId,
        ProductId: r.ProductId,
        RegulatoryRuleCode: r.RegulatoryRuleCode,
        LegalSourceReference: r.LegalSourceReference,
        RegulatoryVersionCode: r.RegulatoryVersionCode);
}

/// <summary>Adaptateur EF Core de <see cref="IMappingTemplateStore"/> (Sections 4, 5 & 31).</summary>
public sealed class EfMappingTemplateStore : IMappingTemplateStore
{
    private readonly ICimpDbContextFactory _factory;
    public EfMappingTemplateStore(ICimpDbContextFactory factory) => _factory = factory;

    public void Save(ExcelMappingTemplate template)
    {
        using var ctx = _factory.CreateGlobal();
        string json = JsonSerializer.Serialize(
            template.ColumnLetterToField.ToDictionary(kv => kv.Key, kv => kv.Value.ToString()));

        var existing = ctx.MappingTemplates.FirstOrDefault(t =>
            t.CompanyId == template.CompanyId &&
            (t.TemplateName == template.TemplateName || t.HeaderSignatureHash == template.HeaderSignatureHash));

        if (existing != null)
        {
            existing.TemplateName = template.TemplateName;
            existing.HeaderSignatureHash = template.HeaderSignatureHash;
            existing.ColumnMapJson = json;
        }
        else
        {
            ctx.MappingTemplates.Add(new MappingTemplateRow
            {
                Id = template.Id,
                CompanyId = template.CompanyId,
                TemplateName = template.TemplateName,
                HeaderSignatureHash = template.HeaderSignatureHash,
                ColumnMapJson = json
            });
        }

        ctx.SaveChanges();
    }

    public IReadOnlyList<ExcelMappingTemplate> GetForCompany(Guid companyId)
    {
        using var ctx = _factory.CreateGlobal();
        return ctx.MappingTemplates.AsNoTracking()
            .Where(t => t.CompanyId == companyId)
            .ToList()
            .Select(row => new ExcelMappingTemplate
            {
                Id = row.Id,
                CompanyId = row.CompanyId,
                TemplateName = row.TemplateName,
                HeaderSignatureHash = row.HeaderSignatureHash,
                ColumnLetterToField = (JsonSerializer.Deserialize<Dictionary<string, string>>(row.ColumnMapJson) ?? new())
                    .ToDictionary(kv => kv.Key, kv => Enum.Parse<CanonicalExcelField>(kv.Value), StringComparer.OrdinalIgnoreCase)
            })
            .ToList();
    }
}
