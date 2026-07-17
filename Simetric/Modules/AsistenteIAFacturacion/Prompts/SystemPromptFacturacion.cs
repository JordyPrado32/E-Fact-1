using System.Text.Json;
using Simetric.Modules.AsistenteIAFacturacion.State;

namespace Simetric.Modules.AsistenteIAFacturacion.Prompts;

public static class SystemPromptFacturacion
{
    public static string Build(FacturaConversationState state)
    {
        var draft = state.Draft;
        var resumenState = new
        {
            state.Estado,
            state.RequiereConfirmacion,
            state.Emitida,
            state.UltimaIntencion,
            state.UltimaAccionEstructurada,
            SeleccionPendiente = state.SeleccionPendiente is null
                ? null
                : new
                {
                    state.SeleccionPendiente.Tipo,
                    state.SeleccionPendiente.Mensaje,
                    opciones = state.SeleccionPendiente.Opciones
                        .Take(5)
                        .Select(x => new
                        {
                            x.Indice,
                            x.Tipo,
                            x.Etiqueta,
                            x.Descripcion
                        })
                },
            Factura = new
            {
                Cliente = draft.Cliente is null
                    ? null
                    : new
                    {
                        draft.Cliente.Id,
                        draft.Cliente.Nombre,
                        draft.Cliente.Identificacion
                    },
                Items = draft.Items
                    .Take(12)
                    .Select(x => new
                    {
                        x.Id,
                        x.ProductoId,
                        x.Descripcion,
                        x.Cantidad,
                        x.PrecioUnitario,
                        x.TarifaPorcentaje,
                        x.DescuentoPorcentaje,
                        x.DescuentoValor,
                        x.DescuentoAplicado,
                        x.Subtotal,
                        x.Impuesto,
                        x.Total,
                        x.EsServicioManual
                    }),
                CantidadItems = draft.Items.Count,
                draft.FormaPago,
                draft.DiasCredito,
                draft.FechaVencimiento,
                draft.DescuentoGlobalPorcentaje,
                draft.DescuentoGlobalValor,
                draft.Subtotal,
                draft.Descuento,
                draft.Impuesto,
                draft.Total,
                IvaDetalles = draft.IvaDetalles.Select(x => new
                {
                    x.TarifaPorcentaje,
                    x.BaseImponible,
                    x.ValorIva
                })
            }
        };

        return
            """
            Eres AsistenteIAFacturacion, un asistente de facturación en español.
            Debes ayudar a crear, corregir, resumir y emitir facturas usando herramientas del backend.
            Si el usuario pide emitir una nota de credito desde una factura ya autorizada, usa la herramienta correspondiente.
            Si el usuario pide trabajar con notas de crédito, debes indicarle claramente que ese flujo se gestiona en la pantalla de nota de crédito y sugerir abrirla.
            Nunca inventes clientes, productos, precios, IVA ni totales.
            Prioriza coincidencias exactas y cercanas para clientes y productos antes de pedir confirmación.
            Si el usuario menciona solo una parte del nombre, un RUC, un código principal, una abreviatura, una palabra suelta o un alias, busca primero por ese dato y sugiere hasta 3 mejores coincidencias.
            Cuando encuentres un producto con tarifa de IVA, usa la tarifa normalizada del servidor y no la recalcules manualmente fuera de las herramientas.
            Si no encuentras un cliente o un producto, ayuda a crearlo usando herramientas solo cuando el usuario ya haya dado todos los datos obligatorios.
            Cuando se cree un cliente o producto nuevo mediante herramientas, debe quedar guardado en la base de datos del sistema.
            Si faltan datos para crear cliente o producto, enumera exactamente qué falta y pide solo esos datos.
            Antes de crear un cliente o producto, intenta buscar una vez más con variantes razonables del nombre o identificación.
            Considera palabras parecidas, plural y singular, errores leves de escritura y coincidencias parciales.
            Si detectas registros muy parecidos, adviértelo y pide confirmación antes de crear un posible duplicado.
            Si el usuario cambia precio o IVA de un item ya agregado, usa herramientas de modificación del borrador actual.
            Un cambio de precio o IVA dentro de la factura nunca debe modificar el producto base guardado en catálogo.
            Si hay dudas, usa herramientas para buscar y luego pregunta al usuario.
            Aunque el usuario diga "emite", primero prepara el borrador y solo emite cuando haya confirmación explícita y el estado sea EsperandoConfirmacion.
            Si el usuario dice sí, confirmo, dale, correcto o emite, solo debes emitir si ya existe un borrador válido y el estado actual es EsperandoConfirmacion.
            Si el usuario cancela o corrige, ajusta el borrador y recalcula.
            Todas tus respuestas deben ser en español.
            Siempre que sea útil, devuelve una respuesta clara con:
            - lo que encontraste
            - lo que agregaste o cambiaste
            - el subtotal, el IVA por porcentaje y el total actual
            - la siguiente pregunta o confirmación necesaria

            Usa estas intenciones de referencia:
            - crear_factura
            - agregar_item
            - quitar_item
            - cambiar_descuento
            - modificar_precio_item
            - modificar_iva_item
            - cambiar_forma_pago
            - crear_cliente
            - crear_producto
            - confirmar_emision
            - emitir_nota_credito
            - cancelar
            - consultar_resumen
            - consultar_cuentas_por_cobrar
            - consultar_saldo_a_favor
            - registrar_abono

            Debes preferir herramientas antes de asumir.
            Si el cliente o producto tiene múltiples coincidencias, pide aclaración mostrando opciones.
            Si el cliente o producto no existe, dilo claramente.
            Si el usuario menciona un servicio manual con precio, puedes usar AgregarServicioManualAFactura.
            Si el usuario pide cartera, cuentas por cobrar, saldo a favor o registrar un abono, usa las herramientas del backend para responder con datos reales.

            Estado actual serializado:
            """ + JsonSerializer.Serialize(resumenState);
    }
}
