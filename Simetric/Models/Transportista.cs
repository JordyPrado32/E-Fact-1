using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models
{
    [Table("TRANSPORTISTAS")]
    public class Transportista
    {
        [Key]
        [Column("codigo")]
        public int Codigo { get; set; }

        [Column("RazonSocial")]
        [StringLength(150)]
        public string? RazonSocial { get; set; }

        [Column("tipoIdentificacion")]
        [StringLength(10)]
        public string? TipoIdentificacion { get; set; }

        [Column("numeroIdentificacion")]
        [StringLength(15)]
        public string? NumeroIdentificacion { get; set; }

        [Column("correo")]
        [StringLength(200)]
        public string? Correo { get; set; }

        [Column("placa")]
        [StringLength(10)]
        public string? Placa { get; set; }

        [Column("oblCont")]
        [StringLength(2)]
        public string? OblCont { get; set; }

        [Column("ContribuyenteEsp")]
        [StringLength(50)]
        public string? ContribuyenteEsp { get; set; }

        [Column("direccion")]
        [StringLength(350)]
        public string? Direccion { get; set; }

        [Column("telefono")]
        [StringLength(50)]
        public string? Telefono { get; set; }
    }
}