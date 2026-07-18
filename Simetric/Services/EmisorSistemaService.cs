using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;

namespace Simetric.Services;

public sealed class EmisorSistemaService
{
    private const string MarcadorCompraNotas = "[COMPRA_DOCS:";
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaEnsured;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly InitialSequencePromptService _initialSequencePromptService;
    private readonly ICajaSerieResolver _cajaSerieResolver;
    private readonly EmisorCertificadoValidator _emisorCertificadoValidator;

    public EmisorSistemaService(
        IDbContextFactory<AppDbContext> dbFactory,
        InitialSequencePromptService initialSequencePromptService,
        ICajaSerieResolver cajaSerieResolver,
        EmisorCertificadoValidator emisorCertificadoValidator)
    {
        _dbFactory = dbFactory;
        _initialSequencePromptService = initialSequencePromptService;
        _cajaSerieResolver = cajaSerieResolver;
        _emisorCertificadoValidator = emisorCertificadoValidator;
    }

    public async Task EnsureSchemaAsync()
    {
        if (_schemaEnsured)
            return;

        await SchemaLock.WaitAsync();
        try
        {
            if (_schemaEnsured)
                return;

            await using var context = await _dbFactory.CreateDbContextAsync();
            foreach (var statement in BuildEnsureSchemaStatements())
                await context.Database.ExecuteSqlRawAsync(statement);

            _schemaEnsured = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    public async Task<Emisor?> GetEmisorSistemaAsync()
    {
        await EnsureSchemaAsync();

        await using var context = await _dbFactory.CreateDbContextAsync();
        var emisor = await context.Emisores
            .AsNoTracking()
            .Include(e => e.Usuario)
            .Where(e => e.Estado && e.EsEmisorSistema)
            .OrderByDescending(e => e.Codigo)
            .FirstOrDefaultAsync();

        if (emisor != null)
            emisor.TieneClaveCertificadoConfigurada = !string.IsNullOrWhiteSpace(emisor.ClaveCertificado);

        return emisor;
    }

    public async Task<EmisorSistemaGuardadoResultado> GuardarAsync(int idUsuarioBackOffice, Emisor emisorInput)
    {
        await EnsureSchemaAsync();

        await using var contextoValidacion = await _dbFactory.CreateDbContextAsync();
        var usuarioBackOffice = await contextoValidacion.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.IdUsuario == idUsuarioBackOffice);

        if (!TienePermisoEmisorSistema(usuarioBackOffice?.IdTipoUsuario))
            return EmisorSistemaGuardadoResultado.Error("Solo los usuarios autorizados pueden configurar el emisor maestro.");

        NormalizarEmisor(emisorInput);
        emisorInput.CodEstablecimiento = NormalizarSerie(emisorInput.CodEstablecimiento, "001");
        emisorInput.CodPuntoEmision = NormalizarSerie(emisorInput.CodPuntoEmision, "001");
        emisorInput.Retenciones = NormalizarRespuesta(emisorInput.Retenciones, "NO");

        var error = ValidarEmisor(emisorInput);
        if (error != null)
            return EmisorSistemaGuardadoResultado.Error(error);

        var strategy = contextoValidacion.Database.CreateExecutionStrategy();
        Emisor? emisorGuardado = null;

        await strategy.ExecuteAsync(async () =>
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            await using var transaction = await context.Database.BeginTransactionAsync();

            var existente = await context.Emisores
                .Where(e => e.EsEmisorSistema)
                .OrderByDescending(e => e.Estado)
                .ThenByDescending(e => e.Codigo)
                .FirstOrDefaultAsync();

            var ownerId = existente?.IdUsuario ?? idUsuarioBackOffice;

            if (existente == null)
            {
                existente = new Emisor
                {
                    Estado = true,
                    IdUsuario = ownerId,
                    EsEmisorSistema = true
                };
                context.Emisores.Add(existente);
            }

            existente.RazonSocial = emisorInput.RazonSocial;
            existente.Ruc = emisorInput.Ruc;
            existente.NomComercial = emisorInput.NomComercial;
            existente.DirEstablecimiento = emisorInput.DirEstablecimiento;
            existente.CodEstablecimiento = emisorInput.CodEstablecimiento;
            existente.Resolusion = emisorInput.Resolusion;
            existente.ContribuyenteEspecial = emisorInput.ContribuyenteEspecial;
            existente.CodPuntoEmision = emisorInput.CodPuntoEmision;
            existente.LlevaContabilidad = emisorInput.LlevaContabilidad;
            existente.LogoImagen = emisorInput.LogoImagen;
            existente.TipoEmision = string.IsNullOrWhiteSpace(emisorInput.TipoEmision) ? "1" : emisorInput.TipoEmision;
            existente.TiempoEspera = emisorInput.TiempoEspera;
            existente.ClaveInterna = emisorInput.ClaveInterna;
            existente.TipoAmbiente = string.IsNullOrWhiteSpace(emisorInput.TipoAmbiente) ? "2" : emisorInput.TipoAmbiente;
            existente.DireccionMatriz = emisorInput.DireccionMatriz;
            existente.Token = emisorInput.Token;
            existente.Retenciones = emisorInput.Retenciones;
            existente.RetIva = emisorInput.RetIva;
            existente.RetFuente = emisorInput.RetFuente;
            existente.IdEmpresa = emisorInput.IdEmpresa;
            existente.IdSucursal = emisorInput.IdSucursal;
            existente.PathCertificado = emisorInput.PathCertificado;
            existente.Email = emisorInput.Email;
            existente.Direccion = emisorInput.Direccion;
            existente.Telefono = emisorInput.Telefono;
            existente.Estado = true;
            existente.IdUsuario = ownerId;
            existente.EsEmisorSistema = true;

            if (emisorInput.EliminarClaveCertificado)
                existente.ClaveCertificado = null;
            else if (!string.IsNullOrWhiteSpace(emisorInput.ClaveCertificado))
                existente.ClaveCertificado = emisorInput.ClaveCertificado;

            var validacionCertificado = _emisorCertificadoValidator.Validar(existente);
            if (!validacionCertificado.IsValid && validacionCertificado.TieneConfiguracion)
                throw new InvalidOperationException(validacionCertificado.Message);

            await context.SaveChangesAsync();

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE EMISOR SET es_emisor_sistema = 0 WHERE es_emisor_sistema = 1 AND codigo <> {existente.Codigo}");

            await transaction.CommitAsync();
            existente.TieneClaveCertificadoConfigurada = !string.IsNullOrWhiteSpace(existente.ClaveCertificado);
            emisorGuardado = existente;
        });

        return EmisorSistemaGuardadoResultado.Ok(emisorGuardado!);
    }

