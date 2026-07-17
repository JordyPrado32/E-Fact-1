using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Modules.AsistenteIAFacturacion.DTOs;
using Simetric.Services;
using System.Collections.Concurrent;

namespace Simetric.Modules.AsistenteIAFacturacion.Services;

public sealed class SystemProductoServiceAdapter : IProductoService
{
    private static readonly TimeSpan OwnerCacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TarifaCacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<int, CachedValue<int>> OwnerCache = new();
    private static CachedValue<IReadOnlyDictionary<string, decimal>>? _tarifaMapCache;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public SystemProductoServiceAdapter(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<ProductoDto>> BuscarAsync(int userId, string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<ProductoDto>();

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var ownerId = await ResolveOwnerIdAsync(context, userId, cancellationToken);
        if (ownerId <= 0)
            return Array.Empty<ProductoDto>();

        var searchTerms = SearchMatchHelper.BuildSearchTerms(query);
        var tarifaMap = await LoadTarifaMapAsync(context, cancellationToken);
        var baseQuery = context.Productos
            .AsNoTracking()
            .Include(p => p.TipoProductoNavigation)
            .Include(p => p.IdsubtipoNavigation)
            .Where(p => p.Idusuario == ownerId && (p.Estado == null || p.Estado == true));

        var productos = new List<Models.Producto>();
        foreach (var term in searchTerms)
        {
            productos.AddRange(await baseQuery
                .Where(p =>
                    EF.Functions.Like((p.Nombre ?? string.Empty).ToLower(), $"%{term}%") ||
                    EF.Functions.Like((p.CodigoPrincipal ?? string.Empty).ToLower(), $"%{term}%") ||
                    EF.Functions.Like((p.CodAuxiliar ?? string.Empty).ToLower(), $"%{term}%") ||
                    EF.Functions.Like((p.Tipocompravena ?? string.Empty).ToLower(), $"%{term}%") ||
                    EF.Functions.Like((p.TipoProductoNavigation!.Descripcion ?? string.Empty).ToLower(), $"%{term}%") ||
                    EF.Functions.Like((p.IdsubtipoNavigation!.Descripcion ?? string.Empty).ToLower(), $"%{term}%"))
                .Take(30)
                .ToListAsync(cancellationToken));
        }

        return productos
            .GroupBy(x => x.Codigo)
            .Select(x => x.First())
            .Select(producto => new
            {
                Producto = producto,
                Score = SearchMatchHelper.Score(query,
                    producto.CodigoPrincipal,
                    producto.CodAuxiliar,
                    producto.Nombre,
                    producto.TipoProductoNavigation?.Descripcion,
                    producto.IdsubtipoNavigation?.Descripcion,
                    producto.Tipocompravena)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Producto.Nombre)
            .Take(8)
            .Select(x => Map(x.Producto, tarifaMap))
            .ToList();
    }

    public async Task<ProductoDto?> ObtenerAsync(int userId, int productoId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var ownerId = await ResolveOwnerIdAsync(context, userId, cancellationToken);
        if (ownerId <= 0)
            return null;

        var tarifaMap = await LoadTarifaMapAsync(context, cancellationToken);
        var producto = await context.Productos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Codigo == productoId && p.Idusuario == ownerId, cancellationToken);

        return producto is null ? null : Map(producto, tarifaMap);
    }

    public async Task<(ProductoDto? Producto, string Message)> CrearAsync(int userId, ProductoCreateRequestDto request, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var ownerId = await ResolveOwnerIdAsync(context, userId, cancellationToken);
        if (ownerId <= 0)
            return (null, "No pude resolver el usuario propietario para crear el producto.");

        var nombre = request.Nombre?.Trim();
        var tipo = string.IsNullOrWhiteSpace(request.Tipo) ? "PRODUCTO" : request.Tipo.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(nombre))
            return (null, "Para crear el producto necesito al menos el nombre.");
        if (!request.PrecioUnitario.HasValue || request.PrecioUnitario.Value <= 0m)
            return (null, "Para crear el producto necesito un precio unitario mayor a cero.");
        if (tipo is not ("PRODUCTO" or "SERVICIO"))
            return (null, "El tipo del producto debe ser PRODUCTO o SERVICIO.");

        var sugerencias = await BuscarAsync(userId, nombre, cancellationToken);
        var posiblesDuplicados = sugerencias
            .Where(x => SearchMatchHelper.IsLikelyDuplicate(nombre, x.Nombre, x.CodigoPrincipal))
            .Take(3)
            .ToList();
        if (posiblesDuplicados.Count > 0)
        {
            var opciones = string.Join("; ", posiblesDuplicados.Select(x => $"{x.Nombre} ({x.CodigoPrincipal ?? "sin código"})"));
            return (null, $"Encontré productos muy parecidos antes de crear uno nuevo: {opciones}. Si sí deseas crear otro, indícame un nombre o código más específico.");
        }

        var tarifa = TaxRateHelper.NormalizePercent(request.TarifaPorcentaje ?? 0m);
        var porcentajeCodigo = await ResolvePorcentajeCodigoAsync(context, tarifa, cancellationToken);
        if (tarifa > 0m && string.IsNullOrWhiteSpace(porcentajeCodigo))
            return (null, $"No encontré una tarifa de IVA configurada para {tarifa:0}%.");

        var codigoPrincipal = string.IsNullOrWhiteSpace(request.CodigoPrincipal)
            ? $"IA-{DateTime.UtcNow:yyyyMMddHHmmss}"
            : request.CodigoPrincipal.Trim();

        var existente = await context.Productos
            .AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.Idusuario == ownerId &&
                (p.Estado == null || p.Estado == true) &&
                ((p.CodigoPrincipal ?? string.Empty) == codigoPrincipal || (p.Nombre ?? string.Empty) == nombre),
                cancellationToken);
        if (existente is not null)
        {
            var tarifaExistenteMap = await LoadTarifaMapAsync(context, cancellationToken);
            return (Map(existente, tarifaExistenteMap), $"Ya existía el producto {existente.Nombre}.");
        }

