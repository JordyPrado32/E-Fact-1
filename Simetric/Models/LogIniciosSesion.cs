using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("Log_IniciosSesion")]
public partial class LogIniciosSesion
{
    [Key]
    public int IdLog { get; set; }

    public int? IdUsuario { get; set; }

    public DateTime? FechaAcceso { get; set; }

    public string? DireccionIp { get; set; }

    public string? Navegador { get; set; }

    public bool? Exitoso { get; set; }

    public string? DetalleError { get; set; }

    [ForeignKey("IdUsuario")]
    public virtual Usuario? IdUsuarioNavigation { get; set; }


}
