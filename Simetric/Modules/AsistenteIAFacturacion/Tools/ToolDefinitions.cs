namespace Simetric.Modules.AsistenteIAFacturacion.Tools;

public static class ToolDefinitions
{
    public const string BuscarCliente = "BuscarCliente";
    public const string BuscarProducto = "BuscarProducto";
    public const string CrearCliente = "CrearCliente";
    public const string CrearProducto = "CrearProducto";
    public const string CrearBorradorFactura = "CrearBorradorFactura";
    public const string AgregarProductoAFactura = "AgregarProductoAFactura";
    public const string AgregarServicioManualAFactura = "AgregarServicioManualAFactura";
    public const string AplicarDescuentoLinea = "AplicarDescuentoLinea";
    public const string AplicarDescuentoGlobal = "AplicarDescuentoGlobal";
    public const string QuitarProductoDeFactura = "QuitarProductoDeFactura";
    public const string ModificarCantidadProducto = "ModificarCantidadProducto";
    public const string ModificarPrecioItem = "ModificarPrecioItem";
    public const string ModificarIvaItem = "ModificarIvaItem";
    public const string ModificarFormaPago = "ModificarFormaPago";
    public const string BuscarClientesConCuentasPorCobrar = "BuscarClientesConCuentasPorCobrar";
    public const string ConsultarCuentasPorCobrar = "ConsultarCuentasPorCobrar";
    public const string ConsultarSaldoAFavor = "ConsultarSaldoAFavor";
    public const string RegistrarAbonoGeneral = "RegistrarAbonoGeneral";
    public const string CalcularTotales = "CalcularTotales";
    public const string ValidarFactura = "ValidarFactura";
    public const string ObtenerResumenFactura = "ObtenerResumenFactura";
    public const string EmitirFactura = "EmitirFactura";
    public const string EmitirNotaCreditoDesdeFactura = "EmitirNotaCreditoDesdeFactura";

