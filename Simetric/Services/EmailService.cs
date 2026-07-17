using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Simetric.Components.Helpers;
using Simetric.Data;
using Simetric.Models;
using Simetric.Services;
using Simetric.ViewModels;
using System.Diagnostics;
using System.Net;
using System.Globalization;

public interface IEmailService
{
    Task EnviarClaveTemporal(string emailDestino, string claveTemporal);
    Task EnviarClaveTemporal(string emailDestino, string claveTemporal, int minutosExpira, string motivo);
    Task EnviarBienvenidaVendedorBackOfficeAsync(string emailDestino, string nombreVendedor, string loginUrl, string usuario, string? codigoAcceso = null, string? setupUrl = null, int? minutosExpira = null);
    Task EnviarCredencialesBackOfficeAsync(string emailDestino, string nombreUsuario, string perfil, string loginUrl, string usuario, string contrasenaTemporal);
    Task EnviarCuentaCreadaAsync(string emailDestino, string? nombreUsuario, string? claveTemporal = null);
    Task EnviarFacturaAsync(
        string numeroFactura,
        IEnumerable<string> destinatarios,
        string? nombreCliente,
        decimal? totalFactura,
        string rutaXmlAdjunto,
        string? rutaPdfAdjunto);
    Task EnviarNotaCreditoAsync(
        string numeroNotaCredito,
        string numeroDocumentoModificado,
        IEnumerable<string> destinatarios,
        string? nombreCliente,
        decimal? totalNotaCredito,
        string rutaXmlAdjunto,
        string? rutaPdfAdjunto);
    Task EnviarNotaDebitoAsync(
        string numeroNotaDebito,
        string numeroDocumentoModificado,
        IEnumerable<string> destinatarios,
        string? nombreCliente,
        decimal? totalNotaDebito,
        string rutaXmlAdjunto,
        string? rutaPdfAdjunto);
    Task EnviarGuiaRemisionAsync(
        string numeroGuiaRemision,
        string numeroDocumentoSustento,
        IEnumerable<string> destinatarios,
        string? nombreDestinatario,
        string rutaXmlAdjunto,
        string? rutaPdfAdjunto);
    Task EnviarLiquidacionCompraAsync(
        string numeroLiquidacion,
        IEnumerable<string> destinatarios,
        string? nombreProveedor,
        decimal? totalLiquidacion,
        string rutaXmlAdjunto,
        string? rutaPdfAdjunto);
    Task EnviarRetencionAsync(
        string numeroRetencion,
        string numeroDocumentoSustento,
        IEnumerable<string> destinatarios,
        string? nombreProveedor,
        decimal? totalRetenido,
        string rutaXmlAdjunto,
        string? rutaPdfAdjunto);
    Task EnviarEstadoCuentaAsync(
        IEnumerable<string> destinatarios,
        EstadoCuentaDetalleVM detalle,
        byte[] pdfAdjunto,
        string nombreArchivoPdf);
    Task EnviarConfirmacionCompraDocumentosAsync(
        string emailDestino,
        string? nombreCliente,
        int documentosComprados,
        int saldoActual,
        decimal montoTotal,
        string? reference,
        string? authorizationCode);
    Task EnviarNotificacionPagoFirmaElectronicaAsync(
        string? nombreSolicitante,
        string? identificacion,
        string? correoSolicitante,
        string? tipoFirma,
        string? vigencia,
        decimal montoTotal,
        string? reference,
        string? authorizationCode,
        int solicitudId);
}

public class EmailService : IEmailService
{
    private readonly AppDbContext _db;
    private readonly AuditService _auditService;
    private readonly ILogger<EmailService> _logger;
    private readonly string _host;
    private readonly int _puerto;
    private readonly string _usuario;
    private readonly string _pass;
    private readonly string _nombreRemitente;
    private readonly int _timeoutMs;
    private readonly SecureSocketOptions _seguridadPreferida;
    private readonly List<string> _notificacionPagosDestinatarios;
    private readonly string _urlAccesoPlataforma;

    public EmailService(
        AppDbContext db,
        AuditService auditService,
        IConfiguration configuration,
        ILogger<EmailService> logger)
    {
        _db = db;
        _auditService = auditService;
        _logger = logger;

        _host = GetConfigValue(
            configuration,
            "EmailComprobantes:Smtp:Host",
            "EmailComprobantes:Host",
            "Smtp:Host") ?? "mail.numericasoftware.com";
        _puerto = GetConfigInt(
            configuration,
            "EmailComprobantes:Smtp:Puerto",
            "EmailComprobantes:Puerto",
            "Smtp:Puerto") ?? 8889;
        _usuario = "soporte@numericasoftware.com";
        _pass = GetConfigValue(
            configuration,
            "EmailComprobantes:Smtp:Password",
            "EmailComprobantes:Password",
            "Smtp:Password") ?? "Soporte2026$";
        _nombreRemitente = GetConfigValue(
            configuration,
            "EmailComprobantes:Smtp:NombreRemitente",
            "EmailComprobantes:NombreRemitente",
            "Smtp:NombreRemitente") ?? "Sistema E-Fact";
        var appBaseUrl = configuration["AppBaseUrl"] ?? "https://efact.numericasoftware.com/";
        _urlAccesoPlataforma = new Uri(new Uri(appBaseUrl.TrimEnd('/') + "/"), "login").ToString();
        _timeoutMs = GetConfigInt(
            configuration,
            "EmailComprobantes:Smtp:TimeoutMs",
            "EmailComprobantes:TimeoutMs",
            "Smtp:TimeoutMs") ?? 15000;
        _seguridadPreferida = ParseSecureSocketOption(GetConfigValue(
            configuration,
            "EmailComprobantes:Smtp:Seguridad",
            "EmailComprobantes:Seguridad",
            "Smtp:Seguridad"));
        _notificacionPagosDestinatarios = NormalizarListaCorreos(
            (configuration.GetSection("EmailComprobantes:NotificacionPagosDestinatarios").Get<string[]>() ?? Array.Empty<string>())
                .Concat(ParseCorreosConfigurados(configuration["EmailComprobantes:NotificacionPagosCorreo"]))
                .Concat(ParseCorreosConfigurados(configuration["EmailComprobantes:ContabilidadCorreo"]))
                .Concat(ParseCorreosConfigurados(configuration["EmailComprobantes:ContabilidadDestinatarios"])));

        _logger.LogInformation(
            "SMTP configurado para {Host}:{Puerto} con seguridad {Seguridad} y remitente {Remitente}.",
            _host,
            _puerto,
            _seguridadPreferida,
            _usuario);
    }

    public Task EnviarClaveTemporal(string emailDestino, string claveTemporal)
        => EnviarClaveTemporal(
            emailDestino,
            claveTemporal,
            RecoveryCodeHelper.MinutosExpiracionPorDefecto,
            "Seguridad / Recuperacion");

    public async Task EnviarCuentaCreadaAsync(string emailDestino, string? nombreUsuario, string? claveTemporal = null)
    {
        emailDestino = NormalizarEmail(emailDestino);
        var nombreSeguro = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(nombreUsuario) ? "Usuario" : nombreUsuario.Trim());
        var claveSegura = WebUtility.HtmlEncode(claveTemporal?.Trim() ?? string.Empty);
        var urlAccesoSegura = WebUtility.HtmlEncode(_urlAccesoPlataforma);
        var bloqueClave = string.IsNullOrWhiteSpace(claveTemporal)
            ? string.Empty
            : $@"
                  <div style='background:linear-gradient(180deg,#f6fbff 0%,#ebf4fc 100%);border:1px solid #cfe0f0;border-radius:24px;padding:22px 20px;text-align:center;'>
                    <div style='font-size:11px;font-weight:800;letter-spacing:0.16em;text-transform:uppercase;color:#5b7d99;margin-bottom:10px;'>Clave temporal</div>
                    <div style='display:inline-block;background:#006bb5;color:#ffffff;border-radius:18px;padding:14px 24px;font-size:30px;font-weight:900;letter-spacing:0.08em;'>{claveSegura}</div>
                  </div>";

        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(_nombreRemitente, _usuario));
        mensaje.To.Add(MailboxAddress.Parse(emailDestino));
        mensaje.Subject = "Tu cuenta ha sido confirmada satisfactoriamente";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
