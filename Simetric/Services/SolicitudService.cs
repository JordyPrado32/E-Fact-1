using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.DTOs.ESign;
using Simetric.Models;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Configuration;
using Dapper;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using Simetric.Services.ESign;
using System.Text.RegularExpressions;

namespace Simetric.Services
{
    public class SolicitudService
    {
        private const string TipoObservacionCodigo = "CODIGO";
        private const string TipoObservacionCampo = "CAMPO";
        private const string TipoObservacionFirma = "FIRMA_P12";
        private const string EstadoObservacionPendiente = "PENDIENTE";
        private const string EstadoObservacionRespondida = "RESPONDIDA";
        private const string EstadoObservacionRevisada = "REVISADA";
        private const long MaxArchivoP12Bytes = 5 * 1024 * 1024;
        private const long MaxDocumentoBytes = 10 * 1024 * 1024;
        private const long MaxVideoAceptacionBytes = 50 * 1024 * 1024;
        private const int EstadoSolicitudPendiente = 2;
        private static readonly string[] TiposDocumentoBase =
        [
            "CEDULA_FRONTAL",
            "CEDULA_POSTERIOR",
            "SELFIE_CEDULA"
        ];
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IWebHostEnvironment _env;
        private readonly IDataProtector _firmaProtector;
        private readonly BesPrecompraService _besPrecompraService;
        private readonly IConfiguration _configuration;
        public string? UltimoErrorCrearSolicitud { get; private set; }

        public SolicitudService(
            IDbContextFactory<AppDbContext> contextFactory,
            IWebHostEnvironment env,
            IDataProtectionProvider dataProtectionProvider,
            BesPrecompraService besPrecompraService,
            IConfiguration configuration)
        {
            _contextFactory = contextFactory;
            _env = env;
            _firmaProtector = dataProtectionProvider.CreateProtector("Simetric.SolicitudFirma.P12.v1");
            _besPrecompraService = besPrecompraService;
            _configuration = configuration;
        }

        // --- MÉTODOS PARA EL USUARIO (CLIENTE) ---

        public async Task<bool> CrearSolicitudFullAsync(UsuSolicitudFirma solicitud, List<(string TempFileName, string Tipo)> archivos)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            UltimoErrorCrearSolicitud = null;

            if (!TryValidarSolicitudCreacion(
                    solicitud,
                    archivos,
                    out var archivosNormalizados,
                    out var mensajeValidacion))
            {
                UltimoErrorCrearSolicitud = mensajeValidacion;
                return false;
            }

            // 1. Obtener la estrategia de ejecución para soportar transacciones con reintentos
            var strategy = context.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                // 2. Iniciar la transacción dentro de la estrategia
                using var transaction = await context.Database.BeginTransactionAsync();

