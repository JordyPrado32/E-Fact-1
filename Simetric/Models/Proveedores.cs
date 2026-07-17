namespace Simetric.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("PROVEEDORES")]
    public class Proveedor
    {
        [Key]
        public int idProveedor { get; set; }

        [StringLength(50)]
        public string? primerApellido { get; set; }

        [StringLength(50)]
        public string? segundoApellido { get; set; }

        [StringLength(50)]
        public string? primerNombre { get; set; }

        [StringLength(50)]
        public string? segundoNombre { get; set; }

        [StringLength(150)]
        public string? nombreComercial { get; set; }

        [StringLength(150)]
        public string? nombre { get; set; }

        [StringLength(13)]
        public string? ruc { get; set; }

        public char? contribuyenteEspecial { get; set; }

        [StringLength(500)]
        public string? direccion { get; set; }

        [StringLength(50)]
        public string? telefono { get; set; }

        [StringLength(50)]
        public string? telefonoMovil { get; set; }

        [StringLength(150)]
        public string? email { get; set; }

        public char? personaNatural { get; set; }

        public bool? estado { get; set; } // bit en SQL

        [StringLength(50)]
        public string? tipoCuenta { get; set; }

        [StringLength(50)]
        public string? numeroCuenta { get; set; }

        public int? institucionFin { get; set; }

        [StringLength(150)]
        public string? aNombreDe { get; set; }

        public bool? cheque { get; set; } // bit en SQL

        [StringLength(2)]
        public string? tipoIdentificacion { get; set; }

        public DateTime? fechaActualizacion { get; set; }

        [StringLength(1000)]
        public string? actividadContribuyente { get; set; }

        [StringLength(50)]
        public string? clasificacionPyme { get; set; }

        public char? obligado { get; set; }

        public int? retIva { get; set; }

        public int? retFuente { get; set; }

        [StringLength(50)]
        public string? cuentaContable { get; set; }

        public bool? llevaRetencion { get; set; } // bit en SQL

        [StringLength(50)]
        public string? cuentaAnticipo { get; set; }

        // Columnas de la segunda imagen
        public int? idRetIva1 { get; set; }

        [StringLength(5)]
        public string? retIva1 { get; set; }

        public int? idRetIva2 { get; set; }

        [StringLength(5)]
        public string? retIva2 { get; set; }

        public int? idRetFuente1 { get; set; }

        [StringLength(5)]
        public string? retFuente1 { get; set; }

        public int? idRetFuente2 { get; set; }

        [StringLength(5)]
        public string? retFuente2 { get; set; }

        [StringLength(500)]
        public string? detalle { get; set; }

        [StringLength(50)]
        public string? tipoContribuyente { get; set; }

        [StringLength(50)]
        public string? ingresosFactura { get; set; }

        [StringLength(50)]
        public string? formaPago { get; set; }

        public int? plazoPago { get; set; }

        public decimal? saldoInicial { get; set; }

        [StringLength(20)]
        public string? tipoProvision { get; set; }
    }
}
