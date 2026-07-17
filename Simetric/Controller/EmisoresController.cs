using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;
using Simetric.Services;
using System.Text.RegularExpressions;

namespace Simetric.Controllers
{
    [ApiController]
    [Route("api/emisores")]
    public class EmisoresController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly EmisorCertificadoValidator _emisorCertificadoValidator;

        public EmisoresController(
            AppDbContext context,
            EmisorCertificadoValidator emisorCertificadoValidator)
        {
            _context = context;
            _emisorCertificadoValidator = emisorCertificadoValidator;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int? idUsuario)
        {
            if (idUsuario == null || idUsuario <= 0)
                return BadRequest("Id de usuario requerido.");

            var idCuenta = await ObtenerIdCuentaEmisor(idUsuario.Value);
            if (idCuenta is null)
                return NotFound("Usuario no encontrado.");

            var data = await _context.Emisores
                .AsNoTracking()
                .Where(e => e.Estado == true && e.IdUsuario == idCuenta.Value && !e.EsEmisorSistema)
                .OrderByDescending(e => e.Codigo)
                .ToListAsync();

            data.ForEach(NormalizarEmisor);
            data.ForEach(OcultarClaveProtegida);
            return Ok(data);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, [FromQuery] int? idUsuario)
        {
            if (idUsuario == null || idUsuario <= 0)
                return BadRequest("Id de usuario requerido.");

            var idCuenta = await ObtenerIdCuentaEmisor(idUsuario.Value);
            if (idCuenta is null)
                return NotFound("Usuario no encontrado.");

            var emisor = await _context.Emisores
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Codigo == id && e.IdUsuario == idCuenta.Value && !e.EsEmisorSistema);

            if (emisor == null)
                return NotFound();

