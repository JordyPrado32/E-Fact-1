namespace Simetric.DTOs;

public class LiquidacionCompraPreviewDto
{
    public int? Usuario { get; set; }

    public string ClaveAcceso { get; set; } = "";
    public string NumeroAutorizacion { get; set; } = "";
    public bool EstaAutorizada { get; set; }

    public int Ambiente { get; set; } = 2;
    public string Estab { get; set; } = "001";
    public string PtoEmi { get; set; } = "001";
    public string Secuencial { get; set; } = "";
    public string Serie => $"{Estab}{PtoEmi}";
    public string SerieVisual => $"{Estab}-{PtoEmi}";

    public DateTime? FechaEmision { get; set; } = DateTime.Today;

    public int? CodEmisor { get; set; }
    public string NombreEmisorEncontrado { get; set; } = "";
    public string RucEmisor { get; set; } = "";
    public string RazonSocialEmisor { get; set; } = "";
    public string NombreComercialEmisor { get; set; } = "";
    public string DireccionMatriz { get; set; } = "";
    public string DireccionEstablecimiento { get; set; } = "";
    public string ContribuyenteEspecial { get; set; } = "";
    public string ObligadoContabilidad { get; set; } = "NO";
    public string EmailEmisor { get; set; } = "";
    public string TelefonoEmisor { get; set; } = "";
    public string LogoEmisor { get; set; } = "";

    public string TipoIdentificacionProveedor { get; set; } = "05";
    public string TipoIdentificacionProveedorNombre { get; set; } = "";
    public string IdentificacionProveedor { get; set; } = "";
    public string RazonSocialProveedor { get; set; } = "";
    public string DireccionProveedor { get; set; } = "";
    public string TelefonoFijoProveedor { get; set; } = "";
    public string TelefonoProveedor { get; set; } = "";
    public string EmailProveedor { get; set; } = "";
    public List<string> CorreosAdicionalesProveedor { get; set; } = new();
    public List<string> CorreosAdicionalesProveedorGuardar { get; set; } = new();
    public int? CodProveedor { get; set; }
    public bool EsClienteProveedor { get; set; }
    public bool EsProveedorProveedor { get; set; } = true;
    public string SegmentoCliente { get; set; } = "";
    public string CuentaContableCliente { get; set; } = "";
    public string FuenteOrigenCliente { get; set; } = "";
    public bool TieneLimiteCreditoCliente { get; set; }
    public string CuentaContableProveedor { get; set; } = "";
    public string CodigoProveedorInterno { get; set; } = "";
    public string CreditoTributarioProveedor { get; set; } = "";
    public bool EsSujetoRetencionProveedor { get; set; }
    public bool RegistraInformacionBancariaProveedor { get; set; }
    public string BancoProveedor { get; set; } = "";
    public string TipoCuentaProveedor { get; set; } = "";
    public string NumeroCuentaProveedor { get; set; } = "";

    public string FormaPago { get; set; } = "";
    public string FormaPagoNombre { get; set; } = "";
    public int? Plazo { get; set; }
    public string UnidadTiempo { get; set; } = "dias";
    public string Moneda { get; set; } = "DOLAR";
    public decimal DescuentoGlobalPorcentaje { get; set; }

    public decimal Subtotal0 { get; set; }
    public decimal Subtotal5 { get; set; }
    public decimal Subtotal8 { get; set; }
    public decimal Subtotal15 { get; set; }
    public decimal NoImp { get; set; }
    public decimal ExIva { get; set; }
    public decimal TotalSinImpuestos { get; set; }
    public decimal TotalDescuento { get; set; }
    public decimal Iva5 { get; set; }
    public decimal Iva8 { get; set; }
    public decimal Iva15 { get; set; }
    public decimal IvaTotal { get; set; }
    public decimal ImporteTotal { get; set; }

    public List<LiquidacionCompraDetalleDto> Detalles { get; set; } = new();

    public bool ExisteEmisor => CodEmisor.HasValue && CodEmisor.Value > 0;

    // Alias de compatibilidad para reutilizar el mismo criterio de datos
    // que maneja la compra normal al guardar proveedor/comprobante.
    public string RucProveedor
    {
        get => IdentificacionProveedor;
        set => IdentificacionProveedor = value ?? "";
    }

    public string TipoIdentificacionComprador
    {
        get => TipoIdentificacionProveedor;
        set => TipoIdentificacionProveedor = value ?? "";
    }

    public string TipoIdentificacionCompradorNombre
    {
        get => TipoIdentificacionProveedorNombre;
        set => TipoIdentificacionProveedorNombre = value ?? "";
    }

    public string FechaAutorizacionSri =>
        FechaEmision?.ToString("dd/MM/yyyy") ?? "";
}

public class LiquidacionCompraDetalleDto
{
    public int CodProducto { get; set; }
    public string CodPrincipal { get; set; } = "";
    public string CodAuxiliar { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public decimal Cantidad { get; set; } = 1m;
    public decimal PrecioUnitario { get; set; }
    public decimal PorcentajeDescuento { get; set; }
    public decimal Descuento { get; set; }
    public decimal PrecioTotalSinImpuesto { get; set; }

    public int CodigoPorcentaje { get; set; }
    public int Tarifa { get; set; }

    public decimal ValorIva { get; set; }
    public decimal ValorTotal { get; set; }
}

public class LiquidacionCatalogoOptionDto
{
    public string Codigo { get; set; } = "";
    public string Descripcion { get; set; } = "";
}

public class LiquidacionCompraGuardadoResultadoDto
{
    public int CodFactura { get; set; }
    public string? XmlUrl { get; set; }
    public string? PdfUrl { get; set; }
    public List<string> ProblemasArchivos { get; set; } = new();
    public bool ArchivosGeneradosCorrectamente => ProblemasArchivos.Count == 0;
}