<div style='margin:0;padding:0;background:#f4f7f6;'>
  <div style='display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;'>
    Tu cuenta en E-FACT fue creada correctamente.
  </div>

  <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0' style='width:100%;background:linear-gradient(180deg,#f4f7f6 0%,#eaf2fb 100%);margin:0;padding:24px 0;'>
    <tr>
      <td align='center' style='padding:0 14px;'>
        <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0' style='max-width:600px;width:100%;'>
          <tr>
            <td style='padding:0;'>
              <div style='background:#ffffff;border:1px solid #d8e5f1;border-radius:28px;overflow:hidden;box-shadow:0 24px 60px rgba(0,74,124,0.14);'>
                <div style='background:linear-gradient(135deg,#006bb5 0%,#004a7c 100%);padding:28px 28px 26px 28px;color:#ffffff;'>
                  <div style='display:inline-block;padding:8px 14px;border-radius:999px;background:rgba(255,255,255,0.14);border:1px solid rgba(255,255,255,0.18);font-size:11px;font-weight:700;letter-spacing:0.08em;text-transform:uppercase;color:#dcecff;'>
                    Nuevo acceso
                  </div>
                  <div style='font-family:Segoe UI,Arial,sans-serif;font-size:34px;line-height:1.05;font-weight:800;letter-spacing:0.02em;margin-top:16px;'>
                    E-FACT
                  </div>
                  <div style='font-family:Segoe UI,Arial,sans-serif;font-size:16px;line-height:1.45;font-weight:600;color:#e2effa;margin-top:8px;'>
                    Tu cuenta ha sido confirmada satisfactoriamente
                  </div>
                </div>

                <div style='padding:30px 28px 24px 28px;font-family:Segoe UI,Arial,sans-serif;color:#2c3e50;'>
                  <div style='font-size:24px;line-height:1.2;font-weight:800;color:#17324d;margin:0 0 10px 0;'>
                    Hola, {nombreSeguro}
                  </div>
                  <div style='font-size:15px;line-height:1.75;color:#47627c;margin:0 0 22px 0;'>
                    Tu cuenta en <strong>E-FACT</strong> fue creada y confirmada satisfactoriamente. Ya puedes acceder a la plataforma con tu correo registrado.
                  </div>

                  {bloqueClave}

                  <div style='text-align:center;margin-top:22px;'>
                    <a href='{urlAccesoSegura}' style='display:inline-block;background:#006bb5;color:#ffffff;text-decoration:none;border-radius:14px;padding:14px 24px;font-size:15px;font-weight:800;'>Acceder a la plataforma</a>
                  </div>

                  <div style='margin-top:24px;background:#f8fbfe;border:1px solid #dde9f3;border-radius:20px;padding:18px;'>
                    <div style='font-size:12px;line-height:1.4;font-weight:800;letter-spacing:0.1em;text-transform:uppercase;color:#6b88a3;margin:0 0 12px 0;'>
                      Importante
                    </div>
                    <div style='font-size:14px;line-height:1.7;color:#47627c;margin:0 0 12px 0;'>
                      Ingresa a la plataforma desde el enlace anterior usando tu correo registrado.
                    </div>
                    <div style='font-size:14px;line-height:1.7;color:#47627c;margin:0 0 12px 0;'>
                      <strong>Recuerda cambiar la contraseña en tu primer intento de ingreso.</strong>
                    </div>
                  </div>

                  <div style='margin-top:22px;padding-top:18px;border-top:1px solid #e2ebf3;font-size:13px;line-height:1.75;color:#6b7f92;'>
                    Por seguridad, cambia esta clave apenas ingreses y no la compartas con terceros.
                  </div>
                </div>

                <div style='padding:0 28px 24px 28px;'>
                  <div style='background:#eff6fc;border:1px solid #d8e7f4;border-radius:18px;padding:14px 16px;font-family:Segoe UI,Arial,sans-serif;font-size:12px;line-height:1.7;color:#5d7891;'>
                    Correo generado automaticamente por <span style='color:#004a7c;font-weight:800;'>Numerica E-FACT</span>.
                  </div>
                </div>
              </div>
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</div>"
        };

        mensaje.Body = bodyBuilder.ToMessageBody();
        await SendMessageAsync(mensaje);
    }

    public async Task EnviarClaveTemporal(string emailDestino, string claveTemporal, int minutosExpira, string motivo)
    {
        emailDestino = NormalizarEmail(emailDestino);
        minutosExpira = minutosExpira > 0 ? minutosExpira : RecoveryCodeHelper.MinutosExpiracionPorDefecto;
        motivo = string.IsNullOrWhiteSpace(motivo) ? "Seguridad / Recuperacion" : motivo.Trim();
        var codigoRecuperacion = PrepararCodigoRecuperacionParaCorreo(claveTemporal);

        // =========================================================
        // 1) Encontrar el usuario por email para sacar el IdUsuario
        //    (cambia u.Correo por el nombre real si es distinto)
        // =========================================================
        var usuario = await _db.Usuarios
            .FirstOrDefaultAsync(u => u.Email == emailDestino); // <-- si tu campo NO se llama Correo, cámbialo

        int? idUsuario = usuario?.IdUsuario;

        // =========================================================
        // 2) Capturar valores ANTES (previos) - NO metas la clave real
        // =========================================================
        var valoresPrevios = usuario == null ? null : new
        {
            usuario.IdUsuario,
            usuario.Email,
            usuario.Estado,
            // Ejemplos comunes (si existen en tu modelo):
            // usuario.IntentosFallidos,
            // usuario.Bloqueado,
            // usuario.BloqueadoHasta,
            // usuario.ClaveTemporalExpira,
            // TieneClaveTemporal = usuario.ClaveTemporalHash != null
        };

        if (usuario != null)
        {
            usuario.TokenRecuperacion = Simetric.Components.Helpers.SecurityHelper.HashPassword(codigoRecuperacion);
            usuario.FechaExpiracionToken = DateTime.Now.AddMinutes(minutosExpira);
            await _db.SaveChangesAsync();
        }

        // =========================================================
        // 4) Capturar valores DESPUÉS (nuevo)
        // =========================================================
        var usuarioDespues = usuario == null
            ? null
            : await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.IdUsuario == usuario.IdUsuario);

        var valorNuevo = usuarioDespues == null ? null : new
        {
            usuarioDespues.IdUsuario,
            usuarioDespues.Email,
            usuarioDespues.Estado,
            // Ejemplos si existen:
            // usuarioDespues.IntentosFallidos,
            // usuarioDespues.Bloqueado,
            // usuarioDespues.BloqueadoHasta,
            // usuarioDespues.ClaveTemporalExpira,
            // TieneClaveTemporal = usuarioDespues.ClaveTemporalHash != null
        };

        // =========================================================
        // 5) Armar correo
        // =========================================================
        var motivoSeguro = WebUtility.HtmlEncode(motivo);
        var codigoSeguro = WebUtility.HtmlEncode(codigoRecuperacion);

        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(_nombreRemitente, _usuario));
        mensaje.To.Add(MailboxAddress.Parse(emailDestino));
        mensaje.Subject = "Codigo de acceso para actualizar tu clave | E-FACT";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