    public async Task<List<FacturaListDto>> ListarFacturasRecargasSistemaAsync(
        int top = 200,
        bool soloRecargas = true)
    {
        await EnsureSchemaAsync();

        await using var context = await _dbFactory.CreateDbContextAsync();
        var codEmisorSistema = await context.Emisores
            .AsNoTracking()
            .Where(e => e.Estado && e.EsEmisorSistema)
            .OrderByDescending(e => e.Codigo)
            .Select(e => (int?)e.Codigo)
            .FirstOrDefaultAsync();

        if (!codEmisorSistema.HasValue)
            return new List<FacturaListDto>();

        IQueryable<Factura> query = context.Facturas
            .AsNoTracking()
            .Include(f => f.CodclientesNavigation)
            .Where(f => f.Codemisor == codEmisorSistema.Value);

        if (soloRecargas)
        {
            query = query.Where(f =>
                f.Notas != null &&
                f.Notas.Contains(MarcadorCompraNotas));
        }

        query = query.OrderByDescending(f => f.Codfactura);

        if (top > 0)
            query = query.Take(top);

        return await query
            .Select(f => new FacturaListDto
            {
                Codfactura = f.Codfactura,
                Numfactura = f.Numfactura,
                Serie = f.Serie,
                FechaEmision = f.Fechaentrega,
                EstadoSri = f.Estadoenviosri,
                Autorizado = f.Autorizado,
                NumeroAutorizacion = f.Numautorizacion,
                FechaAutorizacion = f.Fchautorizacion,
                Total = f.Valortotal,
                Tipopago = f.Tipopago,
                Estado = f.Estado,
                Cliente = f.CodclientesNavigation != null
                    ? (!string.IsNullOrWhiteSpace(f.CodclientesNavigation.Nombrerazonsocial)
                        ? f.CodclientesNavigation.Nombrerazonsocial
                        : ((f.CodclientesNavigation.Nombres ?? "") + " " + (f.CodclientesNavigation.Apellidos ?? "")).Trim())
                    : null,
                IdentificacionCliente = f.CodclientesNavigation != null
                    ? f.CodclientesNavigation.Numeroidentificacion
                    : null
            })
            .ToListAsync();
    }

