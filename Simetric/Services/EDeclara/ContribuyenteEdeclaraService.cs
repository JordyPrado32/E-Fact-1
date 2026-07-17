using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Services;

public class ContribuyenteEdeclaraService
{
    private static readonly System.Threading.SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaEnsured;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ContribuyenteEdeclaraService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
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
            await context.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID('dbo.Contribuyentes_Edeclare', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Contribuyentes_Edeclare] (
        [CODContribuyentes_Edeclare] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Contribuyentes_Edeclare] PRIMARY KEY,
        [APELLIDOS] NVARCHAR(150) NULL,
        [NOMBRES] NVARCHAR(150) NULL,
        [NOMBRECOMERCIAL] NVARCHAR(200) NULL,
        [NOMBRERAZONSOCIAL] NVARCHAR(250) NULL,
        [TIPOIDENTIFICACION] NVARCHAR(10) NOT NULL,
        [NUMEROIDENTIFICACION] NVARCHAR(20) NULL,
        [DIRECCION] NVARCHAR(250) NULL,
        [TELEFONOCONVENCIONAL] NVARCHAR(50) NULL,
        [CELULAR] NVARCHAR(50) NULL,
        [CORREO] NVARCHAR(100) NULL,
        [TIPO_CLIENTE] INT NOT NULL CONSTRAINT [DF_Contribuyentes_Edeclare_TIPO_CLIENTE] DEFAULT(1),
        [OBLGCONTA] NVARCHAR(10) NULL,
        [USUARIO] INT NULL,
        [ESTADO] BIT NULL CONSTRAINT [DF_Contribuyentes_Edeclare_ESTADO] DEFAULT(1),
        [OBSERVACIONES] NVARCHAR(MAX) NULL,
        [PAIS] INT NULL,
        [PROVINCIA] INT NULL,
        [CIUDAD] INT NULL,
        [PERSONANATURAL] BIT NULL,
        [CONTRIBUYENTEESPECIAL] BIT NULL,
        [ACTIVIDADCONTRIBUYENTE] NVARCHAR(MAX) NULL,
        [NUMCONTRIBUYENTE] NVARCHAR(50) NULL,
        [PERIODICIDADIVA] NVARCHAR(50) NULL,
        [PERIODICIDADRENTA] NVARCHAR(100) NULL,
        [FECHADECLARACION] DATE NULL,
        [FECHAINGRESO] DATE NULL
    );
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Contribuyentes_Edeclare') AND name = 'PERIODICIDADIVA')
    BEGIN
        ALTER TABLE dbo.Contribuyentes_Edeclare ADD [PERIODICIDADIVA] NVARCHAR(50) NULL;
    END
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Contribuyentes_Edeclare') AND name = 'PERIODICIDADRENTA')
    BEGIN
        ALTER TABLE dbo.Contribuyentes_Edeclare ADD [PERIODICIDADRENTA] NVARCHAR(100) NULL;
    END
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Contribuyentes_Edeclare') AND name = 'FECHADECLARACION')
    BEGIN
        ALTER TABLE dbo.Contribuyentes_Edeclare ADD [FECHADECLARACION] DATE NULL;
    END
