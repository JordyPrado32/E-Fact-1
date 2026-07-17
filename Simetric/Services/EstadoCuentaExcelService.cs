using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using Simetric.DTOs;
using Simetric.ViewModels;

namespace Simetric.Services;

public interface IEstadoCuentaExcelService
{
    Task<ReporteArchivoDescargaDto> GenerarListadoAsync(IReadOnlyCollection<EstadoCuentaClienteResumenVM> items);
    Task<ReporteArchivoDescargaDto> GenerarDetalleAsync(EstadoCuentaDetalleVM detalle);
}

public sealed class EstadoCuentaExcelService : IEstadoCuentaExcelService
{
    private static readonly CultureInfo Cultura = new("es-EC");

    public Task<ReporteArchivoDescargaDto> GenerarListadoAsync(IReadOnlyCollection<EstadoCuentaClienteResumenVM> items)
    {
        if (items.Count == 0)
        {
            throw new InvalidOperationException("No hay registros para exportar.");
        }

        var workbook = ExcelWorkbook.Create(
            "Listado",
            BuildListadoRows(items));

        return Task.FromResult(new ReporteArchivoDescargaDto
        {
            FileName = $"estado-cuenta-clientes-{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Content = workbook
        });
    }

    public Task<ReporteArchivoDescargaDto> GenerarDetalleAsync(EstadoCuentaDetalleVM detalle)
    {
        var workbook = ExcelWorkbook.Create(
            ("Resumen", BuildResumenRows(detalle)),
            ("Facturas", BuildFacturasRows(detalle)),
            ("Abonos", BuildAbonosRows(detalle)),
            ("Historial", BuildMovimientosRows(detalle)));

        return Task.FromResult(new ReporteArchivoDescargaDto
        {
            FileName = $"estado-cuenta-{detalle.IdCliente}-{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Content = workbook
        });
    }

    private static List<ExcelRow> BuildListadoRows(IReadOnlyCollection<EstadoCuentaClienteResumenVM> items)
    {
        var rows = new List<ExcelRow>
        {
            ExcelRow.Title("Estado de cuenta por cliente", 8),
            ExcelRow.Subtitle($"Generado el {DateTime.Now.ToString("dd/MM/yyyy HH:mm", Cultura)}", 8),
            ExcelRow.Empty(),
            ExcelRow.Header("Cliente", "Identificacion", "Facturas", "Valor facturado", "Total abonos", "Saldo actual", "Estado", "Fecha emision")
        };

        foreach (var item in items)
        {
            rows.Add(new ExcelRow(
                Cell.Text(item.NombreCliente),
                Cell.Text(item.NumeroIdentificacion),
                Cell.Text(item.FacturasPendientes.ToString(Cultura)),
                Cell.Currency(item.ValorFacturado),
                Cell.Currency(item.TotalAbonos),
                Cell.Currency(item.SaldoActual),
                Cell.Text(ResolverEstado(item)),
                Cell.Date(item.FechaEmision)));
        }

        return rows;
    }

    private static List<ExcelRow> BuildResumenRows(EstadoCuentaDetalleVM detalle)
    {
        return
        [
            ExcelRow.Title("Estado de cuenta por cliente", 4),
            ExcelRow.Subtitle(detalle.NombreCliente, 4),
            ExcelRow.Empty(),
            ExcelRow.LabelValue("Cliente", detalle.NombreCliente, "RUC/CI", detalle.NumeroIdentificacion),
            ExcelRow.LabelValue("Saldo total", detalle.SaldoTotal, "Facturas pendientes", detalle.FacturasPendientes),
            ExcelRow.LabelValue("Ultimo abono", detalle.MontoUltimoAbono, "Fecha ultimo abono", detalle.FechaUltimoAbono),
            ExcelRow.LabelValue("Dias vencidos", detalle.DiasVencidosMaximos, "Saldo a favor", detalle.SaldoAFavorDisponible)
        ];
    }

