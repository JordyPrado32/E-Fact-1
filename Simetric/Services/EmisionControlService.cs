using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;
using System.Text.Json;

namespace Simetric.Services;

public sealed class EmisionEstado
{
    public bool TieneEmisorActivo { get; init; }
    public bool TieneFirmaElectronica { get; init; }
    public bool PuedeEmitir { get; init; }
    public bool MostrarAviso { get; init; }
    public bool BloqueoForzado { get; init; }
    public bool EmisionPermitidaPorConfiguracion { get; init; }
    public bool RequiereCompraDocumentos { get; init; }
    public bool AdvertenciaSaldoBajo { get; init; }
    public bool PlanIlimitadoActivo { get; init; }
    public DateTime? PlanIlimitadoVigenteHasta { get; init; }
    public int SaldoDocumentosDisponibles { get; init; }
    public string Titulo { get; init; } = string.Empty;
    public string Mensaje { get; init; } = string.Empty;
    public string RutaAccion { get; init; } = "/emisor";
    public string TextoAccion { get; init; } = "Activar cuenta";
}

public sealed class EmisionBloqueadaException : InvalidOperationException
{
    public EmisionBloqueadaException(string message) : base(message)
    {
    }
}

public sealed class EmisionControlService
{
    private static readonly JsonSerializerOptions HistorialJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class PlanIlimitadoInfo
    {
        public bool Activo { get; init; }
        public DateTime? VigenciaHasta { get; init; }
    }

    private sealed class UsuarioEmisionInfo
    {
        public int IdUsuario { get; init; }
        public bool EsAsociado { get; init; }
        public int? IdJefe { get; init; }
        public int? SaldoDocumentos { get; init; }
        public DateTime? FechaUltimaRecargaDocumentos { get; init; }
        public string? HistorialComprasDocumentosJson { get; init; }

        public int IdUsuarioTitularCuenta =>
            EsAsociado && IdJefe is > 0
                ? IdJefe.Value
                : IdUsuario;
    }

    public const int UmbralSaldoBajoDocumentos = 5;
    public const string MensajeFirmaRequerida =
        "Para poder emitir comprobantes electronicos, debes cargar tu firma electronica valida.";
    public const string MensajeSaldoAgotado =
        "Ya no tienes documentos disponibles. Para emitir mas comprobantes, realiza una nueva compra de documentos.";
    public const string RutaCompraDocumentos = "/compra-documentos";

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly AuditService _auditService;
    private readonly AuditActorResolver _auditActorResolver;
    private readonly EmisorCertificadoValidator _emisorCertificadoValidator;

    public EmisionControlService(
        IDbContextFactory<AppDbContext> dbFactory,
        AuditService auditService,
        AuditActorResolver auditActorResolver,
        EmisorCertificadoValidator emisorCertificadoValidator)
    {
        _dbFactory = dbFactory;
        _auditService = auditService;
        _auditActorResolver = auditActorResolver;
        _emisorCertificadoValidator = emisorCertificadoValidator;
    }

    public event Action? Changed;

