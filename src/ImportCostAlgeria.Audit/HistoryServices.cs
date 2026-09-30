using System;
using System.Collections.Generic;
using System.Linq;
using ImportCostAlgeria.CalculationEngine;

namespace ImportCostAlgeria.Audit;

public sealed record CalculationHistorySnapshot(
    Guid SnapshotId,
    Guid CompanyId,
    Guid ImportOperationId,
    string ImportNumber,
    DateTime ExecutedAtUtc,
    Guid ExecutedByUserId,
    string RegulatoryVersionCode,
    ImportCalculationSummary CalculationResult);

/// <summary>
/// Gestionnaire de l'historique immuable des calculs, de l'historique réglementaire et des actions utilisateurs
/// (Sections 3, 19, 34, 39 — V1.1).
/// </summary>
public sealed class CalculationHistoryService
{
    private readonly List<CalculationHistorySnapshot> _snapshots = new();

    public CalculationHistorySnapshot SaveCalculationSnapshot(
        Guid companyId,
        Guid userId,
        string regulatoryVersionCode,
        ImportCalculationSummary summary)
    {
        var snap = new CalculationHistorySnapshot(
            SnapshotId: Guid.NewGuid(),
            CompanyId: companyId,
            ImportOperationId: summary.ImportOperationId,
            ImportNumber: summary.ImportNumber,
            ExecutedAtUtc: DateTime.UtcNow,
            ExecutedByUserId: userId,
            RegulatoryVersionCode: regulatoryVersionCode,
            CalculationResult: summary);

        _snapshots.Add(snap);
        return snap;
    }

    public IReadOnlyList<CalculationHistorySnapshot> GetHistoryForImport(Guid companyId, Guid importOperationId) =>
        _snapshots
            .Where(s => s.CompanyId == companyId && s.ImportOperationId == importOperationId)
            .OrderByDescending(s => s.ExecutedAtUtc)
            .ToList();
}
