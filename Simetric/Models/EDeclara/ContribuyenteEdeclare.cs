using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("Contribuyentes_Edeclare")]
public class ContribuyenteEdeclare
{
    [Key]
    [Column("CODContribuyentes_Edeclare")]
    public int CodContribuyente { get; set; }

    [Column("APELLIDOS")]
    public string? Apellidos { get; set; }

    [Column("NOMBRES")]
    public string? Nombres { get; set; }

    [Column("NOMBRECOMERCIAL")]
    public string? Nombrecomercial { get; set; }

    [Column("NOMBRERAZONSOCIAL")]
    public string? Nombrerazonsocial { get; set; }

    [Column("TIPOIDENTIFICACION")]
    public string Tipoidentificacion { get; set; } = string.Empty;

    [Column("NUMEROIDENTIFICACION")]
    public string? Numeroidentificacion { get; set; }

    [Column("DIRECCION")]
    public string? Direccion { get; set; }

    [Column("TELEFONOCONVENCIONAL")]
    public string? Telefonoconvencional { get; set; }

    [Column("CELULAR")]
    public string? Celular { get; set; }

    [Column("CORREO")]
    public string? Correo { get; set; }

    [Column("TIPO_CLIENTE")]
    public int TipoCliente { get; set; }

    [Column("OBLGCONTA")]
    public string? Oblgconta { get; set; }

    [Column("USUARIO")]
    public int? Usuario { get; set; }

    [Column("ESTADO")]
    public bool? Estado { get; set; }

    [Column("OBSERVACIONES")]
    public string? Observaciones { get; set; }

    [Column("PAIS")]
    public int? Pais { get; set; }

    [Column("PROVINCIA")]
    public int? Provincia { get; set; }

    [Column("CIUDAD")]
    public int? Ciudad { get; set; }

    [Column("PERSONANATURAL")]
    public bool? PersonaNatural { get; set; }

    [Column("CONTRIBUYENTEESPECIAL")]
    public bool? ContribuyenteEspecial { get; set; }

    [Column("ACTIVIDADCONTRIBUYENTE")]
    public string? ActividadContribuyente { get; set; }

    [Column("NUMCONTRIBUYENTE")]
    public string? NumContribuyente { get; set; }

    [Column("PERIODICIDADIVA")]
    public string? PeriodicidadIva { get; set; }

    [Column("PERIODICIDADRENTA")]
    public string? PeriodicidadRenta { get; set; }

    [Column("FECHADECLARACION")]
    public DateOnly? FechaDeclaracion { get; set; }

    [Column("FECHAINGRESO")]
    public DateOnly? FechaIngreso { get; set; }

    [ForeignKey("Ciudad")]
    public virtual Ciudad? CiudadNavegacion { get; set; }
}