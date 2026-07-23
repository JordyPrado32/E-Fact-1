using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using Simetric.Auth;
using Simetric.Components;
using Simetric.Data;
using Simetric.Modules.AsistenteIAFacturacion.Config;
using Simetric.Modules.AsistenteIAFacturacion.Services;
using Simetric.Modules.AsistenteIAFacturacion.State;
using Simetric.Modules.AsistenteIAFacturacion.Tools;
using Simetric.Services;
using Simetric.Services.EContax;
using Simetric.Services.EDeclara;
using Simetric.Services.ESign;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);
QuestPDF.Settings.License = LicenseType.Community;
var culturaEspanol = CultureInfo.GetCultureInfo("es-EC");
CultureInfo.DefaultThreadCurrentCulture = culturaEspanol;
CultureInfo.DefaultThreadCurrentUICulture = culturaEspanol;

builder.Configuration.AddUserSecrets<Program>(optional: true);

// UI
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => {
        options.DetailedErrors = true;
    });

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(culturaEspanol);
    options.SupportedCultures = [culturaEspanol];
    options.SupportedUICultures = [culturaEspanol];
    options.RequestCultureProviders.Clear();
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

builder.Services.Configure<OpenAISettings>(builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<WhatsAppCloudApiOptions>(builder.Configuration.GetSection("WhatsAppCloudApi"));

builder.Services.AddSingleton<SqlAuditService>();
builder.Services.AddSingleton<AuditService>();
builder.Services.AddSingleton<CurrentUserContext>();
builder.Services.AddSingleton<AuditActorResolver>();
builder.Services.AddSingleton<SqlAuditSaveChangesInterceptor>();
builder.Services.AddScoped<EfactSharedDataService>();
builder.Services.AddScoped<EContaxSharedDataService>();
builder.Services.AddScoped<EContaxTenantService>();
builder.Services.AddScoped<EContaxOrganizacionService>();
builder.Services.AddScoped<EContaxCatalogService>();
builder.Services.AddScoped<EDeclaraSharedDataService>();
builder.Services.AddScoped<ContribuyenteEdeclaraService>();

// AUTENTICACION / AUTORIZACION
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services.AddScoped<CustomAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<CustomAuthStateProvider>());
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.Name = "Simetric.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Cookie.IsEssential = true;
});
builder.Services.AddDataProtection()
    .SetApplicationName("Simetric")
    .PersistKeysToFileSystem(new DirectoryInfo(
        Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys")));

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "Auth_Session";
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        // La sesion debe cerrarse tras 30 minutos sin actividad.
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;

        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            }
        };
    });

// Base de datos
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("No se encontró la cadena de conexión 'DefaultConnection'.");
}

builder.Services.AddDbContextFactory<AppDbContext>((sp, options) =>
    options
        .UseSqlServer(connectionString, sqlOptions =>
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 2,
                maxRetryDelay: TimeSpan.FromSeconds(3),
                errorNumbersToAdd: null))
        .AddInterceptors(sp.GetRequiredService<SqlAuditSaveChangesInterceptor>()),
    ServiceLifetime.Singleton);

// REGISTRO DE HTTPCLIENT (Solución integrada para ModSecurity)
// HttpContextAccessor y fábrica centralizada de HttpClient
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped(sp =>
{
    var configuredBaseUrl = builder.Configuration["AppBaseUrl"];
    HttpClient httpClient;

    var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
    var request = httpContextAccessor.HttpContext?.Request;
    if (request is not null && request.Host.HasValue)
    {
        httpClient = new HttpClient
        {
            BaseAddress = new Uri($"{request.Scheme}://{request.Host}/")
        };
    }
    else if (Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var configuredBaseUri))
    {
        httpClient = new HttpClient
        {
            BaseAddress = configuredBaseUri
        };
    }
    else
    {
        httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://efact.numericasoftware.com/")
        };
    }

    // =========================================================================
    // SOLUCIÓN PARA MODSECURITY: Forzar la cabecera User-Agent en toda petición
    // =========================================================================
    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SimetricEfact/1.0 (BlazorServer; SRI-Ecuador)");

    return httpClient;
});

