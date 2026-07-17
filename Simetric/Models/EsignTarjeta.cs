using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("ESIGN_TARJETAS")]
public class EsignTarjeta
{
    [Key]
    [Column("ID_TARJETA")]
    public int IdTarjeta { get; set; }

    [Column("ID_USUARIO")]
    public int IdUsuario { get; set; }

    [Required]
    [MaxLength(20)]
    [Column("DOCUMENTO")]
    public string Documento { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [Column("TOKEN")]
    public string Token { get; set; } = string.Empty;

    [MaxLength(50)]
    [Column("MARCA_TARJETA")]
    public string? MarcaTarjeta { get; set; }

    [MaxLength(50)]
    [Column("NUMERO_MASCARA")]
    public string? NumeroMascara { get; set; }

    [Column("FECHA_REGISTRO")]
    public DateTime FechaRegistro { get; set; } = DateTime.Now;

    [Column("ESTADO")]
    public bool Estado { get; set; } = true;
}