<div style='margin:0;padding:0;background:#f4f7f6;'>
  <div style='display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;'>
    Tu codigo de acceso E-FACT vence en {minutosExpira} minutos.
  </div>

  <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0' style='width:100%;background:linear-gradient(180deg,#f4f7f6 0%,#eaf2fb 100%);margin:0;padding:24px 0;'>
    <tr>
      <td align='center' style='padding:0 14px;'>
        <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0' style='max-width:600px;width:100%;'>
          <tr>
            <td style='padding:0;'>
              <div style='background:#ffffff;border:1px solid #d8e5f1;border-radius:28px;overflow:hidden;box-shadow:0 24px 60px rgba(0,74,124,0.14);'>
                <div style='background:linear-gradient(135deg,#006bb5 0%,#004a7c 100%);padding:28px 28px 26px 28px;color:#ffffff;'>
                  <div style='display:inline-block;padding:8px 14px;border-radius:999px;background:rgba(255,255,255,0.14);border:1px solid rgba(255,255,255,0.18);font-size:11px;font-weight:700;letter-spacing:0.08em;text-transform:uppercase;color:#dcecff;'>
                    Seguridad activa
                  </div>
                  <div style='font-family:Segoe UI,Arial,sans-serif;font-size:34px;line-height:1.05;font-weight:800;letter-spacing:0.02em;margin-top:16px;'>
                    E-FACT
                  </div>
                  <div style='font-family:Segoe UI,Arial,sans-serif;font-size:16px;line-height:1.45;font-weight:600;color:#e2effa;margin-top:8px;'>
                    Codigo de acceso para actualizar tu clave
                  </div>
                  <div style='font-family:Segoe UI,Arial,sans-serif;font-size:13px;line-height:1.5;color:#cfe5f8;margin-top:10px;max-width:420px;'>
                    {motivoSeguro}
                  </div>
                </div>

                <div style='padding:30px 28px 24px 28px;font-family:Segoe UI,Arial,sans-serif;color:#2c3e50;'>
                  <div style='font-size:24px;line-height:1.2;font-weight:800;color:#17324d;margin:0 0 10px 0;'>
                    Hola,
                  </div>
                  <div style='font-size:15px;line-height:1.75;color:#47627c;margin:0 0 22px 0;'>
                    Generamos un codigo de acceso para que puedas continuar con el cambio de tu contrasña.
                    Este codigo vence en <span style='color:#006bb5;font-weight:800;'>{minutosExpira} minutos</span>.
                  </div>

                  <div style='background:linear-gradient(180deg,#f6fbff 0%,#ebf4fc 100%);border:1px solid #cfe0f0;border-radius:24px;padding:22px 20px;text-align:center;box-shadow:inset 0 1px 0 rgba(255,255,255,0.7);'>
                    <div style='font-size:11px;line-height:1.4;font-weight:800;letter-spacing:0.16em;text-transform:uppercase;color:#5b7d99;margin:0 0 10px 0;'>
                      Tu codigo de acceso
                    </div>
                    <div style='display:inline-block;background:linear-gradient(135deg,#006bb5 0%,#004a7c 100%);color:#ffffff;border-radius:18px;padding:14px 24px;font-size:30px;line-height:1;font-weight:900;letter-spacing:0.22em;box-shadow:0 14px 28px rgba(0,107,181,0.28);'>
                      {codigoSeguro}
                    </div>
                  </div>

                  <div style='margin-top:24px;background:#f8fbfe;border:1px solid #dde9f3;border-radius:20px;padding:18px 18px 6px 18px;'>
                    <div style='font-size:12px;line-height:1.4;font-weight:800;letter-spacing:0.1em;text-transform:uppercase;color:#6b88a3;margin:0 0 12px 0;'>
                      Como usarlo
                    </div>
                    <div style='font-size:14px;line-height:1.7;color:#47627c;margin:0 0 12px 0;'>
                      1. Ingresa al sistema con tu contraséña temporal.
                    </div>
                    <div style='font-size:14px;line-height:1.7;color:#47627c;margin:0 0 12px 0;'>
                      2. Escribe este codigo en la pantalla de cambio de clave.
                    </div>
                    <div style='font-size:14px;line-height:1.7;color:#47627c;margin:0 0 12px 0;'>
                      3. Define tu nueva contraseña y finaliza el proceso.
                    </div>
                  </div>

                  <div style='margin-top:22px;padding-top:18px;border-top:1px solid #e2ebf3;font-size:13px;line-height:1.75;color:#6b7f92;'>
                    No compartas este codigo con nadie. Si tu no solicitaste este cambio, ignora este correo o contacta soporte.
                  </div>
                </div>

                <div style='padding:0 28px 24px 28px;'>
                  <div style='background:#eff6fc;border:1px solid #d8e7f4;border-radius:18px;padding:14px 16px;font-family:Segoe UI,Arial,sans-serif;font-size:12px;line-height:1.7;color:#5d7891;'>
                    Correo generado automaticamente por <span style='color:#004a7c;font-weight:800;'>Numerica E-FACT</span>.
                  </div>
                </div>
              </div>
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</div>"
        };

        mensaje.Body = bodyBuilder.ToMessageBody();

        try
        {
            await SendMessageAsync(mensaje);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando clave temporal a {Email}.", EnmascararEmail(emailDestino));

            // Auditoría de error (sin romper más cosas)
            await _auditService.RegistrarAuditoriaAsync(
                idUsuario,
                "ERROR_ENVIO_CLAVE_TEMPORAL",
                valoresPrevios,
                valorNuevo,
                new { Email = EnmascararEmail(emailDestino), Motivo = motivo, Error = ex.Message }
            );

            throw;
        }

        // =========================================================
        // 7) Auditoría OK (NO debe bloquear el envío)
        // =========================================================
        await _auditService.RegistrarAuditoriaAsync(
     idUsuario,
     "RECUPERACION_SOLICITADA",
     valoresPreviosObj: new
     {
         Evento = "OlvidoContrasena",
         Email = EnmascararEmail(emailDestino),
         Fecha = DateTime.Now
     },
     valorNuevoObj: new
     {
         Evento = "ClaveTemporalEnviada",
         Email = EnmascararEmail(emailDestino),
         ExpiraEnMinutos = minutosExpira
     },
     detallesObj: new
     {
         Motivo = motivo,
         ClaveTemporal = "NO_GUARDADA_POR_SEGURIDAD"
     }
 );
    }

    public async Task EnviarBienvenidaVendedorBackOfficeAsync(string emailDestino, string nombreVendedor, string loginUrl, string usuario, string? codigoAcceso = null, string? setupUrl = null, int? minutosExpira = null)
    {
        emailDestino = NormalizarEmail(emailDestino);

        var nombreSeguro = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(nombreVendedor) ? "Vendedor" : nombreVendedor.Trim());
        var loginSeguro = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(loginUrl) ? "/login" : loginUrl.Trim());
        var usuarioSeguro = WebUtility.HtmlEncode((usuario ?? string.Empty).Trim());
        var setupSeguro = WebUtility.HtmlEncode(esUrlVacia(setupUrl) ? loginUrl : setupUrl!.Trim());
        var codigoSeguro = WebUtility.HtmlEncode((codigoAcceso ?? string.Empty).Trim());
        var minutosTexto = minutosExpira.GetValueOrDefault(RecoveryCodeHelper.MinutosExpiracionPorDefecto);
        var incluyeCodigo = !string.IsNullOrWhiteSpace(codigoAcceso);
        var esContrasenaDirecta = incluyeCodigo && string.IsNullOrWhiteSpace(setupUrl);
        var botonUrl = esContrasenaDirecta ? loginSeguro : setupSeguro;
        var botonTexto = esContrasenaDirecta ? "Abrir BackOffice" : "Crear contraseña";
        var bloqueAccesoHtml = incluyeCodigo
            ? $@"
      <div style='margin:18px 0;border:1px solid #e5e7eb;border-radius:14px;overflow:hidden;'>
        <div style='padding:12px 16px;background:#111827;color:#fff;font-weight:800;'>Pasos para ingresar</div>
        <ol style='margin:0;padding:16px 18px 16px 38px;color:#374151;line-height:1.65;font-size:14px;'>
          <li>Haz clic en el boton <strong>{botonTexto}</strong></li>
          <li>En la pantalla de login escribe este usuario/correo: <strong>{usuarioSeguro}</strong></li>
          <li>En el campo de contraseña escribe la contraseña temporal que aparece abajo: <strong>{codigoSeguro}</strong></li>
          <li>Presiona <strong>Iniciar sesion</strong></li>
        </ol>
      </div>
      <div style='background:#f9fafb;border:1px solid #e5e7eb;border-radius:14px;padding:18px;margin:18px 0;'>
        <p style='margin:0 0 8px;font-size:14px;'><strong>Usuario / correo:</strong> <a href='mailto:{usuarioSeguro}' style='color:#2563eb;'>{usuarioSeguro}</a></p>
        <p style='margin:0 0 8px;font-size:14px;'><strong>{(esContrasenaDirecta ? "Contraseña temporal" : "Codigo de acceso")}:</strong></p>
        <div style='display:inline-block;background:#111827;color:#fff;border-radius:12px;padding:12px 18px;font-size:22px;font-weight:900;letter-spacing:.16em;'>{codigoSeguro}</div>
        {(esContrasenaDirecta ? "" : $"<p style='margin:12px 0 0;font-size:12px;color:#6b7280;'>Este codigo vence en {minutosTexto} minutos.</p>")}
      </div>
      <a href='{botonUrl}' style='display:inline-block;background:#111827;color:#fff;text-decoration:none;border-radius:10px;padding:12px 18px;font-weight:800;font-size:14px;margin-top:18px;'>{botonTexto}</a>
      <p style='margin:18px 0 0;font-size:13px;color:#6b7280;'>{(esContrasenaDirecta ? "" : "Primero crea tu contraseña con el codigo de acceso. Luego podras iniciar sesion normalmente.")}</p>"
            : $@"
      <div style='background:#f9fafb;border:1px solid #e5e7eb;border-radius:14px;padding:18px;margin:18px 0;'>
        <p style='margin:0;font-size:14px;'><strong>Usuario:</strong> {usuarioSeguro}</p>
      </div>
      <p style='margin:18px 0 0;font-size:13px;color:#6b7280;'>Puedes cambiar la contraseña cuando quieras desde Mi perfil dentro del BackOffice.</p>";
        var bloqueAccesoTexto = incluyeCodigo
            ? esContrasenaDirecta
                ? $"\nPasos:\n1. Abre BackOffice: {loginUrl}\n2. Escribe este usuario/correo: {usuario}\n3. Escribe esta contraseña temporal: {codigoAcceso}\n4. Presiona Iniciar sesion\n5. Luego puedes cambiarla desde Mi perfil\n\nCredenciales:\nUsuario: {usuario}\nContraseña temporal: {codigoAcceso}\n"
                : $"\nUsuario: {usuario}\nCodigo de acceso: {codigoAcceso}\nCrear contraseña: {setupUrl}\nEste codigo vence en {minutosTexto} minutos.\n"
            : "\nPuedes cambiar la contraseña cuando quieras desde Mi perfil en BackOffice.\n";

        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(_nombreRemitente, _usuario));
        mensaje.To.Add(MailboxAddress.Parse(emailDestino));
        mensaje.Subject = "Tus credenciales de acceso BackOffice";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
<div style='margin:0;padding:0;background:#f4f6f8;font-family:Segoe UI,Arial,sans-serif;color:#111827;'>
  <div style='max-width:640px;margin:0 auto;padding:28px 16px;'>
    <div style='background:#111827;border-radius:18px 18px 0 0;padding:26px 30px;color:#fff;'>
      <div style='font-size:12px;text-transform:uppercase;letter-spacing:.12em;color:#9ca3af;font-weight:800;'>Numerica BackOffice</div>
      <h1 style='margin:8px 0 0;font-size:24px;line-height:1.25;'>Bienvenido, {nombreSeguro}</h1>
    </div>
    <div style='background:#fff;border:1px solid #e5e7eb;border-top:0;border-radius:0 0 18px 18px;padding:28px 30px;'>
      <p style='margin:0 0 18px;font-size:15px;color:#374151;'>Se creo tu usuario de vendedor. Sigue estos pasos uno por uno para entrar a BackOffice.</p>
      {bloqueAccesoHtml}
    </div>
  </div>
</div>",
            TextBody = $"Bienvenido, {nombreVendedor}\n\nLogin: {loginUrl}\nUsuario: {usuario}\n{bloqueAccesoTexto}"
        };

        mensaje.Body = bodyBuilder.ToMessageBody();
        await SendMessageAsync(mensaje);

        static bool esUrlVacia(string? url) => string.IsNullOrWhiteSpace(url);
    }

    public async Task EnviarCredencialesBackOfficeAsync(string emailDestino, string nombreUsuario, string perfil, string loginUrl, string usuario, string contrasenaTemporal)
    {
        emailDestino = NormalizarEmail(emailDestino);

        var nombreSeguro = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(nombreUsuario) ? "Usuario" : nombreUsuario.Trim());
        var perfilSeguro = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(perfil) ? "BackOffice" : perfil.Trim());
        var loginSeguro = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(loginUrl) ? "/login" : loginUrl.Trim());
        var usuarioSeguro = WebUtility.HtmlEncode((usuario ?? string.Empty).Trim());
        var claveSeguro = WebUtility.HtmlEncode((contrasenaTemporal ?? string.Empty).Trim());

        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(_nombreRemitente, _usuario));
        mensaje.To.Add(MailboxAddress.Parse(emailDestino));
        mensaje.Subject = $"Tus credenciales de acceso {perfilSeguro} | E-FACT";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
