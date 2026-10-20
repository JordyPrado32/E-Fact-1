using Microsoft.EntityFrameworkCore;
using Simetric.Data;
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
    private readonly EContaxTenantService _tenant;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public EContaxFacturacionService(
        FacturacionService facturacionService,
        IdentificacionService identificacionService,
        EContaxCatalogService catalogService,
        EContaxSeguridadService seguridad,
        EContaxTenantService tenant,
        IDbContextFactory<AppDbContext> dbFactory)
    {
        _facturacionService = facturacionService;
        _identificacionService = identificacionService;
        _catalogService = catalogService;
        _seguridad = seguridad;
        _tenant = tenant;
        _dbFactory = dbFactory;
    }

    public string? UltimoErrorGuardarFactura => _facturacionService.UltimoErrorGuardarFactura;

    public async Task<Caja?> GetCajaUsuarioAsync(int idUsuario)
    {
        var (_, caja) = await ObtenerContextoCajaAsync(idUsuario);
        return caja;
    }

    public async Task<string> GetSerieFacturaVisualAsync(int idUsuario)
    {
        var (_, caja) = await ObtenerContextoCajaAsync(idUsuario);
        return FormatearSerie(caja.SerieFactura, caja.NumCaja);
    }

    public async Task<string> GetSerieFacturaRawAsync(int idUsuario)
    {
        var (_, caja) = await ObtenerContextoCajaAsync(idUsuario);
        return FormatearSerie(caja.SerieFactura, caja.NumCaja).Replace("-", string.Empty);
    }

    public async Task<List<CajaSerieResolucion>> GetSeriesFacturaHabilitadasAsync(int idUsuario)
    {
        var (_, caja) = await ObtenerContextoCajaAsync(idUsuario);
        return [CrearResolucion(caja, caja.SerieFactura)];
    }

    public async Task<List<CajaSerieResolucion>> GetSeriesComprasHabilitadasAsync(int idUsuario)
    {
        var (_, caja) = await ObtenerContextoCajaAsync(idUsuario);
        return [CrearResolucion(caja, caja.SerieCompras ?? caja.SerieFactura)];
    }

    public async Task<List<CajaSerieResolucion>> GetSeriesNotaCreditoHabilitadasAsync(int idUsuario)
    {
        var (_, caja) = await ObtenerContextoCajaAsync(idUsuario);
        return [CrearResolucion(caja, caja.SerieNotasCred ?? caja.SerieFactura)];
    }

    public async Task<List<CajaSerieResolucion>> GetSeriesNotaDebitoHabilitadasAsync(int idUsuario)
    {
        var (_, caja) = await ObtenerContextoCajaAsync(idUsuario);
        return [CrearResolucion(caja, caja.SerieDebitos ?? caja.SerieFactura)];
    }

    public async Task<string> GetSerieNotaCreditoRawAsync(int idUsuario)
    {
        var (_, caja) = await ObtenerContextoCajaAsync(idUsuario);
        return FormatearSerie(caja.SerieNotasCred ?? caja.SerieFactura, caja.NumCaja).Replace("-", string.Empty);
    }

    public async Task<string> GetSerieNotaDebitoRawAsync(int idUsuario)
    {
        var (_, caja) = await ObtenerContextoCajaAsync(idUsuario);
        return FormatearSerie(caja.SerieDebitos ?? caja.SerieFactura, caja.NumCaja).Replace("-", string.Empty);
    }

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

    public async Task<string> GetNextFacturaNumeroAsync(int idUsuario, int? codEmisor = null, string? serieRaw = null)
    {
        var (_, caja) = await ObtenerContextoCajaAsync(idUsuario);
        var serie = FormatearSerie(caja.SerieFactura, caja.NumCaja).Replace("-", string.Empty);
        return await _facturacionService.GetNextFacturaNumeroAsync(idUsuario, codEmisor, serie);
    }

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

        var (contexto, caja) = await ObtenerContextoCajaAsync(idUsuario);
        factura.Idempresa = contexto.IdEmpresa;
        factura.Idsucursal = contexto.IdSucursal;
        factura.Serie = FormatearSerie(caja.SerieFactura, caja.NumCaja).Replace("-", string.Empty);
        return await _facturacionService.GuardarFacturaCompletaAsync(
            idUsuario,
            factura,
            cliente,
            detalles,
            correosFacturaAdicionales);
    }

    private async Task<(EContaxUserContext Contexto, Caja Caja)> ObtenerContextoCajaAsync(int idUsuario)
    {
        var contexto = await _tenant.GetContextAsync(idUsuario);
        if (contexto.IdSucursal is not > 0)
            throw new InvalidOperationException("El usuario de E-Contax no tiene una sucursal asignada.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var cajas = await db.Caja
            .AsNoTracking()
            .Where(x => x.Estado == true &&
                        x.EsCajaSistema != true &&
                        x.IdEmpresa == contexto.IdEmpresa &&
                        x.IdSucursal == contexto.IdSucursal.Value)
            .OrderBy(x => x.NumCaja)
            .ThenBy(x => x.Sec)
            .ToListAsync();

        if (cajas.Count == 0)
            throw new InvalidOperationException("La sucursal no tiene un punto de emisión configurado.");

        if (cajas.Count > 1)
            throw new InvalidOperationException("La sucursal tiene más de un punto de emisión activo. Debe quedar uno solo para emitir en E-Contax.");

        return (contexto, cajas[0]);
    }

    private static CajaSerieResolucion CrearResolucion(Caja caja, string? serie)
    {
        var serieVisual = FormatearSerie(serie, caja.NumCaja);
        var partes = serieVisual.Split('-');
        return new CajaSerieResolucion(
            caja.IdUsuario ?? 0,
            caja.Sec,
            caja.IdUsuario ?? 0,
            caja.NumCaja ?? 0,
            partes[0],
            partes[1],
            serieVisual,
            serieVisual.Replace("-", string.Empty));
    }

    private static string FormatearSerie(string? serie, int? numeroCaja)
    {
        var digitos = new string((serie ?? string.Empty).Where(char.IsDigit).ToArray());
        var establecimiento = digitos.Length >= 3 ? digitos[..3] : "001";
        var punto = digitos.Length >= 6
            ? digitos.Substring(3, 3)
            : (numeroCaja is > 0 ? numeroCaja.Value.ToString("D3") : "001");
        return $"{establecimiento}-{punto}";
    }
}
