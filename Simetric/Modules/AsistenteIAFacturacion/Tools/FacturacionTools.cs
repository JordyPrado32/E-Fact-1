using System.Globalization;
using Simetric.Modules.AsistenteIAFacturacion.DTOs;
using Simetric.Modules.AsistenteIAFacturacion.Services;
using Simetric.Modules.AsistenteIAFacturacion.State;
using Simetric.Services;
using Simetric.ViewModels;

namespace Simetric.Modules.AsistenteIAFacturacion.Tools;

public sealed class FacturacionTools
{
    private readonly IClienteService _clienteService;
    private readonly IProductoService _productoService;
    private readonly IFacturacionService _facturacionService;
    private readonly AbonoService _abonoService;

    public FacturacionTools(
        IClienteService clienteService,
        IProductoService productoService,
        IFacturacionService facturacionService,
        AbonoService abonoService)
    {
        _clienteService = clienteService;
        _productoService = productoService;
        _facturacionService = facturacionService;
        _abonoService = abonoService;
    }

    public async Task<ToolResultDto> BuscarClienteAsync(FacturaConversationState state, string query, CancellationToken cancellationToken)
    {
        state.Estado = FacturaConversationStates.BuscandoCliente;
        state.UltimaIntencion = "crear_factura";

        var clientes = await _clienteService.BuscarAsync(state.UserId, query, cancellationToken);
        return new ToolResultDto
        {
            ToolName = ToolDefinitions.BuscarCliente,
            Success = clientes.Count > 0,
            Message = clientes.Count switch
            {
                0 => $"No encontré clientes para '{query}'.",
                1 => $"Encontré 1 cliente para '{query}'.",
                _ => $"Encontré {clientes.Count} clientes para '{query}'."
            },
            Data = clientes
        };
    }

    public async Task<ToolResultDto> BuscarProductoAsync(FacturaConversationState state, string query, CancellationToken cancellationToken)
    {
        state.UltimaIntencion = "agregar_item";
        var productos = await _productoService.BuscarAsync(state.UserId, query, cancellationToken);
        return new ToolResultDto
        {
            ToolName = ToolDefinitions.BuscarProducto,
            Success = productos.Count > 0,
            Message = productos.Count switch
            {
                0 => $"No encontré productos para '{query}'.",
                1 => $"Encontré 1 producto para '{query}'.",
                _ => $"Encontré {productos.Count} productos para '{query}'."
            },
            Data = productos
        };
    }

    public async Task<ToolResultDto> CrearClienteAsync(FacturaConversationState state, ClienteCreateRequestDto request, CancellationToken cancellationToken)
    {
        state.UltimaIntencion = "crear_cliente";
        var (cliente, message) = await _clienteService.CrearAsync(state.UserId, request, cancellationToken);
        if (cliente is null)
            return Fail(ToolDefinitions.CrearCliente, message);

        state.Draft.Cliente = cliente;
        state.Estado = FacturaConversationStates.ClienteSeleccionado;
        return Ok(ToolDefinitions.CrearCliente, message, cliente);
    }

    public async Task<ToolResultDto> CrearProductoAsync(FacturaConversationState state, ProductoCreateRequestDto request, CancellationToken cancellationToken)
    {
        state.UltimaIntencion = "crear_producto";
        var (producto, message) = await _productoService.CrearAsync(state.UserId, request, cancellationToken);
        if (producto is null)
            return Fail(ToolDefinitions.CrearProducto, message);

        return Ok(ToolDefinitions.CrearProducto, message, producto);
    }

    public async Task<ToolResultDto> CrearBorradorFacturaAsync(FacturaConversationState state, int? clienteId, string? clienteNombre, CancellationToken cancellationToken)
    {
        state.Draft = new FacturaDraftDto();
        state.Emitida = false;
        state.RequiereConfirmacion = false;
        state.Estado = FacturaConversationStates.SinFactura;
        state.UltimaIntencion = "crear_factura";

        if (clienteId.HasValue && clienteId.Value > 0)
        {
            var cliente = await _clienteService.ObtenerAsync(state.UserId, clienteId.Value, cancellationToken);
            if (cliente is null)
            {
                state.Estado = FacturaConversationStates.BuscandoCliente;
                return Fail(ToolDefinitions.CrearBorradorFactura, "El cliente indicado no existe.");
            }

            state.Draft.Cliente = cliente;
            state.Estado = FacturaConversationStates.ClienteSeleccionado;
            return Ok(ToolDefinitions.CrearBorradorFactura, $"Borrador creado con cliente {cliente.Nombre}.", state.Draft);
        }

        if (!string.IsNullOrWhiteSpace(clienteNombre))
        {
            state.Estado = FacturaConversationStates.BuscandoCliente;
            return Ok(ToolDefinitions.CrearBorradorFactura, $"Borrador creado. Falta confirmar el cliente '{clienteNombre}'.", state.Draft);
        }

        return Ok(ToolDefinitions.CrearBorradorFactura, "Borrador de factura creado. Falta seleccionar cliente.", state.Draft);
    }

