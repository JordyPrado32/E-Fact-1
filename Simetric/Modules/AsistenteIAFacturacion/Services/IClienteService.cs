using Simetric.Modules.AsistenteIAFacturacion.DTOs;

namespace Simetric.Modules.AsistenteIAFacturacion.Services;

public interface IClienteService
{
    Task<IReadOnlyList<ClienteDto>> BuscarAsync(int userId, string query, CancellationToken cancellationToken = default);
    Task<ClienteDto?> ObtenerAsync(int userId, int clienteId, CancellationToken cancellationToken = default);
    Task<(ClienteDto? Cliente, string Message)> CrearAsync(int userId, ClienteCreateRequestDto request, CancellationToken cancellationToken = default);
}
