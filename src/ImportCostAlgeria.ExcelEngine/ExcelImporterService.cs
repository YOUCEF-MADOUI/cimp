using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Core.Services;

namespace ImportCostAlgeria.ExcelEngine;

public sealed record RawExcelSheetData(
    string FileName,
    string WorksheetName,
    IReadOnlyList<(string ColumnLetter, int ColumnIndex, string RawHeader)> Headers,
    IReadOnlyList<IReadOnlyDictionary<string, string>> DataRowsByColumnLetter);

public sealed record ExcelImportConversionResult(
    bool RequiresInteractiveUserMapping,
    ExcelHeaderAnalysisResult HeaderAnalysis,
    IReadOnlyList<ImportLine> ConvertedLines,
    IReadOnlyList<ProductCatalogRecognitionProposal> CatalogProposals,
    IReadOnlyList<string> ValidationMessages);

/// <summary>
/// Abstraction de persistance des modèles de mapping Excel par fournisseur (Sections 4, 5 & 31).
/// Implémentée par ImportCostAlgeria.Database.Repositories.EfMappingTemplateStore pour une persistance
/// réelle en base ; à défaut, un stockage en mémoire (session courante) est utilisé.
/// </summary>
public interface IMappingTemplateStore
{
    void Save(ExcelMappingTemplate template);
    IReadOnlyList<ExcelMappingTemplate> GetForCompany(Guid companyId);
}

public sealed class InMemoryMappingTemplateStore : IMappingTemplateStore
{
    private readonly List<ExcelMappingTemplate> _savedTemplates = new();

    public void Save(ExcelMappingTemplate template)
    {
        _savedTemplates.RemoveAll(t =>
            t.CompanyId == template.CompanyId &&
            (string.Equals(t.TemplateName, template.TemplateName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(t.HeaderSignatureHash, template.HeaderSignatureHash, StringComparison.OrdinalIgnoreCase)));
        _savedTemplates.Add(template);
    }

    public IReadOnlyList<ExcelMappingTemplate> GetForCompany(Guid companyId) =>
        _savedTemplates.Where(t => t.CompanyId == companyId).ToList();
}

/// <summary>
/// Service complet d'importation Excel, d'application/sauvegarde des modèles de mapping (Section 4 & 5)
/// et de rapprochement avec la Base Produits de l'entreprise (Section 31).
/// </summary>
public sealed class ExcelImporterService
{
    private readonly ExcelColumnDetectorAndMapper _detector;
    private readonly ProductCatalogService _productCatalog;
    private readonly IMappingTemplateStore _templateStore;

    public ExcelImporterService(
        ExcelColumnDetectorAndMapper detector,
        ProductCatalogService productCatalog)
        : this(detector, productCatalog, new InMemoryMappingTemplateStore())
    {
    }

    public ExcelImporterService(
        ExcelColumnDetectorAndMapper detector,
        ProductCatalogService productCatalog,
        IMappingTemplateStore templateStore)
    {
        _detector = detector;
        _productCatalog = productCatalog;
        _templateStore = templateStore;
    }

    public ExcelMappingTemplate SaveUserMappingAsTemplate(
        Guid companyId,
        string templateName,
        IReadOnlyList<string> rawHeaders,
        Dictionary<string, CanonicalExcelField> columnLetterToField)
    {
        string signatureHash = ExcelColumnDetectorAndMapper.ComputeHeaderSignature(rawHeaders);
        var template = new ExcelMappingTemplate
        {
            CompanyId = companyId,
            TemplateName = templateName,
            HeaderSignatureHash = signatureHash,
            ColumnLetterToField = new Dictionary<string, CanonicalExcelField>(columnLetterToField, StringComparer.OrdinalIgnoreCase)
        };

        _templateStore.Save(template);
        return template;
    }

    public ExcelImportConversionResult ProcessExcelSheet(
        Guid companyId,
        RawExcelSheetData sheet,
        string defaultCurrencyCode,
        string? defaultOriginIso2,
        IReadOnlyDictionary<string, CanonicalExcelField>? userInteractiveOverrides = null)
    {
        var companyTemplates = _templateStore.GetForCompany(companyId);
        var analysis = _detector.AnalyzeHeaders(sheet.Headers, companyTemplates);

        var effectiveMap = new Dictionary<string, CanonicalExcelField>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in analysis.Columns)
        {
            if (col.IsAutomaticallyRecognized && col.MatchedField != CanonicalExcelField.Unmapped)
            {
                effectiveMap[col.ColumnLetter] = col.MatchedField;
            }
        }

