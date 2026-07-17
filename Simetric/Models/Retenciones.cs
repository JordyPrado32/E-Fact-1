using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

// ================== RETENCION ==================
[Table("RETENCION")]
public class Retencion
{
    [Key]
    [Column("codigo")]
    public int Codigo { get; set; }

    [Column("descripcion")]
    public string? Descripcion { get; set; }
}

// ================== RETENCIONIVA ==================
[Table("RETENCIONIVA")]
public class RetencionIva
{
    [Key]
    [Column("codigo")]
    public int Codigo { get; set; }

    [Column("descripcion")]
    public string? Descripcion { get; set; }

    [Column("valor")]
    public decimal? Valor { get; set; }
}

// ================== RETENCIONISD ==================
[Table("RETENCIONISD")]
public class RetencionIsd
{
    [Key]
    [Column("codigo")]
    public int Codigo { get; set; }

    [Column("descripcion")]
    public string? Descripcion { get; set; }

    [Column("valor")]
    public decimal? Valor { get; set; }
}

[Table("RETENCIONRENTA")]
public class RetencionRenta
{
    [Key]
    [Column("codigo")]
    public string Codigo { get; set; } = "";   // ✅ STRING

    [Column("descripcion")]
    public string? Descripcion { get; set; }

    [Column("valor")]
    public decimal? Valor { get; set; }

    [Column("valorFinal")]
    public decimal? ValorFinal { get; set; }

    [Column("codigoFormulario103")]
    public string? CodigoFormulario103 { get; set; }

    [Column("informacionExtra")]
    public string? InformacionExtra { get; set; }

    [Column("Estado")]
    public bool? Estado { get; set; }
}

[Table("COMPRASRETVALOR")]
public class CompraRetValor
{
    [Key]
    [Column("sec")]
    public int Sec { get; set; }

    [Column("idCompra")]
    public int? IdCompra { get; set; }

    [Column("idRet")]
    public int? IdRet { get; set; }

    [Column("valor")]
    public decimal? Valor { get; set; }

    [Column("base")]
    public decimal? Base { get; set; }

    [Column("tipo")]
    public string? Tipo { get; set; }

    [Column("estado")]
    public bool? Estado { get; set; }

    [Column("serie")]
    public string? Serie { get; set; }

    [Column("numSri")]
    public int? NumSri { get; set; }

    [Column("autorizacion")]
    public string? Autorizacion { get; set; }

    [Column("idRetencionInfo")]
    public int? IdRetencionInfo { get; set; }

    [Column("porcentajeRetencion")]
    public decimal? PorcentajeRetencion { get; set; }

    [Column("valorRetenido")]
    public decimal? ValorRetenido { get; set; }
}