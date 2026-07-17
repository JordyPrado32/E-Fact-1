using System;

namespace Simetric.DTOs
{
    public class ChartItemDto
    {
        public string Etiqueta { get; set; } = "";
        public decimal Valor { get; set; }
    }

    public class CanalVentaDto
    {
        public string Canal { get; set; } = "";
        public int Transacciones { get; set; }
        public decimal TicketPromedio { get; set; }
        public decimal TotalFacturado { get; set; }
    }

    public class ReporteVendedorDto
    {
        public int IdVendedor { get; set; }
        public string Vendedor { get; set; } = "";
        public string Usuario { get; set; } = "";
        public string Email { get; set; } = "";
        public DateTime? FechaRegistro { get; set; }
        public int Compras { get; set; }
        public decimal TotalComprado { get; set; }
    }

    public class IngresosMesDto
    {
        public string MesAnio { get; set; } = "";
        public int Transacciones { get; set; }
        public decimal FacturacionBruta { get; set; }
        public decimal Recaudacion { get; set; }
    }
}
