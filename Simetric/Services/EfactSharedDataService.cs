using Microsoft.EntityFrameworkCore;
using Simetric.Data;

namespace Simetric.Services;

public sealed class EfactSharedDataService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ClienteService _clienteService;

    public EfactSharedDataService(
        IDbContextFactory<AppDbContext> dbFactory,
        ClienteService clienteService)
    {
        _dbFactory = dbFactory;
        _clienteService = clienteService;
    }

    public async Task<EfactSharedDataSnapshot> GetSnapshotAsync(int userId, int maxItems = 8)
    {
        if (userId <= 0)
            throw new InvalidOperationException("La sesion del usuario no es valida.");

        maxItems = Math.Clamp(maxItems, 1, 40);

        await using var context = await _dbFactory.CreateDbContextAsync();
        var ownerId = await ResolveOwnerUserIdAsync(context, userId);
        if (ownerId is null)
            throw new InvalidOperationException("No se encontro el usuario.");

        await _clienteService.EnsureConsumidorFinalAsync(ownerId.Value);

        var clientesQuery = context.Clientes
            .AsNoTracking()
            .Where(cliente => cliente.Usuario == ownerId.Value);

        var productosQuery = context.Productos
            .AsNoTracking()
            .Where(producto => producto.Idusuario == ownerId.Value);

        var emisoresQuery = context.Emisores
            .AsNoTracking()
            .Where(emisor => emisor.IdUsuario == ownerId.Value);

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
            .Select(cliente => new EfactClientSummary(
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
            .Select(producto => new EfactProductSummary(
                producto.Codigo,
                string.IsNullOrWhiteSpace(producto.Nombre) ? "Producto sin nombre" : producto.Nombre.Trim(),
                Clean(producto.CodigoPrincipal),
                producto.ValorUnitario,
                string.IsNullOrWhiteSpace(producto.Tipocompravena) ? "Sin tipo" : producto.Tipocompravena.Trim(),
                producto.Estado == true))
            .ToList();

        var emisores = emisoresRaw
            .Select(emisor => new EfactIssuerSummary(
                emisor.Codigo,
                FirstFilled(emisor.RazonSocial, emisor.NomComercial, "Emisor sin nombre"),
                Clean(emisor.Ruc),
                emisor.Estado))
            .ToList();

        return new EfactSharedDataSnapshot(
            ownerId.Value,
            totalClientes,
            totalProductos,
            totalEmisores,
            clientes,
            productos,
            emisores);
    }

    private static async Task<int?> ResolveOwnerUserIdAsync(AppDbContext context, int userId)
    {
        var user = await context.Usuarios
            .AsNoTracking()
            .Where(usuario => usuario.IdUsuario == userId)
            .Select(usuario => new { usuario.IdUsuario, usuario.idJefe })
            .FirstOrDefaultAsync();

        return user is null ? null : user.idJefe ?? user.IdUsuario;
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

public sealed record EfactSharedDataSnapshot(
    int OwnerUserId,
    int TotalClientes,
    int TotalProductos,
    int TotalEmisores,
    IReadOnlyList<EfactClientSummary> Clientes,
    IReadOnlyList<EfactProductSummary> Productos,
    IReadOnlyList<EfactIssuerSummary> Emisores);

public sealed record EfactClientSummary(
    int Id,
    string Nombre,
    string Identificacion,
    string Correo,
    string Telefono,
    bool Activo);

public sealed record EfactProductSummary(
    int Codigo,
    string Nombre,
    string CodigoPrincipal,
    decimal? ValorUnitario,
    string Tipo,
    bool Activo);

public sealed record EfactIssuerSummary(
    int Codigo,
    string Nombre,
    string Ruc,
    bool Activo);
