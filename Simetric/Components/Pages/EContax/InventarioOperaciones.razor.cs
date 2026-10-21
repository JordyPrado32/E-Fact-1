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
    protected int ProductoOperacion { get; set; }
    protected decimal CantidadOperacion { get; set; }
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
        Transferencia.Detalles = [new() { CodigoProducto = ProductoOperacion, Cantidad = CantidadOperacion }];
        await EjecutarAsync($"api/e-contax/inventarios/transferencias?userId={_usuarioId}", Transferencia);
    }

    protected async Task DevolverAsync()
    {
        Devolucion.Detalles = [new() { CodigoProducto = ProductoOperacion, Cantidad = CantidadOperacion }];
        await EjecutarAsync($"api/e-contax/inventarios/devoluciones-compra?userId={_usuarioId}", Devolucion);
    }

    protected async Task AplicarTomaAsync() => await EjecutarAsync($"api/e-contax/inventarios/tomas-fisicas?userId={_usuarioId}", new EContaxInventarioTomaFisicaDto { IdSucursal = SucursalToma, Observacion = ObservacionToma, Detalles = LineasToma.Select(x => new EContaxInventarioTomaFisicaDetalleDto { CodigoProducto = x.CodigoProducto, StockContado = x.StockContado }).ToList() });

    protected async Task ReservarAsync() => await CambiarReservaAsync("reservas");
    protected async Task LiberarReservaAsync() => await CambiarReservaAsync("reservas/liberar");

    private async Task CambiarReservaAsync(string ruta)
    {
        await EjecutarAsync($"api/e-contax/inventarios/{ruta}?userId={_usuarioId}", new EContaxInventarioReservaDto { CodigoProducto = ProductoOperacion, IdSucursal = SucursalToma, Cantidad = CantidadOperacion });
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

    protected sealed record LineaToma(int CodigoProducto, string Nombre, decimal StockSistema)
    {
        public decimal StockContado { get; set; } = StockSistema;
    }
}
