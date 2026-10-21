using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models.EContax;

[Table("ECONTAX_INVENTARIO_TRANSFERENCIA")]
public sealed class EContaxInventarioTransferencia
{
    [Key] public int IdTransferencia { get; set; }
    public int IdEmpresa { get; set; }
    [MaxLength(30)] public string NumeroTransferencia { get; set; } = string.Empty;
    public int IdSucursalOrigen { get; set; }
    public int IdSucursalDestino { get; set; }
    [MaxLength(20)] public string Estado { get; set; } = "COMPLETADA";
    public DateTime FechaTransferencia { get; set; }
    public int IdUsuario { get; set; }
    [MaxLength(500)] public string? Observacion { get; set; }
    public ICollection<EContaxInventarioTransferenciaDetalle> Detalles { get; set; } = new List<EContaxInventarioTransferenciaDetalle>();
}

[Table("ECONTAX_INVENTARIO_TRANSFERENCIA_DET")]
public sealed class EContaxInventarioTransferenciaDetalle
{
    [Key] public int IdDetalle { get; set; }
    public int IdTransferencia { get; set; }
    public int CodigoProducto { get; set; }
    public decimal Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public EContaxInventarioTransferencia? Transferencia { get; set; }
}

[Table("ECONTAX_INVENTARIO_DEVOLUCION")]
public sealed class EContaxInventarioDevolucion
{
    [Key] public int IdDevolucion { get; set; }
    public int IdEmpresa { get; set; }
    public int IdSucursal { get; set; }
    public int IdRecepcion { get; set; }
    [MaxLength(30)] public string NumeroDevolucion { get; set; } = string.Empty;
    [MaxLength(20)] public string Estado { get; set; } = "APLICADA";
    public DateTime FechaDevolucion { get; set; }
    public int IdUsuario { get; set; }
    [MaxLength(500)] public string? Observacion { get; set; }
    public ICollection<EContaxInventarioDevolucionDetalle> Detalles { get; set; } = new List<EContaxInventarioDevolucionDetalle>();
}

[Table("ECONTAX_INVENTARIO_DEVOLUCION_DET")]
public sealed class EContaxInventarioDevolucionDetalle
{
    [Key] public int IdDetalle { get; set; }
    public int IdDevolucion { get; set; }
    public int CodigoProducto { get; set; }
    public decimal Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public EContaxInventarioDevolucion? Devolucion { get; set; }
}

[Table("ECONTAX_INVENTARIO_TOMA_FISICA")]
public sealed class EContaxInventarioTomaFisica
{
    [Key] public int IdToma { get; set; }
    public int IdEmpresa { get; set; }
    public int IdSucursal { get; set; }
    [MaxLength(30)] public string NumeroToma { get; set; } = string.Empty;
    [MaxLength(20)] public string Estado { get; set; } = "APLICADA";
    public DateTime FechaToma { get; set; }
    public int IdUsuario { get; set; }
    [MaxLength(500)] public string? Observacion { get; set; }
    public ICollection<EContaxInventarioTomaFisicaDetalle> Detalles { get; set; } = new List<EContaxInventarioTomaFisicaDetalle>();
}

[Table("ECONTAX_INVENTARIO_TOMA_FISICA_DET")]
public sealed class EContaxInventarioTomaFisicaDetalle
{
    [Key] public int IdDetalle { get; set; }
    public int IdToma { get; set; }
    public int CodigoProducto { get; set; }
    public decimal StockSistema { get; set; }
    public decimal StockContado { get; set; }
    public decimal Diferencia { get; set; }
    public decimal CostoUnitario { get; set; }
    public EContaxInventarioTomaFisica? Toma { get; set; }
}

[Table("ECONTAX_INVENTARIO_VALORACION_HIST")]
public sealed class EContaxInventarioValoracionHistorial
{
    [Key] public long IdHistorial { get; set; }
    public int IdEmpresa { get; set; }
    [MaxLength(20)] public string MetodoAnterior { get; set; } = string.Empty;
    [MaxLength(20)] public string MetodoNuevo { get; set; } = string.Empty;
    public int IdUsuario { get; set; }
    public DateTime Fecha { get; set; }
    [MaxLength(500)] public string? Observacion { get; set; }
}
