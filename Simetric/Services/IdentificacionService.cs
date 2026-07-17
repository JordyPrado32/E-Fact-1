using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Services
{
    public class IdentificacionService
    {
        private readonly AppDbContext _context;

        public IdentificacionService(AppDbContext context) => _context = context;

        public async Task<List<Identificacion>> GetAllActiveAsync()
        {
            var items = await _context.Identificacion
                .AsNoTracking()
                .Where(i => i.Estado == true)
                .ToListAsync();

            return Ordenar(items).ToList();
        }

        public async Task<Identificacion?> GetByIdAsync(int id)
        {
            return await _context.Identificacion
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.IdeSec == id);
        }

        public async Task<bool> SaveAsync(Identificacion modelo)
        {
            try
            {
                var codigo = (modelo.IdeCodigo ?? string.Empty).Trim();
                var descripcion = (modelo.IdeDescripcion ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(descripcion))
                {
                    return false;
                }

                if (!codigo.All(char.IsDigit))
                {
                    return false;
                }

                var existeCodigo = await _context.Identificacion
                    .AnyAsync(x =>
                        x.IdeSec != modelo.IdeSec &&
                        ((x.IdeCodigo ?? string.Empty).Trim() == codigo));

                if (existeCodigo)
                {
                    return false;
                }

                var descripcionNormalizada = descripcion.ToUpper();

                var existeDescripcion = await _context.Identificacion
                    .AnyAsync(x =>
                        x.IdeSec != modelo.IdeSec &&
                        x.IdeDescripcion != null &&
                        x.IdeDescripcion.Trim().ToUpper() == descripcionNormalizada);

                if (existeDescripcion)
                {
                    return false;
                }

                var esNuevo = modelo.IdeSec == 0;

                if (esNuevo)
                {
                    var nuevo = new Identificacion
                    {
                        IdeCodigo = codigo,
                        IdeDescripcion = descripcion,
                        Estado = modelo.Estado ?? true
                    };

                    _context.Identificacion.Add(nuevo);
                }
                else
                {
                    var actual = await _context.Identificacion
                        .FirstOrDefaultAsync(x => x.IdeSec == modelo.IdeSec);

                    if (actual is null)
                    {
                        return false;
                    }

                    actual.IdeCodigo = codigo;
                    actual.IdeDescripcion = descripcion;
                    actual.Estado = modelo.Estado ?? actual.Estado ?? true;
                }

                var changes = await _context.SaveChangesAsync();
                return esNuevo ? changes > 0 : changes >= 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeleteLogicalAsync(int id)
        {
            try
            {
                var item = await _context.Identificacion.FindAsync(id);
                if (item == null)
                {
                    return false;
                }

                item.Estado = false;
                await _context.SaveChangesAsync();
                _context.Entry(item).State = EntityState.Detached;

                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<Identificacion>> SearchAsync(string term)
        {
            var query = _context.Identificacion
                .AsNoTracking()
                .Where(i => i.Estado == true);

            if (!string.IsNullOrWhiteSpace(term))
            {
                var filtro = term.Trim();
                query = query.Where(i =>
                    EF.Functions.Like(i.IdeCodigo, $"%{filtro}%") ||
                    (i.IdeDescripcion != null && EF.Functions.Like(i.IdeDescripcion, $"%{filtro}%")));
            }

            var items = await query.ToListAsync();
            return Ordenar(items).ToList();
        }

        private static IEnumerable<Identificacion> Ordenar(IEnumerable<Identificacion> items) =>
            items
                .OrderBy(i => ParseCodigoNumerico(i.IdeCodigo))
                .ThenBy(i => (i.IdeCodigo ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => (i.IdeDescripcion ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);

        private static int ParseCodigoNumerico(string? codigo) =>
            int.TryParse(codigo?.Trim(), out var numero) ? numero : int.MaxValue;
    }
}