        var entity = new Models.Producto
        {
            Idusuario = ownerId,
            Nombre = nombre,
            CodigoPrincipal = codigoPrincipal,
            ValorUnitario = TaxRateHelper.NormalizeMoney(request.PrecioUnitario.Value),
            Tipocompravena = tipo,
            Estado = true,
            Observacion = string.IsNullOrWhiteSpace(request.Observacion) ? "Creado por Asistente IA" : request.Observacion.Trim(),
            Codigoimpuesto = TaxRateHelper.ResolveSriTaxCode(tarifa),
            Porcentajeimpuesto = porcentajeCodigo ?? "0"
        };

        context.Productos.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        var tarifaMap = await LoadTarifaMapAsync(context, cancellationToken);
        return (Map(entity, tarifaMap), $"Creé el {tipo.ToLowerInvariant()} {nombre} con IVA {tarifa:0}%.");
    }

    private static ProductoDto Map(Models.Producto producto, IReadOnlyDictionary<string, decimal> tarifaMap)
    {
        var tarifa = tarifaMap.TryGetValue(producto.Porcentajeimpuesto?.Trim() ?? string.Empty, out var value)
            ? value
            : TaxRateHelper.ParsePercentOrZero(producto.Porcentajeimpuesto);

        return new ProductoDto
        {
            Id = producto.Codigo,
            Nombre = producto.Nombre?.Trim() ?? "Producto sin nombre",
            CodigoPrincipal = producto.CodigoPrincipal,
            Categoria = producto.TipoProductoNavigation?.Descripcion?.Trim(),
            Subcategoria = producto.IdsubtipoNavigation?.Descripcion?.Trim(),
            PrecioUnitario = decimal.Round(producto.ValorUnitario ?? 0m, 2),
            TarifaPorcentaje = tarifa,
            Tipo = producto.Tipocompravena
        };
    }

    private static async Task<int> ResolveOwnerIdAsync(AppDbContext context, int userId, CancellationToken cancellationToken)
    {
        if (TryGetValidCacheValue(OwnerCache, userId, out var cachedOwnerId))
            return cachedOwnerId;

        var user = await context.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == userId)
            .Select(u => new { u.IdUsuario, u.idJefe })
            .FirstOrDefaultAsync(cancellationToken);

        var ownerId = user?.idJefe ?? user?.IdUsuario ?? 0;
        OwnerCache[userId] = new CachedValue<int>(ownerId, DateTimeOffset.UtcNow.Add(OwnerCacheLifetime));
        return ownerId;
    }

    private static async Task<Dictionary<string, decimal>> LoadTarifaMapAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        if (_tarifaMapCache is { } cache && cache.ExpiresAt > DateTimeOffset.UtcNow)
            return cache.Value.ToDictionary(x => x.Key, x => x.Value);

        var tarifaMap = await context.Porcentajeivas
            .AsNoTracking()
            .Where(x => x.Codigo != null && (x.Estado == "A" || x.Estado == "1"))
            .ToDictionaryAsync(
                x => x.Codigo!.Trim(),
                x => TaxRateHelper.ParsePercentOrZero(x.Valor),
                cancellationToken);

        _tarifaMapCache = new CachedValue<IReadOnlyDictionary<string, decimal>>(tarifaMap, DateTimeOffset.UtcNow.Add(TarifaCacheLifetime));
        return tarifaMap;
    }

    private static async Task<string?> ResolvePorcentajeCodigoAsync(AppDbContext context, decimal tarifa, CancellationToken cancellationToken)
    {
        var coincidencias = await context.Porcentajeivas
            .AsNoTracking()
            .Where(x => x.Codigo != null && (x.Estado == "A" || x.Estado == "1"))
            .Select(x => new { x.Codigo, x.Valor })
            .ToListAsync(cancellationToken);

        if (tarifa <= 0m)
        {
            return coincidencias
                .Where(x => TaxRateHelper.ParsePercentOrZero(x.Valor) <= 0m)
                .Select(x => x.Codigo!.Trim())
                .FirstOrDefault();
        }

        return coincidencias
            .FirstOrDefault(x => TaxRateHelper.NormalizePercent(TaxRateHelper.ParsePercentOrZero(x.Valor)) == tarifa)
            ?.Codigo?
            .Trim();
    }

    private static bool TryGetValidCacheValue<T>(ConcurrentDictionary<int, CachedValue<T>> cache, int key, out T value)
    {
        if (cache.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            value = entry.Value;
            return true;
        }

        value = default!;
        return false;
    }

    private readonly record struct CachedValue<T>(T Value, DateTimeOffset ExpiresAt);
}
