using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;
using Simetric.Services;
using System.Net.Mail;

namespace Simetric.Controllers;

[ApiController]
[Route("api/clientes")]
public class ClientesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ClienteService _clienteService;
    private readonly UbicacionEcuadorCatalogService _ubicacionEcuadorCatalogService;
    private readonly CedulaLookupService _cedulaLookupService;

    public ClientesController(
        AppDbContext context,
        ClienteService clienteService,
        UbicacionEcuadorCatalogService ubicacionEcuadorCatalogService,
        CedulaLookupService cedulaLookupService)
    {
        _context = context;
        _clienteService = clienteService;
        _ubicacionEcuadorCatalogService = ubicacionEcuadorCatalogService;
        _cedulaLookupService = cedulaLookupService;
    }

    private static bool IsValidUser(int userId) => userId > 0;

    private async Task EnsureDiasCreditoColumnAsync()
    {
        await _context.Database.ExecuteSqlRawAsync("""
IF COL_LENGTH('dbo.CLIENTES', 'DIAS_CREDITO') IS NULL
    ALTER TABLE [dbo].[CLIENTES] ADD [DIAS_CREDITO] INT NULL;
""");
    }

    public class ClienteUpsertDto
    {
        public string? Apellidos { get; set; }
        public string? Nombres { get; set; }
        public string? Nombrecomercial { get; set; }
        public string? Nombrerazonsocial { get; set; }
        public string? Numeroidentificacion { get; set; }
        public string? Direccion { get; set; }
        public string? Telefonoconvencional { get; set; }
        public string? Celular { get; set; }
        public string? Correo { get; set; }
        public int? DiasCredito { get; set; }
        public string? Observaciones { get; set; }
        public int TipoCliente { get; set; }
        public bool? Estado { get; set; } = true;
        public int? Pais { get; set; }
        public int? Provincia { get; set; }
        public int? Ciudad { get; set; }

        // Blazor envía el IDE_SEC
        public int? Tipoidentificacion { get; set; }
        public List<string> CorreosAdicionales { get; set; } = new();
        public string? Oblgconta { get; set; }
    }

    public class BulkImportResultDto
    {
        public int Creados { get; set; }
        public List<string> Errores { get; set; } = new();
    }


    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int userId, [FromQuery] bool incluirInactivos = false)
    {
        if (!IsValidUser(userId))
            return Unauthorized("Sesión no válida.");

        await EnsureDiasCreditoColumnAsync();

        // 1. Identificamos la jerarquía del usuario
        var usuarioInfo = await _context.Usuarios
            .Where(u => u.IdUsuario == userId)
            .Select(u => new { u.IdUsuario, u.idJefe })
            .FirstOrDefaultAsync();

        if (usuarioInfo == null)
            return NotFound("Usuario no encontrado.");

        // 2. Determinamos el ID del "dueño" de la cartera de clientes.
        // Si el usuario tiene un jefe asignado, usamos el ID del jefe.
        int ownerId = usuarioInfo.idJefe ?? usuarioInfo.IdUsuario;

        await _clienteService.EnsureConsumidorFinalAsync(ownerId);

        // 3. Filtramos los clientes por el ID del propietario (Jefe)
        var query = _context.Clientes
            .AsNoTracking()
            .Where(c => c.Usuario == ownerId); // ✅ Filtro jerárquico

        if (!incluirInactivos)
            query = query.Where(c => c.Estado == true);

        var data = await query
            .OrderByDescending(c => c.Codcliente)
            .Select(c => new
            {
                c.Codcliente,
                c.Apellidos,
                c.Nombres,
                c.Nombrecomercial,
                c.Nombrerazonsocial,
                c.Numeroidentificacion,
                c.Direccion,
                c.Telefonoconvencional,
                c.Celular,
                c.Correo,
                c.DiasCredito,

                CorreosAdicionales = _context.ClientesCorreos
                    .Where(cc => cc.CodCliente == c.Codcliente && cc.Estado == true)
                    .Select(cc => cc.Correo)
                    .ToList(),

                c.Observaciones,
                c.Oblgconta,
                c.TipoCliente,
                c.Estado,
                c.Pais,
                c.Provincia,
                c.Ciudad,
                Tipoidentificacion = _context.Identificacion
                    .Where(i => i.IdeCodigo == c.Tipoidentificacion)
                    .Select(i => i.IdeSec)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return Ok(data);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, [FromQuery] int userId)
    {
        if (!IsValidUser(userId))
            return Unauthorized("Sesión no válida.");

        await EnsureDiasCreditoColumnAsync();

        // 1. Identificar al dueño de la cartera
        var usuarioInfo = await _context.Usuarios
            .Where(u => u.IdUsuario == userId)
            .Select(u => new { u.IdUsuario, u.idJefe })
            .FirstOrDefaultAsync();

        if (usuarioInfo == null)
            return NotFound("Usuario no encontrado.");

        int ownerId = usuarioInfo.idJefe ?? usuarioInfo.IdUsuario;

        await _clienteService.EnsureConsumidorFinalAsync(ownerId);

        // 2. Buscar por ownerId en lugar de userId
        var c = await _context.Clientes
            .AsNoTracking()
            .Where(x => x.Codcliente == id && x.Usuario == ownerId) // ✅ Corregido
            .Select(x => new
            {
                x.Codcliente,
                x.Apellidos,
                x.Nombres,
                x.Nombrecomercial,
                x.Nombrerazonsocial,
                x.Numeroidentificacion,
                x.Direccion,
                x.Telefonoconvencional,
                x.Celular,
                x.Correo,
                x.DiasCredito,
                CorreosAdicionales = _context.ClientesCorreos
                    .Where(cc => cc.CodCliente == x.Codcliente && cc.Estado == true)
                    .Select(cc => cc.Correo)
                    .ToList(),
                x.Observaciones,
                x.Oblgconta,
                x.TipoCliente,
                x.Estado,
                x.Pais,
                x.Provincia,
                x.Ciudad,
                Tipoidentificacion = _context.Identificacion
                    .Where(i => i.IdeCodigo == x.Tipoidentificacion)
                    .Select(i => i.IdeSec)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        if (c is null)
            return NotFound();

        return Ok(c);
    }

    [HttpGet("consulta-identificacion")]
    public async Task<IActionResult> ConsultarIdentificacion([FromQuery] string identificacion, CancellationToken cancellationToken)
    {
        var resultado = await _cedulaLookupService.ConsultarAsync(identificacion, cancellationToken);
        return resultado.Success ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromQuery] int userId, [FromBody] ClienteUpsertDto dto)
    {
        if (!IsValidUser(userId))
            return Unauthorized("Sesión no válida.");

        await EnsureDiasCreditoColumnAsync();

        // 1. Identificar quién es el "dueño" (Jefe) de la cuenta
        var usuarioInfo = await _context.Usuarios
            .Where(u => u.IdUsuario == userId)
            .Select(u => new { u.IdUsuario, u.idJefe })
            .FirstOrDefaultAsync();

        if (usuarioInfo == null)
            return NotFound("Usuario no encontrado.");

        // El cliente siempre se asigna al Jefe (o al usuario mismo si ya es el jefe)
        int ownerId = usuarioInfo.idJefe ?? usuarioInfo.IdUsuario;

        await ResolverUbicacionEcuadorQuemada(dto, null, null);

        var error = await ValidarCliente(dto, ownerId);
        if (error is not null)
            return BadRequest(error);

        string? codigoReal = null;
        if (dto.Tipoidentificacion.HasValue)
        {
            codigoReal = await _context.Identificacion
                .Where(i => i.IdeSec == dto.Tipoidentificacion.Value)
                .Select(i => i.IdeCodigo)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(codigoReal))
                return BadRequest("El tipo de identificación seleccionado no es válido.");
        }

        NormalizarDto(dto);

        var entity = new Cliente
        {
            Apellidos = dto.Apellidos,
            Nombres = dto.Nombres,
            Nombrecomercial = dto.Nombrecomercial,
            Nombrerazonsocial = dto.Nombrerazonsocial,
            Numeroidentificacion = dto.Numeroidentificacion,
            Direccion = dto.Direccion,
            Telefonoconvencional = dto.Telefonoconvencional,
            Celular = dto.Celular,
            Correo = dto.Correo,
            DiasCredito = dto.DiasCredito,
            Observaciones = dto.Observaciones,
            Oblgconta = dto.Oblgconta,
            TipoCliente = dto.TipoCliente,
            Estado = dto.Estado ?? true,
            Pais = dto.Pais,
            Provincia = dto.Provincia,
            Ciudad = dto.Ciudad,
            Tipoidentificacion = codigoReal,
            Usuario = ownerId // ✅ CAMBIO: Se guarda el ID del Jefe para que sea compartido
        };

        _context.Clientes.Add(entity);
        await _context.SaveChangesAsync();

        // Manejar correos adicionales
        var correosExistentes = await _context.ClientesCorreos
            .Where(cc => cc.CodCliente == entity.Codcliente)
            .ToListAsync();

        _context.ClientesCorreos.RemoveRange(correosExistentes);

        if (dto.CorreosAdicionales != null)
        {
            foreach (var correoExtra in dto.CorreosAdicionales.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                _context.ClientesCorreos.Add(new ClienteCorreo
                {
                    CodCliente = entity.Codcliente,
                    Correo = correoExtra.Trim(),
                    Estado = true
                });
            }
        }

        await _context.SaveChangesAsync();

        return Ok(new { entity.Codcliente });
    }

    [HttpPost("bulk")]
    public async Task<ActionResult<BulkImportResultDto>> BulkCreate([FromQuery] int userId, [FromBody] List<ClienteUpsertDto> models)
    {
        if (!IsValidUser(userId))
            return Unauthorized("Sesión no válida.");

        await EnsureDiasCreditoColumnAsync();

        var usuarioInfo = await _context.Usuarios
            .Where(u => u.IdUsuario == userId)
            .Select(u => new { u.IdUsuario, u.idJefe })
            .FirstOrDefaultAsync();

        if (usuarioInfo == null)
            return NotFound("Usuario no encontrado.");

        int ownerId = usuarioInfo.idJefe ?? usuarioInfo.IdUsuario;

        var result = new BulkImportResultDto();
        if (models == null || !models.Any())
        {
            return Ok(result);
        }

        // Load existing identifications and names to prevent duplicates
        var existingClients = await _context.Clientes
            .Where(c => c.Usuario == ownerId && c.Estado == true)
            .Select(c => new { c.Numeroidentificacion, c.Apellidos, c.Nombres, c.Nombrerazonsocial, c.Correo })
            .ToListAsync();

        var existingIdSet = new HashSet<string>(
            existingClients.Where(c => c.Numeroidentificacion != null).Select(c => c.Numeroidentificacion!.Trim()), 
            StringComparer.OrdinalIgnoreCase);

        var existingEmailSet = new HashSet<string>(
            existingClients.Where(c => c.Correo != null).Select(c => c.Correo!.Trim()), 
            StringComparer.OrdinalIgnoreCase);

        var existingNamesSet = new HashSet<string>(
            existingClients.Select(c => {
                var fullName = !string.IsNullOrWhiteSpace(c.Nombrerazonsocial) 
                    ? c.Nombrerazonsocial 
                    : $"{c.Apellidos} {c.Nombres}";
                return RemoveAccents(fullName).Trim().ToLowerInvariant();
            }).Where(n => !string.IsNullOrEmpty(n))
        );

        var tempIdsLote = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tempNamesLote = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tempEmailsLote = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var identificacionesLookups = await _context.Identificacion.ToListAsync();
        var tiposClienteLookups = await _context.Tipoclientes.ToListAsync();

        // Pre-load all provinces and cities once to avoid N DB queries per row
        var todasLasProvincias = await _context.Provincias.ToListAsync();
        var todasLasCiudades = await _context.Ciudades.ToListAsync();

        var savedEntities = new List<(Cliente Entity, List<string> Correos)>();

        for (int i = 0; i < models.Count; i++)
        {
            var dto = models[i];
            var fila = i + 2; // Fila real en CSV (la 1 es la cabecera)

            if (dto == null) continue;

            NormalizarDto(dto);

            // Validar campos obligatorios
            var valError = ValidarClienteOptimizado(dto, identificacionesLookups, tiposClienteLookups);
            if (valError is not null)
            {
                result.Errores.Add($"Fila {fila}: {valError}");
                continue;
            }

            // Validar duplicado de identificación (insensible a mayúsculas/minúsculas)
            if (!string.IsNullOrWhiteSpace(dto.Numeroidentificacion))
            {
                var cleanId = dto.Numeroidentificacion.Trim().ToLowerInvariant();
                if (existingIdSet.Contains(cleanId) || tempIdsLote.Contains(cleanId))
                {
                    result.Errores.Add($"Fila {fila}: Identificación duplicada. Ya existe un cliente registrado con la cédula/RUC '{dto.Numeroidentificacion}'.");
                    continue;
                }
                tempIdsLote.Add(cleanId);
            }

            // Validar duplicado de correo electrónico (insensible a mayúsculas/minúsculas)
            if (!string.IsNullOrWhiteSpace(dto.Correo))
            {
                var cleanEmail = dto.Correo.Trim().ToLowerInvariant();
                if (existingEmailSet.Contains(cleanEmail) || tempEmailsLote.Contains(cleanEmail))
                {
                    result.Errores.Add($"Fila {fila}: Correo electrónico duplicado. Ya existe un cliente registrado con el correo '{dto.Correo}'.");
                    continue;
                }
                tempEmailsLote.Add(cleanEmail);
            }

            // Validar duplicado de nombre/razón social (insensible a tildes/mayúsculas/minúsculas)
            var modelName = !string.IsNullOrWhiteSpace(dto.Nombrerazonsocial) 
                ? dto.Nombrerazonsocial 
                : $"{dto.Apellidos} {dto.Nombres}";
            var normalizedModelName = RemoveAccents(modelName).Trim().ToLowerInvariant();
            if (existingNamesSet.Contains(normalizedModelName) || tempNamesLote.Contains(normalizedModelName))
            {
                result.Errores.Add($"Fila {fila}: Nombre duplicado. Ya existe un cliente registrado con el nombre o razón social '{modelName}'.");
                continue;
            }
            tempNamesLote.Add(normalizedModelName);

            // Map Tipoidentificacion
            string? codigoReal = null;
            if (dto.Tipoidentificacion.HasValue)
            {
                codigoReal = identificacionesLookups
                    .Where(ide => ide.IdeSec == dto.Tipoidentificacion.Value)
                    .Select(ide => ide.IdeCodigo)
                    .FirstOrDefault();

                if (string.IsNullOrWhiteSpace(codigoReal))
                {
                    result.Errores.Add($"Fila {fila}: El tipo de identificación seleccionado no es válido.");
                    continue;
                }
            }

            await ResolverUbicacionEcuadorQuemada(dto, todasLasProvincias, todasLasCiudades);

            var entity = new Cliente
            {
                Apellidos = dto.Apellidos,
                Nombres = dto.Nombres,
                Nombrecomercial = dto.Nombrecomercial,
                Nombrerazonsocial = dto.Nombrerazonsocial,
                Numeroidentificacion = dto.Numeroidentificacion,
                Direccion = dto.Direccion,
                Telefonoconvencional = dto.Telefonoconvencional,
                Celular = dto.Celular,
                Correo = dto.Correo,
                DiasCredito = dto.DiasCredito,
                Observaciones = dto.Observaciones,
                Oblgconta = dto.Oblgconta,
                TipoCliente = dto.TipoCliente,
                Estado = dto.Estado ?? true,
                Pais = dto.Pais,
                Provincia = dto.Provincia,
                Ciudad = dto.Ciudad,
                Tipoidentificacion = codigoReal,
                Usuario = ownerId
            };

            _context.Clientes.Add(entity);

            var correosFiltrados = dto.CorreosAdicionales?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (correosFiltrados != null && correosFiltrados.Any())
            {
                savedEntities.Add((entity, correosFiltrados));
            }

            result.Creados++;
        }

        if (result.Creados > 0)
        {
            await _context.SaveChangesAsync();

            // Insert child email entities now that primary keys are generated
            foreach (var item in savedEntities)
            {
                foreach (var correoExtra in item.Correos)
                {
                    _context.ClientesCorreos.Add(new ClienteCorreo
                    {
                        CodCliente = item.Entity.Codcliente,
                        Correo = correoExtra.Trim(),
                        Estado = true
                    });
                }
            }

            if (savedEntities.Any())
            {
                await _context.SaveChangesAsync();
            }
        }

        return Ok(result);
    }

    private static string RemoveAccents(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
        var stringBuilder = new System.Text.StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }


    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromQuery] int userId, [FromBody] ClienteUpsertDto dto)
    {
        if (!IsValidUser(userId))
            return Unauthorized("Sesión no válida.");

        await EnsureDiasCreditoColumnAsync();

        // 1. Identificar la jerarquía del usuario actual
        var usuarioInfo = await _context.Usuarios
            .Where(u => u.IdUsuario == userId)
            .Select(u => new { u.IdUsuario, u.idJefe })
            .FirstOrDefaultAsync();

        if (usuarioInfo == null)
            return NotFound("Usuario no encontrado.");

        // El "dueño" de los datos siempre es el Jefe
        int ownerId = usuarioInfo.idJefe ?? usuarioInfo.IdUsuario;

        // 2. Buscar el cliente asegurando que pertenezca al grupo (ownerId)
        var cliente = await _context.Clientes
            .FirstOrDefaultAsync(x => x.Codcliente == id && x.Usuario == ownerId); // ✅ Filtro por ownerId

        if (cliente is null)
            return NotFound("El cliente no existe o no pertenece a su grupo de trabajo.");

        await ResolverUbicacionEcuadorQuemada(dto, null, null);

        var error = await ValidarCliente(dto, ownerId, clienteIdExistente: id);
        if (error is not null)
            return BadRequest(error);

        string? codigoReal = null;
        if (dto.Tipoidentificacion.HasValue)
        {
            codigoReal = await _context.Identificacion
                .Where(i => i.IdeSec == dto.Tipoidentificacion.Value)
                .Select(i => i.IdeCodigo)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(codigoReal))
                return BadRequest("El tipo de identificación seleccionado no es válido.");
        }

        NormalizarDto(dto);

        // Actualización de campos
        cliente.Apellidos = dto.Apellidos;
        cliente.Nombres = dto.Nombres;
        cliente.Nombrecomercial = dto.Nombrecomercial;
        cliente.Nombrerazonsocial = dto.Nombrerazonsocial;
        cliente.Numeroidentificacion = dto.Numeroidentificacion;
        cliente.Direccion = dto.Direccion;
        cliente.Telefonoconvencional = dto.Telefonoconvencional;
        cliente.Celular = dto.Celular;
        cliente.Correo = dto.Correo;
        cliente.DiasCredito = dto.DiasCredito;
        cliente.Observaciones = dto.Observaciones;
        cliente.Oblgconta = dto.Oblgconta;
        cliente.TipoCliente = dto.TipoCliente;
        cliente.Pais = dto.Pais;
        cliente.Provincia = dto.Provincia;
        cliente.Ciudad = dto.Ciudad;
        cliente.Tipoidentificacion = codigoReal;

        if (dto.Estado is not null)
            cliente.Estado = dto.Estado;

        // ✅ IMPORTANTE: Mantener el cliente asignado al ownerId (Jefe)
        // No lo cambies al userId del asociado que edita, para que no se pierda el acceso grupal.
        cliente.Usuario = ownerId;

        // Manejo de correos adicionales (borrón y cuenta nueva)
        var correosExistentes = await _context.ClientesCorreos
            .Where(cc => cc.CodCliente == cliente.Codcliente)
            .ToListAsync();

        _context.ClientesCorreos.RemoveRange(correosExistentes);

        if (dto.CorreosAdicionales != null)
        {
            foreach (var correoExtra in dto.CorreosAdicionales.Where(cor => !string.IsNullOrWhiteSpace(cor)))
            {
                _context.ClientesCorreos.Add(new ClienteCorreo
                {
                    CodCliente = cliente.Codcliente,
                    Correo = correoExtra.Trim(),
                    Estado = true
                });
            }
        }

        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpPut("{id:int}/desactivar")]
    public async Task<IActionResult> Desactivar(int id, [FromQuery] int userId)
    {
        if (!IsValidUser(userId)) return Unauthorized();

        var ownerId = await GetOwnerIdInternal(userId); // Ver método auxiliar abajo
        var c = await _context.Clientes.FirstOrDefaultAsync(x => x.Codcliente == id && x.Usuario == ownerId);

        if (c is null) return NotFound();
        c.Estado = false;
        await _context.SaveChangesAsync();
        return Ok();
    }
    private async Task<int> GetOwnerIdInternal(int userId)
    {
        var info = await _context.Usuarios
            .Where(u => u.IdUsuario == userId)
            .Select(u => new { u.IdUsuario, u.idJefe })
            .FirstOrDefaultAsync();

        return info?.idJefe ?? userId;
    }
    [HttpPut("{id:int}/activar")]
    public async Task<IActionResult> Activar(int id, [FromQuery] int userId)
    {
        if (!IsValidUser(userId)) return Unauthorized();

        var ownerId = await GetOwnerIdInternal(userId);
        var c = await _context.Clientes.FirstOrDefaultAsync(x => x.Codcliente == id && x.Usuario == ownerId);

        if (c is null) return NotFound();
        c.Estado = true;
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteFisico(int id, [FromQuery] int userId)
    {
        if (!IsValidUser(userId)) return Unauthorized();

        var ownerId = await GetOwnerIdInternal(userId);
        var c = await _context.Clientes.FirstOrDefaultAsync(x => x.Codcliente == id && x.Usuario == ownerId);

        if (c is null) return NotFound();
        _context.Clientes.Remove(c);
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("lookups")]
    public async Task<IActionResult> Lookups()
    {
        await PaisCatalogoService.AsegurarCatalogoAsync(_context);

        var tipos = await _context.Tipoclientes
            .AsNoTracking()
            .OrderBy(t => t.TclCodigo)
            .Select(t => new
            {
                tclCodigo = t.TclCodigo,
                descripcion = t.TclDescripcion
            })
            .ToListAsync();

        var paises = await _context.Paises
            .AsNoTracking()
            .OrderBy(p => p.Descripcion)
            .Select(p => new
            {
                idPais = p.IdPais,
                descripcion = p.Descripcion
            })
            .ToListAsync();

        var identificaciones = await _context.Identificacion
            .AsNoTracking()
            .OrderBy(i => i.IdeSec)
            .Select(i => new
            {
                ideSec = i.IdeSec,
                ideCodigo = i.IdeCodigo,
                ideDescripcion = i.IdeDescripcion
            })
            .ToListAsync();

        return Ok(new { tipos, paises, identificaciones });
    }

    [HttpGet("provincias")]
    public async Task<IActionResult> Provincias([FromQuery] int paisId)
    {
        if (paisId <= 0)
            return BadRequest();

        var provincias = await _context.Provincias
            .AsNoTracking()
            .Where(x => x.IdPais == paisId)
            .OrderBy(x => x.Descripcion)
            .Select(x => new
            {
                idProvincia = x.IdProvincia,
                descripcion = x.Descripcion
            })
            .ToListAsync();

        return Ok(provincias);
    }

    [HttpGet("ciudades")]
    public async Task<IActionResult> Ciudades([FromQuery] int provinciaId)
    {
        if (provinciaId <= 0)
            return BadRequest();

        var ciudades = await _context.Ciudades
            .AsNoTracking()
            .Where(x => x.IdProvincia == provinciaId)
            .OrderBy(x => x.Descripcion)
            .Select(x => new
            {
                idCiudad = x.IdCiudad,
                descripcion = x.Descripcion
            })
            .ToListAsync();

        return Ok(ciudades);
    }

    [HttpGet("ubicacion-ecuador")]
    public async Task<IActionResult> UbicacionEcuador()
    {
        var idPaisEcuador = await _context.Paises
            .AsNoTracking()
            .Where(p => p.Descripcion != null && p.Descripcion.Trim().ToUpper() == "ECUADOR")
            .Select(p => p.IdPais)
            .FirstOrDefaultAsync();

        if (idPaisEcuador <= 0)
            return NotFound("No se encontró el catálogo de Ecuador.");

        await _ubicacionEcuadorCatalogService.EnsureCatalogoAsync();

        var provincias = await _context.Provincias
            .AsNoTracking()
            .Where(x => x.IdPais == idPaisEcuador)
            .OrderBy(x => x.Descripcion)
            .Select(x => new
            {
                idProvincia = x.IdProvincia,
                descripcion = x.Descripcion,
                idPais = x.IdPais
            })
            .ToListAsync();

        var provinciaIds = provincias.Select(x => x.idProvincia).ToList();

        var ciudades = await _context.Ciudades
            .AsNoTracking()
            .Where(x => x.IdProvincia.HasValue && provinciaIds.Contains(x.IdProvincia.Value))
            .OrderBy(x => x.Descripcion)
            .Select(x => new
            {
                idCiudad = x.IdCiudad,
                descripcion = x.Descripcion,
                idProvincia = x.IdProvincia
            })
            .ToListAsync();

        return Ok(new
        {
            idPais = idPaisEcuador,
            provincias,
            ciudades
        });
    }

    private async Task ResolverUbicacionEcuadorQuemada(ClienteUpsertDto dto,
        List<Simetric.Models.Provincia>? todasLasProvincias,
        List<Simetric.Models.Ciudad>? todasLasCiudades)
    {
        if (dto.Provincia >= 0 && dto.Ciudad >= 0)
            return;

        await _ubicacionEcuadorCatalogService.EnsureCatalogoAsync();

        int provinciaIndex = -1;

        if (UbicacionEcuadorCatalogService.TryGetProvinciaCatalogo(dto.Provincia, out var provinciaCatalogo, out provinciaIndex))
        {
            var provinciasPais = todasLasProvincias != null
                ? todasLasProvincias.Where(x => x.IdPais == dto.Pais).ToList()
                : await _context.Provincias.Where(x => x.IdPais == dto.Pais).ToListAsync();

            var provinciaReal = provinciasPais.FirstOrDefault(x =>
                UbicacionEcuadorCatalogService.NormalizarClaveUbicacion(x.Descripcion) ==
                UbicacionEcuadorCatalogService.NormalizarClaveUbicacion(provinciaCatalogo.Nombre));

            if (provinciaReal is not null)
                dto.Provincia = provinciaReal.IdProvincia;
        }

        if (UbicacionEcuadorCatalogService.TryGetCiudadCatalogo(dto.Ciudad, out var ciudadCatalogo, out var ciudadProvinciaIndex, out _) &&
            ciudadProvinciaIndex == provinciaIndex &&
            dto.Provincia.HasValue &&
            dto.Provincia > 0)
        {
            var ciudadesProvincia = todasLasCiudades != null
                ? todasLasCiudades.Where(x => x.IdProvincia == dto.Provincia.Value).ToList()
                : await _context.Ciudades.Where(x => x.IdProvincia == dto.Provincia.Value).ToListAsync();

            var ciudadReal = ciudadesProvincia.FirstOrDefault(x =>
                UbicacionEcuadorCatalogService.NormalizarClaveUbicacion(x.Descripcion) ==
                UbicacionEcuadorCatalogService.NormalizarClaveUbicacion(ciudadCatalogo));

            if (ciudadReal is not null)
                dto.Ciudad = ciudadReal.IdCiudad;
        }
    }

    private async Task<string?> ValidarCliente(ClienteUpsertDto dto, int ownerId, int? clienteIdExistente = null, bool checkDuplicatesInDb = true)
    {
        if (dto is null)
            return "Datos no válidos.";

        // Normalizar RUC/Cédula scientific notation and symbols
        if (dto.Tipoidentificacion.HasValue && dto.Tipoidentificacion.Value > 0)
        {
            var ideLookup = await _context.Identificacion.FirstOrDefaultAsync(i => i.IdeSec == dto.Tipoidentificacion.Value);
            if (ideLookup != null)
            {
                var normCodigoReal = ideLookup.IdeCodigo;
                var normDescReal = ideLookup.IdeDescripcion ?? "";

                bool normEsCedula = string.Equals(normCodigoReal, "05", StringComparison.OrdinalIgnoreCase) ||
                                    normDescReal.Contains("cédula", StringComparison.OrdinalIgnoreCase) ||
                                    normDescReal.Contains("cedula", StringComparison.OrdinalIgnoreCase);

                bool normEsRuc = string.Equals(normCodigoReal, "04", StringComparison.OrdinalIgnoreCase) ||
                                 normDescReal.Contains("RUC", StringComparison.OrdinalIgnoreCase);

                if (normEsRuc || normEsCedula)
                {
                    if (dto.Numeroidentificacion != null)
                    {
                        var rawId = dto.Numeroidentificacion.Trim();
                        // Parse scientific notation (e.g. 1.79E+12 or 1,79E+12)
                        if (rawId.Contains('E', StringComparison.OrdinalIgnoreCase))
                        {
                            var normalizedStr = rawId.Replace(',', '.');
                            if (double.TryParse(normalizedStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedDouble))
                            {
                                rawId = parsedDouble.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
                            }
                        }

                        // Clean trailing decimals (e.g. 1701042960.0)
                        var normalizedDecimal = rawId.Replace(',', '.');
                        if (double.TryParse(normalizedDecimal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedNum))
                        {
                            if (parsedNum % 1 == 0)
                            {
                                rawId = parsedNum.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
                            }
                        }

                        // Strip non-digits
                        dto.Numeroidentificacion = new string(rawId.Where(char.IsDigit).ToArray());
                    }
                }
            }
        }

        NormalizarDto(dto);

        if (dto.TipoCliente <= 0)
            return "El Tipo de cliente (Natural o Juridica) es obligatorio.";

        if (!dto.Tipoidentificacion.HasValue || dto.Tipoidentificacion <= 0)
            return "El Tipo de identificación (Cedula o RUC) es obligatorio.";

        var ide = await _context.Identificacion.FirstOrDefaultAsync(i => i.IdeSec == dto.Tipoidentificacion.Value);
        if (ide == null)
            return "El tipo de identificación seleccionado es inválido o no existe.";

        var codigoReal = ide.IdeCodigo;
        var descReal = ide.IdeDescripcion ?? "";

        bool esCedula = string.Equals(codigoReal, "05", StringComparison.OrdinalIgnoreCase) ||
                         descReal.Contains("cédula", StringComparison.OrdinalIgnoreCase) ||
                         descReal.Contains("cedula", StringComparison.OrdinalIgnoreCase);

        bool esRuc = string.Equals(codigoReal, "04", StringComparison.OrdinalIgnoreCase) ||
                      descReal.Contains("RUC", StringComparison.OrdinalIgnoreCase);

        bool esPasaporte = string.Equals(codigoReal, "06", StringComparison.OrdinalIgnoreCase) ||
                            descReal.Contains("pasaporte", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(dto.Numeroidentificacion) && dto.Numeroidentificacion.All(char.IsDigit))
        {
            if (esCedula && dto.Numeroidentificacion.Length == 9)
            {
                dto.Numeroidentificacion = "0" + dto.Numeroidentificacion;
            }
            else if (esRuc && dto.Numeroidentificacion.Length == 12)
            {
                dto.Numeroidentificacion = "0" + dto.Numeroidentificacion;
            }

            // Auto-reconstruct RUC numbers truncated/rounded by Excel scientific notation (e.g. ending in 000)
            if (esRuc && dto.Numeroidentificacion.Length == 13 && dto.Numeroidentificacion.EndsWith("000"))
            {
                var ruc = dto.Numeroidentificacion;
                var tercerDigito = int.Parse(ruc.Substring(2, 1), System.Globalization.CultureInfo.InvariantCulture);
                if (tercerDigito == 9) // Private legal entity (RUC de Sociedad Privada/Extranjera sin cédula)
                {
                    int[] coeficientes = { 4, 3, 2, 7, 6, 5, 4, 3, 2 };
                    var suma = 0;
                    for (var idx = 0; idx < coeficientes.Length; idx++)
                        suma += int.Parse(ruc[idx].ToString(), System.Globalization.CultureInfo.InvariantCulture) * coeficientes[idx];
                    var residuo = suma % 11;
                    var digitoCalculado = residuo == 0 ? 0 : 11 - residuo;
                    dto.Numeroidentificacion = ruc[..9] + digitoCalculado + "001";
                }
                else if (tercerDigito <= 5) // Natural person RUC
                {
                    int[] coeficientes = { 2, 1, 2, 1, 2, 1, 2, 1, 2 };
                    var suma = 0;
                    for (var idx = 0; idx < 9; idx++)
                    {
                        var valor = int.Parse(ruc[idx].ToString(), System.Globalization.CultureInfo.InvariantCulture) * coeficientes[idx];
                        suma += valor > 9 ? valor - 9 : valor;
                    }
                    var digitoCalculado = (10 - (suma % 10)) % 10;
                    dto.Numeroidentificacion = ruc[..9] + digitoCalculado + "001";
                }
                else if (tercerDigito == 6) // Public entity RUC
                {
                    int[] coeficientes = { 3, 2, 7, 6, 5, 4, 3, 2 };
                    var suma = 0;
                    for (var idx = 0; idx < coeficientes.Length; idx++)
                        suma += int.Parse(ruc[idx].ToString(), System.Globalization.CultureInfo.InvariantCulture) * coeficientes[idx];
                    var residuo = suma % 11;
                    var digitoCalculado = residuo == 0 ? 0 : 11 - residuo;
                    dto.Numeroidentificacion = ruc[..8] + digitoCalculado + "0001";
                }
            }
        }

        if (esCedula)
        {
            if (string.IsNullOrWhiteSpace(dto.Numeroidentificacion))
                return "La identificación (cédula) es obligatoria.";

            if (!dto.Numeroidentificacion.All(char.IsDigit))
                return "La cédula debe contener únicamente dígitos numéricos.";

            if (dto.Numeroidentificacion.Length != 10)
                return $"Cédula inválida. La cédula debe tener exactamente 10 dígitos (se recibieron {dto.Numeroidentificacion.Length} dígitos).";

            if (!ValidarCedulaEcuatoriana(dto.Numeroidentificacion))
                return "Cédula inválida. No supera el algoritmo de validación del dígito verificador ecuatoriano.";
        }
        else if (esRuc)
        {
            if (string.IsNullOrWhiteSpace(dto.Numeroidentificacion))
                return "La identificación (RUC) es obligatoria.";

            if (!dto.Numeroidentificacion.All(char.IsDigit))
                return "El RUC debe contener únicamente dígitos numéricos.";

            if (dto.Numeroidentificacion.Length != 13)
                return $"RUC inválido. El RUC debe tener exactamente 13 dígitos (se recibieron {dto.Numeroidentificacion.Length} dígitos).";

            if (!ValidarRucEcuatoriano(dto.Numeroidentificacion))
                return "RUC inválido. No supera el algoritmo de validación del dígito verificador del SRI.";
        }
        else if (esPasaporte)
        {
            if (string.IsNullOrWhiteSpace(dto.Numeroidentificacion))
                return "La identificación (Pasaporte) es obligatoria.";

            if (!dto.Numeroidentificacion.All(char.IsLetterOrDigit))
                return "El pasaporte debe contener únicamente caracteres alfanuméricos (letras y números).";

            if (dto.Numeroidentificacion.Length < 3 || dto.Numeroidentificacion.Length > 20)
                return $"El pasaporte debe tener una longitud de entre 3 y 20 caracteres (se recibieron {dto.Numeroidentificacion.Length} caracteres).";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(dto.Numeroidentificacion))
                return "La identificación es obligatoria.";

            if (dto.Numeroidentificacion.Length < 3 || dto.Numeroidentificacion.Length > 20)
                return $"La identificación debe tener una longitud de entre 3 y 20 caracteres (se recibieron {dto.Numeroidentificacion.Length} caracteres).";
        }

        if (checkDuplicatesInDb)
        {
            // Validar duplicado de identificación (insensible a mayúsculas/minúsculas)
            if (!string.IsNullOrWhiteSpace(dto.Numeroidentificacion))
            {
                var idBuscado = dto.Numeroidentificacion.Trim().ToLowerInvariant();
                var duplicateId = await _context.Clientes
                    .AnyAsync(c => c.Usuario == ownerId &&
                                   c.Estado == true &&
                                   c.Numeroidentificacion != null &&
                                   c.Numeroidentificacion.Trim().ToLower() == idBuscado &&
                                   (!clienteIdExistente.HasValue || c.Codcliente != clienteIdExistente.Value));
                if (duplicateId)
                {
                    return $"Identificación duplicada. Ya existe un cliente activo registrado con la identificación '{dto.Numeroidentificacion}'.";
                }
            }

            // Validar duplicado de correo electrónico (insensible a mayúsculas/minúsculas)
            if (!string.IsNullOrWhiteSpace(dto.Correo))
            {
                var correoBuscado = dto.Correo.Trim().ToLowerInvariant();
                var duplicateEmail = await _context.Clientes
                    .AnyAsync(c => c.Usuario == ownerId &&
                                   c.Estado == true &&
                                   c.Correo != null &&
                                   c.Correo.Trim().ToLower() == correoBuscado &&
                                   (!clienteIdExistente.HasValue || c.Codcliente != clienteIdExistente.Value));
                if (duplicateEmail)
                {
                    return $"Correo electrónico duplicado. Ya existe un cliente activo registrado con el correo electrónico '{dto.Correo}'.";
                }
            }
        }

        if (string.IsNullOrWhiteSpace(dto.Correo))
            return "El correo electrónico es obligatorio.";

        if (dto.DiasCredito is < 0)
            return "Los días de crédito no pueden ser negativos.";

        try
        {
            _ = new System.Net.Mail.MailAddress(dto.Correo);
        }
        catch
        {
            return "El formato del correo electrónico es inválido (debe cumplir el formato usuario@dominio.com).";
        }

        var correoPrincipalNormalizado = NormalizarCorreo(dto.Correo);
        var correosSecundariosNormalizados = (dto.CorreosAdicionales ?? new List<string>())
            .Select(NormalizarCorreo)
            .Where(correo => !string.IsNullOrWhiteSpace(correo))
            .ToList();

        if (correosSecundariosNormalizados.Any(correo => string.Equals(correo, correoPrincipalNormalizado, StringComparison.OrdinalIgnoreCase)))
            return "El correo secundario no puede ser igual al correo principal.";

        if (correosSecundariosNormalizados.Count != correosSecundariosNormalizados.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return "No se permiten correos secundarios repetidos.";

        foreach (var correoSecundario in correosSecundariosNormalizados)
        {
            try
            {
                _ = new MailAddress(correoSecundario);
            }
            catch
            {
                return "Uno de los correos secundarios adicionales es inválido o tiene un formato incorrecto.";
            }
        }

        if (string.IsNullOrWhiteSpace(dto.Oblgconta))
            return "Debe indicar si está obligado a llevar contabilidad (SI o NO).";

        if (dto.Oblgconta != "SI" && dto.Oblgconta != "NO")
            return "El campo obligado a llevar contabilidad solo permite los valores 'SI' o 'NO'.";

        var esJuridica = await EsTipoJuridico(dto.TipoCliente);

        if (esJuridica)
        {
            if (string.IsNullOrWhiteSpace(dto.Nombrerazonsocial))
                return "La razón social es obligatoria para personas jurídicas.";

            dto.Apellidos = null;
            dto.Nombres = null;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(dto.Apellidos))
                return "Los apellidos son obligatorios para personas naturales.";

            if (string.IsNullOrWhiteSpace(dto.Nombres))
                return "Los nombres son obligatorios para personas naturales.";

            dto.Nombrecomercial = null;
            dto.Nombrerazonsocial = null;
        }

        return null;
    }

    private static bool ValidarCedulaEcuatoriana(string cedula)
    {
        cedula = new string((cedula ?? "").Where(char.IsDigit).ToArray());

        if (cedula.Length != 10)
            return false;

        int[] coeficientes = { 2, 1, 2, 1, 2, 1, 2, 1, 2 };
        var provincia = int.Parse(cedula[..2], System.Globalization.CultureInfo.InvariantCulture);
        var tercerDigito = int.Parse(cedula.Substring(2, 1), System.Globalization.CultureInfo.InvariantCulture);

        if (provincia < 1 || provincia > 24 || tercerDigito > 5)
            return false;

        var suma = 0;
        for (var i = 0; i < 9; i++)
        {
            var valor = int.Parse(cedula[i].ToString(), System.Globalization.CultureInfo.InvariantCulture) * coeficientes[i];
            suma += valor > 9 ? valor - 9 : valor;
        }

        var digitoRecibido = int.Parse(cedula[9].ToString(), System.Globalization.CultureInfo.InvariantCulture);
        var digitoCalculado = (10 - (suma % 10)) % 10;
        return digitoCalculado == digitoRecibido;
    }

    private static bool ValidarRucEcuatoriano(string ruc)
    {
        ruc = new string((ruc ?? "").Where(char.IsDigit).ToArray());

        if (ruc == "9999999999999") // Consumidor Final
            return true;

        if (ruc.Length != 13)
            return false;

        var provincia = int.Parse(ruc[..2], System.Globalization.CultureInfo.InvariantCulture);
        if (provincia < 1 || provincia > 24)
            return false;

        var tercerDigito = int.Parse(ruc.Substring(2, 1), System.Globalization.CultureInfo.InvariantCulture);

        return tercerDigito switch
        {
            <= 5 => ValidarCedulaEcuatoriana(ruc[..10]) && ruc.EndsWith("001", StringComparison.Ordinal),
            6 => ValidarRucPublico(ruc),
            9 => ValidarRucPrivado(ruc),
            _ => false
        };
    }

    private static bool ValidarRucPrivado(string ruc)
    {
        int[] coeficientes = { 4, 3, 2, 7, 6, 5, 4, 3, 2 };
        var suma = 0;

        for (var i = 0; i < coeficientes.Length; i++)
            suma += int.Parse(ruc[i].ToString(), System.Globalization.CultureInfo.InvariantCulture) * coeficientes[i];

        var residuo = suma % 11;
        var digitoCalculado = residuo == 0 ? 0 : 11 - residuo;
        var digitoRecibido = int.Parse(ruc[9].ToString(), System.Globalization.CultureInfo.InvariantCulture);

        return digitoCalculado == digitoRecibido && ruc[10..] != "000";
    }

    private static bool ValidarRucPublico(string ruc)
    {
        int[] coeficientes = { 3, 2, 7, 6, 5, 4, 3, 2 };
        var suma = 0;

        for (var i = 0; i < coeficientes.Length; i++)
            suma += int.Parse(ruc[i].ToString(), System.Globalization.CultureInfo.InvariantCulture) * coeficientes[i];

        var residuo = suma % 11;
        var digitoCalculado = residuo == 0 ? 0 : 11 - residuo;
        var digitoRecibido = int.Parse(ruc[8].ToString(), System.Globalization.CultureInfo.InvariantCulture);

        return digitoCalculado == digitoRecibido && ruc[9..] != "0000";
    }

    private async Task<bool> EsTipoJuridico(int tipoCliente)
    {
        var descripcion = await _context.Tipoclientes
            .Where(t => t.TclCodigo == tipoCliente)
            .Select(t => t.TclDescripcion)
            .FirstOrDefaultAsync();

        return TipoClienteClasificacion.EsJuridica(descripcion);
    }
    private static void NormalizarDto(ClienteUpsertDto dto)
    {
        dto.Apellidos = dto.Apellidos?.Trim();
        dto.Nombres = dto.Nombres?.Trim();
        dto.Nombrecomercial = dto.Nombrecomercial?.Trim();
        dto.Nombrerazonsocial = dto.Nombrerazonsocial?.Trim();
        dto.Numeroidentificacion = dto.Numeroidentificacion?.Trim();
        dto.Direccion = dto.Direccion?.Trim();
        dto.Telefonoconvencional = SanitizarTelefono(dto.Telefonoconvencional, esCelular: false);
        dto.Celular = SanitizarTelefono(dto.Celular, esCelular: true);
        dto.Correo = dto.Correo?.Trim();
        dto.CorreosAdicionales = (dto.CorreosAdicionales ?? new List<string>())
            .Select(correo => correo?.Trim() ?? string.Empty)
            .Where(correo => !string.IsNullOrWhiteSpace(correo))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        dto.Observaciones = dto.Observaciones?.Trim();
        var oblgStr = dto.Oblgconta?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(oblgStr) || oblgStr == "0" || oblgStr == "NO")
        {
            dto.Oblgconta = "NO";
        }
        else if (oblgStr == "1" || oblgStr == "SI" || oblgStr == "S")
        {
            dto.Oblgconta = "SI";
        }
        else
        {
            dto.Oblgconta = "NO"; // Safe default
        }
    }

    private static string? SanitizarTelefono(string? valor, bool esCelular)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var digitos = new string(valor.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(digitos)) return null;
        if (esCelular && digitos.Length == 9 && digitos.StartsWith('9'))
        {
            digitos = "0" + digitos;
        }
        return digitos;
    }

    private static string NormalizarCorreo(string? valor) =>
        (valor ?? string.Empty).Trim().ToLowerInvariant();

    private string? ValidarClienteOptimizado(ClienteUpsertDto dto, List<Identificacion> identificaciones, List<Tipocliente> tiposCliente)
    {
        if (dto is null)
            return "Datos no válidos.";

        // Normalizar RUC/Cédula scientific notation and symbols
        if (dto.Tipoidentificacion.HasValue && dto.Tipoidentificacion.Value > 0)
        {
            var ideLookup = identificaciones.FirstOrDefault(i => i.IdeSec == dto.Tipoidentificacion.Value);
            if (ideLookup != null)
            {
                var normCodigoReal = ideLookup.IdeCodigo;
                var normDescReal = ideLookup.IdeDescripcion ?? "";

                bool normEsCedula = string.Equals(normCodigoReal, "05", StringComparison.OrdinalIgnoreCase) ||
                                    normDescReal.Contains("cédula", StringComparison.OrdinalIgnoreCase) ||
                                    normDescReal.Contains("cedula", StringComparison.OrdinalIgnoreCase);

                bool normEsRuc = string.Equals(normCodigoReal, "04", StringComparison.OrdinalIgnoreCase) ||
                                 normDescReal.Contains("RUC", StringComparison.OrdinalIgnoreCase);

                if (normEsRuc || normEsCedula)
                {
                    if (dto.Numeroidentificacion != null)
                    {
                        var rawId = dto.Numeroidentificacion.Trim();
                        // Parse scientific notation (e.g. 1.79E+12 or 1,79E+12)
                        if (rawId.Contains('E', StringComparison.OrdinalIgnoreCase))
                        {
                            var normalizedStr = rawId.Replace(',', '.');
                            if (double.TryParse(normalizedStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedDouble))
                            {
                                rawId = parsedDouble.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
                            }
                        }

                        // Clean trailing decimals (e.g. 1701042960.0)
                        var normalizedDecimal = rawId.Replace(',', '.');
                        if (double.TryParse(normalizedDecimal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedNum))
                        {
                            if (parsedNum % 1 == 0)
                            {
                                rawId = parsedNum.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
                            }
                        }

                        // Strip non-digits
                        dto.Numeroidentificacion = new string(rawId.Where(char.IsDigit).ToArray());
                    }
                }
            }
        }

        NormalizarDto(dto);

        if (dto.TipoCliente <= 0)
            return "El Tipo de cliente (Natural o Juridica) es obligatorio.";

        if (!dto.Tipoidentificacion.HasValue || dto.Tipoidentificacion <= 0)
            return "El Tipo de identificación (Cedula o RUC) es obligatorio.";

        var ide = identificaciones.FirstOrDefault(i => i.IdeSec == dto.Tipoidentificacion.Value);
        if (ide == null)
            return "El tipo de identificación seleccionado es inválido o no existe.";

        var codigoReal = ide.IdeCodigo;
        var descReal = ide.IdeDescripcion ?? "";

        bool esCedula = string.Equals(codigoReal, "05", StringComparison.OrdinalIgnoreCase) ||
                         descReal.Contains("cédula", StringComparison.OrdinalIgnoreCase) ||
                         descReal.Contains("cedula", StringComparison.OrdinalIgnoreCase);

        bool esRuc = string.Equals(codigoReal, "04", StringComparison.OrdinalIgnoreCase) ||
                      descReal.Contains("RUC", StringComparison.OrdinalIgnoreCase);

        bool esPasaporte = string.Equals(codigoReal, "06", StringComparison.OrdinalIgnoreCase) ||
                            descReal.Contains("pasaporte", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(dto.Numeroidentificacion) && dto.Numeroidentificacion.All(char.IsDigit))
        {
            if (esCedula && dto.Numeroidentificacion.Length == 9)
            {
                dto.Numeroidentificacion = "0" + dto.Numeroidentificacion;
            }
            else if (esRuc && dto.Numeroidentificacion.Length == 12)
            {
                dto.Numeroidentificacion = "0" + dto.Numeroidentificacion;
            }

            // Auto-reconstruct RUC numbers truncated/rounded by Excel scientific notation (e.g. ending in 000)
            if (esRuc && dto.Numeroidentificacion.Length == 13 && dto.Numeroidentificacion.EndsWith("000"))
            {
                var ruc = dto.Numeroidentificacion;
                var tercerDigito = int.Parse(ruc.Substring(2, 1), System.Globalization.CultureInfo.InvariantCulture);
                if (tercerDigito == 9) // Private legal entity (RUC de Sociedad Privada/Extranjera sin cédula)
                {
                    int[] coeficientes = { 4, 3, 2, 7, 6, 5, 4, 3, 2 };
                    var suma = 0;
                    for (var idx = 0; idx < coeficientes.Length; idx++)
                        suma += int.Parse(ruc[idx].ToString(), System.Globalization.CultureInfo.InvariantCulture) * coeficientes[idx];
                    var residuo = suma % 11;
                    var digitoCalculado = residuo == 0 ? 0 : 11 - residuo;
                    dto.Numeroidentificacion = ruc[..9] + digitoCalculado + "001";
                }
                else if (tercerDigito <= 5) // Natural person RUC
                {
                    int[] coeficientes = { 2, 1, 2, 1, 2, 1, 2, 1, 2 };
                    var suma = 0;
                    for (var idx = 0; idx < 9; idx++)
                    {
                        var valor = int.Parse(ruc[idx].ToString(), System.Globalization.CultureInfo.InvariantCulture) * coeficientes[idx];
                        suma += valor > 9 ? valor - 9 : valor;
                    }
                    var digitoCalculado = (10 - (suma % 10)) % 10;
                    dto.Numeroidentificacion = ruc[..9] + digitoCalculado + "001";
                }
                else if (tercerDigito == 6) // Public entity RUC
                {
                    int[] coeficientes = { 3, 2, 7, 6, 5, 4, 3, 2 };
                    var suma = 0;
                    for (var idx = 0; idx < coeficientes.Length; idx++)
                        suma += int.Parse(ruc[idx].ToString(), System.Globalization.CultureInfo.InvariantCulture) * coeficientes[idx];
                    var residuo = suma % 11;
                    var digitoCalculado = residuo == 0 ? 0 : 11 - residuo;
                    dto.Numeroidentificacion = ruc[..8] + digitoCalculado + "0001";
                }
            }
        }

        if (esCedula)
        {
            if (string.IsNullOrWhiteSpace(dto.Numeroidentificacion))
                return "La identificación (cédula) es obligatoria.";

            if (!dto.Numeroidentificacion.All(char.IsDigit))
                return "La cédula debe contener únicamente dígitos numéricos.";

            if (dto.Numeroidentificacion.Length != 10)
                return $"Cédula inválida. La cédula debe tener exactamente 10 dígitos (se recibieron {dto.Numeroidentificacion.Length} dígitos).";

            if (!ValidarCedulaEcuatoriana(dto.Numeroidentificacion))
                return "Cédula inválida. No supera el algoritmo de validación del dígito verificador ecuatoriano.";
        }
        else if (esRuc)
        {
            if (string.IsNullOrWhiteSpace(dto.Numeroidentificacion))
                return "La identificación (RUC) es obligatoria.";

            if (!dto.Numeroidentificacion.All(char.IsDigit))
                return "El RUC debe contener únicamente dígitos numéricos.";

            if (dto.Numeroidentificacion.Length != 13)
                return $"RUC inválido. El RUC debe tener exactamente 13 dígitos (se recibieron {dto.Numeroidentificacion.Length} dígitos).";

            if (!ValidarRucEcuatoriano(dto.Numeroidentificacion))
                return "RUC inválido. No supera el algoritmo de validación del dígito verificador del SRI.";
        }
        else if (esPasaporte)
        {
            if (string.IsNullOrWhiteSpace(dto.Numeroidentificacion))
                return "La identificación (Pasaporte) es obligatoria.";

            if (!dto.Numeroidentificacion.All(char.IsLetterOrDigit))
                return "El pasaporte debe contener únicamente caracteres alfanuméricos (letras y números).";

            if (dto.Numeroidentificacion.Length < 3 || dto.Numeroidentificacion.Length > 20)
                return $"El pasaporte debe tener una longitud de entre 3 y 20 caracteres (se recibieron {dto.Numeroidentificacion.Length} caracteres).";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(dto.Numeroidentificacion))
                return "La identificación es obligatoria.";

            if (dto.Numeroidentificacion.Length < 3 || dto.Numeroidentificacion.Length > 20)
                return $"La identificación debe tener una longitud de entre 3 y 20 caracteres (se recibieron {dto.Numeroidentificacion.Length} caracteres).";
        }

        if (string.IsNullOrWhiteSpace(dto.Correo))
            return "El correo electrónico es obligatorio.";

        if (dto.DiasCredito is < 0)
            return "Los días de crédito no pueden ser negativos.";

        try
        {
            _ = new System.Net.Mail.MailAddress(dto.Correo);
        }
        catch
        {
            return "El formato del correo electrónico es inválido (debe cumplir el formato usuario@dominio.com).";
        }

        var correoPrincipalNormalizado = NormalizarCorreo(dto.Correo);
        var correosSecundariosNormalizados = (dto.CorreosAdicionales ?? new List<string>())
            .Select(NormalizarCorreo)
            .Where(correo => !string.IsNullOrWhiteSpace(correo))
            .ToList();

        if (correosSecundariosNormalizados.Any(correo => string.Equals(correo, correoPrincipalNormalizado, StringComparison.OrdinalIgnoreCase)))
            return "El correo secundario no puede ser igual al correo principal.";

        if (correosSecundariosNormalizados.Count != correosSecundariosNormalizados.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return "No se permiten correos secundarios repetidos.";

        foreach (var correoSecundario in correosSecundariosNormalizados)
        {
            try
            {
                _ = new MailAddress(correoSecundario);
            }
            catch
            {
                return "Uno de los correos secundarios adicionales es inválido o tiene un formato incorrecto.";
            }
        }

        if (string.IsNullOrWhiteSpace(dto.Oblgconta))
            return "Debe indicar si está obligado a llevar contabilidad (SI o NO).";

        if (dto.Oblgconta != "SI" && dto.Oblgconta != "NO")
            return "El campo obligado a llevar contabilidad solo permite los valores 'SI' o 'NO'.";

        var tCl = tiposCliente.FirstOrDefault(t => t.TclCodigo == dto.TipoCliente);
        var descTipo = tCl?.TclDescripcion;
        var esJuridica = TipoClienteClasificacion.EsJuridica(descTipo);

        if (esJuridica)
        {
            if (string.IsNullOrWhiteSpace(dto.Nombrerazonsocial))
                return "La razón social es obligatoria para personas jurídicas.";

            dto.Apellidos = null;
            dto.Nombres = null;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(dto.Apellidos))
                return "Los apellidos son obligatorios para personas naturales.";

            if (string.IsNullOrWhiteSpace(dto.Nombres))
                return "Los nombres son obligatorios para personas naturales.";

            dto.Nombrerazonsocial = null;
        }

        return null;
    }
}
