using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;
using System.Text.Json;

namespace Simetric.Services;

public class RetencionCorreoService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IWebHostEnvironment _env;
    private readonly IEmailService _emailService;
    private readonly RetencionGeneradaService _retencionGeneradaService;
    private readonly IRetencionPdfService _retencionPdfService;

    public RetencionCorreoService(
        IDbContextFactory<AppDbContext> dbFactory,
        IWebHostEnvironment env,
        IEmailService emailService,
        RetencionGeneradaService retencionGeneradaService,
        IRetencionPdfService retencionPdfService)
    {
        _dbFactory = dbFactory;
        _env = env;
        _emailService = emailService;
        _retencionGeneradaService = retencionGeneradaService;
        _retencionPdfService = retencionPdfService;
    }

    private sealed class RetencionCorreoMetadata
    {
        public List<string> Destinatarios { get; set; } = new();
        public bool CorreoEnviado { get; set; }
        public DateTime? FechaEnvioCorreo { get; set; }
        public string? UltimoErrorCorreo { get; set; }
    }

    private static List<string> NormalizarCorreos(IEnumerable<string?>? correos)
    {
        if (correos == null)
            return new List<string>();

        return correos
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ParseCorreosDocumento(string? correoad)
    {
        if (string.IsNullOrWhiteSpace(correoad))
            return new List<string>();

        return NormalizarCorreos(
            correoad.Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string SerializarCorreosDocumento(IEnumerable<string> correos)
        => string.Join(";", NormalizarCorreos(correos));

    private static RetencionCorreoMetadata LeerRetencionCorreoMetadata(string? detalleextra)
    {
        if (string.IsNullOrWhiteSpace(detalleextra))
            return new RetencionCorreoMetadata();

        try
        {
            return JsonSerializer.Deserialize<RetencionCorreoMetadata>(detalleextra) ?? new RetencionCorreoMetadata();
        }
        catch
        {
            return new RetencionCorreoMetadata();
        }
    }

    private static string EscribirRetencionCorreoMetadata(RetencionCorreoMetadata metadata)
        => JsonSerializer.Serialize(metadata);

    private static bool RetencionEstaAutorizada(string? autorizado)
    {
        if (string.IsNullOrWhiteSpace(autorizado))
            return false;

        var valor = autorizado.Trim();
        return valor.Equals("true", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("1", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("s", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("si", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("sí", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("a", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("autorizado", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<int?> RegistrarDestinatariosRetencionPorCompraAsync(
        int codCompra,
        string? correoPrincipal,
        List<FacturaCorreoDestinoDto>? correosRetencion = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var compra = await db.ComprasFacturas
            .FirstOrDefaultAsync(x => x.CodFactura == codCompra);

        if (compra == null)
            return null;

        var retencion = await db.RetencionInfo
            .OrderByDescending(x => x.Sec)
            .FirstOrDefaultAsync(x => x.IcCompra == codCompra);

        if (retencion == null)
            return null;

        var correosNormalizados = NormalizarCorreos(correosRetencion?.Select(x => x.Correo));
        var correosGuardarEnCliente = NormalizarCorreos(
            correosRetencion?
                .Where(x => x.GuardarEnCliente)
                .Select(x => x.Correo));

        var destinatariosRetencion = await ComprobanteCorreoDestinatariosHelper.ConstruirDestinatariosClienteAsync(
            db,
            retencion.Usuario ?? compra.Usuario,
            compra.CodClientes,
            correoPrincipal,
            correosNormalizados);

        var metadata = LeerRetencionCorreoMetadata(retencion.Detalleextra);
        metadata.Destinatarios = destinatariosRetencion;

        if (!metadata.CorreoEnviado)
        {
            metadata.FechaEnvioCorreo = null;
            metadata.UltimoErrorCorreo = null;
        }

        retencion.Correoad = SerializarCorreosDocumento(destinatariosRetencion);
        retencion.Detalleextra = EscribirRetencionCorreoMetadata(metadata);

        if (compra.CodClientes.HasValue && correosGuardarEnCliente.Any())
        {
            var correosExistentes = await db.ClientesCorreos
                .Where(cc => cc.CodCliente == compra.CodClientes.Value && cc.Estado)
                .Select(cc => cc.Correo)
                .ToListAsync();

            var hashCorreos = new HashSet<string>(
                correosExistentes
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var correo in correosGuardarEnCliente)
            {
                if (string.Equals(correo, correoPrincipal?.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (hashCorreos.Contains(correo))
                    continue;

                db.ClientesCorreos.Add(new ClienteCorreo
                {
                    CodCliente = compra.CodClientes.Value,
                    Correo = correo,
                    Estado = true
                });

                hashCorreos.Add(correo);
            }
        }

        await db.SaveChangesAsync();
        return retencion.Sec;
    }

    public async Task<FacturaCorreoEnvioResultadoDto> IntentarEnviarRetencionPorCorreoAsync(int secRetencion, string? rutaXmlExistente = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var retencion = await db.RetencionInfo
            .FirstOrDefaultAsync(x => x.Sec == secRetencion);

        if (retencion == null)
        {
            return new FacturaCorreoEnvioResultadoDto
            {
                Error = true,
                Mensaje = "No se encontró la retención para procesar el envío por correo."
            };
        }

        var compra = retencion.IcCompra.HasValue
            ? await db.ComprasFacturas
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CodFactura == retencion.IcCompra.Value)
            : null;

        string? correoPrincipalProveedor = null;
        if (compra?.CodClientes is > 0)
        {
            correoPrincipalProveedor = await db.Clientes
                .AsNoTracking()
                .Where(c => c.Codcliente == compra.CodClientes.Value)
                .Select(c => c.Correo)
                .FirstOrDefaultAsync();
        }

        var metadata = LeerRetencionCorreoMetadata(retencion.Detalleextra);
        var destinatariosBase = await ComprobanteCorreoDestinatariosHelper.ConstruirDestinatariosClienteAsync(
            db,
            retencion.Usuario ?? compra?.Usuario,
            compra?.CodClientes,
            correoPrincipalProveedor);

        var destinatarios = ComprobanteCorreoDestinatariosHelper.NormalizarCorreos(
            metadata.Destinatarios
                .Concat(ParseCorreosDocumento(retencion.Correoad))
                .Concat(destinatariosBase));

        if (!destinatarios.Any())
        {
            metadata.Destinatarios = new List<string>();
            metadata.CorreoEnviado = false;
            metadata.UltimoErrorCorreo = "La retención no tiene destinatarios configurados.";
            retencion.Detalleextra = EscribirRetencionCorreoMetadata(metadata);
            await db.SaveChangesAsync();

            return new FacturaCorreoEnvioResultadoDto
            {
                SinDestinatarios = true,
                Mensaje = "La retención no tiene destinatarios configurados para el envío por correo."
            };
        }

        if (metadata.CorreoEnviado)
        {
            return new FacturaCorreoEnvioResultadoDto
            {
                YaEnviado = true,
                TotalDestinatarios = destinatarios.Count,
                Mensaje = $"La retención ya fue enviada previamente a {destinatarios.Count} destinatario(s)."
            };
        }

        if (!RetencionEstaAutorizada(retencion.Autorizado))
        {
            retencion.Correoad = SerializarCorreosDocumento(destinatarios);
            metadata.Destinatarios = destinatarios;
            metadata.UltimoErrorCorreo = null;
            retencion.Detalleextra = EscribirRetencionCorreoMetadata(metadata);
            await db.SaveChangesAsync();

            return new FacturaCorreoEnvioResultadoDto
            {
                PendienteAutorizacion = true,
                TotalDestinatarios = destinatarios.Count,
                Mensaje = $"La retención aún no está autorizada. El correo queda pendiente para {destinatarios.Count} destinatario(s) hasta que Autorizado tenga un valor aprobado."
            };
        }

        try
        {
            var rutaXml = ResolverRutaXmlRetencion(retencion, rutaXmlExistente);
            if (string.IsNullOrWhiteSpace(rutaXml))
                throw new FileNotFoundException("No se encontró el XML de la retención para adjuntarlo al correo.");

            var retencionView = await _retencionGeneradaService.GetRetencionDetalleAsync(secRetencion);
            if (retencionView == null)
                throw new InvalidOperationException("No se encontró el detalle completo de la retención para generar el PDF.");

            var rutaPdf = await _retencionPdfService.GenerarPdfRetencionAsync(retencionView);
            var numeroRetencion = !string.IsNullOrWhiteSpace(retencionView.NumeroCompleto)
                ? retencionView.NumeroCompleto
                : (retencion.NumRetencion ?? retencion.Sec.ToString());

            await _emailService.EnviarRetencionAsync(
                numeroRetencion,
                retencionView.DocumentoSustentoVisual,
                destinatarios,
                ObtenerNombreProveedor(retencionView),
                retencionView.TotalRetenido,
                rutaXml,
                rutaPdf);

            metadata.Destinatarios = destinatarios;
            metadata.CorreoEnviado = true;
            metadata.FechaEnvioCorreo = DateTime.Now;
            metadata.UltimoErrorCorreo = null;

            retencion.Correoad = SerializarCorreosDocumento(destinatarios);
            retencion.Detalleextra = EscribirRetencionCorreoMetadata(metadata);

            await db.SaveChangesAsync();

            return new FacturaCorreoEnvioResultadoDto
            {
                Enviado = true,
                TotalDestinatarios = destinatarios.Count,
                Mensaje = $"La retención fue enviada correctamente a {destinatarios.Count} destinatario(s)."
            };
        }
        catch (Exception ex)
        {
            metadata.Destinatarios = destinatarios;
            metadata.CorreoEnviado = false;
            metadata.UltimoErrorCorreo = ex.Message;

            retencion.Correoad = SerializarCorreosDocumento(destinatarios);
            retencion.Detalleextra = EscribirRetencionCorreoMetadata(metadata);

            await db.SaveChangesAsync();

            return new FacturaCorreoEnvioResultadoDto
            {
                Error = true,
                TotalDestinatarios = destinatarios.Count,
                Mensaje = $"No se pudo enviar la retención por correo: {ex.Message}"
            };
        }
    }

    public async Task<List<int>> GetRetencionesAutorizadasPendientesCorreoAsync(int maxRegistros = 20)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var candidatos = await db.RetencionInfo
            .AsNoTracking()
            .Where(r =>
                (!string.IsNullOrWhiteSpace(r.Correoad) || !string.IsNullOrWhiteSpace(r.Detalleextra)))
            .OrderBy(r => r.Sec)
            .Select(r => new
            {
                r.Sec,
                r.Autorizado,
                r.Correoad,
                r.Detalleextra
            })
            .Take(Math.Max(maxRegistros * 5, 50))
            .ToListAsync();

        return candidatos
            .Where(r =>
            {
                var metadata = LeerRetencionCorreoMetadata(r.Detalleextra);
                var tieneDestinatarios = metadata.Destinatarios.Any() || ParseCorreosDocumento(r.Correoad).Any();
                return RetencionEstaAutorizada(r.Autorizado) && tieneDestinatarios && !metadata.CorreoEnviado;
            })
            .Select(r => r.Sec)
            .Take(maxRegistros)
            .ToList();
    }

    private string? ResolverRutaXmlRetencion(RetencionInfo retencion, string? rutaXmlExistente)
    {
        if (!string.IsNullOrWhiteSpace(rutaXmlExistente))
        {
            if (Path.IsPathFullyQualified(rutaXmlExistente) && File.Exists(rutaXmlExistente))
                return rutaXmlExistente;

            var rutaDesdeNombre = Path.Combine(ObtenerWebRootPath(), "comprobantes", "generados", rutaXmlExistente);
            if (File.Exists(rutaDesdeNombre))
                return rutaDesdeNombre;
        }

        if (!string.IsNullOrWhiteSpace(retencion.NombreXml))
        {
            var rutaPorNombre = Path.Combine(ObtenerWebRootPath(), "comprobantes", "generados", retencion.NombreXml);
            if (File.Exists(rutaPorNombre))
                return rutaPorNombre;
        }

        if (!string.IsNullOrWhiteSpace(retencion.Clave))
        {
            var rutaFallback = Path.Combine(ObtenerWebRootPath(), "comprobantes", "generados", $"RET_{retencion.Clave}.xml");
            if (File.Exists(rutaFallback))
                return rutaFallback;
        }

        return null;
    }

    private string ObtenerWebRootPath()
    {
        if (!string.IsNullOrWhiteSpace(_env.WebRootPath))
            return _env.WebRootPath;

        return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    }

    private static string ObtenerNombreProveedor(RetencionGeneradaDetalleViewDto view)
    {
        if (!string.IsNullOrWhiteSpace(view.Proveedor?.nombre))
            return view.Proveedor.nombre.Trim();

        var nombreCompuesto = string.Join(" ", new[]
        {
            view.Proveedor?.primerNombre,
            view.Proveedor?.segundoNombre,
            view.Proveedor?.primerApellido,
            view.Proveedor?.segundoApellido
        }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();

        return !string.IsNullOrWhiteSpace(nombreCompuesto)
            ? nombreCompuesto
            : (view.RetencionInfo.IdCliente ?? "Proveedor");
    }
}
