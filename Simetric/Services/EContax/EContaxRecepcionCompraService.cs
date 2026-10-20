using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs.EContax;
using Simetric.Models.EContax;

namespace Simetric.Services.EContax;

public sealed class EContaxRecepcionCompraService
{
    private const string Borrador = "BORRADOR";
    private const string RecibidaParcial = "RECIBIDA_PARCIAL";
    private const string RecibidaCompleta = "RECIBIDA_COMPLETA";
    private const string Anulada = "ANULADA";
    private const string Aprobada = "APROBADA";
    private const string Enviada = "ENVIADA";

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly EContaxTenantService _tenantService;
    private readonly EContaxSeguridadService _seguridad;

    public EContaxRecepcionCompraService(
        IDbContextFactory<AppDbContext> dbFactory,
        EContaxTenantService tenantService,
        EContaxSeguridadService seguridad)
    {
        _dbFactory = dbFactory;
        _tenantService = tenantService;
        _seguridad = seguridad;
    }

    public async Task<List<EContaxRecepcionCompraListaDto>> ListarAsync(int userId, string? search = null, string? estado = null)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.ComprasRecepciones, EContaxAccion.Ver);
        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await _tenantService.GetContextAsync(context, userId);
        var query = from recepcion in context.EContaxRecepcionesCompra.AsNoTracking()
                    join orden in context.EContaxOrdenesCompra.AsNoTracking()
                        on recepcion.IdOrdenCompra equals orden.IdOrdenCompra
                    where recepcion.IdEmpresa == actor.IdEmpresa && orden.IdEmpresa == actor.IdEmpresa
                    select new { recepcion, orden };

        if (!actor.EsJefeEmpresa && actor.IdSucursal is > 0)
            query = query.Where(x => x.recepcion.IdSucursal == actor.IdSucursal.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var filtro = search.Trim();
            query = query.Where(x => x.recepcion.NumeroRecepcion.Contains(filtro) ||
                x.orden.NumeroOrden.Contains(filtro) || x.orden.NombreProveedor.Contains(filtro));
        }

        if (!string.IsNullOrWhiteSpace(estado))
            query = query.Where(x => x.recepcion.Estado == estado.Trim().ToUpperInvariant());

        return await query
            .OrderByDescending(x => x.recepcion.IdRecepcion)
            .Select(x => new EContaxRecepcionCompraListaDto
            {
                IdRecepcion = x.recepcion.IdRecepcion,
                NumeroRecepcion = x.recepcion.NumeroRecepcion,
                IdOrdenCompra = x.recepcion.IdOrdenCompra,
                NumeroOrden = x.orden.NumeroOrden,
                NombreProveedor = x.orden.NombreProveedor,
                FechaRecepcion = x.recepcion.FechaRecepcion,
                Estado = x.recepcion.Estado,
                TotalRecibido = x.recepcion.Detalles.Sum(d => d.CantidadRecibida),
                Lineas = x.recepcion.Detalles.Count
            })
            .ToListAsync();
    }

    public async Task<List<EContaxRecepcionOrdenDto>> ListarOrdenesDisponiblesAsync(int userId)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.ComprasRecepciones, EContaxAccion.Ver);
        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await _tenantService.GetContextAsync(context, userId);
        var ordenes = await ObtenerOrdenesDisponiblesAsync(context, actor);
        return ordenes.Select(x => MapOrden(x.Orden, x.Recibido)).ToList();
    }

    public async Task<EContaxRecepcionOrdenDto?> ObtenerOrdenDisponibleAsync(int userId, int idOrdenCompra)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.ComprasRecepciones, EContaxAccion.Ver);
        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await _tenantService.GetContextAsync(context, userId);
        var orden = await ObtenerOrdenAsync(context, actor, idOrdenCompra);
        if (orden is null || orden.Estado is not (Aprobada or Enviada))
            return null;

        var recibido = await ObtenerCantidadesRecibidasAsync(context, actor, idOrdenCompra, null);
        var dto = MapOrden(orden, recibido);
        return dto.Detalles.Any(x => x.CantidadPendiente > 0) ? dto : null;
    }

    public async Task<EContaxRecepcionCompraDto?> ObtenerAsync(int userId, int id)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.ComprasRecepciones, EContaxAccion.Ver);
        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await _tenantService.GetContextAsync(context, userId);
        var recepcion = await ObtenerRecepcionAsync(context, actor, id);
        if (recepcion is null)
            return null;

        var orden = await context.EContaxOrdenesCompra.AsNoTracking()
            .Include(x => x.Detalles)
            .FirstAsync(x => x.IdOrdenCompra == recepcion.IdOrdenCompra);
        var recibido = await ObtenerCantidadesRecibidasAsync(context, actor, recepcion.IdOrdenCompra, id);
        return MapRecepcion(recepcion, orden, recibido);
    }

    public async Task<EContaxRecepcionCompraDto> CrearAsync(int userId, EContaxRecepcionCompraUpsertDto dto)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.ComprasRecepciones, EContaxAccion.Crear);
        await using var context = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var actor = await _tenantService.GetContextAsync(context, userId);
        var orden = await ObtenerOrdenAsync(context, actor, dto.IdOrdenCompra);
        ValidarOrdenRecepcionable(orden);
        var recibidas = await ObtenerCantidadesRecibidasAsync(context, actor, dto.IdOrdenCompra, null);
        var detalles = PrepararDetalles(orden!, dto.Detalles, recibidas);

        var ahora = DateTime.Now;
        var recepcion = new EContaxRecepcionCompra
        {
            IdEmpresa = actor.IdEmpresa,
            IdSucursal = orden!.IdSucursal,
            IdOrdenCompra = orden.IdOrdenCompra,
            NumeroRecepcion = string.Empty,
            FechaRecepcion = dto.FechaRecepcion ?? ahora,
            Estado = Borrador,
            Observacion = Normalizar(dto.Observacion),
            UsuarioCreacion = userId,
            FechaCreacion = ahora,
            Detalles = detalles
        };
        context.EContaxRecepcionesCompra.Add(recepcion);
        await context.SaveChangesAsync();
        recepcion.NumeroRecepcion = $"RC-{recepcion.IdRecepcion:D6}";
        AgregarHistorial(context, recepcion.IdRecepcion, null, Borrador, dto.Observacion, userId, ahora);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return (await ObtenerAsync(userId, recepcion.IdRecepcion))!;
    }

    public async Task<EContaxRecepcionCompraDto> ActualizarAsync(int userId, int id, EContaxRecepcionCompraUpsertDto dto)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.ComprasRecepciones, EContaxAccion.Editar);
        await using var context = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var actor = await _tenantService.GetContextAsync(context, userId);
        var recepcion = await ObtenerRecepcionAsync(context, actor, id);
        if (recepcion is null)
            throw new InvalidOperationException("La recepción no existe o no pertenece a tu contexto.");
        if (recepcion.Estado != Borrador)
            throw new InvalidOperationException("Solo se pueden editar recepciones en borrador.");
        if (recepcion.IdOrdenCompra != dto.IdOrdenCompra)
            throw new InvalidOperationException("No se puede cambiar la orden de compra de una recepción.");

        var orden = await ObtenerOrdenAsync(context, actor, recepcion.IdOrdenCompra);
        ValidarOrdenRecepcionable(orden);
        var recibidas = await ObtenerCantidadesRecibidasAsync(context, actor, recepcion.IdOrdenCompra, id);
        var detalles = PrepararDetalles(orden!, dto.Detalles, recibidas);
        recepcion.FechaRecepcion = dto.FechaRecepcion ?? recepcion.FechaRecepcion;
        recepcion.Observacion = Normalizar(dto.Observacion);
        recepcion.UsuarioActualizacion = userId;
        recepcion.FechaActualizacion = DateTime.Now;
        context.EContaxRecepcionesCompraDetalles.RemoveRange(recepcion.Detalles);
        recepcion.Detalles = detalles;
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return (await ObtenerAsync(userId, id))!;
    }

    public async Task<EContaxRecepcionCompraDto> ConfirmarAsync(int userId, int id)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.ComprasRecepciones, EContaxAccion.Editar);
        await using var context = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var actor = await _tenantService.GetContextAsync(context, userId);
        var recepcion = await ObtenerRecepcionAsync(context, actor, id);
        if (recepcion is null)
            throw new InvalidOperationException("La recepción no existe o no pertenece a tu contexto.");
        if (recepcion.Estado != Borrador)
            throw new InvalidOperationException("Solo se pueden confirmar recepciones en borrador.");

        var orden = await ObtenerOrdenAsync(context, actor, recepcion.IdOrdenCompra);
        ValidarOrdenRecepcionable(orden);
        var recibidas = await ObtenerCantidadesRecibidasAsync(context, actor, recepcion.IdOrdenCompra, id);
        var totalActual = recepcion.Detalles.Sum(x => x.CantidadRecibida);
        if (totalActual <= 0)
            throw new InvalidOperationException("La recepción debe contener al menos una cantidad recibida.");

        var completa = orden!.Detalles.All(detalle => recibidas.GetValueOrDefault(detalle.IdDetalle) +
            recepcion.Detalles.Where(x => x.IdDetalleOrdenCompra == detalle.IdDetalle).Sum(x => x.CantidadRecibida) >= detalle.Cantidad);
        var nuevoEstado = completa ? RecibidaCompleta : RecibidaParcial;
        recepcion.Estado = nuevoEstado;
        recepcion.UsuarioActualizacion = userId;
        recepcion.FechaActualizacion = DateTime.Now;
        AgregarHistorial(context, id, Borrador, nuevoEstado, recepcion.Observacion, userId, DateTime.Now);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return (await ObtenerAsync(userId, id))!;
    }

    public async Task<EContaxRecepcionCompraDto> AnularAsync(int userId, int id, string? observacion)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.ComprasRecepciones, EContaxAccion.Eliminar);
        await using var context = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var actor = await _tenantService.GetContextAsync(context, userId);
        var recepcion = await ObtenerRecepcionAsync(context, actor, id);
        if (recepcion is null)
            throw new InvalidOperationException("La recepción no existe o no pertenece a tu contexto.");
        if (recepcion.Estado == Anulada)
            throw new InvalidOperationException("La recepción ya está anulada.");

        var anterior = recepcion.Estado;
        recepcion.Estado = Anulada;
        recepcion.Observacion = Normalizar(observacion) ?? recepcion.Observacion;
        recepcion.UsuarioActualizacion = userId;
        recepcion.FechaActualizacion = DateTime.Now;
        AgregarHistorial(context, id, anterior, Anulada, observacion, userId, DateTime.Now);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return (await ObtenerAsync(userId, id))!;
    }

    private async Task<EContaxOrdenCompra?> ObtenerOrdenAsync(AppDbContext context, EContaxUserContext actor, int id)
    {
        var query = context.EContaxOrdenesCompra
            .AsNoTracking()
            .Include(x => x.Detalles)
            .Where(x => x.IdOrdenCompra == id && x.IdEmpresa == actor.IdEmpresa);
        if (!actor.EsJefeEmpresa && actor.IdSucursal is > 0)
            query = query.Where(x => x.IdSucursal == actor.IdSucursal.Value);
        return await query.FirstOrDefaultAsync();
    }

    private async Task<EContaxRecepcionCompra?> ObtenerRecepcionAsync(AppDbContext context, EContaxUserContext actor, int id)
    {
        var query = context.EContaxRecepcionesCompra
            .Include(x => x.Detalles)
            .Where(x => x.IdRecepcion == id && x.IdEmpresa == actor.IdEmpresa);
        if (!actor.EsJefeEmpresa && actor.IdSucursal is > 0)
            query = query.Where(x => x.IdSucursal == actor.IdSucursal.Value);
        return await query.FirstOrDefaultAsync();
    }

    private async Task<List<(EContaxOrdenCompra Orden, Dictionary<int, decimal> Recibido)>> ObtenerOrdenesDisponiblesAsync(AppDbContext context, EContaxUserContext actor)
    {
        var query = context.EContaxOrdenesCompra.AsNoTracking()
            .Include(x => x.Detalles)
            .Where(x => x.IdEmpresa == actor.IdEmpresa && (x.Estado == Aprobada || x.Estado == Enviada));
        if (!actor.EsJefeEmpresa && actor.IdSucursal is > 0)
            query = query.Where(x => x.IdSucursal == actor.IdSucursal.Value);
        var ordenes = await query.OrderByDescending(x => x.IdOrdenCompra).ToListAsync();
        var resultado = new List<(EContaxOrdenCompra, Dictionary<int, decimal>)>();
        foreach (var orden in ordenes)
        {
            var recibido = await ObtenerCantidadesRecibidasAsync(context, actor, orden.IdOrdenCompra, null);
            if (orden.Detalles.Any(x => x.Cantidad > recibido.GetValueOrDefault(x.IdDetalle)))
                resultado.Add((orden, recibido));
        }
        return resultado;
    }

    private static List<EContaxRecepcionCompraDetalle> PrepararDetalles(
        EContaxOrdenCompra orden,
        IEnumerable<EContaxRecepcionCompraDetalleUpsertDto>? modelos,
        IReadOnlyDictionary<int, decimal> recibidas)
    {
        var items = (modelos ?? Enumerable.Empty<EContaxRecepcionCompraDetalleUpsertDto>()).ToList();
        if (items.Count == 0 || items.All(x => x.CantidadRecibida <= 0))
            throw new InvalidOperationException("La recepción debe contener al menos una cantidad recibida.");
        if (items.GroupBy(x => x.IdDetalleOrdenCompra).Any(x => x.Key <= 0 || x.Count() > 1))
            throw new InvalidOperationException("No se pueden repetir líneas de la orden en la recepción.");

        var ordenDetalle = orden.Detalles.ToDictionary(x => x.IdDetalle);
        if (items.Any(x => !ordenDetalle.ContainsKey(x.IdDetalleOrdenCompra)))
            throw new InvalidOperationException("Una o más líneas no pertenecen a la orden seleccionada.");

        var resultado = new List<EContaxRecepcionCompraDetalle>(items.Count);
        foreach (var item in items)
        {
            if (item.CantidadRecibida < 0)
                throw new InvalidOperationException("La cantidad recibida no puede ser negativa.");
            var original = ordenDetalle[item.IdDetalleOrdenCompra];
            var pendiente = original.Cantidad - recibidas.GetValueOrDefault(original.IdDetalle);
            if (item.CantidadRecibida > pendiente)
                throw new InvalidOperationException($"La cantidad recibida de '{original.Descripcion}' supera la pendiente ({pendiente:0.####}).");
            resultado.Add(new EContaxRecepcionCompraDetalle
            {
                IdDetalleOrdenCompra = original.IdDetalle,
                CodigoProducto = original.CodigoProducto,
                CodigoPrincipal = original.CodigoPrincipal,
                Descripcion = original.Descripcion,
                CantidadOrdenada = original.Cantidad,
                CantidadRecibida = item.CantidadRecibida,
                CantidadDiferencia = Math.Max(0, original.Cantidad - item.CantidadRecibida),
                Observacion = Normalizar(item.Observacion)
            });
        }
        return resultado;
    }

    private async Task<Dictionary<int, decimal>> ObtenerCantidadesRecibidasAsync(
        AppDbContext context,
        EContaxUserContext actor,
        int idOrdenCompra,
        int? excluirRecepcion)
    {
        var query = from detalle in context.EContaxRecepcionesCompraDetalles.AsNoTracking()
                    join recepcion in context.EContaxRecepcionesCompra.AsNoTracking()
                        on detalle.IdRecepcion equals recepcion.IdRecepcion
                    where recepcion.IdEmpresa == actor.IdEmpresa && recepcion.IdOrdenCompra == idOrdenCompra &&
                        recepcion.Estado != Anulada && (!excluirRecepcion.HasValue || recepcion.IdRecepcion != excluirRecepcion.Value)
                    group detalle by detalle.IdDetalleOrdenCompra into grupo
                    select new { IdDetalle = grupo.Key, Cantidad = grupo.Sum(x => x.CantidadRecibida) };
        var resultado = await query.ToListAsync();
        return resultado.ToDictionary(x => x.IdDetalle, x => x.Cantidad);
    }

    private static EContaxRecepcionOrdenDto MapOrden(EContaxOrdenCompra orden, IReadOnlyDictionary<int, decimal> recibido) => new()
    {
        IdOrdenCompra = orden.IdOrdenCompra,
        NumeroOrden = orden.NumeroOrden,
        NombreProveedor = orden.NombreProveedor,
        Estado = orden.Estado,
        Detalles = orden.Detalles.Select(x => new EContaxRecepcionOrdenDetalleDto
        {
            IdDetalleOrdenCompra = x.IdDetalle,
            CodigoProducto = x.CodigoProducto,
            CodigoPrincipal = x.CodigoPrincipal,
            Descripcion = x.Descripcion,
            CantidadOrdenada = x.Cantidad,
            CantidadYaRecibida = recibido.GetValueOrDefault(x.IdDetalle),
            CantidadPendiente = Math.Max(0, x.Cantidad - recibido.GetValueOrDefault(x.IdDetalle))
        }).ToList()
    };

    private static EContaxRecepcionCompraDto MapRecepcion(
        EContaxRecepcionCompra recepcion,
        EContaxOrdenCompra orden,
        IReadOnlyDictionary<int, decimal> recibido) => new()
        {
            IdRecepcion = recepcion.IdRecepcion,
            NumeroRecepcion = recepcion.NumeroRecepcion,
            IdOrdenCompra = recepcion.IdOrdenCompra,
            NumeroOrden = orden.NumeroOrden,
            NombreProveedor = orden.NombreProveedor,
            FechaRecepcion = recepcion.FechaRecepcion,
            Estado = recepcion.Estado,
            TotalRecibido = recepcion.Detalles.Sum(x => x.CantidadRecibida),
            Lineas = recepcion.Detalles.Count,
            IdSucursal = recepcion.IdSucursal,
            Observacion = recepcion.Observacion,
            Detalles = recepcion.Detalles.Select(x => new EContaxRecepcionCompraDetalleDto
            {
                IdDetalleRecepcion = x.IdDetalleRecepcion,
                IdDetalleOrdenCompra = x.IdDetalleOrdenCompra,
                CodigoProducto = x.CodigoProducto,
                CodigoPrincipal = x.CodigoPrincipal,
                Descripcion = x.Descripcion,
                CantidadOrdenada = x.CantidadOrdenada,
                CantidadYaRecibida = recibido.GetValueOrDefault(x.IdDetalleOrdenCompra),
                CantidadRecibida = x.CantidadRecibida,
                CantidadPendiente = Math.Max(0, x.CantidadOrdenada - recibido.GetValueOrDefault(x.IdDetalleOrdenCompra) - x.CantidadRecibida),
                CantidadDiferencia = x.CantidadDiferencia,
                Observacion = x.Observacion
            }).ToList()
        };

    private static void ValidarOrdenRecepcionable(EContaxOrdenCompra? orden)
    {
        if (orden is null)
            throw new InvalidOperationException("La orden no existe o no pertenece a tu contexto.");
        if (orden.Estado is not (Aprobada or Enviada))
            throw new InvalidOperationException("Solo se pueden recibir órdenes aprobadas o enviadas.");
    }

    private static void AgregarHistorial(AppDbContext context, int idRecepcion, string? anterior, string nuevo, string? observacion, int userId, DateTime fecha) =>
        context.EContaxRecepcionesCompraHistorial.Add(new EContaxRecepcionCompraHistorial
        {
            IdRecepcion = idRecepcion,
            EstadoAnterior = anterior,
            EstadoNuevo = nuevo,
            Observacion = Normalizar(observacion),
            Usuario = userId,
            Fecha = fecha
        });

    private static string? Normalizar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
