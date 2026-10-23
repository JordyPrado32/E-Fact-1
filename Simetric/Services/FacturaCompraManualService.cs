using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;
using Simetric.ViewModels;

namespace Simetric.Services;

public sealed class FacturaCompraManualService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public FacturaCompraManualService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<int> GuardarAsync(int idUsuario, FacturaCompraManualVM modelo)
    {
        if (string.IsNullOrWhiteSpace(modelo.RucProveedor) || string.IsNullOrWhiteSpace(modelo.Proveedor))
            throw new InvalidOperationException("Selecciona un proveedor del CRUD de proveedores.");
        if (modelo.FechaEmision == default || modelo.FechaEmision.Date > DateTime.Today)
            throw new InvalidOperationException("La fecha de emisión debe ser válida y no puede ser futura.");

        var ruc = SoloDigitos(modelo.RucProveedor);
        if (ruc.Length is not (10 or 13))
            throw new InvalidOperationException("La identificación del proveedor debe tener 10 o 13 dígitos.");
        if (string.IsNullOrWhiteSpace(modelo.FormaPago))
            throw new InvalidOperationException("Selecciona la forma de pago.");
        if (modelo.EsCredito && modelo.PlazoCredito <= 0)
            throw new InvalidOperationException("Ingresa un plazo de crédito mayor a cero.");
        if (modelo.Detalles.Count == 0 || modelo.Detalles.Any(x =>
            string.IsNullOrWhiteSpace(x.Descripcion) ||
            x.Cantidad <= 0 ||
            x.PrecioUnitario < 0 ||
            x.Descuento < 0 ||
            x.Descuento > x.Cantidad * x.PrecioUnitario ||
            x.Tarifa is not (0 or 5 or 8 or 12 or 15)))
            throw new InvalidOperationException("Agrega al menos un detalle válido a la factura.");
        if (modelo.Total <= 0m)
            throw new InvalidOperationException("La factura debe tener un total mayor a cero.");

        await using var context = await _dbFactory.CreateDbContextAsync();
        if (!await context.Proveedores.AsNoTracking().AnyAsync(x => x.estado == true && x.ruc == ruc))
            throw new InvalidOperationException("El proveedor debe existir y estar activo en el CRUD de proveedores.");

        var cliente = await context.Clientes.FirstOrDefaultAsync(x => x.Numeroidentificacion == ruc && (x.Usuario == idUsuario || x.Usuario == null));
        if (cliente == null)
        {
            cliente = new Cliente
            {
                Numeroidentificacion = ruc,
                Tipoidentificacion = string.IsNullOrWhiteSpace(modelo.TipoIdentificacion) ? (ruc.Length == 13 ? "04" : "05") : modelo.TipoIdentificacion,
                Nombrerazonsocial = modelo.Proveedor.Trim(),
                Nombrecomercial = string.IsNullOrWhiteSpace(modelo.NombreComercial) ? modelo.Proveedor.Trim() : modelo.NombreComercial.Trim(),
                Nombres = string.IsNullOrWhiteSpace(modelo.Nombres) ? modelo.Proveedor.Trim() : modelo.Nombres.Trim(),
                Apellidos = string.IsNullOrWhiteSpace(modelo.Apellidos) ? "." : modelo.Apellidos.Trim(),
                Direccion = modelo.Direccion.Trim(),
                Telefonoconvencional = modelo.Telefono.Trim(),
                Correo = modelo.Correo.Trim(),
                Referencia = modelo.Referencia.Trim(),
                Oblgconta = modelo.ObligadoContabilidad,
                TipoCliente = 2,
                Usuario = idUsuario,
                Estado = true,
                Fechaingreso = DateOnly.FromDateTime(DateTime.Today)
            };
            context.Clientes.Add(cliente);
            await context.SaveChangesAsync();
        }

        var serie = "MANUAL";
        var numero = DateTime.Now.ToString("yyyyMMddHHmmssfff");
        var duplicada = await context.ComprasFacturas.AnyAsync(x =>
            x.Usuario == idUsuario && x.CodDocumento == "01" && x.TipoDocumento == "COMPRA" &&
            x.Serie == serie && x.NumFactura == numero && x.CodClientes == cliente.Codcliente && x.Estado == true);
        if (duplicada)
            throw new InvalidOperationException("La factura de compra ya está registrada para este proveedor.");

        var total = modelo.Total;
        var esCredito = modelo.EsCredito;
        var compra = new ComprasFactura
        {
            CodClave = $"MANUAL-COMPRA-{Guid.NewGuid():N}",
            CodClientes = cliente.Codcliente,
            FchAutorizacion = modelo.FechaEmision.Date,
            NumFactura = numero,
            CodDocumento = "01",
            Subtotal = modelo.Subtotal,
            Subtotal0 = modelo.Detalles.Where(x => x.Tarifa == 0).Sum(x => x.Base),
            Subtotal12 = modelo.Detalles.Where(x => x.Tarifa == 12).Sum(x => x.Base),
            Iva = modelo.Iva,
            ValorTotal = total,
            FechaVence = esCredito ? modelo.FechaEmision.Date.AddDays(Math.Max(modelo.PlazoCredito, 1)) : modelo.FechaEmision.Date,
            Usuario = idUsuario,
            Autorizado = "1",
            Mensaje = string.IsNullOrWhiteSpace(modelo.Observacion)
                ? "Factura de compra registrada manualmente; no se envía al SRI."
                : modelo.Observacion.Trim(),
            Serie = serie,
            TipoPago = string.IsNullOrWhiteSpace(modelo.FormaPago) ? "20" : modelo.FormaPago,
            TiempoCredito = esCredito ? Math.Max(modelo.PlazoCredito, 1) : 0,
            Estado = true,
            TipoDocumento = "COMPRA",
            EstadoEnvioSRI = "NO APLICA",
            FechaRegistro = DateTime.Now,
            ValorApagar = esCredito ? total : 0m,
            EstadoPago = esCredito ? "PENDIENTE" : "PAGADO",
            FechaCancelado = esCredito ? null : DateOnly.FromDateTime(DateTime.Today),
            Contabilizado = false,
            Inventario = false
        };

        context.ComprasFacturas.Add(compra);
        await context.SaveChangesAsync();

        foreach (var detalle in modelo.Detalles)
        {
            context.ComprasDetalleFac.Add(new ComprasDetalleFac
            {
                CodFactura = compra.CodFactura,
                CodProducto = 0,
                CodPrincipal = detalle.Codigo.Trim(),
                CantProducto = detalle.Cantidad,
                DescripProducto = detalle.Descripcion.Trim(),
                PrecioProducto = detalle.PrecioUnitario,
                Descuento = detalle.Descuento,
                ValorTProducto = detalle.Base,
                ValorIVA = detalle.Iva,
                ValorTotal = detalle.Base + detalle.Iva,
                PorImp = (int)detalle.Tarifa,
                Tarifa = (int)detalle.Tarifa,
                Inventariado = false,
                Observacion = string.IsNullOrWhiteSpace(detalle.Detalle) ? "Factura de compra manual" : detalle.Detalle.Trim()
            });
        }

        await context.SaveChangesAsync();
        return compra.CodFactura;
    }

    public async Task<List<CompraManualVM>> ListarAsync(int idUsuario, bool incluirAnuladas = true)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var compras = await CargarCabecerasAsync(context, idUsuario, incluirAnuladas);
        var ids = compras.Select(x => x.IdFactura).ToList();
        var pagos = ids.Count == 0
            ? new Dictionary<int, decimal>()
            : await context.CuentasPorPagarPagos.AsNoTracking()
                .Where(x => x.Estado && ids.Contains(x.CodFactura))
                .GroupBy(x => x.CodFactura)
                .Select(x => new { x.Key, Total = x.Sum(p => p.Monto) })
                .ToDictionaryAsync(x => x.Key, x => x.Total);

        foreach (var compra in compras)
        {
            var pagosRegistrados = pagos.GetValueOrDefault(compra.IdFactura);
            compra.TotalPagado = compra.SaldoPendiente <= 0m && compra.EstadoPago.Contains("PAG", StringComparison.OrdinalIgnoreCase)
                ? compra.Total
                : pagosRegistrados;
            compra.SaldoPendiente = compra.Estado == "ANULADA" ? 0m : Math.Max(compra.SaldoPendiente - pagosRegistrados, 0m);
        }

        return compras;
    }

    public async Task<CompraManualVM?> ObtenerAsync(int idUsuario, int idFactura)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var compra = (await CargarCabecerasAsync(context, idUsuario, incluirAnuladas: true))
            .FirstOrDefault(x => x.IdFactura == idFactura);
        if (compra == null)
            return null;

        compra.Detalles = await context.ComprasDetalleFac.AsNoTracking()
            .Where(x => x.CodFactura == idFactura)
            .OrderBy(x => x.CodLinea)
            .Select(x => new CompraManualDetalleVM
            {
                Codigo = x.CodPrincipal ?? "",
                Descripcion = x.DescripProducto ?? "",
                Detalle = x.Observacion ?? "",
                Cantidad = x.CantProducto ?? 0m,
                PrecioUnitario = x.PrecioProducto ?? 0m,
                Descuento = x.Descuento ?? 0m,
                Base = x.ValorTProducto ?? 0m,
                Iva = x.ValorIVA ?? 0m,
                Total = x.ValorTotal ?? 0m,
                Tarifa = x.Tarifa ?? 0
            })
            .ToListAsync();
        return compra;
    }

    public async Task<bool> AnularAsync(int idUsuario, int idFactura)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var compra = await context.ComprasFacturas.FirstOrDefaultAsync(x =>
            x.CodFactura == idFactura &&
            x.Usuario == idUsuario &&
            x.CodDocumento == "01" &&
            x.TipoDocumento == "COMPRA" &&
            x.CodClave != null && x.CodClave.StartsWith("MANUAL-COMPRA-") &&
            x.Estado == true);
        if (compra == null)
            return false;

        var tienePagos = await context.CuentasPorPagarPagos.AnyAsync(x => x.CodFactura == idFactura && x.Estado && x.Monto > 0m);
        if (tienePagos)
            throw new InvalidOperationException("No se puede anular una compra que ya tiene pagos registrados.");

        compra.Estado = false;
        compra.EstadoPago = "ANULADO";
        compra.ValorApagar = 0m;
        compra.FechaCancelado = null;
        compra.Mensaje = "Factura de compra manual anulada; no se envía al SRI.";
        await context.SaveChangesAsync();
        return true;
    }

    private static async Task<List<CompraManualVM>> CargarCabecerasAsync(AppDbContext context, int idUsuario, bool incluirAnuladas)
    {
        var comprasQuery = context.ComprasFacturas.AsNoTracking()
            .Where(x => x.Usuario == idUsuario &&
                x.CodDocumento == "01" &&
                x.TipoDocumento == "COMPRA" &&
                x.CodClave != null && x.CodClave.StartsWith("MANUAL-COMPRA-"));
        if (!incluirAnuladas)
            comprasQuery = comprasQuery.Where(x => x.Estado == true);

        return await (
            from compra in comprasQuery
            join cliente in context.Clientes.AsNoTracking() on compra.CodClientes equals cliente.Codcliente into clientes
            from cliente in clientes.DefaultIfEmpty()
            orderby compra.FechaRegistro descending, compra.CodFactura descending
            select new CompraManualVM
            {
                IdFactura = compra.CodFactura,
                NumeroDocumento = FormatearDocumento(compra),
                Proveedor = cliente == null ? "Proveedor sin nombre" : (cliente.Nombrerazonsocial ?? cliente.Nombrecomercial ?? ((cliente.Nombres ?? "") + " " + (cliente.Apellidos ?? "")).Trim()),
                IdentificacionProveedor = cliente == null ? "" : cliente.Numeroidentificacion ?? "",
                FechaEmision = compra.FchAutorizacion,
                FechaVencimiento = compra.FechaVence,
                FormaPago = compra.TipoPago ?? "",
                EstadoPago = compra.EstadoPago ?? "",
                Estado = compra.Estado == true ? "ACTIVA" : "ANULADA",
                Observacion = compra.Mensaje ?? "",
                Total = compra.ValorTotal ?? 0m,
                SaldoPendiente = compra.Estado == true ? Math.Max(compra.ValorApagar ?? 0m, 0m) : 0m
            }).ToListAsync();
    }

    private static string FormatearDocumento(ComprasFactura compra)
    {
        var serie = (compra.Serie ?? "").Replace("-", "").Trim();
        var serieFormateada = serie.Length == 6 ? $"{serie[..3]}-{serie[3..]}" : serie;
        return $"{serieFormateada}-{(compra.NumFactura ?? "").PadLeft(9, '0')}";
    }

    private static string SoloDigitos(string? valor) => new((valor ?? "").Where(char.IsDigit).ToArray());
}
