using Simetric.Models;

namespace Simetric.Services
{
    public interface IProveedorService
    {
        Task<List<Proveedor>> GetAllAsync();
        Task<Proveedor?> GetByIdAsync(int id);
        Task<bool> UpsertAsync(Proveedor proveedor);
        Task<bool> DeleteAsync(int id);
        Task<bool> SaveAsync(Proveedor proveedor);
        Task<List<Identificacion>> GetIdentificacionesAsync();

    }
}