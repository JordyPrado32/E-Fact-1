using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;
using Simetric.Services;

namespace Simetric.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductosController : ControllerBase
{
    private const decimal PrecioMaximoPermitido = 99999999.99m;
    private readonly AppDbContext _db;

    public ProductosController(AppDbContext db)
    {
        _db = db;
    }

    // Métodos de ayuda para la jerarquía
    private static bool IsValidUser(int userId) => userId > 0;

    private async Task<int?> GetOwnerIdAsync(int userId)
    {
        var user = await _db.Usuarios
            .Where(u => u.IdUsuario == userId)
            .Select(u => new { u.IdUsuario, u.idJefe })
            .FirstOrDefaultAsync();

        return user == null ? null : (user.idJefe ?? user.IdUsuario);
    }

    #region DTOs
    public class ProductoDto
    {
        public int Codigo { get; set; }
        public string? Nombre { get; set; }
        public string? CodigoPrincipal { get; set; }
        public decimal? ValorUnitario { get; set; }
        public decimal? Precio2 { get; set; }
        public decimal? Precio3 { get; set; }
        public string? TipoCompravena { get; set; }
        public int? TipoProducto { get; set; }
        public int? Idsubtipo { get; set; }
        public string? Codigoimpuesto { get; set; }
        public string? Porcentajeimpuesto { get; set; }
        public bool? Estado { get; set; }
        public string? Observacion { get; set; }
        public int? Idusuario { get; set; }
    }

    public class ProductoUpsertDto
    {
        public int Codigo { get; set; }
        public string? Nombre { get; set; }
        public string? CodigoPrincipal { get; set; }
        public decimal? ValorUnitario { get; set; }
        public decimal? Precio2 { get; set; }
        public decimal? Precio3 { get; set; }
        public string? TipoCompravena { get; set; }
        public int? TipoProducto { get; set; }
        public int? Idsubtipo { get; set; }
        public string? Codigoimpuesto { get; set; }
        public string? Porcentajeimpuesto { get; set; }
        public bool? Estado { get; set; }
        public string? Observacion { get; set; }
    }

    public class BulkImportResultDto
    {
        public int Creados { get; set; }
        public List<string> Errores { get; set; } = new();
    }
    #endregion

    [HttpGet("lookups")]
    public async Task<ActionResult> Lookups([FromQuery] int userId)
    {
        if (!IsValidUser(userId))
            return Unauthorized("Sesión no válida.");

        // ✅ Corregido: Buscar el dueño para traer las categorías correctas
        int? ownerId = await GetOwnerIdAsync(userId);
        if (ownerId == null) return NotFound("Usuario no encontrado.");
        var tipos = await _db.Productotipos
            .AsNoTracking()
            .Where(x => x.Idusuario == ownerId && x.Estado == true)
            .OrderBy(x => x.Descripcion)
            .Select(x => new { x.Idtipoproducto, x.Descripcion })
            .ToListAsync();

        var subtipos = await _db.Productosubtipos
            .AsNoTracking()
            .Where(x => x.Idusuario == ownerId && x.Estado == "A")
            .OrderBy(x => x.Descripcion)
            .Select(x => new { x.Idsubtipo, x.Descripcion, x.Idtipoproducto })
            .ToListAsync();

        var impuestos = await _db.Codigoimpuestos
            .AsNoTracking()
            .OrderBy(x => x.Codigo)
            .Select(x => new { x.Codigo, x.Descripcion })
            .ToListAsync();

        var ivas = await _db.Porcentajeivas
            .AsNoTracking()
            .Where(x => x.Estado == "A" || x.Estado == "1")
            .OrderBy(x => x.Codigo)
            .Select(x => new { x.Codigo, x.Descripcion, Valor = x.Valor == null ? null : x.Valor.ToString() })
            .ToListAsync();

        return Ok(new { tipos, subtipos, impuestos, ivas });
    }

