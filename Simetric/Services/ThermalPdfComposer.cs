using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace Simetric.Services;

public static class ThermalPdfComposer
{
    private static readonly CultureInfo Cultura = new("es-EC");

    public static void ConfigurePage(PageDescriptor page, FormatoImpresionDocumento formato)
    {
        var pointsPerMillimeter = 2.83465f;
        page.ContinuousSize(formato.ObtenerAnchoMm() * pointsPerMillimeter);
        page.Margin(3.2f * pointsPerMillimeter);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(x => x.FontSize(formato == FormatoImpresionDocumento.Termica50mm ? 7.1f : 8.1f));
    }

    public static void ComposeTicket(IContainer container, ThermalTicketModel model)
    {
        container.Column(column =>
        {
            column.Spacing(4);

            if (!string.IsNullOrWhiteSpace(model.EmisorNombre))
            {
                column.Item().AlignCenter().Text(model.EmisorNombre.Trim())
                    .SemiBold()
                    .FontSize(10.5f)
                    .FontColor("#284E6C");
            }

            if (!string.IsNullOrWhiteSpace(model.EmisorSecundario))
            {
                column.Item().AlignCenter().Text(model.EmisorSecundario.Trim())
                    .FontSize(7.2f)
                    .FontColor(Colors.Grey.Darken1);
            }

            column.Item().PaddingTop(2).Element(Divider);

            column.Item().AlignCenter().Text(model.TituloDocumento)
                .SemiBold()
                .FontColor("#284E6C");

            column.Item().AlignCenter().Text(model.NumeroDocumento)
                .SemiBold()
                .FontSize(9.3f);

            if (!string.IsNullOrWhiteSpace(model.EstadoDocumento))
            {
                column.Item().AlignCenter().Text($"Estado: {model.EstadoDocumento}")
                    .FontSize(7.2f)
                    .FontColor(Colors.Grey.Darken1);
            }

            if (!string.IsNullOrWhiteSpace(model.FechaEmisionTexto))
            {
                column.Item().AlignCenter().Text($"Emision: {model.FechaEmisionTexto}")
                    .FontSize(7.2f)
                    .FontColor(Colors.Grey.Darken1);
            }

            if (!string.IsNullOrWhiteSpace(model.NumeroAutorizacion))
            {
                column.Item().Text($"Numero autorizacion: {model.NumeroAutorizacion}")
                    .FontSize(7.2f);
            }

            if (!string.IsNullOrWhiteSpace(model.AmbienteTexto))
            {
                column.Item().Text($"Ambiente: {model.AmbienteTexto}")
                    .FontSize(7.2f);
            }

            if (!string.IsNullOrWhiteSpace(model.TipoEmisionTexto))
            {
                column.Item().Text($"Tipo emision: {model.TipoEmisionTexto}")
                    .FontSize(7.2f);
            }

            if (!string.IsNullOrWhiteSpace(model.ClaveAcceso))
            {
                column.Item().PaddingTop(2).Text(model.EtiquetaAcceso)
                    .SemiBold()
                    .FontColor("#284E6C");
                column.Item().Text(model.ClaveAcceso.Trim())
                    .FontSize(6.7f);
            }

            if (model.Bloques.Any())
            {
                foreach (var bloque in model.Bloques)
                {
                    column.Item().PaddingTop(3).Element(Divider);
                    column.Item().Text(bloque.Titulo)
                        .SemiBold()
                        .FontColor("#284E6C");

                    foreach (var linea in bloque.Lineas.Where(x => !string.IsNullOrWhiteSpace(x.Valor)))
                    {
                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Text($"{linea.Etiqueta}:").SemiBold();
                            row.RelativeItem().AlignRight().Text(linea.Valor);
                        });
                    }
                }
            }

            if (model.Items.Any())
            {
                column.Item().PaddingTop(3).Element(Divider);
                column.Item().Text(model.TituloItems)
                    .SemiBold()
                    .FontColor("#284E6C");

                foreach (var item in model.Items)
                {
                    column.Item().PaddingTop(2).Column(itemColumn =>
                    {
                        itemColumn.Item().Text(item.Descripcion)
                            .SemiBold();

                        if (!string.IsNullOrWhiteSpace(item.DetalleSecundario))
                        {
                            itemColumn.Item().Text(item.DetalleSecundario)
                                .FontSize(6.9f)
                                .FontColor(Colors.Grey.Darken1);
                        }

                        itemColumn.Item().Row(row =>
                        {
                            row.RelativeItem().Text($"Cant: {item.CantidadTexto}");
                            row.RelativeItem().AlignRight().Text(item.TotalTexto).SemiBold();
                        });
                    });
                }
            }

            if (model.Totales.Any())
            {
                column.Item().PaddingTop(3).Element(Divider);

                for (var index = 0; index < model.Totales.Count; index++)
                {
                    var total = model.Totales[index];
                    var destacado = index == model.Totales.Count - 1;

                    column.Item().Row(row =>
                    {
                        var textoEtiqueta = row.RelativeItem().Text(total.Etiqueta)
                            .FontColor(destacado ? "#284E6C" : Colors.Black);
                        if (destacado)
                            textoEtiqueta.SemiBold();

                        var textoValor = row.RelativeItem().AlignRight().Text(total.Valor)
                            .FontColor(destacado ? "#284E6C" : Colors.Black);
                        if (destacado)
                            textoValor.SemiBold();
                    });
                }
            }

            if (!string.IsNullOrWhiteSpace(model.Notas))
            {
                column.Item().PaddingTop(3).Element(Divider);
                column.Item().Text("Notas")
                    .SemiBold()
                    .FontColor("#284E6C");
                column.Item().Text(model.Notas.Trim());
            }

            column.Item().PaddingTop(4).Element(Divider);
            column.Item().AlignCenter().Text($"Generado: {DateTime.Now.ToString("dd/MM/yyyy HH:mm", Cultura)}")
                .FontSize(6.8f)
                .FontColor(Colors.Grey.Darken1);
        });
    }

    private static IContainer Divider(IContainer container)
        => container.PaddingVertical(1).Height(1).Background("#C5D7E5");
}

