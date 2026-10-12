using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models.EDeclara;

namespace Simetric.Services.EDeclara;

// ─────────────────────────────────────────────────────────────────────────────
// Modelos del snapshot
// ─────────────────────────────────────────────────────────────────────────────

// ─────────────────────────────────────────────────────────────────────────────
// Servicio
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Proporciona datos de resumen del módulo E-Declara para el dashboard.
/// Usa las tablas existentes: CLIENTES (contribuyentes) y PRODUCTO (declaraciones).
///
/// Registro en Program.cs:
///   builder.Services.AddScoped&lt;EDeclaraSharedDataService&gt;();
/// </summary>
public sealed class EDeclaraSharedDataService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly EDeclaraDeclaracionesService _declaracionesService;

    public EDeclaraSharedDataService(
        IDbContextFactory<AppDbContext> dbFactory,
        EDeclaraDeclaracionesService declaracionesService)
    {
        _dbFactory = dbFactory;
        _declaracionesService = declaracionesService;
    }

    /// <summary>
    /// Devuelve un snapshot con totales y los últimos <paramref name="top"/> registros
    /// de contribuyentes (= Clientes) y declaraciones (= Productos) del usuario.
    /// </summary>
    public async Task<EDeclaraSharedDataSnapshot> GetSnapshotAsync(int userId, int top = 10)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // ── Contribuyentes → tabla Contribuyentes_Edeclare filtrada por usuario ──────────
        var totalContribuyentes = await db.ContribuyentesEdeclare
            .Where(c => c.Usuario == userId)
            .CountAsync();

        var contribuyentesActivos = await db.ContribuyentesEdeclare
            .Where(c => c.Usuario == userId && c.Estado == true)
            .CountAsync();

        var personasNaturales = await db.ContribuyentesEdeclare
            .Where(c => c.Usuario == userId && c.PersonaNatural == true)
            .CountAsync();

        var conCorreo = await db.ContribuyentesEdeclare
            .Where(c => c.Usuario == userId && c.Correo != null && c.Correo != "")
            .CountAsync();

        var contribuyentes = await db.ContribuyentesEdeclare
            .Where(c => c.Usuario == userId)
            .OrderByDescending(c => c.CodContribuyente)
            .Take(top)
            .Select(c => new EDeclaraContribuyenteResumen
            {
                Nombre = !string.IsNullOrWhiteSpace(c.Nombrerazonsocial) 
                    ? c.Nombrerazonsocial.Trim() 
                    : $"{c.Apellidos} {c.Nombres}".Trim(),
                Identificacion = c.Numeroidentificacion,
                Activo = c.Estado == true
            })
            .ToListAsync();

        var resumenDeclaraciones = await _declaracionesService.ObtenerResumenAsync(userId, top);
        var totalDeclaraciones = resumenDeclaraciones.Total;
        var declaraciones = resumenDeclaraciones.Items;

        return new EDeclaraSharedDataSnapshot
        {
            TotalContribuyentes = totalContribuyentes,
            TotalDeclaraciones  = totalDeclaraciones,
            ContribuyentesActivos = contribuyentesActivos,
            PersonasNaturales   = personasNaturales,
            ConCorreo           = conCorreo,
            Contribuyentes      = contribuyentes,
            Declaraciones       = declaraciones
        };
    }
}
