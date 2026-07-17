using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using Simetric.DTOs;

namespace Simetric.Services;

public interface IFacturasExcelService
{
    Task<ReporteArchivoDescargaDto> GenerarAsync(IReadOnlyCollection<FacturaListDto> items);
}

public sealed class FacturasExcelService : IFacturasExcelService
{
    public Task<ReporteArchivoDescargaDto> GenerarAsync(IReadOnlyCollection<FacturaListDto> items)
    {
        if (items.Count == 0)
        {
            throw new InvalidOperationException("No hay facturas para exportar.");
        }

        var workbook = ExcelWorkbook.Create(items);
        return Task.FromResult(new ReporteArchivoDescargaDto
        {
            FileName = $"facturas_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Content = workbook
        });
    }

    private static class ExcelWorkbook
    {
        public static byte[] Create(IReadOnlyCollection<FacturaListDto> items)
        {
            var rows = BuildRows(items);
            var sharedStrings = BuildSharedStrings(rows);

            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                AddEntry(archive, "[Content_Types].xml", ContentTypes());
                AddEntry(archive, "_rels/.rels", RootRels());
                AddEntry(archive, "xl/workbook.xml", Workbook());
                AddEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRels());
                AddEntry(archive, "xl/styles.xml", Styles());
                AddEntry(archive, "xl/sharedStrings.xml", SharedStringsXml(sharedStrings));
                AddEntry(archive, "xl/worksheets/sheet1.xml", SheetXml(rows, sharedStrings));
            }

            return stream.ToArray();
        }

        private static List<Row> BuildRows(IReadOnlyCollection<FacturaListDto> items)
        {
            var rows = new List<Row>
            {
                new(
                    Cell.Text("Fecha", 1),
                    Cell.Text("Numero", 1),
                    Cell.Text("Cliente", 1),
                    Cell.Text("Identificacion", 1),
                    Cell.Text("Estado SRI", 1),
                    Cell.Text("Total", 1))
            };

            foreach (var item in items)
            {
                rows.Add(new Row(
                    Cell.Date(item.FechaEmision),
                    Cell.Text(item.NumeroCompleto),
                    Cell.Text(item.Cliente ?? string.Empty),
                    Cell.Text(item.IdentificacionCliente ?? string.Empty),
                    Cell.Text((item.Autorizado ?? false) ? "AUTORIZADO" : "PENDIENTE"),
                    Cell.Currency(item.Total ?? 0m)));
            }

            return rows;
        }

        private static Dictionary<string, int> BuildSharedStrings(IEnumerable<Row> rows)
        {
            var lookup = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                foreach (var cell in row.Cells)
                {
                    if (cell.Kind != CellKind.Text)
                        continue;

                    var value = cell.Value?.ToString() ?? string.Empty;
                    if (!lookup.ContainsKey(value))
                    {
                        lookup[value] = lookup.Count;
                    }
                }
            }

            return lookup;
        }

        private static string SheetXml(IReadOnlyList<Row> rows, Dictionary<string, int> sharedStrings)
        {
            var sb = new StringBuilder();
            sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                sb.Append($"""<row r="{rowIndex + 1}">""");
                var row = rows[rowIndex];
                for (var colIndex = 0; colIndex < row.Cells.Length; colIndex++)
                {
                    var reference = $"{ColumnName(colIndex + 1)}{rowIndex + 1}";
                    AppendCell(sb, reference, row.Cells[colIndex], sharedStrings);
                }
                sb.Append("</row>");
            }

            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        private static void AppendCell(StringBuilder sb, string reference, Cell cell, Dictionary<string, int> sharedStrings)
        {
            switch (cell.Kind)
            {
                case CellKind.Date:
                    if (cell.Value is DateTime date)
                    {
                        sb.Append($"""<c r="{reference}" s="2"><v>{ToExcelDate(date).ToString(CultureInfo.InvariantCulture)}</v></c>""");
                    }
                    else
                    {
                        sb.Append($"""<c r="{reference}" t="s"><v>{sharedStrings[string.Empty]}</v></c>""");
                    }
                    break;
                case CellKind.Number:
                    sb.Append($"""<c r="{reference}" s="3"><v>{Convert.ToDecimal(cell.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}</v></c>""");
                    break;
                default:
                    var value = cell.Value?.ToString() ?? string.Empty;
                    sb.Append($"""<c r="{reference}" t="s" s="{cell.StyleIndex}"><v>{sharedStrings[value]}</v></c>""");
                    break;
            }
        }

        private static string SharedStringsXml(Dictionary<string, int> sharedStrings)
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

        private static string ContentTypes() =>
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/><Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/></Types>""";

        private static string RootRels() =>
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""";

        private static string Workbook() =>
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Facturas" sheetId="1" r:id="rId1"/></sheets></workbook>""";

        private static string WorkbookRels() =>
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/></Relationships>""";

        private static string Styles() =>
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="10"/><name val="Segoe UI"/><color rgb="FF17324A"/></font><font><b/><sz val="10"/><name val="Segoe UI"/><color rgb="FFFFFFFF"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF0B5B97"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style="thin"><color rgb="FFD7E3F0"/></left><right style="thin"><color rgb="FFD7E3F0"/></right><top style="thin"><color rgb="FFD7E3F0"/></top><bottom style="thin"><color rgb="FFD7E3F0"/></bottom><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="4"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1"/><xf numFmtId="14" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1"/><xf numFmtId="4" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1"/></cellXfs></styleSheet>""";

        private static void AddEntry(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        private static string ColumnName(int index)
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

        private static double ToExcelDate(DateTime value) =>
            value.Date.Subtract(new DateTime(1899, 12, 30)).TotalDays;

        private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;
    }

    private sealed record Row(params Cell[] Cells);

    private sealed record Cell(CellKind Kind, object? Value, int StyleIndex = 0)
    {
        public static Cell Text(string value, int styleIndex = 0) => new(CellKind.Text, value, styleIndex);
        public static Cell Date(DateTime? value) => value.HasValue ? new(CellKind.Date, value.Value, 2) : Text(string.Empty);
        public static Cell Currency(decimal value) => new(CellKind.Number, value, 3);
    }

    private enum CellKind
    {
        Text,
        Date,
        Number
    }
}