    private static List<ExcelRow> BuildFacturasRows(EstadoCuentaDetalleVM detalle)
    {
        var rows = new List<ExcelRow>
        {
            ExcelRow.Header("Factura", "Fecha emision", "Fecha vencimiento", "Valor facturado", "Total abonos", "Saldo actual", "Estado")
        };

        foreach (var item in detalle.Facturas)
        {
            rows.Add(new ExcelRow(
                Cell.Text(item.NumeroFactura),
                Cell.Date(item.FechaEmision),
                Cell.Date(item.FechaVencimiento),
                Cell.Currency(item.ValorFacturado),
                Cell.Currency(item.TotalAbonos),
                Cell.Currency(item.SaldoActual),
                Cell.Text(item.Estado)));
        }

        return rows;
    }

    private static List<ExcelRow> BuildAbonosRows(EstadoCuentaDetalleVM detalle)
    {
        var rows = new List<ExcelRow>
        {
            ExcelRow.Header("Fecha pago", "Factura", "Concepto", "Forma pago", "Monto", "Observacion")
        };

        foreach (var item in detalle.Abonos)
        {
            rows.Add(new ExcelRow(
                Cell.Date(item.FechaPago),
                Cell.Text(item.NumeroFactura),
                Cell.Text(item.Concepto),
                Cell.Text(item.FormaPago),
                Cell.Currency(item.Monto),
                Cell.Text(item.Observacion)));
        }

        return rows;
    }

    private static List<ExcelRow> BuildMovimientosRows(EstadoCuentaDetalleVM detalle)
    {
        var rows = new List<ExcelRow>
        {
            ExcelRow.Header("Fecha", "Documento", "Concepto", "Debito", "Credito", "Saldo")
        };

        foreach (var item in detalle.Movimientos)
        {
            rows.Add(new ExcelRow(
                Cell.Date(item.Fecha),
                Cell.Text(item.Documento),
                Cell.Text(item.Concepto),
                Cell.Currency(item.Debito),
                Cell.Currency(item.Credito),
                Cell.Currency(item.Saldo)));
        }

        return rows;
    }

    private static string ResolverEstado(EstadoCuentaClienteResumenVM item)
    {
        if (item.SaldoActual <= 0)
        {
            return "Pagado";
        }

        if (item.FechaVencimiento.HasValue && item.FechaVencimiento.Value.Date < DateTime.Today)
        {
            return "Vencido";
        }

        return "Pendiente";
    }

    private sealed class ExcelWorkbook
    {
        public static byte[] Create(string sheetName, List<ExcelRow> rows) =>
            Create((sheetName, rows));

        public static byte[] Create(params (string Name, List<ExcelRow> Rows)[] sheets)
        {
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var sharedStrings = BuildSharedStrings(sheets);

                AddEntry(archive, "[Content_Types].xml", BuildContentTypes(sheets.Length));
                AddEntry(archive, "_rels/.rels", BuildRootRels());
                AddEntry(archive, "xl/workbook.xml", BuildWorkbook(sheets));
                AddEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRels(sheets.Length, sharedStrings.Count > 0));
                AddEntry(archive, "xl/styles.xml", BuildStyles());

                if (sharedStrings.Count > 0)
                {
                    AddEntry(archive, "xl/sharedStrings.xml", BuildSharedStringsXml(sharedStrings));
                }

                for (var i = 0; i < sheets.Length; i++)
                {
                    AddEntry(archive, $"xl/worksheets/sheet{i + 1}.xml", BuildSheetXml(sheets[i].Rows, sharedStrings.Lookup));
                }
            }