    public async Task<List<FacturaListDto>> ListarMisFacturasRecargasSistemaAsync(int ownerUserId, int top = 200)
    {
        await EnsureSchemaAsync();

        if (ownerUserId <= 0)
            return new List<FacturaListDto>();

        await using var context = await _dbFactory.CreateDbContextAsync();
        IQueryable<Factura> query = context.Facturas
            .AsNoTracking()
            .Include(f => f.CodemisorNavigation)
            .Include(f => f.CodclientesNavigation)
            .Where(f =>
                f.Idusuario == ownerUserId &&
                f.CodemisorNavigation != null &&
                f.CodemisorNavigation.EsEmisorSistema &&
                f.Notas != null &&
                f.Notas.Contains(MarcadorCompraNotas))
            .OrderByDescending(f => f.Codfactura);

        if (top > 0)
            query = query.Take(top);

        return await query
            .Select(f => new FacturaListDto
            {
                Codfactura = f.Codfactura,
                Numfactura = f.Numfactura,
                Serie = f.Serie,
                FechaEmision = f.Fechaentrega,
                EstadoSri = f.Estadoenviosri,
                Autorizado = f.Autorizado,
                NumeroAutorizacion = f.Numautorizacion,
                FechaAutorizacion = f.Fchautorizacion,
                Total = f.Valortotal,
                Tipopago = f.Tipopago,
                Estado = f.Estado,
                Cliente = f.CodclientesNavigation != null
                    ? (!string.IsNullOrWhiteSpace(f.CodclientesNavigation.Nombrerazonsocial)
                        ? f.CodclientesNavigation.Nombrerazonsocial
                        : ((f.CodclientesNavigation.Nombres ?? "") + " " + (f.CodclientesNavigation.Apellidos ?? "")).Trim())
                    : null,
                IdentificacionCliente = f.CodclientesNavigation != null
                    ? f.CodclientesNavigation.Numeroidentificacion
                    : null
            })
            .ToListAsync();
    }

