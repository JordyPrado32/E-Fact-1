using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("Usuarios")]
public partial class Usuario
{
    [Key]
    public int IdUsuario { get; set; }

    public string Nombres { get; set; } = null!;
    public string Apellidos { get; set; } = null!;
    public string? NombreEmpresa { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? DireccionEmpresa { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;

    public int? IdTipoUsuario { get; set; }
    public int? IdTipoIdentificacion { get; set; }
    public int? TipoCliente { get; set; }

    public int? IntentosFallidos { get; set; }
    public bool? CuentaBloqueada { get; set; }
    public DateTime? FechaDesbloqueo { get; set; }
    public byte[]? FirmaElectronica { get; set; }
    public string? AvatarUrl { get; set; }
    public string? TokenRecuperacion { get; set; }
    public DateTime? FechaExpiracionToken { get; set; }

    // Corregido según tu hallazgo en la DB
    [Column("MFA_Habilitado")]
    public bool? MfaHabilitado { get; set; }

    [Column("MFA_SecretKey")]
    public string? MfaSecretKey { get; set; }

    [Column("CodigoSms_Temp")]
    public string? CodigoSmsTemp { get; set; }

    public DateTime? FechaCreacion { get; set; }
    public DateTime? UltimoAcceso { get; set; }
    public bool? Estado { get; set; }
    public bool? ClaveTemporal { get; set; }
    public string? Celular { get; set; } = null!;
    public string? Identificacion { get; set; }
    public DateTime? FechaNacimiento { get; set; }
    [Column("idVendedor")]
    public int? IdVendedor { get; set; }
    public int? idJefe { get; set; }

    public bool? estadoAsociado { get; set; }
    public int SaldoDocumentos { get; set; }
    public DateTime? FechaUltimaRecargaDocumentos { get; set; }
    public string? HistorialComprasDocumentosJson { get; set; }

    [NotMapped]
    public string NombreCompleto => $"{Nombres} {Apellidos}".Trim();

    // --- RELACIONES ---
    public virtual ICollection<Factura> Facturas { get; set; } = new List<Factura>();

    [ForeignKey("IdTipoIdentificacion")]
    public virtual TipoIdentificacion? IdTipoIdentificacionNavigation { get; set; }

    [ForeignKey("IdTipoUsuario")]
    public virtual TipoUsuario? IdTipoUsuarioNavigation { get; set; }

    public virtual ICollection<LogIniciosSesion> LogIniciosSesions { get; set; } = new List<LogIniciosSesion>();
}
