using Simetric.Modules.AsistenteIAFacturacion.DTOs;
using Simetric.Modules.AsistenteIAFacturacion.State;

namespace Simetric.Modules.AsistenteIAFacturacion.Services;

public interface IOpenAIAsistenteService
{
    Task<OpenAIAsistenteResult> ProcesarAsync(FacturaConversationState state, string mensaje, CancellationToken cancellationToken = default);
    Task<OpenAIAsistenteResult?> TryProcesarRapidoAsync(FacturaConversationState state, string mensaje, CancellationToken cancellationToken = default);
    OpenAIDiagnosticsDto GetDiagnostics();
}

public sealed class OpenAIAsistenteResult
{
    public string Respuesta { get; set; } = string.Empty;
    public string? AccionDetectada { get; set; }
    public string? RutaSugerida { get; set; }
}
