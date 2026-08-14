using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;

namespace Simetric.Services.ESign;

public sealed class ESignFirmaAccessService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public ESignFirmaAccessService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<ESignFirmaAccessResult> ObtenerAccesoAsync(
        int usuarioId,
        CancellationToken cancellationToken = default)
    {
        if (usuarioId <= 0)
        {
            return ESignFirmaAccessResult.SinAcceso("No se pudo identificar al usuario.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var solicitudesPagadas = await context.UsuSolicitudFirma
            .AsNoTracking()
            .Where(s => s.SolIdUsuarioCliente == usuarioId &&
                        s.SolActivo &&
                        s.SolPagoExitoso == true)
            .Select(s => new
            {
                s.SolId,
                s.SolFechaPago,
                s.SolFechaAprobacion,
                s.SolFechaSolicitud,
                s.SolVigencia
            })
            .ToListAsync(cancellationToken);

        var ahora = DateTime.Now;
        var periodos = solicitudesPagadas
            .Select(s =>
            {
                // Las solicitudes antiguas pueden no tener SOLFECHAPAGO aunque consten pagadas.
                var inicio = s.SolFechaPago ?? s.SolFechaAprobacion ?? s.SolFechaSolicitud;
                return new
                {
                    s.SolId,
                    Inicio = inicio,
                    Fin = CalcularFechaFin(inicio, s.SolVigencia),
                    s.SolVigencia
                };
            })
            .OrderByDescending(p => p.Fin)
            .ToList();

        var activo = periodos.FirstOrDefault(p => p.Inicio <= ahora && ahora < p.Fin);
        if (activo is not null)
        {
            return new ESignFirmaAccessResult(
                true,
                activo.Inicio,
                activo.Fin,
                activo.SolId,
                activo.SolVigencia,
                $"Acceso activo hasta el {activo.Fin:dd/MM/yyyy}.");
        }

        var ultimo = periodos.FirstOrDefault();
        return ultimo is null
            ? ESignFirmaAccessResult.SinAcceso("Necesitas comprar y pagar una firma en E-Rúbrica para firmar PDFs.")
            : new ESignFirmaAccessResult(
                false,
                ultimo.Inicio,
                ultimo.Fin,
                ultimo.SolId,
                ultimo.SolVigencia,
                $"Tu período para firmar PDFs venció el {ultimo.Fin:dd/MM/yyyy}.");
    }

    internal static DateTime CalcularFechaFin(DateTime inicio, string? vigencia)
    {
        var texto = QuitarDiacriticos(vigencia ?? string.Empty).ToUpperInvariant();
        var match = Regex.Match(texto, @"\d+");
        var cantidad = match.Success && int.TryParse(match.Value, out var valor) && valor > 0
            ? valor
            : 1;

        if (texto.Contains("DIA", StringComparison.Ordinal))
            return inicio.AddDays(cantidad);

        if (texto.Contains("MES", StringComparison.Ordinal))
            return inicio.AddMonths(cantidad);

        return inicio.AddYears(Math.Clamp(cantidad, 1, 10));
    }

    private static string QuitarDiacriticos(string valor)
    {
        var normalizado = valor.Normalize(NormalizationForm.FormD);
        var resultado = new StringBuilder(normalizado.Length);
        foreach (var caracter in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caracter) != UnicodeCategory.NonSpacingMark)
                resultado.Append(caracter);
        }

        return resultado.ToString().Normalize(NormalizationForm.FormC);
    }
}

public sealed record ESignFirmaAccessResult(
    bool TieneAcceso,
    DateTime? VigenteDesde,
    DateTime? VigenteHasta,
    int? SolicitudId,
    string? VigenciaContratada,
    string Mensaje)
{
    public static ESignFirmaAccessResult SinAcceso(string mensaje) =>
        new(false, null, null, null, null, mensaje);
}
