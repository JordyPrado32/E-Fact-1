using Simetric.DTOs;
using Simetric.Models;
using Simetric.Models.EContax;

namespace Simetric.Services.EContax;

public sealed class EContaxFacturacionService
{
    private readonly FacturacionService _facturacionService;
    private readonly IdentificacionService _identificacionService;
    private readonly EContaxCatalogService _catalogService;
    private readonly EContaxSeguridadService _seguridad;

    public EContaxFacturacionService(
        FacturacionService facturacionService,
        IdentificacionService identificacionService,
        EContaxCatalogService catalogService, EContaxSeguridadService seguridad)
    {
        _facturacionService = facturacionService;
        _identificacionService = identificacionService;
        _catalogService = catalogService;
        _seguridad = seguridad;
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

    public async Task ConfigurarSecuenciaInicialFacturaAsync(
        int idUsuario,
        bool usuarioYaFacturoAntes,
        string? secuenciaAnterior,
        int? codEmisor = null)
    {
        await _seguridad.ExigirAccionAsync(idUsuario, EContaxRoutes.FacturacionNueva, EContaxAccion.Crear);
        await _facturacionService.ConfigurarSecuenciaInicialFacturaAsync(
            idUsuario,
            usuarioYaFacturoAntes,
            secuenciaAnterior,
            codEmisor);
    }

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

    public async Task<List<FacturaListDto>> ListarFacturasAsync(int idUsuario, int top = 500)
    {
        await _seguridad.ExigirAccionAsync(idUsuario, EContaxRoutes.FacturacionDocumentos, EContaxAccion.Ver);
        return await _facturacionService.ListarFacturasUsuarioAsync(idUsuario, top);
    }

    public Task<ProductoLookupDetalleDto?> BuscarProductoParaDetalleAsync(int idUsuario, string criterio) =>
        _catalogService.BuscarProductoParaDetalleAsync(idUsuario, criterio);

    public async Task<bool> GuardarFacturaCompletaAsync(
        int idUsuario,
        Factura factura,
        Cliente cliente,
        List<Detallefactura> detalles,
        List<FacturaCorreoDestinoDto>? correosFacturaAdicionales = null)
    {
        await _seguridad.ExigirAccionAsync(idUsuario, EContaxRoutes.FacturacionNueva, EContaxAccion.Crear);
        var actual = await _catalogService.GetClienteByIdentificacionAsync(idUsuario, cliente.Numeroidentificacion ?? "");
        if (actual is null)
            await _seguridad.ExigirAccionAsync(idUsuario, EContaxRoutes.Clientes,
                cliente.Codcliente > 0 ? EContaxAccion.Editar : EContaxAccion.Crear);
        else if ((cliente.Codcliente > 0 && actual.Codcliente != cliente.Codcliente) ||
            actual.Estado != true || actual.Nombres != cliente.Nombres || actual.Apellidos != cliente.Apellidos ||
            actual.Nombrerazonsocial != cliente.Nombrerazonsocial || actual.Nombrecomercial != cliente.Nombrecomercial ||
            actual.Correo != cliente.Correo || actual.Celular != cliente.Celular || actual.Telefonoconvencional != cliente.Telefonoconvencional ||
            actual.Direccion != cliente.Direccion || actual.Referencia != cliente.Referencia || actual.Observaciones != cliente.Observaciones ||
            actual.TipoCliente != cliente.TipoCliente || actual.Tipoidentificacion != cliente.Tipoidentificacion || actual.Pais != cliente.Pais ||
            actual.Provincia != cliente.Provincia || actual.Ciudad != cliente.Ciudad || actual.Oblgconta != cliente.Oblgconta ||
            correosFacturaAdicionales?.Any(x => x.GuardarEnCliente) == true)
            await _seguridad.ExigirAccionAsync(idUsuario, EContaxRoutes.Clientes, EContaxAccion.Editar);
        return await _facturacionService.GuardarFacturaCompletaAsync(
            idUsuario,
            factura,
            cliente,
            detalles,
            correosFacturaAdicionales);
    }
}