public sealed class ThermalTicketModel
{
    public string TituloDocumento { get; init; } = string.Empty;
    public string NumeroDocumento { get; init; } = string.Empty;
    public string EstadoDocumento { get; init; } = string.Empty;
    public string FechaEmisionTexto { get; init; } = string.Empty;
    public string NumeroAutorizacion { get; init; } = string.Empty;
    public string EtiquetaAcceso { get; init; } = "Clave de acceso";
    public string ClaveAcceso { get; init; } = string.Empty;
    public string AmbienteTexto { get; init; } = string.Empty;
    public string TipoEmisionTexto { get; init; } = string.Empty;
    public string EmisorNombre { get; init; } = string.Empty;
    public string EmisorSecundario { get; init; } = string.Empty;
    public string TituloItems { get; init; } = "Detalle";
    public string Notas { get; init; } = string.Empty;
    public List<ThermalTicketBlock> Bloques { get; init; } = new();
    public List<ThermalTicketItem> Items { get; init; } = new();
    public List<ThermalTicketLine> Totales { get; init; } = new();
}

public sealed class ThermalTicketBlock
{
    public string Titulo { get; init; } = string.Empty;
    public List<ThermalTicketLine> Lineas { get; init; } = new();
}

public sealed class ThermalTicketItem
{
    public string Descripcion { get; init; } = string.Empty;
    public string DetalleSecundario { get; init; } = string.Empty;
    public string CantidadTexto { get; init; } = string.Empty;
    public string TotalTexto { get; init; } = string.Empty;
}

public sealed class ThermalTicketLine
{
    public string Etiqueta { get; init; } = string.Empty;
    public string Valor { get; init; } = string.Empty;
}
