using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Simetric.Components.Helpers;
using Simetric.Data;
using Simetric.Models;
using Simetric.Services;

namespace Simetric.Controllers;

[ApiController]
[Route("api/register")]
public sealed class RegisterController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ClienteService _clienteService;
    private readonly IEmailService _emailService;
    private readonly VendedorBackOfficeService _vendedorService;

    public RegisterController(
        IDbContextFactory<AppDbContext> dbFactory,
        ClienteService clienteService,
        IEmailService emailService,
        VendedorBackOfficeService vendedorService)
    {
        _dbFactory = dbFactory;
        _clienteService = clienteService;
        _emailService = emailService;
        _vendedorService = vendedorService;
    }

    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            if (request is null ||
                string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.Identificacion))
            {
                return BadRequest("Los datos de registro son obligatorios.");
            }

            var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
            var validationError = ValidarSolicitud(request, email);
            if (validationError is not null)
                return BadRequest(validationError);

            var nombres = request.TipoCliente == 2 ? request.RazonSocial.Trim() : request.Nombres.Trim();

            VendedorBackOffice? vendedor = null;
            var referenciaVendedor = ExtraerReferenciaVendedor(request.VendedorUrl);
            if (!string.IsNullOrWhiteSpace(referenciaVendedor))
            {
                vendedor = await _vendedorService.ResolverEnlaceRegistroAsync(referenciaVendedor);
                if (vendedor is null)
                    return BadRequest("La URL del aliado o vendedor no es válida o está inactiva.");
            }

            const string avatarUrl = "images/Avatars/numi-efact.jpg";

            var usuarioId = 0;
            await using var strategyDb = await _dbFactory.CreateDbContextAsync();
            var executionStrategy = strategyDb.Database.CreateExecutionStrategy();

            await executionStrategy.ExecuteAsync(async () =>
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                await using var transaction = await db.Database.BeginTransactionAsync();

                var usuarioExistente = await db.Usuarios
                    .AsNoTracking()
                    .Where(u => u.Email == email)
                    .Select(u => new { u.IdUsuario })
                    .FirstOrDefaultAsync();

                if (usuarioExistente is not null)
                {
                    throw new UsuarioYaRegistradoException(
                        usuarioExistente.IdUsuario,
                        vendedor?.IdVendedor,
                        "Esta persona ya tiene una cuenta activa de e-fact.");
                }

                var usuario = new Usuario
                {
                    Nombres = request.TipoCliente == 2 ? request.RazonSocial.Trim() : request.Nombres.Trim(),
                    Apellidos = request.TipoCliente == 2 ? string.Empty : (request.Apellidos ?? string.Empty).Trim(),
                    NombreEmpresa = request.TipoCliente == 2 ? request.RazonSocial.Trim() : string.Empty,
                    Email = email,
                    DireccionEmpresa = request.Direccion?.Trim(),
                    AvatarUrl = avatarUrl,
                    Celular = request.Celular?.Trim(),
                    Identificacion = request.Identificacion.Trim(),
                    IdTipoIdentificacion = request.TipoDocumento?.Trim().ToUpperInvariant() switch
                    {
                        "RUC" => 2,
                        "PASAPORTE" or "EXTERIOR" => 3,
                        _ => 1
                    },
                    PasswordHash = Simetric.Components.Helpers.SecurityHelper.HashPassword(request.Password),
                    IdTipoUsuario = 1,
                    Estado = true,
                    FechaCreacion = DateTime.Now,
                    SaldoDocumentos = 5,
                    TipoCliente = request.TipoCliente,
                    IdVendedor = vendedor?.IdVendedor
                };

                db.Usuarios.Add(usuario);
                await db.SaveChangesAsync();
                await _clienteService.EnsureConsumidorFinalAsync(db, usuario.IdUsuario);
                await transaction.CommitAsync();
                usuarioId = usuario.IdUsuario;
            });

            try
            {
                await _emailService.EnviarCuentaCreadaAsync(email, nombres);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"La cuenta se creo, pero no se pudo enviar el correo de confirmacion: {ex.Message}");
            }

            return Ok(new
            {
                status = "ok",
                success = true,
                idUsuario = usuarioId,
                idVendedor = vendedor?.IdVendedor,
                message = "Cuenta creada correctamente. Ya puedes iniciar sesion."
            });
        }
        catch (UsuarioYaRegistradoException ex)
        {
            return BadRequest(new
            {
                status = "already_registered",
                success = false,
                idUsuario = ex.IdUsuario,
                idVendedor = ex.IdVendedor,
                message = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al registrar usuario desde API externa: {ex.Message}");
            return StatusCode(StatusCodes.Status500InternalServerError,
                "Error al registrar la cuenta. Intenta nuevamente en unos segundos.");
        }
    }

    private static string? ExtraerReferenciaVendedor(string? vendedorUrl)
    {
        if (string.IsNullOrWhiteSpace(vendedorUrl))
            return null;

        var valor = vendedorUrl.Trim();
        if (Uri.TryCreate(valor, UriKind.Absolute, out var uri) || Uri.TryCreate(valor, UriKind.Relative, out uri))
        {
            var query = QueryHelpers.ParseQuery(uri.Query);
            if (query.TryGetValue("vendedor", out var referencia) && !string.IsNullOrWhiteSpace(referencia))
                return referencia.ToString();
        }

        return valor;
    }

    private static string? ValidarSolicitud(RegisterRequest request, string email)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return "El email es obligatorio.";

        if (!new EmailAddressAttribute().IsValid(email))
            return "Formato de correo invalido.";

        if (string.IsNullOrWhiteSpace(request.Direccion))
            return "La dirección es obligatoria.";

        if (request.Direccion.Trim().Length is < 5 or > 100)
            return "La dirección debe tener entre 5 y 100 caracteres.";

        if (request.TipoCliente is not (1 or 2))
            return "Debe seleccionar persona natural o empresa.";

        var tipoDocumento = request.TipoDocumento?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(tipoDocumento))
            return "El tipo de documento es obligatorio.";

        if (tipoDocumento is not ("CEDULA" or "RUC" or "PASAPORTE" or "EXTERIOR"))
            return "El tipo de documento no es válido.";

        if (request.TipoCliente == 2 && tipoDocumento == "CEDULA")
            return "Una empresa debe registrarse con RUC o identificación del exterior.";

        if (string.IsNullOrWhiteSpace(request.Identificacion))
            return "La identificación es obligatoria.";

        var identificacion = request.Identificacion.Trim();
        if (tipoDocumento is "PASAPORTE" or "EXTERIOR")
        {
            if (identificacion.Length is < 3 or > 20 || !identificacion.All(char.IsLetterOrDigit))
                return "La identificación del exterior debe tener entre 3 y 20 letras o números.";
        }
        else if (tipoDocumento == "RUC")
        {
            if (identificacion.Length != 13)
                return "El RUC debe tener exactamente 13 dígitos.";

            if (!identificacion.All(char.IsDigit))
                return "El RUC debe contener únicamente dígitos.";

            if (request.TipoCliente == 2)
            {
                var tercerDigito = identificacion[2];
                var estructuraValida = tercerDigito switch
                {
                    '6' => identificacion[9..] != "0000",
                    '9' => identificacion[10..] != "000",
                    _ => false
                };

                if (!estructuraValida)
                    return "La identificación no es un RUC de empresa válido.";
            }
            else if (!identificacion.EndsWith("001", StringComparison.Ordinal) || !ValidarCedulaEcuatoriana(identificacion[..10]))
                return "La identificación no es un RUC ecuatoriano válido.";
        }
        else if (identificacion.Length != 10 || !ValidarCedulaEcuatoriana(identificacion))
        {
            return "La cédula ingresada no es una cédula ecuatoriana válida.";
        }

        if (string.IsNullOrWhiteSpace(request.Password))
            return "La contraseña es obligatoria.";

        if (!Regex.IsMatch(request.Password, @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$"))
            return "Mínimo 8 caracteres, 1 mayúscula, 1 minúscula, 1 número y 1 especial.";

        if (request.TipoCliente == 2)
        {
            if (string.IsNullOrWhiteSpace(request.RazonSocial))
                return "La razón social es obligatoria.";

            if (request.RazonSocial.Trim().Length < 3)
                return "La razón social debe tener al menos 3 caracteres.";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Nombres))
                return "Los nombres son obligatorios.";

            if (!Regex.IsMatch(request.Nombres.Trim(), @"^[a-zA-ZÀ-ÿ\s]{2,}$"))
                return "Ingresa nombres válidos.";

            if (string.IsNullOrWhiteSpace(request.Apellidos))
                return "Los apellidos son obligatorios.";

            if (!Regex.IsMatch(request.Apellidos.Trim(), @"^[a-zA-ZÀ-ÿ\s]{2,}$"))
                return "Ingresa apellidos válidos.";
        }

        return null;
    }

    private static bool ValidarCedulaEcuatoriana(string cedula)
    {
        if (cedula.Length != 10 || !cedula.All(char.IsDigit))
            return false;

        var provincia = int.Parse(cedula[..2]);
        var tercerDigito = int.Parse(cedula.Substring(2, 1));
        if (provincia is < 1 or > 24 || tercerDigito > 5)
            return false;

        int[] coeficientes = { 2, 1, 2, 1, 2, 1, 2, 1, 2 };
        var suma = 0;
        for (var i = 0; i < coeficientes.Length; i++)
        {
            var valor = int.Parse(cedula[i].ToString()) * coeficientes[i];
            suma += valor > 9 ? valor - 9 : valor;
        }

        var digitoCalculado = (10 - (suma % 10)) % 10;
        return digitoCalculado == int.Parse(cedula[9].ToString());
    }

    public sealed class RegisterRequest
    {
        public string Nombres { get; set; } = string.Empty;
        public string Apellidos { get; set; } = string.Empty;
        public string RazonSocial { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Direccion { get; set; } = string.Empty;
        public string Celular { get; set; } = string.Empty;
        public string? TipoDocumento { get; set; }
        public string Identificacion { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? VendedorUrl { get; set; }
        public int TipoCliente { get; set; }
    }

    private sealed class UsuarioYaRegistradoException : Exception
    {
        public int IdUsuario { get; }
        public int? IdVendedor { get; }

        public UsuarioYaRegistradoException(int idUsuario, int? idVendedor, string message)
            : base(message)
        {
            IdUsuario = idUsuario;
            IdVendedor = idVendedor;
        }
    }
}
