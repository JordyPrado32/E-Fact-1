using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;
using Simetric.Services.EContax;
using Simetric.Services.EDeclara;

namespace Simetric.Services;

public sealed class AppAccessService
{
    public const string AdminRoleId = "2";
    public const string FreeServiceKey = "e-fact";
    public const string EContaxServiceKey = EContaxRoutes.ServiceKey;
    public const string EContaxRoute = EContaxRoutes.Root;
    public const string EDeclaraServiceKey = EDeclaraRoutes.ServiceKey;
    public const string EDeclaraRoute = EDeclaraRoutes.Root;

    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaEnsured;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly PagoService _pagoService;

    public AppAccessService(IDbContextFactory<AppDbContext> dbFactory, PagoService pagoService)
    {
        _dbFactory = dbFactory;
        _pagoService = pagoService;
    }

    public async Task EnsureSchemaAsync()
    {
        if (_schemaEnsured)
            return;

        await SchemaLock.WaitAsync();
        try
        {
            if (_schemaEnsured)
                return;

            await using var context = await _dbFactory.CreateDbContextAsync();
            foreach (var statement in BuildEnsureSchemaStatements())
            {
                await context.Database.ExecuteSqlRawAsync(statement);
            }

            _schemaEnsured = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    public async Task<List<AppServiceCatalogItem>> GetCatalogForUserAsync(int userId, bool isSuperAdmin)
    {
        await EnsureSchemaAsync();

        await using var context = await _dbFactory.CreateDbContextAsync();
        var services = await context.AppServicios
            .AsNoTracking()
            .Where(x => x.Estado)
            .OrderBy(x => x.OrdenVisual)
            .ThenBy(x => x.Nombre)
            .ToListAsync();

        var catalog = new List<AppServiceCatalogItem>(services.Count);
        foreach (var service in services)
        {
            var access = await EvaluateAccessAsync(context, userId, service, isSuperAdmin);
            catalog.Add(new AppServiceCatalogItem
            {
                ServicioId = service.ServicioId,
                Clave = service.Clave,
                Nombre = service.Nombre,
                Descripcion = service.Descripcion,
                RutaAcceso = ResolveRoute(service),
                RequiereSuscripcion = service.RequiereSuscripcion,
                TieneAcceso = access.HasAccess,
                EsSuperAdministrador = access.IsSuperAdmin,
                TieneSuscripcionActiva = access.HasActiveSubscription,
                FechaFinSuscripcion = access.SubscriptionEndDate,
                Icono = service.Icono,
                ColorHex = string.IsNullOrWhiteSpace(service.ColorHex) ? "#0d6efd" : service.ColorHex,
                EstadoAcceso = access.StatusText,
                MotivoBloqueo = access.DenialReason
            });
        }

        return catalog;
    }

    public async Task<AppServiceAccessDecision> CanAccessAsync(int userId, string? serviceKey, bool isSuperAdmin)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(serviceKey))
        {
            return new AppServiceAccessDecision
            {
                HasAccess = false,
                DenialReason = "No se pudo identificar el servicio solicitado.",
                StatusText = "Servicio no valido"
            };
        }

        await EnsureSchemaAsync();

        await using var context = await _dbFactory.CreateDbContextAsync();
        var normalizedKey = serviceKey.Trim().ToLowerInvariant();
        var service = await context.AppServicios
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Estado && x.Clave == normalizedKey);

        if (service is null)
        {
            return new AppServiceAccessDecision
            {
                HasAccess = false,
                DenialReason = "El servicio solicitado no existe o no esta disponible.",
                StatusText = "No disponible"
            };
        }

