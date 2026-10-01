using System;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using ImportCostAlgeria.ExcelEngine;
using NPOI.HSSF.UserModel;
using Xunit;

namespace ImportCostAlgeria.UnitTests;

/// <summary>
/// Revue du 2026-10-01 (correction urgente — import Excel "Function not supported") : tests de la VRAIE
/// chaîne de lecture utilisée par l'écran Windows "Importations → Import Excel / CSV"
/// (<see cref="ExcelFileReader.ReadFirstSheet"/>), pour les quatre formats exigés : .xlsx, .xlsm, .xls,
/// .csv — y compris un classeur .xlsx contenant une formule, qui reproduisait auparavant l'échec total
/// de l'import ("Function not supported") avant que la lecture ne soit corrigée pour n'utiliser QUE la
/// valeur déjà calculée et mise en cache dans le fichier (jamais de recalcul via le moteur de ClosedXML).
/// </summary>
public sealed class ExcelFileReaderTests : IDisposable
{
    private readonly string _tempDirectory;

    public ExcelFileReaderTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "cimp-exceltests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch { /* nettoyage best-effort du test */ }
    }

    private string TempFile(string fileName) => Path.Combine(_tempDirectory, fileName);

    // ------------------------------------------------------------------
    // .xlsx simple (sans formule)
    // ------------------------------------------------------------------
    [Fact]
    public void ReadFirstSheet_ShouldReadSimpleXlsx_HeadersAndRows_AndKeepEmptyCellsEmpty()
    {
        string path = TempFile("simple.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Articles");
            ws.Cell("A1").Value = "Référence";
            ws.Cell("B1").Value = "Désignation";
            ws.Cell("C1").Value = "Quantité";
            ws.Cell("A2").Value = "ART-001";
            ws.Cell("B2").Value = "Roulement à billes";
            ws.Cell("C2").Value = 10;
            ws.Cell("A3").Value = "ART-002";
            ws.Cell("B3").Value = "Filtre à huile";
            // C3 volontairement vide : une cellule vide doit rester vide, jamais une valeur inventée.
            wb.SaveAs(path);
        }

        var sheet = ExcelFileReader.ReadFirstSheet(path);

        Assert.Equal(3, sheet.Headers.Count);
        Assert.Contains(sheet.Headers, h => h.RawHeader == "Référence");
        Assert.Contains(sheet.Headers, h => h.RawHeader == "Désignation");
        Assert.Contains(sheet.Headers, h => h.RawHeader == "Quantité");
        Assert.Equal(2, sheet.DataRowsByColumnLetter.Count);

        var row2 = sheet.DataRowsByColumnLetter[1];
        Assert.Equal("ART-002", row2["A"]);
        Assert.Equal("Filtre à huile", row2["B"]);
        Assert.Equal(string.Empty, row2["C"]);
    }

    // ------------------------------------------------------------------
    // .xlsx contenant une formule (cause racine exacte de "Function not supported")
    // ------------------------------------------------------------------
    [Fact]
    public void ReadFirstSheet_ShouldReadXlsxContainingAFormula_UsingOnlyTheCachedValue_NeverRecalculating()
    {
        string path = TempFile("avec_formule.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Articles");
            ws.Cell("A1").Value = "Référence";
            ws.Cell("B1").Value = "Libellé calculé";
            ws.Cell("A2").Value = "ART-001";

            // XLOOKUP (Excel 365) n'est PAS implémentée par le moteur de calcul interne de ClosedXML au
            // moment de la rédaction de ce test : c'est exactement le type de formule fournisseur qui
            // provoquait "Function not supported" sur TOUT le fichier dès que l'on appelait
            // GetString()/Value sur la cellule. On fixe nous-mêmes la valeur mise en cache — exactement
            // ce que fait Excel lorsqu'il enregistre un fichier déjà calculé — pour simuler fidèlement un
            // fichier fournisseur réel reçu par CIMP.
            ws.Cell("B2").FormulaA1 = "=XLOOKUP(A2,A2:A2,A2:A2)";
            ws.Cell("B2").CachedValue = "ART-001 (valeur déjà calculée par Excel)";

            wb.SaveAs(path);
        }

        // L'appel ne doit JAMAIS lever d'exception, et doit restituer la valeur déjà calculée par Excel
        // (jamais recalculée par ClosedXML).
        var sheet = ExcelFileReader.ReadFirstSheet(path);

        Assert.Equal(2, sheet.Headers.Count);
        Assert.Single(sheet.DataRowsByColumnLetter);
        Assert.Equal("ART-001 (valeur déjà calculée par Excel)", sheet.DataRowsByColumnLetter[0]["B"]);
    }

    // ------------------------------------------------------------------
    // .xlsm (classeur avec macros) : même moteur de lecture que .xlsx côté CIMP
    // ------------------------------------------------------------------
    [Fact]
    public void ReadFirstSheet_ShouldReadXlsm_HeadersAndRows()
    {
        string path = TempFile("arrivage.xlsm");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Arrivage");
            ws.Cell("A1").Value = "Référence";
            ws.Cell("B1").Value = "Origine";
            ws.Cell("A2").Value = "ART-900";
            ws.Cell("B2").Value = "CN";
            wb.SaveAs(path);
        }

        var sheet = ExcelFileReader.ReadFirstSheet(path);

        Assert.Equal(2, sheet.Headers.Count);
        Assert.Single(sheet.DataRowsByColumnLetter);
        Assert.Equal("CN", sheet.DataRowsByColumnLetter[0]["B"]);
    }

    // ------------------------------------------------------------------
    // .xls (binaire historique, BIFF8) : ClosedXML NE SAIT PAS le lire -> ExcelDataReader
    // ------------------------------------------------------------------
    [Fact]
    public void ReadFirstSheet_ShouldReadLegacyXls_HeadersAndRows_AndKeepEmptyCellsEmpty()
    {
        string path = TempFile("legacy.xls");
        var workbook = new HSSFWorkbook();
        var sheet = workbook.CreateSheet("Articles");

        var headerRow = sheet.CreateRow(0);
        headerRow.CreateCell(0).SetCellValue("Référence");
        headerRow.CreateCell(1).SetCellValue("Désignation");
        headerRow.CreateCell(2).SetCellValue("Quantité");

        var dataRow1 = sheet.CreateRow(1);
        dataRow1.CreateCell(0).SetCellValue("ART-100");
        dataRow1.CreateCell(1).SetCellValue("Pompe à eau");
        dataRow1.CreateCell(2).SetCellValue(5);

        var dataRow2 = sheet.CreateRow(2);
        dataRow2.CreateCell(0).SetCellValue("ART-101");
        dataRow2.CreateCell(1).SetCellValue("Joint torique");
        // Cellule Quantité volontairement absente sur cette ligne -> doit rester vide.

        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            workbook.Write(fs, leaveOpen: false);
        }

        var result = ExcelFileReader.ReadFirstSheet(path);

        Assert.Equal(3, result.Headers.Count);
        Assert.Equal(2, result.DataRowsByColumnLetter.Count);

        var row2 = result.DataRowsByColumnLetter[1];
        Assert.Equal("ART-101", row2["A"]);
        Assert.Equal("Joint torique", row2["B"]);
        Assert.Equal(string.Empty, row2["C"]);
    }

    // ------------------------------------------------------------------
    // .csv (séparateur point-virgule, courant en France/Algérie)
    // ------------------------------------------------------------------
    [Fact]
    public void ReadFirstSheet_ShouldReadCsv_WithSemicolonDelimiter_AndKeepEmptyCellsEmpty()
    {
        string path = TempFile("fournisseur.csv");
        File.WriteAllText(
            path,
            "Référence;Désignation;Quantité\nART-200;Vis M6;100\nART-201;Rondelle;\n",
            new UTF8Encoding(true));

        var result = ExcelFileReader.ReadFirstSheet(path);

        Assert.Equal(3, result.Headers.Count);
        Assert.Equal(2, result.DataRowsByColumnLetter.Count);

        var row2 = result.DataRowsByColumnLetter[1];
        Assert.Equal("ART-201", row2["A"]);
        Assert.Equal(string.Empty, row2["C"]);
    }

    // ------------------------------------------------------------------
    // Non-régression explicitement exigée : le fichier source n'est JAMAIS modifié par la lecture.
    // ------------------------------------------------------------------
    [Fact]
    public void ReadFirstSheet_ShouldNeverModifyTheSourceXlsxFile()
    {
        string path = TempFile("ne_pas_modifier.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Articles");
            ws.Cell("A1").Value = "Référence";
            ws.Cell("A2").Value = "ART-300";
            wb.SaveAs(path);
        }

        byte[] before = File.ReadAllBytes(path);

        ExcelFileReader.ReadFirstSheet(path);

        byte[] after = File.ReadAllBytes(path);
        Assert.Equal(before, after);
    }

    [Fact]
    public void ReadFirstSheet_ShouldNeverModifyTheSourceXlsFile()
    {
        string path = TempFile("ne_pas_modifier.xls");
        var workbook = new HSSFWorkbook();
        var sheet = workbook.CreateSheet("Articles");
        sheet.CreateRow(0).CreateCell(0).SetCellValue("Référence");
        sheet.CreateRow(1).CreateCell(0).SetCellValue("ART-400");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            workbook.Write(fs, leaveOpen: false);
        }

        byte[] before = File.ReadAllBytes(path);

        ExcelFileReader.ReadFirstSheet(path);

        byte[] after = File.ReadAllBytes(path);
        Assert.Equal(before, after);
    }

    // ------------------------------------------------------------------
    // Erreur technique utile en cas d'échec réel (fichier corrompu) : la cause réelle n'est jamais
    // masquée derrière un message générique du type "Function not supported".
    // ------------------------------------------------------------------
    [Fact]
    public void ReadFirstSheet_ShouldWrapTheRealException_WithFileNameAndInnerException_WhenFileIsCorrupted()
    {
        string path = TempFile("corrompu.xlsx");
        File.WriteAllText(path, "Ceci n'est pas un fichier Excel valide.");

        var ex = Assert.Throws<InvalidOperationException>(() => ExcelFileReader.ReadFirstSheet(path));

        Assert.Contains("corrompu.xlsx", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void ReadFirstSheet_ShouldThrowFileNotFoundException_WhenFileDoesNotExist()
    {
        string path = TempFile("inexistant.xlsx");

        Assert.Throws<FileNotFoundException>(() => ExcelFileReader.ReadFirstSheet(path));
    }
}