builder.Services.AddScoped<ICajaSerieResolver, CajaSerieResolver>();
builder.Services.AddScoped<FacturacionService>();
builder.Services.AddScoped<CompraDocumentosFacturacionService>();
builder.Services.AddScoped<EmisorSistemaService>();
builder.Services.AddScoped<FacturaStoredProcedureBootstrapService>();
builder.Services.AddScoped<GuiaRemisionService>();
builder.Services.AddScoped<IGuiaRemisionPdfService, GuiaRemisionPdfService>();
builder.Services.AddScoped<SecurityService>();
builder.Services.AddScoped<IMenuService, MenuService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IFacturaPdfService, FacturaPdfService>();
builder.Services.AddScoped<ILiquidacionCompraPdfService, LiquidacionCompraPdfService>();
builder.Services.AddScoped<INotaCreditoPdfService, NotaCreditoPdfService>();
builder.Services.AddScoped<INotaDebitoPdfService, NotaDebitoPdfService>();
builder.Services.AddScoped<IRetencionPdfService, RetencionPdfService>();
builder.Services.AddScoped<TaxService>();
builder.Services.AddScoped<ProductCategoryService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<TipoClienteService>();
builder.Services.AddScoped<IdentificacionService>();
builder.Services.AddScoped<ConfiguracionService>();
builder.Services.AddScoped<ClienteService>();
builder.Services.AddScoped<VendedorBackOfficeService>();
builder.Services.AddScoped<PagoService>();
builder.Services.AddScoped<IUsuarioService, UsuarioService>();
builder.Services.AddScoped<RetencionesService>();
builder.Services.AddScoped<RetencionGeneradaService>();
builder.Services.AddScoped<IProveedorService, ProveedorService>();
builder.Services.AddScoped<ComprobanteRetencionGenerator>();
builder.Services.AddScoped<NotaCreditoService>();
builder.Services.AddScoped<ComprasXmlService>();
builder.Services.AddScoped<AbonoService>();
builder.Services.AddScoped<LiquidacionCompraService>();
builder.Services.AddScoped<LiquidacionCompraXmlGenerator>();
builder.Services.AddScoped<RetencionCorreoService>();
builder.Services.AddScoped<NotaDebitoService>();
builder.Services.AddScoped<ReporteComprobantesService>();
builder.Services.AddScoped<IReporteComprobantesPdfService, ReporteComprobantesPdfService>();
builder.Services.AddScoped<IReporteComprobantesExcelService, ReporteComprobantesExcelService>();
builder.Services.AddScoped<IEstadoCuentaExcelService, EstadoCuentaExcelService>();
builder.Services.AddScoped<IEstadoCuentaPdfService, EstadoCuentaPdfService>();
builder.Services.AddScoped<IFacturasExcelService, FacturasExcelService>();
builder.Services.AddScoped<ISimpleExcelExportService, SimpleExcelExportService>();
builder.Services.AddScoped<InitialSequencePromptService>();
builder.Services.AddSingleton<TutorialRegistryService>();
builder.Services.AddScoped<TutorialStateService>();
builder.Services.AddScoped<EmisorOnboardingService>();
builder.Services.AddScoped<EmisionControlService>();
builder.Services.AddScoped<EmisorCertificadoProtector>();
builder.Services.AddScoped<EmisorCertificadoValidator>();
builder.Services.AddScoped<FirmaRenovacionService>();
builder.Services.AddHttpClient<FirmaInfoApiService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddScoped<SolicitudService>();
builder.Services.AddScoped<UbicacionEcuadorCatalogService>();
builder.Services.AddScoped<SweetAlertService>();
builder.Services.AddScoped<ComprobanteCorreoEstadoService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<DemoCatalogService>();
builder.Services.AddSingleton<PendingLoginFlowService>();
builder.Services.AddScoped<MenuStateService>();
builder.Services.AddScoped<AppAccessService>();
builder.Services.AddScoped<SqlPerformanceBootstrapService>();
builder.Services.AddScoped<SelectedAppServiceStateService>();
builder.Services.AddScoped<LoginDestinationService>();
builder.Services.AddScoped<EfactSharedDataService>();
builder.Services.AddScoped<EContaxSharedDataService>();
builder.Services.AddScoped<EContaxAdministracionService>();
builder.Services.AddScoped<EContaxFacturacionService>();
builder.Services.AddScoped<EContaxTenantService>();
builder.Services.AddScoped<EContaxOrganizacionService>();
builder.Services.AddScoped<EContaxCatalogService>();
builder.Services.AddScoped<EContaxOrganizacionBootstrapService>();
builder.Services.AddScoped<EDeclaraSharedDataService>();
builder.Services.AddScoped<IEDeclaraMenuService, EDeclaraMenuService>();
builder.Services.AddScoped<IESignMenuService, ESignMenuService>();
builder.Services.AddScoped<BesPrecompraService>();
builder.Services.AddHttpClient<CedulaLookupService>(client =>
{
    client.BaseAddress = new Uri("http://nessoftfact-001-site6.atempurl.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient<SriXmlProcessorService>(client =>
{
    client.BaseAddress = new Uri("http://localhost:8090/");
    client.Timeout = TimeSpan.FromSeconds(90);
});
builder.Services.AddHttpClient<WhatsAppSupportService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddSingleton<IFacturaConversationStore, InMemoryFacturaConversationStore>();
builder.Services.AddScoped<Simetric.Modules.AsistenteIAFacturacion.Services.IClienteService, SystemClienteServiceAdapter>();
builder.Services.AddScoped<IProductoService, SystemProductoServiceAdapter>();
builder.Services.AddScoped<IFacturacionService, SystemFacturacionServiceAdapter>();
builder.Services.AddScoped<FacturacionTools>();
builder.Services.AddScoped<ToolDispatcher>();
builder.Services.AddHttpClient<IOpenAIAsistenteService, OpenAIAsistenteService>();
builder.Services.AddScoped<IAsistenteFacturacionService, AsistenteFacturacionService>();

builder.Services.AddHostedService<ComprobanteCorreoDispatcherService>();
builder.Services.AddHostedService<FacturaSriReintentoDispatcherService>();
if (builder.Configuration.GetValue<bool>("FirmaRenovacion:NotificacionesCorreoHabilitadas"))
{
    builder.Services.AddHostedService<FirmaRenovacionNotificationService>();
}

var app = builder.Build();

try
{
    await using var scope = app.Services.CreateAsyncScope();
    var initialSequencePromptService = scope.ServiceProvider.GetRequiredService<InitialSequencePromptService>();
    await initialSequencePromptService.EnsureSchemaAsync();

    var comprobanteCorreoEstadoService = scope.ServiceProvider.GetRequiredService<ComprobanteCorreoEstadoService>();
    await comprobanteCorreoEstadoService.EnsureSchemaAsync();

    var ubicacionEcuadorCatalogService = scope.ServiceProvider.GetRequiredService<UbicacionEcuadorCatalogService>();
    await ubicacionEcuadorCatalogService.EnsureCatalogoAsync();

    var appAccessService = scope.ServiceProvider.GetRequiredService<AppAccessService>();
    await appAccessService.EnsureSchemaAsync();

    var emisorSistemaService = scope.ServiceProvider.GetRequiredService<EmisorSistemaService>();
    await emisorSistemaService.EnsureSchemaAsync();

    var sqlPerformanceBootstrapService = scope.ServiceProvider.GetRequiredService<SqlPerformanceBootstrapService>();
    await sqlPerformanceBootstrapService.EnsureSchemaAsync();

    var facturaStoredProcedureBootstrapService = scope.ServiceProvider.GetRequiredService<FacturaStoredProcedureBootstrapService>();
    await facturaStoredProcedureBootstrapService.EnsureSchemaAsync();

    var edeclaraMenuService = scope.ServiceProvider.GetRequiredService<IEDeclaraMenuService>();
    await edeclaraMenuService.EnsureSchemaAsync();

    var esignMenuService = scope.ServiceProvider.GetRequiredService<IESignMenuService>();
    await esignMenuService.EnsureSchemaAsync();

    var econtaxOrganizacionBootstrapService = scope.ServiceProvider.GetRequiredService<EContaxOrganizacionBootstrapService>();
    await econtaxOrganizacionBootstrapService.EnsureSchemaAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"No se pudo asegurar la inicialización de catálogos: {ex.Message}");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost,
    KnownNetworks = { },
    KnownProxies = { }
});

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRequestLocalization();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
