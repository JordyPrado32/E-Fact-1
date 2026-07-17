using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("REPORTEVENTABACKOFFICE")]
public class ReporteVentaBackOffice
{
    [Key]
    public int IdReporte { get; set; }

    [Required]
    [MaxLength(150)]
    public string Cliente { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string Producto { get; set; } = null!; // e-fact / e-sign

    [Required]
    [MaxLength(100)]
    public string PlanPaquete { get; set; } = null!; // plan o paquete contratado

    [Column(TypeName = "decimal(18,2)")]
    public decimal Valor { get; set; }

    public DateTime Fecha { get; set; }

    [Required]
    [MaxLength(50)]
    public string Canal { get; set; } = null!; // Web / Números Asesores

    [MaxLength(100)]
    public string? Vendedor { get; set; } // vendedor, si aplica

    [Required]
    [MaxLength(50)]
    public string Estado { get; set; } = null!; // pendiente / pagada / anulada

    [Required]
    [MaxLength(50)]
    public string FormaPago { get; set; } = null!; // forma de pago

    [MaxLength(500)]
    public string? Observacion { get; set; } // observación

    public byte[]? ComprobanteArchivo { get; set; } // comprobante binario (varbinary)
}

