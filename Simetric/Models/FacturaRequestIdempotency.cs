using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("FACTURA_IDEMPOTENCIA", Schema = "dbo")]
public sealed class FacturaRequestIdempotency
{
    [Key]
    [Column("ID")]
    public long Id { get; set; }

    [Column("ID_USUARIO")]
    public int IdUsuario { get; set; }

    [Required]
    [StringLength(120)]
    [Column("REQUEST_ID")]
    public string RequestId { get; set; } = string.Empty;

    [Column("CODFACTURA")]
    public int Codfactura { get; set; }

    [Column("CREADO_EN")]
    public DateTimeOffset CreadoEn { get; set; }
}
