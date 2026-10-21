namespace Simetric.DTOs.EContax;

public sealed class EContaxInventarioItemDto
{
    public int CodigoProducto { get; set; }
    public string? CodigoPrincipal { get; set; }
    public string? NombreProducto { get; set; }
    public int IdSucursal { get; set; }
    public string? NombreSucursal { get; set; }
    public decimal StockActual { get; set; }
    public decimal StockReservado { get; set; }
    public decimal StockDisponible => Math.Max(0, StockActual - StockReservado);
    public decimal StockMinimo { get; set; }
    public decimal StockMaximo { get; set; }
    public decimal CostoPromedio { get; set; }
    public string? Alerta { get; set; }
    public bool TieneLotePorVencer { get; set; }
    public DateTime? ProximoVencimiento { get; set; }
}

public sealed class EContaxInventarioDashboardDto
{
    public string MetodoValoracion { get; set; } = "PROMEDIO";
    public bool PuedeEditar { get; set; }
    public bool PuedeConsultarTodasSucursales { get; set; }
    public int TotalProductos { get; set; }
    public decimal StockTotal { get; set; }
    public int ProductosBajoMinimo { get; set; }
    public int ProductosSobreMaximo { get; set; }
    public int LotesPorVencer { get; set; }
    public decimal ValorInventario { get; set; }
    public List<EContaxSucursalDto> Sucursales { get; set; } = new();
    public List<EContaxInventarioItemDto> Items { get; set; } = new();
}

public sealed class EContaxInventarioMovimientoDto
{
    public long IdMovimiento { get; set; }
    public DateTime FechaMovimiento { get; set; }
    public string TipoMovimiento { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal CostoTotal { get; set; }
    public decimal SaldoCantidad { get; set; }
    public decimal SaldoCosto { get; set; }
    public string? NumeroLote { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public string MetodoValoracion { get; set; } = "PROMEDIO";
    public string? Referencia { get; set; }
    public string? Observacion { get; set; }
}

public sealed class EContaxInventarioKardexDto
{
    public int CodigoProducto { get; set; }
    public int IdSucursal { get; set; }
    public string? NombreProducto { get; set; }
    public string? CodigoPrincipal { get; set; }
    public string? NombreSucursal { get; set; }
    public decimal StockActual { get; set; }
    public decimal StockReservado { get; set; }
    public decimal StockDisponible => Math.Max(0, StockActual - StockReservado);
    public decimal CostoPromedio { get; set; }
    public string MetodoValoracion { get; set; } = "PROMEDIO";
    public bool PuedeEditar { get; set; }
    public bool PuedeConsultarTodasSucursales { get; set; }
    public List<EContaxSucursalDto> Sucursales { get; set; } = new();
    public List<EContaxInventarioMovimientoDto> Movimientos { get; set; } = new();
}

public sealed class EContaxInventarioAjusteDto
{
    public int CodigoProducto { get; set; }
    public int? IdSucursal { get; set; }
    public string TipoMovimiento { get; set; } = "ENTRADA";
    public decimal Cantidad { get; set; }
    public decimal? CostoUnitario { get; set; }
    public decimal? StockMinimo { get; set; }
    public decimal? StockMaximo { get; set; }
    public string? Observacion { get; set; }
}

public sealed class EContaxInventarioTransferenciaDto
{
    public int? IdSucursalOrigen { get; set; }
    public int? IdSucursalDestino { get; set; }
    public List<EContaxInventarioOperacionDetalleDto> Detalles { get; set; } = new();
    public string? Observacion { get; set; }
}

public sealed class EContaxInventarioDevolucionDto
{
    public int IdRecepcion { get; set; }
    public List<EContaxInventarioOperacionDetalleDto> Detalles { get; set; } = new();
    public string? Observacion { get; set; }
}

public sealed class EContaxInventarioOperacionDetalleDto
{
    public int CodigoProducto { get; set; }
    public decimal Cantidad { get; set; }
}

public sealed class EContaxInventarioTomaFisicaDto
{
    public int? IdSucursal { get; set; }
    public List<EContaxInventarioTomaFisicaDetalleDto> Detalles { get; set; } = new();
    public string? Observacion { get; set; }
}

public sealed class EContaxInventarioTomaFisicaDetalleDto
{
    public int CodigoProducto { get; set; }
    public decimal StockContado { get; set; }
}

public sealed class EContaxInventarioReservaDto
{
    public int CodigoProducto { get; set; }
    public int? IdSucursal { get; set; }
    public decimal Cantidad { get; set; }
    public List<EContaxInventarioOperacionDetalleDto> Detalles { get; set; } = new();
    public string? Referencia { get; set; }
    public string? Observacion { get; set; }
}

public sealed class EContaxInventarioRevalorizacionDto
{
    public int CodigoProducto { get; set; }
    public int? IdSucursal { get; set; }
    public decimal CostoUnitario { get; set; }
    public string? Observacion { get; set; }
}

public sealed class EContaxInventarioOperacionResultadoDto
{
    public string Numero { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
}

public sealed class EContaxInventarioConfiguracionDto
{
    public string MetodoValoracion { get; set; } = "PROMEDIO";
}

public sealed class EContaxInventarioReporteFiltroDto
{
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    public int? IdSucursal { get; set; }
    public int? CodigoProducto { get; set; }
    public string? Producto { get; set; }
    public string? TipoMovimiento { get; set; }
}

public sealed class EContaxInventarioReporteMovimientoDto
{
    public DateTime FechaMovimiento { get; set; }
    public int CodigoProducto { get; set; }
    public string? CodigoPrincipal { get; set; }
    public string? NombreProducto { get; set; }
    public int IdSucursal { get; set; }
    public string? NombreSucursal { get; set; }
    public string TipoMovimiento { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal CostoTotal { get; set; }
    public decimal SaldoCantidad { get; set; }
    public decimal SaldoCosto { get; set; }
    public string? NumeroLote { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public string? Referencia { get; set; }
    public string? Observacion { get; set; }
}

public sealed class EContaxInventarioReporteDto
{
    public DateTime GeneradoEn { get; set; } = DateTime.Now;
    public decimal TotalEntradas { get; set; }
    public decimal TotalSalidas { get; set; }
    public decimal ValorEntradas { get; set; }
    public decimal ValorSalidas { get; set; }
    public List<EContaxSucursalDto> Sucursales { get; set; } = new();
    public List<EContaxInventarioReporteMovimientoDto> Movimientos { get; set; } = new();
}
