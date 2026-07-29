using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Simetric.Services;

public static class PdfInformacionAdicional
{
    public const string Proveedor = "Numerica Software (1793233799001)";
    public const string LineaProveedor = $"Proveedor:  {Proveedor}";

    public static void Componer(IContainer container, float fuenteBase, float fuenteTitulo)
    {
        container.Border(1)
            .BorderColor(Colors.Blue.Lighten4)
            .Background(Colors.White)
            .Padding(7)
            .Column(column =>
            {
                column.Spacing(2);
                column.Item().Text("Información adicional")
                    .FontSize(fuenteTitulo)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken3);
                column.Item().Text(LineaProveedor)
                    .FontSize(fuenteBase);
            });
    }
}
