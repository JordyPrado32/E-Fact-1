using System.Text.Json.Serialization;

namespace Simetric.Modules.AsistenteIAFacturacion.DTOs;

public sealed class ChatFacturaResponse
{
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("respuesta")]
    public string Respuesta { get; set; } = string.Empty;

    [JsonPropertyName("estado")]
    public string Estado { get; set; } = "SinFactura";

    [JsonPropertyName("estadoVersion")]
    public long EstadoVersion { get; set; }

    [JsonPropertyName("facturaDraft")]
    public FacturaDraftDto FacturaDraft { get; set; } = new();

    [JsonPropertyName("requiereConfirmacion")]
    public bool RequiereConfirmacion { get; set; }

    [JsonPropertyName("emitida")]
    public bool Emitida { get; set; }

    [JsonPropertyName("accionDetectada")]
    public string? AccionDetectada { get; set; }

    [JsonPropertyName("rutaSugerida")]
    public string? RutaSugerida { get; set; }

    [JsonPropertyName("seleccionPendienteTipo")]
    public string? SeleccionPendienteTipo { get; set; }

    [JsonPropertyName("seleccionPendienteMensaje")]
    public string? SeleccionPendienteMensaje { get; set; }

    [JsonPropertyName("opcionesSeleccion")]
    public List<SelectionOptionDto> OpcionesSeleccion { get; set; } = new();

    [JsonPropertyName("progreso")]
    public List<BotProgressStepDto> Progreso { get; set; } = new();

    [JsonPropertyName("datosFaltantes")]
    public List<string> DatosFaltantes { get; set; } = new();

    [JsonPropertyName("operacionPendiente")]
    public PendingOperationDto? OperacionPendiente { get; set; }
}

public sealed class PendingOperationDto
{
    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = string.Empty;

    [JsonPropertyName("resumen")]
    public string Resumen { get; set; } = string.Empty;

    [JsonPropertyName("expiraEn")]
    public DateTimeOffset ExpiraEn { get; set; }
}