        if (userInteractiveOverrides != null)
        {
            foreach (var kvp in userInteractiveOverrides)
            {
                effectiveMap[kvp.Key] = kvp.Value;
            }
        }

        bool stillUnrecognized = analysis.UnrecognizedColumnsRequiringUserMapping
            .Any(u => !effectiveMap.ContainsKey(u.ColumnLetter) || effectiveMap[u.ColumnLetter] == CanonicalExcelField.Unmapped);

        if (stillUnrecognized)
        {
            return new ExcelImportConversionResult(
                RequiresInteractiveUserMapping: true,
                HeaderAnalysis: analysis,
                ConvertedLines: Array.Empty<ImportLine>(),
                CatalogProposals: Array.Empty<ProductCatalogRecognitionProposal>(),
                ValidationMessages: new[] { "Mapping interactif requis pour une ou plusieurs colonnes non reconnues." });
        }

        var lines = new List<ImportLine>();
        var proposals = new List<ProductCatalogRecognitionProposal>();
        var validationMessages = new List<string>();

        string? FindValue(IReadOnlyDictionary<string, string> row, CanonicalExcelField target)
        {
            var colEntry = effectiveMap.FirstOrDefault(x => x.Value == target);
            if (string.IsNullOrEmpty(colEntry.Key))
                return null;
            return row.TryGetValue(colEntry.Key, out var val) ? val?.Trim() : null;
        }

        int lineNo = 1;
        foreach (var row in sheet.DataRowsByColumnLetter)
        {
            string reference = FindValue(row, CanonicalExcelField.ProductReference) ?? $"ART-{lineNo:D3}";
            string designation = FindValue(row, CanonicalExcelField.Designation) ?? "Désignation non renseignée";
            decimal quantity = ParseDecimalOrDefault(FindValue(row, CanonicalExcelField.Quantity), 0m);
            decimal unitPrice = ParseDecimalOrDefault(FindValue(row, CanonicalExcelField.UnitPurchasePrice), 0m);
            string currency = FindValue(row, CanonicalExcelField.Currency) ?? defaultCurrencyCode;
            string? hsCode = FindValue(row, CanonicalExcelField.HsCode);
            string? origin = FindValue(row, CanonicalExcelField.OriginCountry) ?? defaultOriginIso2;
            decimal? excelDuty = ParseNullableDecimal(FindValue(row, CanonicalExcelField.ExcelDutyRate));
            decimal? grossWeight = ParseNullableDecimal(FindValue(row, CanonicalExcelField.GrossWeightKg));
            decimal? volume = ParseNullableDecimal(FindValue(row, CanonicalExcelField.VolumeM3));

            if (quantity <= 0m)
            {
                validationMessages.Add($"Ligne {lineNo} ({reference}) : Quantité invalide ou nulle ({quantity}).");
            }

            // Rapprochement avec la Base Produits de l'entreprise (Section 31)
            var catalogProposal = _productCatalog.TryRecognizeProductForCompany(companyId, reference);
            if (catalogProposal != null)
            {
                proposals.Add(catalogProposal);
            }

            lines.Add(new ImportLine
            {
                LineNumber = lineNo++,
                ProductId = catalogProposal?.ProductId,
                ProductReference = reference,
                Designation = designation,
                Quantity = quantity > 0m ? quantity : 1m,
                UnitPurchasePrice = unitPrice,
                CurrencyCode = currency.ToUpperInvariant(),
                HsCodeConfirmed10 = hsCode,
                OriginCountryIso2 = origin?.ToUpperInvariant(),
                ExcelDutyRatePercent = excelDuty,
                LineGrossWeightKg = grossWeight,
                LineVolumeM3 = volume
            });
        }

        return new ExcelImportConversionResult(
            RequiresInteractiveUserMapping: false,
            HeaderAnalysis: analysis,
            ConvertedLines: lines,
            CatalogProposals: proposals,
            ValidationMessages: validationMessages);
    }

    private static decimal ParseDecimalOrDefault(string? raw, decimal fallback) =>
        ParseNullableDecimal(raw) ?? fallback;

    private static decimal? ParseNullableDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        string cleaned = raw.Replace("%", "").Replace(" ", "").Replace("\u00A0", "").Replace(",", ".").Trim();
        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val) ? val : null;
    }
}
