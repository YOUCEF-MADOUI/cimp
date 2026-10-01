using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ImportCostAlgeria.Core.Services;
using ImportCostAlgeria.ExcelEngine;

namespace ImportCostAlgeria.UnitTests;

/// <summary>
/// Revue du 2026-10-01 (correction urgente — validation du mapping Excel) : reproduit exactement le
/// scénario signalé avec le fichier fournisseur "arrivage1.xlsx" (colonnes ref / ARTICLE / QTY / euro /
/// TOTAL) et vérifie que <see cref="ExcelImporterService.ProcessExcelSheet"/> n'exige PLUS que TOUTES
/// les colonnes soient mappées : seuls les champs réellement indispensables (Désignation, Quantité,
/// Prix d'achat unitaire) doivent être couverts par une colonne ; une colonne fournisseur inutile à CIMP
/// (ex. "TOTAL") peut légitimement rester <see cref="CanonicalExcelField.Unmapped"/> sans bloquer
/// l'import, alors qu'un champ réellement obligatoire manquant doit toujours bloquer l'import avec un
/// message clair.
/// </summary>
public sealed class ExcelMappingValidationTests
{
    private static ExcelImporterService NewImporter() =>
        new(new ExcelColumnDetectorAndMapper(), new ProductCatalogService());

    /// <summary>Reproduit exactement l'en-tête du fichier arrivage1.xlsx signalé (avec ou sans TOTAL/colonnes en trop).</summary>
    private static RawExcelSheetData BuildArrivage1Sheet(params string[] extraHeaders)
    {
        var headers = new List<(string ColumnLetter, int ColumnIndex, string RawHeader)>
        {
            ("A", 1, "ref"),
            ("B", 2, "ARTICLE"),
            ("C", 3, "QTY"),
            ("D", 4, "euro")
        };

        int nextIndex = 5;
        var columnLetters = new[] { "E", "F", "G", "H", "I" };
        foreach (var extra in extraHeaders)
        {
            headers.Add((columnLetters[nextIndex - 5], nextIndex, extra));
            nextIndex++;
        }

        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = "ART-001",
            ["B"] = "Roulement à billes",
            ["C"] = "100",
            ["D"] = "1,25"
        };
        foreach (var (columnLetter, _, _) in headers.Skip(4))
            row[columnLetter] = "125,00"; // valeur quelconque dans les colonnes supplémentaires (TOTAL, REMARQUE, ...)

