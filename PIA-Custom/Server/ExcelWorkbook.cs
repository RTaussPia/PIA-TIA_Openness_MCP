using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace Pia.Custom;

internal static class ExcelWorkbook
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
    private static readonly Regex ExcelEscape = new Regex(@"_x[0-9a-fA-F]{4}_", RegexOptions.CultureInvariant);

    internal static string Save(string directory, string title, string timestamp, string[] columns, JsonElement[][] rows)
    {
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var package = new ZipArchive(file, ZipArchiveMode.Create))
            {
                WritePackageMetadata(package);
                WriteSheet(package, columns, rows);
            }

            for (int sequence = 1; sequence <= 999; sequence++)
            {
                string suffix = sequence == 1 ? "" : "-" + sequence.ToString(CultureInfo.InvariantCulture);
                string path = Path.Combine(directory, $"{title}{suffix}_{timestamp}.xlsx");
                try
                {
                    File.Move(temporary, path);
                    return path;
                }
                catch (IOException) when (File.Exists(path)) { }
            }
            throw new IOException("No available file name for this export.");
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void WritePackageMetadata(ZipArchive package)
    {
        WritePart(package, "[Content_Types].xml",
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "</Types>");
        WritePart(package, "_rels/.rels",
            $"<Relationships xmlns=\"{PackageRelationships}\"><Relationship Id=\"rId1\" Type=\"{OfficeRelationships}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        WritePart(package, "xl/workbook.xml",
            $"<workbook xmlns=\"{SpreadsheetNamespace}\" xmlns:r=\"{OfficeRelationships}\"><sheets><sheet name=\"Export\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        WritePart(package, "xl/_rels/workbook.xml.rels",
            $"<Relationships xmlns=\"{PackageRelationships}\"><Relationship Id=\"rId1\" Type=\"{OfficeRelationships}/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
    }

    private static void WritePart(ZipArchive package, string name, string contents)
    {
        using var writer = new StreamWriter(package.CreateEntry(name).Open(), Utf8);
        writer.Write(contents);
    }

    private static void WriteSheet(ZipArchive package, string[] columns, JsonElement[][] rows)
    {
        using var stream = package.CreateEntry("xl/worksheets/sheet1.xml").Open();
        using var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = Utf8, NewLineHandling = NewLineHandling.Entitize });
        var letters = new string[columns.Length];
        xml.WriteStartElement("worksheet", SpreadsheetNamespace);
        xml.WriteStartElement("sheetData", SpreadsheetNamespace);
        StartRow(xml, "1");
        for (int column = 0; column < columns.Length; column++)
        {
            letters[column] = ColumnLetters(column + 1);
            WriteTextCell(xml, letters[column] + "1", columns[column]);
        }
        xml.WriteEndElement();

        for (int row = 0; row < rows.Length; row++)
        {
            string number = (row + 2).ToString(CultureInfo.InvariantCulture);
            StartRow(xml, number);
            for (int column = 0; column < columns.Length; column++)
                WriteCell(xml, letters[column] + number, rows[row][column]);
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    private static void StartRow(XmlWriter xml, string number)
    {
        xml.WriteStartElement("row", SpreadsheetNamespace);
        xml.WriteAttributeString("r", number);
    }

    private static string ColumnLetters(int number)
    {
        string result = "";
        while (number > 0)
        {
            number--;
            result = (char)('A' + number % 26) + result;
            number /= 26;
        }
        return result;
    }

    private static void WriteCell(XmlWriter xml, string address, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                WriteTextCell(xml, address, value.GetString()!);
                break;
            case JsonValueKind.Number:
                StartCell(xml, address, "n");
                xml.WriteElementString("v", SpreadsheetNamespace, value.GetRawText());
                xml.WriteEndElement();
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                StartCell(xml, address, "b");
                xml.WriteElementString("v", SpreadsheetNamespace, value.GetBoolean() ? "1" : "0");
                xml.WriteEndElement();
                break;
        }
    }

    private static void StartCell(XmlWriter xml, string address, string type)
    {
        xml.WriteStartElement("c", SpreadsheetNamespace);
        xml.WriteAttributeString("r", address);
        xml.WriteAttributeString("t", type);
    }

    private static void WriteTextCell(XmlWriter xml, string address, string value)
    {
        StartCell(xml, address, "inlineStr");
        xml.WriteStartElement("is", SpreadsheetNamespace);
        xml.WriteStartElement("t", SpreadsheetNamespace);
        xml.WriteAttributeString("xml", "space", null, "preserve");
        xml.WriteString(ExcelEscape.Replace(value, match => "_x005F_" + match.Value.Substring(1)));
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
    }
}