    public async Task<EmisionEstado> ObtenerEstadoAsync(int? idUsuario)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        return await ObtenerEstadoAsync(context, idUsuario);
    }

    public async Task<int> ObtenerSaldoDisponibleAsync(int? idUsuario)
    {
        if (idUsuario is not > 0)
        {
            return 0;
        }

        await using var context = await _dbFactory.CreateDbContextAsync();
        return await ObtenerSaldoDisponibleAsync(context, idUsuario.Value);
    }

    public async Task AsegurarPuedeEmitirAsync(int? idUsuario)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        await AsegurarPuedeEmitirAsync(context, idUsuario);
    }

    public async Task AsegurarPuedeEmitirAsync(AppDbContext context, int? idUsuario)
    {
        var estado = await ObtenerEstadoAsync(context, idUsuario);

        if (!estado.PuedeEmitir)
        {
            throw new EmisionBloqueadaException(estado.Mensaje);
        }
    }

    public async Task ConsumirDocumentoAsync(AppDbContext context, int? idUsuario)
    {
        await AsegurarPuedeEmitirAsync(context, idUsuario);
        await ConsumirDocumentoValidadoAsync(context, idUsuario);
    }

    public async Task ConsumirDocumentoValidadoAsync(AppDbContext context, int? idUsuario)
    {
        var usuarioInfo = await ObtenerUsuarioEmisionInfoAsync(context, idUsuario!.Value);

        if (ObtenerPlanIlimitado(usuarioInfo.HistorialComprasDocumentosJson).Activo)
        {
            return;
        }

        var idUsuarioSaldo = usuarioInfo.IdUsuarioTitularCuenta;
        var saldoPrevio = new
        {
            IdUsuario = idUsuarioSaldo,
            SaldoDocumentos = usuarioInfo.SaldoDocumentos,
            FechaUltimaRecargaDocumentos = usuarioInfo.FechaUltimaRecargaDocumentos
        };

        var filasAfectadas = await context.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE Usuarios
               SET SaldoDocumentos = SaldoDocumentos - 1
               WHERE IdUsuario = {idUsuarioSaldo}
                 AND ISNULL(SaldoDocumentos, 0) > 0");

        if (filasAfectadas <= 0)
        {
            throw new EmisionBloqueadaException(MensajeSaldoAgotado);
        }

        var saldoNuevo = await context.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == idUsuarioSaldo)
            .Select(u => new
            {
                u.IdUsuario,
                u.SaldoDocumentos,
                u.FechaUltimaRecargaDocumentos
            })
            .FirstOrDefaultAsync();

        if (_auditService.IsEnabled)
        {
            var detalle = new Dictionary<string, object?>
            {
                ["Entidad"] = "Usuario",
                ["Tabla"] = "Usuarios",
                ["Llaves"] = new { IdUsuario = idUsuarioSaldo },
                ["Operacion"] = "ConsumoSaldoDocumentos"
            };

            var ruta = _auditActorResolver.ResolveRequestPath();
            if (!string.IsNullOrWhiteSpace(ruta))
            {
                detalle["Ruta"] = ruta;
            }

            var direccionIp = _auditActorResolver.ResolveRemoteIpAddress();
            if (!string.IsNullOrWhiteSpace(direccionIp))
            {
                detalle["DireccionIP"] = direccionIp;
            }

            await _auditService.TryRegistrarAuditoriaAsync(
                _auditActorResolver.ResolveCurrentUserId(),
                "MODIFICAR",
                saldoPrevio,
                saldoNuevo,
                detalle);
        }
    }

    public void NotifyChanged() => Changed?.Invoke();

    public async Task<EmisionEstado> ObtenerEstadoAsync(AppDbContext context, int? idUsuario)
    {
        if (idUsuario is not > 0)
        {
            return new EmisionEstado
            {
                PuedeEmitir = false,
                Titulo = "Sesion no identificada",
                Mensaje = "No se pudo identificar el usuario activo para validar la emision."
            };
        }

        var usuarioInfo = await ObtenerUsuarioEmisionInfoAsync(context, idUsuario.Value);
        var idUsuarioEmisor = usuarioInfo.IdUsuarioTitularCuenta;
        var emisor = await context.Emisores
            .AsNoTracking()
            .Where(e => e.IdUsuario == idUsuarioEmisor && e.Estado == true)
            .OrderBy(e => e.EsEmisorSistema)
            .ThenByDescending(e => e.Codigo)
            .FirstOrDefaultAsync();

        var tieneEmisor = emisor is not null;
        var validacionFirma = _emisorCertificadoValidator.Validar(emisor);
        var tieneFirma = validacionFirma.IsValid;
        var saldoDisponible = Math.Max(usuarioInfo.SaldoDocumentos ?? 0, 0);
        var planIlimitado = ObtenerPlanIlimitado(usuarioInfo.HistorialComprasDocumentosJson);
        var usaEmisorSistema = emisor?.EsEmisorSistema == true;

        if (!tieneFirma)
        {
            return new EmisionEstado
            {
                TieneEmisorActivo = tieneEmisor,
                TieneFirmaElectronica = false,
                PuedeEmitir = false,
                MostrarAviso = true,
                PlanIlimitadoActivo = planIlimitado.Activo,
                PlanIlimitadoVigenteHasta = planIlimitado.VigenciaHasta,
                SaldoDocumentosDisponibles = saldoDisponible,
                Titulo = "Cuenta pendiente de activacion",
                Mensaje = validacionFirma.TieneConfiguracion
                    ? validacionFirma.Message
                    : MensajeFirmaRequerida,
                RutaAccion = "/firma",
                TextoAccion = "Configurar firma"
            };
        }

        if (usaEmisorSistema)
        {
            return new EmisionEstado
            {
                TieneEmisorActivo = true,
                TieneFirmaElectronica = true,
                PuedeEmitir = true,
                EmisionPermitidaPorConfiguracion = true,
                PlanIlimitadoActivo = true,
                PlanIlimitadoVigenteHasta = null,
                SaldoDocumentosDisponibles = saldoDisponible
            };
        }

        if (planIlimitado.Activo)
        {
            return new EmisionEstado
            {
                TieneEmisorActivo = true,
                TieneFirmaElectronica = true,
                PuedeEmitir = true,
                EmisionPermitidaPorConfiguracion = true,
                PlanIlimitadoActivo = true,
                PlanIlimitadoVigenteHasta = planIlimitado.VigenciaHasta,
                SaldoDocumentosDisponibles = saldoDisponible
            };
        }

        if (saldoDisponible <= 0)
        {
            return new EmisionEstado
            {
                TieneEmisorActivo = true,
                TieneFirmaElectronica = true,
                PuedeEmitir = false,
                MostrarAviso = true,
                BloqueoForzado = true,
                RequiereCompraDocumentos = true,
                SaldoDocumentosDisponibles = 0,
                Titulo = "Documentos agotados",
                Mensaje = MensajeSaldoAgotado,
                RutaAccion = RutaCompraDocumentos,
                TextoAccion = "Comprar documentos"
            };
        }

        if (saldoDisponible < UmbralSaldoBajoDocumentos)
        {
            return new EmisionEstado
            {
                TieneEmisorActivo = true,
                TieneFirmaElectronica = true,
                PuedeEmitir = true,
                MostrarAviso = true,
                EmisionPermitidaPorConfiguracion = true,
                AdvertenciaSaldoBajo = true,
                SaldoDocumentosDisponibles = saldoDisponible,
                Titulo = "Saldo de documentos bajo",
                Mensaje = ConstruirMensajeSaldoBajo(saldoDisponible),
                RutaAccion = RutaCompraDocumentos,
                TextoAccion = "Comprar mas"
            };
        }

        return new EmisionEstado
        {
            TieneEmisorActivo = true,
            TieneFirmaElectronica = true,
            PuedeEmitir = true,
            EmisionPermitidaPorConfiguracion = true,
            SaldoDocumentosDisponibles = saldoDisponible
        };
    }

    private static string ConstruirMensajeSaldoBajo(int saldoDisponible)
    {
        var etiqueta = saldoDisponible == 1 ? "documento disponible" : "documentos disponibles";
        return $"Te quedan {saldoDisponible} {etiqueta}. Te recomendamos comprar mas documentos para no interrumpir tus emisiones.";
    }

    private static async Task<int> ObtenerSaldoDisponibleAsync(AppDbContext context, int idUsuario)
    {
        var usuarioInfo = await ObtenerUsuarioEmisionInfoAsync(context, idUsuario);
        return Math.Max(usuarioInfo.SaldoDocumentos ?? 0, 0);
    }

    private static async Task<UsuarioEmisionInfo> ObtenerUsuarioEmisionInfoAsync(AppDbContext context, int idUsuario)
    {
        if (idUsuario <= 0)
        {
            return new UsuarioEmisionInfo { IdUsuario = idUsuario };
        }

        var identidad = await context.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == idUsuario)
            .Select(u => new
            {
                u.IdUsuario,
                EsAsociado = u.estadoAsociado == true,
                u.idJefe
            })
            .FirstOrDefaultAsync();

        if (identidad is null)
        {
            return new UsuarioEmisionInfo { IdUsuario = idUsuario };
        }

        var idUsuarioTitular = identidad.EsAsociado && identidad.idJefe is > 0
            ? identidad.idJefe.Value
            : identidad.IdUsuario;

        var cuenta = await context.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == idUsuarioTitular)
            .Select(u => new UsuarioEmisionInfo
            {
                IdUsuario = identidad.IdUsuario,
                EsAsociado = identidad.EsAsociado,
                IdJefe = identidad.idJefe,
                SaldoDocumentos = u.SaldoDocumentos,
                FechaUltimaRecargaDocumentos = u.FechaUltimaRecargaDocumentos,
                HistorialComprasDocumentosJson = u.HistorialComprasDocumentosJson
            })
            .FirstOrDefaultAsync();

        return cuenta ?? new UsuarioEmisionInfo
        {
            IdUsuario = identidad.IdUsuario,
            EsAsociado = identidad.EsAsociado,
            IdJefe = identidad.idJefe
        };
    }

    private static PlanIlimitadoInfo ObtenerPlanIlimitado(string? historialJson)
    {
        if (string.IsNullOrWhiteSpace(historialJson))
        {
            return new PlanIlimitadoInfo();
        }

        try
        {
            var planesActivos = JsonSerializer.Deserialize<List<CompraDocumentosHistorialItem>>(
                    historialJson,
                    HistorialJsonOptions)?
                .Where(item =>
                    item.SaldoAplicado &&
                    item.EsIlimitado &&
                    (item.EsPermanente || item.VigenciaHasta > DateTime.Now))
                .ToList();

            if (planesActivos is not { Count: > 0 })
            {
                return new PlanIlimitadoInfo();
            }

            var esPermanente = planesActivos.Any(item => item.EsPermanente);
            return new PlanIlimitadoInfo
            {
                Activo = true,
                VigenciaHasta = esPermanente
                    ? null
                    : planesActivos.Max(item => item.VigenciaHasta)
            };
        }
        catch (JsonException)
        {
            return new PlanIlimitadoInfo();
        }
    }
}