    private static readonly object[] CachedTools =
    [
        Function(BuscarCliente, "Busca clientes reales en el sistema por nombre, razón social o identificación.",
            Properties(("query", "string", "Texto a buscar del cliente.", true))),
        Function(BuscarProducto, "Busca productos reales en el sistema por nombre o código.",
            Properties(("query", "string", "Texto a buscar del producto.", true))),
        Function(CrearCliente, "Crea un cliente nuevo cuando no existe y el usuario ya proporcionó todos los datos requeridos.",
            Properties(
                ("nombreCompleto", "string", "Nombre completo del cliente cuando es persona natural.", false),
                ("apellidos", "string", "Apellidos del cliente persona natural.", false),
                ("nombres", "string", "Nombres del cliente persona natural.", false),
                ("razonSocial", "string", "Razón social si es persona jurídica.", false),
                ("nombreComercial", "string", "Nombre comercial si es persona jurídica.", false),
                ("identificacion", "string", "Cédula o RUC solo con números.", true),
                ("correo", "string", "Correo principal del cliente.", true),
                ("celular", "string", "Celular de 10 dígitos.", true),
                ("telefono", "string", "Teléfono de 7 a 10 dígitos.", true),
                ("direccion", "string", "Dirección del cliente.", true),
                ("obligadoContabilidad", "string", "SI o NO.", true),
                ("esEmpresa", "boolean", "Indica si el cliente es persona jurídica.", true),
                ("pais", "string", "Nombre del país.", true),
                ("provincia", "string", "Nombre de la provincia.", true),
                ("ciudad", "string", "Nombre de la ciudad.", true))),
        Function(CrearProducto, "Crea un producto o servicio nuevo cuando no existe y el usuario ya indicó nombre, precio e IVA.",
            Properties(
                ("nombre", "string", "Nombre del producto o servicio.", true),
                ("codigoPrincipal", "string", "Código principal opcional.", false),
                ("precioUnitario", "number", "Precio unitario base del producto.", true),
                ("tipo", "string", "PRODUCTO o SERVICIO.", true),
                ("tarifaPorcentaje", "number", "IVA del producto en porcentaje, por ejemplo 0, 5, 12, 15.", true),
                ("observacion", "string", "Observación opcional.", false))),
        Function(CrearBorradorFactura, "Crea o reinicia un borrador de factura y opcionalmente asigna un cliente.",
            Properties(("clienteId", "integer", "Identificador del cliente seleccionado.", false),
                       ("clienteNombre", "string", "Nombre del cliente cuando aún no hay id.", false))),
        Function(AgregarProductoAFactura, "Agrega un producto existente al borrador de factura.",
            Properties(("productoId", "integer", "Identificador del producto.", true),
                       ("cantidad", "number", "Cantidad a agregar.", true),
                       ("descuentoPorcentaje", "number", "Descuento porcentual opcional.", false),
                       ("descuentoValor", "number", "Descuento fijo opcional.", false))),
        Function(AgregarServicioManualAFactura, "Agrega un servicio manual al borrador cuando el usuario da descripción y precio.",
            Properties(("descripcion", "string", "Descripción del servicio.", true),
                       ("cantidad", "number", "Cantidad del servicio.", true),
                       ("precioUnitario", "number", "Precio unitario del servicio.", true),
                       ("descuentoPorcentaje", "number", "Descuento porcentual opcional.", false),
                       ("descuentoValor", "number", "Descuento fijo opcional.", false),
                       ("tarifaPorcentaje", "number", "IVA del servicio en porcentaje.", false))),
        Function(AplicarDescuentoLinea, "Aplica o cambia descuento a una línea específica del borrador.",
            Properties(("referenciaItem", "string", "Id o descripción del item.", true),
                       ("porcentaje", "number", "Descuento porcentual.", false),
                       ("valor", "number", "Descuento fijo.", false))),
        Function(AplicarDescuentoGlobal, "Aplica o cambia descuento global a toda la factura.",
            Properties(("porcentaje", "number", "Descuento porcentual global.", false),
                       ("valor", "number", "Descuento fijo global.", false))),
        Function(QuitarProductoDeFactura, "Quita un item del borrador por id o descripción.",
            Properties(("referenciaItem", "string", "Id o descripción del item.", true))),
        Function(ModificarCantidadProducto, "Modifica la cantidad de un item ya agregado.",
            Properties(("referenciaItem", "string", "Id o descripción del item.", true),
                       ("cantidad", "number", "Nueva cantidad.", true))),
        Function(ModificarPrecioItem, "Modifica solo el precio unitario de un item dentro de la factura actual, sin alterar el producto guardado en catálogo.",
            Properties(("referenciaItem", "string", "Id o descripción del item.", true),
                       ("precioUnitario", "number", "Nuevo precio unitario para esta factura.", true))),
        Function(ModificarIvaItem, "Modifica solo el IVA de un item dentro de la factura actual, sin alterar el producto guardado en catálogo.",
            Properties(("referenciaItem", "string", "Id o descripción del item.", true),
                       ("tarifaPorcentaje", "number", "Nueva tarifa de IVA para esta factura.", true))),
        Function(ModificarFormaPago, "Cambia la forma de pago de la factura.",
            Properties(("formaPago", "string", "Forma de pago elegida por el usuario.", true),
                       ("diasCredito", "integer", "Cantidad de días de crédito cuando aplique.", false))),
        Function(ConsultarCuentasPorCobrar, "Consulta cuentas por cobrar reales del usuario o de un cliente específico.",
            Properties(("filtroCliente", "string", "Nombre o identificación del cliente para filtrar la cartera.", false))),
        Function(ConsultarSaldoAFavor, "Consulta el saldo a favor disponible de un cliente.",
            Properties(("filtroCliente", "string", "Nombre o identificación del cliente.", false))),
        Function(RegistrarAbonoGeneral, "Registra un abono general real y lo distribuye automáticamente en las facturas pendientes del cliente.",
            Properties(("monto", "number", "Monto recibido para aplicar.", true),
                       ("filtroCliente", "string", "Nombre o identificación del cliente.", false),
                       ("observacion", "string", "Observación opcional del pago.", false))),
        Function(BuscarClientesConCuentasPorCobrar, "Busca los clientes que actualmente tienen cuentas por cobrar o cartera pendiente.",
            Properties(("filtroCliente", "string", "Nombre o identificacion para filtrar los clientes con cartera.", false))),
        Function(CalcularTotales, "Recalcula subtotal, descuentos, impuestos y total del borrador.",
            Properties()),
        Function(ValidarFactura, "Valida que la factura esté lista para pedir confirmación final o emitir.",
            Properties()),
        Function(ObtenerResumenFactura, "Devuelve un resumen legible del borrador actual.",
            Properties()),
        Function(EmitirFactura, "Emite la factura usando el servicio real solo cuando el usuario ya confirmó explícitamente.",
            Properties()),
        Function(EmitirNotaCreditoDesdeFactura, "Emite una nota de credito automatica desde una factura autorizada existente.",
            Properties(
                ("referenciaFactura", "string", "Numero o referencia de la factura origen.", true),
                ("motivo", "string", "Motivo de la nota de credito.", false)))
    ];

    public static object[] BuildTools() => CachedTools;

    private static object Function(string name, string description, object parameters) => new
    {
        type = "function",
        function = new
        {
            name,
            description,
            parameters
        }
    };

    private static object Properties(params (string name, string type, string description, bool required)[] properties)
    {
        var props = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var item in properties)
        {
            props[item.name] = new
            {
                type = item.type,
                description = item.description
            };

            if (item.required)
                required.Add(item.name);
        }

        return new
        {
            type = "object",
            properties = props,
            required,
            additionalProperties = false
        };
    }
}