            return stream.ToArray();
        }

        private static SharedStringStore BuildSharedStrings((string Name, List<ExcelRow> Rows)[] sheets)
        {
            var values = new List<string>();
            var lookup = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var (_, rows) in sheets)
            {
                foreach (var row in rows)
                {
                    foreach (var cell in row.Cells.Where(c => c.Type == CellType.Text))
                    {
                        var value = cell.Value?.ToString() ?? string.Empty;
                        if (!lookup.ContainsKey(value))
                        {
                            lookup[value] = values.Count;
                            values.Add(value);
                        }
                    }
                }
            }

            return new SharedStringStore(values, lookup);
        }

        private static string BuildContentTypes(int sheetCount)
        {
            var sb = new StringBuilder();
            sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
            sb.Append("""<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""");
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

        private static string BuildWorkbook((string Name, List<ExcelRow> Rows)[] sheets)
        {
            var sb = new StringBuilder();
            sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>""");
            for (var i = 0; i < sheets.Length; i++)
            {
                sb.Append($"""<sheet name="{EscapeAttribute(NormalizeSheetName(sheets[i].Name))}" sheetId="{i + 1}" r:id="rId{i + 1}"/>""");
            }
            sb.Append("</sheets></workbook>");
            return sb.ToString();
        }

        private static string BuildWorkbookRels(int sheetCount, bool includeSharedStrings)
        {
            var sb = new StringBuilder();
            sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""");
            for (var i = 1; i <= sheetCount; i++)
            {
                sb.Append($"""<Relationship Id="rId{i}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i}.xml"/>""");
            }

            sb.Append($"""<Relationship Id="rId{sheetCount + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>""");
            if (includeSharedStrings)
            {
                sb.Append($"""<Relationship Id="rId{sheetCount + 2}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/>""");
            }

            sb.Append("</Relationships>");
            return sb.ToString();
        }

        private static string BuildStyles() =>
            """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="3"><font><sz val="10"/><name val="Segoe UI"/><color rgb="FF17324A"/></font><font><b/><sz val="16"/><name val="Segoe UI"/><color rgb="FF0B5B97"/></font><font><b/><sz val="10"/><name val="Segoe UI"/><color rgb="FFFFFFFF"/></font></fonts><fills count="4"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFF4F9FD"/><bgColor indexed="64"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FF0B5B97"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style="thin"><color rgb="FFD7E3F0"/></left><right style="thin"><color rgb="FFD7E3F0"/></right><top style="thin"><color rgb="FFD7E3F0"/></top><bottom style="thin"><color rgb="FFD7E3F0"/></bottom><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="6"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/><xf numFmtId="0" fontId="0" fillId="2" borderId="0" xfId="0" applyFill="1"/><xf numFmtId="0" fontId="2" fillId="3" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1"/><xf numFmtId="4" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1"/><xf numFmtId="14" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>""";

        private static string BuildSharedStringsXml(SharedStringStore store)
        {
            var sb = new StringBuilder();
            sb.Append($"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="{store.Values.Count}" uniqueCount="{store.Values.Count}">""");
            foreach (var value in store.Values)
            {
                sb.Append($"""<si><t xml:space="preserve">{EscapeText(value)}</t></si>""");
            }
            sb.Append("</sst>");
            return sb.ToString();
        }

        private static string BuildSheetXml(List<ExcelRow> rows, Dictionary<string, int> sharedStrings)
        {
            var merges = new List<string>();
            var sb = new StringBuilder();
            sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                sb.Append($"""<row r="{rowIndex + 1}">""");

                for (var colIndex = 0; colIndex < row.Cells.Length; colIndex++)
                {
                    var cell = row.Cells[colIndex];
                    var reference = $"{GetColumnName(colIndex + 1)}{rowIndex + 1}";
                    AppendCell(sb, reference, cell, sharedStrings);

                    if (cell.MergeAcross > 0)
                    {
                        merges.Add($"{reference}:{GetColumnName(colIndex + 1 + cell.MergeAcross)}{rowIndex + 1}");
                    }
                }

                sb.Append("</row>");
            }

            sb.Append("</sheetData>");
            if (merges.Count > 0)
            {
                sb.Append($"""<mergeCells count="{merges.Count}">""");
                foreach (var merge in merges)
                {
                    sb.Append($"""<mergeCell ref="{merge}"/>""");
                }
                sb.Append("</mergeCells>");
            }

            sb.Append("</worksheet>");
            return sb.ToString();
        }

        private static void AppendCell(StringBuilder sb, string reference, Cell cell, Dictionary<string, int> sharedStrings)
        {
            var style = cell.StyleIndex.ToString(CultureInfo.InvariantCulture);
            switch (cell.Type)
            {
                case CellType.Number:
                    sb.Append($"""<c r="{reference}" s="{style}"><v>{Convert.ToDecimal(cell.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}</v></c>""");
                    break;
                case CellType.Date:
                    if (cell.Value is DateTime dt)
                    {
                        sb.Append($"""<c r="{reference}" s="{style}"><v>{ToExcelDate(dt).ToString(CultureInfo.InvariantCulture)}</v></c>""");
                    }
                    else
                    {
                        sb.Append($"""<c r="{reference}" t="s" s="0"><v>{sharedStrings[string.Empty]}</v></c>""");
                    }
                    break;
                default:
                    var value = cell.Value?.ToString() ?? string.Empty;
                    sb.Append($"""<c r="{reference}" t="s" s="{style}"><v>{sharedStrings[value]}</v></c>""");
                    break;
            }
        }

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

        private static double ToExcelDate(DateTime value) =>
            value.Date.Subtract(new DateTime(1899, 12, 30)).TotalDays;

        private static string NormalizeSheetName(string value)
        {
            var invalid = new[] { '\\', '/', '?', '*', '[', ']', ':' };
            var normalized = new string(value.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                normalized = "Hoja";
            }

            return normalized.Length > 31 ? normalized[..31] : normalized;
        }

        private static string EscapeText(string value) => SecurityElement.Escape(value) ?? string.Empty;
        private static string EscapeAttribute(string value) => SecurityElement.Escape(value) ?? string.Empty;
    }

    private sealed record SharedStringStore(List<string> Values, Dictionary<string, int> Lookup)
    {
        public int Count => Values.Count;
    }

    private sealed record ExcelRow(params Cell[] Cells)
    {
        public static ExcelRow Empty() => new(Cell.Text(string.Empty));
        public static ExcelRow Title(string value, int mergeAcross) => new(Cell.Text(value, 1, mergeAcross));
        public static ExcelRow Subtitle(string value, int mergeAcross) => new(Cell.Text(value, 2, mergeAcross));
        public static ExcelRow Header(params string[] values) => new(values.Select(v => Cell.Text(v, 3)).ToArray());

        public static ExcelRow LabelValue(string label1, object? value1, string label2, object? value2) => new(
            Cell.Text(label1, 3),
            ToCell(value1),
            Cell.Text(label2, 3),
            ToCell(value2));

        private static Cell ToCell(object? value)
        {
            if (value is decimal dec)
                return Cell.Currency(dec);

            if (value is int number)
                return Cell.Number(number);

            if (value is DateTime date)
                return Cell.Date(date);

            return Cell.Text(value?.ToString() ?? string.Empty);
        }
    }

    private sealed record Cell(CellType Type, object? Value, int StyleIndex, int MergeAcross = 0)
    {
        public static Cell Text(string value, int styleIndex = 0, int mergeAcross = 0) => new(CellType.Text, value, styleIndex, mergeAcross);
        public static Cell Number(decimal value, int styleIndex = 4) => new(CellType.Number, value, styleIndex);
        public static Cell Number(int value, int styleIndex = 0) => new(CellType.Number, value, styleIndex);
        public static Cell Currency(decimal value) => new(CellType.Number, value, 4);
        public static Cell Date(DateTime? value) => value.HasValue ? new(CellType.Date, value.Value, 5) : Text(string.Empty);
    }

    private enum CellType
    {
        Text,
        Number,
        Date
    }
}