<div style='margin:0;padding:0;background:#f4f6f8;font-family:Segoe UI,Arial,sans-serif;color:#111827;'>
  <div style='max-width:640px;margin:0 auto;padding:28px 16px;'>
    <div style='background:#111827;border-radius:18px 18px 0 0;padding:26px 30px;color:#fff;'>
      <div style='font-size:12px;text-transform:uppercase;letter-spacing:.12em;color:#9ca3af;font-weight:800;'>Numerica BackOffice</div>
      <h1 style='margin:8px 0 0;font-size:24px;line-height:1.25;'>Bienvenido, {nombreSeguro}</h1>
    </div>
    <div style='background:#fff;border:1px solid #e5e7eb;border-top:0;border-radius:0 0 18px 18px;padding:28px 30px;'>
      <p style='margin:0 0 18px;font-size:15px;color:#374151;'>Se creo tu usuario de <strong>{perfilSeguro}</strong>. Sigue estos pasos uno por uno para entrar a BackOffice.</p>
      <div style='margin:18px 0;border:1px solid #e5e7eb;border-radius:14px;overflow:hidden;'>
        <div style='padding:12px 16px;background:#111827;color:#fff;font-weight:800;'>Pasos para ingresar</div>
        <ol style='margin:0;padding:16px 18px 16px 38px;color:#374151;line-height:1.65;font-size:14px;'>
          <li>Haz clic en el boton <strong>Abrir BackOffice</strong></li>
          <li>En la pantalla de login escribe este usuario/correo: <strong>{usuarioSeguro}</strong></li>
          <li>En el campo de contrasena escribe la contrasena temporal que aparece abajo: <strong>{claveSeguro}</strong></li>
          <li>Presiona <strong>Iniciar sesion</strong></li>
          <li>Cuando ya estes dentro, puedes cambiar la contrasena desde <strong>Mi perfil</strong></li>
        </ol>
      </div>
      <div style='background:#f9fafb;border:1px solid #e5e7eb;border-radius:14px;padding:18px;margin:18px 0;'>
        <p style='margin:0 0 8px;font-size:14px;'><strong>Perfil:</strong> {perfilSeguro}</p>
        <p style='margin:0 0 8px;font-size:14px;'><strong>Usuario / correo:</strong> <a href='mailto:{usuarioSeguro}' style='color:#2563eb;'>{usuarioSeguro}</a></p>
        <p style='margin:0 0 8px;font-size:14px;'><strong>Contrasena temporal:</strong></p>
        <div style='display:inline-block;background:#111827;color:#fff;border-radius:12px;padding:12px 18px;font-size:22px;font-weight:900;letter-spacing:.16em;'>{claveSeguro}</div>
      </div>
      <a href='{loginSeguro}' style='display:inline-block;background:#111827;color:#fff;text-decoration:none;border-radius:10px;padding:12px 18px;font-weight:800;font-size:14px;margin-top:4px;'>Abrir BackOffice</a>
      <p style='margin:18px 0 0;font-size:13px;color:#6b7280;'>No necesitas crear una contrasena antes de entrar. Usa la contrasena temporal de este correo y luego cambiala desde Mi perfil.</p>
    </div>
  </div>
</div>",
            TextBody = $"Bienvenido, {nombreUsuario}\n\nPasos:\n1. Abre BackOffice: {loginUrl}\n2. Escribe este usuario/correo: {usuario}\n3. Escribe esta contrasena temporal: {contrasenaTemporal}\n4. Presiona Iniciar sesion\n5. Luego puedes cambiarla desde Mi perfil\n\nCredenciales:\nPerfil: {perfil}\nUsuario: {usuario}\nContrasena temporal: {contrasenaTemporal}\n"
        };

        mensaje.Body = bodyBuilder.ToMessageBody();
        await SendMessageAsync(mensaje);
    }

    public async Task EnviarNotificacionAprobacion(string emailAsociado, string nombreJefe)
    {
        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(_nombreRemitente, _usuario));
        mensaje.To.Add(new MailboxAddress("", emailAsociado));
        mensaje.Subject = "¡Tu acceso ha sido aprobado!";

        mensaje.Body = new TextPart("html")
        {
            Text = $@"
        <div style='font-family: sans-serif; padding: 20px;'>
            <h2>Acceso Concedido</h2>
            <p>Hola, tu solicitud de acceso ha sido aprobada por <b>{nombreJefe}</b>.</p>
            <p>Ya puedes iniciar sesión con tu correo y contraseña.</p>
            <a href='https://tu-dominio.com/' style='background: #006bb5; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;'>Iniciar Sesión</a>
        </div>"
        };
        await SendMessageAsync(mensaje);
    }
    public async Task EnviarFacturaAsync(
        string numeroFactura,
        IEnumerable<string> destinatarios,
        string? nombreCliente,
        decimal? totalFactura,
        string rutaXmlAdjunto,
        string? rutaPdfAdjunto)
    {
        var destinatariosNormalizados = destinatarios
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Debug.WriteLine($"[SMTP-FACTURA] Numero: {numeroFactura}");
        Debug.WriteLine($"[SMTP-FACTURA] Destinatarios: {string.Join(", ", destinatariosNormalizados)}");
        Debug.WriteLine($"[SMTP-FACTURA] XML: {rutaXmlAdjunto}");
        Debug.WriteLine($"[SMTP-FACTURA] PDF: {rutaPdfAdjunto}");
        _logger.LogInformation(
            "SMTP factura {NumeroFactura} - destinatarios:{Destinatarios} - xml:{RutaXml} - pdf:{RutaPdf}",
            numeroFactura,
            string.Join(", ", destinatariosNormalizados),
            rutaXmlAdjunto,
            rutaPdfAdjunto ?? "(sin pdf)");

        if (!destinatariosNormalizados.Any())
            throw new InvalidOperationException("La factura no tiene destinatarios válidos para el envío.");

        if (string.IsNullOrWhiteSpace(rutaXmlAdjunto) || !File.Exists(rutaXmlAdjunto))
            throw new FileNotFoundException("No se encontró el XML de la factura para adjuntarlo al correo.", rutaXmlAdjunto);

        if (!string.IsNullOrWhiteSpace(rutaPdfAdjunto) && !File.Exists(rutaPdfAdjunto))
            throw new FileNotFoundException("No se encontró el PDF de la factura para adjuntarlo al correo.", rutaPdfAdjunto);

        var asunto = $"Factura electrónica {numeroFactura}";

        var clienteSeguro = string.IsNullOrWhiteSpace(nombreCliente)
            ? "cliente"
            : System.Net.WebUtility.HtmlEncode(nombreCliente.Trim());

        var totalTexto = (totalFactura ?? 0m).ToString("N2", new CultureInfo("es-EC"));
        var archivosAdjuntos = !string.IsNullOrWhiteSpace(rutaPdfAdjunto)
            ? "PDF y XML electrónicos"
            : "XML electrónico";

        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(_nombreRemitente, _usuario));

        foreach (var correo in destinatariosNormalizados)
            mensaje.To.Add(MailboxAddress.Parse(correo));

        mensaje.Subject = asunto;

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
<div style='font-family:Segoe UI,Arial,sans-serif;background:#f6f8fb;padding:24px'>
  <div style='max-width:620px;margin:auto;background:#ffffff;border:1px solid #e8eef5;border-radius:14px;overflow:hidden'>
    <div style='background:#0f172a;color:#fff;padding:18px 22px'>
      <div style='font-size:18px;font-weight:700'>E-FACT</div>
      <div style='opacity:.85;font-size:13px;margin-top:4px'>Entrega de comprobante electrónico</div>
    </div>
    <div style='padding:22px'>
      <p style='margin:0 0 10px 0;color:#111827'>Estimado cliente,</p>
      <p style='margin:0 0 16px 0;color:#374151;line-height:1.5'>
        Adjuntamos los archivos correspondientes a la factura electrónica <b>{numeroFactura}</b> emitida para <b>{clienteSeguro}</b>.
      </p>
      <div style='background:#f8fafc;border:1px solid #e5e7eb;border-radius:12px;padding:14px 16px;line-height:1.7;color:#1f2937'>
        <div><b>Factura:</b> {numeroFactura}</div>
        <div><b>Cliente:</b> {clienteSeguro}</div>
        <div><b>Total:</b> ${totalTexto}</div>
        <div><b>Archivos adjuntos:</b> {archivosAdjuntos}</div>
      </div>
      <p style='margin:16px 0 0 0;color:#374151;line-height:1.5'>
        Este correo fue generado automáticamente por el sistema de facturación electrónica. Si necesitas una nueva copia o una corrección en los destinatarios, por favor contacta al emisor.
      </p>
    </div>
  </div>
</div>"
        };

        bodyBuilder.Attachments.Add(rutaXmlAdjunto);
        if (!string.IsNullOrWhiteSpace(rutaPdfAdjunto))
            bodyBuilder.Attachments.Add(rutaPdfAdjunto);

        mensaje.Body = bodyBuilder.ToMessageBody();

        await SendMessageAsync(mensaje);
    }

    public async Task EnviarNotaCreditoAsync(
        string numeroNotaCredito,
        string numeroDocumentoModificado,
        IEnumerable<string> destinatarios,
        string? nombreCliente,
        decimal? totalNotaCredito,
        string rutaXmlAdjunto,
        string? rutaPdfAdjunto)
    {
        var destinatariosNormalizados = destinatarios
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!destinatariosNormalizados.Any())
            throw new InvalidOperationException("La nota de crédito no tiene destinatarios válidos para el envío.");

        if (string.IsNullOrWhiteSpace(rutaXmlAdjunto) || !File.Exists(rutaXmlAdjunto))
            throw new FileNotFoundException("No se encontró el XML de la nota de crédito para adjuntarlo al correo.", rutaXmlAdjunto);

        if (!string.IsNullOrWhiteSpace(rutaPdfAdjunto) && !File.Exists(rutaPdfAdjunto))
            throw new FileNotFoundException("No se encontró el PDF de la nota de crédito para adjuntarlo al correo.", rutaPdfAdjunto);

        var clienteSeguro = string.IsNullOrWhiteSpace(nombreCliente)
            ? "cliente"
            : System.Net.WebUtility.HtmlEncode(nombreCliente.Trim());

        var totalTexto = (totalNotaCredito ?? 0m).ToString("N2", new CultureInfo("es-EC"));
        var documentoModificadoSeguro = string.IsNullOrWhiteSpace(numeroDocumentoModificado)
            ? "documento relacionado"
            : System.Net.WebUtility.HtmlEncode(numeroDocumentoModificado.Trim());
        var archivosAdjuntos = !string.IsNullOrWhiteSpace(rutaPdfAdjunto)
            ? "PDF y XML electrónicos"
            : "XML electrónico";

        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(_nombreRemitente, _usuario));

        foreach (var correo in destinatariosNormalizados)
            mensaje.To.Add(MailboxAddress.Parse(correo));

        mensaje.Subject = $"Nota de crédito electrónica {numeroNotaCredito}";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
