using Microsoft.EntityFrameworkCore;
using Simetric.Models;
using Simetric.Data;

namespace Simetric.Services
{
public class ConfiguracionService
{
    private const int MaxPuntosEmisionPorCuenta = 50;
    private static readonly SemaphoreSlim CajaSistemaSchemaLock = new(1, 1);
    private static bool _cajaSistemaSchemaEnsured;
    private readonly AppDbContext _context;
        public ConfiguracionService(AppDbContext context) => _context = context;

        // Método auxiliar para limpiar el rastreo de memoria de EF
        private void DetachEntity<T>(T entity) where T : class
        {
            _context.Entry(entity).State = EntityState.Detached;
        }

        // ==========================================
        // MÉTODOS FORMAS DE PAGO (Primary Key: Id)
        // ==========================================

        public async Task<List<FormasPago>> GetFormasPagoActiveAsync() =>
            await _context.FormasPago.AsNoTracking().Where(f => f.Estado == true).ToListAsync();

        public async Task<FormasPago?> GetFormaPagoByIdAsync(int id) =>
            await _context.FormasPago.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);

        public async Task<bool> SaveFormasPagoAsync(FormasPago modelo, int? idUsuarioAuditoria = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modelo.Codigo) ||
                    string.IsNullOrWhiteSpace(modelo.Descripcion) ||
                    string.IsNullOrWhiteSpace(modelo.DescripcionSri))
                    return false;

                modelo.Codigo = modelo.Codigo.Trim();
                modelo.Descripcion = modelo.Descripcion.Trim();
                modelo.DescripcionSri = modelo.DescripcionSri.Trim();

                if (!modelo.Codigo.All(char.IsDigit))
                    return false;

                if (modelo.Codigo.Length > 2)
                    return false;

                if (modelo.TipoVenta != true && modelo.TipoCompra != true)
                    return false;

                bool existeCodigo = await _context.FormasPago
                    .AnyAsync(x => x.Codigo == modelo.Codigo &&
                                   x.Id != modelo.Id &&
                                   x.Estado == true);

                if (existeCodigo)
                    return false;

                bool existeDescripcion = await _context.FormasPago
                    .AnyAsync(x => x.Descripcion != null &&
                                   modelo.Descripcion != null &&
                                   x.Descripcion.ToLower() == modelo.Descripcion.ToLower() &&
                                   x.Id != modelo.Id &&
                                   x.Estado == true);

                if (existeDescripcion)
                    return false;

                if (modelo.Id == 0)
                {
                    modelo.Estado = true;
                    _context.FormasPago.Add(modelo);
                }
                else
                {
                    _context.Entry(modelo).State = EntityState.Modified;
                }

                bool success = await _context.SaveChangesAsync() > 0;

                if (success)
                {
                    DetachEntity(modelo);
                }

                return success;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeleteFormasPagoLogicalAsync(int id, int? idUsuarioAuditoria = null)
        {
            var item = await _context.FormasPago.FindAsync(id);
            if (item == null) return false;

            var copiaPrevia = await _context.FormasPago.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);

            item.Estado = false;
            var success = await _context.SaveChangesAsync() > 0;

            if (success)
            {
                DetachEntity(item);
            }
            return success;
        }

        // ==========================================
        // MÉTODOS TIPO DOCUMENTO (Primary Key: Sec)
        // ==========================================

        public async Task<List<TipoDocumento>> GetTipoDocumentoAsync() =>
            await _context.TipoDocumento.AsNoTracking().ToListAsync();

        public async Task<TipoDocumento?> GetTipoDocBySecAsync(int sec) =>
            await _context.TipoDocumento.AsNoTracking().FirstOrDefaultAsync(t => t.Sec == sec);

        public async Task<bool> SaveTipoDocumentoAsync(TipoDocumento modelo, int? idUsuarioAuditoria = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modelo.Codigo) ||
                    string.IsNullOrWhiteSpace(modelo.Descripcion))
                    return false;

                modelo.Codigo = modelo.Codigo.Trim();
                modelo.Descripcion = modelo.Descripcion.Trim();

                bool existeCodigo = await _context.TipoDocumento
                    .AnyAsync(x => x.Codigo == modelo.Codigo &&
                                   x.Sec != modelo.Sec);

                if (existeCodigo)
                    return false;

                bool existeDescripcion = await _context.TipoDocumento
                    .AnyAsync(x => x.Descripcion != null &&
                                   modelo.Descripcion != null &&
                                   x.Descripcion.ToLower() == modelo.Descripcion.ToLower() &&
                                   x.Sec != modelo.Sec);

                if (existeDescripcion)
                    return false;

                TipoDocumento? original = null;
                string accion = modelo.Sec == 0 ? "CREAR TIPO DOC" : "MODIFICAR TIPO DOC";

                if (modelo.Sec == 0)
                {
                    _context.TipoDocumento.Add(modelo);
                }
                else
                {
                    original = await _context.TipoDocumento
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Sec == modelo.Sec);

                    _context.Entry(modelo).State = EntityState.Modified;
                }

                bool success = await _context.SaveChangesAsync() > 0;

                if (success)
                {
                    DetachEntity(modelo);
                }

                return success;
            }
            catch
            {
                return false;
            }
        }
        public async Task<List<Caja>> GetCajasPorUsuarioAsync(int idUsuarioActual)
        {
            var usuario = await _context.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.IdUsuario == idUsuarioActual);

            if (usuario == null) return new List<Caja>();

            // Si es Asociado (tiene un jefe)
            if (usuario.idJefe != null && usuario.estadoAsociado == true)
            {
                return await _context.Caja
                    .AsNoTracking()
                    .Where(c => c.IdUsuario == idUsuarioActual && c.Estado == true)
                    .OrderBy(c => c.NumCaja)
                    .ToListAsync();
            }

            // Si es Jefe (él es el dueño)
            var idsCuenta = await GetUsuariosCuentaIdsAsync(idUsuarioActual);
            return await _context.Caja
                .AsNoTracking()
                .Where(c => c.Estado == true && idsCuenta.Contains(c.IdUsuario ?? 0))
                .OrderBy(c => c.NumCaja)
                .ToListAsync();
        }
        public async Task<List<Usuario>> GetAsociadosByJefeAsync(int idJefe)
        {
            return await _context.Usuarios
                .AsNoTracking()
                .Where(u => u.idJefe == idJefe && u.estadoAsociado == true)
                .OrderBy(u => u.Nombres)
                .ToListAsync();
        }

        public async Task<List<Usuario>> GetUsuariosCuentaAsync(int idUsuario)
        {
            var idsCuenta = await GetUsuariosCuentaIdsAsync(idUsuario);
            if (!idsCuenta.Any())
            {
                return new List<Usuario>();
            }

            return await _context.Usuarios
                .AsNoTracking()
                .Where(u => idsCuenta.Contains(u.IdUsuario))
                .OrderBy(u => u.Nombres)
                .ThenBy(u => u.Apellidos)
                .ToListAsync();
        }
        // Método para que el jefe asigne la caja a un asociado
        public async Task<bool> AsignarCajaAAsociadoAsync(int idCaja, int idAsociado)
        {
            var caja = await _context.Caja.FindAsync(idCaja);
            if (caja == null) return false;

            caja.IdUsuario = idAsociado; // Aquí cambiamos quién la opera
            return await _context.SaveChangesAsync() > 0;
        }
        public async Task<bool> DeleteTipoDocAsync(int sec, int? idUsuarioAuditoria = null)
        {
            try
            {
                var item = await _context.TipoDocumento.FindAsync(sec);
                if (item == null) return false;

                var copiaPrevia = await _context.TipoDocumento.AsNoTracking().FirstOrDefaultAsync(t => t.Sec == sec);

                _context.TipoDocumento.Remove(item);
                var success = await _context.SaveChangesAsync() > 0;

                return success;
            }
            catch { return false; }
        }

        // ==========================================
        // MÉTODOS CAJA (Primary Key: Sec)
        // ==========================================

        public async Task<List<Caja>> GetCajasAsync()
        {
            return await _context.Caja.AsNoTracking().ToListAsync();
        }

        public async Task<List<Caja>> GetCajasCuentaActivasAsync(int idUsuario)
        {
            await EnsureCajaSistemaSchemaAsync();

            var idsCuenta = await GetUsuariosCuentaIdsAsync(idUsuario);
            if (!idsCuenta.Any())
            {
                return new List<Caja>();
            }

            return await _context.Caja
                .AsNoTracking()
                .Where(c => c.Estado == true &&
                            c.EsCajaSistema != true &&
                            idsCuenta.Contains(c.IdUsuario ?? 0))
                .OrderBy(c => c.NumCaja)
                .ThenBy(c => c.Sec)
                .ToListAsync();
        }

        public async Task<List<Caja>> GetCajasSistemaActivasAsync()
        {
            await EnsureCajaSistemaSchemaAsync();

            return await _context.Caja
                .AsNoTracking()
                .Where(c => c.Estado == true && c.EsCajaSistema == true)
                .OrderBy(c => c.NumCaja)
                .ThenBy(c => c.Sec)
                .ToListAsync();
        }

        public async Task<Caja?> GetCajaBySecAsync(int sec) =>
            await _context.Caja.AsNoTracking().FirstOrDefaultAsync(c => c.Sec == sec);

        public async Task<bool> SaveCajaAsync(Caja modelo, int? idUsuarioAuditoria = null)
        {
            try
            {
                await EnsureCajaSistemaSchemaAsync();

                // 1. Validaciones de Integridad Básica
                if ((modelo.NumCaja ?? 0) <= 0) return false;
                if ((modelo.IdSucursal ?? 0) <= 0) return false;
                if ((modelo.IdEmpresa ?? 0) <= 0) return false;

                // 2. Limpieza de Strings
                modelo.SerieFactura = modelo.SerieFactura?.Trim();
                modelo.SerieNotasCred = modelo.SerieNotasCred?.Trim();
                modelo.SerieGuia = modelo.SerieGuia?.Trim();
                modelo.SerieCompras = modelo.SerieCompras?.Trim();
                modelo.SerieDebitos = modelo.SerieDebitos?.Trim();

                var idUsuarioContexto = idUsuarioAuditoria.GetValueOrDefault() > 0
                    ? idUsuarioAuditoria!.Value
                    : (modelo.IdUsuario ?? 0);

                if (idUsuarioContexto <= 0)
                {
                    return false;
                }

                var regexSerie = new System.Text.RegularExpressions.Regex(@"^\d{3}-\d{3}$");
                var esCajaSistema = modelo.EsCajaSistema == true;

                // 3. Validación de Formato de Series
                if (string.IsNullOrWhiteSpace(modelo.SerieFactura) || !regexSerie.IsMatch(modelo.SerieFactura)) return false;
                if (string.IsNullOrWhiteSpace(modelo.SerieNotasCred) || !regexSerie.IsMatch(modelo.SerieNotasCred)) return false;
                if (string.IsNullOrWhiteSpace(modelo.SerieGuia) || !regexSerie.IsMatch(modelo.SerieGuia)) return false;
                // El SRI no permite puntos de emisión 000
                if (modelo.SerieFactura.EndsWith("-000") ||
                    modelo.SerieNotasCred.EndsWith("-000") ||
                    modelo.SerieGuia.EndsWith("-000"))
                {
                    return false; // Bloquea el guardado si detecta 000
                }
                // 👆 HASTA AQUÍ LO NUEVO

                var usuariosCuenta = esCajaSistema
                    ? new List<int>()
                    : await GetUsuariosCuentaIdsAsync(idUsuarioContexto);

                if (modelo.Sec == 0)
                {
                    // ---- LÓGICA PARA NUEVA CAJA ----

                    // Cada caja representa un punto de emision. Una cuenta puede administrar
                    // varios puntos, incluso cuando todos son operados por el usuario titular.
                    int conteoCajasActuales = await _context.Caja
                        .CountAsync(x =>
                            x.Estado == true &&
                            (esCajaSistema
                                ? x.EsCajaSistema == true
                                : x.EsCajaSistema != true && usuariosCuenta.Contains(x.IdUsuario ?? 0)));

                    if (conteoCajasActuales >= MaxPuntosEmisionPorCuenta) return false;

                    // 6. Validar que el número de caja sea único en la cuenta
                    bool existeNumCaja = await _context.Caja
                        .AnyAsync(x =>
                            x.Estado == true &&
                            x.NumCaja == modelo.NumCaja &&
                            (esCajaSistema
                                ? x.EsCajaSistema == true
                                : x.EsCajaSistema != true && usuariosCuenta.Contains(x.IdUsuario ?? 0)));

                    if (existeNumCaja) return false;

                    // 7. Unicidad de Series dentro de la misma cuenta
                    //    No debe bloquear una caja nueva solo porque otra cuenta del sistema
                    //    use la misma serie base 001-001.
                    // DESPUÉS
                    bool serieDuplicada = await _context.Caja.AnyAsync(x =>
                        x.Estado == true &&
                        (esCajaSistema
                            ? x.EsCajaSistema == true
                            : x.IdUsuario.HasValue && x.EsCajaSistema != true && usuariosCuenta.Contains(x.IdUsuario.Value)) &&
                        x.Sec != modelo.Sec &&          // ← excluye la misma caja
                        (x.SerieFactura == modelo.SerieFactura ||
                         x.SerieNotasCred == modelo.SerieNotasCred));

                    if (serieDuplicada) return false;
                    modelo.Estado = true;
                    _context.Caja.Add(modelo);
                }
                else
                {
                    // ---- LÓGICA PARA ACTUALIZAR (Caja 1 o existentes) ----

                    // Verificamos si la serie que intenta poner ya la tiene OTRA caja de la misma cuenta
                    bool serieEnUsoPorOtro = await _context.Caja.AnyAsync(x =>
                        x.Estado == true &&
                        (esCajaSistema
                            ? x.EsCajaSistema == true
                            : x.IdUsuario.HasValue && x.EsCajaSistema != true && usuariosCuenta.Contains(x.IdUsuario.Value)) &&
                        x.Sec != modelo.Sec &&
                        (x.SerieFactura == modelo.SerieFactura || x.SerieNotasCred == modelo.SerieNotasCred));

                    if (serieEnUsoPorOtro) return false;

                    _context.Caja.Update(modelo);
                }

                bool success = await _context.SaveChangesAsync() > 0;

                if (success)
                {
                    DetachEntity(modelo);
                }

                return success;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeleteCajaAsync(int sec, int? idUsuarioAuditoria = null)
        {
            try
            {
                var item = await _context.Caja.FindAsync(sec);
                if (item == null) return false;

                var copiaPrevia = await _context.Caja.AsNoTracking().FirstOrDefaultAsync(c => c.Sec == sec);

                _context.Caja.Remove(item);
                var success = await _context.SaveChangesAsync() > 0;

                return success;
            }
            catch { return false; }
        }

        public async Task<bool> MarcarCajaPrincipalAsync(int sec, int idUsuarioContexto, bool soloCajasSistema = false)
        {
            if (sec <= 0 || idUsuarioContexto <= 0)
            {
                return false;
            }

            await EnsureCajaSistemaSchemaAsync();

            var usuariosCuenta = await GetUsuariosCuentaIdsAsync(idUsuarioContexto);
            var objetivo = await _context.Caja.FirstOrDefaultAsync(c =>
                c.Sec == sec &&
                c.Estado == true &&
                (soloCajasSistema
                    ? c.EsCajaSistema == true
                    : c.IdUsuario.HasValue && c.EsCajaSistema != true && usuariosCuenta.Contains(c.IdUsuario.Value)));

            if (objetivo == null)
            {
                return false;
            }

            var principalActual = await _context.Caja
                .Where(c =>
                    c.Estado == true &&
                    (soloCajasSistema
                        ? c.EsCajaSistema == true
                        : c.IdUsuario.HasValue && c.EsCajaSistema != true && usuariosCuenta.Contains(c.IdUsuario.Value)) &&
                    c.Sec != objetivo.Sec &&
                    c.NumCaja == 1)
                .FirstOrDefaultAsync();

            if (objetivo.NumCaja == 1)
            {
                return true;
            }

            var numeroObjetivo = objetivo.NumCaja ?? 1;
            objetivo.NumCaja = 1;

            if (principalActual != null)
            {
                principalActual.NumCaja = numeroObjetivo;
            }

            return await _context.SaveChangesAsync() > 0;
        }

        private async Task EnsureCajaSistemaSchemaAsync()
        {
            if (_cajaSistemaSchemaEnsured)
            {
                return;
            }

            await CajaSistemaSchemaLock.WaitAsync();
            try
            {
                if (_cajaSistemaSchemaEnsured)
                {
                    return;
                }

                await _context.Database.ExecuteSqlRawAsync("""
IF COL_LENGTH('dbo.CAJA', 'es_caja_sistema') IS NULL
BEGIN
    ALTER TABLE [dbo].[CAJA] ADD [es_caja_sistema] BIT NOT NULL CONSTRAINT [DF_CAJA_es_caja_sistema] DEFAULT(0);
END
""");

                _cajaSistemaSchemaEnsured = true;
            }
            finally
            {
                CajaSistemaSchemaLock.Release();
            }
        }

        // ==========================================
        // MÉTODOS DE APOYO (Usuarios)
        // ==========================================

        public async Task<List<Usuario>> GetUsuariosAsync() =>
            await _context.Usuarios.AsNoTracking().OrderBy(u => u.Nombres).ToListAsync();

        public async Task<bool> UsuarioTieneCajaAsync(int idUsuario, int currentSec) =>
            idUsuario > 0 && await _context.Caja.AsNoTracking()
                .AnyAsync(c => c.Estado == true && c.IdUsuario == idUsuario && c.Sec != currentSec);

        private async Task<int> GetTitularCuentaIdAsync(int idUsuario)
        {
            if (idUsuario <= 0)
            {
                return 0;
            }

            var usuario = await _context.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.IdUsuario == idUsuario);

            if (usuario == null)
            {
                return 0;
            }

            return usuario.estadoAsociado == true && usuario.idJefe is > 0
                ? usuario.idJefe.Value
                : usuario.IdUsuario;
        }

        private async Task<List<int>> GetUsuariosCuentaIdsAsync(int idUsuario)
        {
            var idTitular = await GetTitularCuentaIdAsync(idUsuario);
            if (idTitular <= 0)
            {
                return new List<int>();
            }

            return await _context.Usuarios
                .AsNoTracking()
                .Where(u => u.IdUsuario == idTitular || (u.idJefe == idTitular && u.estadoAsociado == true))
                .Select(u => u.IdUsuario)
                .ToListAsync();
        }
    }
}
