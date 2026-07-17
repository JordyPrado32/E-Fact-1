using Simetric.DTOs;
using Simetric.Models;

namespace Simetric.Services.EContax;

public sealed class EContaxFacturacionService
{
    private readonly FacturacionService _facturacionService;
    private readonly IdentificacionService _identificacionService;
    private readonly EContaxCatalogService _catalogService;

    public EContaxFacturacionService(
        FacturacionService facturacionService,
        IdentificacionService identificacionService,
        EContaxCatalogService catalogService)
    {
        _facturacionService = facturacionService;
        _identificacionService = identificacionService;
        _catalogService = catalogService;
    }

    public string? UltimoErrorGuardarFactura => _facturacionService.UltimoErrorGuardarFactura;

    public Task<Caja?> GetCajaUsuarioAsync(int idUsuario) =>
        _facturacionService.GetCajaUsuarioAsync(idUsuario);

    public Task<string> GetSerieFacturaVisualAsync(int idUsuario) =>
        _facturacionService.GetSerieFacturaVisualAsync(idUsuario);

    public Task<List<Emisor>> GetEmisoresActivosAsync(int idUsuario) =>
        _facturacionService.GetEmisoresActivosAsync(idUsuario);

    public Task<List<FormasPago>> ObtenerFormasPagoAsync() =>
        _facturacionService.ObtenerFormasPagoAsync();

    public Task<List<Tipocliente>> GetTiposClienteAsync() =>
        _facturacionService.GetTiposClienteAsync();

    public Task<List<Identificacion>> GetIdentificacionesActivasAsync() =>
        _identificacionService.GetAllActiveAsync();

    public Task<List<Pais>> GetPaisesAsync() =>
        _facturacionService.GetPaisesAsync();

    public Task<bool> DebePreguntarSecuenciaInicialAsync(int idUsuario, int? codEmisor) =>
        _facturacionService.DebePreguntarSecuenciaInicialAsync(idUsuario, codEmisor);

    public Task<string> GetNextFacturaNumeroAsync(int idUsuario, int? codEmisor = null) =>
        _facturacionService.GetNextFacturaNumeroAsync(idUsuario, codEmisor);

    public Task ConfigurarSecuenciaInicialFacturaAsync(
        int idUsuario,
        bool usuarioYaFacturoAntes,
        string? secuenciaAnterior,
        int? codEmisor = null) =>
        _facturacionService.ConfigurarSecuenciaInicialFacturaAsync(
            idUsuario,
            usuarioYaFacturoAntes,
            secuenciaAnterior,
            codEmisor);

    public Task<List<Cliente>> BuscarClientesFiltroAsync(int idUsuario, string filtro) =>
        _catalogService.BuscarClientesFiltroAsync(idUsuario, filtro);

    public Task<Cliente?> GetClienteByIdentificacionAsync(int idUsuario, string identificacion) =>
        _catalogService.GetClienteByIdentificacionAsync(idUsuario, identificacion);

    public Task<Cliente> UpsertClienteAsync(int idUsuario, Cliente cliente) =>
        _catalogService.UpsertClienteAsync(idUsuario, cliente);

    public Task<List<Provincia>> GetProvinciasByPaisAsync(int idPais) =>
        _facturacionService.GetProvinciasByPaisAsync(idPais);

    public Task<List<Ciudad>> GetCiudadesByProvinciaAsync(int idProvincia) =>
        _facturacionService.GetCiudadesByProvinciaAsync(idProvincia);

    public Task<List<string>> GetCorreosAdicionalesClienteAsync(int idUsuario, int codCliente) =>
        _catalogService.GetCorreosAdicionalesClienteAsync(idUsuario, codCliente);

    public Task<List<ProductoLookupDetalleDto>> BuscarProductosFiltroAsync(int idUsuario, string filtro) =>
        _catalogService.BuscarProductosFiltroAsync(idUsuario, filtro);

    public Task<ProductoLookupDetalleDto?> BuscarProductoParaDetalleAsync(int idUsuario, string criterio) =>
        _catalogService.BuscarProductoParaDetalleAsync(idUsuario, criterio);

    public Task<bool> GuardarFacturaCompletaAsync(
        int idUsuario,
        Factura factura,
        Cliente cliente,
        List<Detallefactura> detalles,
        List<FacturaCorreoDestinoDto>? correosFacturaAdicionales = null) =>
        _facturacionService.GuardarFacturaCompletaAsync(
            idUsuario,
            factura,
            cliente,
            detalles,
            correosFacturaAdicionales);
}
