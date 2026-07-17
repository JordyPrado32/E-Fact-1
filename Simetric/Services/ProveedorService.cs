using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Services
{
    public class ProveedorService : IProveedorService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public ProveedorService(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        public async Task<List<Proveedor>> GetAllAsync()
        {
            using var db = _factory.CreateDbContext();
            return await db.Proveedores.AsNoTracking().ToListAsync();
        }

        // Este es tu método original, cámbialo si prefieres usar UpsertAsync
        public async Task<bool> SaveAsync(Proveedor proveedor)
        {
            using var db = _factory.CreateDbContext();
            if (proveedor.idProveedor == 0)
                db.Proveedores.Add(proveedor);
            else
                db.Proveedores.Update(proveedor);

            return await db.SaveChangesAsync() > 0;
        }
        public async Task<List<Identificacion>> GetIdentificacionesAsync()
        {
            using var db = _factory.CreateDbContext();
            // Traemos solo las que estén activas para el Dropdown
            return await db.Identificacion
                .Where(x => x.Estado == true)
                .OrderBy(x => x.IdeDescripcion)
                .ToListAsync();
        }
        public async Task<bool> DeleteAsync(int id)
        {
            using var db = _factory.CreateDbContext();
            var item = await db.Proveedores.FindAsync(id);
            if (item == null) return false;
            db.Proveedores.Remove(item);
            return await db.SaveChangesAsync() > 0;
        }

        public async Task<Proveedor?> GetByIdAsync(int id)
        {
            // CORRECCIÓN: Usar _factory en lugar de _contextFactory
            using var db = _factory.CreateDbContext();
            return await db.Proveedores.FindAsync(id);
        }

        public async Task<bool> UpsertAsync(Proveedor proveedor)
        {
            using var db = _factory.CreateDbContext();

            if (proveedor.idProveedor == 0)
            {
                db.Proveedores.Add(proveedor);
            }
            else
            {
                db.Proveedores.Update(proveedor);
            }

            // Guardamos los cambios y devolvemos true si se afectó al menos una fila
            var result = await db.SaveChangesAsync();
            return result > 0;
        }
    }
}