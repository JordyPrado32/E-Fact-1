using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models.EContax;
using Simetric.Services;

namespace Simetric.Services.EContax;

public sealed class EContaxSharedDataService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly EContaxCatalogService _catalogService;
    private readonly EContaxTenantService _tenantService;

    public EContaxSharedDataService(
        IDbContextFactory<AppDbContext> dbFactory,
        EContaxCatalogService catalogService,
        EContaxTenantService tenantService)
    {
        _dbFactory = dbFactory;
        _catalogService = catalogService;
        _tenantService = tenantService;
    }

    public async Task<EContaxSharedDataSnapshot> GetSnapshotAsync(int userId, int maxItems = 8)
    {
        if (userId <= 0)
            throw new InvalidOperationException("La sesion del usuario no es valida.");

        maxItems = Math.Clamp(maxItems, 1, 40);

        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenantService.GetContextAsync(context, userId);

        await _catalogService.EnsureConsumidorFinalAsync(userId);

        var clientesQuery = context.Clientes
            .AsNoTracking()
            .Where(cliente => cliente.Idempresa == userContext.IdEmpresa);

        var productosQuery = context.Productos
            .AsNoTracking()
            .Where(producto => producto.Idempresa == userContext.IdEmpresa);

        if (!userContext.EsJefeEmpresa && userContext.IdSucursal is > 0)
        {
            productosQuery = productosQuery.Where(producto => producto.Idsucursal == userContext.IdSucursal);
        }
        else if (!userContext.EsJefeEmpresa)
        {
            productosQuery = productosQuery.Where(_ => false);
        }

        var emisoresQuery = context.Emisores
            .AsNoTracking()
            .Where(emisor => emisor.IdUsuario == userContext.IdUsuarioTitular);

        var totalClientes = await clientesQuery.CountAsync();
        var totalProductos = await productosQuery.CountAsync();
        var totalEmisores = await emisoresQuery.CountAsync();

        var clientesRaw = await clientesQuery
            .OrderByDescending(cliente => cliente.Codcliente)
            .Take(maxItems)
            .Select(cliente => new
            {
                cliente.Codcliente,
                cliente.Apellidos,
                cliente.Nombres,
                cliente.Nombrecomercial,
                cliente.Nombrerazonsocial,
                cliente.Numeroidentificacion,
                cliente.Correo,
                cliente.Celular,
                cliente.Telefonoconvencional,
                cliente.Estado
            })
            .ToListAsync();

        var productosRaw = await productosQuery
            .OrderByDescending(producto => producto.Codigo)
            .Take(maxItems)
            .Select(producto => new
            {
                producto.Codigo,
                producto.Nombre,
                producto.CodigoPrincipal,
                producto.ValorUnitario,
                producto.Tipocompravena,
                producto.Estado
            })
            .ToListAsync();

        var emisoresRaw = await emisoresQuery
            .OrderByDescending(emisor => emisor.Codigo)
            .Take(maxItems)
            .Select(emisor => new
            {
                emisor.Codigo,
                emisor.RazonSocial,
                emisor.NomComercial,
                emisor.Ruc,
                emisor.Estado
            })
            .ToListAsync();

        var clientes = clientesRaw
            .Select(cliente => new EContaxClientSummary(
                cliente.Codcliente,
                BuildClienteNombre(
                    cliente.Nombrerazonsocial,
                    cliente.Nombrecomercial,
                    cliente.Nombres,
                    cliente.Apellidos),
                Clean(cliente.Numeroidentificacion),
                Clean(cliente.Correo),
                FirstFilled(cliente.Celular, cliente.Telefonoconvencional),
                cliente.Estado == true))
            .ToList();

        var productos = productosRaw
            .Select(producto => new EContaxProductSummary(
                producto.Codigo,
                string.IsNullOrWhiteSpace(producto.Nombre) ? "Producto sin nombre" : producto.Nombre.Trim(),
                Clean(producto.CodigoPrincipal),
                producto.ValorUnitario,
                string.IsNullOrWhiteSpace(producto.Tipocompravena) ? "Sin tipo" : producto.Tipocompravena.Trim(),
                producto.Estado == true))
            .ToList();

        var emisores = emisoresRaw
            .Select(emisor => new EContaxIssuerSummary(
                emisor.Codigo,
                FirstFilled(emisor.RazonSocial, emisor.NomComercial, "Emisor sin nombre"),
                Clean(emisor.Ruc),
                emisor.Estado))
            .ToList();

        return new EContaxSharedDataSnapshot(
            userContext.IdUsuarioTitular,
            totalClientes,
            totalProductos,
            totalEmisores,
            clientes,
            productos,
            emisores);
    }

    private static string BuildClienteNombre(
        string? razonSocial,
        string? nombreComercial,
        string? nombres,
        string? apellidos)
    {
        var nombrePersona = string.Join(" ", new[] { nombres, apellidos }
            .Where(value => !string.IsNullOrWhiteSpace(value)))
            .Trim();

        return FirstFilled(razonSocial, nombreComercial, nombrePersona, "Cliente sin nombre");
    }

    private static string FirstFilled(params string?[] values) =>
        values.Select(Clean).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;
}
