namespace Simetric.Models.EDeclara;

public sealed class EDeclaraCompraDocumento
{
    public long Id { get; set; }
    public int IdContribuyente { get; set; }
    public int Anio { get; set; }
    public int Periodo { get; set; }
    public int TipoDocumento { get; set; } = 1;
    public int? TipoFacturacion { get; set; }
    public string Serie { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public DateTime FechaEmision { get; set; } = DateTime.Today;
    public string Ruc { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string IdentificacionComprador { get; set; } = string.Empty;
    public string Concepto { get; set; } = string.Empty;
    public string Casilla { get; set; } = string.Empty;
    public decimal Tarifa { get; set; }
    public decimal BaseImponible { get; set; }
    public decimal Iva { get; set; }
    public string SerieDocumentoModificado { get; set; } = string.Empty;
    public string NumeroDocumentoModificado { get; set; } = string.Empty;
    public string NumeroAutorizacion { get; set; } = string.Empty;
    public string Origen { get; set; } = "MANUAL";
    public bool EsFaltante { get; set; }
    public IReadOnlyList<string> Detalles { get; set; } = Array.Empty<string>();
    public bool RequiereClasificacion => TipoDocumento is (1 or 3) && !EsFaltante && (TipoFacturacion is null or <= 0 || string.IsNullOrWhiteSpace(Casilla));
    public decimal Total => BaseImponible + Iva;
}

public sealed record EDeclaraActividadCompra(int Id, string Descripcion);
public sealed record EDeclaraCasillaCompra(string Id, string Descripcion, string CasillaAsociada = "");
public sealed record EDeclaraTarifaCompra(decimal Valor, string Descripcion);

public sealed class EDeclaraComprasCatalogos
{
    public IReadOnlyList<EDeclaraActividadCompra> Actividades { get; init; } = Array.Empty<EDeclaraActividadCompra>();
    public IReadOnlyList<EDeclaraCasillaCompra> CasillasCompras { get; init; } = Array.Empty<EDeclaraCasillaCompra>();
    public IReadOnlyList<EDeclaraCasillaCompra> CasillasNotasCredito { get; init; } = Array.Empty<EDeclaraCasillaCompra>();
    public IReadOnlyList<EDeclaraTarifaCompra> Tarifas { get; init; } = Array.Empty<EDeclaraTarifaCompra>();
}

public sealed record EDeclaraImportacionResultado(int Importados, int Omitidos, IReadOnlyList<string> Errores)
{
    public int Procesados => Importados + Omitidos;
}
