using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema; // Añadir esta línea

namespace Simetric.Models;

[Table("Productotipo")] // Forzar nombre singular en la BD
public partial class Productotipo
{
    [Key]
    public int Idtipoproducto { get; set; }

    public string? Descripcion { get; set; }

    public bool? Estado { get; set; }

    public string? Valoracioninventario { get; set; }

    public bool? Perecible { get; set; }

    public int? Idempresa { get; set; }

    public int? Stockminimo { get; set; }

    public int? Stockmaximo { get; set; }

    public virtual ICollection<Producto> Productos { get; set; } = new List<Producto>();

    public virtual ICollection<Productosubtipo> Productosubtipos { get; set; } = new List<Productosubtipo>();

    [Column("IDUSUARIO")]
    public int? Idusuario { get; set; }

}