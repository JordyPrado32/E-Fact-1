using Simetric.Modules.AsistenteIAFacturacion.DTOs;

namespace Simetric.Modules.AsistenteIAFacturacion.State;

public sealed class FacturaConversationState
{
    public string SessionId { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string Estado { get; set; } = FacturaConversationStates.SinFactura;
    public string? UltimaIntencion { get; set; }
    public string? UltimaAccionEstructurada { get; set; }
    public bool RequiereConfirmacion { get; set; }
    public bool Emitida { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; } = DateTimeOffset.UtcNow;
    public FacturaDraftDto Draft { get; set; } = new();
    public List<FacturaConversationMessage> Historial { get; set; } = new();
    public PendingSelectionState? SeleccionPendiente { get; set; }
}

public sealed class FacturaConversationMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
}

public static class FacturaConversationStates
{
    public const string SinFactura = "SinFactura";
    public const string BuscandoCliente = "BuscandoCliente";
    public const string ClienteSeleccionado = "ClienteSeleccionado";
    public const string AgregandoItems = "AgregandoItems";
    public const string CalculandoTotales = "CalculandoTotales";
    public const string EsperandoConfirmacion = "EsperandoConfirmacion";
    public const string FacturaEmitida = "FacturaEmitida";
    public const string Cancelado = "Cancelado";
}

public sealed class PendingSelectionState
{
    public string Tipo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public List<SelectionOptionDto> Opciones { get; set; } = new();
    public string? Accion { get; set; }
    public decimal? Cantidad { get; set; }
    public decimal? Monto { get; set; }
    public string? Observacion { get; set; }
    public decimal? DescuentoPorcentaje { get; set; }
    public decimal? DescuentoValor { get; set; }
}