                try
                {
                    // Configuración de campos iniciales
                    solicitud.SolIdEstadoNumerica = EstadoSolicitudPendiente;
                    solicitud.SolFechaSolicitud = DateTime.Now;
                    solicitud.SolActivo = true;

                    // Mapear banderas de archivos de Uanataca
                    solicitud.SolUanatacaHasFrontId = archivosNormalizados.Any(a => a.Tipo == "CEDULA_FRONTAL");
                    solicitud.SolUanatacaHasBackId = archivosNormalizados.Any(a => a.Tipo == "CEDULA_POSTERIOR");
                    solicitud.SolUanatacaHasSelfie = archivosNormalizados.Any(a => a.Tipo == "SELFIE_CEDULA");
                    solicitud.SolUanatacaHasRucFile = archivosNormalizados.Any(a => a.Tipo == "RUC_FILE");
                    solicitud.SolUanatacaHasSeniorVideo = archivosNormalizados.Any(a => a.Tipo == "VIDEO_ACEPTACION");
                    solicitud.SolUanatacaHasAppointment = archivosNormalizados.Any(a => a.Tipo == "NOMBRAMIENTO");
                    solicitud.SolUanatacaHasAcceptance = archivosNormalizados.Any(a => a.Tipo == "ACEPTACION_NOMBRAMIENTO");
                    solicitud.SolUanatacaHasConstitution = archivosNormalizados.Any(a => a.Tipo == "CONSTITUCION");
                    solicitud.SolUanatacaHasManagerId = archivosNormalizados.Any(a => a.Tipo == "CEDULA_REPRESENTANTE");
                    solicitud.SolUanatacaHasAuthorization = archivosNormalizados.Any(a => a.Tipo == "AUTORIZACION");
                    solicitud.SolUanatacaHasAdditional = archivosNormalizados.Any(a => a.Tipo == "ARCHIVO_ADICIONAL");

                    // Guardar la cabecera para obtener el ID (Identity)
                    context.UsuSolicitudFirma.Add(solicitud);
                    await context.SaveChangesAsync();

                    // 3. Procesar archivos físicos y registros
                    string uploadPath = Path.Combine(_env.WebRootPath, "uploads", "solicitudes");
                    if (!Directory.Exists(uploadPath)) Directory.CreateDirectory(uploadPath);

                    foreach (var item in archivosNormalizados)
                    {
                        string tempFullPath = Path.Combine(_env.WebRootPath, "uploads", "temp", item.TempFileName);
                        if (!File.Exists(tempFullPath))
                        {
                            throw new FileNotFoundException($"No se encontró el archivo temporal: {item.TempFileName}");
                        }

                        string ext = Path.GetExtension(item.TempFileName);
                        // Nombre único: ID_TIPO_GUID.ext
                        string fileName = $"{solicitud.SolId}_{item.Tipo}_{Guid.NewGuid()}{ext}";
                        string fullPath = Path.Combine(uploadPath, fileName);

                        // Mover el archivo físico desde el directorio temporal
                        File.Move(tempFullPath, fullPath);

                        var fileInfo = new FileInfo(fullPath);

                        // Crear registro del documento vinculado a la solicitud
                        var nuevoDoc = new UsuSolicitudDocumento
                        {
                            DocIdSolicitud = solicitud.SolId,
                            DocTipo = item.Tipo,
                            DocNombreArchivo = fileName,
                            DocRutaArchivo = "/uploads/solicitudes/" + fileName,
                            DocExtension = ext,
                            DocTamanoArchivo = fileInfo.Length,
                            DocFechaCarga = DateTime.Now,
                            DocVigente = true,
                            DocVersion = 1,
                            DocObservacion = string.Empty
                        };
                        context.UsuSolicitudDocumento.Add(nuevoDoc);
                    }

                    // 4. Registrar historial inicial
                    var historialInicial = new UsuSolicitudEstadoHistorial
                    {
                        HisIdSolicitud = solicitud.SolId,
                        HisIdEstadoAnterior = null,
                        HisIdEstadoNuevo = EstadoSolicitudPendiente,
                        HisOrigenEstado = "NUMERICA",
                        HisComentario = "Solicitud ingresada correctamente con documentos.",
                        HisFechaCambio = DateTime.Now,
                        HisIdUsuarioResponsable = solicitud.SolIdUsuarioCliente
                    };
                    context.UsuSolicitudEstadoHistorial.Add(historialInicial);

                    // 5. Persistir cambios de documentos/historial y confirmar
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return true;
                }
                catch (Exception ex)
                {
                    // El rollback es automático al salir del bloque 'using transaction' ante una excepción,
                    // pero lo hacemos explícito para mayor claridad.
                    await transaction.RollbackAsync();
                    LimpiarArchivosSolicitudIncompleta(solicitud.SolId);
                    UltimoErrorCrearSolicitud = ObtenerMensajeRaiz(ex);
                    return false;
                }
            });
        }

        public async Task<string> GuardarArchivoTemporalAsync(IBrowserFile file, string tipo, long maxBytes)
        {
            string tempPath = Path.Combine(_env.WebRootPath, "uploads", "temp");
            if (!Directory.Exists(tempPath)) Directory.CreateDirectory(tempPath);

            string ext = Path.GetExtension(file.Name);
            string tempFileName = $"{tipo}_{Guid.NewGuid()}{ext}";
            string fullPath = Path.Combine(tempPath, tempFileName);

            await using (var stream = file.OpenReadStream(maxAllowedSize: maxBytes))
            await using (var fs = new FileStream(fullPath, FileMode.Create))
            {
                await stream.CopyToAsync(fs);
            }

            return tempFileName;
        }

        public bool EliminarArchivoTemporal(string tempFileName)
        {
            try
            {
                string fullPath = Path.Combine(_env.WebRootPath, "uploads", "temp", tempFileName);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al eliminar archivo temporal {tempFileName}: {ex.Message}");
            }
            return false;
        }

        private static string ObtenerMensajeRaiz(Exception ex)
        {
            var actual = ex;
            while (actual.InnerException is not null)
            {
                actual = actual.InnerException;
            }

            return actual.Message;
        }

        private bool TryValidarSolicitudCreacion(
            UsuSolicitudFirma solicitud,
            IEnumerable<(string TempFileName, string Tipo)> archivos,
            out List<(string TempFileName, string Tipo)> archivosNormalizados,
            out string mensajeError)
        {
            archivosNormalizados = new List<(string TempFileName, string Tipo)>();

            if (solicitud is null)
            {
                mensajeError = "No se recibio la informacion de la solicitud.";
                return false;
            }

            var resultados = new List<ValidationResult>();
            var esSolicitudValida = Validator.TryValidateObject(
                solicitud,
                new ValidationContext(solicitud),
                resultados,
                validateAllProperties: true);

            if (!esSolicitudValida)
            {
                mensajeError = resultados
                    .Select(r => r.ErrorMessage)
                    .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
                    ?? "La solicitud contiene datos invalidos.";
                return false;
            }

            var tiposRequeridos = ObtenerTiposDocumentoRequeridos(solicitud);
            var tiposRecibidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var archivo in archivos ?? Enumerable.Empty<(string TempFileName, string Tipo)>())
            {
                var tipoNormalizado = (archivo.Tipo ?? string.Empty).Trim().ToUpperInvariant();
                var tempFileName = Path.GetFileName((archivo.TempFileName ?? string.Empty).Trim());

                if (string.IsNullOrWhiteSpace(tipoNormalizado) || string.IsNullOrWhiteSpace(tempFileName))
                {
                    mensajeError = "Se detectaron archivos incompletos en la solicitud.";
                    return false;
                }

                var config = ObtenerDocumentoSolicitudConfig(tipoNormalizado);
                if (config is null)
                {
                    mensajeError = $"El tipo de archivo {tipoNormalizado} no esta permitido.";
                    return false;
                }

                if (!tiposRecibidos.Add(tipoNormalizado))
                {
                    mensajeError = $"Se envio mas de un archivo para {tipoNormalizado}.";
                    return false;
                }

                var extension = Path.GetExtension(tempFileName);
                if (!config.ExtensionesPermitidas.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    mensajeError = $"El archivo {tipoNormalizado} tiene una extension no permitida.";
                    return false;
                }

                var rutaTemporal = Path.Combine(_env.WebRootPath, "uploads", "temp", tempFileName);
                var infoTemporal = new FileInfo(rutaTemporal);
                if (!infoTemporal.Exists)
                {
                    mensajeError = $"No se encontro el archivo temporal para {tipoNormalizado}.";
                    return false;
                }

                if (infoTemporal.Length <= 0 || infoTemporal.Length > config.TamanoMaximoBytes)
                {
                    mensajeError = $"El archivo {tipoNormalizado} excede el tamano permitido o esta vacio.";
                    return false;
                }

                archivosNormalizados.Add((tempFileName, tipoNormalizado));
            }

            var faltantes = tiposRequeridos
                .Where(tipo => !tiposRecibidos.Contains(tipo))
                .ToList();

            if (faltantes.Count > 0)
            {
                mensajeError = $"Faltan documentos requeridos: {string.Join(", ", faltantes)}.";
                return false;
            }

            mensajeError = string.Empty;
            return true;
        }

        private static List<string> ObtenerTiposDocumentoRequeridos(UsuSolicitudFirma solicitud)
        {
            var tipos = new List<string>(TiposDocumentoBase);

            if (solicitud.SolEsMayor65)
            {
                tipos.Add("VIDEO_ACEPTACION");
            }

            if (solicitud.SolTieneRuc)
            {
                tipos.Add("RUC_FILE");
            }

            if (string.Equals(solicitud.SolTipoPersona, "JURIDICA", StringComparison.OrdinalIgnoreCase))
            {
                tipos.AddRange(
                [
                    "NOMBRAMIENTO",
                    "CONSTITUCION",
                    "CEDULA_REPRESENTANTE",
                    "AUTORIZACION",
                    "ACEPTACION_NOMBRAMIENTO"
                ]);
            }

            return tipos;
        }

        private void LimpiarArchivosSolicitudIncompleta(int solicitudId)
        {
            if (solicitudId <= 0)
            {
                return;
            }

            try
            {
                var uploadPath = Path.Combine(_env.WebRootPath, "uploads", "solicitudes");
                if (!Directory.Exists(uploadPath))
                {
                    return;
                }

                foreach (var ruta in Directory.EnumerateFiles(uploadPath, $"{solicitudId}_*"))
                {
                    File.Delete(ruta);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"No se pudieron limpiar archivos de la solicitud {solicitudId}: {ex.Message}");
            }
        }

        public async Task<List<SolicitudPagoClienteDto>> ObtenerSolicitudesClienteAsync(int usuarioClienteId)
        {
            if (usuarioClienteId <= 0)
            {
                return new List<SolicitudPagoClienteDto>();
            }

            using var context = await _contextFactory.CreateDbContextAsync();
            var connection = context.Database.GetDbConnection();

            const string sql = """
                SELECT
                    s.SOL_ID AS SolId,
                    s.SOL_ID_USUARIO_CLIENTE AS SolIdUsuarioCliente,
                    s.SOL_ID_ESTADO_NUMERICA AS SolIdEstadoNumerica,
                    e.EST_NOMBRE AS EstadoSolicitud,
                    s.SOL_NOMBRES AS SolNombres,
                    s.SOL_PRIMER_APELLIDO AS SolPrimerApellido,
                    s.SOL_SEGUNDO_APELLIDO AS SolSegundoApellido,
                    s.SOL_CORREO_1 AS SolCorreo1,
                    s.SOL_TIPO_IDENTIFICACION AS SolTipoIdentificacion,
                    s.SOL_IDENTIFICACION AS SolIdentificacion,
                    s.SOL_FECHA_SOLICITUD AS SolFechaSolicitud,
                    s.SolFormatoFirma AS SolFormatoFirma,
                    s.SolTiempoVigencia AS SolVigencia,
                    s.SolMontoPago AS SolMontoPago,
                    s.SolPagoExitoso AS SolPagoExitoso,
                    s.SolIdTransaccionPago AS SolIdTransaccionPago,
                    s.SolFechaPago AS SolFechaPago,
                    (
                        SELECT COUNT(1)
                        FROM USU_SOLICITUD_OBSERVACION o
                        WHERE o.OBS_ID_SOLICITUD = s.SOL_ID
                          AND o.OBS_ACTIVO = 1
                          AND UPPER(o.OBS_ESTADO) = 'PENDIENTE'
                          AND UPPER(o.OBS_TIPO) IN ('CODIGO', 'CAMPO')
                    ) AS ObservacionesPendientes,
                    ultima.ObsDetalle AS UltimaNotificacion,
                    ultima.ObsFechaObservacion AS FechaUltimaNotificacion
                FROM USU_SOLICITUD_FIRMA s
                LEFT JOIN USU_ESTADO_FIRMA e ON e.EST_ID = s.SOL_ID_ESTADO_NUMERICA
                OUTER APPLY (
                    SELECT TOP 1
                        o.OBS_DETALLE AS ObsDetalle,
                        o.OBS_FECHA_OBSERVACION AS ObsFechaObservacion
                    FROM USU_SOLICITUD_OBSERVACION o
                    WHERE o.OBS_ID_SOLICITUD = s.SOL_ID
                      AND o.OBS_ACTIVO = 1
                      AND UPPER(o.OBS_ESTADO) = 'PENDIENTE'
                      AND UPPER(o.OBS_TIPO) IN ('CODIGO', 'CAMPO')
                    ORDER BY o.OBS_FECHA_OBSERVACION DESC
                ) ultima
                WHERE s.SOL_ACTIVO = 1
                  AND s.SOL_ID_USUARIO_CLIENTE = @UsuarioClienteId
                ORDER BY s.SOL_FECHA_SOLICITUD DESC;
                """;

            var solicitudes = await connection.QueryAsync<SolicitudPagoClienteDto>(
                sql,
                new { UsuarioClienteId = usuarioClienteId });

            return solicitudes.AsList();
        }

        public async Task<List<SolicitudFirmaClienteDto>> ObtenerFirmasClienteAsync(int usuarioClienteId)
        {
            if (usuarioClienteId <= 0)
            {
                return new List<SolicitudFirmaClienteDto>();
            }

            using var context = await _contextFactory.CreateDbContextAsync();
            var connection = context.Database.GetDbConnection();

            const string sql = """
                SELECT
                    s.SOL_ID AS SolId,
                    s.SOL_NOMBRES AS SolNombres,
                    s.SOL_PRIMER_APELLIDO AS SolPrimerApellido,
                    s.SOL_SEGUNDO_APELLIDO AS SolSegundoApellido,
                    s.SOL_TIPO_IDENTIFICACION AS SolTipoIdentificacion,
                    s.SOL_IDENTIFICACION AS SolIdentificacion,
                    s.SOL_FECHA_SOLICITUD AS SolFechaSolicitud,
                    s.SOL_FECHA_APROBACION AS SolFechaAprobacion,
                    s.SOL_FECHA_ACTUALIZACION AS SolFechaActualizacion,
                    s.SolFormatoFirma AS SolFormatoFirma,
                    s.SolTiempoVigencia AS SolVigencia,
                    s.SolPagoExitoso AS SolPagoExitoso,
                    s.SolFechaPago AS SolFechaPago,
                    s.SolIdTransaccionPago AS SolIdTransaccionPago,
                    e.EST_NOMBRE AS EstadoSolicitud,
                    CAST(CASE WHEN s.SolArchivoP12 IS NULL THEN 0 ELSE 1 END AS bit) AS TieneArchivoP12,
                    ISNULL(DATALENGTH(s.SolArchivoP12), 0) AS TamanoArchivoProtegido,
                    s.SolClaveP12 AS ClaveP12Protegida
                FROM USU_SOLICITUD_FIRMA s
                LEFT JOIN USU_ESTADO_FIRMA e ON e.EST_ID = s.SOL_ID_ESTADO_NUMERICA
                WHERE s.SOL_ACTIVO = 1
                  AND s.SOL_ID_USUARIO_CLIENTE = @UsuarioClienteId
                  AND s.SolArchivoP12 IS NOT NULL
                  AND s.SolClaveP12 IS NOT NULL
                ORDER BY
                    COALESCE(s.SOL_FECHA_APROBACION, s.SOL_FECHA_ACTUALIZACION, s.SOL_FECHA_SOLICITUD) DESC;
                """;

            var filas = await connection.QueryAsync<SolicitudFirmaClienteRow>(
                sql,
                new { UsuarioClienteId = usuarioClienteId });

            return filas
                .Select(fila => new SolicitudFirmaClienteDto
                {
                    SolId = fila.SolId,
                    SolNombres = fila.SolNombres,
                    SolPrimerApellido = fila.SolPrimerApellido,
                    SolSegundoApellido = fila.SolSegundoApellido,
                    SolTipoIdentificacion = fila.SolTipoIdentificacion,
                    SolIdentificacion = fila.SolIdentificacion,
                    SolFechaSolicitud = fila.SolFechaSolicitud,
                    SolFechaAprobacion = fila.SolFechaAprobacion,
                    SolFechaActualizacion = fila.SolFechaActualizacion,
                    SolFormatoFirma = fila.SolFormatoFirma,
                    SolVigencia = fila.SolVigencia,
                    SolPagoExitoso = fila.SolPagoExitoso,
                    SolFechaPago = fila.SolFechaPago,
                    SolIdTransaccionPago = fila.SolIdTransaccionPago,
                    EstadoSolicitud = fila.EstadoSolicitud,
                    TieneArchivoP12 = fila.TieneArchivoP12,
                    TamanoArchivoProtegido = fila.TamanoArchivoProtegido,
                    ClaveP12 = DesprotegerClaveP12(fila.ClaveP12Protegida)
                })
                .ToList();
        }

        public async Task<SolicitudArchivoP12Dto?> ObtenerArchivoFirmaP12ClienteAsync(int solId, int usuarioClienteId)
        {
            if (solId <= 0 || usuarioClienteId <= 0)
            {
                return null;
            }

            using var context = await _contextFactory.CreateDbContextAsync();
            var solicitud = await context.UsuSolicitudFirma
                .AsNoTracking()
                .Where(s => s.SolId == solId &&
                            s.SolIdUsuarioCliente == usuarioClienteId &&
                            s.SolActivo)
                .Select(s => new
                {
                    s.SolId,
                    s.SolIdentificacion,
                    s.SolArchivoP12
                })
                .FirstOrDefaultAsync();

            if (solicitud?.SolArchivoP12 is null || solicitud.SolArchivoP12.Length == 0)
            {
                return null;
            }

            var contenido = DesprotegerArchivoP12(solicitud.SolArchivoP12);
            if (contenido is null || contenido.Length == 0)
            {
                return null;
            }

            return new SolicitudArchivoP12Dto
            {
                Contenido = contenido,
                NombreArchivo = $"firma-{solicitud.SolIdentificacion}-{solicitud.SolId}.p12"
            };
        }

        public async Task<BesSolicitudOperacionResultadoDto> SincronizarSolicitudBesAsync(
            int solId,
            bool forzarCreacion = false,
            CancellationToken cancellationToken = default)
        {
            if (!_besPrecompraService.IsConfigured)
            {
                return new BesSolicitudOperacionResultadoDto
                {
                    Success = false,
                    Message = "La integracion BES no esta configurada en este ambiente."
                };
            }

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var solicitud = await context.UsuSolicitudFirma
                .Include(s => s.Documentos.Where(d => d.DocVigente))
                .FirstOrDefaultAsync(s => s.SolId == solId, cancellationToken);

            if (solicitud is null)
            {
                return new BesSolicitudOperacionResultadoDto
                {
                    Success = false,
                    Message = "No se encontro la solicitud local a sincronizar."
                };
            }

            if (solicitud.SolPagoExitoso != true)
            {
                return new BesSolicitudOperacionResultadoDto
                {
                    Success = false,
                    Message = "La solicitud aun no tiene un pago aprobado, por lo que no puede enviarse a BES."
                };
            }

            if (forzarCreacion || string.IsNullOrWhiteSpace(solicitud.SolUanatacaUuid))
            {
                return await CrearSolicitudBesAsync(context, solicitud, cancellationToken);
            }

            return await ActualizarEstadoBesAsync(context, solicitud, cancellationToken);
        }

        // --- MÉTODOS PARA SOPORTE (ADMIN) ---

        public async Task<List<SolicitudPagoClienteDto>> ObtenerSolicitudesAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var connection = context.Database.GetDbConnection();

            const string sql = """
                SELECT
                    s.SOL_ID AS SolId,
                    s.SOL_ID_USUARIO_CLIENTE AS SolIdUsuarioCliente,
                    s.SOL_ID_ESTADO_NUMERICA AS SolIdEstadoNumerica,
                    e.EST_NOMBRE AS EstadoSolicitud,
                    s.SOL_NOMBRES AS SolNombres,
                    s.SOL_PRIMER_APELLIDO AS SolPrimerApellido,
                    s.SOL_SEGUNDO_APELLIDO AS SolSegundoApellido,
                    s.SOL_CORREO_1 AS SolCorreo1,
                    s.SOL_TIPO_IDENTIFICACION AS SolTipoIdentificacion,
                    s.SOL_IDENTIFICACION AS SolIdentificacion,
                    s.SOL_FECHA_SOLICITUD AS SolFechaSolicitud,
                    s.SolFormatoFirma AS SolFormatoFirma,
                    s.SolTiempoVigencia AS SolVigencia,
                    s.SolMontoPago AS SolMontoPago,
                    s.SolPagoExitoso AS SolPagoExitoso,
                    s.SolIdTransaccionPago AS SolIdTransaccionPago,
                    s.SolFechaPago AS SolFechaPago,
                    (
                        SELECT COUNT(1)
                        FROM USU_SOLICITUD_OBSERVACION o
                        WHERE o.OBS_ID_SOLICITUD = s.SOL_ID
                          AND o.OBS_ACTIVO = 1
                          AND UPPER(o.OBS_ESTADO) = 'PENDIENTE'
                          AND UPPER(o.OBS_TIPO) IN ('CODIGO', 'CAMPO')
                    ) AS ObservacionesPendientes,
                    ultima.ObsDetalle AS UltimaNotificacion,
                    ultima.ObsFechaObservacion AS FechaUltimaNotificacion
                FROM USU_SOLICITUD_FIRMA s
                LEFT JOIN USU_ESTADO_FIRMA e ON e.EST_ID = s.SOL_ID_ESTADO_NUMERICA
                OUTER APPLY (
                    SELECT TOP 1
                        o.OBS_DETALLE AS ObsDetalle,
                        o.OBS_FECHA_OBSERVACION AS ObsFechaObservacion
                    FROM USU_SOLICITUD_OBSERVACION o
                    WHERE o.OBS_ID_SOLICITUD = s.SOL_ID
                      AND o.OBS_ACTIVO = 1
                      AND UPPER(o.OBS_ESTADO) = 'PENDIENTE'
                      AND UPPER(o.OBS_TIPO) IN ('CODIGO', 'CAMPO')
                    ORDER BY o.OBS_FECHA_OBSERVACION DESC
                ) ultima
                WHERE s.SOL_ACTIVO = 1
                ORDER BY s.SOL_FECHA_SOLICITUD DESC;
                """;

            var solicitudes = await connection.QueryAsync<SolicitudPagoClienteDto>(sql);
            return solicitudes.AsList();
        }

        public async Task<UsuSolicitudFirma?> ObtenerDetalleSolicitudAsync(int id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var solicitud = await context.UsuSolicitudFirma
                .Include(s => s.EstadoNumerica)
                .Include(s => s.Documentos.Where(d => d.DocVigente)) // Trae solo documentos actuales
                .FirstOrDefaultAsync(s => s.SolId == id);

            if (solicitud is null)
            {
                return null;
            }

            try
            {
                await context.Entry(solicitud)
                    .Collection(s => s.Observaciones)
                    .Query()
                    .Where(o => o.ObsActivo)
                    .LoadAsync();
            }
            catch (Exception ex)
            {
                // La gestion principal no debe caerse si la tabla de observaciones aun no esta lista.
                Console.WriteLine($"No se pudieron cargar observaciones de solicitud {id}: {ex.Message}");
            }

            return solicitud;
        }

        public async Task<List<UsuSolicitudDocumento>> ObtenerDocumentosSolicitudAsync(int id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.UsuSolicitudDocumento
                .Where(d => d.DocIdSolicitud == id && d.DocVigente)
                .OrderBy(d => d.DocTipo)
                .ToListAsync();
        }

        public async Task<bool> GuardarFirmaP12Async(
            int solId,
            IBrowserFile archivoP12,
            string claveP12,
            int usuarioSoporteId)
        {
            if (solId <= 0 || usuarioSoporteId <= 0 || archivoP12 is null)
            {
                return false;
            }

            var claveNormalizada = NormalizarClaveP12(claveP12);
            if (!Regex.IsMatch(claveNormalizada, "^[A-Z0-9]{12}$"))
            {
                return false;
            }

            if (!string.Equals(Path.GetExtension(archivoP12.Name), ".p12", StringComparison.OrdinalIgnoreCase) ||
                archivoP12.Size <= 0 ||
                archivoP12.Size > MaxArchivoP12Bytes)
            {
                return false;
            }

            byte[] contenidoArchivo;
            await using (var stream = archivoP12.OpenReadStream(MaxArchivoP12Bytes))
            using (var ms = new MemoryStream())
            {
                await stream.CopyToAsync(ms);
                contenidoArchivo = ms.ToArray();
            }

            if (contenidoArchivo.Length == 0)
            {
                return false;
            }

            await using var strategyContext = await _contextFactory.CreateDbContextAsync();
            var strategy = strategyContext.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                await using var transaction = await context.Database.BeginTransactionAsync();

                try
                {
                    var solicitud = await context.UsuSolicitudFirma.FindAsync(solId);
                    if (solicitud is null || !solicitud.SolActivo)
                    {
                        return false;
                    }

                    var ahora = DateTime.Now;
                    var estadoAnterior = solicitud.SolIdEstadoNumerica;
                    solicitud.SolArchivoP12 = _firmaProtector.Protect(contenidoArchivo);
                    solicitud.SolClaveP12 = _firmaProtector.Protect(claveNormalizada);
                    solicitud.SolIdEstadoNumerica = 3;
                    solicitud.SolFechaAprobacion = ahora;
                    solicitud.SolFechaActualizacion = ahora;
                    solicitud.SolIdUsuarioSoporte = usuarioSoporteId;

                    var entregasAnteriores = await context.UsuSolicitudObservacion
                        .Where(o => o.ObsIdSolicitud == solId &&
                                    o.ObsActivo &&
                                    o.ObsTipo.ToUpper() == TipoObservacionFirma)
                        .ToListAsync();

                    foreach (var entregaAnterior in entregasAnteriores)
                    {
                        entregaAnterior.ObsEstado = EstadoObservacionRevisada;
                        entregaAnterior.ObsActivo = false;
                    }

                    context.UsuSolicitudObservacion.Add(new UsuSolicitudObservacion
                    {
                        ObsIdSolicitud = solId,
                        ObsCampoObservedo = "SOL_ARCHIVO_P12",
                        ObsTipo = TipoObservacionFirma,
                        ObsDetalle = "Tu firma electronica esta lista. Descarga el archivo .p12 y revisa la clave de instalacion.",
                        ObsEstado = EstadoObservacionPendiente,
                        ObsTokenCorreccion = Guid.NewGuid().ToString("N"),
                        ObsFechaExpiracionToken = ObtenerFechaExpiracionFirma(ahora, solicitud.SolVigencia),
                        ObsFechaObservacion = ahora,
                        ObsIdUsuarioSoporte = usuarioSoporteId,
                        ObsRespuestaUsuario = string.Empty,
                        ObsActivo = true
                    });

                    context.UsuSolicitudEstadoHistorial.Add(new UsuSolicitudEstadoHistorial
                    {
                        HisIdSolicitud = solId,
                        HisIdEstadoAnterior = estadoAnterior,
                        HisIdEstadoNuevo = 3,
                        HisOrigenEstado = "NUMERICA",
                        HisComentario = "Soporte cargo el archivo .p12, la clave de descarga y aprobo la solicitud.",
                        HisIdUsuarioResponsable = usuarioSoporteId,
                        HisFechaCambio = ahora
                    });

                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    return false;
                }
            });
        }

        public async Task<List<SolicitudNotificacionDto>> ObtenerNotificacionesPendientesClienteAsync(int usuarioClienteId, int take = 8)
        {
            if (usuarioClienteId <= 0)
            {
                return new List<SolicitudNotificacionDto>();
            }

            using var context = await _contextFactory.CreateDbContextAsync();
            var connection = context.Database.GetDbConnection();

            const string sql = """
                SELECT TOP (@Take)
                    o.OBS_ID AS ObsId,
                    o.OBS_ID_SOLICITUD AS SolId,
                    o.OBS_TIPO AS ObsTipo,
                    o.OBS_CAMPO_OBSERVADO AS ObsCampoObservado,
                    o.OBS_DETALLE AS ObsDetalle,
                    o.OBS_RESPUESTA_USUARIO AS ObsRespuestaUsuario,
                    CAST('CLIENTE' AS varchar(20)) AS Destino,
                    o.OBS_FECHA_OBSERVACION AS ObsFechaObservacion,
                    s.SOL_IDENTIFICACION AS SolIdentificacion,
                    s.SOL_TIPO_IDENTIFICACION AS SolTipoIdentificacion,
                    s.SOL_NOMBRES AS SolNombres,
                    s.SOL_PRIMER_APELLIDO AS SolPrimerApellido,
                    s.SOL_SEGUNDO_APELLIDO AS SolSegundoApellido
                FROM USU_SOLICITUD_OBSERVACION o
                INNER JOIN USU_SOLICITUD_FIRMA s ON s.SOL_ID = o.OBS_ID_SOLICITUD
                WHERE s.SOL_ACTIVO = 1
                  AND s.SOL_ID_USUARIO_CLIENTE = @UsuarioClienteId
                  AND o.OBS_ACTIVO = 1
                  AND UPPER(o.OBS_ESTADO) = 'PENDIENTE'
                  AND UPPER(o.OBS_TIPO) IN ('CODIGO', 'CAMPO')
                ORDER BY o.OBS_FECHA_OBSERVACION DESC;
                """;

            var notificaciones = await connection.QueryAsync<SolicitudNotificacionDto>(
                sql,
                new { UsuarioClienteId = usuarioClienteId, Take = Math.Clamp(take, 1, 20) });

            return notificaciones.AsList();
        }

        public async Task<List<SolicitudNotificacionDto>> ObtenerEntregasFirmaPendientesClienteAsync(int usuarioClienteId, int take = 8)
        {
            if (usuarioClienteId <= 0)
            {
                return new List<SolicitudNotificacionDto>();
            }

            using var context = await _contextFactory.CreateDbContextAsync();
            var connection = context.Database.GetDbConnection();

            const string sql = """
                SELECT TOP (@Take)
                    o.OBS_ID AS ObsId,
                    o.OBS_ID_SOLICITUD AS SolId,
                    o.OBS_TIPO AS ObsTipo,
                    o.OBS_CAMPO_OBSERVADO AS ObsCampoObservado,
                    o.OBS_DETALLE AS ObsDetalle,
                    o.OBS_RESPUESTA_USUARIO AS ObsRespuestaUsuario,
                    CAST('FIRMA' AS varchar(20)) AS Destino,
                    o.OBS_FECHA_OBSERVACION AS ObsFechaObservacion,
                    s.SOL_IDENTIFICACION AS SolIdentificacion,
                    s.SOL_TIPO_IDENTIFICACION AS SolTipoIdentificacion,
                    s.SOL_NOMBRES AS SolNombres,
                    s.SOL_PRIMER_APELLIDO AS SolPrimerApellido,
                    s.SOL_SEGUNDO_APELLIDO AS SolSegundoApellido
                FROM USU_SOLICITUD_OBSERVACION o
                INNER JOIN USU_SOLICITUD_FIRMA s ON s.SOL_ID = o.OBS_ID_SOLICITUD
                WHERE s.SOL_ACTIVO = 1
                  AND s.SOL_ID_USUARIO_CLIENTE = @UsuarioClienteId
                  AND s.SolArchivoP12 IS NOT NULL
                  AND s.SolClaveP12 IS NOT NULL
                  AND o.OBS_ACTIVO = 1
                  AND UPPER(o.OBS_ESTADO) = 'PENDIENTE'
                  AND UPPER(o.OBS_TIPO) = 'FIRMA_P12'
                ORDER BY o.OBS_FECHA_OBSERVACION DESC;
                """;

            var notificaciones = await connection.QueryAsync<SolicitudNotificacionDto>(
                sql,
                new { UsuarioClienteId = usuarioClienteId, Take = Math.Clamp(take, 1, 20) });

            return notificaciones.AsList();
        }

        public async Task<List<SolicitudNotificacionDto>> ObtenerRespuestasPendientesSoporteAsync(int usuarioSoporteId, int take = 8)
        {
            if (usuarioSoporteId <= 0)
            {
                return new List<SolicitudNotificacionDto>();
            }

            using var context = await _contextFactory.CreateDbContextAsync();
            var connection = context.Database.GetDbConnection();

            const string sql = """
                SELECT TOP (@Take)
                    o.OBS_ID AS ObsId,
                    o.OBS_ID_SOLICITUD AS SolId,
                    o.OBS_TIPO AS ObsTipo,
                    o.OBS_CAMPO_OBSERVADO AS ObsCampoObservado,
                    o.OBS_DETALLE AS ObsDetalle,
                    o.OBS_RESPUESTA_USUARIO AS ObsRespuestaUsuario,
                    CAST('SOPORTE' AS varchar(20)) AS Destino,
                    o.OBS_FECHA_OBSERVACION AS ObsFechaObservacion,
                    s.SOL_IDENTIFICACION AS SolIdentificacion,
                    s.SOL_TIPO_IDENTIFICACION AS SolTipoIdentificacion,
                    s.SOL_NOMBRES AS SolNombres,
                    s.SOL_PRIMER_APELLIDO AS SolPrimerApellido,
                    s.SOL_SEGUNDO_APELLIDO AS SolSegundoApellido
                FROM USU_SOLICITUD_OBSERVACION o
                INNER JOIN USU_SOLICITUD_FIRMA s ON s.SOL_ID = o.OBS_ID_SOLICITUD
                WHERE s.SOL_ACTIVO = 1
                  AND o.OBS_ACTIVO = 1
                  AND o.OBS_ID_USUARIO_SOPORTE = @UsuarioSoporteId
                  AND UPPER(o.OBS_ESTADO) = 'RESPONDIDA'
                ORDER BY o.OBS_FECHA_OBSERVACION DESC;
                """;

            var notificaciones = await connection.QueryAsync<SolicitudNotificacionDto>(
                sql,
                new { UsuarioSoporteId = usuarioSoporteId, Take = Math.Clamp(take, 1, 20) });

            return notificaciones.AsList();
        }

        public async Task<bool> MarcarRespuestaSoporteVistaAsync(int observacionId, int usuarioSoporteId)
        {
            if (observacionId <= 0 || usuarioSoporteId <= 0)
            {
                return false;
            }

            using var context = await _contextFactory.CreateDbContextAsync();
            var observacion = await context.UsuSolicitudObservacion
                .FirstOrDefaultAsync(o => o.ObsId == observacionId &&
                                          o.ObsActivo &&
                                          o.ObsIdUsuarioSoporte == usuarioSoporteId &&
                                          o.ObsEstado.ToUpper() == EstadoObservacionRespondida);

            if (observacion is null)
            {
                return false;
            }

            observacion.ObsEstado = EstadoObservacionRevisada;
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> MarcarEntregaFirmaVistaAsync(int observacionId, int usuarioClienteId)
        {
            if (observacionId <= 0 || usuarioClienteId <= 0)
            {
                return false;
            }

            using var context = await _contextFactory.CreateDbContextAsync();
            var observacion = await context.UsuSolicitudObservacion
                .FirstOrDefaultAsync(o => o.ObsId == observacionId &&
                                          o.ObsActivo &&
                                          o.ObsTipo.ToUpper() == TipoObservacionFirma &&
                                          o.ObsEstado.ToUpper() == EstadoObservacionPendiente);

            if (observacion is null)
            {
                return false;
            }

            var perteneceAlCliente = await context.UsuSolicitudFirma.AnyAsync(s =>
                s.SolId == observacion.ObsIdSolicitud &&
                s.SolIdUsuarioCliente == usuarioClienteId &&
                s.SolActivo);

            if (!perteneceAlCliente)
            {
                return false;
            }

            observacion.ObsEstado = EstadoObservacionRevisada;
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> SolicitarCodigoAsync(int solId, string detalle, int usuarioSoporteId)
        {
            var mensaje = string.IsNullOrWhiteSpace(detalle)
                ? "Soporte solicita que ingreses un codigo alfanumerico de 6 caracteres."
                : detalle.Trim();

            var campo = new SolicitudCampoObservadoDto
            {
                Campo = "CODIGO_6_ALFANUMERICO",
                Etiqueta = "Codigo alfanumerico de 6 caracteres",
                Detalle = mensaje
            };

            return await CrearObservacionesAsync(solId, TipoObservacionCodigo, new[] { campo }, usuarioSoporteId);
        }

        public async Task<bool> SolicitarCorreccionCamposAsync(
            int solId,
            IEnumerable<SolicitudCampoObservadoDto> campos,
            string detalleGeneral,
            int usuarioSoporteId)
        {
            var camposValidos = campos
                .Where(campo => !string.IsNullOrWhiteSpace(campo.Campo) && !string.IsNullOrWhiteSpace(campo.Etiqueta))
                .Select(campo => new SolicitudCampoObservadoDto
                {
                    Campo = campo.Campo.Trim(),
                    Etiqueta = campo.Etiqueta.Trim(),
                    Detalle = string.IsNullOrWhiteSpace(campo.Detalle) ? detalleGeneral.Trim() : campo.Detalle.Trim()
                })
                .ToList();

            if (!camposValidos.Any())
            {
                return false;
            }

            return await CrearObservacionesAsync(solId, TipoObservacionCampo, camposValidos, usuarioSoporteId);
        }

        private async Task<bool> CrearObservacionesAsync(
            int solId,
            string tipo,
            IEnumerable<SolicitudCampoObservadoDto> campos,
            int usuarioSoporteId)
        {
            await using var strategyContext = await _contextFactory.CreateDbContextAsync();
            var strategy = strategyContext.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                await using var transaction = await context.Database.BeginTransactionAsync();

                try
                {
                    var solicitud = await context.UsuSolicitudFirma.FindAsync(solId);
                    if (solicitud is null || !solicitud.SolActivo)
                    {
                        return false;
                    }

                    var tieneSolicitudPendiente = await context.UsuSolicitudObservacion.AnyAsync(o =>
                        o.ObsIdSolicitud == solId &&
                        o.ObsActivo &&
                        o.ObsEstado.ToUpper() == EstadoObservacionPendiente &&
                        (o.ObsTipo.ToUpper() == TipoObservacionCodigo || o.ObsTipo.ToUpper() == TipoObservacionCampo));

                    if (tieneSolicitudPendiente)
                    {
                        return false;
                    }

                    var ahora = DateTime.Now;
                    var observaciones = campos.Select(campo => new UsuSolicitudObservacion
                    {
                        ObsIdSolicitud = solId,
                        ObsCampoObservedo = campo.Campo,
                        ObsTipo = tipo,
                        ObsDetalle = PrepararDetalleObservacion(campo, tipo),
                        ObsEstado = EstadoObservacionPendiente,
                        ObsTokenCorreccion = Guid.NewGuid().ToString("N"),
                        ObsFechaExpiracionToken = ahora.AddDays(7),
                        ObsFechaObservacion = ahora,
                        ObsIdUsuarioSoporte = usuarioSoporteId,
                        ObsRespuestaUsuario = string.Empty,
                        ObsActivo = true
                    }).ToList();

                    context.UsuSolicitudObservacion.AddRange(observaciones);

                    solicitud.SolFechaActualizacion = ahora;
                    solicitud.SolFechaRevision ??= ahora;
                    solicitud.SolIdUsuarioSoporte = usuarioSoporteId;

                    context.UsuSolicitudEstadoHistorial.Add(new UsuSolicitudEstadoHistorial
                    {
                        HisIdSolicitud = solId,
                        HisIdEstadoAnterior = solicitud.SolIdEstadoNumerica,
                        HisIdEstadoNuevo = solicitud.SolIdEstadoNumerica,
                        HisOrigenEstado = "NUMERICA",
                        HisComentario = tipo == TipoObservacionCodigo
                            ? "Soporte solicito un codigo al usuario."
                            : $"Soporte solicito correccion de {observaciones.Count} campo(s).",
                        HisIdUsuarioResponsable = usuarioSoporteId,
                        HisFechaCambio = ahora
                    });

                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    return false;
                }
            });
        }

        private static string PrepararDetalleObservacion(SolicitudCampoObservadoDto campo, string tipo)
        {
            var detalle = string.IsNullOrWhiteSpace(campo.Detalle)
                ? "Por favor revisa este dato y vuelve a ingresarlo correctamente."
                : campo.Detalle.Trim();

            return tipo == TipoObservacionCodigo
                ? $"{campo.Etiqueta}: {detalle}"
                : $"{campo.Etiqueta} debe corregirse. {detalle}";
        }

        public async Task<bool> ResponderObservacionAsync(int observacionId, int usuarioClienteId, string respuesta)
        {
            if (observacionId <= 0 || usuarioClienteId <= 0 || string.IsNullOrWhiteSpace(respuesta))
            {
                return false;
            }

            using var context = await _contextFactory.CreateDbContextAsync();

            var observacion = await context.UsuSolicitudObservacion
                .FirstOrDefaultAsync(o => o.ObsId == observacionId && o.ObsActivo);

            if (observacion is null ||
                !string.Equals(observacion.ObsEstado, EstadoObservacionPendiente, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var solicitud = await context.UsuSolicitudFirma
                .FirstOrDefaultAsync(s => s.SolId == observacion.ObsIdSolicitud &&
                                          s.SolIdUsuarioCliente == usuarioClienteId &&
                                          s.SolActivo);

            if (solicitud is null)
            {
                return false;
            }

            var respuestaNormalizada = string.Equals(observacion.ObsTipo, TipoObservacionCodigo, StringComparison.OrdinalIgnoreCase)
                ? NormalizarCodigoAlfanumerico6(respuesta)
                : respuesta.Trim();

            if (string.Equals(observacion.ObsTipo, TipoObservacionCodigo, StringComparison.OrdinalIgnoreCase) &&
                !Regex.IsMatch(respuestaNormalizada, "^[A-Z0-9]{6}$"))
            {
                return false;
            }

            if (string.Equals(observacion.ObsTipo, TipoObservacionCampo, StringComparison.OrdinalIgnoreCase) &&
                !AplicarCorreccionCampo(solicitud, observacion.ObsCampoObservedo, respuestaNormalizada))
            {
                return false;
            }

            var ahora = DateTime.Now;
            observacion.ObsRespuestaUsuario = respuestaNormalizada;
            observacion.ObsEstado = EstadoObservacionRespondida;

            solicitud.SolFechaActualizacion = ahora;

            context.UsuSolicitudEstadoHistorial.Add(new UsuSolicitudEstadoHistorial
            {
                HisIdSolicitud = solicitud.SolId,
                HisIdEstadoAnterior = solicitud.SolIdEstadoNumerica,
                HisIdEstadoNuevo = solicitud.SolIdEstadoNumerica,
                HisOrigenEstado = "NUMERICA",
                HisComentario = "El usuario respondio una solicitud de soporte.",
                HisIdUsuarioResponsable = usuarioClienteId,
                HisFechaCambio = ahora
            });

            await context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ResponderObservacionDocumentoAsync(
            int observacionId,
            int usuarioClienteId,
            IBrowserFile archivo)
        {
            if (observacionId <= 0 || usuarioClienteId <= 0 || archivo is null)
            {
                return false;
            }

            using var context = await _contextFactory.CreateDbContextAsync();

            var observacion = await context.UsuSolicitudObservacion
                .FirstOrDefaultAsync(o => o.ObsId == observacionId && o.ObsActivo);

            if (observacion is null ||
                !string.Equals(observacion.ObsEstado, EstadoObservacionPendiente, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(observacion.ObsTipo, TipoObservacionCampo, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var tipoDocumento = ObtenerTipoDocumentoDesdeCampo(observacion.ObsCampoObservedo);
            var configDocumento = ObtenerDocumentoCorreccionConfig(tipoDocumento);
            if (tipoDocumento is null || configDocumento is null)
            {
                return false;
            }

            var extension = Path.GetExtension(archivo.Name);
            if (archivo.Size <= 0 ||
                archivo.Size > configDocumento.TamanoMaximoBytes ||
                string.IsNullOrWhiteSpace(extension) ||
                !configDocumento.ExtensionesPermitidas.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            var solicitud = await context.UsuSolicitudFirma
                .FirstOrDefaultAsync(s => s.SolId == observacion.ObsIdSolicitud &&
                                          s.SolIdUsuarioCliente == usuarioClienteId &&
                                          s.SolActivo);

            if (solicitud is null)
            {
                return false;
            }

            var uploadPath = Path.Combine(_env.WebRootPath, "uploads", "solicitudes");
            Directory.CreateDirectory(uploadPath);

            var fileName = $"{solicitud.SolId}_{tipoDocumento}_{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(uploadPath, fileName);

            try
            {
                await using (var stream = archivo.OpenReadStream(configDocumento.TamanoMaximoBytes))
                await using (var fs = new FileStream(fullPath, FileMode.CreateNew))
                {
                    await stream.CopyToAsync(fs);
                }

                var documentosTipo = await context.UsuSolicitudDocumento
                    .Where(d => d.DocIdSolicitud == solicitud.SolId &&
                                d.DocTipo == tipoDocumento)
                    .ToListAsync();

                var documentosVigentes = documentosTipo
                    .Where(d => d.DocVigente)
                    .ToList();

                var nuevaVersion = documentosTipo
                    .Select(d => d.DocVersion)
                    .DefaultIfEmpty(0)
                    .Max() + 1;

                var ahora = DateTime.Now;
                foreach (var documento in documentosVigentes)
                {
                    documento.DocVigente = false;
                    documento.DocObservacion = $"Reemplazado por correccion solicitada por soporte el {ahora:dd/MM/yyyy HH:mm}.";
                }

                var nuevoDocumento = new UsuSolicitudDocumento
                {
                    DocIdSolicitud = solicitud.SolId,
                    DocTipo = tipoDocumento,
                    DocNombreArchivo = fileName,
                    DocRutaArchivo = "/uploads/solicitudes/" + fileName,
                    DocExtension = extension,
                    DocTamanoArchivo = archivo.Size,
                    DocFechaCarga = ahora,
                    DocVigente = true,
                    DocVersion = nuevaVersion,
                    DocObservacion = $"Actualizado por correccion solicitada por soporte el {ahora:dd/MM/yyyy HH:mm}."
                };
                context.UsuSolicitudDocumento.Add(nuevoDocumento);

                observacion.ObsRespuestaUsuario = $"Archivo actualizado: {archivo.Name}";
                observacion.ObsEstado = EstadoObservacionRespondida;
                solicitud.SolFechaActualizacion = ahora;

                context.UsuSolicitudEstadoHistorial.Add(new UsuSolicitudEstadoHistorial
                {
                    HisIdSolicitud = solicitud.SolId,
                    HisIdEstadoAnterior = solicitud.SolIdEstadoNumerica,
                    HisIdEstadoNuevo = solicitud.SolIdEstadoNumerica,
                    HisOrigenEstado = "NUMERICA",
                    HisComentario = $"El usuario reemplazo el documento {tipoDocumento} solicitado por soporte.",
                    HisIdUsuarioResponsable = usuarioClienteId,
                    HisFechaCambio = ahora
                });

                await context.SaveChangesAsync();
                observacion.ObsIdDocumento = nuevoDocumento.DocId;
                await context.SaveChangesAsync();
                return true;
            }
            catch
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }

                return false;
            }
        }

        private static bool AplicarCorreccionCampo(UsuSolicitudFirma solicitud, string? campo, string respuesta)
        {
            var valor = NormalizarEspacios(respuesta);
            if (string.IsNullOrWhiteSpace(valor))
            {
                return false;
            }

            switch ((campo ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "SOL_TIPO_PERSONA":
                    var tipoPersona = valor.ToUpperInvariant();
                    if (!EsValorPermitido(tipoPersona, "NATURAL", "JURIDICA"))
                    {
                        return false;
                    }

                    solicitud.SolTipoPersona = tipoPersona;
                    if (tipoPersona == "JURIDICA")
                    {
                        solicitud.SolTieneRuc = true;
                    }

                    return true;

                case "SOL_TIPO_IDENTIFICACION":
                    var tipoIdentificacion = valor.ToUpperInvariant();
                    if (!EsValorPermitido(tipoIdentificacion, "CEDULA", "PASAPORTE"))
                    {
                        return false;
                    }

                    solicitud.SolTipoIdentificacion = tipoIdentificacion;
                    if (tipoIdentificacion == "PASAPORTE")
                    {
                        solicitud.SolCodigoDactilar = null;
                    }

                    return true;

                case "SOL_IDENTIFICACION":
                    if (string.Equals(solicitud.SolTipoIdentificacion, "CEDULA", StringComparison.OrdinalIgnoreCase))
                    {
                        var cedula = SoloDigitos(valor);
                        if (cedula.Length != 10)
                        {
                            return false;
                        }

                        solicitud.SolIdentificacion = cedula;
                        return true;
                    }

                    var pasaporte = Regex.Replace(valor.ToUpperInvariant(), @"[^A-Z0-9-]", string.Empty);
                    if (pasaporte.Length is < 3 or > 20)
                    {
                        return false;
                    }

                    solicitud.SolIdentificacion = pasaporte;
                    return true;

                case "SOL_CODIGO_DACTILAR":
                    var codigoDactilar = Regex.Replace(valor.ToUpperInvariant(), @"[^A-Z0-9]", string.Empty);
                    if (codigoDactilar.Length != 10)
                    {
                        return false;
                    }

                    solicitud.SolCodigoDactilar = codigoDactilar;
                    return true;

                case "SOL_NOMBRES":
                    solicitud.SolNombres = valor;
                    return true;

                case "SOL_PRIMER_APELLIDO":
                    solicitud.SolPrimerApellido = valor;
                    return true;

                case "SOL_SEGUNDO_APELLIDO":
                    solicitud.SolSegundoApellido = valor;
                    return true;

                case "SOL_FECHA_NACIMIENTO":
                    if (!TryParseFecha(valor, out var fechaNacimiento))
                    {
                        return false;
                    }

                    solicitud.SolFechaNacimiento = fechaNacimiento.Date;
                    return true;

                case "SOL_SEXO":
                    var sexo = valor.ToUpperInvariant();
                    if (!EsValorPermitido(sexo, "M", "F"))
                    {
                        return false;
                    }

                    solicitud.SolSexo = sexo;
                    return true;

                case "SOL_NACIONALIDAD":
                    solicitud.SolNacionalidad = valor;
                    return true;

                case "SOL_TIENE_RUC":
                    if (!TryParseBooleano(valor, out var tieneRuc))
                    {
                        return false;
                    }

                    solicitud.SolTieneRuc = tieneRuc;
                    if (!tieneRuc)
                    {
                        solicitud.SolNroRuc = null;
                    }

                    return true;

                case "SOL_NRO_RUC":
                    var ruc = SoloDigitos(valor);
                    if (ruc.Length != 13)
                    {
                        return false;
                    }

                    solicitud.SolNroRuc = ruc;
                    solicitud.SolTieneRuc = true;
                    return true;

                case "SOL_TELEFONO_1":
                    var telefono1 = SoloDigitos(valor);
                    if (telefono1.Length is < 10 or > 15)
                    {
                        return false;
                    }

                    solicitud.SolTelefono1 = telefono1;
                    return true;

                case "SOL_TELEFONO_2":
                    var telefono2 = SoloDigitos(valor);
                    if (telefono2.Length is < 7 or > 15)
                    {
                        return false;
                    }

                    solicitud.SolTelefono2 = telefono2;
                    return true;

                case "SOL_CORREO_1":
                    if (!EsCorreoValido(valor))
                    {
                        return false;
                    }

                    solicitud.SolCorreo1 = valor.Trim().ToLowerInvariant();
                    return true;

                case "SOL_CORREO_2":
                    if (!EsCorreoValido(valor))
                    {
                        return false;
                    }

                    solicitud.SolCorreo2 = valor.Trim().ToLowerInvariant();
                    return true;

                case "SOL_PROVINCIA":
                    solicitud.SolProvincia = valor;
                    return true;

                case "SOL_CANTON":
                    solicitud.SolCanton = valor;
                    return true;

                case "SOL_DIRECCION":
                    solicitud.SolDireccion = valor;
                    return true;

                case "SOL_COMPANY_NAME":
                    solicitud.SolCompanyName = valor;
                    return true;

                case "SOL_DEPARTMENT":
                    solicitud.SolDepartment = valor;
                    return true;

                case "SOL_POSITION":
                    solicitud.SolPosition = valor;
                    return true;

                case "SOL_REASON":
                    solicitud.SolReason = valor;
                    return true;

                case "SOL_IDENTIFICATION_TYPE_MANAGER":
                    var tipoDocManager = valor.ToUpperInvariant();
                    if (!EsValorPermitido(tipoDocManager, "CEDULA", "PASAPORTE"))
                    {
                        return false;
                    }
                    solicitud.SolIdentificationTypeManager = tipoDocManager;
                    return true;

                case "SOL_IDENTIFICATION_MANAGER":
                    if (string.Equals(solicitud.SolIdentificationTypeManager, "CEDULA", StringComparison.OrdinalIgnoreCase))
                    {
                        var cedulaManager = SoloDigitos(valor);
                        if (cedulaManager.Length != 10)
                        {
                            return false;
                        }
                        solicitud.SolIdentificationManager = cedulaManager;
                        return true;
                    }
                    var pasaporteManager = Regex.Replace(valor.ToUpperInvariant(), @"[^A-Z0-9-]", string.Empty);
                    if (pasaporteManager.Length is < 3 or > 20)
                    {
                        return false;
                    }
                    solicitud.SolIdentificationManager = pasaporteManager;
                    return true;

                case "SOL_NAMES_MANAGER":
                    solicitud.SolNamesManager = valor;
                    return true;

                case "SOL_LAST_NAME_MANAGER":
                    solicitud.SolLastNameManager = valor;
                    return true;

                default:
                    return false;
            }
        }

        private static string NormalizarEspacios(string? valor)
            => Regex.Replace(valor ?? string.Empty, @"\s+", " ").Trim();

        private static string SoloDigitos(string? valor)
            => Regex.Replace(valor ?? string.Empty, @"\D", string.Empty);

        private static bool EsCorreoValido(string valor)
        {
            try
            {
                _ = new System.Net.Mail.MailAddress(valor);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string? ObtenerTipoDocumentoDesdeCampo(string? campo)
        {
            return (campo ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "DOC_CEDULA_FRONTAL" or "CEDULA_FRONTAL" => "CEDULA_FRONTAL",
                "DOC_CEDULA_POSTERIOR" or "CEDULA_POSTERIOR" => "CEDULA_POSTERIOR",
                "DOC_SELFIE_CEDULA" or "SELFIE_CEDULA" => "SELFIE_CEDULA",
                "DOC_VIDEO_ACEPTACION" or "VIDEO_ACEPTACION" => "VIDEO_ACEPTACION",
                _ => null
            };
        }

        private static DocumentoCorreccionConfig? ObtenerDocumentoSolicitudConfig(string? tipoDocumento)
        {
            return (tipoDocumento ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "CEDULA_FRONTAL" => new DocumentoCorreccionConfig([".jpg", ".jpeg", ".png", ".pdf"], MaxDocumentoBytes),
                "CEDULA_POSTERIOR" => new DocumentoCorreccionConfig([".jpg", ".jpeg", ".png", ".pdf"], MaxDocumentoBytes),
                "SELFIE_CEDULA" => new DocumentoCorreccionConfig([".jpg", ".jpeg", ".png"], MaxDocumentoBytes),
                "VIDEO_ACEPTACION" => new DocumentoCorreccionConfig([".mp4", ".mov", ".avi", ".webm"], MaxVideoAceptacionBytes),
                "RUC_FILE" => new DocumentoCorreccionConfig([".pdf"], MaxDocumentoBytes),
                "NOMBRAMIENTO" => new DocumentoCorreccionConfig([".pdf"], MaxDocumentoBytes),
                "CONSTITUCION" => new DocumentoCorreccionConfig([".pdf"], MaxDocumentoBytes),
                "CEDULA_REPRESENTANTE" => new DocumentoCorreccionConfig([".jpg", ".jpeg", ".png", ".pdf"], MaxDocumentoBytes),
                "AUTORIZACION" => new DocumentoCorreccionConfig([".pdf"], MaxDocumentoBytes),
                "ACEPTACION_NOMBRAMIENTO" => new DocumentoCorreccionConfig([".pdf"], MaxDocumentoBytes),
                "ARCHIVO_ADICIONAL" => new DocumentoCorreccionConfig([".jpg", ".jpeg", ".png", ".pdf"], MaxDocumentoBytes),
                _ => null
            };
        }

        private static DocumentoCorreccionConfig? ObtenerDocumentoCorreccionConfig(string? tipoDocumento)
        {
            return ObtenerDocumentoSolicitudConfig(tipoDocumento);
        }

        private static bool EsValorPermitido(string valor, params string[] permitidos) =>
            permitidos.Any(permitido => string.Equals(permitido, valor, StringComparison.OrdinalIgnoreCase));

        private static bool TryParseFecha(string valor, out DateTime fecha)
        {
            if (DateTime.TryParseExact(
                    valor,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out fecha))
            {
                return true;
            }

            return DateTime.TryParse(valor, CultureInfo.CurrentCulture, DateTimeStyles.None, out fecha) ||
                   DateTime.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha);
        }

        private static bool TryParseBooleano(string valor, out bool resultado)
        {
            resultado = false;
            var normalizado = valor.Trim().ToUpperInvariant();

            if (EsValorPermitido(normalizado, "SI", "S", "TRUE", "1", "YES"))
            {
                resultado = true;
                return true;
            }

            if (EsValorPermitido(normalizado, "NO", "N", "FALSE", "0"))
            {
                return true;
            }

            return false;
        }

        private static string NormalizarCodigoAlfanumerico6(string? valor)
        {
            var caracteres = (valor ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Take(6)
                .Select(char.ToUpperInvariant);

            return new string(caracteres.ToArray());
        }

        private static string NormalizarClaveP12(string? valor)
        {
            var caracteres = (valor ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Take(12)
                .Select(char.ToUpperInvariant);

            return new string(caracteres.ToArray());
        }

        private static DateTime ObtenerFechaExpiracionFirma(DateTime fechaBase, string? vigencia)
        {
            if (Regex.IsMatch(vigencia ?? string.Empty, @"\b30\b") &&
                Regex.IsMatch(vigencia ?? string.Empty, @"D[IÍ]AS?", RegexOptions.IgnoreCase))
            {
                return fechaBase.AddDays(30);
            }

            var match = Regex.Match(vigencia ?? string.Empty, @"\d+");
            if (!match.Success || !int.TryParse(match.Value, out var anios))
            {
                return fechaBase.AddYears(1);
            }

            return fechaBase.AddYears(Math.Clamp(anios, 1, 4));
        }

        private string? DesprotegerClaveP12(string? valorProtegido)
        {
            if (string.IsNullOrWhiteSpace(valorProtegido))
            {
                return null;
            }

            try
            {
                return _firmaProtector.Unprotect(valorProtegido);
            }
            catch (CryptographicException)
            {
                var claveLegada = NormalizarClaveP12(valorProtegido);
                return Regex.IsMatch(claveLegada, "^[A-Z0-9]{12}$") ? claveLegada : null;
            }
        }

        private byte[]? DesprotegerArchivoP12(byte[] valorProtegido)
        {
            try
            {
                return _firmaProtector.Unprotect(valorProtegido);
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        private async Task<BesSolicitudOperacionResultadoDto> CrearSolicitudBesAsync(
            AppDbContext context,
            UsuSolicitudFirma solicitud,
            CancellationToken cancellationToken)
        {
            var productUuid = await _besPrecompraService.ResolverProductoUuidAsync(solicitud, cancellationToken);
            var request = await ConstruirSolicitudBesAsync(solicitud, productUuid, cancellationToken);
            var createResult = await _besPrecompraService.CrearSolicitudAsync(request, cancellationToken);

            if (!createResult.Success)
            {
                solicitud.SolUanatacaComments = TruncateText(
                    string.Join(" ",
                        new[] { "Error al crear la solicitud BES.", createResult.ErrorMessage, createResult.ResponseBody }
                            .Where(item => !string.IsNullOrWhiteSpace(item))),
                    4000);
                solicitud.SolFechaActualizacion = DateTime.Now;
                await context.SaveChangesAsync(cancellationToken);

                return new BesSolicitudOperacionResultadoDto
                {
                    Success = false,
                    StatusCode = createResult.StatusCode,
                    Message = createResult.ErrorMessage ?? "BES rechazo la creacion de la solicitud.",
                    ErrorBody = createResult.ResponseBody
                };
            }

            solicitud.SolUanatacaUuid = createResult.Uuid;
            solicitud.SolUanatacaProductUuid = productUuid;
            solicitud.SolUanatacaOfferUuid ??= _configuration["BESPrecompra:DefaultOfferUuid"];
            solicitud.SolUanatacaStatus ??= "NEW";
            solicitud.SolUanatacaStatusText ??= "SOLICITUD REGISTRADA";
            solicitud.SolFechaActualizacion = DateTime.Now;
            await context.SaveChangesAsync(cancellationToken);

            var syncResult = await ActualizarEstadoBesAsync(context, solicitud, cancellationToken);
            syncResult.Created = true;
            syncResult.Location = createResult.Location;
            syncResult.Uuid ??= createResult.Uuid;
            return syncResult;
        }

        private async Task<BesSolicitudOperacionResultadoDto> ActualizarEstadoBesAsync(
            AppDbContext context,
            UsuSolicitudFirma solicitud,
            CancellationToken cancellationToken)
        {
            var remote = (await _besPrecompraService.BuscarSolicitudesAsync(
                uuid: solicitud.SolUanatacaUuid,
                cancellationToken: cancellationToken))
                .FirstOrDefault();

            if (remote is null)
            {
                return new BesSolicitudOperacionResultadoDto
                {
                    Success = false,
                    Message = "La solicitud ya fue enviada a BES, pero no se pudo recuperar su estado remoto.",
                    Uuid = solicitud.SolUanatacaUuid
                };
            }

            solicitud.SolUanatacaUuid = remote.Uuid ?? solicitud.SolUanatacaUuid;
            solicitud.SolUanatacaStatus = remote.Status;
            solicitud.SolUanatacaToken = remote.Token;
            solicitud.SolUanatacaStatusText = remote.UanatacaStatus;
            solicitud.SolUanatacaComments = TruncateText(remote.Comments, 4000);
            solicitud.SolUanatacaProductUuid = remote.ProductUuid ?? solicitud.SolUanatacaProductUuid;
            solicitud.SolUanatacaStakeholderUuid = remote.StakeholderUuid;
            solicitud.SolUanatacaCreatedBy = remote.CreatedBy;
            solicitud.SolUanatacaActive = remote.Active;
            solicitud.SolUanatacaCountable = remote.Countable;
            solicitud.SolUanatacaRenovation = remote.Renovation;
            solicitud.SolUanatacaOfferUuid = remote.OfferUuid ?? solicitud.SolUanatacaOfferUuid;
            solicitud.SolFechaActualizacion = DateTime.Now;

            if (string.Equals(remote.Status, "APPROVED", StringComparison.OrdinalIgnoreCase) &&
                solicitud.SolFechaAprobacion is null)
            {
                solicitud.SolFechaAprobacion = remote.ApprovedDate?.ToLocalTime() ?? DateTime.Now;
            }

            await context.SaveChangesAsync(cancellationToken);

            return new BesSolicitudOperacionResultadoDto
            {
                Success = true,
                StatusCode = 200,
                Message = "Solicitud BES sincronizada correctamente.",
                Uuid = solicitud.SolUanatacaUuid,
                ProviderStatus = solicitud.SolUanatacaStatus,
                ProviderStatusText = solicitud.SolUanatacaStatusText
            };
        }

        private async Task<BesCreateCertificateRequestDto> ConstruirSolicitudBesAsync(
            UsuSolicitudFirma solicitud,
            string productUuid,
            CancellationToken cancellationToken)
        {
            var documentos = solicitud.Documentos
                .Where(d => d.DocVigente)
                .ToDictionary(d => d.DocTipo.ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);

            return new BesCreateCertificateRequestDto
            {
                IdentificationType = MapIdentificationType(solicitud.SolTipoIdentificacion),
                Identification = solicitud.SolIdentificacion,
                FingerprintCode = string.Equals(solicitud.SolTipoIdentificacion, "CEDULA", StringComparison.OrdinalIgnoreCase)
                    ? solicitud.SolCodigoDactilar
                    : null,
                Names = solicitud.SolNombres,
                LastName1 = solicitud.SolPrimerApellido,
                LastName2 = solicitud.SolSegundoApellido,
                BirthDate = solicitud.SolFechaNacimiento.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                Nationality = solicitud.SolNacionalidad,
                Sex = MapSex(solicitud.SolSexo),
                PhoneNumber = solicitud.SolTelefono1,
                PhoneNumber2 = solicitud.SolTelefono2,
                Email = solicitud.SolCorreo1,
                Email2 = solicitud.SolCorreo2,
                Province = solicitud.SolProvincia,
                City = solicitud.SolCanton,
                Address = solicitud.SolDireccion,
                ProductUuid = productUuid,
                OfferUuid = solicitud.SolUanatacaOfferUuid ?? _configuration["BESPrecompra:DefaultOfferUuid"],
                Ruc = solicitud.SolTieneRuc ? solicitud.SolNroRuc : null,
                Company = solicitud.SolCompanyName,
                Department = solicitud.SolDepartment,
                Position = solicitud.SolPosition,
                Reason = solicitud.SolReason,
                IdentificationTypeManager = string.IsNullOrWhiteSpace(solicitud.SolIdentificationTypeManager)
                    ? null
                    : MapIdentificationType(solicitud.SolIdentificationTypeManager),
                IdentificationManager = solicitud.SolIdentificationManager,
                NamesManager = solicitud.SolNamesManager,
                LastNameManager = solicitud.SolLastNameManager,
                FrontIdentification = await BuildFilePayloadAsync(documentos, "CEDULA_FRONTAL", cancellationToken),
                BackIdentification = await BuildFilePayloadAsync(documentos, "CEDULA_POSTERIOR", cancellationToken),
                Selfie = await BuildFilePayloadAsync(documentos, "SELFIE_CEDULA", cancellationToken),
                SeniorVideo = await BuildOptionalFilePayloadAsync(documentos, "VIDEO_ACEPTACION", cancellationToken),
                RucFile = await BuildOptionalFilePayloadAsync(documentos, "RUC_FILE", cancellationToken),
                Constitution = await BuildOptionalFilePayloadAsync(documentos, "CONSTITUCION", cancellationToken),
                Appointment = await BuildOptionalFilePayloadAsync(documentos, "NOMBRAMIENTO", cancellationToken),
                AcceptanceAppointment = await BuildOptionalFilePayloadAsync(documentos, "ACEPTACION_NOMBRAMIENTO", cancellationToken),
                Authorization = await BuildOptionalFilePayloadAsync(documentos, "AUTORIZACION", cancellationToken),
                ManagerIdentification = await BuildOptionalFilePayloadAsync(documentos, "CEDULA_REPRESENTANTE", cancellationToken),
                AdditionalFile = await BuildOptionalFilePayloadAsync(documentos, "ARCHIVO_ADICIONAL", cancellationToken)
            };
        }

        private async Task<BesArchivoAdjuntoDto> BuildFilePayloadAsync(
            IReadOnlyDictionary<string, UsuSolicitudDocumento> documentos,
            string docTipo,
            CancellationToken cancellationToken)
        {
            if (!documentos.TryGetValue(docTipo, out var documento))
            {
                throw new InvalidOperationException($"Falta el documento requerido {docTipo} para enviar la solicitud a BES.");
            }

            return await BuildOptionalFilePayloadAsync(documento, cancellationToken)
                ?? throw new InvalidOperationException($"No se pudo leer el documento {docTipo} para BES.");
        }

        private async Task<BesArchivoAdjuntoDto?> BuildOptionalFilePayloadAsync(
            IReadOnlyDictionary<string, UsuSolicitudDocumento> documentos,
            string docTipo,
            CancellationToken cancellationToken)
        {
            return documentos.TryGetValue(docTipo, out var documento)
                ? await BuildOptionalFilePayloadAsync(documento, cancellationToken)
                : null;
        }

        private async Task<BesArchivoAdjuntoDto?> BuildOptionalFilePayloadAsync(
            UsuSolicitudDocumento documento,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(documento.DocNombreArchivo))
            {
                return null;
            }

            var uploadsPath = Path.Combine(_env.WebRootPath, "uploads", "solicitudes", documento.DocNombreArchivo);
            if (!File.Exists(uploadsPath))
            {
                throw new FileNotFoundException($"No se encontro el documento {documento.DocNombreArchivo} asociado a la solicitud.");
            }

            var bytes = await File.ReadAllBytesAsync(uploadsPath, cancellationToken);
            return new BesArchivoAdjuntoDto
            {
                Name = documento.DocNombreArchivo,
                Type = ObtenerMimeType(documento.DocExtension),
                Base64 = Convert.ToBase64String(bytes)
            };
        }

        private static string MapIdentificationType(string? tipo)
        {
            return string.Equals(tipo, "CEDULA", StringComparison.OrdinalIgnoreCase)
                ? "CÉDULA"
                : "PASAPORTE";
        }

        private static string MapSex(string? sex)
            => string.Equals(sex, "F", StringComparison.OrdinalIgnoreCase) ? "MUJER" : "HOMBRE";

        private static string ObtenerMimeType(string? extension)
        {
            return (extension ?? string.Empty).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".pdf" => "application/pdf",
                ".mp4" => "video/mp4",
                ".mov" => "video/quicktime",
                ".avi" => "video/x-msvideo",
                ".webm" => "video/webm",
                _ => "application/octet-stream"
            };
        }

        private static string? TruncateText(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return value.Length <= maxLength ? value : value[..maxLength];
        }

        public async Task<bool> ActualizarEstadoAsync(int solId, int nuevoEstadoId, string comentario, int usuarioSoporteId)
        {
            await using var strategyContext = await _contextFactory.CreateDbContextAsync();
            var strategy = strategyContext.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var context = await _contextFactory.CreateDbContextAsync();
                await using var transaction = await context.Database.BeginTransactionAsync();

                try
                {
                    var solicitud = await context.UsuSolicitudFirma.FindAsync(solId);
                    if (solicitud == null) return false;

                    int? anteriorEstado = solicitud.SolIdEstadoNumerica;

                    solicitud.SolIdEstadoNumerica = nuevoEstadoId;
                    solicitud.SolFechaActualizacion = DateTime.Now;
                    solicitud.SolIdUsuarioSoporte = usuarioSoporteId;

                    if (nuevoEstadoId == 3)
                        solicitud.SolFechaAprobacion = DateTime.Now;

                    context.UsuSolicitudEstadoHistorial.Add(new UsuSolicitudEstadoHistorial
                    {
                        HisIdSolicitud = solId,
                        HisIdEstadoAnterior = anteriorEstado,
                        HisIdEstadoNuevo = nuevoEstadoId,
                        HisOrigenEstado = "NUMERICA",
                        HisComentario = comentario,
                        HisIdUsuarioResponsable = usuarioSoporteId,
                        HisFechaCambio = DateTime.Now
                    });

                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    return false;
                }
            });
        }

        private sealed class SolicitudFirmaClienteRow
        {
            public int SolId { get; set; }
            public string SolNombres { get; set; } = string.Empty;
            public string SolPrimerApellido { get; set; } = string.Empty;
            public string? SolSegundoApellido { get; set; }
            public string SolTipoIdentificacion { get; set; } = string.Empty;
            public string SolIdentificacion { get; set; } = string.Empty;
            public DateTime SolFechaSolicitud { get; set; }
            public DateTime? SolFechaAprobacion { get; set; }
            public DateTime? SolFechaActualizacion { get; set; }
            public string SolFormatoFirma { get; set; } = string.Empty;
            public string SolVigencia { get; set; } = string.Empty;
            public bool SolPagoExitoso { get; set; }
            public DateTime? SolFechaPago { get; set; }
            public string? SolIdTransaccionPago { get; set; }
            public string? EstadoSolicitud { get; set; }
            public bool TieneArchivoP12 { get; set; }
            public int TamanoArchivoProtegido { get; set; }
            public string? ClaveP12Protegida { get; set; }
        }

        private sealed record DocumentoCorreccionConfig(string[] ExtensionesPermitidas, long TamanoMaximoBytes);
    }
}
