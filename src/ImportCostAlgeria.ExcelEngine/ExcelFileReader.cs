using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;

namespace ImportCostAlgeria.ExcelEngine;

/// <summary>
/// Lecture RÉELLE d'un fichier fournisseur sur disque (Priorité 3 du plan de finalisation Windows).
/// Contrairement à <see cref="ExcelImporterService"/> (qui travaille sur une abstraction
/// <see cref="RawExcelSheetData"/> déjà en mémoire), cette classe ouvre effectivement le fichier
/// choisi par l'utilisateur (bouton "Importer Excel" de l'application Windows) :
///   - .xlsx / .xlsm / .xls -> ClosedXML (première feuille non vide, ou feuille nommée si précisée) ;
///   - .csv                 -> analyseur CSV tolérant (détection automatique du séparateur , ou ;).
/// Ne modifie et n'invente jamais de donnée : les cellules vides restent vides, à charge de
/// l'utilisateur de les compléter via le mapping interactif ou la saisie manuelle.
/// </summary>
public static class ExcelFileReader
{
    public static RawExcelSheetData ReadFirstSheet(string filePath, string? explicitWorksheetName = null)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Fichier introuvable : {filePath}", filePath);
        }

        string extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".csv" => ReadCsv(filePath),
            ".xlsx" or ".xlsm" or ".xls" => ReadWorkbook(filePath, explicitWorksheetName),
            _ => throw new NotSupportedException(
                $"Format de fichier non pris en charge : '{extension}'. Formats acceptés : .xlsx, .xlsm, .xls, .csv.")
        };
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
            string rawHeader = cell.GetString().Trim();
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
                string value = cell.IsEmpty() ? string.Empty : cell.GetString().Trim();
                if (!string.IsNullOrEmpty(value))
                    rowHasAnyValue = true;
                rowDict[columnLetter] = value;
            }

            if (rowHasAnyValue)
                dataRows.Add(rowDict);
        }

        return new RawExcelSheetData(Path.GetFileName(filePath), worksheet.Name, headers, dataRows);
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