        var access = await EvaluateAccessAsync(context, userId, service, isSuperAdmin);
        access.Route = ResolveRoute(service);
        return access;
    }

    public async Task<List<AppServiceSubscriptionStatus>> GetSubscriptionStatusesAsync(int userId)
    {
        await EnsureSchemaAsync();

        await using var context = await _dbFactory.CreateDbContextAsync();
        var services = await context.AppServicios
            .AsNoTracking()
            .Where(x => x.Estado && x.RequiereSuscripcion && x.Clave != FreeServiceKey)
            .OrderBy(x => x.OrdenVisual)
            .ThenBy(x => x.Nombre)
            .ToListAsync();

        var today = DateTime.Today;
        var statuses = new List<AppServiceSubscriptionStatus>(services.Count);

        foreach (var service in services)
        {
            var subscription = await context.UsuarioServicioSuscripciones
                .AsNoTracking()
                .Where(x => x.IdUsuario == userId && x.ServicioId == service.ServicioId)
                .OrderByDescending(x => x.FechaActualizacion)
                .ThenByDescending(x => x.SuscripcionId)
                .FirstOrDefaultAsync();

            var status = BuildSubscriptionStatus(service, subscription, today);
            statuses.Add(status);
        }

        return statuses;
    }

    public async Task EnsurePendingSubscriptionAsync(int userId, string serviceKey)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(serviceKey))
            return;

        await EnsureSchemaAsync();

        await using var context = await _dbFactory.CreateDbContextAsync();
        var normalizedKey = serviceKey.Trim().ToLowerInvariant();
        var service = await context.AppServicios
            .FirstOrDefaultAsync(x => x.Estado && x.Clave == normalizedKey);

        if (service is null || !service.RequiereSuscripcion || service.Clave == FreeServiceKey)
            return;

        var existing = await context.UsuarioServicioSuscripciones
            .FirstOrDefaultAsync(x => x.IdUsuario == userId && x.ServicioId == service.ServicioId);

        if (existing is null)
        {
            context.UsuarioServicioSuscripciones.Add(new UsuarioServicioSuscripcion
            {
                IdUsuario = userId,
                ServicioId = service.ServicioId,
                Estado = "PENDIENTE_PAGO",
                FechaCreacion = DateTime.Now,
                FechaActualizacion = DateTime.Now,
                Observacion = $"Suscripcion pendiente creada para {service.Nombre}."
            });
            await context.SaveChangesAsync();
            return;
        }

        if (!string.Equals(existing.Estado, "ACTIVA", StringComparison.OrdinalIgnoreCase))
        {
            existing.Estado = "PENDIENTE_PAGO";
            existing.FechaActualizacion = DateTime.Now;
            existing.Observacion = $"Suscripcion pendiente actualizada para {service.Nombre}.";
            await context.SaveChangesAsync();
        }
    }

    public async Task EnsurePendingSubscriptionBundleAsync(int userId)
    {
        var paidServices = await GetPaidServicesAsync();
        foreach (var service in paidServices)
        {
            await EnsurePendingSubscriptionAsync(userId, service.Clave);
        }
    }

    public async Task<List<AppServicio>> GetPaidServicesAsync()
    {
        await EnsureSchemaAsync();

        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.AppServicios
            .AsNoTracking()
            .Where(x => x.Estado && x.RequiereSuscripcion && x.Clave != FreeServiceKey)
            .OrderBy(x => x.OrdenVisual)
            .ThenBy(x => x.Nombre)
            .ToListAsync();
    }

    public static string ResolveRoute(AppServicio service) =>
        string.Equals(service.Clave, FreeServiceKey, StringComparison.OrdinalIgnoreCase)
            ? "/dashboard"
            : string.IsNullOrWhiteSpace(service.RutaAcceso)
                ? $"/servicios/{service.Clave}"
                : service.RutaAcceso;

    private async Task<AppServiceAccessDecision> EvaluateAccessAsync(
        AppDbContext context,
        int userId,
        AppServicio service,
        bool isSuperAdmin)
    {
        if (string.Equals(service.Clave, "backoffice", StringComparison.OrdinalIgnoreCase))
        {
            var userObj = await context.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.IdUsuario == userId);
            if (isSuperAdmin || userObj?.IdTipoUsuario == 7)
            {
                return new AppServiceAccessDecision
                {
                    HasAccess = true,
                    IsSuperAdmin = isSuperAdmin,
                    StatusText = isSuperAdmin ? "Acceso por rol administrador" : "Acceso Backoffice"
                };
            }
            return new AppServiceAccessDecision
            {
                HasAccess = false,
                DenialReason = "Acceso restringido a personal de Backoffice.",
                StatusText = "No autorizado"
            };
        }

        if (!service.RequiereSuscripcion ||
            string.Equals(service.Clave, FreeServiceKey, StringComparison.OrdinalIgnoreCase))
        {
            return new AppServiceAccessDecision
            {
                HasAccess = true,
                IsFreeAccess = true,
                StatusText = "Acceso libre"
            };
        }

        if (isSuperAdmin)
        {
            return new AppServiceAccessDecision
            {
                HasAccess = true,
                IsSuperAdmin = true,
                StatusText = "Acceso por rol administrador"
            };
        }

        var today = DateTime.Today;
        // 1. Buscamos primero la suscripción estrictamente futura (que no vence hoy)
        var activeSubscription = await context.UsuarioServicioSuscripciones
            .AsNoTracking()
            .Where(x => x.IdUsuario == userId && x.ServicioId == service.ServicioId)
            .OrderByDescending(x => x.FechaFin)
            .ThenByDescending(x => x.FechaActualizacion)
            .ThenByDescending(x => x.SuscripcionId)
            .FirstOrDefaultAsync(x =>
                x.Estado == "ACTIVA" &&
                (x.EsVitalicia || !x.FechaFin.HasValue || x.FechaFin.Value.Date > today) &&
                (!x.FechaInicio.HasValue || x.FechaInicio.Value.Date <= today));

        if (activeSubscription is not null)
        {
            var status = activeSubscription.EsVitalicia
                ? "Suscripcion vitalicia activa"
                : activeSubscription.FechaFin.HasValue
                    ? $"Activa hasta {activeSubscription.FechaFin.Value:dd/MM/yyyy}"
                    : "Suscripcion activa";

            return new AppServiceAccessDecision
            {
                HasAccess = true,
                HasActiveSubscription = true,
                SubscriptionEndDate = activeSubscription.FechaFin,
                StatusText = status
            };
        }

        // 2. Si no hay suscripción estrictamente futura, pero hay una que vence hoy o ya venció, intentar cobro automático en E-Declara
        if (string.Equals(service.Clave, EDeclaraServiceKey, StringComparison.OrdinalIgnoreCase))
        {
            var subscription = await context.UsuarioServicioSuscripciones
                .FirstOrDefaultAsync(x => x.IdUsuario == userId && x.ServicioId == service.ServicioId);

            if (subscription is not null)
            {
                bool isExpiredOrExpiringToday = string.Equals(subscription.Estado, "ACTIVA", StringComparison.OrdinalIgnoreCase) &&
                                                subscription.FechaFin.HasValue &&
                                                subscription.FechaFin.Value.Date <= today;

                bool isPending = string.Equals(subscription.Estado, "PENDIENTE_PAGO", StringComparison.OrdinalIgnoreCase);

                if (isExpiredOrExpiringToday || isPending)
                {
                    var card = await context.EdeclareTarjetas
                        .FirstOrDefaultAsync(x => x.IdUsuario == userId && x.Estado);

                    if (card is not null && !string.IsNullOrWhiteSpace(card.Token))
                    {
                        var planToCharge = subscription.PlanAgendado ?? subscription.PlanActual ?? "individual";
                        var cicloToCharge = subscription.CicloAgendado ?? subscription.CicloActual ?? "mensual";

                        decimal subtotal = 10.00m;
                        if (string.Equals(planToCharge, "individual", StringComparison.OrdinalIgnoreCase))
                        {
                            subtotal = string.Equals(cicloToCharge, "anual", StringComparison.OrdinalIgnoreCase) ? 80.00m : 10.00m;
                        }
                        else if (string.Equals(planToCharge, "asesor", StringComparison.OrdinalIgnoreCase))
                        {
                            subtotal = string.Equals(cicloToCharge, "anual", StringComparison.OrdinalIgnoreCase) ? 200.00m : 25.00m;
                        }
                        else if (string.Equals(planToCharge, "empresarial", StringComparison.OrdinalIgnoreCase))
                        {
                            subtotal = string.Equals(cicloToCharge, "anual", StringComparison.OrdinalIgnoreCase) ? 500.00m : 50.00m;
                        }
                        decimal iva = Math.Round(subtotal * 0.15m, 2);
                        decimal total = subtotal + iva;

                        var userObj = await context.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == userId);
                        if (userObj is not null)
                        {
                            var documentSanitized = new string((userObj.Identificacion ?? "").Where(char.IsLetterOrDigit).ToArray());
                            var reference = $"SUB{userId}{DateTime.Now:yyyyMMddHHmmss}";
                            var request = new
                            {
                                integration = true,
                                document = documentSanitized,
                                token = card.Token,
                                reference = reference,
                                description = $"Debito automatico E-Declara {planToCharge}",
                                amount = total,
                                amount_with_tax = subtotal,
                                amount_without_tax = 0.00m,
                                tax_value = iva,
                                generate_invoice = 0
                            };

                            var chargeResult = await _pagoService.CobrarTarjetaTokenizada(request);
                            if (chargeResult.IsSuccess)
                            {
                                subscription.Estado = "ACTIVA";
                                subscription.FechaInicio = DateTime.Today;
                                subscription.FechaFin = string.Equals(cicloToCharge, "anual", StringComparison.OrdinalIgnoreCase)
                                    ? DateTime.Today.AddYears(1)
                                    : DateTime.Today.AddMonths(1);
                                subscription.PlanActual = planToCharge;
                                subscription.CicloActual = cicloToCharge;
                                subscription.PlanAgendado = null;
                                subscription.CicloAgendado = null;
                                subscription.FechaActualizacion = DateTime.Now;
                                subscription.Observacion = $"Debito recurrente automatico exitoso. Ref: {reference}. Tarjeta: {card.MarcaTarjeta} {card.NumeroMascara}. Plan: {planToCharge} ({cicloToCharge})";

                                await context.SaveChangesAsync();

                                return new AppServiceAccessDecision
                                {
                                    HasAccess = true,
                                    HasActiveSubscription = true,
                                    SubscriptionEndDate = subscription.FechaFin,
                                    StatusText = $"Renovada automáticamente hasta {subscription.FechaFin.Value:dd/MM/yyyy}"
                                };
                            }
                            else
                            {
                                subscription.Estado = "PENDIENTE_PAGO";
                                subscription.FechaActualizacion = DateTime.Now;
                                subscription.Observacion = $"Intento de cobro automatico fallido. Ref: {reference}. Error: {chargeResult.ErrorMessage}.";
                                await context.SaveChangesAsync();
                            }
                        }
                    }
                    else
                    {
                        // No hay tarjetas registradas
                        if (isExpiredOrExpiringToday)
                        {
                            subscription.Estado = "PENDIENTE_PAGO";
                            subscription.FechaActualizacion = DateTime.Now;
                            subscription.Observacion = "Suscripción expirada. No se pudo renovar automáticamente porque no hay tarjetas registradas.";
                            await context.SaveChangesAsync();
                        }
                    }
                }
            }
        }

        // 3. Fallback de cortesía para otros servicios: permitir acceso hasta el final del día de vencimiento
        var todayAccessSubscription = await context.UsuarioServicioSuscripciones
            .AsNoTracking()
            .Where(x => x.IdUsuario == userId && x.ServicioId == service.ServicioId)
            .OrderByDescending(x => x.FechaFin)
            .FirstOrDefaultAsync(x =>
                x.Estado == "ACTIVA" &&
                (!x.FechaFin.HasValue || x.FechaFin.Value.Date >= today) &&
                (!x.FechaInicio.HasValue || x.FechaInicio.Value.Date <= today));

        if (todayAccessSubscription is not null)
        {
            return new AppServiceAccessDecision
            {
                HasAccess = true,
                HasActiveSubscription = true,
                SubscriptionEndDate = todayAccessSubscription.FechaFin,
                StatusText = todayAccessSubscription.EsVitalicia
                    ? "Suscripcion vitalicia activa"
                    : todayAccessSubscription.FechaFin.HasValue
                        ? $"Activa hasta {todayAccessSubscription.FechaFin.Value:dd/MM/yyyy}"
                        : "Suscripcion activa"
            };
        }

        return new AppServiceAccessDecision
        {
            HasAccess = false,
            RequiresSubscription = true,
            DenialReason = "Necesitas una suscripcion activa para ingresar a este servicio.",
            StatusText = "Suscripcion requerida"
        };
    }

    // ── ÚNICO método BuildEnsureSchemaStatements, completo y correcto ──────────
    private static IEnumerable<string> BuildEnsureSchemaStatements()
    {
        yield return """
IF OBJECT_ID(N'[dbo].[APP_SERVICIOS]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[APP_SERVICIOS]
    (
        [ServicioId]           INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Clave]                NVARCHAR(50)  NOT NULL,
        [Nombre]               NVARCHAR(100) NOT NULL,
        [Descripcion]          NVARCHAR(250) NULL,
        [RutaAcceso]           NVARCHAR(150) NULL,
        [RequiereSuscripcion]  BIT NOT NULL CONSTRAINT [DF_APP_SERVICIOS_RequiereSuscripcion] DEFAULT (1),
        [Estado]               BIT NOT NULL CONSTRAINT [DF_APP_SERVICIOS_Estado]              DEFAULT (1),
        [OrdenVisual]          INT NOT NULL CONSTRAINT [DF_APP_SERVICIOS_OrdenVisual]         DEFAULT (0),
        [Icono]                NVARCHAR(50)  NULL,
        [ColorHex]             NVARCHAR(20)  NULL,
        [FechaCreacion]        DATETIME2 NOT NULL CONSTRAINT [DF_APP_SERVICIOS_FechaCreacion] DEFAULT (SYSUTCDATETIME())
    );
END;
""";

        yield return """
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[APP_SERVICIOS]')
      AND [name] = N'UX_APP_SERVICIOS_Clave')
BEGIN
    CREATE UNIQUE INDEX [UX_APP_SERVICIOS_Clave] ON [dbo].[APP_SERVICIOS]([Clave]);
END;
""";

        yield return """
IF OBJECT_ID(N'[dbo].[USUARIO_SERVICIO_SUSCRIPCION]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[USUARIO_SERVICIO_SUSCRIPCION]
    (
        [SuscripcionId]      INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [IdUsuario]          INT NOT NULL,
        [ServicioId]         INT NOT NULL,
        [Estado]             NVARCHAR(20) NOT NULL CONSTRAINT [DF_USUARIO_SERVICIO_SUSCRIPCION_Estado]        DEFAULT (N'PENDIENTE_PAGO'),
        [FechaInicio]        DATE NULL,
        [FechaFin]           DATE NULL,
        [EsVitalicia]        BIT NOT NULL CONSTRAINT [DF_USUARIO_SERVICIO_SUSCRIPCION_EsVitalicia]            DEFAULT (0),
        [Observacion]        NVARCHAR(250) NULL,
        [FechaCreacion]      DATETIME2 NOT NULL CONSTRAINT [DF_USUARIO_SERVICIO_SUSCRIPCION_FechaCreacion]    DEFAULT (SYSUTCDATETIME()),
        [FechaActualizacion] DATETIME2 NULL,
        CONSTRAINT [FK_USUARIO_SERVICIO_SUSCRIPCION_USUARIOS]
            FOREIGN KEY ([IdUsuario])  REFERENCES [dbo].[Usuarios]([IdUsuario]),
        CONSTRAINT [FK_USUARIO_SERVICIO_SUSCRIPCION_APP_SERVICIOS]
            FOREIGN KEY ([ServicioId]) REFERENCES [dbo].[APP_SERVICIOS]([ServicioId])
    );
END;
""";

        yield return """
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[dbo].[USUARIO_SERVICIO_SUSCRIPCION]')
      AND [name] = N'IX_USUARIO_SERVICIO_SUSCRIPCION_USUARIO_SERVICIO')
BEGIN
    CREATE INDEX [IX_USUARIO_SERVICIO_SUSCRIPCION_USUARIO_SERVICIO]
        ON [dbo].[USUARIO_SERVICIO_SUSCRIPCION]([IdUsuario], [ServicioId], [Estado], [FechaFin]);
END;
""";

        yield return """
IF OBJECT_ID(N'[dbo].[EDECLARE_TARJETAS]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EDECLARE_TARJETAS]
    (
        [ID_TARJETA]       INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [ID_USUARIO]       INT NOT NULL,
        [DOCUMENTO]        NVARCHAR(20) NOT NULL,
        [TOKEN]            NVARCHAR(100) NOT NULL,
        [MARCA_TARJETA]    NVARCHAR(50) NULL,
        [NUMERO_MASCARA]   NVARCHAR(50) NULL,
        [FECHA_REGISTRO]   DATETIME2 NOT NULL CONSTRAINT [DF_EDECLARE_TARJETAS_FechaRegistro] DEFAULT (SYSUTCDATETIME()),
        [ESTADO]           BIT NOT NULL CONSTRAINT [DF_EDECLARE_TARJETAS_Estado] DEFAULT (1),
        CONSTRAINT [FK_EDECLARE_TARJETAS_USUARIOS]
            FOREIGN KEY ([ID_USUARIO]) REFERENCES [dbo].[Usuarios]([IdUsuario])
    );
END;
""";

        yield return """
IF OBJECT_ID(N'[dbo].[USUARIO_SERVICIO_SUSCRIPCION]', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[USUARIO_SERVICIO_SUSCRIPCION]') AND name = N'PlanActual')
    BEGIN
        ALTER TABLE [dbo].[USUARIO_SERVICIO_SUSCRIPCION] ADD [PlanActual] NVARCHAR(50) NULL;
    END;
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[USUARIO_SERVICIO_SUSCRIPCION]') AND name = N'CicloActual')
    BEGIN
        ALTER TABLE [dbo].[USUARIO_SERVICIO_SUSCRIPCION] ADD [CicloActual] NVARCHAR(20) NULL;
    END;
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[USUARIO_SERVICIO_SUSCRIPCION]') AND name = N'PlanAgendado')
    BEGIN
        ALTER TABLE [dbo].[USUARIO_SERVICIO_SUSCRIPCION] ADD [PlanAgendado] NVARCHAR(50) NULL;
    END;
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[USUARIO_SERVICIO_SUSCRIPCION]') AND name = N'CicloAgendado')
    BEGIN
        ALTER TABLE [dbo].[USUARIO_SERVICIO_SUSCRIPCION] ADD [CicloAgendado] NVARCHAR(20) NULL;
    END;
END;
""";

        // ── Rutas corregidas: e-declara → /e-declara  |  e-conta → /e-contax ──
        yield return """
MERGE [dbo].[APP_SERVICIOS] AS target
USING (VALUES
    (N'e-fact',     N'E-FACT',     N'Facturacion electronica con acceso libre para todos los usuarios registrados.', N'/dashboard',        CAST(0 AS bit), CAST(1 AS bit), 1, N'ri-file-chart-line',  N'#0d6efd'),
    (N'e-conta',    N'E-CONTAX',   N'Contabilidad y control financiero habilitado por suscripcion.',                 N'/e-contax',         CAST(1 AS bit), CAST(1 AS bit), 3, N'ri-line-chart-line',  N'#00A896'),
    (N'e-declara',  N'E-DECLARA',  N'Declaraciones tributarias con acceso segun plan activo.',                       N'/e-declara',        CAST(1 AS bit), CAST(1 AS bit), 4, N'ri-government-line',  N'#fd7e14'),
    (N'e-people',   N'E-PEOPLE',   N'Gestion de talento humano y colaboradores bajo suscripcion.',                  N'/servicios/e-people',CAST(1 AS bit), CAST(1 AS bit), 5, N'ri-team-line',        N'#6f42c1'),
    (N'e-sign',     N'eRúbrica',   N'eRúbrica para firma electronica y certificado digital de documentos en linea.', N'/e-sign',            CAST(0 AS bit), CAST(1 AS bit), 2, N'ri-key-2-line',        N'#2E7D32'),
    (N'backoffice', N'BACKOFFICE', N'Acceso exclusivo para administradores y personal de backoffice.',               N'/backoffice',        CAST(0 AS bit), CAST(1 AS bit), 6, N'ri-shield-user-line', N'#0a1c3e')
) AS source ([Clave], [Nombre], [Descripcion], [RutaAcceso], [RequiereSuscripcion], [Estado], [OrdenVisual], [Icono], [ColorHex])
ON target.[Clave] = source.[Clave]
WHEN MATCHED THEN
    UPDATE SET
        [Nombre]              = source.[Nombre],
        [Descripcion]         = source.[Descripcion],
        [RutaAcceso]          = source.[RutaAcceso],
        [RequiereSuscripcion] = source.[RequiereSuscripcion],
        [Estado]              = source.[Estado],
        [OrdenVisual]         = source.[OrdenVisual],
        [Icono]               = source.[Icono],
        [ColorHex]            = source.[ColorHex]
WHEN NOT MATCHED THEN
    INSERT ([Clave], [Nombre], [Descripcion], [RutaAcceso], [RequiereSuscripcion], [Estado], [OrdenVisual], [Icono], [ColorHex])
    VALUES (source.[Clave], source.[Nombre], source.[Descripcion], source.[RutaAcceso], source.[RequiereSuscripcion], source.[Estado], source.[OrdenVisual], source.[Icono], source.[ColorHex]);
""";

        yield return """
IF OBJECT_ID(N'[dbo].[USU_SOLICITUD_FIRMA]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'[dbo].[USU_SOLICITUD_FIRMA]', N'SOL_UANATACA_UUID') IS NULL
    BEGIN
        ALTER TABLE [dbo].[USU_SOLICITUD_FIRMA] ADD
            [SOL_UANATACA_UUID] VARCHAR(50) NULL,
            [SOL_UANATACA_STATUS] VARCHAR(50) NULL,
            [SOL_UANATACA_TOKEN] VARCHAR(50) NULL,
            [SOL_UANATACA_STATUS_TEXT] VARCHAR(100) NULL,
            [SOL_UANATACA_COMMENTS] NVARCHAR(MAX) NULL,
            [SOL_UANATACA_PRODUCT_UUID] VARCHAR(50) NULL,
            [SOL_UANATACA_STAKEHOLDER_UUID] VARCHAR(50) NULL,
            [SOL_UANATACA_CREATED_BY] VARCHAR(100) NULL,
            [SOL_UANATACA_ACTIVE] BIT NULL,
            [SOL_UANATACA_COUNTABLE] BIT NULL,
            [SOL_UANATACA_RENOVATION] BIT NULL,
            [SOL_UANATACA_OFFER_UUID] VARCHAR(50) NULL,
            [SOL_UANATACA_HAS_FRONT_ID] BIT NULL,
            [SOL_UANATACA_HAS_BACK_ID] BIT NULL,
            [SOL_UANATACA_HAS_SELFIE] BIT NULL,
            [SOL_UANATACA_HAS_RUC_FILE] BIT NULL,
            [SOL_UANATACA_HAS_SENIOR_VIDEO] BIT NULL,
            [SOL_UANATACA_HAS_APPOINTMENT] BIT NULL,
            [SOL_UANATACA_HAS_ACCEPTANCE] BIT NULL,
            [SOL_UANATACA_HAS_CONSTITUTION] BIT NULL,
            [SOL_UANATACA_HAS_MANAGER_ID] BIT NULL,
            [SOL_UANATACA_HAS_AUTHORIZATION] BIT NULL,
            [SOL_UANATACA_HAS_ADDITIONAL] BIT NULL;
    END;
END;
""";

        yield return """
IF OBJECT_ID(N'[dbo].[USU_SOLICITUD_FIRMA]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'[dbo].[USU_SOLICITUD_FIRMA]', N'SOL_COMPANY_NAME') IS NULL
    BEGIN
        ALTER TABLE [dbo].[USU_SOLICITUD_FIRMA] ADD
            [SOL_COMPANY_NAME] VARCHAR(150) NULL,
            [SOL_DEPARTMENT] VARCHAR(100) NULL,
            [SOL_POSITION] VARCHAR(100) NULL,
            [SOL_REASON] VARCHAR(250) NULL,
            [SOL_IDENTIFICATION_TYPE_MANAGER] VARCHAR(20) NULL,
            [SOL_IDENTIFICATION_MANAGER] VARCHAR(20) NULL,
            [SOL_NAMES_MANAGER] VARCHAR(100) NULL,
            [SOL_LAST_NAME_MANAGER] VARCHAR(100) NULL;
    END;
END;
""";
    }

    private static AppServiceSubscriptionStatus BuildSubscriptionStatus(
        AppServicio service,
        UsuarioServicioSuscripcion? subscription,
        DateTime today)
    {
        if (subscription is null)
        {
            return new AppServiceSubscriptionStatus
            {
                ServicioId = service.ServicioId,
                Clave = service.Clave,
                Nombre = service.Nombre,
                Descripcion = service.Descripcion,
                Icono = service.Icono,
                ColorHex = string.IsNullOrWhiteSpace(service.ColorHex) ? "#0d6efd" : service.ColorHex,
                Estado = "NO_CONTRATADA",
                EstadoLabel = "Sin suscripcion",
                Detalle = "Todavia no existe una suscripcion creada para este servicio."
            };
        }

        var isActive = string.Equals(subscription.Estado, "ACTIVA", StringComparison.OrdinalIgnoreCase) &&
                       (subscription.EsVitalicia || !subscription.FechaFin.HasValue || subscription.FechaFin.Value.Date >= today) &&
                       (!subscription.FechaInicio.HasValue || subscription.FechaInicio.Value.Date <= today);

        if (isActive)
        {
            var activeDetail = subscription.EsVitalicia
                ? "Acceso vitalicio activo."
                : subscription.FechaFin.HasValue
                    ? $"Activa hasta {subscription.FechaFin.Value:dd/MM/yyyy}."
                    : "Suscripcion activa sin fecha fin registrada.";

            return new AppServiceSubscriptionStatus
            {
                ServicioId = service.ServicioId,
                Clave = service.Clave,
                Nombre = service.Nombre,
                Descripcion = service.Descripcion,
                Icono = service.Icono,
                ColorHex = string.IsNullOrWhiteSpace(service.ColorHex) ? "#0d6efd" : service.ColorHex,
                Estado = "ACTIVA",
                EstadoLabel = "Activa",
                Detalle = activeDetail,
                FechaFin = subscription.FechaFin
            };
        }

        if (string.Equals(subscription.Estado, "PENDIENTE_PAGO", StringComparison.OrdinalIgnoreCase))
        {
            return new AppServiceSubscriptionStatus
            {
                ServicioId = service.ServicioId,
                Clave = service.Clave,
                Nombre = service.Nombre,
                Descripcion = service.Descripcion,
                Icono = service.Icono,
                ColorHex = string.IsNullOrWhiteSpace(service.ColorHex) ? "#0d6efd" : service.ColorHex,
                Estado = "PENDIENTE_PAGO",
                EstadoLabel = "Pendiente de pago",
                Detalle = "La suscripcion ya fue creada y esta esperando confirmacion o checkout."
            };
        }

        return new AppServiceSubscriptionStatus
        {
            ServicioId = service.ServicioId,
            Clave = service.Clave,
            Nombre = service.Nombre,
            Descripcion = service.Descripcion,
            Icono = service.Icono,
            ColorHex = string.IsNullOrWhiteSpace(service.ColorHex) ? "#0d6efd" : service.ColorHex,
            Estado = subscription.Estado,
            EstadoLabel = string.Equals(subscription.Estado, "EXPIRADA", StringComparison.OrdinalIgnoreCase)
                ? "Expirada"
                : "Inactiva",
            Detalle = subscription.FechaFin.HasValue
                ? $"La vigencia termino el {subscription.FechaFin.Value:dd/MM/yyyy}."
                : "La suscripcion no esta activa actualmente.",
            FechaFin = subscription.FechaFin
        };
    }
}