        return new RawExcelSheetData(
            FileName: "arrivage1.xlsx",
            WorksheetName: "Feuil1",
            Headers: headers,
            DataRowsByColumnLetter: new IReadOnlyDictionary<string, string>[] { row });
    }

    // ------------------------------------------------------------------
    // TEST 1 : ref | ARTICLE | QTY | euro | TOTAL, avec TOTAL = Unmapped -> import accepté.
    // ------------------------------------------------------------------
    [Fact]
    public void ProcessExcelSheet_ShouldAcceptImport_WhenOnlyAnUnnecessaryColumnStaysUnmapped()
    {
        var sheet = BuildArrivage1Sheet("TOTAL");
        var overrides = new Dictionary<string, CanonicalExcelField>
        {
            ["A"] = CanonicalExcelField.ProductReference,
            ["B"] = CanonicalExcelField.Designation,
            ["C"] = CanonicalExcelField.Quantity,
            ["D"] = CanonicalExcelField.UnitPurchasePrice,
            ["E"] = CanonicalExcelField.Unmapped // TOTAL : non nécessaire à CIMP, reste Unmapped.
        };

        var result = NewImporter().ProcessExcelSheet(Guid.NewGuid(), sheet, "EUR", "CN", overrides);

        Assert.False(result.RequiresInteractiveUserMapping);
        Assert.Single(result.ConvertedLines);
        Assert.Equal("ART-001", result.ConvertedLines[0].ProductReference);
        Assert.Equal("Roulement à billes", result.ConvertedLines[0].Designation);
        Assert.Equal(100m, result.ConvertedLines[0].Quantity);
        Assert.Equal(1.25m, result.ConvertedLines[0].UnitPurchasePrice);
    }

    // ------------------------------------------------------------------
    // TEST 2 : même fichier, mais QTY = Unmapped -> import refusé avec message clair.
    // ------------------------------------------------------------------
    [Fact]
    public void ProcessExcelSheet_ShouldRejectImport_WithAClearMessage_WhenARequiredFieldIsUnmapped()
    {
        var sheet = BuildArrivage1Sheet("TOTAL");
        var overrides = new Dictionary<string, CanonicalExcelField>
        {
            ["A"] = CanonicalExcelField.ProductReference,
            ["B"] = CanonicalExcelField.Designation,
            ["C"] = CanonicalExcelField.Unmapped, // QTY laissé Unmapped par erreur : doit bloquer.
            ["D"] = CanonicalExcelField.UnitPurchasePrice,
            ["E"] = CanonicalExcelField.Unmapped
        };

        var result = NewImporter().ProcessExcelSheet(Guid.NewGuid(), sheet, "EUR", "CN", overrides);

        Assert.True(result.RequiresInteractiveUserMapping);
        Assert.Empty(result.ConvertedLines);
        Assert.Contains(result.ValidationMessages, m => m.Contains("Mapping incomplet"));
        Assert.Contains(result.ValidationMessages, m => m.Contains("Quantité est obligatoire."));
        // Message EXACT exigé par la revue pour le cas d'un seul champ obligatoire manquant.
        Assert.Contains("Mapping incomplet :\nQuantité est obligatoire.", result.ValidationMessages);
    }

    // ------------------------------------------------------------------
    // TEST 3 : ref | ARTICLE | QTY | euro (sans TOTAL) -> import accepté.
    // ------------------------------------------------------------------
    [Fact]
    public void ProcessExcelSheet_ShouldAcceptImport_WhenAllRequiredFieldsAreMapped_AndThereIsNoExtraColumnAtAll()
    {
        var sheet = BuildArrivage1Sheet(); // pas de colonne TOTAL du tout
        var overrides = new Dictionary<string, CanonicalExcelField>
        {
            ["A"] = CanonicalExcelField.ProductReference,
            ["B"] = CanonicalExcelField.Designation,
            ["C"] = CanonicalExcelField.Quantity,
            ["D"] = CanonicalExcelField.UnitPurchasePrice
        };

        var result = NewImporter().ProcessExcelSheet(Guid.NewGuid(), sheet, "EUR", "CN", overrides);

        Assert.False(result.RequiresInteractiveUserMapping);
        Assert.Single(result.ConvertedLines);
    }

    // ------------------------------------------------------------------
    // TEST 4 : colonnes supplémentaires inconnues (TOTAL, REMARQUE, AUTRE) -> ne bloquent pas l'import.
    // ------------------------------------------------------------------
    [Fact]
    public void ProcessExcelSheet_ShouldAcceptImport_WhenSeveralUnknownExtraColumnsStayUnmapped()
    {
        var sheet = BuildArrivage1Sheet("TOTAL", "REMARQUE", "AUTRE");
        var overrides = new Dictionary<string, CanonicalExcelField>
        {
            ["A"] = CanonicalExcelField.ProductReference,
            ["B"] = CanonicalExcelField.Designation,
            ["C"] = CanonicalExcelField.Quantity,
            ["D"] = CanonicalExcelField.UnitPurchasePrice
            // E (TOTAL), F (REMARQUE), G (AUTRE) ne sont volontairement PAS dans les overrides :
            // elles restent à la valeur par défaut CanonicalExcelField.Unmapped, exactement comme si
            // l'utilisateur ne les avait jamais touchées dans la grille de mapping interactif.
        };

        var result = NewImporter().ProcessExcelSheet(Guid.NewGuid(), sheet, "EUR", "CN", overrides);

        Assert.False(result.RequiresInteractiveUserMapping);
        Assert.Single(result.ConvertedLines);

        // Les 3 colonnes en trop sont bien restées Unmapped dans l'analyse, sans empêcher l'import.
        var extraColumns = result.HeaderAnalysis.Columns.Where(c => c.ColumnLetter is "E" or "F" or "G").ToList();
        Assert.Equal(3, extraColumns.Count);
        Assert.All(extraColumns, c => Assert.Equal(CanonicalExcelField.Unmapped, c.MatchedField));
    }

    // ------------------------------------------------------------------
    // Non-régression explicite : Désignation ou Prix d'achat manquant doit aussi bloquer (pas seulement Quantité).
    // ------------------------------------------------------------------
    [Fact]
    public void ProcessExcelSheet_ShouldRejectImport_WhenDesignationIsUnmapped()
    {
        var sheet = BuildArrivage1Sheet("TOTAL");
        var overrides = new Dictionary<string, CanonicalExcelField>
        {
            ["A"] = CanonicalExcelField.ProductReference,
            ["B"] = CanonicalExcelField.Unmapped, // Désignation laissée Unmapped : doit bloquer.
            ["C"] = CanonicalExcelField.Quantity,
            ["D"] = CanonicalExcelField.UnitPurchasePrice,
            ["E"] = CanonicalExcelField.Unmapped
        };

        var result = NewImporter().ProcessExcelSheet(Guid.NewGuid(), sheet, "EUR", "CN", overrides);

        Assert.True(result.RequiresInteractiveUserMapping);
        Assert.Contains("Mapping incomplet :\nDésignation est obligatoire.", result.ValidationMessages);
    }

    [Fact]
    public void ProcessExcelSheet_ShouldRejectImport_WhenUnitPurchasePriceIsUnmapped()
    {
        var sheet = BuildArrivage1Sheet("TOTAL");
        var overrides = new Dictionary<string, CanonicalExcelField>
        {
            ["A"] = CanonicalExcelField.ProductReference,
            ["B"] = CanonicalExcelField.Designation,
            ["C"] = CanonicalExcelField.Quantity,
            ["D"] = CanonicalExcelField.Unmapped, // Prix d'achat laissé Unmapped : doit bloquer.
            ["E"] = CanonicalExcelField.Unmapped
        };

        var result = NewImporter().ProcessExcelSheet(Guid.NewGuid(), sheet, "EUR", "CN", overrides);

        Assert.True(result.RequiresInteractiveUserMapping);
        Assert.Contains("Mapping incomplet :\nPrix d'achat unitaire est obligatoire.", result.ValidationMessages);
    }

    // ------------------------------------------------------------------
    // ProductReference n'est volontairement pas obligatoire : une référence est générée automatiquement.
    // ------------------------------------------------------------------
    [Fact]
    public void ProcessExcelSheet_ShouldAcceptImport_AndGenerateAReference_WhenProductReferenceColumnIsUnmapped()
    {
        var sheet = BuildArrivage1Sheet("TOTAL");
        var overrides = new Dictionary<string, CanonicalExcelField>
        {
            ["A"] = CanonicalExcelField.Unmapped, // Référence non mappée : doit être générée, pas bloquante.
            ["B"] = CanonicalExcelField.Designation,
            ["C"] = CanonicalExcelField.Quantity,
            ["D"] = CanonicalExcelField.UnitPurchasePrice,
            ["E"] = CanonicalExcelField.Unmapped
        };

        var result = NewImporter().ProcessExcelSheet(Guid.NewGuid(), sheet, "EUR", "CN", overrides);

        Assert.False(result.RequiresInteractiveUserMapping);
        Assert.Single(result.ConvertedLines);
        Assert.StartsWith("ART-", result.ConvertedLines[0].ProductReference);
    }
}
