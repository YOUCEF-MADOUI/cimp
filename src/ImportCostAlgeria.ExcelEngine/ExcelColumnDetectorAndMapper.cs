using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ImportCostAlgeria.ExcelEngine;

public enum CanonicalExcelField
{
    Unmapped = 0,
    ProductReference,   // Référence (REF, REFERENCE, ARTICLE, CODE ARTICLE)
    Designation,        // Désignation (DESIGNATION, DESIGNATION PRODUIT)
    Quantity,           // Quantité (QTE, QUANTITE)
    UnitPurchasePrice,  // Prix d'achat (PU, PRIX UNIT, PRIX ACHAT)
    SalePrice,          // Prix de vente DA (PU uniquement, jamais un total de ligne) — PRIX VENTE, PV
    Currency,           // Devise
    HsCode,             // Code SH (SH, HS CODE, CODE DOUANE)
    OriginCountry,      // Pays d'origine (ORIGINE, COUNTRY)
    ExcelDutyRate,      // Droit de douane (DD, DROIT DOUANE)
    Incoterm,           // Incoterm
    GrossWeightKg,      // Poids
    VolumeM3,           // Volume
    Freight,            // Fret
    Insurance,          // Assurance
    Other               // Autre / Ignoré
}

/// <summary>
/// Libellés utilisateur (français) des champs canoniques de mapping Excel (Section 1 de la demande
/// utilisateur — l'écran "Analyse / Mapping des colonnes" doit afficher des libellés lisibles, ex: "Prix
/// de vente DA" plutôt que le nom brut de l'énumération <see cref="CanonicalExcelField.SalePrice"/>).
/// Centralisé ici (même projet que l'énumération) pour rester utilisable aussi bien par le convertisseur
/// WPF (ImportCostAlgeria.Presentation) que par les tests unitaires multiplateformes.
/// </summary>
public static class CanonicalExcelFieldLabels
{
    private static readonly IReadOnlyDictionary<CanonicalExcelField, string> LabelsFr = new Dictionary<CanonicalExcelField, string>
    {
        [CanonicalExcelField.Unmapped] = "Non mappée",
        [CanonicalExcelField.ProductReference] = "Référence",
        [CanonicalExcelField.Designation] = "Désignation",
        [CanonicalExcelField.Quantity] = "Quantité",
        [CanonicalExcelField.UnitPurchasePrice] = "Prix d'achat",
        [CanonicalExcelField.SalePrice] = "Prix de vente DA",
        [CanonicalExcelField.Currency] = "Devise",
        [CanonicalExcelField.HsCode] = "Code SH",
        [CanonicalExcelField.OriginCountry] = "Pays d'origine",
        [CanonicalExcelField.ExcelDutyRate] = "Droit de douane (Excel)",
        [CanonicalExcelField.Incoterm] = "Incoterm",
        [CanonicalExcelField.GrossWeightKg] = "Poids brut (kg)",
        [CanonicalExcelField.VolumeM3] = "Volume (m³)",
        [CanonicalExcelField.Freight] = "Fret",
        [CanonicalExcelField.Insurance] = "Assurance",
        [CanonicalExcelField.Other] = "Autre / Ignoré"
    };

    public static string LabelFor(CanonicalExcelField field) =>
        LabelsFr.TryGetValue(field, out var label) ? label : field.ToString();
}

public sealed record DetectedColumnMapping(
    string ColumnLetter,
    int ColumnIndex,
    string RawHeaderText,
    string NormalizedHeaderText,
    CanonicalExcelField MatchedField,
    bool IsAutomaticallyRecognized,
    decimal ConfidenceScore);

public sealed record InteractiveMappingPrompt(
    string ColumnLetter,
    string RawHeaderText,
    string PromptQuestionFr,
    IReadOnlyList<CanonicalExcelField> SuggestedOptions);

public sealed record ExcelHeaderAnalysisResult(
    string HeaderSignatureHash,
    string? MatchedSavedTemplateName,
    IReadOnlyList<DetectedColumnMapping> Columns,
    IReadOnlyList<InteractiveMappingPrompt> UnrecognizedColumnsRequiringUserMapping);

