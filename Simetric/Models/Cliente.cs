using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

public partial class Cliente
{
    [Key]
    public int Codcliente { get; set; }
    public string? Apellidos { get; set; }
    public string? Nombres { get; set; }
    public string? Nombrecomercial { get; set; }
    public string? Nombrerazonsocial { get; set; }
    public string? Tipoidentificacion { get; set; }
    public string? Numeroidentificacion { get; set; }
    public string? Direccion { get; set; }
    public string? Telefonoconvencional { get; set; }
    public string? Celular { get; set; }
    public string? Correo { get; set; }
    public int? DiasCredito { get; set; }
    
    // Cambiado a int? para permitir nulos si la BD lo permite
    public int? TipoCliente { get; set; } 

    public bool? Retenciones { get; set; }
    public bool? Retiva { get; set; }
    public bool? Retfuente { get; set; }
    public string? Numcontribuyente { get; set; }
    public string? Oblgconta { get; set; }

    public int? Usuario { get; set; }
    [ForeignKey("Usuario")]
    public virtual Usuario? UsuarioNavegacion { get; set; }

    public int? Idempresa { get; set; }
    public int? Idsucursal { get; set; }
    public bool? Estado { get; set; }
    public DateOnly? Fechaingreso { get; set; }
    public string? Referencia { get; set; }
    public DateOnly? Fechanacimiento { get; set; }
    public string? Sexo { get; set; }
    public string? Profesion { get; set; }
    public string? Observaciones { get; set; }
    public string? Estadocivil { get; set; }
    public int? Pais { get; set; }
    public int? Provincia { get; set; }
    public int? Ciudad { get; set; }
    public int? Idvendedor { get; set; }
    public decimal? Descuento { get; set; }
    public int? Iddescuento { get; set; }
    public bool? Personanatural { get; set; }
    public bool? Contribuyenteespecial { get; set; }
    public string? Actividadcontribuyente { get; set; }

    public virtual ICollection<Factura> Facturas { get; set; } = new List<Factura>();
    public virtual ICollection<ClienteCorreo> ClientesCorreos { get; set; } = new List<ClienteCorreo>();
    public virtual Tipocliente? TipoClienteNavigation { get; set; }
    public int? IdeSec { get; set; }
    public virtual Identificacion? IdentificacionNavigation { get; set; }


}
