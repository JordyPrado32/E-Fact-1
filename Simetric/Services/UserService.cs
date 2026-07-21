using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Services
{
    public class UserService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public UserService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // --- MÉTODOS DE LECTURA ---

        /// <summary>
        /// Obtiene la lista de todos los usuarios incluyendo sus relaciones de navegación.
        /// </summary>
        public async Task<List<Usuario>> GetUsuariosAsync(int currentUserId, int currentTipoUsuario)
        {
            using var context = await _dbFactory.CreateDbContextAsync();

            // Creamos la consulta base con los includes y el ordenamiento
            var query = context.Usuarios
                .Include(u => u.IdTipoUsuarioNavigation)
                .Include(u => u.IdTipoIdentificacionNavigation)
                .Where(u => u.Estado == true)
                .AsQueryable();

            // 1. Si es Administrador (ID 2 según tu tabla), no filtramos nada (ve todos)
            if (currentTipoUsuario == 2)
            {
                // No aplicamos filtros adicionales
            }
            // 2. Si es Usuario (ID 1), aplicamos el filtro de jerarquía
            else if (currentTipoUsuario == 1)
            {
                // El usuario ve su propio registro O registros donde él sea el jefe (asociados)
                query = query.Where(u => u.IdUsuario == currentUserId || u.idJefe == currentUserId);
            }
            else
            {
                // Opcional: Para otros roles (Soporte, Firma, etc.), podrías definir 
                // que solo vean su propio perfil para evitar fugas de información
                query = query.Where(u => u.IdUsuario == currentUserId);
            }

            return await query
                .OrderByDescending(u => u.FechaCreacion)
                .ToListAsync();
        }

        /// <summary>
        /// Obtiene los roles de usuario activos.
        /// </summary>
        public async Task<List<TipoUsuario>> GetTiposUsuarioAsync()
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            return await context.TipoUsuario
                .Where(t => t.Estado == true)
                .ToListAsync();
        }

        /// <summary>
        /// Obtiene los tipos de identificación activos.
        /// </summary>
        public async Task<List<TipoIdentificacion>> GetTiposIdentificacionAsync()
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            return await context.TipoIdentificacion
                .Where(t => t.Estado == true)
                .ToListAsync();
        }

        /// <summary>
        /// Obtiene un usuario puntual sin cargar navegaciones para refrescar la sesión actual.
        /// </summary>
        public async Task<Usuario?> GetUsuarioByIdAsync(int id)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            return await context.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.IdUsuario == id);
        }

        public async Task<UsuarioCajaDto?> GetCajaUsuarioAsync(int idUsuario)
        {
            if (idUsuario <= 0)
                return null;

            using var context = await _dbFactory.CreateDbContextAsync();
            var usuariosSincronizados = await GetUsuariosSincronizadosPorEmisorRucAsync(context, idUsuario);
            var caja = await context.Caja
                .AsNoTracking()
                .Where(c => c.Estado == true && c.IdUsuario.HasValue && usuariosSincronizados.Contains(c.IdUsuario.Value))
                .OrderBy(c => c.NumCaja)
                .ThenBy(c => c.Sec)
                .FirstOrDefaultAsync();

            if (caja == null)
                return null;

            var serie = NormalizarSerie(caja.SerieFactura, caja.NumCaja ?? 1);
            return new UsuarioCajaDto
            {
                CajaSec = caja.Sec,
                NumeroCaja = caja.NumCaja ?? 0,
                Serie = serie
            };
        }

        public async Task GuardarSerieCajaUsuarioAsync(int idUsuario, string? serie)
        {
            var regexSerie = new System.Text.RegularExpressions.Regex(@"^\d{3}-\d{3}$");
            if (string.IsNullOrWhiteSpace(serie) || !regexSerie.IsMatch(serie))
            {
                throw new Exception("El formato de la serie debe ser 000-000");
            }

            // 2. BLOQUEO NUEVO: No permitir 000 en establecimiento o punto de emisión
            if (serie == "000-000" || serie.StartsWith("000-") || serie.EndsWith("-000"))
            {
                throw new Exception("La serie 000-000 no es válida para el SRI. Use valores desde 001.");
            }
            if (idUsuario <= 0)
                return;

            var serieNormalizada = NormalizarSerie(serie, 1);
            if (!System.Text.RegularExpressions.Regex.IsMatch(serieNormalizada, @"^\d{3}-\d{3}$"))
                throw new InvalidOperationException("La serie debe tener el formato 001-001.");

            using var context = await _dbFactory.CreateDbContextAsync();
            var usuariosSincronizados = await GetUsuariosSincronizadosPorEmisorRucAsync(context, idUsuario);
            var cajas = await context.Caja
                .Where(c => c.Estado == true && c.IdUsuario.HasValue && usuariosSincronizados.Contains(c.IdUsuario.Value))
                .OrderBy(c => c.NumCaja)
                .ThenBy(c => c.Sec)
                .ToListAsync();

            if (cajas.Count == 0)
            {
                throw new InvalidOperationException("El usuario no tiene una caja activa asignada.");
            }

            foreach (var caja in cajas)
            {
                caja.SerieFactura = serieNormalizada;
                caja.SerieNotasCred = serieNormalizada;
                caja.SerieGuia = serieNormalizada;
                caja.SerieCompras = serieNormalizada;
                caja.SerieDebitos = serieNormalizada;
            }

            await context.SaveChangesAsync();
        }

        // --- GUARDAR / EDITAR ---

        /// <summary>
        /// Crea o actualiza un usuario en la base de datos.
        /// </summary>
        public async Task SaveUsuarioAsync(Usuario usuario)
        {
            using var context = await _dbFactory.CreateDbContextAsync();

            if (usuario.IdUsuario == 0)
            {
                // Configuración para nuevo usuario
                usuario.FechaCreacion = DateTime.Now;
                usuario.Estado = true;
                usuario.IntentosFallidos = 0;
                usuario.CuentaBloqueada = false;
                usuario.SaldoDocumentos = usuario.SaldoDocumentos > 0 ? usuario.SaldoDocumentos : 9;

                // Nota: La ClaveTemporal y PasswordHash ya deben venir asignados desde la UI/Controlador
                context.Usuarios.Add(usuario);
            }
            else
            {
                // Buscamos el usuario existente para actualizar solo los campos permitidos
                var existingUser = await context.Usuarios.FindAsync(usuario.IdUsuario);
                if (existingUser != null)
                {
                    // Actualización de datos personales y de contacto
                    existingUser.Nombres = usuario.Nombres;
                    existingUser.Apellidos = usuario.Apellidos;
                    existingUser.Email = usuario.Email;
                    existingUser.Celular = usuario.Celular;
                    existingUser.FechaNacimiento = usuario.FechaNacimiento;
                    existingUser.DireccionEmpresa = usuario.DireccionEmpresa;

                    // Actualización de campos de identidad
                    existingUser.Identificacion = usuario.Identificacion;
                    existingUser.IdTipoIdentificacion = usuario.IdTipoIdentificacion;
                    existingUser.TipoCliente = usuario.TipoCliente;

                    // Personalización
                    existingUser.AvatarUrl = usuario.AvatarUrl; // Campo añadido

                    // Roles y Seguridad
                    existingUser.IdTipoUsuario = usuario.IdTipoUsuario;
                    existingUser.Estado = usuario.Estado;
                    existingUser.CuentaBloqueada = usuario.CuentaBloqueada;

                    // Gestión de Seguridad: Solo actualizamos el Hash si cambió (ej. reset de clave o desbloqueo)
                    if (usuario.PasswordHash != null && existingUser.PasswordHash != usuario.PasswordHash)
                    {
                        existingUser.PasswordHash = usuario.PasswordHash;
                        existingUser.ClaveTemporal = usuario.ClaveTemporal;
                    }

                    context.Usuarios.Update(existingUser);
                }
            }

            await context.SaveChangesAsync();
        }

        // --- OPERACIONES DE SOPORTE ---

        /// <summary>
        /// Realiza un borrado lógico (cambio de estado) del usuario.
        /// </summary>
        public async Task<bool> SoftDeleteAsync(int id)
        {
            using var context = await _dbFactory.CreateDbContextAsync();

            var user = await context.Usuarios.FindAsync(id);
            if (user is null)
            {
                return false;
            }

            user.Estado = false;
            user.estadoAsociado = false;
            user.Email = $"NULL-{user.IdUsuario}@deleted.local";
            user.CuentaBloqueada = false;
            user.IntentosFallidos = 0;
            user.FechaDesbloqueo = null;
            user.TokenRecuperacion = null;
            user.FechaExpiracionToken = null;

            context.Usuarios.Update(user);
            await context.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Desbloquea la cuenta manualmente y resetea los contadores de seguridad.
        /// </summary>
        public async Task UnlockAccountAsync(int id)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            var user = await context.Usuarios.FindAsync(id);
            if (user != null)
            {
                user.CuentaBloqueada = false;
                user.IntentosFallidos = 0;
                user.FechaDesbloqueo = null;

                // La actualización de la clave debe realizarse mediante SaveUsuarioAsync
                // después de generar el nuevo hash en el componente.
                await context.SaveChangesAsync();
            }
        }

        private static string NormalizarSerie(string? serie, int numeroCaja)
        {
            var digits = new string((serie ?? string.Empty).Where(char.IsDigit).ToArray());
            var establecimiento = digits.Length >= 3 ? digits[..3] : "001";
            var punto = digits.Length >= 6 ? digits.Substring(3, 3) : numeroCaja.ToString("D3");
            return $"{establecimiento.PadLeft(3, '0')}-{punto.PadLeft(3, '0')}";
        }

        private static async Task<List<int>> GetUsuariosSincronizadosPorEmisorRucAsync(AppDbContext context, int idUsuario)
        {
            var usuarios = await GetUsuariosCuentaIdsAsync(context, idUsuario);
            if (!usuarios.Contains(idUsuario))
            {
                usuarios.Add(idUsuario);
            }

            var rucs = await context.Emisores
                .AsNoTracking()
                .Where(e =>
                    e.Estado &&
                    !e.EsEmisorSistema &&
                    e.IdUsuario.HasValue &&
                    usuarios.Contains(e.IdUsuario.Value) &&
                    e.Ruc != null &&
                    e.Ruc != string.Empty)
                .Select(e => e.Ruc!.Trim())
                .Distinct()
                .ToListAsync();

            if (rucs.Count == 0)
            {
                return usuarios.Distinct().ToList();
            }

            var usuariosPorRuc = await context.Emisores
                .AsNoTracking()
                .Where(e =>
                    e.Estado &&
                    !e.EsEmisorSistema &&
                    e.IdUsuario.HasValue &&
                    e.Ruc != null &&
                    rucs.Contains(e.Ruc.Trim()))
                .Select(e => e.IdUsuario!.Value)
                .Distinct()
                .ToListAsync();

            return usuarios
                .Concat(usuariosPorRuc)
                .Distinct()
                .ToList();
        }

        private static async Task<List<int>> GetUsuariosCuentaIdsAsync(AppDbContext context, int idUsuario)
        {
            if (idUsuario <= 0)
            {
                return new List<int>();
            }

            var usuario = await context.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.IdUsuario == idUsuario);

            if (usuario == null)
            {
                return new List<int> { idUsuario };
            }

            var titularId = usuario.estadoAsociado == true && usuario.idJefe is > 0
                ? usuario.idJefe.Value
                : usuario.IdUsuario;

            return await context.Usuarios
                .AsNoTracking()
                .Where(u => u.IdUsuario == titularId || (u.idJefe == titularId && u.estadoAsociado == true))
                .Select(u => u.IdUsuario)
                .ToListAsync();
        }
    }

    public sealed class UsuarioCajaDto
    {
        public int CajaSec { get; set; }
        public int NumeroCaja { get; set; }
        public string Serie { get; set; } = string.Empty;
    }
}