    public async Task<ToolResultDto> AgregarProductoAFacturaAsync(FacturaConversationState state, int productoId, decimal cantidad, decimal? descuentoPorcentaje, decimal? descuentoValor, CancellationToken cancellationToken)
    {
        if (cantidad <= 0)
            return Fail(ToolDefinitions.AgregarProductoAFactura, "La cantidad debe ser mayor a cero.");

        var producto = await _productoService.ObtenerAsync(state.UserId, productoId, cancellationToken);
        if (producto is null)
            return Fail(ToolDefinitions.AgregarProductoAFactura, "El producto indicado no existe.");

        var existing = state.Draft.Items.FirstOrDefault(x => x.ProductoId == productoId && !x.EsServicioManual);
        if (existing is null)
        {
            existing = new FacturaItemDraftDto
            {
                ProductoId = producto.Id,
                Descripcion = producto.Nombre,
                CodigoPrincipal = producto.CodigoPrincipal,
                Cantidad = cantidad,
                PrecioUnitario = producto.PrecioUnitario,
                DescuentoPorcentaje = descuentoPorcentaje,
                DescuentoValor = descuentoValor,
                TarifaPorcentaje = producto.TarifaPorcentaje,
                EsServicioManual = false
            };

            state.Draft.Items.Add(existing);
        }
        else
        {
            existing.Cantidad += cantidad;
            if (descuentoPorcentaje.HasValue)
                existing.DescuentoPorcentaje = descuentoPorcentaje;
            if (descuentoValor.HasValue)
                existing.DescuentoValor = descuentoValor;
        }

        state.Estado = FacturaConversationStates.AgregandoItems;
        state.UltimaIntencion = "agregar_item";
        Recalculate(state.Draft);

        return Ok(ToolDefinitions.AgregarProductoAFactura, $"Agregué {cantidad.ToString("0.##", CultureInfo.InvariantCulture)} x {producto.Nombre}.", state.Draft);
    }

    public Task<ToolResultDto> AgregarServicioManualAFacturaAsync(FacturaConversationState state, string descripcion, decimal cantidad, decimal precioUnitario, decimal? descuentoPorcentaje, decimal? descuentoValor, decimal? tarifaPorcentaje)
    {
        if (string.IsNullOrWhiteSpace(descripcion))
            return Task.FromResult(Fail(ToolDefinitions.AgregarServicioManualAFactura, "La descripción del servicio es obligatoria."));

        if (cantidad <= 0 || precioUnitario <= 0)
            return Task.FromResult(Fail(ToolDefinitions.AgregarServicioManualAFactura, "La cantidad y el precio deben ser mayores a cero."));

        state.Draft.Items.Add(new FacturaItemDraftDto
        {
            ProductoId = null,
            Descripcion = descripcion.Trim(),
            Cantidad = cantidad,
            PrecioUnitario = decimal.Round(precioUnitario, 2),
            DescuentoPorcentaje = descuentoPorcentaje,
            DescuentoValor = descuentoValor,
            TarifaPorcentaje = tarifaPorcentaje ?? 0m,
            EsServicioManual = true
        });

        state.Estado = FacturaConversationStates.AgregandoItems;
        state.UltimaIntencion = "agregar_item";
        Recalculate(state.Draft);

        return Task.FromResult(Ok(ToolDefinitions.AgregarServicioManualAFactura, $"Agregué el servicio manual '{descripcion.Trim()}'.", state.Draft));
    }

    public Task<ToolResultDto> AplicarDescuentoLineaAsync(FacturaConversationState state, string referenciaItem, decimal? porcentaje, decimal? valor)
    {
        var item = FindItem(state.Draft, referenciaItem);
        if (item is null)
            return Task.FromResult(Fail(ToolDefinitions.AplicarDescuentoLinea, "No encontré el item a descontar."));

        if (porcentaje is < 0 || valor is < 0)
            return Task.FromResult(Fail(ToolDefinitions.AplicarDescuentoLinea, "El descuento no puede ser negativo."));

        item.DescuentoPorcentaje = porcentaje;
        item.DescuentoValor = valor;
        state.UltimaIntencion = "cambiar_descuento";
        Recalculate(state.Draft);

        return Task.FromResult(Ok(ToolDefinitions.AplicarDescuentoLinea, $"Actualicé el descuento de {item.Descripcion}.", state.Draft));
    }

    public Task<ToolResultDto> AplicarDescuentoGlobalAsync(FacturaConversationState state, decimal? porcentaje, decimal? valor)
    {
        if (porcentaje is < 0 || valor is < 0)
            return Task.FromResult(Fail(ToolDefinitions.AplicarDescuentoGlobal, "El descuento global no puede ser negativo."));

        state.Draft.DescuentoGlobalPorcentaje = porcentaje;
        state.Draft.DescuentoGlobalValor = valor;
        state.UltimaIntencion = "cambiar_descuento";
        Recalculate(state.Draft);

        return Task.FromResult(Ok(ToolDefinitions.AplicarDescuentoGlobal, "Actualicé el descuento global de la factura.", state.Draft));
    }

    public Task<ToolResultDto> QuitarProductoDeFacturaAsync(FacturaConversationState state, string referenciaItem)
    {
        var item = FindItem(state.Draft, referenciaItem);
        if (item is null)
            return Task.FromResult(Fail(ToolDefinitions.QuitarProductoDeFactura, "No encontré el item que quieres quitar."));

        state.Draft.Items.Remove(item);
        state.UltimaIntencion = "quitar_item";
        Recalculate(state.Draft);

        return Task.FromResult(Ok(ToolDefinitions.QuitarProductoDeFactura, $"Quité {item.Descripcion} de la factura.", state.Draft));
    }

    public Task<ToolResultDto> ModificarCantidadProductoAsync(FacturaConversationState state, string referenciaItem, decimal cantidad)
    {
        if (cantidad <= 0)
            return Task.FromResult(Fail(ToolDefinitions.ModificarCantidadProducto, "La nueva cantidad debe ser mayor a cero."));

        var item = FindItem(state.Draft, referenciaItem);
        if (item is null)
            return Task.FromResult(Fail(ToolDefinitions.ModificarCantidadProducto, "No encontré el item para cambiar cantidad."));

        item.Cantidad = cantidad;
        state.UltimaIntencion = "agregar_item";
        Recalculate(state.Draft);

        return Task.FromResult(Ok(ToolDefinitions.ModificarCantidadProducto, $"Actualicé la cantidad de {item.Descripcion} a {cantidad.ToString("0.##", CultureInfo.InvariantCulture)}.", state.Draft));
    }

    public Task<ToolResultDto> ModificarPrecioItemAsync(FacturaConversationState state, string referenciaItem, decimal precioUnitario)
    {
        if (precioUnitario <= 0m)
            return Task.FromResult(Fail(ToolDefinitions.ModificarPrecioItem, "El nuevo precio unitario debe ser mayor a cero."));

        var item = FindItem(state.Draft, referenciaItem);
        if (item is null)
            return Task.FromResult(Fail(ToolDefinitions.ModificarPrecioItem, "No encontré el item para cambiar precio."));

        item.PrecioUnitario = decimal.Round(precioUnitario, 2, MidpointRounding.AwayFromZero);
        state.UltimaIntencion = "modificar_precio_item";
        Recalculate(state.Draft);

        return Task.FromResult(Ok(
            ToolDefinitions.ModificarPrecioItem,
            $"Actualicé el precio de {item.Descripcion} a ${item.PrecioUnitario:0.00} solo para esta factura.",
            state.Draft));
    }

    public Task<ToolResultDto> ModificarIvaItemAsync(FacturaConversationState state, string referenciaItem, decimal tarifaPorcentaje)
    {
        if (tarifaPorcentaje < 0m)
            return Task.FromResult(Fail(ToolDefinitions.ModificarIvaItem, "La tarifa de IVA no puede ser negativa."));

        var item = FindItem(state.Draft, referenciaItem);
        if (item is null)
            return Task.FromResult(Fail(ToolDefinitions.ModificarIvaItem, "No encontré el item para cambiar IVA."));

        item.TarifaPorcentaje = TaxRateHelper.NormalizePercent(tarifaPorcentaje);
        state.UltimaIntencion = "modificar_iva_item";
        Recalculate(state.Draft);

        return Task.FromResult(Ok(
            ToolDefinitions.ModificarIvaItem,
            $"Actualicé el IVA de {item.Descripcion} a {item.TarifaPorcentaje:0}% solo para esta factura.",
            state.Draft));
    }

    public async Task<ToolResultDto> ModificarFormaPagoAsync(FacturaConversationState state, string formaPago, int? diasCredito, CancellationToken cancellationToken)
    {
        var formas = await _facturacionService.ObtenerFormasPagoAsync(cancellationToken);
        var match = formas.FirstOrDefault(x => string.Equals(x, formaPago, StringComparison.OrdinalIgnoreCase))
            ?? formas.FirstOrDefault(x => x.Contains(formaPago, StringComparison.OrdinalIgnoreCase));

        var formaNormalizada = NormalizeText(formaPago);
        if (match is null && formaNormalizada.Contains("credit", StringComparison.OrdinalIgnoreCase))
            match = formas.FirstOrDefault(x => NormalizeText(x).Contains("credit", StringComparison.OrdinalIgnoreCase)) ?? "Crédito";

        if (match is null && (formaNormalizada.Contains("efectivo", StringComparison.OrdinalIgnoreCase) || formaNormalizada.Contains("contado", StringComparison.OrdinalIgnoreCase)))
            match = formas.FirstOrDefault(x => NormalizeText(x).Contains("efectivo", StringComparison.OrdinalIgnoreCase)) ?? "Efectivo";

        if (match is null)
            return Fail(ToolDefinitions.ModificarFormaPago, $"No encontré una forma de pago compatible con '{formaPago}'.");

        state.Draft.FormaPago = match;
        if (IsCreditPayment(match))
        {
            var plazo = diasCredito.GetValueOrDefault() > 0
                ? diasCredito!.Value
                : state.Draft.DiasCredito.GetValueOrDefault() > 0
                    ? state.Draft.DiasCredito!.Value
                    : 30;

            state.Draft.DiasCredito = plazo;
            state.Draft.FechaVencimiento = DateTime.Today.AddDays(plazo);
        }
        else
        {
            state.Draft.DiasCredito = null;
            state.Draft.FechaVencimiento = null;
        }

        state.UltimaIntencion = "cambiar_forma_pago";
        var mensaje = IsCreditPayment(match)
            ? $"Forma de pago cambiada a {match}. Vence el {state.Draft.FechaVencimiento:dd/MM/yyyy}."
            : $"Forma de pago cambiada a {match}.";
        return Ok(ToolDefinitions.ModificarFormaPago, mensaje, state.Draft);
    }

    public async Task<ToolResultDto> ConsultarCuentasPorCobrarAsync(FacturaConversationState state, string? filtroCliente, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        state.UltimaIntencion = "consultar_cuentas_por_cobrar";

        List<FacturaPendienteVM> facturas;
        var resumenPorCliente = false;

        if (!string.IsNullOrWhiteSpace(filtroCliente))
        {
            facturas = await _abonoService.GetFacturasCreditoPendientes(state.UserId, filtroCliente.Trim());
        }
        else if (state.Draft.Cliente?.Id > 0)
        {
            facturas = await _abonoService.GetFacturasCreditoPendientes(state.UserId, state.Draft.Cliente.Id);
        }
        else
        {
            facturas = await _abonoService.GetFacturasCreditoPendientes(state.UserId);
            resumenPorCliente = true;
        }

        if (facturas.Count == 0)
            return Ok(ToolDefinitions.ConsultarCuentasPorCobrar, "No encontré cuentas por cobrar pendientes con ese criterio.");

        var mensaje = resumenPorCliente
            ? BuildPortfolioSummaryMessage(facturas)
            : BuildClientPendingInvoicesMessage(facturas);

        return Ok(ToolDefinitions.ConsultarCuentasPorCobrar, mensaje, facturas.Take(20).ToList());
    }

    public async Task<ToolResultDto> BuscarClientesConCuentasPorCobrarAsync(FacturaConversationState state, string? filtroCliente, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        state.UltimaIntencion = "buscar_clientes_con_cartera";

        var facturas = await _abonoService.GetFacturasCreditoPendientes(state.UserId);

        var clientes = facturas
            .GroupBy(x => new { x.IdCliente, x.NombreCliente, x.NumeroIdentificacion })
            .Select(g => new ClienteCarteraResumenDto
            {
                ClienteId = g.Key.IdCliente,
                NombreCliente = g.Key.NombreCliente,
                Identificacion = g.Key.NumeroIdentificacion,
                FacturasPendientes = g.Count(),
                SaldoPendiente = decimal.Round(g.Sum(x => x.SaldoPendiente), 2)
            })
            .Where(x => x.ClienteId > 0 && x.SaldoPendiente > 0m);

        if (!string.IsNullOrWhiteSpace(filtroCliente))
        {
            var filtro = filtroCliente.Trim();
            clientes = clientes.Where(x =>
                x.NombreCliente.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                x.Identificacion.Contains(filtro, StringComparison.OrdinalIgnoreCase));
        }

        var resultados = clientes
            .OrderByDescending(x => x.SaldoPendiente)
            .ThenBy(x => x.NombreCliente)
            .Take(20)
            .ToList();

        if (resultados.Count == 0)
            return Ok(ToolDefinitions.BuscarClientesConCuentasPorCobrar, "No encontre clientes con cuentas por cobrar para ese criterio.");

        return Ok(
            ToolDefinitions.BuscarClientesConCuentasPorCobrar,
            BuildPendingClientsMessage(resultados),
            resultados);
    }

    public async Task<ToolResultDto> ConsultarSaldoAFavorAsync(FacturaConversationState state, string? filtroCliente, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        state.UltimaIntencion = "consultar_saldo_a_favor";

        var cliente = await ResolveCollectionClientAsync(state, filtroCliente);
        if (!cliente.Success || cliente.ClienteId <= 0)
            return Fail(ToolDefinitions.ConsultarSaldoAFavor, cliente.Message);

        var saldo = await _abonoService.GetSaldoAFavor(state.UserId, cliente.ClienteId);
        var detalle = await _abonoService.GetDetalleSaldoAFavor(state.UserId, cliente.ClienteId);

        var mensaje = saldo <= 0m
            ? $"El cliente {cliente.NombreCliente} no tiene saldo a favor disponible."
            : BuildSaldoAFavorMessage(cliente.NombreCliente, saldo, detalle);

        return Ok(ToolDefinitions.ConsultarSaldoAFavor, mensaje, detalle.Take(10).ToList());
    }

    public async Task<ToolResultDto> RegistrarAbonoGeneralAsync(FacturaConversationState state, decimal monto, string? filtroCliente, string? observacion, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        state.UltimaIntencion = "registrar_abono";

        if (monto <= 0m)
            return Fail(ToolDefinitions.RegistrarAbonoGeneral, "El monto del abono debe ser mayor a cero.");

        if (!string.IsNullOrWhiteSpace(filtroCliente))
        {
            var coincidencias = await _abonoService.GetFacturasCreditoPendientes(state.UserId, filtroCliente.Trim());
            var clientesCoincidentes = coincidencias
                .GroupBy(x => new { x.IdCliente, x.NombreCliente, x.NumeroIdentificacion })
                .Select((g, index) => new SelectionOptionDto
                {
                    Indice = index + 1,
                    Tipo = "cliente",
                    Etiqueta = g.Key.NombreCliente,
                    Descripcion = $"{g.Key.NumeroIdentificacion} | {g.Count()} factura(s) | saldo ${g.Sum(x => x.SaldoPendiente):0.00}",
                    Cliente = new ClienteDto
                    {
                        Id = g.Key.IdCliente,
                        Nombre = g.Key.NombreCliente,
                        Identificacion = g.Key.NumeroIdentificacion
                    }
                })
                .Where(x => x.Cliente is not null && x.Cliente.Id > 0)
                .Take(5)
                .ToList();

            if (clientesCoincidentes.Count > 1)
            {
                state.SeleccionPendiente = new PendingSelectionState
                {
                    Tipo = "cliente",
                    Accion = "registrar_abono",
                    Monto = decimal.Round(monto, 2),
                    Observacion = observacion,
                    Mensaje = $"Encontre varios clientes para '{filtroCliente}'. Elige uno para registrar el abono:",
                    Opciones = clientesCoincidentes
                };

                return Ok(
                    ToolDefinitions.RegistrarAbonoGeneral,
                    state.SeleccionPendiente.Mensaje,
                    state.SeleccionPendiente.Opciones);
            }
        }

        var cliente = await ResolveCollectionClientAsync(state, filtroCliente);
        if (!cliente.Success || cliente.ClienteId <= 0)
            return Fail(ToolDefinitions.RegistrarAbonoGeneral, cliente.Message);

        var nota = string.IsNullOrWhiteSpace(observacion)
            ? "Abono registrado desde el asistente"
            : observacion.Trim();

        var registrado = await _abonoService.RegistrarPagoGeneral(state.UserId, cliente.ClienteId, decimal.Round(monto, 2), nota);
        if (!registrado)
            return Fail(ToolDefinitions.RegistrarAbonoGeneral, $"No pude registrar el abono para {cliente.NombreCliente}.");

        var pendientes = await _abonoService.GetFacturasCreditoPendientes(state.UserId, cliente.ClienteId);
        var saldoAFavor = await _abonoService.GetSaldoAFavor(state.UserId, cliente.ClienteId);
        var saldoPendiente = pendientes.Sum(x => x.SaldoPendiente);

        var mensaje = $"Registré un abono de ${monto:0.00} para {cliente.NombreCliente}. " +
            $"Se distribuyó automáticamente en la cartera pendiente. " +
            $"Saldo pendiente actual: ${saldoPendiente:0.00}. " +
            $"Saldo a favor disponible: ${saldoAFavor:0.00}. " +
            $"Facturas pendientes activas: {pendientes.Count}.";

        return Ok(ToolDefinitions.RegistrarAbonoGeneral, mensaje, new
        {
            cliente.ClienteId,
            cliente.NombreCliente,
            monto = decimal.Round(monto, 2),
            saldoPendiente = decimal.Round(saldoPendiente, 2),
            saldoAFavor = decimal.Round(saldoAFavor, 2),
            facturasPendientes = pendientes.Count
        });
    }

    public Task<ToolResultDto> CalcularTotalesAsync(FacturaConversationState state)
    {
        state.Estado = FacturaConversationStates.CalculandoTotales;
        Recalculate(state.Draft);
        return Task.FromResult(Ok(ToolDefinitions.CalcularTotales, $"Total recalculado: {state.Draft.Total:C2}.", state.Draft));
    }

    public Task<ToolResultDto> ValidarFacturaAsync(FacturaConversationState state)
    {
        var errores = ValidateDraft(state.Draft);
        if (errores.Count > 0)
        {
            state.RequiereConfirmacion = false;
            return Task.FromResult(Fail(ToolDefinitions.ValidarFactura, string.Join(" ", errores), errores));
        }

        state.Estado = FacturaConversationStates.EsperandoConfirmacion;
        state.RequiereConfirmacion = true;
        return Task.FromResult(Ok(ToolDefinitions.ValidarFactura, "La factura está lista para confirmación final.", state.Draft));
    }

    public Task<ToolResultDto> ObtenerResumenFacturaAsync(FacturaConversationState state)
    {
        Recalculate(state.Draft);
        var resumen = BuildSummary(state.Draft);
        return Task.FromResult(Ok(ToolDefinitions.ObtenerResumenFactura, resumen, state.Draft));
    }

    public async Task<ToolResultDto> EmitirFacturaAsync(FacturaConversationState state, CancellationToken cancellationToken)
    {
        var errores = ValidateDraft(state.Draft);
        if (errores.Count > 0)
            return Fail(ToolDefinitions.EmitirFactura, string.Join(" ", errores), errores);

        if (state.Estado != FacturaConversationStates.EsperandoConfirmacion)
            return Fail(ToolDefinitions.EmitirFactura, "La factura aún no está en estado de confirmación final.");

        var result = await _facturacionService.EmitirAsync(state.UserId, state.Draft, cancellationToken);
        if (!result.Success)
            return Fail(ToolDefinitions.EmitirFactura, result.Message);

        state.Estado = FacturaConversationStates.FacturaEmitida;
        state.Emitida = true;
        state.RequiereConfirmacion = false;

        return Ok(ToolDefinitions.EmitirFactura, result.Message, new { state.Draft, result.NumeroFactura });
    }

    public async Task<ToolResultDto> EmitirNotaCreditoDesdeFacturaAsync(FacturaConversationState state, string referenciaFactura, string? motivo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(referenciaFactura))
            return Fail(ToolDefinitions.EmitirNotaCreditoDesdeFactura, "Indica el numero de la factura para emitir la nota de credito.");

        state.UltimaIntencion = "emitir_nota_credito";
        var coincidencias = await _facturacionService.BuscarFacturasParaNotaCreditoAsync(state.UserId, referenciaFactura.Trim(), cancellationToken);
        if (coincidencias.Count == 0)
            return Fail(ToolDefinitions.EmitirNotaCreditoDesdeFactura, $"No encontre una factura para '{referenciaFactura}'.");

        if (coincidencias.Count > 1)
        {
            var opciones = string.Join(", ", coincidencias.Take(3).Select(x =>
                string.IsNullOrWhiteSpace(x.Serie)
                    ? x.NumeroFactura
                    : $"{x.Serie}-{x.NumeroFactura}"));
            return Fail(ToolDefinitions.EmitirNotaCreditoDesdeFactura, $"Encontre varias facturas: {opciones}. Indica el numero exacto.");
        }

        var factura = coincidencias[0];
        var result = await _facturacionService.EmitirNotaCreditoAsync(state.UserId, factura.Id, motivo, cancellationToken);
        if (!result.Success)
            return Fail(ToolDefinitions.EmitirNotaCreditoDesdeFactura, result.Message);

        state.Emitida = true;
        state.RequiereConfirmacion = false;
        state.Estado = FacturaConversationStates.FacturaEmitida;

        return Ok(
            ToolDefinitions.EmitirNotaCreditoDesdeFactura,
            result.Message,
            new { factura.NumeroFactura, factura.Serie, result.NumeroNotaCredito, result.Autorizada });
    }

    public static void Recalculate(FacturaDraftDto draft)
    {
        var items = draft.Items;
        if (items.Count == 0)
        {
            draft.Subtotal = 0m;
            draft.Descuento = 0m;
            draft.Impuesto = 0m;
            draft.Total = 0m;
            draft.IvaDetalles.Clear();
            return;
        }

        decimal baseAfterLineDiscount = 0m;
        decimal lineDiscountSum = 0m;

        foreach (var item in items)
        {
            var baseLine = decimal.Round(item.Cantidad * item.PrecioUnitario, 2);
            var tarifaIva = TaxRateHelper.NormalizePercent(item.TarifaPorcentaje);
            item.TarifaPorcentaje = tarifaIva;
            var discountByPercent = item.DescuentoPorcentaje.HasValue
                ? decimal.Round(baseLine * (item.DescuentoPorcentaje.Value / 100m), 2, MidpointRounding.AwayFromZero)
                : 0m;
            var discountByValue = decimal.Round(item.DescuentoValor ?? 0m, 2, MidpointRounding.AwayFromZero);
            var lineDiscount = Math.Min(baseLine, discountByPercent + discountByValue);

            item.DescuentoAplicado = lineDiscount;
            item.Subtotal = decimal.Round(baseLine - lineDiscount, 2);
            item.Impuesto = 0m;
            item.Total = item.Subtotal;

            baseAfterLineDiscount += item.Subtotal;
            lineDiscountSum += lineDiscount;
        }

        var globalDiscount = draft.DescuentoGlobalPorcentaje.HasValue
            ? decimal.Round(baseAfterLineDiscount * (draft.DescuentoGlobalPorcentaje.Value / 100m), 2, MidpointRounding.AwayFromZero)
            : decimal.Round(draft.DescuentoGlobalValor ?? 0m, 2, MidpointRounding.AwayFromZero);
        globalDiscount = Math.Max(0m, Math.Min(globalDiscount, baseAfterLineDiscount));

        decimal allocatedGlobalAccumulated = 0m;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var proportion = baseAfterLineDiscount <= 0m ? 0m : item.Subtotal / baseAfterLineDiscount;
            var allocatedGlobal = index == items.Count - 1
                ? TaxRateHelper.NormalizeMoney(globalDiscount - allocatedGlobalAccumulated)
                : decimal.Round(globalDiscount * proportion, 2, MidpointRounding.AwayFromZero);
            var finalBase = Math.Max(0m, item.Subtotal - allocatedGlobal);
            var tax = decimal.Round(finalBase * (item.TarifaPorcentaje / 100m), 2, MidpointRounding.AwayFromZero);

            allocatedGlobalAccumulated += allocatedGlobal;
            item.DescuentoAplicado = decimal.Round(item.DescuentoAplicado + allocatedGlobal, 2);
            item.Subtotal = finalBase;
            item.Impuesto = tax;
            item.Total = decimal.Round(finalBase + tax, 2);
        }

        draft.Subtotal = TaxRateHelper.NormalizeMoney(items.Sum(x => x.Subtotal));
        draft.Descuento = TaxRateHelper.NormalizeMoney(items.Sum(x => x.DescuentoAplicado));
        draft.Impuesto = TaxRateHelper.NormalizeMoney(items.Sum(x => x.Impuesto));
        draft.Total = TaxRateHelper.NormalizeMoney(draft.Subtotal + draft.Impuesto);
        draft.IvaDetalles = items
            .GroupBy(x => TaxRateHelper.NormalizePercent(x.TarifaPorcentaje))
            .OrderBy(x => x.Key)
            .Select(group => new FacturaTaxBreakdownDto
            {
                TarifaPorcentaje = group.Key,
                BaseImponible = TaxRateHelper.NormalizeMoney(group.Sum(x => x.Subtotal)),
                ValorIva = TaxRateHelper.NormalizeMoney(group.Sum(x => x.Impuesto))
            })
            .ToList();
    }

    public static List<string> ValidateDraft(FacturaDraftDto draft)
    {
        var errors = new List<string>();

        if (draft.Cliente?.Id <= 0)
            errors.Add("Falta seleccionar un cliente válido.");

        if (draft.Items.Count == 0)
            errors.Add("La factura debe tener al menos un item.");

        foreach (var item in draft.Items)
        {
            if (item.Cantidad <= 0)
                errors.Add($"La cantidad de '{item.Descripcion}' debe ser mayor a cero.");

            if (item.PrecioUnitario < 0)
                errors.Add($"El precio de '{item.Descripcion}' no puede ser negativo.");

            if (item.DescuentoAplicado < 0)
                errors.Add($"El descuento de '{item.Descripcion}' no puede ser negativo.");
        }

        if (draft.Descuento > draft.Subtotal + draft.Descuento)
            errors.Add("El descuento total no puede superar el valor de la factura.");

        return errors;
    }

    public static string BuildSummary(FacturaDraftDto draft)
    {
        var lineas = new List<string>();
        if (draft.Cliente is not null)
            lineas.Add($"Cliente: {draft.Cliente.Nombre} ({draft.Cliente.Identificacion ?? "sin identificación"})");

        foreach (var item in draft.Items)
        {
            lineas.Add($"- {item.Cantidad:0.##} x {item.Descripcion} a ${item.PrecioUnitario:0.00} con IVA {item.TarifaPorcentaje:0}% = ${item.Total:0.00}");
        }

        lineas.Add($"Subtotal: ${draft.Subtotal:0.00}");
        lineas.Add($"Descuento: ${draft.Descuento:0.00}");
        if (draft.IvaDetalles.Count > 0)
        {
            foreach (var iva in draft.IvaDetalles)
            {
                var etiqueta = iva.TarifaPorcentaje <= 0m ? "IVA 0%" : $"IVA {iva.TarifaPorcentaje:0}%";
                lineas.Add($"{etiqueta}: base ${iva.BaseImponible:0.00} -> ${iva.ValorIva:0.00}");
            }
        }
        lineas.Add($"Impuestos: ${draft.Impuesto:0.00}");
        lineas.Add($"Total: ${draft.Total:0.00}");

        if (!string.IsNullOrWhiteSpace(draft.FormaPago))
            lineas.Add($"Forma de pago: {draft.FormaPago}");

        if (draft.DiasCredito.GetValueOrDefault() > 0 && draft.FechaVencimiento.HasValue)
            lineas.Add($"Crédito: {draft.DiasCredito} días | vence {draft.FechaVencimiento.Value:dd/MM/yyyy}");

        return string.Join("\n", lineas);
    }

    private async Task<(bool Success, int ClienteId, string NombreCliente, string Message)> ResolveCollectionClientAsync(FacturaConversationState state, string? filtroCliente)
    {
        if (!string.IsNullOrWhiteSpace(filtroCliente))
        {
            var coincidencias = await _abonoService.GetFacturasCreditoPendientes(state.UserId, filtroCliente.Trim());
            if (coincidencias.Count == 0)
                return (false, 0, string.Empty, $"No encontré cartera pendiente para '{filtroCliente}'.");

            if (coincidencias.Count > 1)
            {
                var opciones = string.Join(", ", coincidencias.Take(3).Select(x => x.NombreCliente));
                return (false, 0, string.Empty, $"Encontré varios clientes con cartera pendiente: {opciones}. Indícame uno más específico.");
            }

            var cliente = coincidencias[0];
            return (true, cliente.IdCliente, cliente.NombreCliente, string.Empty);
        }

        if (state.Draft.Cliente?.Id > 0)
            return (true, state.Draft.Cliente.Id, state.Draft.Cliente.Nombre, string.Empty);

        return (false, 0, string.Empty, "Indícame el cliente para revisar cartera o registrar el abono.");
    }

    private static string BuildPortfolioSummaryMessage(IReadOnlyList<FacturaPendienteVM> facturas)
    {
        var total = facturas.Sum(x => x.SaldoPendiente);
        var clientes = facturas
            .GroupBy(x => new { x.IdCliente, x.NombreCliente })
            .Select(g => new
            {
                g.Key.NombreCliente,
                Facturas = g.Count(),
                Saldo = g.Sum(x => x.SaldoPendiente)
            })
            .OrderByDescending(x => x.Saldo)
            .Take(5)
            .ToList();

        var lineas = new List<string>
        {
            $"Tienes {facturas.Count} factura(s) a crédito pendientes por un total de ${total:0.00}.",
            "Clientes con mayor saldo:"
        };

        lineas.AddRange(clientes.Select(x => $"- {x.NombreCliente}: {x.Facturas} factura(s), saldo ${x.Saldo:0.00}"));
        return string.Join("\n", lineas);
    }

    private static string BuildClientPendingInvoicesMessage(IReadOnlyList<FacturaPendienteVM> facturas)
    {
        var cliente = facturas[0].NombreCliente;
        var total = facturas.Sum(x => x.SaldoPendiente);
        var lineas = new List<string>
        {
            $"{cliente} tiene {facturas.Count} factura(s) pendientes por ${total:0.00}."
        };

        lineas.AddRange(facturas
            .OrderBy(x => x.NumFactura)
            .Take(6)
            .Select(x => $"- Factura {x.NumFactura}: saldo ${x.SaldoPendiente:0.00} de ${x.TotalFactura:0.00}"));

        return string.Join("\n", lineas);
    }

    private static string BuildSaldoAFavorMessage(string nombreCliente, decimal saldo, IReadOnlyList<SaldoAFavorVM> detalle)
    {
        var lineas = new List<string>
        {
            $"{nombreCliente} tiene ${saldo:0.00} de saldo a favor disponible."
        };

        lineas.AddRange(detalle
            .OrderByDescending(x => x.FechaPago)
            .Take(5)
            .Select(x => $"- {x.FechaPago:dd/MM/yyyy}: saldo ${x.SaldoDisponible:0.00} de un pago original de ${x.ValorOriginal:0.00}"));

        return string.Join("\n", lineas);
    }

    private static string BuildPendingClientsMessage(IReadOnlyList<ClienteCarteraResumenDto> clientes)
    {
        var totalSaldo = clientes.Sum(x => x.SaldoPendiente);
        var totalFacturas = clientes.Sum(x => x.FacturasPendientes);

        var lineas = new List<string>
        {
            $"Encontre {clientes.Count} cliente(s) con cuentas por cobrar. Saldo total: ${totalSaldo:0.00} en {totalFacturas} factura(s)."
        };

        lineas.AddRange(clientes.Take(8).Select(x =>
            $"- {x.NombreCliente} ({x.Identificacion}): {x.FacturasPendientes} factura(s), saldo ${x.SaldoPendiente:0.00}"));

        return string.Join("\n", lineas);
    }

    private static bool IsCreditPayment(string? formaPago)
    {
        var normalized = NormalizeText(formaPago);
        return normalized.Contains("credit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "19", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value
            .Trim()
            .Replace("á", "a", StringComparison.OrdinalIgnoreCase)
            .Replace("é", "e", StringComparison.OrdinalIgnoreCase)
            .Replace("í", "i", StringComparison.OrdinalIgnoreCase)
            .Replace("ó", "o", StringComparison.OrdinalIgnoreCase)
            .Replace("ú", "u", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
    }

    private static FacturaItemDraftDto? FindItem(FacturaDraftDto draft, string reference)
    {
        var normalized = reference.Trim().ToLowerInvariant();
        return draft.Items.FirstOrDefault(x =>
            string.Equals(x.Id, normalized, StringComparison.OrdinalIgnoreCase) ||
            x.Descripcion.Contains(reference, StringComparison.OrdinalIgnoreCase));
    }

    private static ToolResultDto Ok(string toolName, string message, object? data = null) => new()
    {
        ToolName = toolName,
        Success = true,
        Message = message,
        Data = data
    };

    private static ToolResultDto Fail(string toolName, string message, object? data = null) => new()
    {
        ToolName = toolName,
        Success = false,
        Message = message,
        Data = data
    };
}
