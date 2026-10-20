using System.Data;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.DTOs.EContax;
using Simetric.Models;
using Simetric.Models.EContax;
using Simetric.Services;

namespace Simetric.Services.EContax;

public sealed class EContaxCatalogService
{
    private const string ConsumidorFinalIdentificacion = "9999999999999";
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly EContaxTenantService _tenantService;
    private readonly EContaxSeguridadService _seguridad;

    public EContaxCatalogService(
        IDbContextFactory<AppDbContext> dbFactory,
        EContaxTenantService tenantService, EContaxSeguridadService seguridad)
    {
        _dbFactory = dbFactory;
        _tenantService = tenantService;
        _seguridad = seguridad;
    }

    public async Task<List<EContaxClienteDto>> GetClientesAsync(int userId, bool incluirInactivos = false)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenantService.GetContextAsync(context, userId);
        await EnsureConsumidorFinalEmpresaAsync(context, userContext);

        var query = BuildClientesEmpresaQuery(context, userContext);

        if (!incluirInactivos)
            query = query.Where(c => c.Estado == true);

        var data = await query
            .OrderByDescending(c => c.Codcliente)
            .Select(c => new EContaxClienteDto
            {
                Codcliente = c.Codcliente,
                Apellidos = c.Apellidos,
                Nombres = c.Nombres,
                Nombrecomercial = c.Nombrecomercial,
                Nombrerazonsocial = c.Nombrerazonsocial,
                Numeroidentificacion = c.Numeroidentificacion,
                Direccion = c.Direccion,
                Telefonoconvencional = c.Telefonoconvencional,
                Celular = c.Celular,
                Correo = c.Correo,
                DiasCredito = c.DiasCredito,
                CorreosAdicionales = context.ClientesCorreos
                    .Where(cc => cc.CodCliente == c.Codcliente && cc.Estado == true)
                    .Select(cc => cc.Correo)
                    .ToList(),
                Observaciones = c.Observaciones,
                Oblgconta = c.Oblgconta,
                TipoCliente = c.TipoCliente ?? 0,
                Estado = c.Estado,
                Pais = c.Pais,
                Provincia = c.Provincia,
                Ciudad = c.Ciudad,
                Tipoidentificacion = context.Identificacion
                    .Where(i => i.IdeCodigo == c.Tipoidentificacion)
                    .Select(i => (int?)i.IdeSec)
                    .FirstOrDefault(),
                Idempresa = c.Idempresa
            })
            .ToListAsync();