<div style='font-family:Segoe UI,Arial,sans-serif;background:#f6f8fb;padding:24px'>
  <div style='max-width:620px;margin:auto;background:#ffffff;border:1px solid #e8eef5;border-radius:14px;overflow:hidden'>
    <div style='background:#0f172a;color:#fff;padding:18px 22px'>
      <div style='font-size:18px;font-weight:700'>E-FACT</div>
      <div style='opacity:.85;font-size:13px;margin-top:4px'>Entrega de comprobante electrónico</div>
    </div>
    <div style='padding:22px'>
      <p style='margin:0 0 10px 0;color:#111827'>Estimado cliente,</p>
      <p style='margin:0 0 16px 0;color:#374151;line-height:1.5'>
        Adjuntamos los archivos correspondientes a la nota de crédito electrónica <b>{numeroNotaCredito}</b> emitida para <b>{clienteSeguro}</b>.
      </p>
      <div style='background:#f8fafc;border:1px solid #e5e7eb;border-radius:12px;padding:14px 16px;line-height:1.7;color:#1f2937'>
        <div><b>Nota de crédito:</b> {numeroNotaCredito}</div>
        <div><b>Documento relacionado:</b> {documentoModificadoSeguro}</div>
        <div><b>Cliente:</b> {clienteSeguro}</div>
        <div><b>Total:</b> ${totalTexto}</div>
        <div><b>Archivos adjuntos:</b> {archivosAdjuntos}</div>
      </div>
      <p style='margin:16px 0 0 0;color:#374151;line-height:1.5'>
        Este correo fue generado automáticamente por el sistema de facturación electrónica. Si necesitas una nueva copia o una corrección en los destinatarios, por favor contacta al emisor.
      </p>
    </div>
  </div>
</div>"
        };

        bodyBuilder.Attachments.Add(rutaXmlAdjunto);
        if (!string.IsNullOrWhiteSpace(rutaPdfAdjunto))
            bodyBuilder.Attachments.Add(rutaPdfAdjunto);

        mensaje.Body = bodyBuilder.ToMessageBody();

        await SendMessageAsync(mensaje);
    }

    public async Task EnviarNotaDebitoAsync(
        string numeroNotaDebito,
        string numeroDocumentoModificado,
        IEnumerable<string> destinatarios,
        string? nombreCliente,
        decimal? totalNotaDebito,
        string rutaXmlAdjunto,
        string? rutaPdfAdjunto)
    {
        var destinatariosNormalizados = destinatarios
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!destinatariosNormalizados.Any())
            throw new InvalidOperationException("La nota de débito no tiene destinatarios válidos para el envío.");

        if (string.IsNullOrWhiteSpace(rutaXmlAdjunto) || !File.Exists(rutaXmlAdjunto))
            throw new FileNotFoundException("No se encontró el XML de la nota de débito para adjuntarlo al correo.", rutaXmlAdjunto);

        if (!string.IsNullOrWhiteSpace(rutaPdfAdjunto) && !File.Exists(rutaPdfAdjunto))
            throw new FileNotFoundException("No se encontró el PDF de la nota de débito para adjuntarlo al correo.", rutaPdfAdjunto);

        var clienteSeguro = string.IsNullOrWhiteSpace(nombreCliente)
            ? "cliente"
            : System.Net.WebUtility.HtmlEncode(nombreCliente.Trim());

        var totalTexto = (totalNotaDebito ?? 0m).ToString("N2", new CultureInfo("es-EC"));
        var documentoModificadoSeguro = string.IsNullOrWhiteSpace(numeroDocumentoModificado)
            ? "documento relacionado"
            : System.Net.WebUtility.HtmlEncode(numeroDocumentoModificado.Trim());
        var archivosAdjuntos = !string.IsNullOrWhiteSpace(rutaPdfAdjunto)
            ? "PDF y XML electrónicos"
            : "XML electrónico";

        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(_nombreRemitente, _usuario));

        foreach (var correo in destinatariosNormalizados)
            mensaje.To.Add(MailboxAddress.Parse(correo));

        mensaje.Subject = $"Nota de débito electrónica {numeroNotaDebito}";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
<div style='font-family:Segoe UI,Arial,sans-serif;background:#f6f8fb;padding:24px'>
  <div style='max-width:620px;margin:auto;background:#ffffff;border:1px solid #e8eef5;border-radius:14px;overflow:hidden'>
    <div style='background:#0f172a;color:#fff;padding:18px 22px'>
      <div style='font-size:18px;font-weight:700'>E-FACT</div>
      <div style='opacity:.85;font-size:13px;margin-top:4px'>Entrega de comprobante electrónico</div>
    </div>
    <div style='padding:22px'>
      <p style='margin:0 0 10px 0;color:#111827'>Estimado cliente,</p>
      <p style='margin:0 0 16px 0;color:#374151;line-height:1.5'>
        Adjuntamos los archivos correspondientes a la nota de débito electrónica <b>{numeroNotaDebito}</b> emitida para <b>{clienteSeguro}</b>.
      </p>
      <div style='background:#f8fafc;border:1px solid #e5e7eb;border-radius:12px;padding:14px 16px;line-height:1.7;color:#1f2937'>
        <div><b>Nota de débito:</b> {numeroNotaDebito}</div>
        <div><b>Documento relacionado:</b> {documentoModificadoSeguro}</div>
        <div><b>Cliente:</b> {clienteSeguro}</div>
        <div><b>Total:</b> ${totalTexto}</div>
        <div><b>Archivos adjuntos:</b> {archivosAdjuntos}</div>
      </div>
      <p style='margin:16px 0 0 0;color:#374151;line-height:1.5'>
        Este correo fue generado automáticamente por el sistema de facturación electrónica. Si necesitas una nueva copia o una corrección en los destinatarios, por favor contacta al emisor.
      </p>
    </div>
  </div>
</div>"
        };

        bodyBuilder.Attachments.Add(rutaXmlAdjunto);
        if (!string.IsNullOrWhiteSpace(rutaPdfAdjunto))
            bodyBuilder.Attachments.Add(rutaPdfAdjunto);

        mensaje.Body = bodyBuilder.ToMessageBody();

        await SendMessageAsync(mensaje);
    }

    public async Task EnviarGuiaRemisionAsync(
        string numeroGuiaRemision,
        string numeroDocumentoSustento,
        IEnumerable<string> destinatarios,
        string? nombreDestinatario,
        string rutaXmlAdjunto,
        string? rutaPdfAdjunto)
    {
        var destinatariosNormalizados = destinatarios
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!destinatariosNormalizados.Any())
            throw new InvalidOperationException("La guía de remisión no tiene destinatarios válidos para el envío.");

        if (string.IsNullOrWhiteSpace(rutaXmlAdjunto) || !File.Exists(rutaXmlAdjunto))
            throw new FileNotFoundException("No se encontró el XML de la guía de remisión para adjuntarlo al correo.", rutaXmlAdjunto);

        if (!string.IsNullOrWhiteSpace(rutaPdfAdjunto) && !File.Exists(rutaPdfAdjunto))
            throw new FileNotFoundException("No se encontró el PDF de la guía de remisión para adjuntarlo al correo.", rutaPdfAdjunto);

        var destinatarioSeguro = string.IsNullOrWhiteSpace(nombreDestinatario)
            ? "destinatario"
            : System.Net.WebUtility.HtmlEncode(nombreDestinatario.Trim());
        var documentoSustentoSeguro = string.IsNullOrWhiteSpace(numeroDocumentoSustento)
            ? "documento sustento"
            : System.Net.WebUtility.HtmlEncode(numeroDocumentoSustento.Trim());
        var archivosAdjuntos = !string.IsNullOrWhiteSpace(rutaPdfAdjunto)
            ? "PDF y XML electrónicos"
            : "XML electrónico";

        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(_nombreRemitente, _usuario));

        foreach (var correo in destinatariosNormalizados)
            mensaje.To.Add(MailboxAddress.Parse(correo));

        mensaje.Subject = $"Guía de remisión electrónica {numeroGuiaRemision}";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
<div style='font-family:Segoe UI,Arial,sans-serif;background:#f6f8fb;padding:24px'>
  <div style='max-width:620px;margin:auto;background:#ffffff;border:1px solid #e8eef5;border-radius:14px;overflow:hidden'>
    <div style='background:#0f172a;color:#fff;padding:18px 22px'>
      <div style='font-size:18px;font-weight:700'>E-FACT</div>
      <div style='opacity:.85;font-size:13px;margin-top:4px'>Entrega de comprobante electrónico</div>
    </div>
    <div style='padding:22px'>
      <p style='margin:0 0 10px 0;color:#111827'>Estimado cliente,</p>
      <p style='margin:0 0 16px 0;color:#374151;line-height:1.5'>
        Adjuntamos los archivos correspondientes a la guía de remisión electrónica <b>{numeroGuiaRemision}</b> emitida para <b>{destinatarioSeguro}</b>.
      </p>
      <div style='background:#f8fafc;border:1px solid #e5e7eb;border-radius:12px;padding:14px 16px;line-height:1.7;color:#1f2937'>
        <div><b>Guía de remisión:</b> {numeroGuiaRemision}</div>
        <div><b>Destinatario:</b> {destinatarioSeguro}</div>
        <div><b>Documento sustento:</b> {documentoSustentoSeguro}</div>
        <div><b>Archivos adjuntos:</b> {archivosAdjuntos}</div>
      </div>
      <p style='margin:16px 0 0 0;color:#374151;line-height:1.5'>
        Este correo fue generado automáticamente por el sistema de facturación electrónica. Si necesitas una nueva copia o una corrección en los destinatarios, por favor contacta al emisor.
      </p>
    </div>
  </div>
