using Simetric.Models;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace Simetric.Models
{
    [Table("Auditoria")]
    public class Auditoria
    {
        [Key]
        [Column("IdAuditoria")]
        public int IdAuditoria { get; set; }

        [Column("IdUsuario")]
        public int? IdUsuario { get; set; }

        [Column("Fecha")]
        public DateTime Fecha { get; set; } = DateTime.Now;

        [Column("Accion")]
        [StringLength(255)]
        public string? Accion { get; set; }

        [Column("ValoresPrevios")]
        public string? ValoresPrevios { get; set; }

        [Column("ValorNuevo")]
        public string? ValorNuevo { get; set; }

        [Column("Detalles")]
        public string? Detalles { get; set; }

        [ForeignKey("IdUsuario")]
        public virtual Usuario? Usuario { get; set; }
    }
}
