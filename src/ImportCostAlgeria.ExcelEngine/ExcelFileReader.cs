using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using ExcelDataReader;

namespace ImportCostAlgeria.ExcelEngine;

/// <summary>
/// Lecture RÉELLE d'un fichier fournisseur sur disque (Priorité 3 du plan de finalisation Windows).
/// Contrairement à <see cref="ExcelImporterService"/> (qui travaille sur une abstraction
/// <see cref="RawExcelSheetData"/> déjà en mémoire), cette classe ouvre effectivement le fichier
/// choisi par l'utilisateur (bouton "Importer Excel" de l'application Windows) :
///   - .xlsx / .xlsm -> ClosedXML, en lisant UNIQUEMENT les valeurs (jamais de recalcul de formule,
///                       voir <see cref="ReadCellTextWithoutEvaluatingFormulas"/>) ;
///   - .xls          -> ExcelDataReader (ClosedXML NE SAIT PAS lire le format binaire historique .xls /
///                       BIFF — ce n'est pas une limitation contournable côté ClosedXML, c'est un format
///                       de fichier totalement différent de l'OOXML .xlsx) ;
///   - .csv          -> analyseur CSV tolérant (détection automatique du séparateur , ou ;).
/// Ne modifie et n'invente jamais de donnée : les cellules vides restent vides, à charge de
/// l'utilisateur de les compléter via le mapping interactif ou la saisie manuelle. Le fichier source
/// n'est JAMAIS réécrit (ouverture en lecture seule uniquement, aucun appel à Save()/SaveAs()).
/// </summary>
public static class ExcelFileReader
{
    /// <summary>
    /// Revue du 2026-10-01 : nécessaire pour qu'ExcelDataReader puisse décoder les classeurs .xls anciens
    /// enregistrés avec une page de code DOS/ANSI (ex. CP1252, courant pour des fichiers français) —
    /// ces encodages ne sont plus enregistrés par défaut en dehors de .NET Framework. L'enregistrement est
    /// idempotent (sans effet s'il a déjà été fait, par exemple par un autre composant de l'application).
    /// </summary>
    static ExcelFileReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static RawExcelSheetData ReadFirstSheet(string filePath, string? explicitWorksheetName = null)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Fichier introuvable : {filePath}", filePath);
        }

        string extension = Path.GetExtension(filePath).ToLowerInvariant();

        try
        {
            return extension switch
            {
                ".csv" => ReadCsv(filePath),
                ".xlsx" or ".xlsm" => ReadWorkbook(filePath, explicitWorksheetName),
                ".xls" => ReadLegacyXls(filePath),
                _ => throw new NotSupportedException(
                    $"Format de fichier non pris en charge : '{extension}'. Formats acceptés : .xlsx, .xlsm, .xls, .csv.")
            };
        }
        catch (Exception ex) when (ex is not FileNotFoundException and not NotSupportedException)
        {
            // Revue du 2026-10-01 : on ne masque JAMAIS l'exception réelle derrière un message générique —
            // elle est ré-encapsulée avec le chemin du fichier et le type d'exception d'origine conservé
            // (ex.InnerException) pour que l'écran d'import puisse afficher une erreur technique utile
            // (cause réelle + fichier concerné) plutôt qu'un message opaque du type "Function not supported".
            throw new InvalidOperationException(
                $"Échec de lecture du fichier '{Path.GetFileName(filePath)}' ({extension}) : {ex.GetType().Name} — {ex.Message}", ex);
        }
    }

    private static RawExcelSheetData ReadWorkbook(string filePath, string? explicitWorksheetName)
    {
        using var workbook = new XLWorkbook(filePath);

        var worksheet = !string.IsNullOrWhiteSpace(explicitWorksheetName)
            ? workbook.Worksheets.FirstOrDefault(w => string.Equals(w.Name, explicitWorksheetName, StringComparison.OrdinalIgnoreCase))
              ?? throw new InvalidOperationException($"Feuille '{explicitWorksheetName}' introuvable dans le classeur.")
            : workbook.Worksheets.FirstOrDefault(w => w.RangeUsed() != null)
              ?? workbook.Worksheets.FirstOrDefault()
              ?? throw new InvalidOperationException("Le classeur Excel ne contient aucune feuille.");

        var usedRange = worksheet.RangeUsed();
        if (usedRange == null)
        {
            return new RawExcelSheetData(Path.GetFileName(filePath), worksheet.Name, Array.Empty<(string, int, string)>(), Array.Empty<IReadOnlyDictionary<string, string>>());
        }

        var firstRow = usedRange.FirstRow();
        var headers = new List<(string ColumnLetter, int ColumnIndex, string RawHeader)>();
        foreach (var cell in firstRow.Cells())
        {
            string columnLetter = cell.WorksheetColumn().ColumnLetter();
            int columnIndex = cell.WorksheetColumn().ColumnNumber();
            string rawHeader = ReadCellTextWithoutEvaluatingFormulas(cell);
            if (string.IsNullOrWhiteSpace(rawHeader))
                continue; // Colonne d'en-tête vide : ignorée, jamais inventée.
            headers.Add((columnLetter, columnIndex, rawHeader));
        }

        var dataRows = new List<IReadOnlyDictionary<string, string>>();
        int lastRowNumber = usedRange.LastRow().RowNumber();
        int firstDataRowNumber = firstRow.RowNumber() + 1;

        for (int rowNum = firstDataRowNumber; rowNum <= lastRowNumber; rowNum++)
        {
            var row = worksheet.Row(rowNum);
            bool rowHasAnyValue = false;
            var rowDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (columnLetter, columnIndex, _) in headers)
            {
                var cell = row.Cell(columnIndex);
                string value = ReadCellTextWithoutEvaluatingFormulas(cell);
                if (!string.IsNullOrEmpty(value))
                    rowHasAnyValue = true;
                rowDict[columnLetter] = value;
            }

            if (rowHasAnyValue)
                dataRows.Add(rowDict);
        }

        return new RawExcelSheetData(Path.GetFileName(filePath), worksheet.Name, headers, dataRows);
    }

    /// <summary>
    /// Revue du 2026-10-01 (correction urgente — erreur "Function not supported" à l'import) : cause
    /// racine identifiée précisément. ClosedXML appelle son propre moteur de calcul interne (CalcEngine)
    /// dès que l'on accède à <c>IXLCell.Value</c>, <c>GetString()</c> ou <c>GetFormattedString()</c> sur
    /// une cellule CONTENANT UNE FORMULE. Ce moteur ne connaît qu'un sous-ensemble des fonctions Excel et
    /// lève une exception "&lt;NOM_FONCTION&gt;() Function not supported" dès que le fichier fournisseur
    /// utilise une fonction qu'il n'implémente pas (ex. RECHERCHEV/VLOOKUP vers un autre classeur,
    /// SOMME.SI.ENS avancé, SI.CONDITIONS/IFS, JOINDRE.TEXTE/TEXTJOIN, formules matricielles dynamiques,
    /// etc. — voir issues ClosedXML #1010, #1217, #2389) — et ce pour TOUTE la feuille, pas seulement la
    /// cellule concernée, provoquant l'échec total de l'import même si une seule formule, sur une seule
    /// cellule, est en cause.
    ///
    /// Or CIMP n'a besoin QUE de la valeur déjà calculée par Excel/le fournisseur et enregistrée dans le
    /// fichier — jamais de recalculer sa formule. <see cref="IXLCell.CachedValue"/> restitue précisément
    /// cette dernière valeur mise en cache SANS JAMAIS invoquer le moteur de calcul de ClosedXML, quelle
    /// que soit la fonction utilisée dans la formule d'origine.
    /// </summary>
    private static string ReadCellTextWithoutEvaluatingFormulas(IXLCell cell)
    {
        if (cell.HasFormula)
        {
            // Chemin normal (quasi tous les classeurs fournisseurs réels, enregistrés par Excel) : la
            // dernière valeur calculée est bien présente et valide dans le fichier -> on la restitue
            // directement, SANS JAMAIS invoquer le moteur de calcul de ClosedXML.
            if (!cell.NeedsRecalculation)
                return FormatCachedValue(cell.CachedValue);

            // Repli (Section « cache formule absente ») : certains classeurs (notamment ceux générés par
            // des outils tiers ou des exports qui n'ont pas persisté la valeur mise en cache de la formule,
            // <c r="..."><f>...</f><v>...</v></c> sans <v>) n'offrent AUCUNE valeur en cache exploitable —
            // NeedsRecalculation reste vrai dès l'ouverture, avant même tout accès de CIMP. Dans ce seul
            // cas (jamais lorsqu'un cache valide existe), on tente une évaluation PONCTUELLE et STRICTEMENT
            // LOCALE à cette cellule (jamais un recalcul global du classeur). Si la fonction utilisée n'est
            // pas supportée par le moteur interne de ClosedXML, l'exception est absorbée ICI MÊME — jamais
            // propagée — pour ne jamais faire échouer la lecture de tout le fichier à cause d'une seule
            // cellule (cf. bug « Function not supported » corrigé le 2026-10-01) : la cellule retombe alors
            // simplement à vide, comme une donnée non déterminable, jamais une valeur inventée.
            try
            {
                return cell.GetString().Trim();
            }
            catch
            {
                return FormatCachedValue(cell.CachedValue);
            }
        }

        return cell.IsEmpty() ? string.Empty : cell.GetString().Trim();
    }

    private static string FormatCachedValue(XLCellValue cachedValue)
    {
        return cachedValue.Type switch
        {
            XLDataType.Blank => string.Empty,
            XLDataType.Boolean => cachedValue.GetBoolean() ? "VRAI" : "FAUX",
            XLDataType.Number => cachedValue.GetNumber().ToString(CultureInfo.InvariantCulture),
            XLDataType.Text => cachedValue.GetText().Trim(),
            XLDataType.DateTime => cachedValue.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            XLDataType.TimeSpan => cachedValue.GetTimeSpan().ToString(),
            // Valeur d'erreur de formule (#REF!, #DIV/0!, #N/A, etc., y compris quand Excel lui-même n'a
            // pas pu calculer la formule) : on ne doit JAMAIS inventer une donnée à partir d'une erreur ->
            // traitée comme une cellule vide, à charge de l'utilisateur de corriger son fichier source si
            // la donnée est réellement indispensable.
            XLDataType.Error => string.Empty,
            _ => cachedValue.ToString() ?? string.Empty
        };
    }

    /// <summary>
    /// Revue du 2026-10-01 : lecture des classeurs .xls (binaire, BIFF2-8). ClosedXML ne prend en charge
    /// QUE le format OOXML (.xlsx/.xlsm/.xltx/.xltm) — ce n'est pas une question de version ou de
    /// configuration, le format .xls est structurellement différent et ClosedXML ne sait pas l'ouvrir.
    /// ExcelDataReader lit nativement .xls ET .xlsx/.xlsm, et ne tente JAMAIS d'évaluer une formule : il
    /// restitue uniquement la dernière valeur calculée et stockée dans le fichier par Excel/le fournisseur,
    /// ce qui exclut par construction toute erreur de type "Function not supported".
    /// </summary>
    private static RawExcelSheetData ReadLegacyXls(string filePath)
    {
        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false }
        });

        if (dataSet.Tables.Count == 0 || dataSet.Tables[0].Rows.Count == 0)
        {
            string emptySheetName = dataSet.Tables.Count > 0 ? dataSet.Tables[0].TableName : "Feuille 1";
            return new RawExcelSheetData(Path.GetFileName(filePath), emptySheetName, Array.Empty<(string, int, string)>(), Array.Empty<IReadOnlyDictionary<string, string>>());
        }

        var table = dataSet.Tables[0];
        var headerRow = table.Rows[0];

        var headers = new List<(string ColumnLetter, int ColumnIndex, string RawHeader)>();
        for (int col = 0; col < table.Columns.Count; col++)
        {
            string raw = FormatLegacyCellValue(headerRow[col]).Trim();
            if (string.IsNullOrWhiteSpace(raw))
                continue; // Colonne d'en-tête vide : ignorée, jamais inventée.
            headers.Add((ToColumnLetter(col + 1), col + 1, raw));
        }

        var dataRows = new List<IReadOnlyDictionary<string, string>>();
        for (int rowIdx = 1; rowIdx < table.Rows.Count; rowIdx++)
        {
            var row = table.Rows[rowIdx];
            bool rowHasAnyValue = false;
            var rowDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (columnLetter, columnIndex, _) in headers)
            {
                string value = columnIndex - 1 < table.Columns.Count ? FormatLegacyCellValue(row[columnIndex - 1]) : string.Empty;
                if (!string.IsNullOrEmpty(value))
                    rowHasAnyValue = true;
                rowDict[columnLetter] = value;
            }

            if (rowHasAnyValue)
                dataRows.Add(rowDict);
        }

        return new RawExcelSheetData(Path.GetFileName(filePath), table.TableName, headers, dataRows);
    }

    private static string FormatLegacyCellValue(object? rawValue)
    {
        return rawValue switch
        {
            null => string.Empty,
            DBNull => string.Empty,
            DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            decimal dec => dec.ToString(CultureInfo.InvariantCulture),
            string s => s.Trim(),
            _ => Convert.ToString(rawValue, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty
        };
    }

    private static RawExcelSheetData ReadCsv(string filePath)
    {
        string[] lines = File.ReadAllLines(filePath, DetectEncoding(filePath));
        var nonEmptyLines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (nonEmptyLines.Count == 0)
        {
            return new RawExcelSheetData(Path.GetFileName(filePath), "CSV", Array.Empty<(string, int, string)>(), Array.Empty<IReadOnlyDictionary<string, string>>());
        }

        char delimiter = DetectDelimiter(nonEmptyLines[0]);

        var headerCells = SplitCsvLine(nonEmptyLines[0], delimiter);
        var headers = new List<(string ColumnLetter, int ColumnIndex, string RawHeader)>();
        for (int i = 0; i < headerCells.Count; i++)
        {
            string raw = headerCells[i].Trim();
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            headers.Add((ToColumnLetter(i + 1), i + 1, raw));
        }

        var dataRows = new List<IReadOnlyDictionary<string, string>>();
        for (int lineIdx = 1; lineIdx < nonEmptyLines.Count; lineIdx++)
        {
            var cells = SplitCsvLine(nonEmptyLines[lineIdx], delimiter);
            var rowDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool rowHasAnyValue = false;

            foreach (var (columnLetter, columnIndex, _) in headers)
            {
                string value = columnIndex - 1 < cells.Count ? cells[columnIndex - 1].Trim() : string.Empty;
                if (!string.IsNullOrEmpty(value))
                    rowHasAnyValue = true;
                rowDict[columnLetter] = value;
            }

            if (rowHasAnyValue)
                dataRows.Add(rowDict);
        }

        return new RawExcelSheetData(Path.GetFileName(filePath), "CSV", headers, dataRows);
    }

    private static Encoding DetectEncoding(string filePath)
    {
        // Respecte un éventuel BOM UTF-8 (fichiers exportés depuis Excel Windows) ; repli Latin1 sinon
        // pour tolérer les accents français exportés en ANSI (comportement fréquent des fournisseurs).
        byte[] bom = new byte[3];
        using (var fs = File.OpenRead(filePath))
        {
            int read = fs.Read(bom, 0, 3);
            if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                return new UTF8Encoding(true);
        }
        return Encoding.Latin1;
    }

    private static char DetectDelimiter(string headerLine)
    {
        int semicolons = headerLine.Count(c => c == ';');
        int commas = headerLine.Count(c => c == ',');
        return semicolons >= commas ? ';' : ',';
    }

    private static List<string> SplitCsvLine(string line, char delimiter)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                    inQuotes = true;
                else if (c == delimiter)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        result.Add(current.ToString());
        return result;
    }

    private static string ToColumnLetter(int columnIndex1Based)
    {
        int dividend = columnIndex1Based;
        string columnName = string.Empty;
        while (dividend > 0)
        {
            int modulo = (dividend - 1) % 26;
            columnName = Convert.ToChar('A' + modulo) + columnName;
            dividend = (dividend - modulo) / 26;
        }
        return columnName;
    }
}
