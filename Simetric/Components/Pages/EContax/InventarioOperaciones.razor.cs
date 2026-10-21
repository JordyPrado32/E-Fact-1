using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Simetric.DTOs.EContax;
using Simetric.Services.EContax;

namespace Simetric.Components.Pages.EContax;

public class InventarioOperacionesBase : ComponentBase
{
    [Inject] protected HttpClient Http { get; set; } = default!;
    [Inject] protected AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    [Inject] protected NavigationManager NavManager { get; set; } = default!;

    protected EContaxInventarioDashboardDto Dashboard { get; private set; } = new();
    protected EContaxInventarioTransferenciaDto Transferencia { get; } = new();
    protected EContaxInventarioDevolucionDto Devolucion { get; } = new();
    protected EContaxInventarioRevalorizacionDto Revalorizacion { get; } = new();
    protected List<EContaxSucursalDto> Sucursales => Dashboard.Sucursales;
    protected List<EContaxInventarioItemDto> Productos => Dashboard.Items.GroupBy(x => x.CodigoProducto).Select(x => x.First()).ToList();
    protected List<LineaToma> LineasToma { get; private set; } = new();
    protected int ProductoTransferenciaAgregar;
    protected decimal CantidadTransferenciaAgregar;
    protected int ProductoDevolucionAgregar;
    protected decimal CantidadDevolucionAgregar;
    protected int ProductoReservaAgregar;
    protected decimal CantidadReservaAgregar;
    protected List<OperacionLinea> LineasTransferencia { get; } = new();
    protected List<OperacionLinea> LineasDevolucion { get; } = new();
    protected List<OperacionLinea> LineasReserva { get; } = new();
    protected int SucursalToma { get; set; }
    protected string? ObservacionToma { get; set; }
    protected bool Cargando { get; private set; } = true;
    protected bool Guardando { get; private set; }
    protected string? Error { get; private set; }
    protected string? Mensaje { get; private set; }
    protected bool PuedeEditar => Dashboard.PuedeEditar;
    private int _usuarioId;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        var user = (await AuthStateProvider.GetAuthenticationStateAsync()).User;
        _usuarioId = int.TryParse(user.FindFirst("IdUsuario")?.Value ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        await CargarAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task CargarAsync()
    {
        try { Dashboard = await Http.GetFromJsonAsync<EContaxInventarioDashboardDto>($"api/e-contax/inventarios?userId={_usuarioId}") ?? new(); SucursalToma = Dashboard.Sucursales.FirstOrDefault()?.IdSucursal ?? 0; PrepararToma(); }
        catch (Exception ex) { Error = ex.Message; }
        finally { Cargando = false; }
    }

    protected void PrepararToma()
    {
        LineasToma = Dashboard.Items.Where(x => x.IdSucursal == SucursalToma).Select(x => new LineaToma(x.CodigoProducto, x.NombreProducto ?? string.Empty, x.StockActual)).ToList();
    }

    protected async Task TransferirAsync()
    {
        Transferencia.Detalles = LineasTransferencia.Select(x => new EContaxInventarioOperacionDetalleDto { CodigoProducto = x.CodigoProducto, Cantidad = x.Cantidad }).ToList();
        await EjecutarAsync($"api/e-contax/inventarios/transferencias?userId={_usuarioId}", Transferencia);
        if (string.IsNullOrWhiteSpace(Error)) LineasTransferencia.Clear();
    }

    protected async Task DevolverAsync()
    {
        Devolucion.Detalles = LineasDevolucion.Select(x => new EContaxInventarioOperacionDetalleDto { CodigoProducto = x.CodigoProducto, Cantidad = x.Cantidad }).ToList();
        await EjecutarAsync($"api/e-contax/inventarios/devoluciones-compra?userId={_usuarioId}", Devolucion);
        if (string.IsNullOrWhiteSpace(Error)) LineasDevolucion.Clear();
    }

    protected async Task AplicarTomaAsync() => await EjecutarAsync($"api/e-contax/inventarios/tomas-fisicas?userId={_usuarioId}", new EContaxInventarioTomaFisicaDto { IdSucursal = SucursalToma, Observacion = ObservacionToma, Detalles = LineasToma.Select(x => new EContaxInventarioTomaFisicaDetalleDto { CodigoProducto = x.CodigoProducto, StockContado = x.StockContado }).ToList() });

    protected async Task ReservarAsync() => await CambiarReservaAsync("reservas", false);
    protected async Task LiberarReservaAsync() => await CambiarReservaAsync("reservas/liberar", true);

    private async Task CambiarReservaAsync(string ruta, bool liberar)
    {
        await EjecutarAsync($"api/e-contax/inventarios/{ruta}?userId={_usuarioId}", new EContaxInventarioReservaDto
        {
            IdSucursal = SucursalToma,
            Detalles = LineasReserva.Select(x => new EContaxInventarioOperacionDetalleDto { CodigoProducto = x.CodigoProducto, Cantidad = x.Cantidad }).ToList()
        });
        if (string.IsNullOrWhiteSpace(Error)) LineasReserva.Clear();
    }

    protected async Task RevalorizarAsync()
    {
        Revalorizacion.IdSucursal = SucursalToma;
        await EjecutarAsync($"api/e-contax/inventarios/revalorizaciones?userId={_usuarioId}", Revalorizacion);
    }

    private async Task EjecutarAsync<T>(string url, T payload)
    {
        if (Guardando) return;
        Guardando = true; Error = null; Mensaje = null;
        try
        {
            var response = await Http.PostAsJsonAsync(url, payload);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
            var resultado = await response.Content.ReadFromJsonAsync<EContaxInventarioOperacionResultadoDto>();
            Mensaje = resultado?.Mensaje ?? "Operación aplicada correctamente.";
            await CargarAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Guardando = false; }
    }

    protected void Volver() => NavManager.NavigateTo(EContaxRoutes.Inventarios);

    protected string NombreProducto(int codigo) => Productos.FirstOrDefault(x => x.CodigoProducto == codigo)?.NombreProducto ?? $"Producto #{codigo}";

    protected void AgregarTransferencia() => AgregarLinea(LineasTransferencia, ref ProductoTransferenciaAgregar, ref CantidadTransferenciaAgregar);
    protected void AgregarDevolucion() => AgregarLinea(LineasDevolucion, ref ProductoDevolucionAgregar, ref CantidadDevolucionAgregar);
    protected void AgregarReserva() => AgregarLinea(LineasReserva, ref ProductoReservaAgregar, ref CantidadReservaAgregar);

    protected void QuitarLinea(List<OperacionLinea> lineas, OperacionLinea linea) => lineas.Remove(linea);

    private void AgregarLinea(List<OperacionLinea> lineas, ref int codigo, ref decimal cantidad)
    {
        Error = null;
        if (codigo <= 0 || cantidad <= 0)
        {
            Error = "Selecciona un producto y una cantidad mayor que cero.";
            return;
        }

        var codigoActual = codigo;
        var existente = lineas.FirstOrDefault(x => x.CodigoProducto == codigoActual);
        if (existente is null) lineas.Add(new OperacionLinea(codigo, cantidad));
        else existente.Cantidad += cantidad;
        codigo = 0;
        cantidad = 0;
    }

    protected sealed record LineaToma(int CodigoProducto, string Nombre, decimal StockSistema)
    {
        public decimal StockContado { get; set; } = StockSistema;
    }

    protected sealed class OperacionLinea
    {
        public OperacionLinea(int codigoProducto, decimal cantidad) { CodigoProducto = codigoProducto; Cantidad = cantidad; }
        public int CodigoProducto { get; }
        public decimal Cantidad { get; set; }
    }
}