</div>"
        };

        bodyBuilder.Attachments.Add(rutaXmlAdjunto);
        if (!string.IsNullOrWhiteSpace(rutaPdfAdjunto))
            bodyBuilder.Attachments.Add(rutaPdfAdjunto);

        mensaje.Body = bodyBuilder.ToMessageBody();

        await SendMessageAsync(mensaje);
    }

    public async Task EnviarLiquidacionCompraAsync(
        string numeroLiquidacion,
        IEnumerable<string> destinatarios,
        string? nombreProveedor,
        decimal? totalLiquidacion,
        string rutaXmlAdjunto,
        string? rutaPdfAdjunto)
    {
        var destinatariosNormalizados = destinatarios
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!destinatariosNormalizados.Any())
            throw new InvalidOperationException("La liquidación de compra no tiene destinatarios válidos para el envío.");

        if (string.IsNullOrWhiteSpace(rutaXmlAdjunto) || !File.Exists(rutaXmlAdjunto))
            throw new FileNotFoundException("No se encontró el XML de la liquidación de compra para adjuntarlo al correo.", rutaXmlAdjunto);

        if (!string.IsNullOrWhiteSpace(rutaPdfAdjunto) && !File.Exists(rutaPdfAdjunto))
            throw new FileNotFoundException("No se encontró el PDF de la liquidación de compra para adjuntarlo al correo.", rutaPdfAdjunto);

        var proveedorSeguro = string.IsNullOrWhiteSpace(nombreProveedor)
            ? "proveedor"
            : System.Net.WebUtility.HtmlEncode(nombreProveedor.Trim());
        var totalTexto = (totalLiquidacion ?? 0m).ToString("N2", new CultureInfo("es-EC"));
        var archivosAdjuntos = !string.IsNullOrWhiteSpace(rutaPdfAdjunto)
            ? "PDF y XML electrónicos"
            : "XML electrónico";

        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(_nombreRemitente, _usuario));

        foreach (var correo in destinatariosNormalizados)
            mensaje.To.Add(MailboxAddress.Parse(correo));

        mensaje.Subject = $"Liquidación de compra electrónica {numeroLiquidacion}";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
<div style='font-family:Segoe UI,Arial,sans-serif;background:#f6f8fb;padding:24px'>
  <div style='max-width:620px;margin:auto;background:#ffffff;border:1px solid #e8eef5;border-radius:14px;overflow:hidden'>
    <div style='background:#0f172a;color:#fff;padding:18px 22px'>
      <div style='font-size:18px;font-weight:700'>E-FACT</div>
      <div style='opacity:.85;font-size:13px;margin-top:4px'>Entrega de comprobante electrónico</div>
    </div>
    <div style='padding:22px'>
      <p style='margin:0 0 10px 0;color:#111827'>Estimado proveedor,</p>
      <p style='margin:0 0 16px 0;color:#374151;line-height:1.5'>
        Adjuntamos los archivos correspondientes a la liquidación de compra electrónica <b>{numeroLiquidacion}</b> emitida para <b>{proveedorSeguro}</b>.
      </p>
      <div style='background:#f8fafc;border:1px solid #e5e7eb;border-radius:12px;padding:14px 16px;line-height:1.7;color:#1f2937'>
        <div><b>Liquidación:</b> {numeroLiquidacion}</div>
        <div><b>Proveedor:</b> {proveedorSeguro}</div>
        <div><b>Total:</b> ${totalTexto}</div>
        <div><b>Archivos adjuntos:</b> {archivosAdjuntos}</div>
      </div>
      <p style='margin:16px 0 0 0;color:#374151;line-height:1.5'>
        Este correo fue generado automáticamente por el sistema de facturación electrónica. Si necesitas una nueva copia o una corrección en los destinatarios, por favor contacta al emisor.
      </p>
    </div>
  </div>
</div>"
        };

        bodyBuilder.Attachments.Add(rutaXmlAdjunto);
        if (!string.IsNullOrWhiteSpace(rutaPdfAdjunto))
            bodyBuilder.Attachments.Add(rutaPdfAdjunto);

        mensaje.Body = bodyBuilder.ToMessageBody();

        await SendMessageAsync(mensaje);
    }

    public async Task EnviarRetencionAsync(
        string numeroRetencion,
        string numeroDocumentoSustento,
        IEnumerable<string> destinatarios,
        string? nombreProveedor,
        decimal? totalRetenido,
        string rutaXmlAdjunto,
        string? rutaPdfAdjunto)
    {
        var destinatariosNormalizados = destinatarios
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!destinatariosNormalizados.Any())
            throw new InvalidOperationException("La retención no tiene destinatarios válidos para el envío.");

        if (string.IsNullOrWhiteSpace(rutaXmlAdjunto) || !File.Exists(rutaXmlAdjunto))
            throw new FileNotFoundException("No se encontró el XML de la retención para adjuntarlo al correo.", rutaXmlAdjunto);

        if (!string.IsNullOrWhiteSpace(rutaPdfAdjunto) && !File.Exists(rutaPdfAdjunto))
            throw new FileNotFoundException("No se encontró el PDF de la retención para adjuntarlo al correo.", rutaPdfAdjunto);

        var proveedorSeguro = string.IsNullOrWhiteSpace(nombreProveedor)
            ? "proveedor"
            : System.Net.WebUtility.HtmlEncode(nombreProveedor.Trim());

        var documentoSustentoSeguro = string.IsNullOrWhiteSpace(numeroDocumentoSustento)
            ? "documento relacionado"
            : System.Net.WebUtility.HtmlEncode(numeroDocumentoSustento.Trim());

        var totalTexto = (totalRetenido ?? 0m).ToString("N2", new CultureInfo("es-EC"));
        var archivosAdjuntos = !string.IsNullOrWhiteSpace(rutaPdfAdjunto)
            ? "PDF y XML electrónicos"
            : "XML electrónico";

        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(_nombreRemitente, _usuario));

        foreach (var correo in destinatariosNormalizados)
            mensaje.To.Add(MailboxAddress.Parse(correo));

        mensaje.Subject = $"Comprobante de retención electrónico {numeroRetencion}";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
<div style='font-family:Segoe UI,Arial,sans-serif;background:#f6f8fb;padding:24px'>
  <div style='max-width:620px;margin:auto;background:#ffffff;border:1px solid #e8eef5;border-radius:14px;overflow:hidden'>
    <div style='background:#0f172a;color:#fff;padding:18px 22px'>
      <div style='font-size:18px;font-weight:700'>E-FACT</div>
      <div style='opacity:.85;font-size:13px;margin-top:4px'>Entrega de comprobante electrónico</div>
    </div>
    <div style='padding:22px'>
      <p style='margin:0 0 10px 0;color:#111827'>Estimado proveedor,</p>
      <p style='margin:0 0 16px 0;color:#374151;line-height:1.5'>
        Adjuntamos los archivos correspondientes al comprobante de retención electrónico <b>{numeroRetencion}</b> emitido para <b>{proveedorSeguro}</b>.
      </p>
      <div style='background:#f8fafc;border:1px solid #e5e7eb;border-radius:12px;padding:14px 16px;line-height:1.7;color:#1f2937'>
        <div><b>Retención:</b> {numeroRetencion}</div>
        <div><b>Documento sustento:</b> {documentoSustentoSeguro}</div>
        <div><b>Proveedor:</b> {proveedorSeguro}</div>
        <div><b>Total retenido:</b> ${totalTexto}</div>
        <div><b>Archivos adjuntos:</b> {archivosAdjuntos}</div>
      </div>
      <p style='margin:16px 0 0 0;color:#374151;line-height:1.5'>
        Este correo fue generado automáticamente por el sistema de facturación electrónica. Si necesitas una nueva copia o una corrección en los destinatarios, por favor contacta al emisor.
      </p>
    </div>
  </div>
</div>"
        };

        bodyBuilder.Attachments.Add(rutaXmlAdjunto);
        if (!string.IsNullOrWhiteSpace(rutaPdfAdjunto))
            bodyBuilder.Attachments.Add(rutaPdfAdjunto);

        mensaje.Body = bodyBuilder.ToMessageBody();

        await SendMessageAsync(mensaje);
    }

    public async Task EnviarConfirmacionCompraDocumentosAsync(
        string emailDestino,
        string? nombreCliente,
        int documentosComprados,
        int saldoActual,
        decimal montoTotal,
        string? reference,
        string? authorizationCode)
    {
        emailDestino = NormalizarEmail(emailDestino);
        var clienteSeguro = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(nombreCliente) ? "Cliente E-FACT" : nombreCliente.Trim());
        var referenceSegura = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(reference) ? "Pendiente de referencia" : reference.Trim());
        var autorizacionSegura = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(authorizationCode) ? "Pendiente de autorizacion" : authorizationCode.Trim());
        var totalTexto = montoTotal.ToString("N2", CultureInfo.GetCultureInfo("es-EC"));

        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(_nombreRemitente, _usuario));
        mensaje.To.Add(MailboxAddress.Parse(emailDestino));

        foreach (var correoNotificacion in ObtenerDestinatariosNotificacionPagos(emailDestino))
            mensaje.Bcc.Add(MailboxAddress.Parse(correoNotificacion));

        mensaje.Subject = "Confirmacion de recarga de documentos | E-FACT";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
