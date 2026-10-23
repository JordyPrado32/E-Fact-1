namespace Simetric.ViewModels;

public sealed class FacturaCompraManualVM
{
    public int? CodCliente { get; set; }
    public string RucProveedor { get; set; } = "";
    public string Proveedor { get; set; } = "";
    public string TipoIdentificacion { get; set; } = "04";
    public string ObligadoContabilidad { get; set; } = "NO";
    public string NombreComercial { get; set; } = "";
    public string Nombres { get; set; } = "";
    public string Apellidos { get; set; } = "";
    public string Direccion { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string Correo { get; set; } = "";
    public string Referencia { get; set; } = "";
    public DateTime FechaEmision { get; set; } = DateTime.Today;
    public string FormaPago { get; set; } = "20";
    public int PlazoCredito { get; set; }
    public string Observacion { get; set; } = "";
    public List<FacturaCompraManualDetalleVM> Detalles { get; set; } = new();

    public decimal Subtotal => Math.Round(Detalles.Sum(x => x.Base), 2);
    public decimal Iva => Math.Round(Detalles.Sum(x => x.Iva), 2);
    public decimal Total => Math.Round(Subtotal + Iva, 2);
    public bool EsCredito => FormaPago == "19" || PlazoCredito > 0;
}

public sealed class FacturaCompraManualDetalleVM
{
    public string Codigo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Detalle { get; set; } = "";
    public decimal Cantidad { get; set; } = 1m;
    public decimal PrecioUnitario { get; set; }
    public decimal Descuento { get; set; }
    public decimal Tarifa { get; set; } = 15m;

    public decimal Base => Math.Round(Math.Max(Cantidad * PrecioUnitario - Descuento, 0m), 2);
    public decimal Iva => Math.Round(Base * Tarifa / 100m, 2);
}

public sealed class CompraManualVM
{
    public int IdFactura { get; init; }
    public string NumeroDocumento { get; init; } = "";
    public string Proveedor { get; init; } = "";
    public string IdentificacionProveedor { get; init; } = "";
    public DateTime? FechaEmision { get; init; }
    public DateTime? FechaVencimiento { get; init; }
    public string FormaPago { get; init; } = "";
    public string EstadoPago { get; init; } = "";
    public string Estado { get; init; } = "ACTIVA";
    public string Observacion { get; init; } = "";
    public decimal Total { get; init; }
    public decimal TotalPagado { get; set; }
    public decimal SaldoPendiente { get; set; }
    public List<CompraManualDetalleVM> Detalles { get; set; } = new();
}

public sealed class CompraManualDetalleVM
{
    public string Codigo { get; init; } = "";
    public string Descripcion { get; init; } = "";
    public string Detalle { get; init; } = "";
    public decimal Cantidad { get; init; }
    public decimal PrecioUnitario { get; init; }
    public decimal Descuento { get; init; }
    public decimal Base { get; init; }
    public decimal Iva { get; init; }
    public decimal Total { get; init; }
    public int Tarifa { get; init; }
}
