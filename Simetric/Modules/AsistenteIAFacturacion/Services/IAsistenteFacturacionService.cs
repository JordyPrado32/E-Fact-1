using Simetric.Modules.AsistenteIAFacturacion.DTOs;

namespace Simetric.Modules.AsistenteIAFacturacion.Services;

public interface IAsistenteFacturacionService
{
    Task<ChatFacturaResponse> ProcesarAsync(int userId, ChatFacturaRequest request, CancellationToken cancellationToken = default);
}
