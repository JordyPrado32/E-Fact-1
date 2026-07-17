using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models
{
    [Table("GUIAREMISION")]
    public class GuiaRemision
    {
        [Key]
        [Column("sec")]
        public int Sec { get; set; }

        [Column("idTranportista")]
        public int? IdTranportista { get; set; }

        [Column("FechaIniTransporte")]
        public DateTime? FechaIniTransporte { get; set; }

        [Column("FechaFinTransporte")]
        public DateTime? FechaFinTransporte { get; set; }

        [Column("placa")]
        public string? Placa { get; set; }

        [Column("codClave")]
        public string? CodClave { get; set; }

        [Column("numGuiaRemision")]
        public string? NumGuiaRemision { get; set; }

        [Column("fecha")]
        public DateTime? Fecha { get; set; }

        [Column("numAutorizacion")]
        public string? NumAutorizacion { get; set; }

        [Column("fechaAutorizacion")]
        public string? FechaAutorizacion { get; set; }

        [Column("mensaje")]
        public string? Mensaje { get; set; }

        [Column("idEmpresa")]
        public int? IdEmpresa { get; set; }

        [Column("idSucursal")]
        public int? IdSucursal { get; set; }

        [StringLength(1)]
        [Column("estadoSRI")]
        public string? EstadoSRI { get; set; }

        [Column("idUsuario")]
        public int? IdUsuario { get; set; }

        [Column("serie")]
        public string? Serie { get; set; }

        [Column("ambiente")]
        public int? Ambiente { get; set; }

        [Column("codfactura")]
        public int? Codfactura { get; set; }

        [Column("direccionPartida")]
        public string? DireccionPartida { get; set; }
    }
}