        await CompletarDatosProveedoresAsync(context, data);
        return data;
    }

    public async Task EnsureConsumidorFinalAsync(int userId)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenantService.GetContextAsync(context, userId);
        await EnsureConsumidorFinalEmpresaAsync(context, userContext);
    }

    public async Task<EContaxClienteDto?> GetClienteAsync(int userId, int codCliente)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenantService.GetContextAsync(context, userId);

        var data = await BuildClientesEmpresaQuery(context, userContext)
            .Where(c => c.Codcliente == codCliente)
            .Select(c => new EContaxClienteDto
            {
                Codcliente = c.Codcliente,
                Apellidos = c.Apellidos,
                Nombres = c.Nombres,
                Nombrecomercial = c.Nombrecomercial,
                Nombrerazonsocial = c.Nombrerazonsocial,
                Numeroidentificacion = c.Numeroidentificacion,
                Direccion = c.Direccion,
                Telefonoconvencional = c.Telefonoconvencional,
                Celular = c.Celular,
                Correo = c.Correo,
                DiasCredito = c.DiasCredito,
                CorreosAdicionales = context.ClientesCorreos
                    .Where(cc => cc.CodCliente == c.Codcliente && cc.Estado == true)
                    .Select(cc => cc.Correo)
                    .ToList(),
                Observaciones = c.Observaciones,
                Oblgconta = c.Oblgconta,
                TipoCliente = c.TipoCliente ?? 0,
                Estado = c.Estado,
                Pais = c.Pais,
                Provincia = c.Provincia,
                Ciudad = c.Ciudad,
                Tipoidentificacion = context.Identificacion
                    .Where(i => i.IdeCodigo == c.Tipoidentificacion)
                    .Select(i => (int?)i.IdeSec)
                    .FirstOrDefault(),
                Idempresa = c.Idempresa
            })
            .FirstOrDefaultAsync();

        if (data is not null)
            await CompletarDatosProveedoresAsync(context, new[] { data });

        return data;
    }

    public async Task<int> CrearClienteAsync(int userId, EContaxClienteUpsertDto dto)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.Clientes, EContaxAccion.Crear);
        await using var strategyContext = await _dbFactory.CreateDbContextAsync();
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var userContext = await _tenantService.GetContextAsync(context, userId);

            await ValidarClienteAsync(context, userContext, dto);
            var codigoIdentificacion = await GetCodigoIdentificacionAsync(context, dto.Tipoidentificacion);
            NormalizarCliente(dto);

            var entity = new Cliente
            {
                Apellidos = dto.Apellidos,
                Nombres = dto.Nombres,
                Nombrecomercial = dto.Nombrecomercial,
                Nombrerazonsocial = dto.Nombrerazonsocial,
                Numeroidentificacion = dto.Numeroidentificacion,
                Direccion = dto.Direccion,
                Telefonoconvencional = dto.Telefonoconvencional,
                Celular = dto.Celular,
                Correo = dto.Correo,
                DiasCredito = dto.DiasCredito,
                Observaciones = dto.Observaciones,
                Oblgconta = dto.Oblgconta,
                TipoCliente = dto.TipoCliente,
                Estado = dto.Estado ?? true,
                Pais = dto.Pais,
                Provincia = dto.Provincia,
                Ciudad = dto.Ciudad,
                Tipoidentificacion = codigoIdentificacion,
                Usuario = userContext.IdUsuarioTitular,
                Idempresa = userContext.IdEmpresa,
                Idsucursal = null
            };

            context.Clientes.Add(entity);
            await context.SaveChangesAsync();
            await SincronizarProveedorAsync(context, entity, dto, codigoIdentificacion);
            await GuardarCorreosClienteAsync(context, entity.Codcliente, dto.CorreosAdicionales);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            return entity.Codcliente;
        });
    }

    public async Task ActualizarClienteAsync(int userId, int codCliente, EContaxClienteUpsertDto dto)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.Clientes, EContaxAccion.Editar);
        await using var strategyContext = await _dbFactory.CreateDbContextAsync();
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var userContext = await _tenantService.GetContextAsync(context, userId);

            var cliente = await BuildClientesEmpresaQuery(context, userContext)
                .FirstOrDefaultAsync(c => c.Codcliente == codCliente);

            if (cliente is null)
                throw new InvalidOperationException("El cliente no existe o no pertenece a la empresa.");

            await ValidarClienteAsync(context, userContext, dto, codCliente);
            var codigoIdentificacion = await GetCodigoIdentificacionAsync(context, dto.Tipoidentificacion);
            NormalizarCliente(dto);

            cliente.Apellidos = dto.Apellidos;
            cliente.Nombres = dto.Nombres;
            cliente.Nombrecomercial = dto.Nombrecomercial;
            cliente.Nombrerazonsocial = dto.Nombrerazonsocial;
            cliente.Numeroidentificacion = dto.Numeroidentificacion;
            cliente.Direccion = dto.Direccion;
            cliente.Telefonoconvencional = dto.Telefonoconvencional;
            cliente.Celular = dto.Celular;
            cliente.Correo = dto.Correo;
            cliente.DiasCredito = dto.DiasCredito;
            cliente.Observaciones = dto.Observaciones;
            cliente.Oblgconta = dto.Oblgconta;
            cliente.TipoCliente = dto.TipoCliente;
            cliente.Pais = dto.Pais;
            cliente.Provincia = dto.Provincia;
            cliente.Ciudad = dto.Ciudad;
            cliente.Tipoidentificacion = codigoIdentificacion;
            cliente.Usuario = userContext.IdUsuarioTitular;
            cliente.Idempresa = userContext.IdEmpresa;
            cliente.Idsucursal = null;

            if (dto.Estado is not null)
                cliente.Estado = dto.Estado;

            await SincronizarProveedorAsync(context, cliente, dto, codigoIdentificacion);
            await GuardarCorreosClienteAsync(context, cliente.Codcliente, dto.CorreosAdicionales);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        });
    }

    public async Task SetEstadoClienteAsync(int userId, int codCliente, bool estado)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.Clientes, estado ? EContaxAccion.Editar : EContaxAccion.Eliminar);
        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenantService.GetContextAsync(context, userId);
        var cliente = await BuildClientesEmpresaQuery(context, userContext)
            .FirstOrDefaultAsync(c => c.Codcliente == codCliente);

        if (cliente is null)
            throw new InvalidOperationException("Cliente no encontrado.");

        cliente.Estado = estado;
        await context.SaveChangesAsync();
    }

    public async Task<List<ProductoLookupDetalleDto>> BuscarProductosFiltroAsync(int userId, string filtro)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenantService.GetContextAsync(context, userId);
        var filtroLower = (filtro ?? string.Empty).Trim().ToLowerInvariant();

        var query = BuildProductosPermitidosQuery(context, userContext, null)
            .Include(p => p.TipoProductoNavigation)
            .Include(p => p.IdsubtipoNavigation)
            .Where(p => p.Estado == true);

        if (!string.IsNullOrWhiteSpace(filtroLower))
        {
            query = query.Where(p =>
                (
                    (p.Nombre ?? "").ToLower().Contains(filtroLower) ||
                    (p.CodigoPrincipal ?? "").ToLower().Contains(filtroLower) ||
                    (p.CodAuxiliar ?? "").ToLower().Contains(filtroLower) ||
                    (p.Tipocompravena ?? "").ToLower().Contains(filtroLower) ||
                    (p.TipoProductoNavigation != null && (p.TipoProductoNavigation.Descripcion ?? "").ToLower().Contains(filtroLower)) ||
                    (p.IdsubtipoNavigation != null && (p.IdsubtipoNavigation.Descripcion ?? "").ToLower().Contains(filtroLower))
                ));
        }

        var productos = await query
            .OrderBy(p => p.Nombre)
            .Take(15)
            .ToListAsync();

        return productos.Select(MapearProductoLookup).ToList();
    }

    public async Task<ProductoLookupDetalleDto?> BuscarProductoParaDetalleAsync(int userId, string criterio)
    {
        if (string.IsNullOrWhiteSpace(criterio))
            return null;

        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenantService.GetContextAsync(context, userId);
        var filtro = criterio.Trim();
        var filtroLower = filtro.ToLowerInvariant();

        var producto = await BuildProductosPermitidosQuery(context, userContext, null)
            .Include(p => p.TipoProductoNavigation)
            .Include(p => p.IdsubtipoNavigation)
            .Where(p =>
                p.Estado == true &&
                (
                    p.CodigoPrincipal == filtro ||
                    p.CodAuxiliar == filtro ||
                    (p.Nombre ?? "").ToLower().Contains(filtroLower) ||
                    (p.Tipocompravena ?? "").ToLower().Contains(filtroLower) ||
                    (p.TipoProductoNavigation != null && (p.TipoProductoNavigation.Descripcion ?? "").ToLower().Contains(filtroLower)) ||
                    (p.IdsubtipoNavigation != null && (p.IdsubtipoNavigation.Descripcion ?? "").ToLower().Contains(filtroLower))
                ))
            .FirstOrDefaultAsync();

        return producto is null ? null : MapearProductoLookup(producto);
    }

    public async Task<List<string>> GetCorreosAdicionalesClienteAsync(int userId, int codCliente)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenantService.GetContextAsync(context, userId);
        var pertenece = await BuildClientesEmpresaQuery(context, userContext)
            .AsNoTracking()
            .AnyAsync(c => c.Codcliente == codCliente);

        if (!pertenece)
            return new List<string>();

        return await context.ClientesCorreos
            .AsNoTracking()
            .Where(cc => cc.CodCliente == codCliente && cc.Estado)
            .OrderBy(cc => cc.Id)
            .Select(cc => cc.Correo)
            .ToListAsync();
    }

    public async Task<Cliente?> GetClienteByIdentificacionAsync(int userId, string identificacion)
    {
        if (string.IsNullOrWhiteSpace(identificacion))
            return null;

        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenantService.GetContextAsync(context, userId);
        var limpia = identificacion.Trim();

        return await BuildClientesEmpresaQuery(context, userContext)
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.Numeroidentificacion == limpia &&
                (c.Estado == null || c.Estado == true));
    }

    public async Task<Cliente> UpsertClienteAsync(int userId, Cliente clienteData)
    {
        if (clienteData is null)
            throw new ArgumentNullException(nameof(clienteData));

        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenantService.GetContextAsync(context, userId);
        var identificacion = (clienteData.Numeroidentificacion ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(identificacion))
            throw new InvalidOperationException("La identificacion del cliente es obligatoria.");

        clienteData.Numeroidentificacion = identificacion;
        clienteData.Estado = true;
        clienteData.Usuario = userContext.IdUsuarioTitular;
        clienteData.Idempresa = userContext.IdEmpresa;
        clienteData.Idsucursal = null;
        clienteData.Fechaingreso ??= DateOnly.FromDateTime(DateTime.Now);

        var clienteDb = await BuildClientesEmpresaQuery(context, userContext)
            .FirstOrDefaultAsync(c => c.Numeroidentificacion == identificacion);

        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.Clientes, clienteDb is null ? EContaxAccion.Crear : EContaxAccion.Editar);
        if (clienteDb is null)
        {
            context.Clientes.Add(clienteData);
            await context.SaveChangesAsync();
            return clienteData;
        }

        clienteDb.Apellidos = clienteData.Apellidos;
        clienteDb.Nombres = clienteData.Nombres;
        clienteDb.Nombrecomercial = clienteData.Nombrecomercial;
        clienteDb.Nombrerazonsocial = clienteData.Nombrerazonsocial;
        clienteDb.Tipoidentificacion = clienteData.Tipoidentificacion;
        clienteDb.Direccion = clienteData.Direccion;
        clienteDb.Telefonoconvencional = clienteData.Telefonoconvencional;
        clienteDb.Celular = clienteData.Celular;
        clienteDb.Correo = clienteData.Correo;
        clienteDb.Observaciones = clienteData.Observaciones;
        clienteDb.Oblgconta = clienteData.Oblgconta;
        clienteDb.TipoCliente = clienteData.TipoCliente;
        clienteDb.Pais = clienteData.Pais;
        clienteDb.Provincia = clienteData.Provincia;
        clienteDb.Ciudad = clienteData.Ciudad;
        clienteDb.Usuario = userContext.IdUsuarioTitular;
        clienteDb.Idempresa = userContext.IdEmpresa;
        clienteDb.Idsucursal = null;
        clienteDb.Estado = true;

        await context.SaveChangesAsync();
        return clienteDb;
    }

    public async Task<List<Cliente>> BuscarClientesFiltroAsync(int userId, string filtro)
    {
        if (string.IsNullOrWhiteSpace(filtro))
            return new List<Cliente>();

        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenantService.GetContextAsync(context, userId);
        var filtroLower = filtro.Trim().ToLowerInvariant();

        return await BuildClientesEmpresaQuery(context, userContext)
            .AsNoTracking()
            .Where(c =>
                (c.Estado == null || c.Estado == true) &&
                (
                    (c.Numeroidentificacion ?? "").ToLower().Contains(filtroLower) ||
                    (c.Numcontribuyente ?? "").ToLower().Contains(filtroLower) ||
                    (c.Referencia ?? "").ToLower().Contains(filtroLower) ||
                    ((c.Nombres ?? "") + " " + (c.Apellidos ?? "")).ToLower().Contains(filtroLower) ||
                    (c.Nombrerazonsocial ?? "").ToLower().Contains(filtroLower) ||
                    (c.Nombrecomercial ?? "").ToLower().Contains(filtroLower)
                ))
            .OrderBy(c => c.Nombrerazonsocial ?? c.Nombrecomercial ?? c.Nombres ?? c.Apellidos)
            .Take(15)
            .ToListAsync();
    }

    public async Task<(EContaxUserScopeDto Contexto, List<EContaxSucursalDto> Sucursales)> GetProductoLookupsContextAsync(int userId)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenantService.GetContextAsync(context, userId);
        var sucursales = await _tenantService.GetSucursalesEmpresaAsync(context, userContext.IdEmpresa);
        if (!userContext.EsJefeEmpresa && userContext.IdSucursal is > 0)
            sucursales = sucursales.Where(x => x.IdSucursal == userContext.IdSucursal).ToList();
        return (EContaxTenantService.ToScopeDto(userContext), sucursales);
    }

    public async Task<List<EContaxProductoDto>> GetProductosAsync(int userId, int? sucursalId = null)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenantService.GetContextAsync(context, userId);

        var data = await BuildProductosPermitidosQuery(context, userContext, sucursalId)
            .OrderByDescending(p => p.Codigo)
            .Select(p => new EContaxProductoDto
            {
                Codigo = p.Codigo,
                Nombre = p.Nombre,
                CodigoPrincipal = p.CodigoPrincipal,
                ValorUnitario = p.ValorUnitario,
                Precio2 = p.Precio2,
                Precio3 = p.Precio3,
                TipoCompravena = p.Tipocompravena,
                TipoProducto = p.TipoProducto,
                Idsubtipo = p.Idsubtipo,
                Codigoimpuesto = p.Codigoimpuesto,
                Porcentajeimpuesto = p.Porcentajeimpuesto,
                Estado = p.Estado,
                Observacion = p.Observacion,
                Idusuario = p.Idusuario,
                Idempresa = p.Idempresa,
                Idsucursal = p.Idsucursal,
                NombreSucursal = context.EContaxSucursales
                    .Where(s => s.IdEmpresa == p.Idempresa && s.IdSucursal == p.Idsucursal)
                    .Select(s => s.Nombre)
                    .FirstOrDefault()
            })
            .ToListAsync();

        await CompletarSucursalesAsync(context, userContext, data);

        return data;
    }

    public async Task<EContaxProductoDto?> GetProductoAsync(int userId, int codigo)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenantService.GetContextAsync(context, userId);

        var item = await BuildProductosPermitidosQuery(context, userContext, null)
            .Where(p => p.Codigo == codigo)
            .Select(p => new EContaxProductoDto
            {
                Codigo = p.Codigo,
                Nombre = p.Nombre,
                CodigoPrincipal = p.CodigoPrincipal,
                ValorUnitario = p.ValorUnitario,
                Precio2 = p.Precio2,
                Precio3 = p.Precio3,
                TipoCompravena = p.Tipocompravena,
                TipoProducto = p.TipoProducto,
                Idsubtipo = p.Idsubtipo,
                Codigoimpuesto = p.Codigoimpuesto,
                Porcentajeimpuesto = p.Porcentajeimpuesto,
                Estado = p.Estado,
                Observacion = p.Observacion,
                Idusuario = p.Idusuario,
                Idempresa = p.Idempresa,
                Idsucursal = p.Idsucursal,
                NombreSucursal = context.EContaxSucursales
                    .Where(s => s.IdEmpresa == p.Idempresa && s.IdSucursal == p.Idsucursal)
                    .Select(s => s.Nombre)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        if (item is not null)
            await CompletarSucursalesAsync(context, userContext, new[] { item });

        return item;
    }

    public async Task<int> CrearProductoAsync(int userId, EContaxProductoUpsertDto model)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.Productos, EContaxAccion.Crear);
        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenantService.GetContextAsync(context, userId);
        var targetSucursalIds = await ResolverSucursalesProductoAsync(context, userContext, model.Idsucursales, model.Idsucursal);

        await ValidarProductoAsync(context, userContext, targetSucursalIds, model);
        NormalizarProducto(model);

        var entity = new Producto
        {
            Nombre = model.Nombre,
            CodigoPrincipal = model.CodigoPrincipal,
            ValorUnitario = model.ValorUnitario,
            Precio2 = model.Precio2,
            Precio3 = model.Precio3,
            Tipocompravena = model.TipoCompravena,
            TipoProducto = model.TipoProducto,
            Idsubtipo = model.Idsubtipo,
            Codigoimpuesto = model.Codigoimpuesto,
            Porcentajeimpuesto = model.Porcentajeimpuesto,
            Estado = model.Estado ?? true,
            Observacion = model.Observacion,
            Idusuario = userContext.IdUsuarioTitular,
            Idempresa = userContext.IdEmpresa,
            Idsucursal = targetSucursalIds[0]
        };

        context.Productos.Add(entity);
        await context.SaveChangesAsync();
        await ReemplazarSucursalesAsync(context, entity.Codigo, userContext.IdEmpresa, targetSucursalIds);
        await context.SaveChangesAsync();
        return entity.Codigo;
    }

    public async Task ActualizarProductoAsync(int userId, int codigo, EContaxProductoUpsertDto model)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.Productos, EContaxAccion.Editar);
        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenantService.GetContextAsync(context, userId);
        var producto = await BuildProductosPermitidosQuery(context, userContext, null)
            .FirstOrDefaultAsync(p => p.Codigo == codigo);

        if (producto is null)
            throw new InvalidOperationException("El producto no existe o no tiene permisos.");

        var targetSucursalIds = userContext.EsJefeEmpresa
            ? await ResolverSucursalesProductoAsync(context, userContext, model.Idsucursales, producto.Idsucursal)
            : await ObtenerSucursalesExistentesAsync(context, userContext, producto.Codigo, producto.Idsucursal);

        await ValidarProductoAsync(context, userContext, targetSucursalIds, model, codigo);
        NormalizarProducto(model);

        producto.Nombre = model.Nombre;
        producto.CodigoPrincipal = model.CodigoPrincipal;
        producto.ValorUnitario = model.ValorUnitario;
        producto.Precio2 = model.Precio2;
        producto.Precio3 = model.Precio3;
        producto.Tipocompravena = model.TipoCompravena;
        producto.TipoProducto = model.TipoProducto;
        producto.Idsubtipo = model.Idsubtipo;
        producto.Codigoimpuesto = model.Codigoimpuesto;
        producto.Porcentajeimpuesto = model.Porcentajeimpuesto;
        producto.Observacion = model.Observacion;
        producto.Idusuario = userContext.IdUsuarioTitular;
        producto.Idempresa = userContext.IdEmpresa;
        producto.Idsucursal = targetSucursalIds[0];

        if (model.Estado is not null)
        {
            if (model.Estado == false && producto.Estado != false)
                await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.Productos, EContaxAccion.Eliminar);
            producto.Estado = model.Estado;
        }

        await ReemplazarSucursalesAsync(context, producto.Codigo, userContext.IdEmpresa, targetSucursalIds);
        await context.SaveChangesAsync();
    }

    public async Task DesactivarProductoAsync(int userId, int codigo)
    {
        await _seguridad.ExigirAccionAsync(userId, EContaxRoutes.Productos, EContaxAccion.Eliminar);
        await using var context = await _dbFactory.CreateDbContextAsync();
        var userContext = await _tenantService.GetContextAsync(context, userId);
        var producto = await BuildProductosPermitidosQuery(context, userContext, null)
            .FirstOrDefaultAsync(p => p.Codigo == codigo);

        if (producto is null)
            throw new InvalidOperationException("Producto no encontrado.");

        producto.Estado = false;
        await context.SaveChangesAsync();
    }

    private IQueryable<Cliente> BuildClientesEmpresaQuery(AppDbContext context, EContaxUserContext userContext) =>
        context.Clientes
            .ExcluirClientesExclusivosBackOffice()
            .Where(c => c.Idempresa == userContext.IdEmpresa);

    private static async Task CompletarDatosProveedoresAsync(
        AppDbContext context,
        IEnumerable<EContaxClienteDto> clientes)
    {
        var lista = clientes.ToList();
        var identificaciones = lista
            .Select(x => x.Numeroidentificacion?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        if (identificaciones.Count == 0)
            return;

        var proveedores = await context.Proveedores
            .AsNoTracking()
            .Where(x => x.ruc != null && identificaciones.Contains(x.ruc) && x.estado != false)
            .ToListAsync();

        foreach (var cliente in lista)
        {
            var proveedor = proveedores.FirstOrDefault(x =>
                string.Equals(x.ruc?.Trim(), cliente.Numeroidentificacion?.Trim(), StringComparison.Ordinal));

            if (proveedor is null)
                continue;

            cliente.EsProveedor = true;
            cliente.CuentaContableProveedor = proveedor.cuentaContable;
            cliente.CreditoTributarioProveedor = proveedor.retIva1;
            cliente.CodigoProveedor = proveedor.detalle;
            cliente.EsSujetoRetencionProveedor = proveedor.llevaRetencion == true;
            cliente.TipoCuentaProveedor = proveedor.tipoCuenta;
            cliente.NumeroCuentaProveedor = proveedor.numeroCuenta;
            cliente.BancoProveedor = proveedor.institucionFin?.ToString();
            cliente.RegistraInformacionBancariaProveedor =
                !string.IsNullOrWhiteSpace(cliente.TipoCuentaProveedor) ||
                !string.IsNullOrWhiteSpace(cliente.NumeroCuentaProveedor) ||
                !string.IsNullOrWhiteSpace(cliente.BancoProveedor);
        }
    }

    private static async Task SincronizarProveedorAsync(
        AppDbContext context,
        Cliente cliente,
        EContaxClienteUpsertDto dto,
        string? codigoIdentificacion)
    {
        if (!dto.EsProveedor.HasValue)
            return;

        var identificacion = dto.Numeroidentificacion?.Trim();
        if (string.IsNullOrWhiteSpace(identificacion))
            return;

        var proveedor = await context.Proveedores
            .FirstOrDefaultAsync(x => x.ruc == identificacion);

        if (dto.EsProveedor != true)
        {
            if (proveedor is not null && proveedor.estado != false)
            {
                proveedor.estado = false;
                proveedor.fechaActualizacion = DateTime.Now;
            }

            return;
        }

        proveedor ??= new Proveedor { ruc = identificacion };
        if (proveedor.idProveedor == 0)
            context.Proveedores.Add(proveedor);

        var nombres = SepararNombre(dto.Nombres);
        var apellidos = SepararNombre(dto.Apellidos);

        proveedor.estado = true;
        proveedor.tipoIdentificacion = codigoIdentificacion;
        proveedor.personaNatural = string.IsNullOrWhiteSpace(cliente.Nombrerazonsocial) ? 'S' : 'N';
        proveedor.primerNombre = proveedor.personaNatural == 'S' ? nombres.ElementAtOrDefault(0) : null;
        proveedor.segundoNombre = proveedor.personaNatural == 'S' ? nombres.ElementAtOrDefault(1) : null;
        proveedor.primerApellido = proveedor.personaNatural == 'S' ? apellidos.ElementAtOrDefault(0) : null;
        proveedor.segundoApellido = proveedor.personaNatural == 'S' ? apellidos.ElementAtOrDefault(1) : null;
        proveedor.nombreComercial = cliente.Nombrecomercial;
        proveedor.nombre = ObtenerNombreProveedor(cliente);
        proveedor.direccion = cliente.Direccion;
        proveedor.telefono = cliente.Telefonoconvencional;
        proveedor.telefonoMovil = cliente.Celular;
        proveedor.email = cliente.Correo;
        proveedor.obligado = cliente.Oblgconta == "SI" ? 'S' : 'N';
        proveedor.cuentaContable = string.IsNullOrWhiteSpace(dto.CuentaContableProveedor)
            ? "21311 - Proveedores"
            : dto.CuentaContableProveedor.Trim();
        proveedor.retIva1 = dto.CreditoTributarioProveedor?.Trim();
        proveedor.llevaRetencion = dto.EsSujetoRetencionProveedor;
        proveedor.detalle = dto.CodigoProveedor?.Trim();
        proveedor.tipoCuenta = dto.RegistraInformacionBancariaProveedor ? dto.TipoCuentaProveedor?.Trim() : null;
        proveedor.numeroCuenta = dto.RegistraInformacionBancariaProveedor ? dto.NumeroCuentaProveedor?.Trim() : null;
        proveedor.institucionFin = dto.RegistraInformacionBancariaProveedor &&
            int.TryParse(dto.BancoProveedor?.Trim(), out var bancoCodigo) ? bancoCodigo : null;
        proveedor.fechaActualizacion = DateTime.Now;
    }

    private static string ObtenerNombreProveedor(Cliente cliente) =>
        !string.IsNullOrWhiteSpace(cliente.Nombrerazonsocial)
            ? cliente.Nombrerazonsocial.Trim()
            : string.Join(" ", new[] { cliente.Nombres, cliente.Apellidos }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim()));

    private static List<string> SepararNombre(string? valor) =>
        (valor ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private IQueryable<Producto> BuildProductosPermitidosQuery(AppDbContext context, EContaxUserContext userContext, int? sucursalId)
    {
        var query = context.Productos
            .Where(p => p.Idempresa == userContext.IdEmpresa);

        if (userContext.EsJefeEmpresa)
        {
            if (sucursalId is > 0)
                query = query.Where(p => p.Idsucursal == sucursalId.Value ||
                    context.EContaxProductosSucursales.Any(a => a.CodigoProducto == p.Codigo &&
                        a.IdEmpresa == userContext.IdEmpresa && a.IdSucursal == sucursalId.Value));

            return query;
        }

        if (userContext.IdSucursal is > 0)
        {
            query = query.Where(p => p.Idsucursal == userContext.IdSucursal ||
                context.EContaxProductosSucursales.Any(a => a.CodigoProducto == p.Codigo &&
                    a.IdEmpresa == userContext.IdEmpresa && a.IdSucursal == userContext.IdSucursal.Value));
        }
        else
        {
            query = query.Where(_ => false);
        }

        return query;
    }

    private async Task<List<int>> ResolverSucursalesProductoAsync(
        AppDbContext context,
        EContaxUserContext userContext,
        IEnumerable<int>? sucursalesSolicitadas,
        int? sucursalCompatibilidad = null)
    {
        if (!userContext.EsJefeEmpresa)
            return new[] { await _tenantService.ResolveTargetSucursalAsync(userContext, null) }.ToList();

        var ids = (sucursalesSolicitadas ?? Enumerable.Empty<int>())
            .Concat(sucursalCompatibilidad is > 0 ? new[] { sucursalCompatibilidad.Value } : Enumerable.Empty<int>())
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
            ids.Add(await _tenantService.ResolveTargetSucursalAsync(userContext, null));

        var validos = await context.EContaxSucursales
            .AsNoTracking()
            .Where(x => x.IdEmpresa == userContext.IdEmpresa && x.Estado && ids.Contains(x.IdSucursal))
            .Select(x => x.IdSucursal)
            .ToListAsync();

        if (validos.Count != ids.Count)
            throw new InvalidOperationException("Una o más sucursales seleccionadas no pertenecen a la empresa.");

        return ids;
    }

    private async Task ReemplazarSucursalesAsync(AppDbContext context, int codigoProducto, int idEmpresa, IEnumerable<int> sucursales)
    {
        var actuales = await context.EContaxProductosSucursales
            .Where(x => x.CodigoProducto == codigoProducto && x.IdEmpresa == idEmpresa)
            .ToListAsync();
        context.EContaxProductosSucursales.RemoveRange(actuales);
        context.EContaxProductosSucursales.AddRange(sucursales.Distinct().Select(idSucursal => new EContaxProductoSucursal
        {
            CodigoProducto = codigoProducto,
            IdEmpresa = idEmpresa,
            IdSucursal = idSucursal
        }));
    }

    private static async Task CompletarSucursalesAsync(
        AppDbContext context,
        EContaxUserContext userContext,
        IEnumerable<EContaxProductoDto> productos)
    {
        var lista = productos.ToList();
        if (lista.Count == 0)
            return;

        var codigos = lista.Select(x => x.Codigo).ToList();
        var asignaciones = await context.EContaxProductosSucursales
            .AsNoTracking()
            .Where(x => x.IdEmpresa == userContext.IdEmpresa && codigos.Contains(x.CodigoProducto) &&
                (userContext.EsJefeEmpresa || x.IdSucursal == userContext.IdSucursal))
            .Join(context.EContaxSucursales,
                productoSucursal => new { productoSucursal.IdEmpresa, productoSucursal.IdSucursal },
                sucursal => new { IdEmpresa = sucursal.IdEmpresa, IdSucursal = sucursal.IdSucursal },
                (productoSucursal, sucursal) => new { productoSucursal.CodigoProducto, productoSucursal.IdSucursal, sucursal.Nombre })
            .ToListAsync();

        foreach (var producto in lista)
        {
            var sucursales = asignaciones.Where(x => x.CodigoProducto == producto.Codigo).ToList();
            producto.Idsucursales = sucursales.Count > 0
                ? sucursales.Select(x => x.IdSucursal).Distinct().OrderBy(x => x).ToList()
                : producto.Idsucursal is > 0 && (userContext.EsJefeEmpresa || producto.Idsucursal == userContext.IdSucursal)
                    ? new List<int> { producto.Idsucursal.Value }
                    : new List<int>();
            producto.NombreSucursal = sucursales.Count > 0
                ? string.Join(", ", sucursales.Select(x => x.Nombre).Distinct(StringComparer.OrdinalIgnoreCase))
                : producto.NombreSucursal;
        }
    }

    private async Task<List<int>> ObtenerSucursalesExistentesAsync(
        AppDbContext context,
        EContaxUserContext userContext,
        int codigoProducto,
        int? sucursalCompatibilidad)
    {
        var ids = await context.EContaxProductosSucursales
            .Where(x => x.CodigoProducto == codigoProducto && x.IdEmpresa == userContext.IdEmpresa)
            .Select(x => x.IdSucursal)
            .ToListAsync();

        if (ids.Count == 0 && sucursalCompatibilidad is > 0)
            ids.Add(sucursalCompatibilidad.Value);

        if (userContext.IdSucursal is not > 0 || !ids.Contains(userContext.IdSucursal.Value))
            throw new InvalidOperationException("El producto no está asignado a tu sucursal.");

        return ids;
    }

    private async Task EnsureConsumidorFinalEmpresaAsync(AppDbContext context, EContaxUserContext userContext)
    {
        var tipoIdentificacion = await context.Identificacion
            .AsNoTracking()
            .OrderBy(x => x.IdeSec)
            .FirstOrDefaultAsync(x => x.IdeCodigo == "07");

        var tiposCliente = await context.Tipoclientes
            .AsNoTracking()
            .OrderBy(x => x.TclCodigo)
            .ToListAsync();

        var tipoCliente = tiposCliente.FirstOrDefault(x => TipoClienteClasificacion.EsNatural(x.TclDescripcion))
            ?? tiposCliente.FirstOrDefault(x => !TipoClienteClasificacion.EsJuridica(x.TclDescripcion));

        var clientesFinales = await BuildClientesEmpresaQuery(context, userContext)
            .Where(c => c.Numeroidentificacion == ConsumidorFinalIdentificacion)
            .OrderByDescending(c => c.Estado == true)
            .ThenByDescending(c => c.Idempresa == userContext.IdEmpresa)
            .ThenBy(c => c.Codcliente)
            .ToListAsync();

        if (clientesFinales.Count > 0)
        {
            var principal = clientesFinales[0];
            NormalizarConsumidorFinal(principal, userContext, tipoIdentificacion?.IdeCodigo, tipoCliente?.TclCodigo);

            foreach (var duplicado in clientesFinales.Skip(1))
            {
                duplicado.Estado = false;
                duplicado.Idempresa = userContext.IdEmpresa;
                duplicado.Idsucursal = null;
            }

            await context.SaveChangesAsync();
            return;
        }

        context.Clientes.Add(new Cliente
        {
            Apellidos = "Final",
            Nombres = "Consumidor",
            Numeroidentificacion = ConsumidorFinalIdentificacion,
            Tipoidentificacion = tipoIdentificacion?.IdeCodigo,
            TipoCliente = tipoCliente?.TclCodigo,
            Direccion = "Consumidor Final",
            Telefonoconvencional = "022222222",
            Celular = "0999999999",
            Correo = "consumidorfinal@numerica",
            Oblgconta = "NO",
            Estado = true,
            Usuario = userContext.IdUsuarioTitular,
            Idempresa = userContext.IdEmpresa,
            Idsucursal = null,
            Fechaingreso = DateOnly.FromDateTime(DateTime.Now)
        });

        await context.SaveChangesAsync();
    }

    private static void NormalizarConsumidorFinal(Cliente cliente, EContaxUserContext userContext, string? tipoIdentificacion, int? tipoCliente)
    {
        cliente.Apellidos = "Final";
        cliente.Nombres = "Consumidor";
        cliente.Nombrecomercial = null;
        cliente.Nombrerazonsocial = null;
        cliente.Numeroidentificacion = ConsumidorFinalIdentificacion;
        cliente.Tipoidentificacion = tipoIdentificacion ?? cliente.Tipoidentificacion;
        cliente.TipoCliente = tipoCliente ?? cliente.TipoCliente;
        cliente.Direccion = string.IsNullOrWhiteSpace(cliente.Direccion) ? "Consumidor Final" : cliente.Direccion;
        cliente.Telefonoconvencional = string.IsNullOrWhiteSpace(cliente.Telefonoconvencional) ? "022222222" : cliente.Telefonoconvencional;
        cliente.Celular = string.IsNullOrWhiteSpace(cliente.Celular) ? "0999999999" : cliente.Celular;
        cliente.Correo = "consumidorfinal@numerica";
        cliente.Oblgconta = "NO";
        cliente.Estado = true;
        cliente.Usuario = userContext.IdUsuarioTitular;
        cliente.Idempresa = userContext.IdEmpresa;
        cliente.Idsucursal = null;
    }

    private async Task ValidarClienteAsync(AppDbContext context, EContaxUserContext userContext, EContaxClienteUpsertDto dto, int? codClienteActual = null)
    {
        NormalizarCliente(dto);

        if (dto.TipoCliente <= 0)
            throw new InvalidOperationException("TIPO_CLIENTE es obligatorio.");

        if (!dto.Tipoidentificacion.HasValue || dto.Tipoidentificacion <= 0)
            throw new InvalidOperationException("TIPO_IDENTIFICACION es obligatorio.");

        if (string.IsNullOrWhiteSpace(dto.Numeroidentificacion))
            throw new InvalidOperationException("NUMERO_IDENTIFICACION es obligatorio.");

        if (!dto.Numeroidentificacion.All(char.IsDigit) || dto.Numeroidentificacion.Length is < 10 or > 13)
            throw new InvalidOperationException("La identificacion debe tener entre 10 y 13 digitos numericos.");

        var duplicado = await BuildClientesEmpresaQuery(context, userContext)
            .AnyAsync(c =>
                c.Codcliente != (codClienteActual ?? 0) &&
                c.Numeroidentificacion == dto.Numeroidentificacion &&
                c.Estado == true);

        if (duplicado)
            throw new InvalidOperationException("Ya existe un cliente con esa identificacion dentro de la empresa.");

        if (string.IsNullOrWhiteSpace(dto.Correo))
            throw new InvalidOperationException("El correo electronico es obligatorio.");

        try
        {
            _ = new System.Net.Mail.MailAddress(dto.Correo);
        }
        catch
        {
            throw new InvalidOperationException("Correo electronico invalido.");
        }

        if (string.IsNullOrWhiteSpace(dto.Direccion))
            throw new InvalidOperationException("La direccion es obligatoria.");

        if (!dto.Pais.HasValue || dto.Pais <= 0)
            throw new InvalidOperationException("El pais es obligatorio.");

        if (dto.Oblgconta is not ("SI" or "NO"))
            throw new InvalidOperationException("Debe indicar si esta obligado a llevar contabilidad.");

        var descripcionTipo = await context.Tipoclientes
            .AsNoTracking()
            .Where(t => t.TclCodigo == dto.TipoCliente)
            .Select(t => t.TclDescripcion)
            .FirstOrDefaultAsync();

        if (TipoClienteClasificacion.EsJuridica(descripcionTipo))
        {
            if (string.IsNullOrWhiteSpace(dto.Nombrerazonsocial))
                throw new InvalidOperationException("La razon social es obligatoria para persona juridica.");

            if (string.IsNullOrWhiteSpace(dto.Nombrecomercial))
                throw new InvalidOperationException("El nombre comercial es obligatorio para persona juridica.");

            dto.Apellidos = null;
            dto.Nombres = null;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(dto.Apellidos))
                throw new InvalidOperationException("Debe ingresar un apellido.");

            if (string.IsNullOrWhiteSpace(dto.Nombres))
                throw new InvalidOperationException("Debe ingresar un nombre.");

            dto.Nombrecomercial = null;
            dto.Nombrerazonsocial = null;
        }
    }

    private async Task<string?> GetCodigoIdentificacionAsync(AppDbContext context, int? ideSec)
    {
        if (ideSec is not > 0)
            return null;

        var codigo = await context.Identificacion
            .Where(i => i.IdeSec == ideSec.Value)
            .Select(i => i.IdeCodigo)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(codigo))
            throw new InvalidOperationException("El tipo de identificacion seleccionado no es valido.");

        return codigo;
    }

    private static async Task GuardarCorreosClienteAsync(AppDbContext context, int codCliente, IEnumerable<string>? correos)
    {
        var actuales = await context.ClientesCorreos
            .Where(cc => cc.CodCliente == codCliente)
            .ToListAsync();

        context.ClientesCorreos.RemoveRange(actuales);

        foreach (var correo in (correos ?? Enumerable.Empty<string>())
            .Select(x => x?.Trim() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            context.ClientesCorreos.Add(new ClienteCorreo
            {
                CodCliente = codCliente,
                Correo = correo,
                Estado = true
            });
        }
    }

    private async Task ValidarProductoAsync(AppDbContext context, EContaxUserContext userContext, IReadOnlyCollection<int> targetSucursalIds, EContaxProductoUpsertDto? model, int? codigoActual = null)
    {
        if (model is null)
            throw new InvalidOperationException("Datos invalidos.");

        NormalizarProducto(model);

        if (string.IsNullOrWhiteSpace(model.TipoCompravena))
            throw new InvalidOperationException("Debe seleccionar si el registro es PRODUCTO o SERVICIO.");

        if (string.IsNullOrWhiteSpace(model.Nombre))
            throw new InvalidOperationException("Debe ingresar el nombre del producto.");

        if (!string.IsNullOrWhiteSpace(model.CodigoPrincipal))
        {
            var duplicado = await context.Productos.AnyAsync(p =>
                p.Codigo != (codigoActual ?? 0) &&
                p.Idempresa == userContext.IdEmpresa &&
                p.CodigoPrincipal == model.CodigoPrincipal &&
                p.Estado == true &&
                (p.Idsucursal.HasValue && targetSucursalIds.Contains(p.Idsucursal.Value) ||
                 context.EContaxProductosSucursales.Any(a => a.CodigoProducto == p.Codigo &&
                     a.IdEmpresa == userContext.IdEmpresa && targetSucursalIds.Contains(a.IdSucursal))));

            if (duplicado)
                throw new InvalidOperationException("Ya existe un producto con ese codigo en una de las sucursales seleccionadas.");
        }

        if (model.TipoProducto.HasValue)
        {
            var categoriaOk = await context.Productotipos.AnyAsync(t =>
                t.Idtipoproducto == model.TipoProducto &&
                t.Idempresa == userContext.IdEmpresa &&
                t.Estado == true);

            if (!categoriaOk)
                throw new InvalidOperationException("La categoria seleccionada no es valida para la empresa.");
        }

        if (model.Idsubtipo.HasValue)
        {
            if (!model.TipoProducto.HasValue)
                throw new InvalidOperationException("Debe seleccionar una categoria antes de elegir la subcategoria.");

            var subtipoOk = await context.Productosubtipos.AnyAsync(s =>
                s.Idsubtipo == model.Idsubtipo &&
                s.IdtipoproductoNavigation.Idempresa == userContext.IdEmpresa &&
                s.Idtipoproducto == model.TipoProducto &&
                s.Estado == "A");

            if (!subtipoOk)
                throw new InvalidOperationException("La subcategoria seleccionada no es valida para la empresa.");
        }
    }

    private static ProductoLookupDetalleDto MapearProductoLookup(Producto p) => new()
    {
        Codproducto = p.Codigo,
        Codprincipal = p.CodigoPrincipal,
        Codauxiliar = p.CodAuxiliar,
        Descripcion = p.Nombre,
        PrecioUnitario = p.ValorUnitario ?? 0m,
        Categoria = p.TipoProductoNavigation?.Descripcion,
        Subcategoria = p.IdsubtipoNavigation?.Descripcion,
        TipoProducto = p.TipoProducto ?? 0,
        SubtipoProducto = p.Idsubtipo ?? 0,
        CodigoImpuestoSri = p.Codigoimpuesto,
        TarifaIva = ObtenerTarifaIvaProducto(p)
    };

    private static int ObtenerTarifaIvaProducto(Producto p)
    {
        var valor = TaxRateHelper.ParsePercentOrZero(p.Porcentajeimpuesto);
        var codigo = (int)Math.Round(valor, 0, MidpointRounding.AwayFromZero);

        return codigo switch
        {
            0 => 0,
            2 => 12,
            3 => 14,
            4 => 15,
            5 => 5,
            6 => 0,
            7 => 0,
            8 => 8,
            10 => 13,
            13 => 10,
            14 => 3,
            15 => 15,
            _ => Math.Max(0, TaxRateHelper.NormalizePercentInt(valor))
        };
    }

    private static void NormalizarCliente(EContaxClienteUpsertDto dto)
    {
        dto.Apellidos = dto.Apellidos?.Trim();
        dto.Nombres = dto.Nombres?.Trim();
        dto.Nombrecomercial = dto.Nombrecomercial?.Trim();
        dto.Nombrerazonsocial = dto.Nombrerazonsocial?.Trim();
        dto.Numeroidentificacion = dto.Numeroidentificacion?.Trim();
        dto.Direccion = dto.Direccion?.Trim();
        dto.Telefonoconvencional = dto.Telefonoconvencional?.Trim();
        dto.Celular = dto.Celular?.Trim();
        dto.Correo = dto.Correo?.Trim();
        dto.CorreosAdicionales = (dto.CorreosAdicionales ?? new List<string>())
            .Select(correo => correo?.Trim() ?? string.Empty)
            .Where(correo => !string.IsNullOrWhiteSpace(correo))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        dto.Observaciones = dto.Observaciones?.Trim();
        dto.Oblgconta = dto.Oblgconta?.Trim().ToUpperInvariant();
    }

    private static void NormalizarProducto(EContaxProductoUpsertDto model)
    {
        model.Nombre = model.Nombre?.Trim();
        model.CodigoPrincipal = model.CodigoPrincipal?.Trim();
        model.TipoCompravena = model.TipoCompravena?.Trim().ToUpperInvariant();
        model.Codigoimpuesto = model.Codigoimpuesto?.Trim();
        model.Porcentajeimpuesto = model.Porcentajeimpuesto?.Trim();
        model.Observacion = model.Observacion?.Trim();
    }

}
