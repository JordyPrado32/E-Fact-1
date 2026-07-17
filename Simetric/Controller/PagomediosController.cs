using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;
using Simetric.Services;

namespace Simetric.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [Route("api/pagomedios")]
    public class PagomediosController : ControllerBase
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IEmailService _emailService;
        private readonly SolicitudService _solicitudService;
        private readonly CompraDocumentosFacturacionService _compraDocumentosFacturacionService;
        private readonly ILogger<PagomediosController> _logger;
        private static readonly JsonSerializerOptions HistorialJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        public PagomediosController(
            IDbContextFactory<AppDbContext> dbFactory,
            IEmailService emailService,
            SolicitudService solicitudService,
            CompraDocumentosFacturacionService compraDocumentosFacturacionService,
            ILogger<PagomediosController> logger)
        {
            _dbFactory = dbFactory;
            _emailService = emailService;
            _solicitudService = solicitudService;
            _compraDocumentosFacturacionService = compraDocumentosFacturacionService;
            _logger = logger;
        }

        [HttpGet("notificacion")]
        public async Task<IActionResult> NotificacionGet()
        {
            var data = Request.Query.ToDictionary(
                item => item.Key,
                item => item.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);

            return await ProcesarNotificacionAsync(data);
        }

        [HttpPost("notificacion")]
        public async Task<IActionResult> NotificacionPost()
        {
            var data = await LeerDatosAsync();
            return await ProcesarNotificacionAsync(data);
        }

        [HttpGet("notificacion-compra-documentos")]
        public async Task<IActionResult> NotificacionCompraDocumentosGet()
        {
            var data = Request.Query.ToDictionary(
                item => item.Key,
                item => item.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);

            return await ProcesarNotificacionCompraDocumentosAsync(data);
        }

        [HttpPost("notificacion-compra-documentos")]
        public async Task<IActionResult> NotificacionCompraDocumentosPost()
        {
            var data = await LeerDatosAsync();
            return await ProcesarNotificacionCompraDocumentosAsync(data);
        }

        private async Task<IActionResult> ProcesarNotificacionAsync(Dictionary<string, string> data)
        {
            var solIdValue = ObtenerValor(data, "customValue", "custom_value", "customvalue");
            var status = ObtenerValor(data, "status");
            var reference = ObtenerValor(data, "reference");
            var authorizationCode = ObtenerValor(data, "authorizationCode", "authorization_code");
            var cardBrand = ObtenerValor(data, "cardBrand", "card_brand");
            var cardNumber = ObtenerValor(data, "cardNumber", "card_number");

            if (!int.TryParse(solIdValue, out var solId) || solId <= 0)
            {
                _logger.LogWarning("Pagomedios retorno sin customValue valido. Datos: {Datos}", JsonSerializer.Serialize(data));
                return Redirect("/solicitud/pago/resultado?estado=sin-solicitud");
            }

            var pagoAprobado = EsPagoAprobado(status);

            await using var context = await _dbFactory.CreateDbContextAsync();
            var solicitud = await context.UsuSolicitudFirma.FirstOrDefaultAsync(item => item.SolId == solId);

            if (solicitud is null)
            {
                _logger.LogWarning("Pagomedios retorno una solicitud inexistente. SolId: {SolId}", solId);
                return Redirect($"/solicitud/pago/resultado?solId={solId}&estado=no-encontrada");
            }

            var pagoAprobadoRecien = pagoAprobado && solicitud.SolPagoExitoso != true;

            solicitud.SolPagoExitoso = pagoAprobado;
            solicitud.SolIdTransaccionPago = FirstNonEmpty(reference, authorizationCode, solicitud.SolIdTransaccionPago);

            if (pagoAprobado)
            {
                solicitud.SolFechaPago ??= DateTime.Now;
            }

            // Registrar venta automatica en BackOffice de forma atomica con la solicitud
            if (pagoAprobadoRecien)
            {
                var vendedorNombre = await ResolverNombreVendedorAsync(context, solicitud.SolIdUsuarioCliente);
                context.ReporteVentasBackOffice.Add(new ReporteVentaBackOffice
                {
                    Cliente = Truncate($"{solicitud.SolNombres} {solicitud.SolPrimerApellido} | {solicitud.SolCorreo1}", 150),
                    Producto = "e-sign",
                    PlanPaquete = Truncate(solicitud.SolVigencia switch
                    {
                        "1 AÑO" => "1 año",
                        "2 AÑOS" => "2 años",
                        "3 AÑOS" => "3 años",
                        "4 AÑOS" => "4 años",
                        _ => solicitud.SolVigencia ?? "1 año"
                    }, 100),
                    Valor = solicitud.SolMontoPago ?? 0m,
                    Fecha = solicitud.SolFechaPago ?? DateTime.Now,
                    Canal = "Web",
                    Vendedor = vendedorNombre,
                    Estado = "pagada",
                    FormaPago = Truncate(string.IsNullOrWhiteSpace(cardBrand) ? "Tarjeta de Crédito" : cardBrand, 50),
                    Observacion = Truncate($"Pago automático en línea (Pagomedios). Ref: {reference} / {authorizationCode}. Solicitud #{solicitud.SolId}", 500)
                });
            }

            await context.SaveChangesAsync();

            if (pagoAprobadoRecien)
            {
                try
                {
                    await _emailService.EnviarNotificacionPagoFirmaElectronicaAsync(
                        $"{solicitud.SolNombres} {solicitud.SolPrimerApellido} {solicitud.SolSegundoApellido}".Trim(),
                        solicitud.SolIdentificacion,
                        solicitud.SolCorreo1,
                        solicitud.SolFormatoFirma,
                        solicitud.SolVigencia,
                        solicitud.SolMontoPago ?? 0m,
                        reference,
                        authorizationCode,
                        solicitud.SolId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "El pago de firma electronica {SolId} fue aprobado pero no se pudo enviar la notificacion administrativa.",
                        solicitud.SolId);
                }

                try
                {
                    var besResult = await _solicitudService.SincronizarSolicitudBesAsync(solicitud.SolId);
                    if (!besResult.Success)
                    {
                        _logger.LogWarning(
                            "La solicitud {SolId} fue pagada pero no se pudo sincronizar con BES. Mensaje: {Mensaje}. Estado: {Estado}.",
                            solicitud.SolId,
                            besResult.Message,
                            besResult.ProviderStatus);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "El pago de firma electronica {SolId} fue aprobado pero fallo el envio automatico a BES.",
                        solicitud.SolId);
                }
            }

            _logger.LogInformation(
                "Pago Pagomedios recibido. SolId: {SolId}. Status: {Status}. Reference: {Reference}. Auth: {Authorization}. Brand: {Brand}. Card: {Card}",
                solId,
                status,
                reference,
                authorizationCode,
                cardBrand,
                cardNumber);

            var estado = pagoAprobado ? "aprobado" : "pendiente";
            return Redirect($"/solicitud/pago/resultado?solId={solId}&estado={estado}&reference={Uri.EscapeDataString(reference ?? string.Empty)}&authorization={Uri.EscapeDataString(authorizationCode ?? string.Empty)}");
        }

        private async Task<IActionResult> ProcesarNotificacionCompraDocumentosAsync(Dictionary<string, string> data)
        {
            var status = ObtenerValor(data, "status");
            var reference = ObtenerValor(data, "reference");
            var authorizationCode = ObtenerValor(data, "authorizationCode", "authorization_code");
            var customValue = ObtenerValor(data, "customValue", "custom_value", "customvalue");
            var correoDestino = ObtenerValor(data, "email", "correo", "third.email");
            var compra = ParseCompraCustomValue(customValue);
            var compraId = FirstNonEmpty(
                ObtenerValor(data, "purchase", "purchaseId", "purchase_id"),
                compra?.PurchaseId);
            var userId = compra?.UserId ??
                ParsePositiveInt(ObtenerValor(data, "uid", "userId", "user_id"));
            var pagoAprobado = false;
            var saldoAplicadoAhora = false;
            var saldoActual = 0;
            var usuarioAcreditadoId = 0;
            var historialActualizado = false;

            _logger.LogInformation(
                "Retorno Pagomedios compra-documentos. Status: {Status}. Reference: {Reference}. Auth: {Authorization}. Compra: {CompraId}. Custom: {CustomValue}",
                status,
                reference,
                authorizationCode,
                compraId,
                customValue);

            await AppendCompraDocumentosLogAsync(
                $"INICIO [{Request.Method}] status={status ?? "(null)"} reference={reference ?? "(null)"} auth={authorizationCode ?? "(null)"} purchase={compraId ?? "(null)"} custom={customValue ?? "(null)"} compra={SerializeForLog(compra)} data={SerializeForLog(data)}");

            if (userId > 0 && !string.IsNullOrWhiteSpace(compraId))
            {
                await using var context = await _dbFactory.CreateDbContextAsync();
                var usuario = await context.Usuarios.FirstOrDefaultAsync(item => item.IdUsuario == userId);

                if (usuario is null)
                {
                    _logger.LogWarning("No se encontro el usuario {UsuarioId} para acreditar la compra de documentos.", userId);
                }
                else
                {
                    if (usuario.estadoAsociado == true && usuario.idJefe is > 0)
                    {
                        var usuarioTitular = await context.Usuarios.FirstOrDefaultAsync(item => item.IdUsuario == usuario.idJefe.Value);
                        if (usuarioTitular is not null)
                        {
                            usuario = usuarioTitular;
                        }
                    }

                    usuarioAcreditadoId = usuario.IdUsuario;

                    var historial = LeerHistorial(usuario.HistorialComprasDocumentosJson);
                    var compraHistorial = BuscarCompraHistorial(historial, compraId, reference, authorizationCode, customValue);

                    if (compraHistorial is null)
                    {
                        _logger.LogWarning(
                            "Se ignoro una confirmacion sin compra pendiente valida. Usuario: {UsuarioId}. Compra: {CompraId}.",
                            usuario.IdUsuario,
                            compraId);
                        return ConstruirRedirectCompraDocumentos("pendiente", compraId, reference, authorizationCode, saldoActual, false);
                    }

                    if (!CoincideCompraId(compraHistorial, compraId) ||
                        !DatosCompraCoinciden(compraHistorial, compra))
                    {
                        _logger.LogWarning(
                            "Se ignoro una confirmacion cuyos datos no coinciden con la compra pendiente. Usuario: {UsuarioId}. Compra: {CompraId}.",
                            usuario.IdUsuario,
                            compraId);
                        return ConstruirRedirectCompraDocumentos("pendiente", compraId, reference, authorizationCode, saldoActual, false);
                    }

                    pagoAprobado = compraHistorial.SaldoAplicado ||
                        EsPagoAprobado(status) ||
                        EsRetornoGetDeCompraPendiente(compraHistorial, compraId, status);

                    compraHistorial.Reference = FirstNonEmpty(reference, compraHistorial.Reference);
                    compraHistorial.AuthorizationCode = FirstNonEmpty(authorizationCode, compraHistorial.AuthorizationCode);
                    compraHistorial.CustomValue = FirstNonEmpty(customValue, compraHistorial.CustomValue);
                    compraHistorial.EmailDestino = FirstNonEmpty(correoDestino, compraHistorial.EmailDestino, usuario.Email);
                    compraHistorial.Estado = NormalizarEstadoPago(status, pagoAprobado);

                    if (pagoAprobado && !compraHistorial.SaldoAplicado)
                    {
                        if (compraHistorial.EsIlimitado)
                        {
                            var ultimaVigencia = historial
                                .Where(item => item != compraHistorial &&
                                               item.SaldoAplicado &&
                                               item.EsIlimitado &&
                                               item.VigenciaHasta > DateTime.Now)
                                .Max(item => item.VigenciaHasta);

                            compraHistorial.VigenciaHasta = (ultimaVigencia ?? DateTime.Now).AddYears(1);
                        }
                        else
                        {
                            usuario.SaldoDocumentos = Math.Max(usuario.SaldoDocumentos, 0) + compraHistorial.Documentos;
                        }

                        usuario.FechaUltimaRecargaDocumentos = DateTime.Now;
                        compraHistorial.SaldoAplicado = true;
                        saldoAplicadoAhora = true;
                    }

                    compraHistorial.Fecha = compraHistorial.Fecha == default
                        ? DateTime.Now
                        : compraHistorial.Fecha;

                    usuario.HistorialComprasDocumentosJson = SerializarHistorial(historial);
                    saldoActual = usuario.SaldoDocumentos;
                    historialActualizado = true;

                    // Registrar venta automatica en BackOffice de forma atomica con el saldo
                    if (saldoAplicadoAhora)
                    {
                        var vendedorNombre = await ResolverNombreVendedorAsync(context, usuario.IdUsuario);
                        context.ReporteVentasBackOffice.Add(new ReporteVentaBackOffice
                        {
                            Cliente = Truncate($"{usuario.Nombres} {usuario.Apellidos} | {usuario.Email}", 150),
                            Producto = "e-fact",
                            PlanPaquete = Truncate(compraHistorial.EsIlimitado 
                                ? "Ilimitados durante 1 año" 
                                : $"{compraHistorial.Documentos} {(compraHistorial.Documentos == 1 ? "documento" : "documentos")}", 100),
                            Valor = compraHistorial.MontoTotal,
                            Fecha = compraHistorial.Fecha,
                            Canal = "Web",
                            Vendedor = vendedorNombre,
                            Estado = "pagada",
                            FormaPago = Truncate("Tarjeta de Crédito", 50),
                            Observacion = Truncate($"Compra de documentos automática en línea (Pagomedios). Ref: {reference} / {authorizationCode}. Compra #{compraId}", 500)
                        });

                        try
                        {
                            var resultadoFactura = await _compraDocumentosFacturacionService.EmitirFacturaAsync(
                                usuario.IdUsuario,
                                compraHistorial,
                                reference,
                                authorizationCode);

                            compraHistorial.CodFactura = resultadoFactura.CodFactura > 0 ? resultadoFactura.CodFactura : compraHistorial.CodFactura;
                            compraHistorial.NumeroFactura = !string.IsNullOrWhiteSpace(resultadoFactura.NumeroFactura)
                                ? resultadoFactura.NumeroFactura
                                : compraHistorial.NumeroFactura;
                            compraHistorial.FechaFactura ??= resultadoFactura.CodFactura > 0 ? DateTime.Now : null;
                            compraHistorial.FacturaAutorizada = resultadoFactura.CodFactura > 0
                                ? resultadoFactura.Autorizada
                                : compraHistorial.FacturaAutorizada;
                            compraHistorial.EstadoFactura = !string.IsNullOrWhiteSpace(resultadoFactura.EstadoSri)
                                ? resultadoFactura.EstadoSri
                                : compraHistorial.EstadoFactura;
                            compraHistorial.MensajeFactura = resultadoFactura.Mensaje;
                        }
                        catch (Exception ex)
                        {
                            compraHistorial.MensajeFactura = "La compra fue aprobada, pero la facturación automática falló. Revisa logs.";
                            _logger.LogError(
                                ex,
                                "La compra de documentos {CompraId} del usuario {UsuarioId} fue aprobada pero falló la facturación automática.",
                                compraId,
                                usuario.IdUsuario);
                        }
                    }

                    await context.SaveChangesAsync();

                    if (saldoAplicadoAhora && !compraHistorial.EsIlimitado && !string.IsNullOrWhiteSpace(usuario.Email))
                    {
                        try
                        {
                            await _emailService.EnviarConfirmacionCompraDocumentosAsync(
                                usuario.Email,
                                usuario.NombreCompleto,
                                compraHistorial.Documentos,
                                usuario.SaldoDocumentos,
                                compraHistorial.MontoTotal,
                                reference,
                                authorizationCode);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "La compra fue aprobada pero no se pudo enviar el correo de confirmacion al usuario {UsuarioId}.",
                                usuario.IdUsuario);
                        }
                    }
                }
            }
            else
            {
                _logger.LogWarning("Pagomedios retorno compra-documentos sin un identificador de compra util. Datos: {Datos}", JsonSerializer.Serialize(data));
            }

            await AppendCompraDocumentosLogAsync(
                $"FIN [{Request.Method}] approved={pagoAprobado} usuario={usuarioAcreditadoId} historial={historialActualizado} saldoAplicado={saldoAplicadoAhora} saldoActual={saldoActual} status={status ?? "(null)"} reference={reference ?? "(null)"} auth={authorizationCode ?? "(null)"} purchase={compraId ?? "(null)"} custom={customValue ?? "(null)"} compra={SerializeForLog(compra)}");

            var estado = pagoAprobado ? "aprobado" : "pendiente";

            return ConstruirRedirectCompraDocumentos(
                estado,
                compraId,
                reference,
                authorizationCode,
                saldoActual,
                saldoAplicadoAhora);
        }

        [HttpGet("notificacion-registro-tarjeta")]
        public async Task<IActionResult> NotificacionRegistroTarjetaGet()
        {
            var data = Request.Query.ToDictionary(
                item => item.Key,
                item => item.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);

            try
            {
                var logPath = Path.Combine(Directory.GetCurrentDirectory(), "pagomedios_redirect_log.txt");
                var logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - GET Query: {Request.QueryString}\n";
                await System.IO.File.AppendAllTextAsync(logPath, logLine);
            }
            catch { }

            return await ProcesarNotificacionRegistroTarjetaAsync(data);
        }

        [HttpPost("notificacion-registro-tarjeta")]
        public async Task<IActionResult> NotificacionRegistroTarjetaPost()
        {
            var data = await LeerDatosAsync();
            try
            {
                var logPath = Path.Combine(Directory.GetCurrentDirectory(), "pagomedios_redirect_log.txt");
                var qs = string.Join("&", data.Select(x => $"{x.Key}={x.Value}"));
                var logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - POST Body: {qs}\n";
                await System.IO.File.AppendAllTextAsync(logPath, logLine);
            }
            catch { }
            return await ProcesarNotificacionRegistroTarjetaAsync(data);
        }

        private async Task<IActionResult> ProcesarNotificacionRegistroTarjetaAsync(Dictionary<string, string> data)
        {
            var customValue = ObtenerValor(data, "customValue", "custom_value", "customvalue");
            var status = ObtenerValor(data, "status");
            var cardToken = ObtenerValor(data, "cardToken", "cardtoken", "token");
            var cardNumber = ObtenerValor(data, "cardNumber", "card_number", "number");
            var cardBrand = ObtenerValor(data, "cardBrand", "card_brand", "type");
            var document = ObtenerValor(data, "document", "identificacion", "documento");

            _logger.LogInformation(
                "Notificacion registro tarjeta. Status: {Status}. Token: {Token}. Custom: {CustomValue}",
                status,
                cardToken,
                customValue);

            var (userId, plan, ciclo) = ParseSuscripcionCustomValue(customValue);

            if (userId <= 0)
            {
                _logger.LogWarning("Registro tarjeta retorno sin customValue valido. Datos: {Datos}", JsonSerializer.Serialize(data));
                return Redirect("/portal-servicios");
            }

            var registroAprobado = EsPagoAprobado(status) || 
                                   string.Equals(status, "1") || 
                                   string.Equals(status, "3") || 
                                   string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase);

            if (registroAprobado && !string.IsNullOrWhiteSpace(cardToken))
            {
                try
                {
                    await using var context = await _dbFactory.CreateDbContextAsync();
                    
                    if (string.IsNullOrWhiteSpace(document))
                    {
                        var userObj = await context.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == userId);
                        document = userObj?.Identificacion;
                    }

                    var existing = await context.EdeclareTarjetas
                        .AnyAsync(t => t.IdUsuario == userId && t.Token == cardToken);

                    if (!existing)
                    {
                        context.EdeclareTarjetas.Add(new EdeclareTarjeta
                        {
                            IdUsuario = userId,
                            Documento = document ?? string.Empty,
                            Token = cardToken,
                            MarcaTarjeta = cardBrand ?? "Desconocida",
                            NumeroMascara = cardNumber ?? "****",
                            FechaRegistro = DateTime.Now,
                            Estado = true
                        });
                        await context.SaveChangesAsync();
                        _logger.LogInformation("Tarjeta registrada exitosamente desde webhook/redirect para usuario {UserId}", userId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al guardar tarjeta tokenizada en notificacion-registro-tarjeta para usuario {UserId}", userId);
                }
            }

            if (string.Equals(Request.Method, "GET", StringComparison.OrdinalIgnoreCase) && !registroAprobado)
            {
                try
                {
                    await using var context = await _dbFactory.CreateDbContextAsync();
                    var tieneTarjeta = await context.EdeclareTarjetas.AnyAsync(t => t.IdUsuario == userId && t.Estado);
                    if (tieneTarjeta)
                    {
                        registroAprobado = true;
                    }
                }
                catch (Exception exVal)
                {
                    _logger.LogError(exVal, "Error al consultar tarjetas en fallback GET para usuario {UserId}", userId);
                }
            }

            string redirectUrl;
            if (string.Equals(plan, "gestion", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(plan, "configuracion", StringComparison.OrdinalIgnoreCase))
            {
                redirectUrl = $"/e-declara/configuracion/suscripcion?registro={(registroAprobado ? "exito" : "error")}";
            }
            else
            {
                redirectUrl = $"/suscripciones?servicio=e-declara&plan={plan}&ciclo={ciclo}&registro={(registroAprobado ? "exito" : "error")}";
            }
            return Redirect(redirectUrl);
        }

        [HttpGet("notificacion-registro-tarjeta-esign")]
        public async Task<IActionResult> NotificacionRegistroTarjetaEsignGet()
        {
            var data = new Dictionary<string, string>();
            foreach (var key in Request.Query.Keys)
            {
                data[key] = Request.Query[key].ToString();
            }
            try
            {
                var logPath = Path.Combine(Directory.GetCurrentDirectory(), "pagomedios_redirect_log.txt");
                var qs = string.Join("&", data.Select(x => $"{x.Key}={x.Value}"));
                var logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - ESIGN GET: {qs}\n";
                await System.IO.File.AppendAllTextAsync(logPath, logLine);
            }
            catch { }

            return await ProcesarNotificacionRegistroTarjetaEsignAsync(data);
        }

        [HttpPost("notificacion-registro-tarjeta-esign")]
        public async Task<IActionResult> NotificacionRegistroTarjetaEsignPost()
        {
            var data = await LeerDatosAsync();
            try
            {
                var logPath = Path.Combine(Directory.GetCurrentDirectory(), "pagomedios_redirect_log.txt");
                var qs = string.Join("&", data.Select(x => $"{x.Key}={x.Value}"));
                var logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - ESIGN POST Body: {qs}\n";
                await System.IO.File.AppendAllTextAsync(logPath, logLine);
            }
            catch { }
            return await ProcesarNotificacionRegistroTarjetaEsignAsync(data);
        }

        private async Task<IActionResult> ProcesarNotificacionRegistroTarjetaEsignAsync(Dictionary<string, string> data)
        {
            var customValue = ObtenerValor(data, "customValue", "custom_value", "customvalue");
            var status = ObtenerValor(data, "status");
            var cardToken = ObtenerValor(data, "cardToken", "cardtoken", "token");
            var cardNumber = ObtenerValor(data, "cardNumber", "card_number", "number");
            var cardBrand = ObtenerValor(data, "cardBrand", "card_brand", "type");
            var document = ObtenerValor(data, "document", "identificacion", "documento");

            _logger.LogInformation(
                "Notificacion registro tarjeta ESIGN. Status: {Status}. Token: {Token}. Custom: {CustomValue}",
                status,
                cardToken,
                customValue);

            var (userId, plan, ciclo) = ParseSuscripcionCustomValue(customValue);

            if (userId <= 0)
            {
                _logger.LogWarning("Registro tarjeta ESIGN retorno sin customValue valido. Datos: {Datos}", JsonSerializer.Serialize(data));
                return Redirect("/e-sign/mis-firmas");
            }

            var registroAprobado = EsPagoAprobado(status) || 
                                   string.Equals(status, "1") || 
                                   string.Equals(status, "3") || 
                                   string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase);

            if (registroAprobado && !string.IsNullOrWhiteSpace(cardToken))
            {
                try
                {
                    await using var context = await _dbFactory.CreateDbContextAsync();
                    
                    if (string.IsNullOrWhiteSpace(document))
                    {
                        var userObj = await context.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == userId);
                        document = userObj?.Identificacion;
                    }

                    var existing = await context.EsignTarjetas
                        .AnyAsync(t => t.IdUsuario == userId && t.Token == cardToken);

                    if (!existing)
                    {
                        context.EsignTarjetas.Add(new EsignTarjeta
                        {
                            IdUsuario = userId,
                            Documento = document ?? string.Empty,
                            Token = cardToken,
                            MarcaTarjeta = cardBrand ?? "Desconocida",
                            NumeroMascara = cardNumber ?? "****",
                            FechaRegistro = DateTime.Now,
                            Estado = true
                        });
                        await context.SaveChangesAsync();
                        _logger.LogInformation("Tarjeta ESIGN registrada exitosamente desde webhook/redirect para usuario {UserId}", userId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al guardar tarjeta tokenizada ESIGN en notificacion para usuario {UserId}", userId);
                }
            }

            if (string.Equals(Request.Method, "GET", StringComparison.OrdinalIgnoreCase) && !registroAprobado)
            {
                try
                {
                    await using var context = await _dbFactory.CreateDbContextAsync();
                    var tieneTarjeta = await context.EsignTarjetas.AnyAsync(t => t.IdUsuario == userId && t.Estado);
                    if (tieneTarjeta)
                    {
                        registroAprobado = true;
                    }
                }
                catch (Exception exVal)
                {
                    _logger.LogError(exVal, "Error al consultar tarjetas ESIGN en fallback GET para usuario {UserId}", userId);
                }
            }

            return Redirect($"/e-sign/mis-firmas?registro={(registroAprobado ? "exito" : "error")}");
        }

        private static (int userId, string plan, string ciclo) ParseSuscripcionCustomValue(string? customValue)
        {
            if (string.IsNullOrWhiteSpace(customValue))
                return (0, "", "");

            int userId = 0;
            string plan = "";
            string ciclo = "";

            var partes = customValue.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < partes.Length - 1; i++)
            {
                if (string.Equals(partes[i], "user", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(partes[i + 1], out userId);
                }
                else if (string.Equals(partes[i], "plan", StringComparison.OrdinalIgnoreCase))
                {
                    plan = partes[i + 1];
                }
                else if (string.Equals(partes[i], "ciclo", StringComparison.OrdinalIgnoreCase))
                {
                    ciclo = partes[i + 1];
                }
            }

            return (userId, plan, ciclo);
        }

        private async Task<Dictionary<string, string>> LeerDatosAsync()
        {
            var data = Request.Query.ToDictionary(
                item => item.Key,
                item => item.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                foreach (var item in form)
                {
                    data[item.Key] = item.Value.ToString();
                }

                return data;
            }

            if (data.Count > 0)
            {
                return data;
            }

            try
            {
                using var document = await JsonDocument.ParseAsync(Request.Body);
                AgregarValoresJson(data, null, document.RootElement);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "No se pudo leer el cuerpo JSON de la notificacion Pagomedios.");
            }

            return data;
        }

        private static string? ObtenerValor(Dictionary<string, string> data, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (data.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }

                var nestedMatch = data.FirstOrDefault(item =>
                    item.Key.EndsWith($".{key}", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(item.Value));

                if (!string.IsNullOrWhiteSpace(nestedMatch.Value))
                {
                    return nestedMatch.Value;
                }
            }

            return null;
        }

        private static void AgregarValoresJson(Dictionary<string, string> data, string? prefix, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        var propertyPath = string.IsNullOrWhiteSpace(prefix)
                            ? property.Name
                            : $"{prefix}.{property.Name}";
                        AgregarValoresJson(data, propertyPath, property.Value);
                    }
                     break;
                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        var itemPath = string.IsNullOrWhiteSpace(prefix)
                            ? $"[{index}]"
                            : $"{prefix}[{index}]";
                        AgregarValoresJson(data, itemPath, item);
                        index++;
                    }
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(prefix))
                    {
                        data[prefix] = element.ValueKind == JsonValueKind.String
                            ? element.GetString() ?? string.Empty
                            : element.ToString();
                    }
                    break;
            }
        }

        private static bool EsPagoAprobado(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            return status.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   status.Trim().Equals("3", StringComparison.OrdinalIgnoreCase) ||
                   status.Contains("aprob", StringComparison.OrdinalIgnoreCase) ||
                   status.Contains("autoriz", StringComparison.OrdinalIgnoreCase) ||
                   status.Contains("approved", StringComparison.OrdinalIgnoreCase) ||
                   status.Contains("authorized", StringComparison.OrdinalIgnoreCase) ||
                   status.Contains("paid", StringComparison.OrdinalIgnoreCase);
        }

        private static CompraDocumentosCustomData? ParseCompraCustomValue(string? customValue)
        {
            if (string.IsNullOrWhiteSpace(customValue))
            {
                return null;
            }

            var resultado = new CompraDocumentosCustomData();
            var partes = customValue.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var parte in partes)
            {
                var tokens = parte.Split(':', 2, StringSplitOptions.TrimEntries);
                if (tokens.Length != 2)
                {
                    continue;
                }

                switch (tokens[0].ToLowerInvariant())
                {
                    case "purchase":
                        resultado.PurchaseId = tokens[1];
                        break;
                    case "user":
                        int.TryParse(tokens[1], out var userId);
                        resultado.UserId = userId;
                        break;
                    case "docs":
                        int.TryParse(tokens[1], out var documentos);
                        resultado.Documentos = documentos;
                        break;
                    case "total":
                        decimal.TryParse(tokens[1], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var total);
                        resultado.Total = total;
                        break;
                    case "plan":
                        resultado.EsIlimitado = string.Equals(tokens[1], "ilimitado-anual", StringComparison.OrdinalIgnoreCase);
                        break;
                }
            }

            return resultado.UserId > 0 &&
                   !string.IsNullOrWhiteSpace(resultado.PurchaseId) &&
                   (resultado.EsIlimitado || resultado.Documentos > 0) &&
                   resultado.Total > 0m
                ? resultado
                : null;
        }

        private static List<CompraDocumentosHistorialItem> LeerHistorial(string? historialJson)
        {
            if (string.IsNullOrWhiteSpace(historialJson))
            {
                return new List<CompraDocumentosHistorialItem>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<CompraDocumentosHistorialItem>>(historialJson, HistorialJsonOptions)
                    ?? new List<CompraDocumentosHistorialItem>();
            }
            catch (JsonException)
            {
                return new List<CompraDocumentosHistorialItem>();
            }
        }

        private static string SerializarHistorial(List<CompraDocumentosHistorialItem> historial)
        {
            var historialNormalizado = historial
                .OrderByDescending(item => item.Fecha)
                .Take(40)
                .ToList();

            var planPermanente = historial
                .Where(item => item.SaldoAplicado && item.EsIlimitado && item.EsPermanente)
                .OrderByDescending(item => item.Fecha)
                .FirstOrDefault();

            if (planPermanente is not null && !historialNormalizado.Contains(planPermanente))
            {
                if (historialNormalizado.Count >= 40)
                {
                    historialNormalizado.RemoveAt(historialNormalizado.Count - 1);
                }

                historialNormalizado.Add(planPermanente);
            }

            return JsonSerializer.Serialize(historialNormalizado, HistorialJsonOptions);
        }

        private static CompraDocumentosHistorialItem? BuscarCompraHistorial(
            IEnumerable<CompraDocumentosHistorialItem> historial,
            string? compraId,
            string? reference,
            string? authorizationCode,
            string? customValue)
        {
            return historial.FirstOrDefault(item =>
                (!string.IsNullOrWhiteSpace(compraId) &&
                 string.Equals(item.Id, compraId, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(reference) &&
                 string.Equals(item.Reference, reference, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(authorizationCode) &&
                 string.Equals(item.AuthorizationCode, authorizationCode, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(customValue) &&
                 string.Equals(item.CustomValue, customValue, StringComparison.OrdinalIgnoreCase)));
        }

        private bool EsRetornoGetDeCompraPendiente(
            CompraDocumentosHistorialItem compra,
            string? compraId,
            string? status) =>
            HttpMethods.IsGet(Request.Method) &&
            !compra.SaldoAplicado &&
            string.IsNullOrWhiteSpace(status) &&
            CoincideCompraId(compra, compraId);

        private static bool CoincideCompraId(CompraDocumentosHistorialItem compra, string? compraId) =>
            !string.IsNullOrWhiteSpace(compraId) &&
            string.Equals(compra.Id, compraId, StringComparison.OrdinalIgnoreCase);

        private static bool DatosCompraCoinciden(
            CompraDocumentosHistorialItem compra,
            CompraDocumentosCustomData? retorno)
        {
            if (retorno is null)
            {
                return true;
            }

            return string.Equals(compra.Id, retorno.PurchaseId, StringComparison.OrdinalIgnoreCase) &&
                   compra.Documentos == retorno.Documentos &&
                   compra.EsIlimitado == retorno.EsIlimitado &&
                   decimal.Round(compra.MontoTotal, 2, MidpointRounding.AwayFromZero) ==
                   decimal.Round(retorno.Total, 2, MidpointRounding.AwayFromZero);
        }

        private RedirectResult ConstruirRedirectCompraDocumentos(
            string estado,
            string? compraId,
            string? reference,
            string? authorizationCode,
            int saldoActual,
            bool saldoAplicadoAhora) =>
            Redirect(
                $"/compra-documentos?estado={Uri.EscapeDataString(estado)}" +
                $"&purchase={Uri.EscapeDataString(compraId ?? string.Empty)}" +
                $"&reference={Uri.EscapeDataString(reference ?? string.Empty)}" +
                $"&authorization={Uri.EscapeDataString(authorizationCode ?? string.Empty)}" +
                $"&saldo={saldoActual}" +
                $"&procesado={saldoAplicadoAhora}");

        private static int ParsePositiveInt(string? value) =>
            int.TryParse(value, out var result) && result > 0 ? result : 0;

        private static string NormalizarEstadoPago(string? status, bool pagoAprobado)
        {
            if (pagoAprobado)
            {
                return "Aprobado";
            }

            if (string.IsNullOrWhiteSpace(status))
            {
                return "Pendiente";
            }

            if (status.Contains("rech", StringComparison.OrdinalIgnoreCase) ||
                status.Contains("decl", StringComparison.OrdinalIgnoreCase))
            {
                return "Rechazado";
            }

            if (status.Contains("rever", StringComparison.OrdinalIgnoreCase))
            {
                return "Reversado";
            }

            return "Pendiente";
        }

        private static string? FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        private static async Task<string> ResolverNombreVendedorAsync(AppDbContext context, int? idUsuario)
        {
            if (idUsuario is not > 0)
                return "Sistema";

            var usuario = await context.Usuarios
                .AsNoTracking()
                .Where(x => x.IdUsuario == idUsuario.Value)
                .Select(x => new { x.IdVendedor })
                .FirstOrDefaultAsync();

            if (usuario?.IdVendedor is not > 0)
                return "Sistema";

            var vendedor = await context.VendedoresBackOffice
                .AsNoTracking()
                .Where(x => x.IdVendedor == usuario.IdVendedor.Value)
                .Select(x => x.Nombre)
                .FirstOrDefaultAsync();

            return string.IsNullOrWhiteSpace(vendedor) ? "Sistema" : vendedor.Trim();
        }

        private static string SerializeForLog<T>(T value)
        {
            try
            {
                return JsonSerializer.Serialize(value);
            }
            catch
            {
                return value?.ToString() ?? "(null)";
            }
        }

        private async Task AppendCompraDocumentosLogAsync(string line)
        {
            try
            {
                var logPath = Path.Combine(Directory.GetCurrentDirectory(), "pagomedios_compra_documentos_log.txt");
                await System.IO.File.AppendAllTextAsync(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {line}{Environment.NewLine}", Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static string Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value : value[..maxLength];
        }

        private sealed class CompraDocumentosCustomData
        {
            public string PurchaseId { get; set; } = string.Empty;
            public int UserId { get; set; }
            public int Documentos { get; set; }
            public decimal Total { get; set; }
            public bool EsIlimitado { get; set; }
        }
    }
}