END");

            _schemaEnsured = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    public async Task<List<ContribuyenteEdeclare>> ObtenerContribuyentesAsync(int userId)
    {
        await EnsureSchemaAsync();
        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.ContribuyentesEdeclare
            .Where(x => x.Usuario == userId)
            .Include(x => x.CiudadNavegacion!)
                .ThenInclude(c => c.Provincia!)
                    .ThenInclude(p => p.Pais)
            .OrderBy(x => x.Apellidos)
            .ThenBy(x => x.Nombres)
            .ToListAsync();
    }

    public async Task<ContribuyenteEdeclare?> ObtenerPorIdAsync(int id, int userId)
    {
        await EnsureSchemaAsync();
        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.ContribuyentesEdeclare
            .FirstOrDefaultAsync(x => x.CodContribuyente == id && x.Usuario == userId);
    }

    public async Task<ContribuyenteEdeclare> CrearAsync(ContribuyenteEdeclare contribuyente)
    {
        await EnsureSchemaAsync();
        await using var context = await _dbFactory.CreateDbContextAsync();
        contribuyente.FechaIngreso = DateOnly.FromDateTime(DateTime.Today);
        context.ContribuyentesEdeclare.Add(contribuyente);
        await context.SaveChangesAsync();
        return contribuyente;
    }

    public async Task<bool> ActualizarAsync(ContribuyenteEdeclare contribuyente)
    {
        await EnsureSchemaAsync();
        await using var context = await _dbFactory.CreateDbContextAsync();
        var existente = await context.ContribuyentesEdeclare
            .FirstOrDefaultAsync(x => x.CodContribuyente == contribuyente.CodContribuyente
                                   && x.Usuario == contribuyente.Usuario);
        if (existente is null) return false;

        existente.Apellidos = contribuyente.Apellidos;
        existente.Nombres = contribuyente.Nombres;
        existente.Nombrecomercial = contribuyente.Nombrecomercial;
        existente.Nombrerazonsocial = contribuyente.Nombrerazonsocial;
        existente.Tipoidentificacion = contribuyente.Tipoidentificacion;
        existente.Numeroidentificacion = contribuyente.Numeroidentificacion;
        existente.Direccion = contribuyente.Direccion;
        existente.Telefonoconvencional = contribuyente.Telefonoconvencional;
        existente.Celular = contribuyente.Celular;
        existente.Correo = contribuyente.Correo;
        existente.TipoCliente = contribuyente.TipoCliente;
        existente.Oblgconta = contribuyente.Oblgconta;
        existente.Estado = contribuyente.Estado;
        existente.Observaciones = contribuyente.Observaciones;
        existente.Pais = contribuyente.Pais;
        existente.Provincia = contribuyente.Provincia;
        existente.Ciudad = contribuyente.Ciudad;
        existente.PersonaNatural = contribuyente.PersonaNatural;
        existente.ContribuyenteEspecial = contribuyente.ContribuyenteEspecial;
        existente.ActividadContribuyente = contribuyente.ActividadContribuyente;
        existente.NumContribuyente = contribuyente.NumContribuyente;
        existente.PeriodicidadIva = contribuyente.PeriodicidadIva;
        existente.PeriodicidadRenta = contribuyente.PeriodicidadRenta;
        existente.FechaDeclaracion = contribuyente.FechaDeclaracion;

        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DesactivarAsync(int id, int userId)
    {
        await EnsureSchemaAsync();
        await using var context = await _dbFactory.CreateDbContextAsync();
        var existente = await context.ContribuyentesEdeclare
            .FirstOrDefaultAsync(x => x.CodContribuyente == id && x.Usuario == userId);
        if (existente is null) return false;

        existente.Estado = false;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ActivarAsync(int id, int userId)
    {
        await EnsureSchemaAsync();
        await using var context = await _dbFactory.CreateDbContextAsync();
        var existente = await context.ContribuyentesEdeclare
            .FirstOrDefaultAsync(x => x.CodContribuyente == id && x.Usuario == userId);
        if (existente is null) return false;

        existente.Estado = true;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExisteIdentificacionAsync(string? identificacion, int? exceptId, int userId)
    {
        if (string.IsNullOrWhiteSpace(identificacion)) return false;
        await EnsureSchemaAsync();
        await using var context = await _dbFactory.CreateDbContextAsync();
        var query = context.ContribuyentesEdeclare
            .Where(x => x.Usuario == userId && x.Numeroidentificacion == identificacion);
        if (exceptId.HasValue)
        {
            query = query.Where(x => x.CodContribuyente != exceptId.Value);
        }
        return await query.AnyAsync();
    }

    public async Task<(bool Excedido, int Limite, int Actual)> ValidarLimiteContribuyentesAsync(int userId)
    {
        await EnsureSchemaAsync();
        await using var context = await _dbFactory.CreateDbContextAsync();

        // 1. Verificar si es SuperAdmin (IdTipoUsuario == 2)
        var user = await context.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.IdUsuario == userId);
        if (user?.IdTipoUsuario == 2)
        {
            return (false, int.MaxValue, 0); // Sin límite para administradores
        }

        // 2. Obtener el ServicioId para "e-declara"
        var servicio = await context.AppServicios.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Clave == "e-declara");
        if (servicio == null)
        {
            return (false, int.MaxValue, 0); // Si no se encuentra el servicio, no limitamos
        }

        // 3. Obtener la suscripción activa
        var today = DateTime.UtcNow.Date;
        var sub = await context.UsuarioServicioSuscripciones.AsNoTracking()
            .Where(x => x.IdUsuario == userId && x.ServicioId == servicio.ServicioId && x.Estado == "ACTIVA")
            .OrderByDescending(x => x.FechaFin)
            .FirstOrDefaultAsync(x => x.EsVitalicia || !x.FechaFin.HasValue || x.FechaFin.Value.Date >= today);

        // El plan por defecto es "individual"
        var planName = sub?.PlanActual?.ToLower() ?? "individual";

        // Obtener el límite del plan
        int limite = planName switch
        {
            "individual" => 1,
            "asesor" => 100,
            "empresarial" => 500,
            _ => 1 // Por defecto fallback a 1
        };

        // 4. Contar los contribuyentes activos actuales del usuario
        int actualActivos = await context.ContribuyentesEdeclare
            .CountAsync(x => x.Usuario == userId && x.Estado == true);

        return (actualActivos >= limite, limite, actualActivos);
    }
}