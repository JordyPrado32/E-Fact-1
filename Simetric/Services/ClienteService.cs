using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Services;

public class ClienteService
{
    private const string ConsumidorNombre = "Consumidor";
    private const string ConsumidorApellido = "Final";
    private const string ConsumidorFinalIdentificacion = "9999999999999";
    private const string ConsumidorFinalCodigoIdentificacion = "07";
    private const string ConsumidorFinalCorreo = "consumidorfinal@numerica";
    private const string PaisDefault = "ECUADOR";
    private const string ProvinciaDefault = "PICHINCHA";
    private const string CiudadDefault = "QUITO";
    private const string TelefonoConvencionalDefault = "022222222";
    private const string CelularDefault = "0999999999";

    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ClienteService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<Cliente>> ObtenerClientesAsync()
    {
        await using var context = await _dbFactory.CreateDbContextAsync();

        return await context.Clientes
            .Include(x => x.CiudadNavegacion!)
                .ThenInclude(c => c.Provincia!)
                    .ThenInclude(p => p.Pais)
            .ToListAsync();
    }

    public async Task EnsureConsumidorFinalAsync(int ownerUserId)
    {
        if (ownerUserId <= 0)
            return;

        await using var context = await _dbFactory.CreateDbContextAsync();
        await EnsureConsumidorFinalAsync(context, ownerUserId);
    }

    public async Task EnsureConsumidorFinalAsync(AppDbContext context, int ownerUserId)
    {
        if (ownerUserId <= 0)
            return;

        ArgumentNullException.ThrowIfNull(context);

        var identificaciones = await context.Identificacion
            .AsNoTracking()
            .OrderBy(x => x.IdeSec)
            .ToListAsync();

        var identificacionConsumidorFinal = identificaciones.FirstOrDefault(x =>
                x.IdeCodigo == ConsumidorFinalCodigoIdentificacion ||
                string.Equals(
                    (x.IdeDescripcion ?? string.Empty).Trim(),
                    $"{ConsumidorNombre} {ConsumidorApellido}",
                    StringComparison.OrdinalIgnoreCase));

        if (identificacionConsumidorFinal is null)
            return;

        var tipoClienteConsumidorFinal = await ResolveTipoClienteConsumidorFinalAsync(context);
        if (tipoClienteConsumidorFinal is null)
            return;

        var ubicacionDefault = await ResolveUbicacionDefaultAsync(context);
        var idVendedor = await context.Usuarios
            .AsNoTracking()
            .Where(x => x.IdUsuario == ownerUserId)
            .Select(x => x.IdVendedor)
            .FirstOrDefaultAsync();
        var cliente = await ResolveConsumidorFinalAsync(context, ownerUserId);
        var esNuevo = cliente is null;

        if (esNuevo)
        {
            cliente = new Cliente
            {
                Usuario = ownerUserId
            };

            context.Clientes.Add(cliente);
        }

        var huboCambios = esNuevo;

        huboCambios |= SetIfDifferent(cliente!, x => x.Apellidos, ConsumidorApellido);
        huboCambios |= SetIfDifferent(cliente!, x => x.Nombres, ConsumidorNombre);
        huboCambios |= SetIfDifferent(cliente!, x => x.Nombrecomercial, null);
        huboCambios |= SetIfDifferent(cliente!, x => x.Nombrerazonsocial, null);
        huboCambios |= SetIfDifferent(cliente!, x => x.Numeroidentificacion, ConsumidorFinalIdentificacion);
        huboCambios |= SetIfDifferent(cliente!, x => x.Tipoidentificacion, identificacionConsumidorFinal.IdeCodigo);
        huboCambios |= SetIfDifferent(cliente!, x => x.TipoCliente, tipoClienteConsumidorFinal.TclCodigo);
        huboCambios |= SetIfDifferent(cliente!, x => x.Oblgconta, "NO");
        huboCambios |= SetIfDifferent(cliente!, x => x.Estado, true);
        huboCambios |= SetIfDifferent(cliente!, x => x.Usuario, ownerUserId);
        huboCambios |= SetIfDifferent(cliente!, x => x.Idvendedor, idVendedor);

        if (string.IsNullOrWhiteSpace(cliente!.Direccion))
            huboCambios |= SetIfDifferent(cliente, x => x.Direccion, "Consumidor Final");

        if (string.IsNullOrWhiteSpace(cliente.Telefonoconvencional))
            huboCambios |= SetIfDifferent(cliente, x => x.Telefonoconvencional, TelefonoConvencionalDefault);

        if (string.IsNullOrWhiteSpace(cliente.Celular))
            huboCambios |= SetIfDifferent(cliente, x => x.Celular, CelularDefault);

        huboCambios |= SetIfDifferent(cliente!, x => x.Correo, ConsumidorFinalCorreo);

        huboCambios |= SetIfDifferent(cliente!, x => x.Pais, ubicacionDefault.PaisId);
        huboCambios |= SetIfDifferent(cliente!, x => x.Provincia, ubicacionDefault.ProvinciaId);
        huboCambios |= SetIfDifferent(cliente!, x => x.Ciudad, ubicacionDefault.CiudadId);

        if (!huboCambios)
            return;

        await context.SaveChangesAsync();
    }

