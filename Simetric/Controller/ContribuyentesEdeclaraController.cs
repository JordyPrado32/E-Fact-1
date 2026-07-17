using Microsoft.AspNetCore.Mvc;
using Simetric.Models;
using Simetric.Services;
using System.ComponentModel.DataAnnotations;

namespace Simetric.Controllers;

[ApiController]
[Route("api/contribuyentes-edeclara")]
public class ContribuyentesEdeclaraController : ControllerBase
{
    private readonly ContribuyenteEdeclaraService _service;
    private readonly ClienteService _clienteService; // reutilizamos lookups

    public ContribuyentesEdeclaraController(
        ContribuyenteEdeclaraService service,
        ClienteService clienteService)
    {
        _service = service;
        _clienteService = clienteService;
    }

    [HttpGet("limite-estado")]
    public async Task<IActionResult> ObtenerLimiteEstado([FromQuery] int userId)
    {
        if (userId <= 0) return BadRequest("userId requerido");
        var (excedido, limite, actual) = await _service.ValidarLimiteContribuyentesAsync(userId);
        return Ok(new { Excedido = excedido, Limite = limite, Actual = actual });
    }

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] int userId)
    {
        if (userId <= 0) return BadRequest("userId requerido");
        var lista = await _service.ObtenerContribuyentesAsync(userId);
        return Ok(lista.Select(MapToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> ObtenerUno(int id, [FromQuery] int userId)
    {
        var item = await _service.ObtenerPorIdAsync(id, userId);
        if (item is null) return NotFound();
        return Ok(MapToDto(item));
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromQuery] int userId, [FromBody] ContribuyenteUpsertDto dto)
    {
        if (userId <= 0) return BadRequest("userId requerido");
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // Validar límite de contribuyentes
        if (dto.Estado != false) // Por defecto es true
        {
            var (excedido, limite, actual) = await _service.ValidarLimiteContribuyentesAsync(userId);
            if (excedido)
            {
                return BadRequest($"Has alcanzado el límite de {limite} contribuyentes activos permitidos para tu plan actual ({actual}/{limite}).");
            }
        }

        var isDuplicate = await _service.ExisteIdentificacionAsync(dto.Numeroidentificacion, null, userId);
        if (isDuplicate)
        {
            return BadRequest("El número de identificación ya está registrado.");
        }

        var entidad = MapToEntity(dto, userId);
        var creado = await _service.CrearAsync(entidad);
        return Ok(MapToDto(creado));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromQuery] int userId, [FromBody] ContribuyenteUpsertDto dto)
    {
        if (userId <= 0) return BadRequest("userId requerido");
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var existente = await _service.ObtenerPorIdAsync(id, userId);
        if (existente is null) return NotFound();

        // Validar límite si se está intentando activar
        if (existente.Estado != true && dto.Estado == true)
        {
            var (excedido, limite, actual) = await _service.ValidarLimiteContribuyentesAsync(userId);
            if (excedido)
            {
                return BadRequest($"Has alcanzado el límite de {limite} contribuyentes activos permitidos para tu plan actual ({actual}/{limite}).");
            }
        }

        var isDuplicate = await _service.ExisteIdentificacionAsync(dto.Numeroidentificacion, id, userId);
        if (isDuplicate)
        {
            return BadRequest("El número de identificación ya está registrado.");
        }

        var entidad = MapToEntity(dto, userId);
        entidad.CodContribuyente = id;
        var ok = await _service.ActualizarAsync(entidad);
        return ok ? Ok() : NotFound();
    }

    [HttpPut("{id:int}/desactivar")]
    public async Task<IActionResult> Desactivar(int id, [FromQuery] int userId)
    {
        var ok = await _service.DesactivarAsync(id, userId);
        return ok ? Ok() : NotFound();
    }

    [HttpPut("{id:int}/activar")]
    public async Task<IActionResult> Activar(int id, [FromQuery] int userId)
    {
        // Validar límite antes de activar
        var (excedido, limite, actual) = await _service.ValidarLimiteContribuyentesAsync(userId);
        if (excedido)
        {
            return BadRequest($"Has alcanzado el límite de {limite} contribuyentes activos permitidos para tu plan actual ({actual}/{limite}).");
        }

        var ok = await _service.ActivarAsync(id, userId);
        return ok ? Ok() : NotFound();
    }

    private static ContribuyenteDto MapToDto(ContribuyenteEdeclare e)
    {
        var emails = (e.Correo ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var primaryEmail = emails.FirstOrDefault();
        var extraEmails = emails.Skip(1).ToList();

        return new()
        {
            CodContribuyente = e.CodContribuyente,
            Apellidos = e.Apellidos,
            Nombres = e.Nombres,
            Nombrecomercial = e.Nombrecomercial,
            Nombrerazonsocial = e.Nombrerazonsocial,
            Tipoidentificacion = e.Tipoidentificacion,
            Numeroidentificacion = e.Numeroidentificacion,
            Direccion = e.Direccion,
            Telefonoconvencional = e.Telefonoconvencional,
            Celular = e.Celular,
            Correo = primaryEmail,
            CorreosAdicionales = extraEmails,
            TipoCliente = e.TipoCliente,
            Oblgconta = e.Oblgconta,
            Estado = e.Estado,
            Observaciones = e.Observaciones,
            Pais = e.Pais,
            Provincia = e.Provincia,
            Ciudad = e.Ciudad,
            PersonaNatural = e.PersonaNatural,
            ContribuyenteEspecial = e.ContribuyenteEspecial,
            ActividadContribuyente = e.ActividadContribuyente,
            NumContribuyente = e.NumContribuyente,
            PeriodicidadIva = e.PeriodicidadIva,
            PeriodicidadRenta = e.PeriodicidadRenta,
            FechaDeclaracion = e.FechaDeclaracion,
        };
    }

    private static ContribuyenteEdeclare MapToEntity(ContribuyenteUpsertDto dto, int userId)
    {
        var emails = new List<string>();
        if (!string.IsNullOrWhiteSpace(dto.Correo)) emails.Add(dto.Correo.Trim());
        if (dto.CorreosAdicionales != null)
        {
            emails.AddRange(dto.CorreosAdicionales.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
        }
        var combinedEmail = string.Join(";", emails);

        return new()
        {
            Apellidos = dto.Apellidos,
            Nombres = dto.Nombres,
            Nombrecomercial = dto.Nombrecomercial,
            Nombrerazonsocial = dto.Nombrerazonsocial,
            Tipoidentificacion = dto.Tipoidentificacion ?? "05",
            Numeroidentificacion = dto.Numeroidentificacion,
            Direccion = dto.Direccion,
            Telefonoconvencional = dto.Telefonoconvencional,
            Celular = dto.Celular,
            Correo = combinedEmail,
            TipoCliente = dto.TipoCliente,
            Oblgconta = dto.Oblgconta,
            Estado = dto.Estado ?? true,
            Observaciones = dto.Observaciones,
            Pais = dto.Pais,
            Provincia = dto.Provincia,
            Ciudad = dto.Ciudad,
            PersonaNatural = dto.PersonaNatural,
            ContribuyenteEspecial = dto.ContribuyenteEspecial,
            ActividadContribuyente = dto.ActividadContribuyente,
            NumContribuyente = dto.NumContribuyente,
            PeriodicidadIva = dto.PeriodicidadIva,
            PeriodicidadRenta = dto.PeriodicidadRenta,
            FechaDeclaracion = dto.FechaDeclaracion,
            Usuario = userId,
        };
    }
}

public class ContribuyenteDto
{
    public int CodContribuyente { get; set; }
    public string? Apellidos { get; set; }
    public string? Nombres { get; set; }
    public string? Nombrecomercial { get; set; }
    public string? Nombrerazonsocial { get; set; }
    public string? Tipoidentificacion { get; set; }
    public string? Numeroidentificacion { get; set; }
    public string? Direccion { get; set; }
    public string? Telefonoconvencional { get; set; }
    public string? Celular { get; set; }
    public string? Correo { get; set; }
    public List<string> CorreosAdicionales { get; set; } = new();
    public int TipoCliente { get; set; }
    public string? Oblgconta { get; set; }
    public bool? Estado { get; set; }
    public string? Observaciones { get; set; }
    public int? Pais { get; set; }
    public int? Provincia { get; set; }
    public int? Ciudad { get; set; }
    public bool? PersonaNatural { get; set; }
    public bool? ContribuyenteEspecial { get; set; }
    public string? ActividadContribuyente { get; set; }
    public string? NumContribuyente { get; set; }
    public string? PeriodicidadIva { get; set; }
    public string? PeriodicidadRenta { get; set; }
    public DateOnly? FechaDeclaracion { get; set; }
}

public class ContribuyenteUpsertDto : IValidatableObject
{
    public string? Apellidos { get; set; }
    public string? Nombres { get; set; }
    public string? Nombrecomercial { get; set; }
    public string? Nombrerazonsocial { get; set; }

    [Required(ErrorMessage = "El tipo de identificación es obligatorio")]
    public string? Tipoidentificacion { get; set; }

    [Required(ErrorMessage = "El número de identificación es obligatorio")]
    public string? Numeroidentificacion { get; set; }

    [Required(ErrorMessage = "La dirección es obligatoria")]
    public string? Direccion { get; set; }
    public string? Telefonoconvencional { get; set; }

    [Required(ErrorMessage = "El celular es obligatorio")]
    public string? Celular { get; set; }

    [Required(ErrorMessage = "El correo electrónico es obligatorio")]
    public string? Correo { get; set; }
    public List<string> CorreosAdicionales { get; set; } = new();
    public int TipoCliente { get; set; }

    [Required(ErrorMessage = "El obligado a llevar contabilidad es obligatorio")]
    public string? Oblgconta { get; set; }
    public bool? Estado { get; set; } = true;
    public string? Observaciones { get; set; }
    public int? Pais { get; set; }
    public int? Provincia { get; set; }
    public int? Ciudad { get; set; }
    public bool? PersonaNatural { get; set; } = true;
    public bool? ContribuyenteEspecial { get; set; }
    public string? ActividadContribuyente { get; set; }
    public string? NumContribuyente { get; set; }

    [Required(ErrorMessage = "La periodicidad del IVA es obligatoria")]
    public string? PeriodicidadIva { get; set; }

    [Required(ErrorMessage = "La periodicidad de la Renta es obligatoria")]
    public string? PeriodicidadRenta { get; set; }

    [Required(ErrorMessage = "La fecha de declaración es obligatoria")]
    public DateOnly? FechaDeclaracion { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        Apellidos = Apellidos?.Trim();
        Nombres = Nombres?.Trim();
        Nombrecomercial = Nombrecomercial?.Trim();
        Nombrerazonsocial = Nombrerazonsocial?.Trim();
        Numeroidentificacion = Numeroidentificacion?.Trim();
        Direccion = Direccion?.Trim();
        Telefonoconvencional = Telefonoconvencional?.Trim();
        Celular = Celular?.Trim();
        Correo = Correo?.Trim();
        Oblgconta = Oblgconta?.Trim().ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(Numeroidentificacion) && !Numeroidentificacion.All(char.IsDigit))
        {
            yield return new ValidationResult("La identificación solo debe contener números.", new[] { nameof(Numeroidentificacion) });
        }

        if (PersonaNatural == true)
        {
            if (string.IsNullOrWhiteSpace(Apellidos))
            {
                yield return new ValidationResult("Los apellidos son obligatorios para persona natural.", new[] { nameof(Apellidos) });
            }
            else if (Apellidos.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
            {
                yield return new ValidationResult("Debe ingresar dos apellidos.", new[] { nameof(Apellidos) });
            }

            if (string.IsNullOrWhiteSpace(Nombres))
            {
                yield return new ValidationResult("Los nombres son obligatorios para persona natural.", new[] { nameof(Nombres) });
            }
            else if (Nombres.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
            {
                yield return new ValidationResult("Debe ingresar dos nombres.", new[] { nameof(Nombres) });
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Nombrerazonsocial))
            {
                yield return new ValidationResult("La razón social es obligatoria para persona jurídica.", new[] { nameof(Nombrerazonsocial) });
            }
            if (string.IsNullOrWhiteSpace(Nombrecomercial))
            {
                yield return new ValidationResult("El nombre comercial es obligatorio para persona jurídica.", new[] { nameof(Nombrecomercial) });
            }
        }

        if (!string.IsNullOrWhiteSpace(Correo))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(Correo, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                yield return new ValidationResult("Correo electrónico principal no válido. Debe tener el formato usuario@dominio.com.", new[] { nameof(Correo) });
            }
        }

        foreach (var extraEmail in CorreosAdicionales.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(extraEmail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                yield return new ValidationResult($"El correo adicional '{extraEmail}' no es válido. Debe tener el formato usuario@dominio.com.", new[] { nameof(CorreosAdicionales) });
            }
            if (string.Equals(extraEmail.Trim(), Correo, StringComparison.OrdinalIgnoreCase))
            {
                yield return new ValidationResult("El correo adicional no puede ser igual al correo principal.", new[] { nameof(CorreosAdicionales) });
            }
        }

        if (!string.IsNullOrWhiteSpace(Telefonoconvencional) && !System.Text.RegularExpressions.Regex.IsMatch(Telefonoconvencional, @"^\d{7,10}$"))
        {
            yield return new ValidationResult("El teléfono convencional debe tener entre 7 y 10 dígitos.", new[] { nameof(Telefonoconvencional) });
        }

        if (!string.IsNullOrWhiteSpace(Celular) && !System.Text.RegularExpressions.Regex.IsMatch(Celular, @"^\d{10}$"))
        {
            yield return new ValidationResult("El celular debe tener exactamente 10 dígitos.", new[] { nameof(Celular) });
        }
    }
}