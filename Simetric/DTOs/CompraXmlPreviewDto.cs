namespace Simetric.DTOs;

public class CompraXmlPreviewDto
{
    public string XmlOriginal { get; set; } = "";
    public int? Usuario { get; set; }

    public string ClaveAccesoRetencion { get; set; } = "";
    public string NombreArchivoRetencion { get; set; } = "";
    // Info tributaria
    public string ClaveAcceso { get; set; } = "";
    public string Estab { get; set; } = "";
    public string PtoEmi { get; set; } = "";
    public string Secuencial { get; set; } = "";
    public string Serie => $"{Estab}{PtoEmi}";
    public string RucProveedor { get; set; } = "";
    public string RazonSocialProveedor { get; set; } = "";
    public int Ambiente { get; set; }

    // Direcciones XML
    public string RucEmisor { get; set; } = "";
    public string DireccionMatriz { get; set; } = "";
    public string DireccionEstablecimiento { get; set; } = "";

    // Datos proveedor desde XML
    public string DireccionProveedor { get; set; } = "";
    public string TelefonoProveedor { get; set; } = "";
    public string TelefonoFijoProveedor { get; set; } = "";
    public string EmailProveedor { get; set; } = "";

    public bool EsManual { get; set; }

    // Info factura
    public string IdentificacionComprador { get; set; } = "";
    public string TipoIdentificacionComprador { get; set; } = "";
    public string TipoIdentificacionCompradorNombre { get; set; } = "";
    public string ObligadoContabilidad { get; set; } = "";
    public DateTime? FechaEmision { get; set; }
    public DateTime? FechaEmisionDocumentoSustento { get; set; }
    public decimal TotalSinImpuestos { get; set; }
    public decimal TotalDescuento { get; set; }
    public decimal ImporteTotal { get; set; }
    public string Moneda { get; set; } = "";
    public string FormaPago { get; set; } = "";
    public string FormaPagoNombre { get; set; } = "";
    public string GuiaRemision { get; set; } = "";

    // Retención
    public string NumeroRetencionGenerado { get; set; } = "";

    // Autorización
    public string NumeroAutorizacion { get; set; } = "";
    public string FechaAutorizacionSri { get; set; } = "";

    // Totales calculados
    public decimal Subtotal12 { get; set; }
    public decimal Subtotal0 { get; set; }
    public decimal Subtotal5 { get; set; }
    public decimal Subtotal8 { get; set; }
    public decimal NoImp { get; set; }
    public decimal ExIva { get; set; }
    public decimal Iva { get; set; }
    public decimal Iva5 { get; set; }
    public decimal Iva8 { get; set; }

    // Datos encontrados en sistema
    public int? CodEmisor { get; set; }
    public string NombreEmisorEncontrado { get; set; } = "";
    public int? CodProveedor { get; set; }

    public bool ExisteEmisor => CodEmisor.HasValue;
    public bool YaImportado { get; set; }
    public int? CodFacturaExistente { get; set; }
    public bool TieneRetencionGenerada { get; set; }

    public List<CompraXmlDetalleDto> Detalles { get; set; } = new();
    public List<CompraRetValorDto> Retenciones { get; set; } = new();
}

public class CompraXmlDetalleDto
{
    public string CodPrincipal { get; set; } = "";
    public string CodAuxiliar { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Descuento { get; set; }
    public decimal PrecioTotalSinImpuesto { get; set; }

    public int CodImp { get; set; }
    public int PorImp { get; set; }
    public int Tarifa { get; set; }

    public decimal ValorIVA { get; set; }
    public decimal ValorICE { get; set; }
    public decimal ValorTotal { get; set; }

    public bool EsBien { get; set; }
    public string RetencionIvaCodigo { get; set; } = "";
    public string RetencionIvaDescripcion { get; set; } = "";
    public decimal RetencionIvaPorcentaje { get; set; }
    public decimal RetencionIvaValor { get; set; }
    public string RetencionRentaCodigo { get; set; } = "";
    public string RetencionRentaDescripcion { get; set; } = "";
    public decimal RetencionRentaPorcentaje { get; set; }
    public decimal RetencionRentaValor { get; set; }
}

public class CompraRetValorDto
{
    public int? Sec { get; set; }
    public int? IdRet { get; set; }
    public string CodigoRetencion { get; set; } = "";

    public decimal? Valor { get; set; }
    public decimal? Base { get; set; }

    public decimal? PorcentajeRetencion { get; set; }
    public decimal? ValorRetenido { get; set; }

    public string? Tipo { get; set; }
    public string? DescripcionRet { get; set; }
    public bool? Estado { get; set; }
    public string? Serie { get; set; }
    public int? NumSri { get; set; }
    public string? Autorizacion { get; set; }
    public int? IdRetencionInfo { get; set; }
    public List<BaseParcialRetencionDto> BasesParciales { get; set; } = new();
}
