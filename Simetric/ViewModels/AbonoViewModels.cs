using System;

namespace Simetric.ViewModels
{
    /// <summary>
    /// Representa un registro de saldo a favor disponible para aplicar en el próximo pago.
    /// </summary>
    public class SaldoAFavorVM
    {
        public int IdAbonoMultiple { get; set; }
        public DateTime FechaPago { get; set; }
        public decimal ValorOriginal { get; set; }
        public decimal SaldoDisponible { get; set; }
        public string? Observacion { get; set; }
    }

    /// <summary>
    /// Representa una factura que ya ha sido completamente saldada.
    /// </summary>
    public class FacturaHistorialVM
    {
        public int IdFactura { get; set; }
        public string? NumFactura { get; set; }
        public decimal TotalFactura { get; set; }
        public decimal TotalAbonado { get; set; }
        public DateTime FechaSaldada { get; set; }

        /// <summary>Excedente pagado sobre el total de la factura (si aplica).</summary>
        public decimal Excedente => TotalAbonado - TotalFactura;
    }

    public class EstadoCuentaClienteResumenVM
    {
        public int IdCliente { get; set; }
        public string NombreCliente { get; set; } = string.Empty;
        public string NumeroIdentificacion { get; set; } = string.Empty;
        public int IdFactura { get; set; }
        public string NumeroFactura { get; set; } = string.Empty;
        public decimal ValorFacturado { get; set; }
        public decimal TotalAbonos { get; set; }
        public decimal SaldoActual { get; set; }
        public DateTime? FechaEmision { get; set; }
        public DateTime? FechaVencimiento { get; set; }
        public int FacturasPendientes { get; set; }
        public decimal SaldoTotalCliente { get; set; }
        public decimal MontoUltimoAbono { get; set; }
        public DateTime? FechaUltimoAbono { get; set; }
        public int DiasVencidosMaximos { get; set; }
    }

    public class EstadoCuentaDetalleVM
    {
        public int IdCliente { get; set; }
        public string NombreCliente { get; set; } = string.Empty;
        public string NumeroIdentificacion { get; set; } = string.Empty;
        public string Correo { get; set; } = string.Empty;
        public decimal SaldoTotal { get; set; }
        public int FacturasPendientes { get; set; }
        public decimal MontoUltimoAbono { get; set; }
        public DateTime? FechaUltimoAbono { get; set; }
        public int DiasVencidosMaximos { get; set; }
        public decimal SaldoAFavorDisponible { get; set; }
        public List<EstadoCuentaFacturaVM> Facturas { get; set; } = new();
        public List<EstadoCuentaAbonoVM> Abonos { get; set; } = new();
        public List<EstadoCuentaMovimientoVM> Movimientos { get; set; } = new();
    }

    public class EstadoCuentaFacturaVM
    {
        public int IdFactura { get; set; }
        public string NumeroFactura { get; set; } = string.Empty;
        public DateTime? FechaEmision { get; set; }
        public DateTime? FechaVencimiento { get; set; }
        public decimal ValorFacturado { get; set; }
        public decimal TotalAbonos { get; set; }
        public decimal SaldoActual { get; set; }
        public string Estado { get; set; } = string.Empty;
        public int DiasVencidos { get; set; }
    }

    public class EstadoCuentaAbonoVM
    {
        public int IdAbono { get; set; }
        public int? IdAbonoMultiple { get; set; }
        public int? IdFactura { get; set; }
        public string NumeroFactura { get; set; } = string.Empty;
        public DateTime? FechaPago { get; set; }
        public decimal Monto { get; set; }
        public string Concepto { get; set; } = string.Empty;
        public string FormaPago { get; set; } = string.Empty;
        public string Observacion { get; set; } = string.Empty;
    }

    public class EstadoCuentaMovimientoVM
    {
        public DateTime Fecha { get; set; }
        public string Documento { get; set; } = string.Empty;
        public string Concepto { get; set; } = string.Empty;
        public decimal Debito { get; set; }
        public decimal Credito { get; set; }
        public decimal Saldo { get; set; }
        public string Tipo { get; set; } = string.Empty;
    }
}
