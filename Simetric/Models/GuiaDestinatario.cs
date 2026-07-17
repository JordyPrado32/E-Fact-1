using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models
{
    [Table("GUIADESTINATARIO")]
    public class GuiaDestinatario
    {
        [Key]
        [Column("sec")]
        public int Sec { get; set; }

        [Column("idGuiaRemision")]
        public int? IdGuiaRemision { get; set; }

        [Column("idDestinatario")]
        public string? IdDestinatario { get; set; }

        [Column("razonSocial")]
        public string? RazonSocial { get; set; }

        [Column("direccion")]
        public string? Direccion { get; set; }

        [Column("motivoTraslado")]
        public string? MotivoTraslado { get; set; }

        [Column("docAduanero")]
        public string? DocAduanero { get; set; }

        [Column("codEstablecimiento")]
        public string? CodEstablecimiento { get; set; }

        [Column("ruta")]
        public string? Ruta { get; set; }

        [Column("codDocSustento")]
        public string? CodDocSustento { get; set; }

        [Column("numDocSustento")]
        public string? NumDocSustento { get; set; }

        [Column("numAutorizacionSustento")]
        public string? NumAutorizacionSustento { get; set; }

        [Column("fechaEmiSustento")]
        public DateTime? FechaEmiSustento { get; set; }

        [Column("serieDocSustento")]
        public string? SerieDocSustento { get; set; }
    }
}