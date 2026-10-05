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
    /// <summary>
    /// Revue du 2026-10-01 (correction urgente — validation du mapping Excel) : liste EXHAUSTIVE et
    /// EXPLICITE des champs réellement indispensables à la création fiable d'une <see cref="ImportLine"/>.
    /// Toute colonne fournisseur qui ne correspond à AUCUN de ces champs (ex. "TOTAL", une colonne de
    /// remarque, une colonne totalement étrangère au modèle CIMP) peut légitimement rester
    /// <see cref="CanonicalExcelField.Unmapped"/> SANS bloquer l'import — ce n'est plus "toutes les
    /// colonnes doivent être mappées" mais "ces champs précis doivent être couverts par au moins une
    /// colonne, peu importe le nombre de colonnes restées Unmapped par ailleurs".
    ///
    /// <see cref="CanonicalExcelField.ProductReference"/> n'est volontairement PAS dans cette liste :
    /// une référence est générée automatiquement (ART-001, ART-002, ...) si aucune colonne n'y est
    /// mappée (voir plus bas, FindValue + génération de repli). De même,
    /// <see cref="CanonicalExcelField.Currency"/> et <see cref="CanonicalExcelField.OriginCountry"/> ne
    /// sont pas obligatoires : la devise/l'origine PAR DÉFAUT de l'opération est utilisée si la colonne
    /// correspondante n'est pas mappée. HsCode, ExcelDutyRate, GrossWeightKg, VolumeM3, Incoterm, Freight,
    /// Insurance restent, comme avant, purement facultatifs.
    /// </summary>
    private static readonly (CanonicalExcelField Field, string LabelFr)[] RequiredFields =
    {
        (CanonicalExcelField.Designation, "Désignation"),
        (CanonicalExcelField.Quantity, "Quantité"),
        (CanonicalExcelField.UnitPurchasePrice, "Prix d'achat unitaire")
    };

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

        // Revue du 2026-10-01 (correction urgente — validation du mapping Excel) : on n'exige PLUS que
        // TOUTES les colonnes du fichier soient mappées à un champ CIMP. Une colonne fournisseur qui ne
        // correspond à aucun besoin de CIMP (ex. "TOTAL", une colonne de remarque, une colonne inconnue)
        // peut légitimement rester CanonicalExcelField.Unmapped SANS bloquer l'import. Seuls les CHAMPS
        // OBLIGATOIRES (RequiredFields) doivent être couverts par AU MOINS une colonne du fichier.
        var mappedFields = new HashSet<CanonicalExcelField>(effectiveMap.Values);
        var missingRequiredFields = RequiredFields
            .Where(rf => !mappedFields.Contains(rf.Field))
            .ToList();

        if (missingRequiredFields.Count > 0)
        {
            string details = string.Join("\n", missingRequiredFields.Select(f => $"{f.LabelFr} est obligatoire."));
            return new ExcelImportConversionResult(
                RequiresInteractiveUserMapping: true,
                HeaderAnalysis: analysis,
                ConvertedLines: Array.Empty<ImportLine>(),
                CatalogProposals: Array.Empty<ProductCatalogRecognitionProposal>(),
                ValidationMessages: new[] { $"Mapping incomplet :\n{details}" });
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
            // Revue du 2026-10-05 (Bug 2 — vérification de la convention de pourcentage, "NE PAS DEVINER") :
            // la valeur brute lue dans la colonne Excel "DD" est stockée TELLE QUELLE, en points de
            // pourcentage directement exploitables (ex : "15" ou "15%" -> 15.0 -> 15 %), SANS normalisation
            // ×100 d'une éventuelle fraction Excel (ex : "0.15" reste 0.15, PAS 15). Décision justifiée par
            // le code existant, pas par supposition : ImportLine.ExcelDutyRatePercent est déjà documenté et
            // testé (voir V1CompleteTestSuite.cs) comme exprimé directement en points de pourcentage
            // (10.0m = 10 %, 5.0m = 5 %), et ImportCalculationOrchestrator applique toujours
            // "valeur × (ExcelDutyRatePercent / 100)" sans transformation préalable — aucune partie du projet
            // ne traite actuellement une valeur Excel "0.05" comme signifiant "5 %". Si un fichier Excel
            // fournit un taux sous forme de fraction (colonne au format Pourcentage Excel, valeur interne
            // 0.05 pour afficher "5 %"), il doit être saisi/exporté sous la forme "5" ou "5%" dans la colonne
            // DD pour être interprété correctement par CIMP — voir l'avertissement explicite déclenché par
            // ImportCalculationOrchestrator lorsqu'un taux Excel anormalement faible (< 1 %) est utilisé.
            decimal? excelDuty = ParseNullableDecimal(FindValue(row, CanonicalExcelField.ExcelDutyRate));
            decimal? grossWeight = ParseNullableDecimal(FindValue(row, CanonicalExcelField.GrossWeightKg));
            decimal? volume = ParseNullableDecimal(FindValue(row, CanonicalExcelField.VolumeM3));
            // Revue du 2026-10-02 (demande utilisateur, Section 1/11) : "Prix de vente" est un PRIX
            // UNITAIRE en DA — JAMAIS un total de ligne (ex: QTE=100, Prix vente=600 DA -> la colonne
            // reste 600, pas 60 000). Aucune conversion de devise n'est appliquée : le prix de vente est
            // toujours directement saisi/attendu en DA (SalePriceDzd), quelle que soit la devise de facture
            // de la ligne (prix d'achat).
            decimal? salePriceDzd = ParseNullableDecimal(FindValue(row, CanonicalExcelField.SalePrice));

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
                LineVolumeM3 = volume,
                SalePriceDzd = salePriceDzd
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
