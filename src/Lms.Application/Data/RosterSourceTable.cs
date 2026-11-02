using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.VisualBasic.FileIO;

namespace Lms.Application.Data;

public static class RosterSourceTable
{
    public static IEnumerable<Dictionary<string, string>> Read(string path, bool allowQuoteSuffix = false)
    {
        var rows = Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? WorkbookRows(path) : CsvRows(path, allowQuoteSuffix);
        string[]? headers = null;
        var rowNumber = 0;
        foreach (var fields in rows)
        {
            rowNumber++;
            if (headers is null)
            {
                if (fields.Length < 2) continue;
                headers = fields.Select((value, index) =>
                    string.IsNullOrWhiteSpace(value) ? $"__empty_{index}" : value.Trim()).ToArray();
                if (headers.Distinct(StringComparer.Ordinal).Count() != headers.Length)
                    throw new InvalidDataException($"Duplicate source columns: {Path.GetFileName(path)}");
                continue;
            }
            if (fields.Length > headers.Length || (fields.Length < headers.Length && !path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Source column count differs: {Path.GetFileName(path)}, row {rowNumber}, expected {headers.Length}, got {fields.Length}.");
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < headers.Length; i++)
                row.Add(headers[i], i < fields.Length ? fields[i].Trim() : "");
            yield return row;
        }
        if (headers is null) throw new InvalidDataException($"Source has no header: {Path.GetFileName(path)}");
    }

    private static IEnumerable<string[]> CsvRows(string path, bool allowQuoteSuffix)
    {
        using var parser = new TextFieldParser(path, Encoding.UTF8);
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        parser.TrimWhiteSpace = false;
        while (!parser.EndOfData)
        {
            string[] fields;
            try { fields = parser.ReadFields() ?? throw new InvalidDataException($"Missing CSV row: {Path.GetFileName(path)}"); }
            catch (MalformedLineException ex)
            {
                if (!allowQuoteSuffix)
                    throw new InvalidDataException($"Malformed CSV in {Path.GetFileName(path)}, line {ex.LineNumber}.", ex);
                fields = ReadQuoteSuffixLine(parser.ErrorLine);
            }
            yield return fields;
        }
    }

    private static string[] ReadQuoteSuffixLine(string line)
    {
        var fields = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];
            if (character == '"' && (quoted || value.Length == 0))
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else quoted = !quoted;
            }
            else if (character == ',' && !quoted)
            {
                fields.Add(value.ToString());
                value.Clear();
            }
            else value.Append(character);
        }
        if (quoted) throw new InvalidDataException("Unterminated quote in roster CSV.");
        fields.Add(value.ToString());
        return fields.ToArray();
    }

    private static IEnumerable<string[]> WorkbookRows(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var strings = Array.Empty<string>();
        var shared = archive.GetEntry("xl/sharedStrings.xml");
        if (shared is not null)
        {
            using var stream = shared.Open();
            strings = XDocument.Load(stream).Descendants(ns + "si")
                .Select(item => string.Concat(item.Descendants(ns + "t").Select(text => text.Value))).ToArray();
        }
        using var sheet = (archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? throw new InvalidDataException("Workbook has no first worksheet.")).Open();
        var document = XDocument.Load(sheet);
        foreach (var row in document.Descendants(ns + "sheetData").Elements(ns + "row"))
        {
            var cells = new SortedDictionary<int, string>();
            foreach (var cell in row.Elements(ns + "c"))
            {
                var reference = (string?)cell.Attribute("r") ?? throw new InvalidDataException("Workbook cell has no reference.");
                var column = 0;
                foreach (var letter in reference.TakeWhile(char.IsLetter))
                    column = checked(column * 26 + letter - 'A' + 1);
                if (column == 0) throw new InvalidDataException("Invalid workbook column.");
                var type = (string?)cell.Attribute("t");
                if (type == "e" || cell.Element(ns + "f") is not null)
                    throw new InvalidDataException($"Error/formula cell in source workbook: {reference}");
                var value = cell.Element(ns + "v")?.Value ?? "";
                cells.Add(column - 1, type switch
                {
                    "s" => value.Length == 0 ? "" : strings[int.Parse(value, CultureInfo.InvariantCulture)],
                    "inlineStr" => string.Concat(cell.Descendants(ns + "t").Select(text => text.Value)),
                    _ => value
                });
            }
            if (cells.Count == 0) continue;
            var fields = new string[cells.Keys.Max() + 1];
            Array.Fill(fields, "");
            foreach (var cell in cells) fields[cell.Key] = cell.Value;
            yield return fields;
        }
    }
}
