using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("VENDEDOR_BACKOFFICE")]
public class VendedorBackOffice
{
    [Key]
    [Column("idVendedor")]
    public int IdVendedor { get; set; }

    [Required]
    [StringLength(120)]
    [Column("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Required]
    [StringLength(60)]
    [Column("codigoReferencia")]
    public string CodigoReferencia { get; set; } = string.Empty;

    [Column("activo")]
    public bool Activo { get; set; } = true;

    [Column("esSistema")]
    public bool EsSistema { get; set; }

    [Column("idUsuarioCreacion")]
    public int? IdUsuarioCreacion { get; set; }

    [Column("fechaCreacion")]
    public DateTime FechaCreacion { get; set; } = DateTime.Now;
}
