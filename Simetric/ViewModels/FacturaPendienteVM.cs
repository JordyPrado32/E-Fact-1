namespace Simetric.ViewModels
{
    public class FacturaPendienteVM
    {
        public int IdFactura { get; set; } // El 'sec' de la tabla Facturas
        public string NumFactura { get; set; } = string.Empty;
        public int IdCliente { get; set; }
        public string NombreCliente { get; set; } = string.Empty;
        public string NumeroIdentificacion { get; set; } = string.Empty;
        public decimal TotalFactura { get; set; }
        public decimal TotalAbonado { get; set; }
        public DateTime? FechaEmision { get; set; }
        public DateTime? FechaVencimiento { get; set; }
        public decimal SaldoPendiente => TotalFactura - TotalAbonado;

        // Propiedad para capturar cuánto se va a pagar en la vista
        public decimal MontoAPagar { get; set; }
        // Indica si la factura está seleccionada para el pago múltiple
        public bool Selected { get; set; }
    }
}