public sealed class AppServiceCatalogItem
{
    public int ServicioId { get; set; }
    public string Clave { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string RutaAcceso { get; set; } = "/dashboard";
    public bool RequiereSuscripcion { get; set; }
    public bool TieneAcceso { get; set; }
    public bool EsSuperAdministrador { get; set; }
    public bool TieneSuscripcionActiva { get; set; }
    public DateTime? FechaFinSuscripcion { get; set; }
    public string? Icono { get; set; }
    public string ColorHex { get; set; } = "#0d6efd";
    public string EstadoAcceso { get; set; } = string.Empty;
    public string? MotivoBloqueo { get; set; }
}

public sealed class AppServiceAccessDecision
{
    public bool HasAccess { get; set; }
    public bool RequiresSubscription { get; set; }
    public bool HasActiveSubscription { get; set; }
    public bool IsSuperAdmin { get; set; }
    public bool IsFreeAccess { get; set; }
    public string? DenialReason { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public DateTime? SubscriptionEndDate { get; set; }
    public string Route { get; set; } = "/dashboard";
}

public sealed class AppServiceSubscriptionStatus
{
    public int ServicioId { get; set; }
    public string Clave { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string? Icono { get; set; }
    public string ColorHex { get; set; } = "#0d6efd";
    public string Estado { get; set; } = "NO_CONTRATADA";
    public string EstadoLabel { get; set; } = "Sin suscripcion";
    public string Detalle { get; set; } = string.Empty;
    public DateTime? FechaFin { get; set; }
}
