using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models.EContax;

[Table("ECONTAX_BODEGA", Schema = "dbo")]
public sealed class EContaxBodega
{
    [Key]
    [Column("id_bodega")]
    public int IdBodega { get; set; }

    [Column("id_empresa")]
    public int IdEmpresa { get; set; }

    [Column("id_sucursal")]
    public int IdSucursal { get; set; }

    [Required]
    [Column("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Required]
    [Column("codigo")]
    public string Codigo { get; set; } = string.Empty;

    [Column("direccion")]
    public string? Direccion { get; set; }

    [Column("observacion")]
    public string? Observacion { get; set; }

    [Column("estado")]
    public bool Estado { get; set; } = true;

    [Column("fecha_creacion")]
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    [Column("fecha_actualizacion")]
    public DateTime? FechaActualizacion { get; set; }

    public EContaxSucursal? Sucursal { get; set; }
}