    [HttpGet]
    public async Task<ActionResult<List<ProductoDto>>> GetAll([FromQuery] int userId)
    {
        if (!IsValidUser(userId)) return Unauthorized("Sesión no válida.");

        int? ownerId = await GetOwnerIdAsync(userId);
        if (ownerId == null) return NotFound("Usuario no encontrado.");
        var data = await _db.Productos
            .AsNoTracking()
            .Where(p => p.Idusuario == ownerId) // ✅ Filtro por grupo
            .OrderByDescending(p => p.Codigo)
            .Select(p => new ProductoDto
            {
                Codigo = p.Codigo,
                Nombre = p.Nombre,
                CodigoPrincipal = p.CodigoPrincipal,
                ValorUnitario = p.ValorUnitario,
                Precio2 = p.Precio2,
                Precio3 = p.Precio3,
                TipoCompravena = p.Tipocompravena,
                TipoProducto = p.TipoProducto,
                Idsubtipo = p.Idsubtipo,
                Codigoimpuesto = p.Codigoimpuesto,
                Porcentajeimpuesto = p.Porcentajeimpuesto,
                Estado = p.Estado,
                Observacion = p.Observacion,
                Idusuario = p.Idusuario
            })
            .ToListAsync();

        return Ok(data);
    }

    [HttpGet("{codigo:int}")]
    public async Task<ActionResult> GetByCodigo(int codigo, [FromQuery] int userId)
    {
        if (!IsValidUser(userId)) return Unauthorized("Sesión no válida.");

        int? ownerId = await GetOwnerIdAsync(userId);
        if (ownerId == null) return NotFound();
        var item = await _db.Productos
            .AsNoTracking()
            .Where(x => x.Codigo == codigo && x.Idusuario == ownerId) // ✅ Corregido: ownerId
            .Select(p => new ProductoDto
            {
                Codigo = p.Codigo,
                Nombre = p.Nombre,
                CodigoPrincipal = p.CodigoPrincipal,
                ValorUnitario = p.ValorUnitario,
                Precio2 = p.Precio2,
                Precio3 = p.Precio3,
                TipoCompravena = p.Tipocompravena,
                TipoProducto = p.TipoProducto,
                Idsubtipo = p.Idsubtipo,
                Codigoimpuesto = p.Codigoimpuesto,
                Porcentajeimpuesto = p.Porcentajeimpuesto,
                Estado = p.Estado,
                Observacion = p.Observacion,
                Idusuario = p.Idusuario
            })
            .FirstOrDefaultAsync();

        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult> Create([FromQuery] int userId, [FromBody] ProductoUpsertDto model)
    {
        if (!IsValidUser(userId)) return Unauthorized();

        int? ownerId = await GetOwnerIdAsync(userId);
        if (ownerId == null) return NotFound();

        var error = await ValidarProductoAsync(ownerId.Value, model);
        if (error is not null) return BadRequest(error);

        NormalizarProducto(model);
        var entity = new Producto
        {
            Nombre = model.Nombre,
            CodigoPrincipal = model.CodigoPrincipal,
            ValorUnitario = model.ValorUnitario,
            Precio2 = model.Precio2,
            Precio3 = model.Precio3,
            Tipocompravena = model.TipoCompravena,
            TipoProducto = model.TipoProducto,
            Idsubtipo = model.Idsubtipo,
            Codigoimpuesto = model.Codigoimpuesto,
            Porcentajeimpuesto = model.Porcentajeimpuesto,
            Estado = model.Estado ?? true,
            Observacion = model.Observacion,
            Idusuario = ownerId.Value // ✅ Se guarda para el Jefe
        };

        try
        {
            _db.Productos.Add(entity);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            var inner = ex.InnerException;
            var sqlMsg = inner?.Message ?? ex.Message;
            
            if (sqlMsg.Contains("FK_PRODUCTO_IMPUESTO") || sqlMsg.Contains("CODIGOSIMPUESTOS"))
            {
                return BadRequest("Error al guardar: La tarifa de impuesto (IVA) seleccionada no es válida en el sistema.");
            }
            if (sqlMsg.Contains("duplicate") || sqlMsg.Contains("duplicado") || sqlMsg.Contains("unique") || sqlMsg.Contains("UK_"))
            {
                return BadRequest("Error al guardar: Ya existe un producto con el mismo código en tu catálogo.");
            }
            return BadRequest($"Error al guardar en la base de datos: {sqlMsg}");
        }

        return Ok(new { entity.Codigo });
    }

    [HttpPost("bulk")]
    public async Task<ActionResult<BulkImportResultDto>> BulkCreate([FromQuery] int userId, [FromBody] List<ProductoUpsertDto> models)
    {
        if (!IsValidUser(userId)) return Unauthorized();

        int? ownerId = await GetOwnerIdAsync(userId);
        if (ownerId == null) return NotFound("Usuario no encontrado.");

        var result = new BulkImportResultDto();
        if (models == null || !models.Any())
        {
            return Ok(result);
        }

        // 1. Cargar datos en memoria para evitar consultas repetitivas en bucle
        var existingCodigos = await _db.Productos
            .Where(p => p.Idusuario == ownerId.Value && p.CodigoPrincipal != null)
            .Select(p => p.CodigoPrincipal!)
            .ToListAsync();
        var existingCodigosSet = new HashSet<string>(existingCodigos, StringComparer.OrdinalIgnoreCase);

        var validCategorias = await _db.Productotipos
            .Where(t => t.Idusuario == ownerId.Value && t.Estado == true)
            .Select(t => t.Idtipoproducto)
            .ToListAsync();
        var validCategoriasSet = new HashSet<int>(validCategorias);

        var validSubtipos = await _db.Productosubtipos
            .Where(s => s.Idusuario == ownerId.Value && s.Estado == "A")
            .Select(s => new { s.Idsubtipo, s.Idtipoproducto })
            .ToListAsync();
        var validSubtiposSet = new HashSet<(int subtipo, int? categoria)>(
            validSubtipos.Select(s => (s.Idsubtipo, (int?)s.Idtipoproducto))
        );

        // Para evitar duplicados dentro del mismo lote que estamos importando
        var tempCodigosLote = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var entitiesToAdd = new List<Producto>();

        for (int i = 0; i < models.Count; i++)
        {
            var model = models[i];
            var fila = i + 2; // Fila real en el CSV (la línea 1 es la cabecera)

            if (model == null) continue;

            NormalizarProducto(model);

            // Validaciones básicas en memoria
            if (string.IsNullOrWhiteSpace(model.TipoCompravena))
            {
                result.Errores.Add($"Fila {fila}: Debe seleccionar si el registro es PRODUCTO o SERVICIO.");
                continue;
            }
            if (model.TipoCompravena != "PRODUCTO" && model.TipoCompravena != "SERVICIO")
            {
                result.Errores.Add($"Fila {fila}: Debe seleccionar un tipo válido: PRODUCTO o SERVICIO.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(model.Nombre))
            {
                result.Errores.Add($"Fila {fila}: Debe ingresar el nombre del producto.");
                continue;
            }
            if (model.Nombre.Length > 120)
            {
                result.Errores.Add($"Fila {fila}: El nombre no puede exceder 120 caracteres.");
                continue;
            }
            if (!string.IsNullOrWhiteSpace(model.CodigoPrincipal) && model.CodigoPrincipal.Length > 50)
            {
                result.Errores.Add($"Fila {fila}: El código no puede exceder 50 caracteres.");
                continue;
            }
            if (!model.ValorUnitario.HasValue)
            {
                result.Errores.Add($"Fila {fila}: Debe ingresar el precio base.");
                continue;
            }
            if (model.ValorUnitario < 0 || model.Precio2 < 0 || model.Precio3 < 0)
            {
                result.Errores.Add($"Fila {fila}: Los precios no pueden ser menores a 0.");
                continue;
            }
            if (model.ValorUnitario > PrecioMaximoPermitido || model.Precio2 > PrecioMaximoPermitido || model.Precio3 > PrecioMaximoPermitido)
            {
                result.Errores.Add($"Fila {fila}: Los precios no pueden superar {PrecioMaximoPermitido:N2}.");
                continue;
            }
            if (!string.IsNullOrWhiteSpace(model.Observacion) && model.Observacion.Length > 250)
            {
                result.Errores.Add($"Fila {fila}: La observación no puede exceder 250 caracteres.");
                continue;
            }
            if (model.Codigoimpuesto == "2" && string.IsNullOrWhiteSpace(model.Porcentajeimpuesto))
            {
                result.Errores.Add($"Fila {fila}: Debe seleccionar la tarifa IVA.");
                continue;
            }

            // Validar duplicado de código principal
            if (!string.IsNullOrWhiteSpace(model.CodigoPrincipal))
            {
                if (existingCodigosSet.Contains(model.CodigoPrincipal) || tempCodigosLote.Contains(model.CodigoPrincipal))
                {
                    result.Errores.Add($"Fila {fila}: Ya existe un producto con el código '{model.CodigoPrincipal}' en su catálogo.");
                    continue;
                }
                tempCodigosLote.Add(model.CodigoPrincipal);
            }

            if (model.TipoProducto.HasValue)
            {
                if (!validCategoriasSet.Contains(model.TipoProducto.Value))
                {
                    result.Errores.Add($"Fila {fila}: La categoría seleccionada no es válida para su usuario.");
                    continue;
                }
            }

            if (model.Idsubtipo.HasValue)
            {
                if (!model.TipoProducto.HasValue)
                {
                    result.Errores.Add($"Fila {fila}: Debe seleccionar una categoría antes de elegir la subcategoría.");
                    continue;
                }

                if (!validSubtiposSet.Contains((model.Idsubtipo.Value, model.TipoProducto.Value)))
                {
                    result.Errores.Add($"Fila {fila}: La subcategoría seleccionada no es válida para su usuario.");
                    continue;
                }
            }

            var entity = new Producto
            {
                Nombre = model.Nombre,
                CodigoPrincipal = model.CodigoPrincipal,
                ValorUnitario = model.ValorUnitario,
                Precio2 = model.Precio2,
                Precio3 = model.Precio3,
                Tipocompravena = model.TipoCompravena,
                TipoProducto = model.TipoProducto,
                Idsubtipo = model.Idsubtipo,
                Codigoimpuesto = model.Codigoimpuesto,
                Porcentajeimpuesto = model.Porcentajeimpuesto,
                Estado = model.Estado ?? true,
                Observacion = model.Observacion,
                Idusuario = ownerId.Value
            };

            entitiesToAdd.Add(entity);
        }

        try
        {
            if (entitiesToAdd.Any())
            {
                _db.Productos.AddRange(entitiesToAdd);
                await _db.SaveChangesAsync();
                result.Creados = entitiesToAdd.Count;
            }
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            var inner = ex.InnerException;
            var sqlMsg = inner?.Message ?? ex.Message;
            
            if (sqlMsg.Contains("FK_PRODUCTO_IMPUESTO") || sqlMsg.Contains("CODIGOSIMPUESTOS"))
            {
                result.Errores.Add("Error al guardar: Uno de los productos tiene una tarifa de impuesto (IVA) no válida en la base de datos.");
            }
            else if (sqlMsg.Contains("duplicate") || sqlMsg.Contains("duplicado") || sqlMsg.Contains("unique") || sqlMsg.Contains("UK_"))
            {
                result.Errores.Add("Error al guardar: Se detectó un código principal que ya existe en tu catálogo de base de datos.");
            }
            else
            {
                result.Errores.Add($"Error al guardar en la base de datos: {sqlMsg}");
            }
        }

        return Ok(result);
    }

    [HttpPut("{codigo:int}")]
    public async Task<ActionResult> Update(int codigo, [FromQuery] int userId, [FromBody] ProductoUpsertDto model)
    {
        if (!IsValidUser(userId)) return Unauthorized();

        int? ownerId = await GetOwnerIdAsync(userId);
        if (ownerId == null) return NotFound();

        // ✅ Corregido: Buscar por ownerId
        var existente = await _db.Productos
            .FirstOrDefaultAsync(x => x.Codigo == codigo && x.Idusuario == ownerId);

        if (existente is null)
            return NotFound("El producto no existe o no tiene permisos.");

        var error = await ValidarProductoAsync(ownerId.Value, model, codigo, existente);
        if (error is not null) return BadRequest(error);

        NormalizarProducto(model);
        existente.Nombre = model.Nombre;
        existente.CodigoPrincipal = model.CodigoPrincipal;
        existente.ValorUnitario = model.ValorUnitario;
        existente.Precio2 = model.Precio2;
        existente.Precio3 = model.Precio3;
        existente.Tipocompravena = model.TipoCompravena;
        existente.TipoProducto = model.TipoProducto;
        existente.Idsubtipo = model.Idsubtipo;
        existente.Codigoimpuesto = model.Codigoimpuesto;
        existente.Porcentajeimpuesto = model.Porcentajeimpuesto;
        existente.Observacion = model.Observacion;

        if (model.Estado is not null) existente.Estado = model.Estado;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{codigo:int}")]
    public async Task<ActionResult> Delete(int codigo, [FromQuery] int userId)
    {
        if (!IsValidUser(userId)) return Unauthorized();

        int? ownerId = await GetOwnerIdAsync(userId);
        if (ownerId == null) return NotFound();

        var existente = await _db.Productos
            .FirstOrDefaultAsync(x => x.Codigo == codigo && x.Idusuario == ownerId); // ✅ Corregido

        if (existente is null) return NotFound();

        existente.Estado = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    #region Validaciones y Normalización
    private async Task<string?> ValidarProductoAsync(int ownerId, ProductoUpsertDto? model, int? codigoActual = null, Producto? productoActual = null)
    {
        if (model is null) return "Datos inválidos.";
        NormalizarProducto(model);

        if (string.IsNullOrWhiteSpace(model.TipoCompravena)) return "Debe seleccionar si el registro es PRODUCTO o SERVICIO.";
        if (model.TipoCompravena != "PRODUCTO" && model.TipoCompravena != "SERVICIO") return "Debe seleccionar un tipo válido: PRODUCTO o SERVICIO.";
        if (string.IsNullOrWhiteSpace(model.Nombre)) return "Debe ingresar el nombre del producto.";
        if (model.Nombre.Length > 120) return "El nombre no puede exceder 120 caracteres.";
        if (!string.IsNullOrWhiteSpace(model.CodigoPrincipal) && model.CodigoPrincipal.Length > 50) return "El código no puede exceder 50 caracteres.";
        if (!model.ValorUnitario.HasValue) return "Debe ingresar el precio base.";
        if (model.ValorUnitario < 0 || model.Precio2 < 0 || model.Precio3 < 0) return "Los precios no pueden ser menores a 0.";
        if (model.ValorUnitario > PrecioMaximoPermitido || model.Precio2 > PrecioMaximoPermitido || model.Precio3 > PrecioMaximoPermitido)
            return $"Los precios no pueden superar {PrecioMaximoPermitido:N2}.";
        if (!string.IsNullOrWhiteSpace(model.Observacion) && model.Observacion.Length > 250) return "La observación no puede exceder 250 caracteres.";
        if (model.Codigoimpuesto == "2" && string.IsNullOrWhiteSpace(model.Porcentajeimpuesto)) return "Debe seleccionar la tarifa IVA.";

        // Validar duplicado de código principal dentro del mismo grupo
        if (!string.IsNullOrWhiteSpace(model.CodigoPrincipal))
        {
            var existeDuplicado = await _db.Productos
                .AnyAsync(p => p.Idusuario == ownerId &&
                               p.Codigo != (codigoActual ?? 0) &&
                               p.CodigoPrincipal == model.CodigoPrincipal);
            if (existeDuplicado) return "Ya existe un producto con ese código en su catálogo.";
        }

        if (model.TipoProducto.HasValue)
        {
            var categoriaSinCambios = productoActual is not null &&
                                      productoActual.TipoProducto == model.TipoProducto;

            var catOk = categoriaSinCambios || await _db.Productotipos.AnyAsync(t =>
                t.Idtipoproducto == model.TipoProducto &&
                t.Idusuario == ownerId &&
                t.Estado == true);

            if (!catOk) return "La categoría seleccionada no es válida para su usuario.";
        }

        if (model.Idsubtipo.HasValue)
        {
            if (!model.TipoProducto.HasValue)
            {
                return "Debe seleccionar una categoría antes de elegir la subcategoría.";
            }

            var subtipoSinCambios = productoActual is not null &&
                                    productoActual.TipoProducto == model.TipoProducto &&
                                    productoActual.Idsubtipo == model.Idsubtipo;

            var subtipoOk = subtipoSinCambios || await _db.Productosubtipos.AnyAsync(s =>
                s.Idsubtipo == model.Idsubtipo &&
                s.Idusuario == ownerId &&
                s.Idtipoproducto == model.TipoProducto &&
                s.Estado == "A");

            if (!subtipoOk) return "La subcategoría seleccionada no es válida para su usuario.";
        }

        return null;
    }

    private static void NormalizarProducto(ProductoUpsertDto model)
    {
        model.Nombre = model.Nombre?.Trim();
        model.CodigoPrincipal = model.CodigoPrincipal?.Trim();
        model.TipoCompravena = model.TipoCompravena?.Trim().ToUpperInvariant();
        model.Codigoimpuesto = string.IsNullOrWhiteSpace(model.Codigoimpuesto) ? null : model.Codigoimpuesto.Trim();
        model.Porcentajeimpuesto = string.IsNullOrWhiteSpace(model.Porcentajeimpuesto) ? null : model.Porcentajeimpuesto.Trim();
        model.Observacion = model.Observacion?.Trim();
        model.ValorUnitario = NormalizarMonto(model.ValorUnitario);
        model.Precio2 = NormalizarMonto(model.Precio2);
        model.Precio3 = NormalizarMonto(model.Precio3);

        if (!model.Precio2.HasValue && model.Precio3.HasValue)
        {
            model.Precio2 = model.Precio3;
            model.Precio3 = null;
        }
    }

    private static decimal? NormalizarMonto(decimal? valor)
    {
        if (!valor.HasValue)
        {
            return null;
        }

        return decimal.Round(valor.Value, 2, MidpointRounding.AwayFromZero);
    }

    #endregion
}
