using Simetric.Services;

namespace Simetric.DTOs;

public static class ReporteComprobantesTipos
{
    public const string Factura = "FACTURA";
    public const string NotaCredito = "NOTA_CREDITO";
    public const string NotaDebito = "NOTA_DEBITO";
    public const string GuiaRemision = "GUIA_REMISION";
    public const string Retencion = "RETENCION";
    public const string LiquidacionCompra = "LIQUIDACION_COMPRA";
}

public static class ReporteComprobantesCategoriaDetalle
{
    public const string Todos = "TODOS";
    public const string Producto = "PRODUCTO";
    public const string Servicio = "SERVICIO";
}

public sealed class ReporteComprobantesCargaDto
{
    public string NombreEmisor { get; set; } = string.Empty;
    public string RucEmisor { get; set; } = string.Empty;
    public string NombreUsuario { get; set; } = string.Empty;
    public DateTime GeneradoEn { get; set; } = DateTime.Now;
    public List<ReporteComprobanteItemDto> Items { get; set; } = new();
    public List<string> ClientesDisponibles { get; set; } = new();
    public List<string> ProductosDisponibles { get; set; } = new();
    public List<string> EstadosDisponibles { get; set; } = new();
}

public sealed class ReporteComprobanteItemDto
{
    public int DocumentoId { get; set; }
    public string TipoDocumento { get; set; } = string.Empty;
    public string TipoDocumentoCodigo { get; set; } = string.Empty;
    public DateTime? FechaEmision { get; set; }
    public string NumeroDocumento { get; set; } = string.Empty;
    public string TerceroNombre { get; set; } = string.Empty;
    public string TerceroIdentificacion { get; set; } = string.Empty;
    public string TerceroRol { get; set; } = string.Empty;
    public string EstadoDocumento { get; set; } = string.Empty;
    public decimal BaseImponible { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
    public string DocumentoRelacionado { get; set; } = string.Empty;
    public string ClaveAcceso { get; set; } = string.Empty;
    public string NumeroAutorizacion { get; set; } = string.Empty;
    public bool EstaAutorizado { get; set; }
    public string XmlUrl { get; set; } = string.Empty;
    public string PdfUrl { get; set; } = string.Empty;
    public List<string> CodigosRelacionados { get; set; } = new();
    public List<string> ProductosRelacionados { get; set; } = new();
    public decimal BaseSinIva { get; set; }
    public decimal BaseConIva { get; set; }
    public bool TieneProducto { get; set; }
    public bool TieneServicio { get; set; }

    public string ResumenProductos
    {
        get
        {
            if (ProductosRelacionados.Count == 0)
            {
                return "Sin detalle asociado";
            }

            var visibles = ProductosRelacionados.Take(3).ToList();
            var resumen = string.Join(", ", visibles);

            return ProductosRelacionados.Count > visibles.Count
                ? $"{resumen} y {ProductosRelacionados.Count - visibles.Count} mas"
                : resumen;
        }
    }

    public string CategoriaDetalle
    {
        get
        {
            if (TieneProducto && TieneServicio)
            {
                return "Mixto";
            }

            if (TieneServicio)
            {
                return "Servicio";
            }

            if (TieneProducto)
            {
                return "Producto";
            }

            return "Sin clasificar";
        }
    }

    public string ResumenCodigos
    {
        get
        {
            if (CodigosRelacionados.Count == 0)
            {
                return "Sin codigo";
            }

            var visibles = CodigosRelacionados.Take(3).ToList();
            var resumen = string.Join(", ", visibles);

            return CodigosRelacionados.Count > visibles.Count
                ? $"{resumen} y {CodigosRelacionados.Count - visibles.Count} mas"
                : resumen;
        }
    }
}

public sealed class ReporteComprobantesFiltroDto
{
    public string TipoDocumento { get; set; } = ReporteComprobantesCategoriaDetalle.Todos;
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public string Producto { get; set; } = string.Empty;
    public string CategoriaDetalle { get; set; } = ReporteComprobantesCategoriaDetalle.Todos;
    public string EstadoDocumento { get; set; } = ReporteComprobantesCategoriaDetalle.Todos;
    public string Autorizacion { get; set; } = DocumentoAutorizacionHelper.FiltroTodos;
}

public sealed class ReporteComprobantesPdfRequest
{
    public string NombreEmisor { get; set; } = string.Empty;
    public string RucEmisor { get; set; } = string.Empty;
    public string NombreUsuario { get; set; } = string.Empty;
    public DateTime GeneradoEn { get; set; } = DateTime.Now;
    public ReporteComprobantesFiltroDto Filtros { get; set; } = new();
    public List<ReporteComprobanteItemDto> Items { get; set; } = new();
}

public sealed class ReporteArchivoDescargaDto
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}
