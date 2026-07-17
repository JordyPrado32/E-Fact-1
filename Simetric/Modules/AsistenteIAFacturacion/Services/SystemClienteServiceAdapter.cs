using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Modules.AsistenteIAFacturacion.DTOs;
using Simetric.Services;
using System.Collections.Concurrent;

namespace Simetric.Modules.AsistenteIAFacturacion.Services;

public sealed class SystemClienteServiceAdapter : IClienteService
{
    private static readonly TimeSpan OwnerCacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ConsumidorFinalWarmLifetime = TimeSpan.FromMinutes(2);
    private static readonly ConcurrentDictionary<int, CachedValue<int>> OwnerCache = new();
    private static readonly ConcurrentDictionary<int, DateTimeOffset> ConsumidorFinalWarmCache = new();

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly Simetric.Services.ClienteService _clienteService;

    public SystemClienteServiceAdapter(IDbContextFactory<AppDbContext> dbFactory, Simetric.Services.ClienteService clienteService)
    {
        _dbFactory = dbFactory;
        _clienteService = clienteService;
    }

    public async Task<IReadOnlyList<ClienteDto>> BuscarAsync(int userId, string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<ClienteDto>();

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var ownerId = await ResolveOwnerIdAsync(context, userId, cancellationToken);
        if (ownerId <= 0)
            return Array.Empty<ClienteDto>();

        await EnsureConsumidorFinalWarmAsync(ownerId);

        var searchTerms = SearchMatchHelper.BuildSearchTerms(query);
        var baseQuery = context.Clientes
            .AsNoTracking()
            .Where(c => c.Usuario == ownerId && (c.Estado == null || c.Estado == true));

        var clientes = new List<Models.Cliente>();
        foreach (var term in searchTerms)
        {
            clientes.AddRange(await baseQuery
                .Where(c =>
                    EF.Functions.Like((c.Numeroidentificacion ?? string.Empty).ToLower(), $"%{term}%") ||
                    EF.Functions.Like((c.Numcontribuyente ?? string.Empty).ToLower(), $"%{term}%") ||
                    EF.Functions.Like((c.Referencia ?? string.Empty).ToLower(), $"%{term}%") ||
                    EF.Functions.Like(((c.Nombres ?? string.Empty) + " " + (c.Apellidos ?? string.Empty)).ToLower(), $"%{term}%") ||
                    EF.Functions.Like((c.Nombrerazonsocial ?? string.Empty).ToLower(), $"%{term}%") ||
                    EF.Functions.Like((c.Nombrecomercial ?? string.Empty).ToLower(), $"%{term}%"))
                .Take(30)
                .ToListAsync(cancellationToken));
        }

        return clientes
            .GroupBy(x => x.Codcliente)
            .Select(x => x.First())
            .Select(cliente => new
            {
                Cliente = cliente,
                Score = SearchMatchHelper.Score(query,
                    cliente.Numeroidentificacion,
                    cliente.Numcontribuyente,
                    cliente.Referencia,
                    $"{cliente.Nombres} {cliente.Apellidos}",
                    cliente.Nombrerazonsocial,
                    cliente.Nombrecomercial)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Cliente.Nombrerazonsocial)
            .ThenBy(x => x.Cliente.Nombres)
            .Take(8)
            .Select(x => Map(x.Cliente))
            .ToList();
    }

    public async Task<ClienteDto?> ObtenerAsync(int userId, int clienteId, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var ownerId = await ResolveOwnerIdAsync(context, userId, cancellationToken);
        if (ownerId <= 0)
            return null;

        var cliente = await context.Clientes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Codcliente == clienteId && c.Usuario == ownerId, cancellationToken);

        return cliente is null ? null : Map(cliente);
    }

    public async Task<(ClienteDto? Cliente, string Message)> CrearAsync(int userId, ClienteCreateRequestDto request, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var ownerId = await ResolveOwnerIdAsync(context, userId, cancellationToken);
        if (ownerId <= 0)
            return (null, "No pude resolver el usuario propietario para crear el cliente.");

        await EnsureConsumidorFinalWarmAsync(ownerId);

        var missingFields = ValidateCreateRequest(request);
        if (missingFields.Count > 0)
            return (null, $"Para crear el cliente faltan estos datos: {string.Join(", ", missingFields)}.");

        var nombreConsulta = request.EsEmpresa == true
            ? request.RazonSocial ?? request.NombreComercial ?? request.Identificacion
            : string.Join(" ", new[] { request.Nombres, request.Apellidos }.Where(x => !string.IsNullOrWhiteSpace(x)));

        var sugerencias = await BuscarAsync(userId, nombreConsulta ?? request.Identificacion ?? string.Empty, cancellationToken);
        var posiblesDuplicados = sugerencias
            .Where(x => SearchMatchHelper.IsLikelyDuplicate(nombreConsulta ?? request.Identificacion ?? string.Empty, x.Nombre, x.Identificacion))
            .Take(3)
            .ToList();
        if (posiblesDuplicados.Count > 0)
        {
            var opciones = string.Join("; ", posiblesDuplicados.Select(x => $"{x.Nombre} ({x.Identificacion ?? "sin identificación"})"));
            return (null, $"Encontré clientes muy parecidos antes de crear uno nuevo: {opciones}. Si sí deseas uno diferente, dame un dato más distintivo.");
        }

        var identificacion = request.Identificacion!.Trim();
        var existente = await context.Clientes
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.Usuario == ownerId &&
                (c.Estado == null || c.Estado == true) &&
                c.Numeroidentificacion == identificacion,
                cancellationToken);
        if (existente is not null)
            return (Map(existente), $"Ya existía el cliente {ResolveDisplayName(existente)} con identificación {identificacion}.");

