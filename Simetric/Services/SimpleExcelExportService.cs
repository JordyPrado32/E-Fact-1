using System.IO.Compression;
using System.Security;
using System.Text;
using Simetric.DTOs;

namespace Simetric.Services;

public interface ISimpleExcelExportService
{
    ReporteArchivoDescargaDto Create(string fileName, params ExcelSheetData[] sheets);
}

public sealed record ExcelSheetData(
    string Name,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows);

public sealed class SimpleExcelExportService : ISimpleExcelExportService
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public ReporteArchivoDescargaDto Create(string fileName, params ExcelSheetData[] sheets)
    {
        if (sheets.Length == 0)
        {
            throw new InvalidOperationException("No hay hojas para exportar.");
        }

        var content = BuildWorkbook(sheets);

        return new ReporteArchivoDescargaDto
        {
            FileName = fileName,
            ContentType = XlsxContentType,
            Content = content
        };
    }

    private static byte[] BuildWorkbook(IReadOnlyList<ExcelSheetData> sheets)
    {
        var sharedStrings = BuildSharedStrings(sheets);

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", BuildContentTypes(sheets.Count));
            AddEntry(archive, "_rels/.rels", BuildRootRels());
            AddEntry(archive, "xl/workbook.xml", BuildWorkbookXml(sheets));
            AddEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRels(sheets.Count));
            AddEntry(archive, "xl/styles.xml", BuildStyles());
            AddEntry(archive, "xl/sharedStrings.xml", BuildSharedStringsXml(sharedStrings));

            for (var i = 0; i < sheets.Count; i++)
            {
                AddEntry(
                    archive,
                    $"xl/worksheets/sheet{i + 1}.xml",
                    BuildSheetXml(sheets[i], sharedStrings));
            }
        }

        return stream.ToArray();
    }

    private static Dictionary<string, int> BuildSharedStrings(IReadOnlyList<ExcelSheetData> sheets)
    {
        var lookup = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var sheet in sheets)
        {
            foreach (var header in sheet.Headers)
            {
                AddSharedString(lookup, header);
            }

            foreach (var row in sheet.Rows)
            {
                foreach (var cell in row)
                {
                    AddSharedString(lookup, cell);
                }
            }
        }

        return lookup;
    }

    private static void AddSharedString(Dictionary<string, int> lookup, string? value)
    {
        var safeValue = value ?? string.Empty;
        if (!lookup.ContainsKey(safeValue))
        {
            lookup[safeValue] = lookup.Count;
        }
    }

    private static string BuildSheetXml(ExcelSheetData sheet, Dictionary<string, int> sharedStrings)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");

        var rowIndex = 1;
        sb.Append($"""<row r="{rowIndex}">""");
        for (var colIndex = 0; colIndex < sheet.Headers.Count; colIndex++)
        {
            AppendSharedStringCell(sb, rowIndex, colIndex + 1, sheet.Headers[colIndex], sharedStrings, 1);
        }
        sb.Append("</row>");

        foreach (var row in sheet.Rows)
        {
            rowIndex++;
            sb.Append($"""<row r="{rowIndex}">""");
            for (var colIndex = 0; colIndex < row.Count; colIndex++)
            {
                AppendSharedStringCell(sb, rowIndex, colIndex + 1, row[colIndex], sharedStrings, 0);
            }
            sb.Append("</row>");
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void AppendSharedStringCell(
        StringBuilder sb,
        int rowIndex,
        int columnIndex,
        string? value,
        Dictionary<string, int> sharedStrings,
        int styleIndex)
    {
        var reference = $"{GetColumnName(columnIndex)}{rowIndex}";
        var key = value ?? string.Empty;
        sb.Append($"""<c r="{reference}" t="s" s="{styleIndex}"><v>{sharedStrings[key]}</v></c>""");
    }

    private static string BuildSharedStringsXml(Dictionary<string, int> sharedStrings)
    {
        var ordered = sharedStrings.OrderBy(x => x.Value).Select(x => x.Key).ToList();
        var sb = new StringBuilder();
        sb.Append($"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="{ordered.Count}" uniqueCount="{ordered.Count}">""");
        foreach (var value in ordered)
        {
            sb.Append($"""<si><t xml:space="preserve">{Escape(value)}</t></si>""");
        }
        sb.Append("</sst>");
        return sb.ToString();
    }

    private static string BuildContentTypes(int sheetCount)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""");
        sb.Append("""<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>""");
        sb.Append("""<Default Extension="xml" ContentType="application/xml"/>""");
        sb.Append("""<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>""");
        sb.Append("""<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>""");
        sb.Append("""<Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>""");
        for (var i = 1; i <= sheetCount; i++)
        {
            sb.Append($"""<Override PartName="/xl/worksheets/sheet{i}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""");
        }
        sb.Append("</Types>");
        return sb.ToString();
    }

    private static string BuildRootRels() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""";

    private static string BuildWorkbookXml(IReadOnlyList<ExcelSheetData> sheets)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>""");
        for (var i = 0; i < sheets.Count; i++)
        {
            sb.Append($"""<sheet name="{EscapeAttribute(NormalizeSheetName(sheets[i].Name))}" sheetId="{i + 1}" r:id="rId{i + 1}"/>""");
        }
        sb.Append("</sheets></workbook>");
        return sb.ToString();
    }

    private static string BuildWorkbookRels(int sheetCount)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""");
        for (var i = 1; i <= sheetCount; i++)
        {
            sb.Append($"""<Relationship Id="rId{i}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i}.xml"/>""");
        }
        sb.Append($"""<Relationship Id="rId{sheetCount + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>""");
        sb.Append($"""<Relationship Id="rId{sheetCount + 2}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/>""");
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private static string BuildStyles() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="10"/><name val="Segoe UI"/><color rgb="FF17324A"/></font><font><b/><sz val="10"/><name val="Segoe UI"/><color rgb="FFFFFFFF"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF0B5B97"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style="thin"><color rgb="FFD7E3F0"/></left><right style="thin"><color rgb="FFD7E3F0"/></right><top style="thin"><color rgb="FFD7E3F0"/></top><bottom style="thin"><color rgb="FFD7E3F0"/></bottom><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1"/><xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1"/></cellXfs></styleSheet>""";

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string GetColumnName(int index)
    {
        var name = string.Empty;
        while (index > 0)
        {
            index--;
            name = (char)('A' + (index % 26)) + name;
            index /= 26;
        }

        return name;
    }

    private static string NormalizeSheetName(string name)
    {
        var invalidChars = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var safe = string.IsNullOrWhiteSpace(name) ? "Reporte" : name.Trim();
        foreach (var invalidChar in invalidChars)
        {
            safe = safe.Replace(invalidChar, '-');
        }

        return safe.Length > 31 ? safe[..31] : safe;
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private static string EscapeAttribute(string value) =>
        (SecurityElement.Escape(value) ?? string.Empty).Replace("\"", "&quot;", StringComparison.Ordinal);
}
