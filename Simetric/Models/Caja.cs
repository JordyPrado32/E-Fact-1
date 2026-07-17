using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("CAJA")]
public partial class Caja
{
    [Key]
    [Column("sec")]
    public int Sec { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "El número de caja debe ser mayor a 0.")]
    [Column("numCaja")]
    public int? NumCaja { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Debe seleccionar un usuario válido.")]
    [Column("idUsuario")]
    public int? IdUsuario { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "El ID de empresa debe ser mayor a 0.")]
    [Column("idEmpresa")]
    public int? IdEmpresa { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "El ID de sucursal debe ser mayor a 0.")]
    [Column("idSucursal")]
    public int? IdSucursal { get; set; }

    [Required(ErrorMessage = "La serie de factura es obligatoria.")]
    [RegularExpression(@"^\d{3}-\d{3}$", ErrorMessage = "Formato requerido: 001-001")]
    [Column("serieFactura")]
    public string? SerieFactura { get; set; }

    [Column("serieCompras")]
    public string? SerieCompras { get; set; }

    [Required(ErrorMessage = "La serie de guía es obligatoria.")]
    [RegularExpression(@"^\d{3}-\d{3}$", ErrorMessage = "Formato requerido: 001-001")]
    [Column("serieGuia")]
    public string? SerieGuia { get; set; }

    [Column("serieDebitos")]
    public string? SerieDebitos { get; set; }

    [Required(ErrorMessage = "La serie de notas de crédito es obligatoria.")]
    [RegularExpression(@"^\d{3}-\d{3}$", ErrorMessage = "Formato requerido: 001-001")]
    [Column("serieNotasCred")]
    public string? SerieNotasCred { get; set; }

    [Column("estado")]
    public bool? Estado { get; set; }

    [Column("es_caja_sistema")]
    public bool? EsCajaSistema { get; set; }

    [Column("secuenciaFacturaInicializada")]
    public bool? SecuenciaFacturaInicializada { get; set; }

    [Column("ultimoSecuencialFactura")]
    public long? UltimoSecuencialFactura { get; set; }

    [NotMapped]
    public int? idJefe { get; set; }
}
