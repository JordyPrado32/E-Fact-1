using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Simetric.Models;

[Table("EMISOR")]
public class Emisor
{
    [Key]
    [Column("codigo")]
    public int Codigo { get; set; }

    [Column("razonSocial")]
    public string? RazonSocial { get; set; }

    [Column("RUC")]
    public string? Ruc { get; set; }

    [Column("nomComercial")]
    public string? NomComercial { get; set; }

    [Column("dirEstablecimiento")]
    public string? DirEstablecimiento { get; set; }

    [Column("codEstablecimiento")]
    public string? CodEstablecimiento { get; set; }

    [Column("resolusion")]
    public string? Resolusion { get; set; }

    [Column("contribuyenteEspecial")]
    public string? ContribuyenteEspecial { get; set; }

    [Column("codPuntoEmision")]
    public string? CodPuntoEmision { get; set; }

    [Column("llevaContabilidad")]
    public string? LlevaContabilidad { get; set; } // "SI" / "NO"

    [Column("logoImagen")]
    public string? LogoImagen { get; set; }

    [Column("tipoEmision")]
    public string? TipoEmision { get; set; }

    [Column("tiempoEspera")]
    public string? TiempoEspera { get; set; }

    [Column("CLAVE_INTERNA")]
    public string? ClaveInterna { get; set; }

    [Column("TIPO_AMBIENTE")]
    public string? TipoAmbiente { get; set; }

    [Column("DIRECCION_MATRIZ")]
    public string? DireccionMatriz { get; set; }

    [Column("TOKEN")]
    public string? Token { get; set; }

    [Column("retenciones")]
    public string? Retenciones { get; set; }

    [Column("retIva")]
    public string? RetIva { get; set; }

    [Column("retFuente")]
    public string? RetFuente { get; set; }

    [Column("idEmpresa")]
    public int? IdEmpresa { get; set; }

    [Column("idSucursal")]
    public int? IdSucursal { get; set; }

    [Column("pathCertificado")]
    public string? PathCertificado { get; set; }

    [Column("EMAIL")]
    public string? Email { get; set; }
    [Column("DIRECCION")]

    public string? Direccion { get; set; }

    [Column("claveCertificado")]
    public string? ClaveCertificado { get; set; }

    [NotMapped]
    [JsonPropertyName("tieneClaveCertificadoConfigurada")]
    public bool TieneClaveCertificadoConfigurada { get; set; }

    [NotMapped]
    public bool EliminarClaveCertificado { get; set; }

    [Column("telefono")]
    public string? Telefono { get; set; }

    [Column("ESTADO")] // Asegúrate de que en la BD sea bit/boolean
    public bool Estado { get; set; } = true;
    [Column("id_usuario")]
    public int? IdUsuario { get; set; } // int null en SQL -> int? en C#

    [Column("es_emisor_sistema")]
    public bool EsEmisorSistema { get; set; }

    // Propiedad de navegación (Opcional, pero recomendada)
    [ForeignKey("IdUsuario")]
    public virtual Usuario? Usuario { get; set; }
}
