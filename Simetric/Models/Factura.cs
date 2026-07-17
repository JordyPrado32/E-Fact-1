using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models
{
    [Table("FACTURA")] // <--- CLAVE: Mayúsculas exactas como en SQL Server
    public partial class Factura
    {
        [Key]
        [Column("CODFACTURA")]

        public int Codfactura { get; set; }

        public string? Codclave { get; set; }

        public int? Codclientes { get; set; }

        public int? Codemisor { get; set; }

        public int? Codrespuesta { get; set; }

        public int? Codcomprobante { get; set; }

        public int? Codtransportista { get; set; }

        public DateTime? Fchautorizacion { get; set; }

        [Column("NUMFACTURA")]
        public string? Numfactura { get; set; }

        [Column("NUMAUTORIZACION")]
        public string? Numautorizacion { get; set; }

        [Display(Name = "Tipo de Documento")]
        public int? Coddocumento { get; set; }

        [NotMapped]
        public string? Tipoidentificacion { get; set; }


        [Column("GUIAREMISION")]
        public string? Guiaremision { get; set; }

        public string? Numretencion { get; set; }

        public decimal? Subtotal12 { get; set; }

        public decimal? Subtotal0 { get; set; }

        public decimal? Subtotal { get; set; }

        public decimal? Descuentos { get; set; }

        public decimal? Iva { get; set; }

        public decimal? Valortotal { get; set; }

        public DateTime? Fechavence { get; set; }

        public decimal? Noimp { get; set; }

        public decimal? Exiva { get; set; }

        public decimal? Valorice { get; set; }
        [NotMapped]
        public decimal? BiIrbpnr { get; set; }
        [NotMapped]
        public decimal? ValorbiIrbpnr { get; set; }

        public int? Idusuario { get; set; }

        public bool? Autorizado { get; set; }

        public string? Mensaje { get; set; }

        public int? Idempresa { get; set; }

        public int? Idsucursal { get; set; }

        public string? Serie { get; set; }

        public string? Fechaautosri { get; set; }

        public string? Tipopago { get; set; }

        public decimal? Descadicionalcod2 { get; set; }

        public string? Estadoenviosri { get; set; }

        public decimal? Subcerototal { get; set; }

        public decimal? Subdocetotal { get; set; }

        public decimal? Subnoimptotal { get; set; }

        public decimal? Subexivatotal { get; set; }

        public int? Ambiente { get; set; }

        public bool? Estado { get; set; }

        public int? Tiempocredito { get; set; }

        public int? Tipodocumento { get; set; }

        public int? Idvendedor { get; set; }

        public int? Diasentrega { get; set; }

        public decimal? Comision { get; set; }

        public int? Idempresapagar { get; set; }

        public decimal? Valorapagar { get; set; }

        public int? Ciudad { get; set; }

        public DateTime? Fechaentrega { get; set; }

        public string? Estadopago { get; set; }

        public DateOnly? Fechacancelado { get; set; }

        public string? Edad { get; set; }

        public string? Estadoatendido { get; set; }

        public bool? Contabilizado { get; set; }

        public string? Detalleextra { get; set; }

        public string? Notas { get; set; }

        public string? Piepagina { get; set; }

        public string? Nombread { get; set; }
        [Column("CORREOAD")]
        public string? Correoad { get; set; }

        public string? Direccionad { get; set; }

        public string? Ordencompra { get; set; }

        // Relaciones
        [ForeignKey("Codclientes")]
        public virtual Cliente? CodclientesNavigation { get; set; }

        [ForeignKey("Codemisor")]
        public virtual Emisor? CodemisorNavigation { get; set; }

        public virtual ICollection<Detallefactura> Detallefacturas { get; set; } = new List<Detallefactura>();

        [ForeignKey("Idusuario")]
        public virtual Usuario? IdusuarioNavigation { get; set; }

        // Propiedad de Navegación (Foreign Key)
        [ForeignKey("Coddocumento")]
        public virtual TipoDocumento? CoddocumentoNavigation { get; set; }

        [Column("DescuentoGlobalPct")]
        public decimal? DescuentoGlobalPct { get; set; }

        [Column("DescuentoGlobalValor")]
        public decimal? DescuentoGlobalValor { get; set; }

    }
}