using Microsoft.EntityFrameworkCore;
using Simetric.Models; // Ajusta al namespace de tu proyecto
using Simetric.Data;   // Ajusta donde esté tu DbContext

namespace Simetric.Services
{
    public class TipoClienteService
    {
        private readonly AppDbContext _context; // Reemplaza con el nombre de tu contexto

        public TipoClienteService(AppDbContext context)
        {
            _context = context;
        }

        // 1. OBTENER TODOS (LECTURA)
        public async Task<List<Tipocliente>> GetTiposClienteAsync()
        {
            return await _context.Tipoclientes
                .AsNoTracking()
                .OrderBy(t => t.TclCodigo)
                .ToListAsync();
        }

        // 2. BUSCAR POR ID (DETALLES)
        public async Task<Tipocliente?> GetTipoClienteByIdAsync(int id)
        {
            return await _context.Tipoclientes
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TclSec == id);
        }

        // 3. BUSCAR POR CÓDIGO ESPECÍFICO (BÚSQUEDA)
        public async Task<Tipocliente?> GetByCodigoAsync(int codigo)
        {
            return await _context.Tipoclientes
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TclCodigo == codigo);
        }

        // 4. CREAR O EDITAR (UPSERT)
        public async Task<bool> SaveTipoClienteAsync(Tipocliente tipo)
        {
            try
            {
                if (tipo.TclSec == 0)
                {
                    // Crear nuevo
                    _context.Tipoclientes.Add(tipo);
                }
                else
                {
                    // Editar existente
                    _context.Tipoclientes.Update(tipo);
                }

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception)
            {
                // Aquí podrías loguear el error
                return false;
            }
        }

        // 5. ELIMINAR
        public async Task<bool> DeleteTipoClienteAsync(int id)
        {
            try
            {
                var tipo = await _context.Tipoclientes.FindAsync(id);
                if (tipo != null)
                {
                    // OJO: Esto fallará si el tipo ya está asignado a un Cliente 
                    // debido a la restricción de llave foránea.
                    _context.Tipoclientes.Remove(tipo);
                    await _context.SaveChangesAsync();
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // 6. FILTRAR POR DESCRIPCIÓN (BUSCADOR)
        public async Task<List<Tipocliente>> SearchTiposAsync(string criterio)
        {
            if (string.IsNullOrWhiteSpace(criterio))
                return await GetTiposClienteAsync();

            return await _context.Tipoclientes
                .AsNoTracking()
                .Where(t => t.TclDescripcion.Contains(criterio) ||
                            t.TclCodigo.ToString().Contains(criterio))
                .ToListAsync();
        }
    }
}