public sealed class ExcelMappingTemplate
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid CompanyId { get; init; }
    public required string TemplateName { get; init; } // Ex: "FOURNISSEUR_X"
    public required string HeaderSignatureHash { get; init; }
    public required Dictionary<string, CanonicalExcelField> ColumnLetterToField { get; init; }
}

/// <summary>
/// Moteur de détection automatique d'en-têtes Excel et de mapping interactif (Sections 4 & 5).
/// N'impose aucun modèle Excel unique : normalise les en-têtes, applique les modèles sauvegardés
/// (ex: FOURNISSEUR_X) et génère les demandes de mapping interactif pour les colonnes inconnues.
/// </summary>
public sealed class ExcelColumnDetectorAndMapper
{
    private static readonly Dictionary<string, CanonicalExcelField> DefaultSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["REF"] = CanonicalExcelField.ProductReference,
        ["REFERENCE"] = CanonicalExcelField.ProductReference,
        ["ARTICLE"] = CanonicalExcelField.ProductReference,
        ["CODE ARTICLE"] = CanonicalExcelField.ProductReference,
        ["DESIGNATION"] = CanonicalExcelField.Designation,
        ["DESIGNATION PRODUIT"] = CanonicalExcelField.Designation,
        ["LIBELLE"] = CanonicalExcelField.Designation,
        ["DESCRIPTION"] = CanonicalExcelField.Designation,
        ["PRODUCT"] = CanonicalExcelField.Designation,
        ["QTE"] = CanonicalExcelField.Quantity,
        ["QUANTITE"] = CanonicalExcelField.Quantity,
        ["QTY"] = CanonicalExcelField.Quantity,
        ["QUANTITY"] = CanonicalExcelField.Quantity,
        ["PU"] = CanonicalExcelField.UnitPurchasePrice,
        ["PRIX UNIT"] = CanonicalExcelField.UnitPurchasePrice,
        ["PRIX UNITAIRE"] = CanonicalExcelField.UnitPurchasePrice,
        ["PRIX ACHAT"] = CanonicalExcelField.UnitPurchasePrice,
        ["PRIX D ACHAT"] = CanonicalExcelField.UnitPurchasePrice,
        ["PRICE"] = CanonicalExcelField.UnitPurchasePrice,
        ["UNIT PRICE"] = CanonicalExcelField.UnitPurchasePrice,
        ["UNIT COST"] = CanonicalExcelField.UnitPurchasePrice,
        // Revue du 2026-10-02 (demande utilisateur, Section 1 — "Prix de vente" manquant du mapping) :
        // PRIX UNITAIRE de vente (JAMAIS un total de ligne) -> ImportLine.SalePriceDzd.
        ["PRIX VENTE"] = CanonicalExcelField.SalePrice,
        ["PRIX DE VENTE"] = CanonicalExcelField.SalePrice,
        ["PRIX DE VENTE DA"] = CanonicalExcelField.SalePrice,
        ["PV"] = CanonicalExcelField.SalePrice,
        ["SALE PRICE"] = CanonicalExcelField.SalePrice,
        ["SELLING PRICE"] = CanonicalExcelField.SalePrice,
        ["PRIX VENTE UNITAIRE"] = CanonicalExcelField.SalePrice,
        ["PRIX DE VENTE UNITAIRE"] = CanonicalExcelField.SalePrice,
        ["DEVISE"] = CanonicalExcelField.Currency,
        ["CURRENCY"] = CanonicalExcelField.Currency,
        ["CCY"] = CanonicalExcelField.Currency,
        ["SH"] = CanonicalExcelField.HsCode,
        ["CODE SH"] = CanonicalExcelField.HsCode,
        ["HS CODE"] = CanonicalExcelField.HsCode,
        ["HS"] = CanonicalExcelField.HsCode,
        ["CODE DOUANE"] = CanonicalExcelField.HsCode,
        ["ORIGINE"] = CanonicalExcelField.OriginCountry,
        ["PAYS D ORIGINE"] = CanonicalExcelField.OriginCountry,
        ["PAYS ORIGINE"] = CanonicalExcelField.OriginCountry,
        ["COUNTRY"] = CanonicalExcelField.OriginCountry,
        ["ORIGIN"] = CanonicalExcelField.OriginCountry,
        ["COUNTRY OF ORIGIN"] = CanonicalExcelField.OriginCountry,
        ["DD"] = CanonicalExcelField.ExcelDutyRate,
        ["DROIT DOUANE"] = CanonicalExcelField.ExcelDutyRate,
        ["DROIT DE DOUANE"] = CanonicalExcelField.ExcelDutyRate,
        ["DUTY RATE"] = CanonicalExcelField.ExcelDutyRate,
        ["DUTY"] = CanonicalExcelField.ExcelDutyRate,
        ["INCOTERM"] = CanonicalExcelField.Incoterm,
        ["POIDS"] = CanonicalExcelField.GrossWeightKg,
        ["POIDS BRUT"] = CanonicalExcelField.GrossWeightKg,
        ["VOLUME"] = CanonicalExcelField.VolumeM3,
        ["FRET"] = CanonicalExcelField.Freight,
        ["ASSURANCE"] = CanonicalExcelField.Insurance
    };

    private static readonly IReadOnlyList<CanonicalExcelField> InteractiveChoices = new[]
    {
        CanonicalExcelField.UnitPurchasePrice, // [ Prix d'achat ]
        CanonicalExcelField.SalePrice,         // [ Prix de vente DA ]
        CanonicalExcelField.Quantity,          // [ Quantité ]
        CanonicalExcelField.Freight,           // [ Fret ]
        CanonicalExcelField.Insurance,         // [ Assurance ]
        CanonicalExcelField.HsCode,            // [ Code SH ]
        CanonicalExcelField.OriginCountry,     // [ Pays d'origine ]
        CanonicalExcelField.ExcelDutyRate,     // [ Droit de douane ]
        CanonicalExcelField.Other              // [ Autre ]
    };

    public ExcelHeaderAnalysisResult AnalyzeHeaders(
        IReadOnlyList<(string ColumnLetter, int ColumnIndex, string RawHeader)> headers,
        IReadOnlyList<ExcelMappingTemplate> savedCompanyTemplates)
    {
        string signatureHash = ComputeHeaderSignature(headers.Select(h => h.RawHeader));
        var matchedTemplate = savedCompanyTemplates.FirstOrDefault(t =>
            string.Equals(t.HeaderSignatureHash, signatureHash, StringComparison.OrdinalIgnoreCase));

        var detected = new List<DetectedColumnMapping>();
        var prompts = new List<InteractiveMappingPrompt>();

        foreach (var (colLetter, colIdx, rawHeader) in headers)
        {
            string normalized = NormalizeHeader(rawHeader);

            // 1. Si un modèle sauvegardé (ex: FOURNISSEUR_X) correspond à la structure du fichier
            if (matchedTemplate != null && matchedTemplate.ColumnLetterToField.TryGetValue(colLetter, out var templateField))
            {
                detected.Add(new DetectedColumnMapping(
                    colLetter, colIdx, rawHeader, normalized, templateField, true, 100m));
                continue;
            }

            // 2. Sinon recherche dans le dictionnaire de synonymes normalisés
            if (DefaultSynonyms.TryGetValue(normalized, out var mappedField))
            {
                detected.Add(new DetectedColumnMapping(
                    colLetter, colIdx, rawHeader, normalized, mappedField, true, 100m));
            }
            else
            {
                // 3. Colonne non reconnue -> Prépare l'invite de mapping interactif (Section 5)
                detected.Add(new DetectedColumnMapping(
                    colLetter, colIdx, rawHeader, normalized, CanonicalExcelField.Unmapped, false, 0m));

                prompts.Add(new InteractiveMappingPrompt(
                    ColumnLetter: colLetter,
                    RawHeaderText: rawHeader,
                    PromptQuestionFr: $"Colonne Excel : \"{rawHeader}\" — Le logiciel ne reconnaît pas cette colonne. À quoi correspond-elle ?",
                    SuggestedOptions: InteractiveChoices));
            }
        }

        return new ExcelHeaderAnalysisResult(
            HeaderSignatureHash: signatureHash,
            MatchedSavedTemplateName: matchedTemplate?.TemplateName,
            Columns: detected,
            UnrecognizedColumnsRequiringUserMapping: prompts);
    }

    public static string NormalizeHeader(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string decomposed = raw.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else
                sb.Append(' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static string ComputeHeaderSignature(IEnumerable<string> rawHeaders)
    {
        string joined = string.Join("|", rawHeaders.Select(NormalizeHeader));
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes);
    }
}
