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

/// <summary>
/// Abstraction de persistance du journal d'audit (Sections 34 & 39).
/// Implémentée par ImportCostAlgeria.Database.Repositories.EfAuditLogStore pour une persistance réelle
/// en base (SQLite/SQL Server). Une entrée d'audit n'est JAMAIS modifiable ni supprimable une fois écrite.
/// </summary>
public interface IAuditLogStore
{
    AuditLogEntry Append(AuditLogEntry entryWithoutSequence);
    IReadOnlyList<AuditLogEntry> GetForCompany(Guid? companyId);
    IReadOnlyList<AuditLogEntry> GetAll();
}

/// <summary>
/// Implémentation par défaut en mémoire (utilisée par les tests unitaires et comme repli
/// si aucune base de données n'est injectée).
/// </summary>
public sealed class InMemoryAuditLogStore : IAuditLogStore
{
    private readonly List<AuditLogEntry> _entries = new();
    private long _counter;

    public AuditLogEntry Append(AuditLogEntry entryWithoutSequence)
    {
        var entry = entryWithoutSequence with { SequenceNumber = ++_counter };
        _entries.Add(entry);
        return entry;
    }

    public IReadOnlyList<AuditLogEntry> GetForCompany(Guid? companyId) =>
        _entries.FindAll(e => e.CompanyId == companyId || e.CompanyId == null);

    public IReadOnlyList<AuditLogEntry> GetAll() => _entries.AsReadOnly();
}

public sealed class AuditTrailService
{
    private readonly IAuditLogStore _store;

    public AuditTrailService() : this(new InMemoryAuditLogStore())
    {
    }

    public AuditTrailService(IAuditLogStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public IReadOnlyList<AuditLogEntry> GetAuditLogsForCompany(Guid companyId) =>
        _store.GetForCompany(companyId);

    /// <summary>
    /// Point d'entrée générique d'écriture d'audit (Section 34) : couvre toutes les catégories exigées
    /// (création/modification/suppression, import, validation de mapping, changement de code SH,
    /// validation IA, changement de taux, publication de règle, calcul, export, changement de frais...).
    /// Une entrée d'audit est immuable dès sa création (Section 39).
    /// </summary>
    public AuditLogEntry RecordAction(
        Guid? companyId,
        Guid userId,
        string userDisplayName,
        string actionCategory,
        string actionName,
        string? oldValue = null,
        string? newValue = null,
        Guid? importOperationId = null,
        Guid? productId = null,
        string? regulatoryRuleCode = null,
        string? legalSourceReference = null,
        string? regulatoryVersionCode = null)
    {
        var entry = new AuditLogEntry(
            SequenceNumber: 0,
            TimestampUtc: DateTime.UtcNow,
            CompanyId: companyId,
            UserId: userId,
            UserDisplayName: userDisplayName,
            ActionCategory: actionCategory,
            ActionName: actionName,
            OldValue: oldValue,
            NewValue: newValue,
            ImportOperationId: importOperationId,
            ProductId: productId,
            RegulatoryRuleCode: regulatoryRuleCode,
            LegalSourceReference: legalSourceReference,
            RegulatoryVersionCode: regulatoryVersionCode);

        return _store.Append(entry);
    }

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
            SequenceNumber: 0,
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

        return _store.Append(entry);
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
            SequenceNumber: 0,
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

        return _store.Append(entry);
    }
}