<table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0' style='width:100%;background:#eef4fb;margin:0;padding:24px 0;font-family:Segoe UI,Arial,sans-serif;color:#16324f;'>
  <tr>
    <td align='center'>
      <table role='presentation' width='640' cellpadding='0' cellspacing='0' border='0' style='width:640px;max-width:640px;background:#ffffff;border:1px solid #dce8f6;border-collapse:separate;'>
        <tr>
          <td style='padding:28px 30px;background:#0b5ed7;color:#ffffff;'>
            <div style='font-size:12px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;'>Pago aprobado</div>
            <div style='font-size:30px;font-weight:800;line-height:1.1;margin-top:10px;'>Recarga acreditada con exito</div>
            <div style='font-size:14px;line-height:1.6;margin-top:10px;color:#dcecff;'>
              Tu compra de documentos ya fue procesada y el saldo se actualizo en tu cuenta.
            </div>
          </td>
        </tr>
        <tr>
          <td style='padding:28px 30px 32px 30px;'>
            <p style='margin:0 0 18px 0;font-size:15px;line-height:1.6;'>
              Hola <b>{clienteSeguro}</b>, este es el resumen de tu recarga en E-FACT.
            </p>
            <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0' style='width:100%;border-collapse:separate;'>
              <tr>
                <td style='background:#f8fbff;border:1px solid #e2edf9;padding:16px 18px;'>
                  <div style='font-size:13px;color:#6782a0;'>Documentos acreditados</div>
                  <div style='font-size:28px;font-weight:800;color:#0b5ed7;margin-top:4px;'>{documentosComprados}</div>
                </td>
              </tr>
              <tr><td style='height:12px;line-height:12px;font-size:12px;'>&nbsp;</td></tr>
              <tr>
                <td style='background:#f8fbff;border:1px solid #e2edf9;padding:16px 18px;'>
                  <div style='font-size:13px;color:#6782a0;'>Saldo actual disponible</div>
                  <div style='font-size:28px;font-weight:800;color:#16324f;margin-top:4px;'>{saldoActual} documentos</div>
                </td>
              </tr>
            </table>
            <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0' style='width:100%;margin-top:18px;border:1px solid #e6edf5;font-size:14px;line-height:1.8;'>
              <tr><td style='padding:16px 18px;'><b>Monto pagado:</b> ${totalTexto}</td></tr>
              <tr><td style='padding:0 18px 8px 18px;'><b>Referencia:</b> {referenceSegura}</td></tr>
              <tr><td style='padding:0 18px 16px 18px;'><b>Autorizacion:</b> {autorizacionSegura}</td></tr>
            </table>
            <p style='margin:18px 0 0 0;font-size:13px;line-height:1.7;color:#5e738a;'>
              Si no reconoces esta operacion, por favor contacta al equipo de soporte de inmediato.
            </p>
          </td>
        </tr>
      </table>
    </td>
  </tr>
</table>"
        };

        mensaje.Body = bodyBuilder.ToMessageBody();

        await SendMessageAsync(mensaje);
    }

    public async Task EnviarEstadoCuentaAsync(
        IEnumerable<string> destinatarios,
        EstadoCuentaDetalleVM detalle,
        byte[] pdfAdjunto,
        string nombreArchivoPdf)
    {
        var destinatariosNormalizados = destinatarios
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!destinatariosNormalizados.Any())
            throw new InvalidOperationException("El estado de cuenta no tiene destinatarios válidos para el envío.");

        if (detalle == null)
            throw new InvalidOperationException("No se encontró la información del estado de cuenta a enviar.");

        if (pdfAdjunto == null || pdfAdjunto.Length == 0)
            throw new InvalidOperationException("No se pudo generar el PDF del estado de cuenta para adjuntarlo al correo.");

        var clienteSeguro = string.IsNullOrWhiteSpace(detalle.NombreCliente)
            ? "cliente"
            : WebUtility.HtmlEncode(detalle.NombreCliente.Trim());
        var identificacionSegura = string.IsNullOrWhiteSpace(detalle.NumeroIdentificacion)
            ? "-"
            : WebUtility.HtmlEncode(detalle.NumeroIdentificacion.Trim());
        var fechaCorteTexto = DateTime.Now.ToString("dd/MM/yyyy", new CultureInfo("es-EC"));
        var saldoTexto = detalle.SaldoTotal.ToString("N2", new CultureInfo("es-EC"));
        var ultimoAbonoTexto = detalle.MontoUltimoAbono.ToString("N2", new CultureInfo("es-EC"));
        var fechaUltimoAbonoTexto = detalle.FechaUltimoAbono?.ToString("dd/MM/yyyy", new CultureInfo("es-EC")) ?? "Sin registros";
        var diasVencidosTexto = detalle.DiasVencidosMaximos > 0 ? detalle.DiasVencidosMaximos.ToString() : "0";

        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(_nombreRemitente, _usuario));

        foreach (var correo in destinatariosNormalizados)
            mensaje.To.Add(MailboxAddress.Parse(correo));

        mensaje.Subject = $"Estado de cuenta de {detalle.NombreCliente} - corte {fechaCorteTexto}";

        var bodyBuilder = new BodyBuilder
        {
            TextBody =
$@"Estimado/a {detalle.NombreCliente},

Adjuntamos su estado de cuenta actualizado generado el {fechaCorteTexto}.

Resumen:
- Cliente: {detalle.NombreCliente}
- Identificación: {detalle.NumeroIdentificacion}
- Saldo pendiente: ${saldoTexto}
- Facturas pendientes: {detalle.FacturasPendientes}
- Último abono: ${ultimoAbonoTexto}
- Fecha último abono: {fechaUltimoAbonoTexto}
- Máximo de días vencidos: {diasVencidosTexto}

En el archivo PDF adjunto encontrará el detalle completo de facturas, abonos y movimientos.

Si necesita aclaraciones o desea registrar un pago, responda a este correo o comuníquese con nosotros.

Atentamente,
{_nombreRemitente}",
            HtmlBody = $@"
<div style='margin:0;padding:24px;background-color:#f4f7fb;font-family:Arial,Helvetica,sans-serif;color:#1f2937;'>
  <table role='presentation' cellpadding='0' cellspacing='0' border='0' width='100%' style='max-width:680px;margin:0 auto;background:#ffffff;border:1px solid #d9e3f0;border-radius:12px;overflow:hidden;'>
    <tr>
      <td style='background:#0f172a;padding:22px 28px;color:#ffffff;'>
        <div style='font-size:20px;font-weight:700;line-height:1.2;'>Estado de cuenta actualizado</div>
        <div style='font-size:13px;opacity:0.88;margin-top:6px;'>Numerica e-fact</div>
      </td>
    </tr>
    <tr>
      <td style='padding:28px;'>
        <p style='margin:0 0 14px 0;font-size:15px;line-height:1.6;'>Estimado/a <strong>{clienteSeguro}</strong>,</p>
        <p style='margin:0 0 18px 0;font-size:15px;line-height:1.6;color:#374151;'>
          Le compartimos su estado de cuenta con corte al <strong>{fechaCorteTexto}</strong>. En el PDF adjunto encontrará el detalle completo de facturas, abonos y movimientos registrados.
        </p>

        <table role='presentation' cellpadding='0' cellspacing='0' border='0' width='100%' style='border-collapse:collapse;background:#f8fbff;border:1px solid #d9e8fb;border-radius:10px;'>
          <tr>
            <td style='padding:16px 18px;font-size:14px;line-height:1.75;color:#1f2937;'>
              <div><strong>Cliente:</strong> {clienteSeguro}</div>
              <div><strong>Identificación:</strong> {identificacionSegura}</div>
              <div><strong>Saldo pendiente:</strong> ${saldoTexto}</div>
              <div><strong>Facturas pendientes:</strong> {detalle.FacturasPendientes}</div>
              <div><strong>Último abono:</strong> ${ultimoAbonoTexto}</div>
              <div><strong>Fecha último abono:</strong> {fechaUltimoAbonoTexto}</div>
              <div><strong>Máximo de días vencidos:</strong> {diasVencidosTexto}</div>
            </td>
          </tr>
        </table>

        <p style='margin:18px 0 0 0;font-size:14px;line-height:1.65;color:#374151;'>
          Si desea revisar su saldo con nosotros o registrar un pago, puede responder directamente a este correo y con gusto le ayudaremos.
        </p>
      </td>
    </tr>
    <tr>
      <td style='padding:18px 28px;background:#f8fafc;border-top:1px solid #e5edf6;font-size:12px;line-height:1.6;color:#64748b;'>
        Este mensaje fue generado automáticamente por Numerica e-fact. Archivo adjunto: <strong>{WebUtility.HtmlEncode(nombreArchivoPdf)}</strong>.
      </td>
    </tr>
  </table>
</div>"
        };

        bodyBuilder.Attachments.Add(nombreArchivoPdf, pdfAdjunto, ContentType.Parse("application/pdf"));
        mensaje.Body = bodyBuilder.ToMessageBody();

        await SendMessageAsync(mensaje);
    }

    public async Task EnviarNotificacionPagoFirmaElectronicaAsync(
        string? nombreSolicitante,
        string? identificacion,
        string? correoSolicitante,
        string? tipoFirma,
        string? vigencia,
        decimal montoTotal,
        string? reference,
        string? authorizationCode,
        int solicitudId)
    {
        var destinatarios = ObtenerDestinatariosNotificacionPagos();
        if (!destinatarios.Any())
        {
            _logger.LogWarning(
                "Se omitio la notificacion administrativa del pago de firma electronica {SolicitudId} porque no hay destinatarios configurados.",
                solicitudId);
            return;
        }

        var solicitanteSeguro = WebUtility.HtmlEncode(
            string.IsNullOrWhiteSpace(nombreSolicitante) ? "Solicitante sin nombre" : nombreSolicitante.Trim());
        var identificacionSegura = WebUtility.HtmlEncode(
            string.IsNullOrWhiteSpace(identificacion) ? "No registrada" : identificacion.Trim());
        var correoSeguro = WebUtility.HtmlEncode(
            string.IsNullOrWhiteSpace(correoSolicitante) ? "No registrado" : correoSolicitante.Trim());
        var tipoFirmaSeguro = WebUtility.HtmlEncode(
            string.IsNullOrWhiteSpace(tipoFirma) ? "No especificado" : tipoFirma.Trim());
        var vigenciaSegura = WebUtility.HtmlEncode(
            string.IsNullOrWhiteSpace(vigencia) ? "No especificada" : vigencia.Trim());
        var referenciaSegura = WebUtility.HtmlEncode(
            string.IsNullOrWhiteSpace(reference) ? "Pendiente de referencia" : reference.Trim());
        var autorizacionSegura = WebUtility.HtmlEncode(
            string.IsNullOrWhiteSpace(authorizationCode) ? "Pendiente de autorizacion" : authorizationCode.Trim());
        var totalTexto = montoTotal.ToString("N2", CultureInfo.GetCultureInfo("es-EC"));

        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(_nombreRemitente, _usuario));

        foreach (var destinatario in destinatarios)
            mensaje.To.Add(MailboxAddress.Parse(destinatario));

        mensaje.Subject = $"Notificacion de pago aprobado de firma electronica | Solicitud #{solicitudId}";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
