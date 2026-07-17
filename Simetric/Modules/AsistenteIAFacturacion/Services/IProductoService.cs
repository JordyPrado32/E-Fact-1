using Simetric.Modules.AsistenteIAFacturacion.DTOs;

namespace Simetric.Modules.AsistenteIAFacturacion.Services;

public interface IProductoService
{
    Task<IReadOnlyList<ProductoDto>> BuscarAsync(int userId, string query, CancellationToken cancellationToken = default);
    Task<ProductoDto?> ObtenerAsync(int userId, int productoId, CancellationToken cancellationToken = default);
    Task<(ProductoDto? Producto, string Message)> CrearAsync(int userId, ProductoCreateRequestDto request, CancellationToken cancellationToken = default);
}
