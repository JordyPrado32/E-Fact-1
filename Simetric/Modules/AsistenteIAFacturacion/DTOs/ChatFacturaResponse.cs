using System.Text.Json.Serialization;

namespace Simetric.Modules.AsistenteIAFacturacion.DTOs;

public sealed class ChatFacturaResponse
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("respuesta")]
    public string Respuesta { get; set; } = string.Empty;

    [JsonPropertyName("estado")]
    public string Estado { get; set; } = "SinFactura";

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
}
