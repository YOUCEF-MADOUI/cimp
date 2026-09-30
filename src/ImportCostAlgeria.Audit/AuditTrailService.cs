using System;
using System.Collections.Generic;

namespace ImportCostAlgeria.Audit;

/// <summary>
/// Entrée immuable du journal d'audit (Sections 34 & 39).
/// Aucune entrée d'audit ne peut être modifiée ni supprimée.
/// </summary>
public sealed record AuditLogEntry(
    long SequenceNumber,
    DateTime TimestampUtc,
    Guid? CompanyId,
    Guid UserId,
    string UserDisplayName,
    string ActionCategory,
    string ActionName,
    string? OldValue,
    string? NewValue,
    Guid? ImportOperationId,
    Guid? ProductId,
    string? RegulatoryRuleCode,
    string? LegalSourceReference,
    string? RegulatoryVersionCode);

public sealed class AuditTrailService
{
    private readonly List<AuditLogEntry> _entries = new();
    private long _counter;

    public IReadOnlyList<AuditLogEntry> GetAuditLogsForCompany(Guid companyId) =>
        _entries.FindAll(e => e.CompanyId == companyId || e.CompanyId == null);

    public AuditLogEntry RecordRegulatoryRateUpdate(
        Guid adminUserId,
        string adminName,
        string ruleCode,
        decimal oldRatePercent,
        decimal newRatePercent,
        string legalSource,
        string versionCode)
    {
        var entry = new AuditLogEntry(
            SequenceNumber: ++_counter,
            TimestampUtc: DateTime.UtcNow,
            CompanyId: null,
            UserId: adminUserId,
            UserDisplayName: adminName,
            ActionCategory: "REGULATORY_RULE",
            ActionName: "UPDATE_REGULATORY_DUTY_RATE",
            OldValue: $"{oldRatePercent:F2} %",
            NewValue: $"{newRatePercent:F2} %",
            ImportOperationId: null,
            ProductId: null,
            RegulatoryRuleCode: ruleCode,
            LegalSourceReference: legalSource,
            RegulatoryVersionCode: versionCode);

        _entries.Add(entry);
        return entry;
    }

    public AuditLogEntry RecordManualExchangeRateOverride(
        Guid companyId,
        Guid userId,
        string userName,
        Guid importOperationId,
        string currencyCode,
        decimal officialRate,
        decimal manualRate)
    {
        var entry = new AuditLogEntry(
            SequenceNumber: ++_counter,
            TimestampUtc: DateTime.UtcNow,
            CompanyId: companyId,
            UserId: userId,
            UserDisplayName: userName,
            ActionCategory: "EXCHANGE_RATE",
            ActionName: "MANUAL_EXCHANGE_RATE_OVERRIDE",
            OldValue: $"Taux réglementaire ({currencyCode}) : {officialRate:F4}",
            NewValue: $"Taux manuel utilisé ({currencyCode}) : {manualRate:F4}",
            ImportOperationId: importOperationId,
            ProductId: null,
            RegulatoryRuleCode: null,
            LegalSourceReference: "Portail officiel ALCES / Dérogation manuelle utilisateur",
            RegulatoryVersionCode: null);

        _entries.Add(entry);
        return entry;
    }
}