            NormalizarEmisor(emisor);
            OcultarClaveProtegida(emisor);
            return Ok(emisor);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Emisor model)
        {
            if (model == null)
                return BadRequest("Datos inválidos.");

            if (model.IdUsuario == null || model.IdUsuario <= 0)
                return BadRequest("El usuario del emisor es obligatorio.");

            var idCuenta = await ObtenerIdCuentaEmisor(model.IdUsuario.Value);
            if (idCuenta is null)
                return BadRequest("Usuario no encontrado.");

            NormalizarEmisor(model);
            model.CodEstablecimiento = NormalizarSerie(model.CodEstablecimiento, "001");
            model.CodPuntoEmision = NormalizarSerie(model.CodPuntoEmision, "001");
            model.Retenciones = NormalizarRespuesta(model.Retenciones, "NO");

            var error = ValidarEmisor(model);
            if (error != null)
                return BadRequest(error);

            var validacionCertificado = _emisorCertificadoValidator.Validar(model);
            if (!validacionCertificado.IsValid && validacionCertificado.TieneConfiguracion)
                return BadRequest(validacionCertificado.Message);

            // Solo se permite un emisor activo por cuenta principal.
            var existeEmisor = await _context.Emisores
                .AnyAsync(e => e.IdUsuario == idCuenta.Value && e.Estado == true && !e.EsEmisorSistema);

            if (existeEmisor)
                return BadRequest("Solo se permite registrar un emisor activo por cuenta.");

            model.Codigo = 0;
            model.Estado = true;
            model.IdUsuario = idCuenta.Value;
            model.EsEmisorSistema = false;

            try
            {
                _context.Emisores.Add(model);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.InnerException?.Message ?? ex.Message });
            }

            NormalizarEmisor(model);
            OcultarClaveProtegida(model);
            return Ok(model);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] Emisor model)
        {
            if (model == null)
                return BadRequest("Datos inválidos.");

            if (model.IdUsuario == null || model.IdUsuario <= 0)
                return BadRequest("El usuario del emisor es obligatorio.");

            var idCuenta = await ObtenerIdCuentaEmisor(model.IdUsuario.Value);
            if (idCuenta is null)
                return BadRequest("Usuario no encontrado.");

            var emisorDb = await _context.Emisores
                .FirstOrDefaultAsync(e => e.Codigo == id && e.IdUsuario == idCuenta.Value && !e.EsEmisorSistema);

            if (emisorDb == null)
                return NotFound();

            NormalizarEmisor(model);
            model.CodEstablecimiento = NormalizarSerie(model.CodEstablecimiento, "001");
            model.CodPuntoEmision = NormalizarSerie(model.CodPuntoEmision, "001");
            model.Retenciones = NormalizarRespuesta(model.Retenciones, "NO");

            var error = ValidarEmisor(model);
            if (error != null)
                return BadRequest(error);

            emisorDb.RazonSocial = model.RazonSocial;
            emisorDb.Ruc = model.Ruc;
            emisorDb.NomComercial = model.NomComercial;
            emisorDb.DirEstablecimiento = model.DirEstablecimiento;
            emisorDb.CodEstablecimiento = NormalizarSerie(model.CodEstablecimiento, "001");
            emisorDb.CodPuntoEmision = NormalizarSerie(model.CodPuntoEmision, "001");
            emisorDb.TipoEmision = model.TipoEmision;
            emisorDb.Resolusion = model.Resolusion;
            emisorDb.ContribuyenteEspecial = model.ContribuyenteEspecial;
            emisorDb.LlevaContabilidad = model.LlevaContabilidad;
            emisorDb.TipoAmbiente = model.TipoAmbiente;
            emisorDb.DireccionMatriz = model.DireccionMatriz;
            emisorDb.Token = model.Token;
            emisorDb.Retenciones = NormalizarRespuesta(model.Retenciones, "NO");
            emisorDb.RetIva = model.RetIva;
            emisorDb.RetFuente = model.RetFuente;
            emisorDb.ClaveInterna = model.ClaveInterna;
            emisorDb.LogoImagen = model.LogoImagen;
            emisorDb.PathCertificado = model.PathCertificado;
            emisorDb.Telefono = model.Telefono;
            emisorDb.TiempoEspera = model.TiempoEspera;
            emisorDb.Email = model.Email;
            emisorDb.Estado = model.Estado;

            emisorDb.IdUsuario = idCuenta.Value;
            emisorDb.EsEmisorSistema = false;

            try
            {
                if (model.EliminarClaveCertificado)
                {
                    emisorDb.ClaveCertificado = null;
                }
                else if (!string.IsNullOrWhiteSpace(model.ClaveCertificado))
                {
                    emisorDb.ClaveCertificado = model.ClaveCertificado;
                }

                var validacionCertificado = _emisorCertificadoValidator.Validar(emisorDb);
                if (!validacionCertificado.IsValid && validacionCertificado.TieneConfiguracion)
                    return BadRequest(validacionCertificado.Message);

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.InnerException?.Message ?? ex.Message });
            }

            return Ok();
        }

        [HttpPut("{id:int}/desactivar")]
        public async Task<IActionResult> Desactivar(int id, [FromQuery] int? idUsuario)
        {
            if (idUsuario == null || idUsuario <= 0)
                return BadRequest("Id de usuario requerido.");

            var idCuenta = await ObtenerIdCuentaEmisor(idUsuario.Value);
            if (idCuenta is null)
                return NotFound("Usuario no encontrado.");

            var emisor = await _context.Emisores
                .FirstOrDefaultAsync(e => e.Codigo == id && e.IdUsuario == idCuenta.Value && !e.EsEmisorSistema);

            if (emisor == null)
                return NotFound();

            emisor.Estado = false;
            await _context.SaveChangesAsync();
            return Ok();
        }

        private async Task<int?> ObtenerIdCuentaEmisor(int idUsuario)
        {
            var usuario = await _context.Usuarios
                .AsNoTracking()
                .Where(u => u.IdUsuario == idUsuario)
                .Select(u => new { u.IdUsuario, u.idJefe, u.estadoAsociado })
                .FirstOrDefaultAsync();

            if (usuario is null)
                return null;

            return usuario.estadoAsociado == true && usuario.idJefe is > 0
                ? usuario.idJefe.Value
                : usuario.IdUsuario;
        }

        private async Task<bool> EsUsuarioAsociado(int idUsuario)
        {
            var usuario = await _context.Usuarios
                .AsNoTracking()
                .Where(u => u.IdUsuario == idUsuario)
                .Select(u => new { u.idJefe, u.estadoAsociado })
                .FirstOrDefaultAsync();

            return usuario is not null && (usuario.estadoAsociado == true || usuario.idJefe is > 0);
        }

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

        private static string? ValidarEmisor(Emisor e)
        {
            if (string.IsNullOrWhiteSpace(e.RazonSocial) && string.IsNullOrWhiteSpace(e.NomComercial))
                return "Debes ingresar la razón social o el nombre comercial.";

            if (!string.IsNullOrWhiteSpace(e.RazonSocial) && e.RazonSocial.Length > 70)
                return "La razón social no puede exceder 70 caracteres.";

            if (string.IsNullOrWhiteSpace(e.Ruc))
                return "El RUC es obligatorio.";

            if (!e.Ruc.All(char.IsDigit) || e.Ruc.Length != 13)
                return "El RUC debe tener exactamente 13 dígitos.";

            if (!string.IsNullOrWhiteSpace(e.NomComercial) && e.NomComercial.Length > 100)
                return "El nombre comercial no puede exceder 100 caracteres.";
            if (!string.IsNullOrWhiteSpace(e.Email) && !e.Email.Contains("@"))
                return "El formato del correo electrónico no es válido.";

            if (e.Email?.Length > 50)
                return "El email no puede exceder 50 caracteres.";
            if (string.IsNullOrWhiteSpace(e.DirEstablecimiento))
                return "La dirección del establecimiento es obligatoria.";

            if (e.DirEstablecimiento.Length > 250)
                return "La dirección del establecimiento no puede exceder 250 caracteres.";

            if (string.IsNullOrWhiteSpace(e.DireccionMatriz))
                return "La dirección matriz es obligatoria.";

            if (e.DireccionMatriz.Length > 255)
                return "La dirección matriz no puede exceder 255 caracteres.";

            if (!string.IsNullOrWhiteSpace(e.ClaveInterna) && e.ClaveInterna.Length > 25)
                return "La clave interna no puede exceder 25 caracteres.";

            if (string.IsNullOrWhiteSpace(e.Telefono))
                return "El teléfono es obligatorio.";

            if (!e.Telefono.All(char.IsDigit) || e.Telefono.Length < 7 || e.Telefono.Length > 10)
                return "El teléfono debe tener entre 7 y 10 dígitos.";

            if (string.IsNullOrWhiteSpace(e.LlevaContabilidad))
                return "Debe seleccionar si lleva contabilidad.";

            if (e.LlevaContabilidad != "SI" && e.LlevaContabilidad != "NO")
                return "LlevaContabilidad solo permite SI o NO.";

            if (!string.IsNullOrWhiteSpace(e.PathCertificado) &&
                !string.Equals(Path.GetExtension(e.PathCertificado), ".p12", StringComparison.OrdinalIgnoreCase))
                return "El archivo de firma debe ser un certificado .p12 válido.";

            if (!string.IsNullOrWhiteSpace(e.PathCertificado) && e.PathCertificado.Length > 150)
                return "La ruta del certificado no puede exceder 150 caracteres.";

            if (!string.IsNullOrWhiteSpace(e.ClaveCertificado) && e.ClaveCertificado.Length > 50)
                return "La clave de la firma no puede exceder 50 caracteres.";

            if (!string.IsNullOrWhiteSpace(e.LogoImagen))
            {
                if (!e.LogoImagen.StartsWith("data:image/jpeg;base64,", StringComparison.OrdinalIgnoreCase) &&
                    !e.LogoImagen.StartsWith("data:image/png;base64,", StringComparison.OrdinalIgnoreCase))
                {
                    return "El logo debe ser una imagen JPG, JPEG o PNG válida en base64.";
                }

                var comaIndex = e.LogoImagen.IndexOf(',');
                if (comaIndex < 0)
                    return "El formato del logo no es válido.";

                var base64 = e.LogoImagen[(comaIndex + 1)..];
                if (string.IsNullOrWhiteSpace(base64))
                    return "El logo no contiene información válida.";

                if (base64.Length > 2_800_000)
                    return "El logo excede el tamaño máximo permitido de 2 MB.";
            }

            e.CodEstablecimiento = NormalizarSerie(e.CodEstablecimiento, "001");
            e.CodPuntoEmision = NormalizarSerie(e.CodPuntoEmision, "001");
            e.Retenciones = string.IsNullOrWhiteSpace(e.Retenciones) ? "NO" : e.Retenciones;

            return null;
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
            e.LogoImagen = e.LogoImagen?.Trim();
        e.PathCertificado = e.PathCertificado?.Trim().TrimStart('~', '/', '\\').Replace('\\', '/');
        if (!string.IsNullOrWhiteSpace(e.PathCertificado) && e.PathCertificado.StartsWith("App_Data/", StringComparison.OrdinalIgnoreCase))
            e.PathCertificado = e.PathCertificado["App_Data/".Length..];
            e.ClaveCertificado = e.ClaveCertificado?.Trim();
            e.Telefono = e.Telefono?.Trim();
            e.TiempoEspera = e.TiempoEspera?.Trim();
            
            e.Email = e.Email?.Trim(); // <--- Añadir esta línea
        }

        private static void OcultarClaveProtegida(Emisor e)
        {
            e.TieneClaveCertificadoConfigurada = !string.IsNullOrWhiteSpace(e.ClaveCertificado);
            e.ClaveCertificado = null;
        }
    }
}