<table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0' style='width:100%;background:#eef4fb;margin:0;padding:24px 0;font-family:Segoe UI,Arial,sans-serif;color:#16324f;'>
  <tr>
    <td align='center'>
      <table role='presentation' width='640' cellpadding='0' cellspacing='0' border='0' style='width:640px;max-width:640px;background:#ffffff;border:1px solid #dce8f6;border-collapse:separate;'>
        <tr>
          <td style='padding:28px 30px;background:#0b5ed7;color:#ffffff;'>
            <div style='font-size:12px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;'>Registro contable</div>
            <div style='font-size:30px;font-weight:800;line-height:1.1;margin-top:10px;'>Pago aprobado de firma electronica</div>
            <div style='font-size:14px;line-height:1.6;margin-top:10px;color:#dcecff;'>
              Se aprobo un pago de solicitud de firma electronica y se envia este aviso para control administrativo.
            </div>
          </td>
        </tr>
        <tr>
          <td style='padding:28px 30px 32px 30px;'>
            <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0' style='width:100%;border:1px solid #e6edf5;font-size:14px;line-height:1.8;'>
              <tr><td style='padding:16px 18px 0 18px;'><b>Solicitud:</b> #{solicitudId}</td></tr>
              <tr><td style='padding:0 18px;'><b>Solicitante:</b> {solicitanteSeguro}</td></tr>
              <tr><td style='padding:0 18px;'><b>Identificacion:</b> {identificacionSegura}</td></tr>
              <tr><td style='padding:0 18px;'><b>Correo:</b> {correoSeguro}</td></tr>
              <tr><td style='padding:0 18px;'><b>Tipo de firma:</b> {tipoFirmaSeguro}</td></tr>
              <tr><td style='padding:0 18px;'><b>Vigencia:</b> {vigenciaSegura}</td></tr>
              <tr><td style='padding:0 18px;'><b>Monto pagado:</b> ${totalTexto}</td></tr>
              <tr><td style='padding:0 18px;'><b>Referencia:</b> {referenciaSegura}</td></tr>
              <tr><td style='padding:0 18px 16px 18px;'><b>Autorizacion:</b> {autorizacionSegura}</td></tr>
            </table>
            <p style='margin:18px 0 0 0;font-size:13px;line-height:1.7;color:#5e738a;'>
              Este correo fue generado automaticamente por E-FACT para facilitar el registro posterior en el sistema contable.
            </p>
          </td>
        </tr>
      </table>
    </td>
  </tr>
</table>"
        };

        mensaje.Body = bodyBuilder.ToMessageBody();

        await SendMessageAsync(mensaje);
    }

    private async Task SendMessageAsync(MimeMessage mensaje)
    {
        if (string.IsNullOrWhiteSpace(_host))
            throw new InvalidOperationException("No se configuro el host SMTP para el envio de correos.");

        if (string.IsNullOrWhiteSpace(_usuario) || string.IsNullOrWhiteSpace(_pass))
            throw new InvalidOperationException("No se configuraron las credenciales SMTP para el envio de correos.");

        AplicarRemitenteFijo(mensaje);

        using var client = new SmtpClient();
        client.Timeout = _timeoutMs;

        Exception? ultimoErrorConexion = null;

        try
        {
            foreach (var socketOption in ObtenerOpcionesSeguridad())
            {
                try
                {
                    Debug.WriteLine($"[SMTP] Intentando conectar a {_host}:{_puerto} con {socketOption}");
                    await client.ConnectAsync(_host, _puerto, socketOption);
                    Debug.WriteLine($"[SMTP] Conectado. Autenticando {_usuario}");
                    await client.AuthenticateAsync(_usuario, _pass);
                    Debug.WriteLine($"[SMTP] Autenticacion OK con {_host}:{_puerto} usando {socketOption}");
                    ultimoErrorConexion = null;
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SMTP][ERROR] {_host}:{_puerto} usando {socketOption}: {ex}");
                    ultimoErrorConexion = ex;

                    _logger.LogWarning(
                        ex,
                        "Fallo al conectar o autenticar SMTP con {Host}:{Puerto} usando {SocketOption}.",
                        _host,
                        _puerto,
                        socketOption);

                    if (client.IsConnected)
                    {
                        await client.DisconnectAsync(true);
                    }
                }
            }

            if (ultimoErrorConexion != null || !client.IsAuthenticated)
            {
                throw new InvalidOperationException(
                    $"No fue posible establecer conexion con el servidor SMTP {_host}:{_puerto}.",
                    ultimoErrorConexion);
            }

            Debug.WriteLine($"[SMTP] From: {string.Join(", ", mensaje.From.Select(x => x.ToString()))}");
            Debug.WriteLine($"[SMTP] Sender: {mensaje.Sender}");
            Debug.WriteLine($"[SMTP] ReplyTo: {string.Join(", ", mensaje.ReplyTo.Select(x => x.ToString()))}");
            Debug.WriteLine($"[SMTP] Enviando mensaje a: {string.Join(", ", mensaje.To.Select(x => x.ToString()))}");
            _logger.LogInformation(
                "SMTP envio - from:{From} - sender:{Sender} - replyTo:{ReplyTo} - to:{To}",
                string.Join(", ", mensaje.From.Select(x => x.ToString())),
                mensaje.Sender?.ToString() ?? "(vacio)",
                string.Join(", ", mensaje.ReplyTo.Select(x => x.ToString())),
                string.Join(", ", mensaje.To.Select(x => x.ToString())));
            await client.SendAsync(mensaje);
            Debug.WriteLine("[SMTP] Mensaje enviado correctamente.");
        }
        finally
        {
            if (client.IsConnected)
                await client.DisconnectAsync(true);
        }
    }

    private void AplicarRemitenteFijo(MimeMessage mensaje)
    {
        var remitente = new MailboxAddress(_nombreRemitente, _usuario);

        mensaje.From.Clear();
        mensaje.From.Add(remitente);
        mensaje.Sender = remitente;
        mensaje.ReplyTo.Clear();
        mensaje.ReplyTo.Add(remitente);
    }

    private IEnumerable<SecureSocketOptions> ObtenerOpcionesSeguridad()
    {
        var opciones = new[]
        {
            _seguridadPreferida,
            SecureSocketOptions.Auto,
            SecureSocketOptions.StartTlsWhenAvailable,
            SecureSocketOptions.StartTls,
            SecureSocketOptions.SslOnConnect
        };

        var vistos = new HashSet<SecureSocketOptions>();
        foreach (var opcion in opciones)
        {
            if (vistos.Add(opcion))
            {
                yield return opcion;
            }
        }
    }

    private static SecureSocketOptions ParseSecureSocketOption(string? valor)
    {
        return valor?.Trim().ToLowerInvariant() switch
        {
            "none" => SecureSocketOptions.None,
            "aut" => SecureSocketOptions.Auto,
            "auto" => SecureSocketOptions.Auto,
            "starttls" => SecureSocketOptions.StartTls,
            "starttlswhenavailable" => SecureSocketOptions.StartTlsWhenAvailable,
            "ssl" => SecureSocketOptions.SslOnConnect,
            "sslonconnect" => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.Auto
        };
    }

    private static string? GetConfigValue(IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static int? GetConfigInt(IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }
        }

        return null;
    }

    private static string NormalizarEmail(string email)
    {
        var emailNormalizado = (email ?? string.Empty).Trim().ToLowerInvariant();
        _ = MailboxAddress.Parse(emailNormalizado);
        return emailNormalizado;
    }

    private List<string> ObtenerDestinatariosNotificacionPagos(params string?[] excluirCorreos)
    {
        if (!_notificacionPagosDestinatarios.Any())
            return new List<string>();

        var excluidos = new HashSet<string>(
            NormalizarListaCorreos(excluirCorreos),
            StringComparer.OrdinalIgnoreCase);

        return _notificacionPagosDestinatarios
            .Where(correo => !excluidos.Contains(correo))
            .ToList();
    }

    private static IEnumerable<string> ParseCorreosConfigurados(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return Array.Empty<string>();

        return valor
            .Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static List<string> NormalizarListaCorreos(IEnumerable<string?>? correos)
    {
        if (correos == null)
            return new List<string>();

        var normalizados = new List<string>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var correo in correos)
        {
            if (string.IsNullOrWhiteSpace(correo))
                continue;

            try
            {
                var email = NormalizarEmail(correo);
                if (vistos.Add(email))
                    normalizados.Add(email);
            }
            catch
            {
                // Ignora correos inválidos de configuración para no bloquear el arranque.
            }
        }

        return normalizados;
    }

    private static string PrepararCodigoRecuperacionParaCorreo(string codigo)
    {
        var codigoNormalizado = (codigo ?? string.Empty).Trim();
        if (RecoveryCodeHelper.EsCodigoValido(codigoNormalizado))
        {
            return codigoNormalizado;
        }

        return RecoveryCodeHelper.GenerarCodigoNumerico();
    }

    private static string EnmascararEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains("@")) return "***";
        var parts = email.Split('@');
        var user = parts[0];
        var dom = parts[1];
        var visible = user.Length <= 2 ? user : user.Substring(0, 2);
        return $"{visible}***@{dom}";
    }
}
