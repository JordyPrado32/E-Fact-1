using System.Text.Json;
using Simetric.Modules.AsistenteIAFacturacion.DTOs;
using Simetric.Modules.AsistenteIAFacturacion.State;

namespace Simetric.Modules.AsistenteIAFacturacion.Tools;

public sealed class ToolDispatcher
{
    private readonly FacturacionTools _tools;

    public ToolDispatcher(FacturacionTools tools)
    {
        _tools = tools;
    }

    public async Task<ToolResultDto> DispatchAsync(string toolName, string? argumentsJson, FacturaConversationState state, CancellationToken cancellationToken = default)
    {
        using var document = string.IsNullOrWhiteSpace(argumentsJson)
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(argumentsJson);

        var root = document.RootElement;
        return toolName switch
        {
            ToolDefinitions.BuscarCliente => await _tools.BuscarClienteAsync(state, GetString(root, "query") ?? string.Empty, cancellationToken),
            ToolDefinitions.BuscarProducto => await _tools.BuscarProductoAsync(state, GetString(root, "query") ?? string.Empty, cancellationToken),
            ToolDefinitions.CrearCliente => await _tools.CrearClienteAsync(state, new ClienteCreateRequestDto
            {
                NombreCompleto = GetString(root, "nombreCompleto"),
                Apellidos = GetString(root, "apellidos"),
                Nombres = GetString(root, "nombres"),
                RazonSocial = GetString(root, "razonSocial"),
                NombreComercial = GetString(root, "nombreComercial"),
                Identificacion = GetString(root, "identificacion"),
                Correo = GetString(root, "correo"),
                Celular = GetString(root, "celular"),
                Telefono = GetString(root, "telefono"),
                Direccion = GetString(root, "direccion"),
                ObligadoContabilidad = GetString(root, "obligadoContabilidad"),
                EsEmpresa = GetBool(root, "esEmpresa"),
                Pais = GetString(root, "pais"),
                Provincia = GetString(root, "provincia"),
                Ciudad = GetString(root, "ciudad")
            }, cancellationToken),
            ToolDefinitions.CrearProducto => await _tools.CrearProductoAsync(state, new ProductoCreateRequestDto
            {
                Nombre = GetString(root, "nombre"),
                CodigoPrincipal = GetString(root, "codigoPrincipal"),
                PrecioUnitario = GetDecimal(root, "precioUnitario"),
                Tipo = GetString(root, "tipo"),
                TarifaPorcentaje = GetDecimal(root, "tarifaPorcentaje"),
                Observacion = GetString(root, "observacion")
            }, cancellationToken),
            ToolDefinitions.CrearBorradorFactura => await _tools.CrearBorradorFacturaAsync(state, GetInt(root, "clienteId"), GetString(root, "clienteNombre"), cancellationToken),
            ToolDefinitions.AgregarProductoAFactura => await _tools.AgregarProductoAFacturaAsync(state, GetInt(root, "productoId") ?? 0, GetDecimal(root, "cantidad") ?? 0m, GetDecimal(root, "descuentoPorcentaje"), GetDecimal(root, "descuentoValor"), cancellationToken),
            ToolDefinitions.AgregarServicioManualAFactura => await _tools.AgregarServicioManualAFacturaAsync(state, GetString(root, "descripcion") ?? string.Empty, GetDecimal(root, "cantidad") ?? 0m, GetDecimal(root, "precioUnitario") ?? 0m, GetDecimal(root, "descuentoPorcentaje"), GetDecimal(root, "descuentoValor"), GetDecimal(root, "tarifaPorcentaje")),
            ToolDefinitions.AplicarDescuentoLinea => await _tools.AplicarDescuentoLineaAsync(state, GetString(root, "referenciaItem") ?? string.Empty, GetDecimal(root, "porcentaje"), GetDecimal(root, "valor")),
            ToolDefinitions.AplicarDescuentoGlobal => await _tools.AplicarDescuentoGlobalAsync(state, GetDecimal(root, "porcentaje"), GetDecimal(root, "valor")),
            ToolDefinitions.QuitarProductoDeFactura => await _tools.QuitarProductoDeFacturaAsync(state, GetString(root, "referenciaItem") ?? string.Empty),
            ToolDefinitions.ModificarCantidadProducto => await _tools.ModificarCantidadProductoAsync(state, GetString(root, "referenciaItem") ?? string.Empty, GetDecimal(root, "cantidad") ?? 0m),
            ToolDefinitions.ModificarPrecioItem => await _tools.ModificarPrecioItemAsync(state, GetString(root, "referenciaItem") ?? string.Empty, GetDecimal(root, "precioUnitario") ?? 0m),
            ToolDefinitions.ModificarIvaItem => await _tools.ModificarIvaItemAsync(state, GetString(root, "referenciaItem") ?? string.Empty, GetDecimal(root, "tarifaPorcentaje") ?? 0m),
            ToolDefinitions.ModificarFormaPago => await _tools.ModificarFormaPagoAsync(state, GetString(root, "formaPago") ?? string.Empty, GetInt(root, "diasCredito"), cancellationToken),
            ToolDefinitions.BuscarClientesConCuentasPorCobrar => await _tools.BuscarClientesConCuentasPorCobrarAsync(state, GetString(root, "filtroCliente"), cancellationToken),
            ToolDefinitions.ConsultarCuentasPorCobrar => await _tools.ConsultarCuentasPorCobrarAsync(state, GetString(root, "filtroCliente"), cancellationToken),
            ToolDefinitions.ConsultarSaldoAFavor => await _tools.ConsultarSaldoAFavorAsync(state, GetString(root, "filtroCliente"), cancellationToken),
            ToolDefinitions.RegistrarAbonoGeneral => await _tools.RegistrarAbonoGeneralAsync(state, GetDecimal(root, "monto") ?? 0m, GetString(root, "filtroCliente"), GetString(root, "observacion"), cancellationToken),
            ToolDefinitions.CalcularTotales => await _tools.CalcularTotalesAsync(state),
            ToolDefinitions.ValidarFactura => await _tools.ValidarFacturaAsync(state),
            ToolDefinitions.ObtenerResumenFactura => await _tools.ObtenerResumenFacturaAsync(state),
            ToolDefinitions.EmitirFactura => await _tools.EmitirFacturaAsync(state, cancellationToken),
            ToolDefinitions.EmitirNotaCreditoDesdeFactura => await _tools.EmitirNotaCreditoDesdeFacturaAsync(state, GetString(root, "referenciaFactura") ?? string.Empty, GetString(root, "motivo"), cancellationToken),
            _ => new ToolResultDto
            {
                ToolName = toolName,
                Success = false,
                Message = $"La herramienta '{toolName}' no está implementada."
            }
        };
    }

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int? GetInt(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : null;

    private static bool? GetBool(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) switch
        {
            true when property.ValueKind == JsonValueKind.True => true,
            true when property.ValueKind == JsonValueKind.False => false,
            true when property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out var value) => value,
            _ => null
        };

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var decimalValue))
            return decimalValue;

        if (property.ValueKind == JsonValueKind.String && decimal.TryParse(property.GetString(), out decimalValue))
            return decimalValue;

        return null;
    }
}