    public async Task<bool> TieneAccesoBackOfficeAsync(int idUsuario)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.Usuarios
            .AsNoTracking()
            .AnyAsync(u => u.IdUsuario == idUsuario &&
                (u.IdTipoUsuario == 7 || u.IdTipoUsuario == 2) &&
                u.Estado == true);
    }

    public async Task<EmisorSistemaSecuenciaInfo?> GetSecuenciaFacturaSistemaAsync()
    {
        await EnsureSchemaAsync();

        var emisor = await GetEmisorSistemaAsync();
        if (emisor?.IdUsuario is not > 0 || emisor.Codigo <= 0)
            return null;

        await using var context = await _dbFactory.CreateDbContextAsync();
        var cajaSistema = await GetCajaPrincipalSistemaAsync(context);
        var serieRaw = NormalizarSerieCaja(cajaSistema?.SerieFactura);
        var state = cajaSistema?.SecuenciaFacturaInicializada == true
            ? new InitialSequencePromptState
            {
                Initialized = true,
                HadPreviousDocuments = cajaSistema.UltimoSecuencialFactura is > 0,
                PreviousSequence = cajaSistema.UltimoSecuencialFactura is > 0
                    ? cajaSistema.UltimoSecuencialFactura.Value.ToString("D9")
                    : string.Empty
            }
            : await _initialSequencePromptService.GetStateAsync(
                emisor.IdUsuario.Value,
                "factura",
                serieRaw,
                emisor.Codigo);

        var siguiente = state.Initialized && state.HadPreviousDocuments
            ? _initialSequencePromptService.GetNextSequenceFromPrevious(state.PreviousSequence)
            : string.Empty;

        return new EmisorSistemaSecuenciaInfo
        {
            EmisorCodigo = emisor.Codigo,
            OwnerUserId = emisor.IdUsuario.Value,
            SerieRaw = serieRaw,
            SerieVisual = FormatearSerieVisual(cajaSistema?.SerieFactura),
            Inicializada = state.Initialized,
            UltimaSecuencia = state.PreviousSequence,
            SiguienteSecuencia = siguiente
        };
    }

    public async Task<EmisorSistemaGuardadoResultado> GuardarSecuenciaFacturaSistemaAsync(
        int idUsuarioBackOffice,
        bool yaFacturoAntes,
        string? ultimaSecuencia)
    {
        await EnsureSchemaAsync();

        if (!await TieneAccesoBackOfficeAsync(idUsuarioBackOffice))
            return EmisorSistemaGuardadoResultado.Error("Solo los usuarios autorizados pueden configurar la secuencia del emisor maestro.");

        var emisor = await GetEmisorSistemaAsync();
        if (emisor?.IdUsuario is not > 0 || emisor.Codigo <= 0)
            return EmisorSistemaGuardadoResultado.Error("Primero debes configurar el emisor maestro del sistema.");

        await using var context = await _dbFactory.CreateDbContextAsync();
        var cajaSistema = await context.Caja
            .Where(c => c.Estado == true && c.EsCajaSistema == true)
            .OrderBy(c => c.NumCaja)
            .ThenBy(c => c.Sec)
            .FirstOrDefaultAsync();
        var serieRaw = NormalizarSerieCaja(cajaSistema?.SerieFactura);
        if (string.IsNullOrWhiteSpace(serieRaw))
            return EmisorSistemaGuardadoResultado.Error("Primero debes configurar la caja maestra del sistema para definir su secuencia.");

        string secuenciaNormalizada = string.Empty;

        if (yaFacturoAntes)
        {
            if (!_initialSequencePromptService.TryNormalizeSequence(ultimaSecuencia, out secuenciaNormalizada))
                return EmisorSistemaGuardadoResultado.Error("La secuencia indicada no es valida. Debe estar entre 000000001 y 999999999.");
        }

        cajaSistema!.SecuenciaFacturaInicializada = true;
        cajaSistema.UltimoSecuencialFactura = yaFacturoAntes && long.TryParse(secuenciaNormalizada, out var ultimoSecuencial)
            ? ultimoSecuencial
            : 0;
        await context.SaveChangesAsync();

        await _initialSequencePromptService.SaveStateAsync(
            emisor.IdUsuario.Value,
            "factura",
            serieRaw,
            new InitialSequencePromptState
            {
                Initialized = true,
                HadPreviousDocuments = yaFacturoAntes,
                PreviousSequence = secuenciaNormalizada
            },
            emisor.Codigo);

        var estadoGuardado = await _initialSequencePromptService.GetStateAsync(
            emisor.IdUsuario.Value,
            "factura",
            serieRaw,
            emisor.Codigo);
        var secuenciaEsperada = yaFacturoAntes ? secuenciaNormalizada : string.Empty;
        if (estadoGuardado.Initialized != true ||
            estadoGuardado.HadPreviousDocuments != yaFacturoAntes ||
            !string.Equals(estadoGuardado.PreviousSequence, secuenciaEsperada, StringComparison.Ordinal))
        {
            return EmisorSistemaGuardadoResultado.Error(
                "No se pudo confirmar el guardado de la secuencia en la caja maestra. Verifica la serie configurada e intenta nuevamente.");
        }

        return EmisorSistemaGuardadoResultado.Ok(emisor) with
        {
            Message = yaFacturoAntes
                ? $"Secuencia del sistema guardada correctamente. La siguiente factura saldra desde { _initialSequencePromptService.GetNextSequenceFromPrevious(secuenciaNormalizada) }."
                : "Secuencia del sistema inicializada correctamente desde 000000001."
        };
    }

    private static IEnumerable<string> BuildEnsureSchemaStatements()
    {
        yield return """
IF COL_LENGTH('dbo.EMISOR', 'es_emisor_sistema') IS NULL
BEGIN
    ALTER TABLE dbo.EMISOR ADD es_emisor_sistema bit NOT NULL CONSTRAINT DF_EMISOR_es_emisor_sistema DEFAULT(0);
END
""";
        yield return """
IF COL_LENGTH('dbo.CAJA', 'es_caja_sistema') IS NULL
BEGIN
    ALTER TABLE dbo.CAJA ADD es_caja_sistema bit NOT NULL CONSTRAINT DF_CAJA_es_caja_sistema DEFAULT(0);
END
""";
    }

    private static async Task<Caja?> GetCajaPrincipalSistemaAsync(AppDbContext context)
    {
        return await context.Caja
            .AsNoTracking()
            .Where(c => c.Estado == true && c.EsCajaSistema == true)
            .OrderBy(c => c.NumCaja)
            .ThenBy(c => c.Sec)
            .FirstOrDefaultAsync();
    }

    private static string NormalizarSerieCaja(string? serieFactura)
        => new string((serieFactura ?? string.Empty).Where(char.IsDigit).ToArray()) is var limpio && limpio.Length >= 6
            ? limpio[..6]
            : string.Empty;

    private static string FormatearSerieVisual(string? serieFactura)
        => string.IsNullOrWhiteSpace(serieFactura) ? "--" : serieFactura;

    private static string? NormalizarSerie(string? valor, string? valorPorDefecto = null)
    {
        var limpio = new string((valor ?? string.Empty).Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(limpio))
            return valorPorDefecto;

        return limpio.Length >= 3
            ? limpio[^3..]
            : limpio.PadLeft(3, '0');
    }

    private static string NormalizarRespuesta(string? valor, string valorPorDefecto)
    {
        var limpio = (valor ?? string.Empty).Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(limpio) ? valorPorDefecto : limpio;
    }

    private static void NormalizarEmisor(Emisor e)
    {
        e.TieneClaveCertificadoConfigurada = !string.IsNullOrWhiteSpace(e.ClaveCertificado);
        e.RazonSocial = e.RazonSocial?.Trim();
        e.Ruc = e.Ruc?.Trim();
        e.NomComercial = e.NomComercial?.Trim();
        e.DirEstablecimiento = e.DirEstablecimiento?.Trim();
        e.CodEstablecimiento = e.CodEstablecimiento?.Trim();
        e.CodPuntoEmision = e.CodPuntoEmision?.Trim();
        e.TipoEmision = e.TipoEmision?.Trim();
        e.Resolusion = e.Resolusion?.Trim();
        e.ContribuyenteEspecial = e.ContribuyenteEspecial?.Trim();
        e.LlevaContabilidad = e.LlevaContabilidad?.Trim().ToUpperInvariant();
        e.TipoAmbiente = e.TipoAmbiente?.Trim();
        e.DireccionMatriz = e.DireccionMatriz?.Trim();
        e.Token = e.Token?.Trim();
        e.Retenciones = e.Retenciones?.Trim().ToUpperInvariant();
        e.RetIva = e.RetIva?.Trim();
        e.RetFuente = e.RetFuente?.Trim();
        e.ClaveInterna = e.ClaveInterna?.Trim();
        e.PathCertificado = e.PathCertificado?.Trim().TrimStart('~', '/', '\\').Replace('\\', '/');
        if (!string.IsNullOrWhiteSpace(e.PathCertificado) && e.PathCertificado.StartsWith("App_Data/", StringComparison.OrdinalIgnoreCase))
            e.PathCertificado = e.PathCertificado["App_Data/".Length..];
        e.Email = e.Email?.Trim();
        e.Direccion = e.Direccion?.Trim();
        e.Telefono = e.Telefono?.Trim();
        e.TiempoEspera = e.TiempoEspera?.Trim();
        e.ClaveCertificado = e.ClaveCertificado?.Trim();
    }

    private static string? ValidarEmisor(Emisor e)
    {
        if (string.IsNullOrWhiteSpace(e.RazonSocial) && string.IsNullOrWhiteSpace(e.NomComercial))
            return "Debes ingresar la razon social o el nombre comercial.";

        if (!string.IsNullOrWhiteSpace(e.RazonSocial) && e.RazonSocial.Length > 70)
            return "La razon social no puede exceder 70 caracteres.";

        if (string.IsNullOrWhiteSpace(e.Ruc))
            return "El RUC es obligatorio.";

        if (!e.Ruc.All(char.IsDigit) || e.Ruc.Length != 13)
            return "El RUC debe tener exactamente 13 digitos.";

        if (!string.IsNullOrWhiteSpace(e.NomComercial) && e.NomComercial.Length > 100)
            return "El nombre comercial no puede exceder 100 caracteres.";

        if (!string.IsNullOrWhiteSpace(e.Email) && !e.Email.Contains("@"))
            return "El formato del correo electronico no es valido.";

        if (!string.IsNullOrWhiteSpace(e.Email) && e.Email.Length > 50)
            return "El email no puede exceder 50 caracteres.";

        if (string.IsNullOrWhiteSpace(e.DirEstablecimiento))
            return "La direccion del establecimiento es obligatoria.";

        if (e.DirEstablecimiento.Length > 250)
            return "La direccion del establecimiento no puede exceder 250 caracteres.";

        if (string.IsNullOrWhiteSpace(e.DireccionMatriz))
            return "La direccion matriz es obligatoria.";

        if (e.DireccionMatriz.Length > 255)
            return "La direccion matriz no puede exceder 255 caracteres.";

        if (!string.IsNullOrWhiteSpace(e.ClaveInterna) && e.ClaveInterna.Length > 25)
            return "La clave interna no puede exceder 25 caracteres.";

        if (!string.IsNullOrWhiteSpace(e.Telefono) &&
            (!e.Telefono.All(char.IsDigit) || e.Telefono.Length < 7 || e.Telefono.Length > 10))
            return "El telefono debe tener entre 7 y 10 digitos.";

        if (string.IsNullOrWhiteSpace(e.LlevaContabilidad))
            return "Debe seleccionar si lleva contabilidad.";

        if (e.LlevaContabilidad != "SI" && e.LlevaContabilidad != "NO")
            return "LlevaContabilidad solo permite SI o NO.";

        if (!string.IsNullOrWhiteSpace(e.PathCertificado) &&
            !string.Equals(Path.GetExtension(e.PathCertificado), ".p12", StringComparison.OrdinalIgnoreCase))
            return "El archivo de firma debe ser un certificado .p12 valido.";

        if (!string.IsNullOrWhiteSpace(e.PathCertificado) && e.PathCertificado.Length > 150)
            return "La ruta del certificado no puede exceder 150 caracteres.";

        if (!string.IsNullOrWhiteSpace(e.ClaveCertificado) && e.ClaveCertificado.Length > 50)
            return "La clave de la firma no puede exceder 50 caracteres.";

        return null;
    }

    private static bool TienePermisoEmisorSistema(int? idTipoUsuario) =>
        idTipoUsuario is 7 or 2;
}

public sealed record EmisorSistemaGuardadoResultado(bool Success, string Message, Emisor? Emisor)
{
    public static EmisorSistemaGuardadoResultado Ok(Emisor emisor) =>
        new(true, "Emisor maestro guardado correctamente.", emisor);

    public static EmisorSistemaGuardadoResultado Error(string message) =>
        new(false, message, null);
}

public sealed class EmisorSistemaSecuenciaInfo
{
    public int EmisorCodigo { get; init; }
    public int OwnerUserId { get; init; }
    public string SerieRaw { get; init; } = string.Empty;
    public string SerieVisual { get; init; } = string.Empty;
    public bool Inicializada { get; init; }
    public string? UltimaSecuencia { get; init; }
    public string? SiguienteSecuencia { get; init; }
}
