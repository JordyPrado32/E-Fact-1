using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Services;

public interface ICajaSerieResolver
{
    /// <summary>
    /// Resuelve la caja y serie del usuario autenticado a partir de la caja activa
    /// que tiene asignada en la tabla CAJA.
    /// </summary>
    Task<CajaSerieResolucion> ResolverAsync(int idUsuario, string? preferredSeriesRaw = null);

    /// <summary>
    /// Obtiene la entidad Caja activa del usuario autenticado.
    /// </summary>
    Task<Caja?> ObtenerCajaAsync(int idUsuario, string? preferredSeriesRaw = null, bool tracking = false);

    /// <summary>
    /// Lista las series de factura activas disponibles para la cuenta del usuario.
    /// </summary>
    Task<List<CajaSerieResolucion>> ListarSeriesFacturaAsync(int idUsuario);
}

public sealed record CajaSerieResolucion(
    int IdUsuario,
    int CajaSec,
    int IdTitularCuenta,
    int NumeroCaja,
    string Establecimiento,
    string PuntoEmision,
    string SerieVisual,
    string SerieRaw);

public sealed class CajaSerieResolver : ICajaSerieResolver
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public CajaSerieResolver(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<CajaSerieResolucion> ResolverAsync(int idUsuario, string? preferredSeriesRaw = null)
    {
        if (idUsuario <= 0)
            throw new Exception("No se pudo identificar al usuario actual para resolver la serie.");

        await using var context = await _dbFactory.CreateDbContextAsync();

        var caja = await BuscarCajaAsync(context, idUsuario, preferredSeriesRaw, tracking: false);

        if (caja == null)
            throw new Exception("El usuario no tiene una caja activa asignada.");

        var numeroCaja = caja.NumCaja ?? 0;
        if (numeroCaja <= 0)
            throw new Exception("La caja asignada no tiene un numero valido.");

        var serieVisual = NormalizarSerieVisual(caja.SerieFactura, numeroCaja);
        var establecimiento = ExtraerEstablecimiento(serieVisual);
        var puntoEmision = ExtraerPuntoEmision(serieVisual);

        return new CajaSerieResolucion(
            IdUsuario: idUsuario,
            CajaSec: caja.Sec,
            IdTitularCuenta: idUsuario,
            NumeroCaja: numeroCaja,
            Establecimiento: establecimiento,
            PuntoEmision: puntoEmision,
            SerieVisual: serieVisual,
            SerieRaw: serieVisual.Replace("-", string.Empty));
    }

    public async Task<Caja?> ObtenerCajaAsync(int idUsuario, string? preferredSeriesRaw = null, bool tracking = false)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var caja = await BuscarCajaAsync(context, idUsuario, preferredSeriesRaw, tracking);

        if (caja == null)
        {
            return null;
        }

        var numeroCaja = caja.NumCaja ?? 0;
        var serieVisual = NormalizarSerieVisual(caja.SerieFactura, numeroCaja);
        NormalizarSeriesCajaEnMemoria(caja, serieVisual);
        return caja;
    }

    public async Task<List<CajaSerieResolucion>> ListarSeriesFacturaAsync(int idUsuario)
    {
        if (idUsuario <= 0)
            return new List<CajaSerieResolucion>();

        await using var context = await _dbFactory.CreateDbContextAsync();
        var usuariosCuenta = await GetUsuariosSincronizadosPorEmisorRucAsync(context, idUsuario);
        if (usuariosCuenta.Count == 0)
            return new List<CajaSerieResolucion>();

        var cajas = await context.Caja
            .AsNoTracking()
            .Where(c =>
                c.Estado == true &&
                c.IdUsuario.HasValue &&
                usuariosCuenta.Contains(c.IdUsuario.Value) &&
                c.SerieFactura != null &&
                c.SerieFactura != string.Empty)
            .OrderBy(c => c.NumCaja == 1 ? 0 : 1)
            .ThenBy(c => c.NumCaja)
            .ThenBy(c => c.Sec)
            .ToListAsync();

        return cajas
            .Select(c =>
            {
                var numeroCaja = c.NumCaja ?? 0;
                var serieVisual = NormalizarSerieVisual(c.SerieFactura, numeroCaja);
                return new CajaSerieResolucion(
                    IdUsuario: idUsuario,
                    CajaSec: c.Sec,
                    IdTitularCuenta: idUsuario,
                    NumeroCaja: numeroCaja,
                    Establecimiento: ExtraerEstablecimiento(serieVisual),
                    PuntoEmision: ExtraerPuntoEmision(serieVisual),
                    SerieVisual: serieVisual,
                    SerieRaw: serieVisual.Replace("-", string.Empty));
            })
            .GroupBy(s => s.SerieRaw)
            .Select(g => g.First())
            .ToList();
    }

    private static async Task<Caja?> BuscarCajaAsync(
        AppDbContext context,
        int idUsuario,
        string? preferredSeriesRaw,
        bool tracking)
    {
        IQueryable<Caja> query = context.Caja;
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        var preferredSeries = SoloDigitos(preferredSeriesRaw);
        if (preferredSeries.Length >= 6)
        {
            preferredSeries = preferredSeries[..6];
            var usuariosCuenta = await GetUsuariosSincronizadosPorEmisorRucAsync(context, idUsuario);
            if (usuariosCuenta.Count > 0)
            {
                var cajasCandidatas = await query
                    .Where(c =>
                        c.Estado == true &&
                        c.IdUsuario.HasValue &&
                        usuariosCuenta.Contains(c.IdUsuario.Value))
                    .OrderBy(c => c.NumCaja)
                    .ThenBy(c => c.Sec)
                    .ToListAsync();

                var cajaPreferida = cajasCandidatas.FirstOrDefault(c =>
                    SoloDigitos(c.SerieFactura) == preferredSeries);

                if (cajaPreferida != null)
                {
                    return cajaPreferida;
                }
            }

            var cajaSistemaPreferida = await query
                .Where(c =>
                    c.Estado == true &&
                    c.EsCajaSistema == true &&
                    c.SerieFactura != null)
                .OrderBy(c => c.NumCaja)
                .ThenBy(c => c.Sec)
                .FirstOrDefaultAsync(c => SoloDigitos(c.SerieFactura) == preferredSeries);

            if (cajaSistemaPreferida != null)
            {
                return cajaSistemaPreferida;
            }
        }

        var usuariosSincronizados = await GetUsuariosSincronizadosPorEmisorRucAsync(context, idUsuario);
        if (usuariosSincronizados.Count > 0)
        {
            var cajaSincronizada = await query
                .Where(c =>
                    c.Estado == true &&
                    c.IdUsuario.HasValue &&
                    usuariosSincronizados.Contains(c.IdUsuario.Value))
                .OrderBy(c => c.NumCaja)
                .ThenBy(c => c.Sec)
                .FirstOrDefaultAsync();

            if (cajaSincronizada != null)
            {
                return cajaSincronizada;
            }
        }

        var cajaSistema = await query
            .Where(c => c.Estado == true && c.EsCajaSistema == true)
            .OrderBy(c => c.NumCaja)
            .ThenBy(c => c.Sec)
            .FirstOrDefaultAsync();

        if (cajaSistema != null)
        {
            return cajaSistema;
        }

        return await query
            .Where(c => c.Estado == true && c.IdUsuario == idUsuario)
            .OrderBy(c => c.NumCaja)
            .ThenBy(c => c.Sec)
            .FirstOrDefaultAsync();
    }

    private static void NormalizarSeriesCajaEnMemoria(Caja caja, string serieVisual)
    {
        caja.SerieFactura = serieVisual;
        caja.SerieGuia = serieVisual;
        caja.SerieNotasCred = serieVisual;
        caja.SerieDebitos = serieVisual;
        caja.SerieCompras = serieVisual;
    }

    private static string NormalizarSerieVisual(string? serie, int numeroCaja)
    {
        var establecimiento = ExtraerEstablecimiento(serie);
        var punto = ExtraerPuntoEmision(serie);

        if (string.IsNullOrWhiteSpace(establecimiento))
        {
            establecimiento = "001";
        }

        if (string.IsNullOrWhiteSpace(punto))
        {
            punto = numeroCaja > 0 ? numeroCaja.ToString("D3") : "001";
        }

        return $"{establecimiento}-{punto}";
    }

    private static string ExtraerEstablecimiento(string? serie)
    {
        if (string.IsNullOrWhiteSpace(serie))
            return "001";

        var raw = serie.Trim();
        if (raw.Contains('-'))
        {
            var parts = raw.Split('-', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? SoloDigitos(parts[0]).PadLeft(3, '0') : "001";
        }

        var digits = SoloDigitos(raw);
        return digits.Length >= 3 ? digits[..3] : digits.PadLeft(3, '0');
    }

    private static string ExtraerPuntoEmision(string? serie)
    {
        if (string.IsNullOrWhiteSpace(serie))
            return "001";

        var raw = serie.Trim();
        if (raw.Contains('-'))
        {
            var parts = raw.Split('-', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 ? SoloDigitos(parts[1]).PadLeft(3, '0') : "001";
        }

        var digits = SoloDigitos(raw);
        return digits.Length >= 6 ? digits.Substring(3, 3) : "001";
    }

    private static string SoloDigitos(string? value)
        => new string((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static async Task<int> GetTitularCuentaIdAsync(AppDbContext context, int idUsuario)
    {
        if (idUsuario <= 0)
            return 0;

        var usuario = await context.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.IdUsuario == idUsuario);

        if (usuario == null)
            return 0;

        return usuario.estadoAsociado == true && usuario.idJefe is > 0
            ? usuario.idJefe.Value
            : usuario.IdUsuario;
    }

    private static async Task<List<int>> GetUsuariosCuentaIdsAsync(AppDbContext context, int idUsuario)
    {
        var titularId = await GetTitularCuentaIdAsync(context, idUsuario);
        if (titularId <= 0)
            return new List<int>();

        return await context.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == titularId || (u.idJefe == titularId && u.estadoAsociado == true))
            .Select(u => u.IdUsuario)
            .ToListAsync();
    }

    private static async Task<List<int>> GetUsuariosSincronizadosPorEmisorRucAsync(AppDbContext context, int idUsuario)
    {
        var usuarios = await GetUsuariosCuentaIdsAsync(context, idUsuario);
        if (!usuarios.Contains(idUsuario))
        {
            usuarios.Add(idUsuario);
        }

        var rucs = await context.Emisores
            .AsNoTracking()
            .Where(e =>
                e.Estado &&
                !e.EsEmisorSistema &&
                e.IdUsuario.HasValue &&
                usuarios.Contains(e.IdUsuario.Value) &&
                e.Ruc != null &&
                e.Ruc != string.Empty)
            .Select(e => e.Ruc!.Trim())
            .Distinct()
            .ToListAsync();

        if (rucs.Count == 0)
        {
            return usuarios.Distinct().ToList();
        }

        var usuariosPorRuc = await context.Emisores
            .AsNoTracking()
            .Where(e =>
                e.Estado &&
                !e.EsEmisorSistema &&
                e.IdUsuario.HasValue &&
                e.Ruc != null &&
                rucs.Contains(e.Ruc.Trim()))
            .Select(e => e.IdUsuario!.Value)
            .Distinct()
            .ToListAsync();

        return usuarios
            .Concat(usuariosPorRuc)
            .Distinct()
            .ToList();
    }
}
