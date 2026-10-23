namespace Simetric.ViewModels;

public sealed class CuentaPorPagarVM
{
    public int IdFactura { get; init; }
    public string NumeroDocumento { get; init; } = "";
    public string Proveedor { get; init; } = "";
    public string IdentificacionProveedor { get; init; } = "";
    public DateTime? FechaEmision { get; init; }
    public DateTime? FechaVencimiento { get; init; }
    public decimal Total { get; init; }
    public decimal TotalPagado { get; init; }
    public decimal SaldoPendiente { get; init; }
    public string Estado { get; init; } = "PENDIENTE";
    public DateTime? FechaUltimoPago { get; init; }

    public bool EstaVencida => SaldoPendiente > 0m && FechaVencimiento?.Date < DateTime.Today;
}

public sealed class CuentaPorPagarPagoVM
{
    public int IdPago { get; init; }
    public int IdFactura { get; init; }
    public string NumeroDocumento { get; init; } = "";
    public DateTime FechaPago { get; init; }
    public decimal Monto { get; init; }
    public string FormaPago { get; init; } = "";
    public string Referencia { get; init; } = "";
    public string Observacion { get; init; } = "";
}

public sealed class EstadoCuentaProveedorResumenVM
{
    public string IdentificacionProveedor { get; init; } = "";
    public string Proveedor { get; init; } = "";
    public decimal TotalFacturado { get; init; }
    public decimal TotalPagado { get; init; }
    public decimal SaldoActual { get; init; }
    public int CuentasPendientes { get; init; }
    public int CuentasVencidas { get; init; }
    public DateTime? FechaUltimoPago { get; init; }
}

public sealed class EstadoCuentaProveedorDetalleVM
{
    public string IdentificacionProveedor { get; init; } = "";
    public string Proveedor { get; init; } = "";
    public decimal SaldoActual { get; init; }
    public List<CuentaPorPagarVM> Cuentas { get; init; } = new();
    public List<CuentaPorPagarPagoVM> Pagos { get; init; } = new();
    public List<EstadoCuentaMovimientoVM> Movimientos { get; init; } = new();
}