        var esEmpresa = request.EsEmpresa ?? false;
        var tipoCliente = await ResolveTipoClienteAsync(context, esEmpresa, cancellationToken);
        if (tipoCliente <= 0)
            return (null, "No encontré un tipo de cliente válido para crear el registro.");

        var ideSec = await ResolveTipoIdentificacionAsync(context, identificacion, cancellationToken);
        if (ideSec <= 0)
            return (null, "No pude determinar el tipo de identificación correcto para el cliente.");

        var tipoIdentificacionCodigo = await context.Identificacion
            .Where(i => i.IdeSec == ideSec)
            .Select(i => i.IdeCodigo)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(tipoIdentificacionCodigo))
            return (null, "No encontré el código interno del tipo de identificación.");

        var paisId = await ResolvePaisIdAsync(context, request.Pais!, cancellationToken);
        if (paisId <= 0)
            return (null, $"No encontré el país '{request.Pais}'.");

        var provinciaId = await ResolveProvinciaIdAsync(context, paisId, request.Provincia!, cancellationToken);
        if (provinciaId <= 0)
            return (null, $"No encontré la provincia '{request.Provincia}'.");

        var ciudadId = await ResolveCiudadIdAsync(context, provinciaId, request.Ciudad!, cancellationToken);
        if (ciudadId <= 0)
            return (null, $"No encontré la ciudad '{request.Ciudad}'.");

        var (nombres, apellidos) = ResolveNaturalNames(request);

        var cliente = new Models.Cliente
        {
            Usuario = ownerId,
            TipoCliente = tipoCliente,
            Tipoidentificacion = tipoIdentificacionCodigo,
            Numeroidentificacion = identificacion,
            Correo = request.Correo!.Trim(),
            Celular = request.Celular!.Trim(),
            Telefonoconvencional = request.Telefono!.Trim(),
            Direccion = request.Direccion!.Trim(),
            Oblgconta = (request.ObligadoContabilidad ?? "NO").Trim().ToUpperInvariant(),
            Pais = paisId,
            Provincia = provinciaId,
            Ciudad = ciudadId,
            Estado = true,
            Observaciones = "Creado por Asistente IA",
            Nombres = esEmpresa ? null : nombres,
            Apellidos = esEmpresa ? null : apellidos,
            Nombrerazonsocial = esEmpresa ? request.RazonSocial?.Trim() : null,
            Nombrecomercial = esEmpresa ? request.NombreComercial?.Trim() : null
        };

        context.Clientes.Add(cliente);
        await context.SaveChangesAsync(cancellationToken);

        return (Map(cliente), $"Creé el cliente {ResolveDisplayName(cliente)} correctamente.");
    }

    private static ClienteDto Map(Models.Cliente cliente)
    {
        var nombre = !string.IsNullOrWhiteSpace(cliente.Nombrerazonsocial)
            ? cliente.Nombrerazonsocial!.Trim()
            : $"{cliente.Nombres} {cliente.Apellidos}".Trim();

        return new ClienteDto
        {
            Id = cliente.Codcliente,
            Nombre = string.IsNullOrWhiteSpace(nombre) ? "Cliente sin nombre" : nombre,
            Identificacion = cliente.Numeroidentificacion,
            NumeroNotificacion = ResolveNumeroNotificacion(cliente),
            Correo = cliente.Correo,
            Direccion = cliente.Direccion,
            TipoIdentificacion = cliente.Tipoidentificacion
        };
    }

    private static string? ResolveNumeroNotificacion(Models.Cliente cliente)
    {
        if (!string.IsNullOrWhiteSpace(cliente.Referencia))
            return cliente.Referencia.Trim();

        if (!string.IsNullOrWhiteSpace(cliente.Numcontribuyente))
            return cliente.Numcontribuyente.Trim();

        return null;
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

    private async Task EnsureConsumidorFinalWarmAsync(int ownerId)
    {
        if (ownerId <= 0)
            return;

        var now = DateTimeOffset.UtcNow;
        if (ConsumidorFinalWarmCache.TryGetValue(ownerId, out var warmUntil) && warmUntil > now)
            return;

        await _clienteService.EnsureConsumidorFinalAsync(ownerId);
        ConsumidorFinalWarmCache[ownerId] = now.Add(ConsumidorFinalWarmLifetime);
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

    private static List<string> ValidateCreateRequest(ClienteCreateRequestDto request)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Identificacion)) missing.Add("identificación");
        if (string.IsNullOrWhiteSpace(request.Correo)) missing.Add("correo");
        if (string.IsNullOrWhiteSpace(request.Celular)) missing.Add("celular");
        if (string.IsNullOrWhiteSpace(request.Telefono)) missing.Add("teléfono");
        if (string.IsNullOrWhiteSpace(request.Direccion)) missing.Add("dirección");
        if (string.IsNullOrWhiteSpace(request.ObligadoContabilidad)) missing.Add("obligado a llevar contabilidad");
        if (string.IsNullOrWhiteSpace(request.Pais)) missing.Add("país");
        if (string.IsNullOrWhiteSpace(request.Provincia)) missing.Add("provincia");
        if (string.IsNullOrWhiteSpace(request.Ciudad)) missing.Add("ciudad");
        if (!request.EsEmpresa.HasValue) missing.Add("si es persona jurídica o persona natural");

        if (request.EsEmpresa == true)
        {
            if (string.IsNullOrWhiteSpace(request.RazonSocial)) missing.Add("razón social");
            if (string.IsNullOrWhiteSpace(request.NombreComercial)) missing.Add("nombre comercial");
        }
        else
        {
            var nombreCompleto = request.NombreCompleto?.Trim();
            var nombres = request.Nombres?.Trim();
            var apellidos = request.Apellidos?.Trim();
            if (string.IsNullOrWhiteSpace(nombreCompleto) && (string.IsNullOrWhiteSpace(nombres) || string.IsNullOrWhiteSpace(apellidos)))
                missing.Add("nombres y apellidos");
        }

        return missing;
    }

    private static (string? Nombres, string? Apellidos) ResolveNaturalNames(ClienteCreateRequestDto request)
    {
        if (!string.IsNullOrWhiteSpace(request.Nombres) && !string.IsNullOrWhiteSpace(request.Apellidos))
            return (request.Nombres.Trim(), request.Apellidos.Trim());

        var partes = (request.NombreCompleto ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (partes.Length >= 4)
        {
            var mitad = partes.Length / 2;
            return (string.Join(' ', partes.Take(mitad)), string.Join(' ', partes.Skip(mitad)));
        }

        return (request.NombreCompleto?.Trim(), request.Apellidos?.Trim());
    }

    private static string ResolveDisplayName(Models.Cliente cliente)
        => !string.IsNullOrWhiteSpace(cliente.Nombrerazonsocial)
            ? cliente.Nombrerazonsocial.Trim()
            : $"{cliente.Nombres} {cliente.Apellidos}".Trim();

    private static async Task<int> ResolveTipoClienteAsync(AppDbContext context, bool esEmpresa, CancellationToken cancellationToken)
    {
        var match = await context.Tipoclientes
            .AsNoTracking()
            .Where(t => t.TclDescripcion != null)
            .Select(t => new { t.TclCodigo, t.TclDescripcion })
            .ToListAsync(cancellationToken);

        var exact = match.FirstOrDefault(t =>
            esEmpresa
                ? TipoClienteClasificacion.EsJuridica(t.TclDescripcion)
                : TipoClienteClasificacion.EsNatural(t.TclDescripcion));

        return exact?.TclCodigo ?? match.FirstOrDefault()?.TclCodigo ?? 0;
    }

    private static async Task<int> ResolveTipoIdentificacionAsync(AppDbContext context, string identificacion, CancellationToken cancellationToken)
    {
        var query = context.Identificacion.AsNoTracking();
        if (identificacion.Length == 13)
        {
            return await query
                .Where(i => (i.IdeDescripcion ?? string.Empty).Contains("ruc") || (i.IdeCodigo ?? string.Empty).Contains("RUC"))
                .Select(i => i.IdeSec)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await query
            .Where(i => (i.IdeDescripcion ?? string.Empty).Contains("ced") || (i.IdeDescripcion ?? string.Empty).Contains("céd"))
            .Select(i => i.IdeSec)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<int> ResolvePaisIdAsync(AppDbContext context, string pais, CancellationToken cancellationToken)
        => await context.Paises
            .AsNoTracking()
            .Where(p => p.Descripcion != null && p.Descripcion.ToLower() == pais.Trim().ToLower())
            .Select(p => p.IdPais)
            .FirstOrDefaultAsync(cancellationToken);

    private static async Task<int> ResolveProvinciaIdAsync(AppDbContext context, int paisId, string provincia, CancellationToken cancellationToken)
        => await context.Provincias
            .AsNoTracking()
            .Where(p => p.IdPais == paisId && p.Descripcion != null && p.Descripcion.ToLower() == provincia.Trim().ToLower())
            .Select(p => p.IdProvincia)
            .FirstOrDefaultAsync(cancellationToken);

    private static async Task<int> ResolveCiudadIdAsync(AppDbContext context, int provinciaId, string ciudad, CancellationToken cancellationToken)
        => await context.Ciudades
            .AsNoTracking()
            .Where(c => c.IdProvincia == provinciaId && c.Descripcion != null && c.Descripcion.ToLower() == ciudad.Trim().ToLower())
            .Select(c => c.IdCiudad)
            .FirstOrDefaultAsync(cancellationToken);
}