    private static bool SetIfDifferent<TValue>(Cliente cliente, System.Linq.Expressions.Expression<Func<Cliente, TValue>> property, TValue newValue)
    {
        if (property.Body is not System.Linq.Expressions.MemberExpression memberExpression)
            throw new InvalidOperationException("La expresion no apunta a una propiedad valida.");

        if (memberExpression.Member is not System.Reflection.PropertyInfo propertyInfo)
            throw new InvalidOperationException("La expresion no apunta a una propiedad valida.");

        var currentValue = (TValue?)propertyInfo.GetValue(cliente);
        if (EqualityComparer<TValue>.Default.Equals(currentValue!, newValue))
            return false;

        propertyInfo.SetValue(cliente, newValue);
        return true;
    }

    private async Task<Cliente?> ResolveConsumidorFinalAsync(AppDbContext context, int ownerUserId)
    {
        var clientes = await context.Clientes
            .Where(x => x.Usuario == ownerUserId)
            .ToListAsync();

        return clientes.FirstOrDefault(x =>
            string.Equals((x.Numeroidentificacion ?? string.Empty).Trim(), ConsumidorFinalIdentificacion, StringComparison.OrdinalIgnoreCase) ||
            string.Equals((x.Tipoidentificacion ?? string.Empty).Trim(), ConsumidorFinalCodigoIdentificacion, StringComparison.OrdinalIgnoreCase) ||
            (string.Equals((x.Apellidos ?? string.Empty).Trim(), ConsumidorApellido, StringComparison.OrdinalIgnoreCase) &&
             string.Equals((x.Nombres ?? string.Empty).Trim(), ConsumidorNombre, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(
                $"{(x.Nombres ?? string.Empty).Trim()} {(x.Apellidos ?? string.Empty).Trim()}".Trim(),
                "Consumidor Final",
                StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Tipocliente?> ResolveTipoClienteConsumidorFinalAsync(AppDbContext context)
    {
        var tiposCliente = await context.Tipoclientes
            .AsNoTracking()
            .OrderBy(x => x.TclCodigo)
            .ToListAsync();

        return tiposCliente.FirstOrDefault(x => TipoClienteClasificacion.EsNatural(x.TclDescripcion))
               ?? tiposCliente.FirstOrDefault(x => !TipoClienteClasificacion.EsJuridica(x.TclDescripcion))
               ?? tiposCliente.FirstOrDefault();
    }

    private async Task<(int? PaisId, int? ProvinciaId, int? CiudadId)> ResolveUbicacionDefaultAsync(AppDbContext context)
    {
        var paises = await context.Paises
            .AsNoTracking()
            .OrderBy(x => x.Descripcion)
            .ToListAsync();

        var pais = paises.FirstOrDefault(x =>
                       string.Equals((x.Descripcion ?? string.Empty).Trim(), PaisDefault, StringComparison.OrdinalIgnoreCase))
                   ?? paises.FirstOrDefault();

        if (pais is null)
            return (null, null, null);

        var provincias = await context.Provincias
            .AsNoTracking()
            .Where(x => x.IdPais == pais.IdPais)
            .OrderBy(x => x.Descripcion)
            .ToListAsync();

        var provincia = provincias.FirstOrDefault(x =>
                            UbicacionEcuadorCatalogService.NormalizarClaveUbicacion(x.Descripcion) == ProvinciaDefault)
                        ?? provincias.FirstOrDefault();

        if (provincia is null)
            return (pais.IdPais, null, null);

        var ciudades = await context.Ciudades
            .AsNoTracking()
            .Where(x => x.IdProvincia == provincia.IdProvincia)
            .OrderBy(x => x.Descripcion)
            .ToListAsync();

        var ciudad = ciudades.FirstOrDefault(x =>
                         UbicacionEcuadorCatalogService.NormalizarClaveUbicacion(x.Descripcion) == CiudadDefault)
                     ?? ciudades.FirstOrDefault();

        return (pais.IdPais, provincia.IdProvincia, ciudad?.IdCiudad);
    }
}
