using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Services;

public sealed class VendedorComisionService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public VendedorComisionService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<VendedorComisionesPageDto> ObtenerAsync(int userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var usuario = await db.Usuarios.AsNoTracking()
            .Where(x => x.IdUsuario == userId && x.Estado == true)
            .Select(x => new { x.IdTipoUsuario, x.TipoCliente, x.IdVendedor })
            .FirstOrDefaultAsync();

        if (usuario is null)
            return new();

        var esAdministrador = usuario.IdTipoUsuario == BackOfficePermissionHelper.SuperAdministradorRoleId ||
                              (usuario.IdTipoUsuario == BackOfficePermissionHelper.BackOfficeRoleId &&
                               usuario.TipoCliente == BackOfficePermissionHelper.AdministradorBackOfficeTipoCliente);
        var esVendedor = usuario.IdTipoUsuario == BackOfficePermissionHelper.BackOfficeRoleId && usuario.TipoCliente == 2;
        var vendedorIds = esAdministrador
            ? await ObtenerVendedoresAsync(db)
            : esVendedor && usuario.IdVendedor is > 0
                ? new List<int> { usuario.IdVendedor.Value }
                : new List<int>();

        foreach (var idVendedor in vendedorIds)
            await SincronizarAsync(idVendedor);

        if (vendedorIds.Count == 0)
            return new() { EsAdministrador = esAdministrador };

        await using var consulta = await _dbFactory.CreateDbContextAsync();
        var filas = await consulta.VendedorComisiones.AsNoTracking()
            .Where(x => vendedorIds.Contains(x.IdVendedor) &&
                        !consulta.Clientes.Any(c => c.Codcliente == x.IdCliente &&
                            consulta.Usuarios.Any(u => u.IdVendedor == x.IdVendedor && u.IdUsuario == c.Usuario)))
            .Join(consulta.VendedoresBackOffice.AsNoTracking(), x => x.IdVendedor, x => x.IdVendedor, (comision, vendedor) => new { comision, vendedor })
            .OrderByDescending(x => x.comision.FechaGeneracion)
            .Select(x => new VendedorComisionRow
            {
                IdComision = x.comision.IdComision,
                IdVendedor = x.comision.IdVendedor,
                IdFactura = x.comision.IdFactura,
                Vendedor = x.vendedor.Nombre,
                Cliente = consulta.Clientes
                    .Where(c => c.Codcliente == x.comision.IdCliente)
                    .Select(c => c.Nombrerazonsocial ?? c.Nombrecomercial ?? ((c.Nombres ?? "") + " " + (c.Apellidos ?? "")))
                    .FirstOrDefault() ?? "Cliente",
                NumeroFactura = consulta.Facturas.Where(f => f.Codfactura == x.comision.IdFactura).Select(f => f.Numfactura).FirstOrDefault() ?? x.comision.IdFactura.ToString(),
                Producto = consulta.Detallefacturas.Where(d => d.Codfactura == x.comision.IdFactura).OrderBy(d => d.Codlinea).Select(d => d.Descripproducto).FirstOrDefault() ?? "Servicio Numerica",
                TipoComision = x.comision.TipoComision,
                BaseComisionable = x.comision.BaseComisionable,
                Porcentaje = x.comision.Porcentaje,
                Valor = x.comision.Valor,
                Periodo = x.comision.Periodo,
                Estado = x.comision.Estado,
                FechaGeneracion = x.comision.FechaGeneracion,
                FechaPago = x.comision.FechaPago
            })
            .ToListAsync();

        var facturaIds = filas.Select(x => x.IdFactura).Distinct().ToArray();
        var detalles = await consulta.Detallefacturas.AsNoTracking()
            .Where(x => facturaIds.Contains(x.Codfactura))
            .Select(x => new { x.Codfactura, x.Descripproducto, x.Cantproducto })
            .ToListAsync();
        var facturasComisionables = detalles
            .Where(x => EsCompraDeProductoComisionable(x.Descripproducto))
            .Select(x => x.Codfactura)
            .ToHashSet();
        filas = filas.Where(x => facturasComisionables.Contains(x.IdFactura)).ToList();
        foreach (var fila in filas)
        {
            fila.Firmas = detalles
                .Where(x => x.Codfactura == fila.IdFactura && AliadoServicioHelper.Clasificar(x.Descripproducto) == "E-RÚBRICA")
                .Select(x => new VendedorFirmaDesglose
                {
                    Descripcion = x.Descripproducto ?? "Firma electrónica",
                    Cantidad = x.Cantproducto,
                    CostoUnitario = ESignPricing.ObtenerCostoProveedor(x.Descripproducto),
                    CostoTotal = decimal.Round(ESignPricing.ObtenerCostoProveedor(x.Descripproducto) * x.Cantproducto, 2, MidpointRounding.AwayFromZero)
                })
                .ToList();
            fila.CostoFirmas = fila.Firmas.Sum(x => x.CostoTotal);
        }

        return new()
        {
            EsAdministrador = esAdministrador,
            Vendedor = esVendedor ? filas.FirstOrDefault()?.Vendedor : null,
            Comisiones = filas
        };
    }

    public async Task<(bool Success, string Message)> MarcarPagadaAsync(int actorId, int idComision)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var actor = await db.Usuarios.AsNoTracking()
            .Where(x => x.IdUsuario == actorId && x.Estado == true)
            .Select(x => new { x.IdTipoUsuario, x.TipoCliente })
            .FirstOrDefaultAsync();
        var esAdministrador = actor?.IdTipoUsuario == BackOfficePermissionHelper.SuperAdministradorRoleId ||
                              (actor?.IdTipoUsuario == BackOfficePermissionHelper.BackOfficeRoleId &&
                               actor.TipoCliente == BackOfficePermissionHelper.AdministradorBackOfficeTipoCliente);
        if (!esAdministrador)
            return (false, "No tienes permisos para pagar comisiones de vendedores.");

        var comision = await db.VendedorComisiones.FirstOrDefaultAsync(x => x.IdComision == idComision);
        if (comision is null)
            return (false, "La comisión no existe.");
        if (!string.Equals(comision.Estado, "Pendiente", StringComparison.OrdinalIgnoreCase))
            return (false, "Solo se pueden pagar comisiones pendientes.");

        comision.Estado = "Pagado";
        comision.FechaPago = DateTime.Now;
        comision.IdUsuarioPago = actorId;
        await db.SaveChangesAsync();
        return (true, "Comisión marcada como pagada.");
    }

    private async Task SincronizarAsync(int idVendedor)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var vendedor = await db.VendedoresBackOffice.AsNoTracking()
            .Where(x => x.IdVendedor == idVendedor && !x.EsSistema && x.Activo &&
                        !db.Usuarios.Any(u => u.IdVendedor == x.IdVendedor && u.IdTipoUsuarioNavigation!.NombreTipo == AliadoPortalService.RoleName))
            .Select(x => new { x.IdVendedor, x.PorcentajeBase })
            .FirstOrDefaultAsync();
        if (vendedor is null)
            return;

        var usuarioVendedorIds = await db.Usuarios.AsNoTracking()
            .Where(x => x.IdVendedor == idVendedor && x.Estado == true)
            .Select(x => x.IdUsuario)
            .ToListAsync();

        var configuracion = await db.AliadoPortalConfiguraciones.AsNoTracking().SingleOrDefaultAsync(x => x.IdConfiguracion == 1) ?? new AliadoPortalConfiguracion();
        var facturas = await db.Facturas.AsNoTracking()
            .Where(x => x.Idvendedor == idVendedor && (x.Estado == true || x.Estado == null) &&
                        !db.Clientes.Any(c => c.Codcliente == x.Codclientes &&
                            c.Usuario.HasValue && usuarioVendedorIds.Contains(c.Usuario.Value)) &&
                        !db.NotaCreditos.Any(nc => nc.IdDocModificado == x.Codfactura && nc.Estado == true && nc.Autorizado == DocumentoAutorizacionHelper.EstadoAutorizado))
            .Select(x => new FacturaVendedorComisionRow
            {
                IdFactura = x.Codfactura,
                IdCliente = x.Codclientes,
                Producto = x.Detallefacturas.OrderBy(d => d.Codlinea).Select(d => d.Descripproducto).FirstOrDefault() ?? string.Empty,
                Detalles = x.Detallefacturas.Select(d => new AliadoComisionDetalle(d.Descripproducto, d.Cantproducto)).ToList(),
                FechaVencimiento = x.Fechavence,
                Subtotal = x.Subtotal,
                Subtotal0 = x.Subtotal0,
                Subtotal12 = x.Subtotal12,
                Fecha = x.Fchautorizacion ?? x.Fechaentrega ?? DateTime.Now,
                Autorizado = x.Autorizado == true,
                EstadoPago = x.Estadopago
            })
            .ToListAsync();
        facturas = facturas
            .Where(x => x.Detalles.Any(d => EsCompraDeProductoComisionable(d.Producto)))
            .ToList();

        var comprasClienteServicio = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var factura in facturas.OrderBy(x => x.Fecha).ThenBy(x => x.IdFactura))
        {
            if (factura.IdCliente is not > 0 || string.IsNullOrWhiteSpace(factura.Producto))
                continue;
            factura.EsRenovacion = !comprasClienteServicio.Add($"{factura.IdCliente.Value}|{AliadoServicioHelper.Normalizar(factura.Producto)}") || factura.FechaVencimiento.HasValue;
        }

        var existentes = await db.VendedorComisiones.Where(x => x.IdVendedor == idVendedor).ToDictionaryAsync(x => new { x.IdFactura, x.TipoComision });
        var ventasPorPeriodo = facturas.GroupBy(x => x.Fecha.ToString("yyyy-MM"))
            .ToDictionary(x => x.Key, x => x.Sum(y => AliadoComisionCalculationService.ObtenerBaseNeta(y.Subtotal, y.Subtotal0, y.Subtotal12)));
        var gestiones = await db.AliadoRenovacionGestiones.AsNoTracking()
            .Where(x => x.IdVendedor == idVendedor)
            .GroupBy(x => x.IdFactura)
            .Select(x => x.OrderByDescending(y => y.FechaGestion).First())
            .ToDictionaryAsync(x => x.IdFactura);

        foreach (var factura in facturas)
        {
            var baseComisionable = ObtenerBaseComisionable(factura);
            if (baseComisionable <= 0)
                continue;

            gestiones.TryGetValue(factura.IdFactura, out var gestion);
            var fechaReferencia = factura.FechaVencimiento ?? factura.Fecha;
            var gestionEfectiva = gestion?.OrigenGestion != "Numerica" && gestion?.Resultado is not null && gestion.Resultado is not "Pendiente" and not "No responde";
            var intervieneNumerica = factura.EsRenovacion && fechaReferencia.Date <= DateTime.Today.AddDays(configuracion.DiasIntervencionNumerica) && !gestionEfectiva;
            var tipo = !factura.EsRenovacion ? "VentaNueva" : intervieneNumerica || string.Equals(gestion?.OrigenGestion, "Numerica", StringComparison.OrdinalIgnoreCase) ? "RenovacionNumerica" : "RenovacionVendedor";
            var periodo = factura.Fecha.ToString("yyyy-MM");
            var ventasPeriodo = ventasPorPeriodo.GetValueOrDefault(periodo);
            var porcentaje = tipo switch
            {
                "RenovacionNumerica" => configuracion.PorcentajeRenovacionNumerica,
                "RenovacionVendedor" => vendedor.PorcentajeBase > 0m ? vendedor.PorcentajeBase : configuracion.PorcentajeRenovacionAliado,
                _ => ventasPeriodo >= 1000m ? 10m : 5m
            };

            if (existentes.TryGetValue(new { factura.IdFactura, TipoComision = tipo }, out var existente))
            {
                if (existente.Estado == "Pendiente" && (existente.BaseComisionable != baseComisionable || existente.Porcentaje != porcentaje))
                {
                    existente.BaseComisionable = baseComisionable;
                    existente.Porcentaje = porcentaje;
                    existente.Valor = AliadoComisionCalculationService.CalcularValor(baseComisionable, porcentaje);
                }
                continue;
            }

            var nueva = new VendedorComision
            {
                IdVendedor = idVendedor,
                IdFactura = factura.IdFactura,
                IdCliente = factura.IdCliente,
                TipoComision = tipo,
                BaseComisionable = baseComisionable,
                Porcentaje = porcentaje,
                Valor = AliadoComisionCalculationService.CalcularValor(baseComisionable, porcentaje),
                Periodo = periodo,
                Estado = "Pendiente",
                FechaGeneracion = factura.Fecha
            };
            db.VendedorComisiones.Add(nueva);
        }

        await db.SaveChangesAsync();
    }

    private static decimal ObtenerBaseComisionable(FacturaVendedorComisionRow factura)
    {
        var firmas = factura.Detalles
            .Where(x => AliadoServicioHelper.Clasificar(x.Producto) == "E-RÚBRICA")
            .ToList();

        return AliadoComisionCalculationService.ObtenerBaseComisionable(
            AliadoComisionCalculationService.ObtenerBaseNeta(factura.Subtotal, factura.Subtotal0, factura.Subtotal12),
            firmas);
    }

    private static bool EsCompraDeProductoComisionable(string? producto)
    {
        var texto = producto ?? string.Empty;
        return texto.Contains("document", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("recarga", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("firma", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("rúbrica", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("rubrica", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("e-sign", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("esign", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("certificado", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<List<int>> ObtenerVendedoresAsync(AppDbContext db) =>
        await db.VendedoresBackOffice.AsNoTracking()
            .Where(x => !x.EsSistema && x.Activo &&
                        !db.Usuarios.Any(u => u.IdVendedor == x.IdVendedor && u.IdTipoUsuarioNavigation!.NombreTipo == AliadoPortalService.RoleName))
            .Select(x => x.IdVendedor)
            .ToListAsync();

    private sealed class FacturaVendedorComisionRow
    {
        public int IdFactura { get; init; }
        public int? IdCliente { get; init; }
        public string Producto { get; init; } = string.Empty;
        public List<AliadoComisionDetalle> Detalles { get; init; } = new();
        public DateTime? FechaVencimiento { get; init; }
        public decimal? Subtotal { get; init; }
        public decimal? Subtotal0 { get; init; }
        public decimal? Subtotal12 { get; init; }
        public DateTime Fecha { get; init; }
        public bool Autorizado { get; init; }
        public string? EstadoPago { get; init; }
        public bool EsRenovacion { get; set; }
    }
}

public sealed class VendedorComisionesPageDto
{
    public bool EsAdministrador { get; init; }
    public string? Vendedor { get; init; }
    public IReadOnlyList<VendedorComisionRow> Comisiones { get; init; } = Array.Empty<VendedorComisionRow>();
}

public sealed class VendedorComisionRow
{
    public int IdComision { get; init; }
    public int IdVendedor { get; init; }
    public int IdFactura { get; init; }
    public string Vendedor { get; init; } = string.Empty;
    public string Cliente { get; init; } = string.Empty;
    public string NumeroFactura { get; init; } = string.Empty;
    public string Producto { get; init; } = string.Empty;
    public string TipoComision { get; init; } = string.Empty;
    public decimal BaseComisionable { get; init; }
    public decimal Porcentaje { get; init; }
    public decimal Valor { get; init; }
    public string Periodo { get; init; } = string.Empty;
    public string Estado { get; init; } = string.Empty;
    public DateTime FechaGeneracion { get; init; }
    public DateTime? FechaPago { get; init; }
    public decimal CostoFirmas { get; set; }
    public List<VendedorFirmaDesglose> Firmas { get; set; } = new();
}

public sealed class VendedorFirmaDesglose
{
    public string Descripcion { get; init; } = string.Empty;
    public decimal Cantidad { get; init; }
    public decimal CostoUnitario { get; init; }
    public decimal CostoTotal { get; init; }
}
