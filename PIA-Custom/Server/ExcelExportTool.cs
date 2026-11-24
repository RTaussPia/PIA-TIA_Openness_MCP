using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using ModelContextProtocol.Server;

namespace Pia.Custom;

public static class ExcelExportTool
{
    private const int MaxTitleLength = 120;
    private const int MaxColumns = 64;
    private const int MaxRows = 5000;
    private const int MaxTextLength = 32767;
    private const int MaxPayloadCharacters = 4000000;

    [McpServerTool(Name = "ExportExcel"), Description("Create one local Excel sheet with headers and rows from the chat. Saves a new timestamped file in Documents/PIA-MCP/Exports. The optional chat instructions are in GetProjectKnowledge document excelExport.")]
    public static object ExportExcel(
        [Description("Context-derived file title, 1–120 characters. Omit paths, file extensions, and timestamps.")] string title,
        [Description("1–64 unique, nonempty column headers in output order.")] string[] columns,
        [Description("Up to 5,000 rows with exactly one cell per column. Cells accept text, numbers, booleans, or null. Total cell content limit is 4 million characters.")] JsonElement[][] rows)
    {
        string? safeTitle = FileTitle(title);
        if (safeTitle == null) return new { status = "invalidTitle" };
        if (columns == null || columns.Length == 0 || columns.Length > MaxColumns)
            return new { status = "invalidColumns", maximum = MaxColumns };

        var headers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
            if (string.IsNullOrWhiteSpace(column) || !ValidText(column) || !headers.Add(column.Trim()))
                return new { status = "invalidColumns", reason = "Headers must be unique, nonempty Excel text." };

        if (rows == null || rows.Length > MaxRows) return new { status = "invalidRows", maximum = MaxRows };
        int size = columns.Sum(column => column.Length);
        for (int row = 0; row < rows.Length; row++)
        {
            if (rows[row] == null || rows[row].Length != columns.Length)
                return new { status = "invalidRowLength", row = row + 1, expected = columns.Length };
            for (int column = 0; column < columns.Length; column++)
            {
                var cell = rows[row][column];
                if (!ValidCell(cell, out var length))
                    return new { status = "invalidCell", row = row + 1, column = column + 1 };
                size += length;
                if (size > MaxPayloadCharacters) return new { status = "payloadTooLarge", maximumCharacters = MaxPayloadCharacters };
            }
        }

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(documents)) return new { status = "exportDirectoryUnavailable" };
        string directory = Path.Combine(documents, "PIA-MCP", "Exports");
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture);

        try
        {
            string path = ExcelWorkbook.Save(directory, safeTitle, timestamp, columns, rows);
            return new { status = "exported", fileName = Path.GetFileName(path), path, rowCount = rows.Length, columnCount = columns.Length };
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return new { status = "exportFailed", reason = "Cannot create a new Excel file in the local Documents export folder." };
        }
    }

    private static string? FileTitle(string title)
    {
        title = (title ?? "").Trim();
        if (title.Length == 0 || title.Length > MaxTitleLength || !ValidText(title)) return null;
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
        string cleaned = new string(title.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        cleaned = cleaned.Trim(' ', '.', '_');
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static bool ValidText(string text)
    {
        if (text.Length > MaxTextLength) return false;
        try { XmlConvert.VerifyXmlChars(text); return true; }
        catch (XmlException) { return false; }
    }

    private static bool ValidCell(JsonElement cell, out int length)
    {
        length = 0;
        switch (cell.ValueKind)
        {
            case JsonValueKind.String:
                string text = cell.GetString()!;
                length = text.Length;
                return ValidText(text);
            case JsonValueKind.Number:
                length = cell.GetRawText().Length;
                return length <= 128 && cell.TryGetDouble(out var number) && !double.IsInfinity(number) && !double.IsNaN(number);
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                return true;
            default:
                return false;
        }
    }
}
