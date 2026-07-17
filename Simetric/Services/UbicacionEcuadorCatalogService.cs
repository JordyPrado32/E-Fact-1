using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Services;

public class UbicacionEcuadorCatalogService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    public const int SyntheticProvinceBase = 1;
    public const int SyntheticCityProvinceFactor = 1000;

    public UbicacionEcuadorCatalogService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<bool> EnsureCatalogoAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var idPaisEcuador = await db.Paises
            .Where(p => p.Descripcion != null && p.Descripcion.Trim().ToUpper() == "ECUADOR")
            .Select(p => p.IdPais)
            .FirstOrDefaultAsync();

        if (idPaisEcuador <= 0)
            return false;

        await CompletarCatalogoAsync(db, idPaisEcuador);
        return true;
    }

    public static int GetSyntheticProvinciaId(int provinciaIndex)
        => -(SyntheticProvinceBase + provinciaIndex);

    public static int GetSyntheticCiudadId(int provinciaIndex, int ciudadIndex)
        => -(((provinciaIndex + 1) * SyntheticCityProvinceFactor) + ciudadIndex + 1);

    public static bool TryGetProvinciaCatalogo(int? idProvincia, out ProvinciaEcuador provincia, out int provinciaIndex)
    {
        provincia = new ProvinciaEcuador();
        provinciaIndex = -1;

        if (!idProvincia.HasValue || idProvincia.Value >= 0)
            return false;

        var index = Math.Abs(idProvincia.Value) - SyntheticProvinceBase;
        if (index < 0 || index >= CatalogoUbicacionEcuador.Provincias.Count)
            return false;

        provincia = CatalogoUbicacionEcuador.Provincias[index];
        provinciaIndex = index;
        return true;
    }

    public static bool TryGetCiudadCatalogo(int? idCiudad, out string ciudad, out int provinciaIndex, out int ciudadIndex)
    {
        ciudad = string.Empty;
        provinciaIndex = -1;
        ciudadIndex = -1;

        if (!idCiudad.HasValue || idCiudad.Value >= 0)
            return false;

        var encoded = Math.Abs(idCiudad.Value);
        provinciaIndex = (encoded / SyntheticCityProvinceFactor) - 1;
        ciudadIndex = (encoded % SyntheticCityProvinceFactor) - 1;

        if (provinciaIndex < 0 ||
            provinciaIndex >= CatalogoUbicacionEcuador.Provincias.Count ||
            ciudadIndex < 0 ||
            ciudadIndex >= CatalogoUbicacionEcuador.Provincias[provinciaIndex].Cantones.Count)
        {
            return false;
        }

        ciudad = CatalogoUbicacionEcuador.Provincias[provinciaIndex].Cantones[ciudadIndex];
        return true;
    }

    private static async Task CompletarCatalogoAsync(AppDbContext db, int idPaisEcuador)
    {
        // Legacy catalog tables do not auto-generate their PKs in this database.
        var nextProvinciaId = await db.Provincias
            .Where(x => x.IdProvincia > 0)
            .Select(x => (int?)x.IdProvincia)
            .MaxAsync() ?? 0;

        var provinciasActuales = await db.Provincias
            .Where(x => x.IdPais == idPaisEcuador)
            .ToListAsync();

        foreach (var provinciaCatalogo in CatalogoUbicacionEcuador.Provincias)
        {
            var provincia = provinciasActuales.FirstOrDefault(x =>
                NormalizarClaveUbicacion(x.Descripcion) == NormalizarClaveUbicacion(provinciaCatalogo.Nombre));

            if (provincia is null)
            {
                provincia = new Provincia
                {
                    IdProvincia = ++nextProvinciaId,
                    IdPais = idPaisEcuador,
                    Descripcion = provinciaCatalogo.Nombre
                };

                db.Provincias.Add(provincia);
                provinciasActuales.Add(provincia);
            }
            else if (!string.Equals(provincia.Descripcion, provinciaCatalogo.Nombre, StringComparison.Ordinal))
            {
                provincia.Descripcion = provinciaCatalogo.Nombre;
            }
        }

        await db.SaveChangesAsync();

        var nextCiudadId = await db.Ciudades
            .Where(x => x.IdCiudad > 0)
            .Select(x => (int?)x.IdCiudad)
            .MaxAsync() ?? 0;

        var provinciaIds = provinciasActuales
            .Where(x => x.IdProvincia > 0)
            .Select(x => x.IdProvincia)
            .ToList();

        var ciudadesActuales = await db.Ciudades
            .Where(x => x.IdProvincia.HasValue && provinciaIds.Contains(x.IdProvincia.Value))
            .ToListAsync();

        foreach (var provinciaCatalogo in CatalogoUbicacionEcuador.Provincias)
        {
            var provincia = provinciasActuales.FirstOrDefault(x =>
                NormalizarClaveUbicacion(x.Descripcion) == NormalizarClaveUbicacion(provinciaCatalogo.Nombre));

            if (provincia is null || provincia.IdProvincia <= 0)
                continue;

            foreach (var cantonCatalogo in provinciaCatalogo.Cantones)
            {
                var existeCiudad = ciudadesActuales.Any(x =>
                    x.IdProvincia == provincia.IdProvincia &&
                    NormalizarClaveUbicacion(x.Descripcion) == NormalizarClaveUbicacion(cantonCatalogo));

                if (existeCiudad)
                    continue;

                var ciudad = new Ciudad
                {
                    IdCiudad = ++nextCiudadId,
                    IdProvincia = provincia.IdProvincia,
                    Descripcion = cantonCatalogo
                };

                db.Ciudades.Add(ciudad);
                ciudadesActuales.Add(ciudad);
            }
        }

        await db.SaveChangesAsync();
    }

    public static string NormalizarClaveUbicacion(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return string.Empty;

        var normalizado = valor.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalizado.Length);

        foreach (var caracter in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caracter) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToUpperInvariant(caracter));
        }

        return builder.ToString();
    }
}
