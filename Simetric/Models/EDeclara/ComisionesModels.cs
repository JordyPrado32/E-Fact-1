using System.ComponentModel.DataAnnotations;

namespace Simetric.Models.EDeclara;

public enum ComisionEstado { Proyectada, Generada, Pendiente, Pagada, Revertida, Anulada }
public enum TipoInvolucrado { Empleado, Vendedor, Referido, Socio, Externo, Otro }
public enum TipoCalculoComision { Porcentaje, ValorFijo }
public enum BaseCalculoComision { Subtotal, Total, Utilidad, ValorFijo }

public sealed class ComisionInvolucrado
{
    public int Id { get; set; }
    public int IdEmpresa { get; set; }
    public int? IdSucursal { get; set; }
    public TipoInvolucrado Tipo { get; set; }
    public int? IdEmpleado { get; set; }
    [Required(ErrorMessage = "La identificación es obligatoria.")]
    [StringLength(30, ErrorMessage = "La identificación admite hasta 30 caracteres.")]
    public string Identificacion { get; set; } = string.Empty;
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(200, ErrorMessage = "El nombre admite hasta 200 caracteres.")]
    public string Nombre { get; set; } = string.Empty;
    [StringLength(40)]
    public string? Telefono { get; set; }
    [EmailAddress(ErrorMessage = "El correo no tiene un formato válido.")]
    [StringLength(150)]
    public string? Correo { get; set; }
    [Range(0, 100, ErrorMessage = "El porcentaje debe estar entre 0 y 100.")]
    public decimal PorcentajePredeterminado { get; set; }
    [Range(0, double.MaxValue, ErrorMessage = "El valor fijo no puede ser negativo.")]
    public decimal ValorPredeterminado { get; set; }
    public TipoCalculoComision TipoCalculo { get; set; }
    public BaseCalculoComision BaseCalculo { get; set; }
    public DateTime FechaVigencia { get; set; } = DateTime.Today;
    public bool Activo { get; set; } = true;
    public string? Observaciones { get; set; }
    public string? DatosBancarios { get; set; }
    public DateTime FechaCreacion { get; set; }
    public decimal TotalGenerado { get; set; }
    public DateTime? FechaUltimaComision { get; set; }
    public string? NumeroUltimaFactura { get; set; }
}

public sealed class ComisionMovimiento
{
    public long Id { get; set; }
    public int IdFactura { get; set; }
    public string? NumeroFactura { get; set; }
    public string? Cliente { get; set; }
    public int IdInvolucrado { get; set; }
    public string Involucrado { get; set; } = string.Empty;
    public string TipoInvolucrado { get; set; } = string.Empty;
    public string BaseUtilizada { get; set; } = string.Empty;
    public decimal Porcentaje { get; set; }
    public decimal ValorFacturado { get; set; }
    public decimal ComisionGenerada { get; set; }
    public ComisionEstado Estado { get; set; }
    public DateTime FechaGeneracion { get; set; }
    public DateTime? FechaPago { get; set; }
    public string? MetodoPago { get; set; }
    public string? ReferenciaPago { get; set; }
    public string? Motivo { get; set; }
}

public sealed class ComisionesResumen
{
    public decimal Generadas { get; set; }
    public decimal Pendientes { get; set; }
    public decimal Pagadas { get; set; }
    public decimal Revertidas { get; set; }
    public decimal Facturacion { get; set; }
    public int InvolucradosActivos { get; set; }
    public decimal PromedioPorFactura { get; set; }
}

public sealed class ComisionConfiguracion
{
    public int IdEmpresa { get; set; }
    [Range(0, double.MaxValue, ErrorMessage = "El límite no puede ser negativo.")]
    public decimal LimitePorFactura { get; set; }
    public string ReglaRedondeo { get; set; } = "2 decimales";
    public string MomentoPendiente { get; set; } = "Al autorizar";
    public bool PermitirModificarPorcentaje { get; set; }
    public bool RequiereAutorizacionSobreLimite { get; set; } = true;
}

public sealed class ComisionHistorial
{
    public long Id { get; set; }
    public int IdInvolucrado { get; set; }
    public string Involucrado { get; set; } = string.Empty;
    public decimal? PorcentajeAnterior { get; set; }
    public decimal PorcentajeNuevo { get; set; }
    public DateTime Fecha { get; set; }
    public string Motivo { get; set; } = string.Empty;
}

public sealed class ComisionPagoRequest
{
    public int IdEmpresa { get; set; }
    public IReadOnlyCollection<long> Ids { get; set; } = [];
    public string MetodoPago { get; set; } = string.Empty;
    public string? Referencia { get; set; }
}

public sealed class ComisionEstadoRequest
{
    public int IdEmpresa { get; set; }
    public IReadOnlyCollection<long> Ids { get; set; } = [];
}

public sealed class ComisionFacturaDetalle
{
    public int IdInvolucrado { get; set; }
    public decimal Porcentaje { get; set; }
    public decimal ValorFijo { get; set; }
    public BaseCalculoComision Base { get; set; }
    public string? Motivo { get; set; }
}
