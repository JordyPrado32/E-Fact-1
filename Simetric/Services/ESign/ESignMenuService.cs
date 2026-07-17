using Dapper;
using Microsoft.Data.SqlClient;
using Simetric.Models;
using System.Data;
using System.Globalization;

namespace Simetric.Services.ESign
{
    public interface IESignMenuService
    {
        Task EnsureSchemaAsync();
        Task<List<Menu>> GetMenusByRol(int idTipoUsuario);
        Task<List<TipoUsuario>> GetTiposUsuario();
        Task<List<Rol>> GetAllRoles();
        Task<List<Menu>> GetAllMenus();
        Task<List<Menu>> GetMenusByRolId(int idRol);
        Task<bool> UpsertRol(Rol rol);
        Task<bool> UpsertMenu(Menu menu);
        Task<bool> EliminarLogicoRol(int idRol);
        Task<bool> EliminarLogicoMenu(int idMenu);
        Task<bool> AsignarMenusARol(int idRol, List<int> idMenus);
        Task<bool> CrearTipoYRolSimultaneo(string nombrePerfil);
        Task<bool> ActualizarOrdenMenus(List<Menu> menus);
    }

    public class ESignMenuService : IESignMenuService
    {
        private readonly string _connectionString;
        private readonly AuditService _auditService;
        private readonly AuditActorResolver _auditActorResolver;
        private bool _schemaEnsured;
        private static readonly SemaphoreSlim SchemaLock = new(1, 1);

        private sealed class MenuProtegidoLookup
        {
            public int IdMenuPadre { get; set; }
            public string? NombreMenu { get; set; }
            public string? RutaMenu { get; set; }
        }

        private sealed class RolMenuSnapshot
        {
            public int IdMenu { get; set; }
            public string NombreMenu { get; set; } = string.Empty;
            public string? RutaMenu { get; set; }
        }

        public ESignMenuService(
            IConfiguration config,
            AuditService auditService,
            AuditActorResolver auditActorResolver)
        {
            _connectionString = config.GetConnectionString("DefaultConnection")
                                 ?? throw new ArgumentNullException("La cadena de conexión no existe.");
            _auditService = auditService;
            _auditActorResolver = auditActorResolver;
        }

        private IDbConnection Connection => new SqlConnection(_connectionString);
        private int? ActorUserId => _auditActorResolver.ResolveCurrentUserId();

        public async Task EnsureSchemaAsync()
        {
            if (_schemaEnsured)
                return;

            await SchemaLock.WaitAsync();
            try
            {
                if (_schemaEnsured)
                    return;

                using var db = Connection;
                db.Open();

                // 1. ESIGN_MENUS Table
                const string sqlMenus = @"
                IF OBJECT_ID('dbo.ESIGN_MENUS', 'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[ESIGN_MENUS] (
                        [IDMENU] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ESIGN_MENUS] PRIMARY KEY,
                        [IDMENUPADRE] INT NULL,
                        [NOMBREMENU] NVARCHAR(100) NOT NULL,
                        [ESTADOMENU] BIT NULL CONSTRAINT [DF_ESIGN_MENUS_ESTADOMENU] DEFAULT(1),
                        [RUTAMENU] NVARCHAR(200) NULL,
                        [ICONOMENU] NVARCHAR(50) NULL,
                        [ORDENMENU] INT NULL
                    );
                END
                ELSE
                BEGIN
                    IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'ORDENMENU' AND Object_ID = Object_ID(N'dbo.ESIGN_MENUS'))
                    BEGIN
                        ALTER TABLE [dbo].[ESIGN_MENUS] ADD [ORDENMENU] INT NULL;
                    END
                END";
                await db.ExecuteAsync(sqlMenus);

                // 2. ESIGN_ROLES Table
                const string sqlRoles = @"
                IF OBJECT_ID('dbo.ESIGN_ROLES', 'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[ESIGN_ROLES] (
                        [IDROL] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ESIGN_ROLES] PRIMARY KEY,
                        [DESCRIPCIONROL] NVARCHAR(100) NOT NULL,
                        [ESTADOROL] BIT NULL CONSTRAINT [DF_ESIGN_ROLES_ESTADOROL] DEFAULT(1),
                        [IDTIPOUSUARIO] INT NULL
                    );
                END";
                await db.ExecuteAsync(sqlRoles);

                // 3. ESIGN_ROL_MENU Table
                const string sqlRolMenu = @"
                IF OBJECT_ID('dbo.ESIGN_ROL_MENU', 'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[ESIGN_ROL_MENU] (
                        [IDROL] INT NOT NULL,
                        [IDMENU] INT NOT NULL,
                        CONSTRAINT [PK_ESIGN_ROL_MENU] PRIMARY KEY CLUSTERED ([IDROL] ASC, [IDMENU] ASC)
                    );
                END";
                await db.ExecuteAsync(sqlRolMenu);

                // 4. ESIGN_TARJETAS Table
                const string sqlTarjetas = @"
                IF OBJECT_ID('dbo.ESIGN_TARJETAS', 'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[ESIGN_TARJETAS] (
                        [ID_TARJETA] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ESIGN_TARJETAS] PRIMARY KEY,
                        [ID_USUARIO] INT NOT NULL,
                        [DOCUMENTO] NVARCHAR(20) NOT NULL,
                        [TOKEN] NVARCHAR(100) NOT NULL,
                        [MARCA_TARJETA] NVARCHAR(50) NULL,
                        [NUMERO_MASCARA] NVARCHAR(50) NULL,
                        [FECHA_REGISTRO] DATETIME NOT NULL,
                        [ESTADO] BIT NOT NULL CONSTRAINT [DF_ESIGN_TARJETAS_ESTADO] DEFAULT(1)
                    );
                END";
                await db.ExecuteAsync(sqlTarjetas);

                // Seed Roles
                const string seedRoles = @"
                IF NOT EXISTS (SELECT TOP 1 1 FROM [dbo].[ESIGN_ROLES])
                BEGIN
                    SET IDENTITY_INSERT [dbo].[ESIGN_ROLES] ON;
                    INSERT INTO [dbo].[ESIGN_ROLES] ([IDROL], [DESCRIPCIONROL], [ESTADOROL], [IDTIPOUSUARIO])
                    SELECT [IDROL], [DESCRIPCIONROL], [ESTADOROL], [IDTIPOUSUARIO] FROM [dbo].[ROLES];
                    SET IDENTITY_INSERT [dbo].[ESIGN_ROLES] OFF;
                END";
                await db.ExecuteAsync(seedRoles);

                // Seed Menus
                const string seedMenus = @"
                SET IDENTITY_INSERT [dbo].[ESIGN_MENUS] ON;
                
                IF NOT EXISTS (SELECT 1 FROM [dbo].[ESIGN_MENUS] WHERE [IDMENU] = 1)
                    INSERT INTO [dbo].[ESIGN_MENUS] ([IDMENU], [IDMENUPADRE], [NOMBREMENU], [ESTADOMENU], [RUTAMENU], [ICONOMENU]) VALUES (1, NULL, 'Inicio', 1, '/e-sign', 'ri-home-4-line');
                
                IF NOT EXISTS (SELECT 1 FROM [dbo].[ESIGN_MENUS] WHERE [IDMENU] = 2)
                    INSERT INTO [dbo].[ESIGN_MENUS] ([IDMENU], [IDMENUPADRE], [NOMBREMENU], [ESTADOMENU], [RUTAMENU], [ICONOMENU]) VALUES (2, NULL, 'Documentos firmados', 1, '/e-sign/documentos', 'ri-file-shield-2-line');
                
                IF NOT EXISTS (SELECT 1 FROM [dbo].[ESIGN_MENUS] WHERE [IDMENU] = 3)
                    INSERT INTO [dbo].[ESIGN_MENUS] ([IDMENU], [IDMENUPADRE], [NOMBREMENU], [ESTADOMENU], [RUTAMENU], [ICONOMENU]) VALUES (3, NULL, 'Firma Electronica', 1, '', 'ri-pen-nib-line');
                
                IF NOT EXISTS (SELECT 1 FROM [dbo].[ESIGN_MENUS] WHERE [IDMENU] = 4)
                    INSERT INTO [dbo].[ESIGN_MENUS] ([IDMENU], [IDMENUPADRE], [NOMBREMENU], [ESTADOMENU], [RUTAMENU], [ICONOMENU]) VALUES (4, 3, 'Nueva Solicitud', 1, '/solicitud/nueva', 'ri-file-add-line');
                
                IF NOT EXISTS (SELECT 1 FROM [dbo].[ESIGN_MENUS] WHERE [IDMENU] = 5)
                    INSERT INTO [dbo].[ESIGN_MENUS] ([IDMENU], [IDMENUPADRE], [NOMBREMENU], [ESTADOMENU], [RUTAMENU], [ICONOMENU]) VALUES (5, 3, 'Mis Pagos', 1, '/solicitud/pagos', 'ri-bill-line');
                
                IF NOT EXISTS (SELECT 1 FROM [dbo].[ESIGN_MENUS] WHERE [IDMENU] = 6)
                    INSERT INTO [dbo].[ESIGN_MENUS] ([IDMENU], [IDMENUPADRE], [NOMBREMENU], [ESTADOMENU], [RUTAMENU], [ICONOMENU]) VALUES (6, NULL, 'Configuracion', 1, '', 'ri-settings-3-line');

                IF NOT EXISTS (SELECT 1 FROM [dbo].[ESIGN_MENUS] WHERE [IDMENU] = 7)
                    INSERT INTO [dbo].[ESIGN_MENUS] ([IDMENU], [IDMENUPADRE], [NOMBREMENU], [ESTADOMENU], [RUTAMENU], [ICONOMENU]) VALUES (7, NULL, 'Soporte', 1, '/e-sign/soporte', 'ri-customer-service-2-line');

                IF NOT EXISTS (SELECT 1 FROM [dbo].[ESIGN_MENUS] WHERE [IDMENU] = 8)
                    INSERT INTO [dbo].[ESIGN_MENUS] ([IDMENU], [IDMENUPADRE], [NOMBREMENU], [ESTADOMENU], [RUTAMENU], [ICONOMENU]) VALUES (8, 6, 'Perfil', 1, '/e-sign/configuracion/perfil', 'ri-user-settings-line');

                IF NOT EXISTS (SELECT 1 FROM [dbo].[ESIGN_MENUS] WHERE [IDMENU] = 9)
                    INSERT INTO [dbo].[ESIGN_MENUS] ([IDMENU], [IDMENUPADRE], [NOMBREMENU], [ESTADOMENU], [RUTAMENU], [ICONOMENU]) VALUES (9, 6, 'Plan disponible', 1, '/e-sign/configuracion/plan', 'ri-bill-line');

                IF NOT EXISTS (SELECT 1 FROM [dbo].[ESIGN_MENUS] WHERE [IDMENU] = 10)
                    INSERT INTO [dbo].[ESIGN_MENUS] ([IDMENU], [IDMENUPADRE], [NOMBREMENU], [ESTADOMENU], [RUTAMENU], [ICONOMENU]) VALUES (10, NULL, 'Mis firmas', 1, '/e-sign/mis-firmas', 'ri-key-2-line');

                IF NOT EXISTS (SELECT 1 FROM [dbo].[ESIGN_MENUS] WHERE [IDMENU] = 11)
                    INSERT INTO [dbo].[ESIGN_MENUS] ([IDMENU], [IDMENUPADRE], [NOMBREMENU], [ESTADOMENU], [RUTAMENU], [ICONOMENU]) VALUES (11, 3, 'Mis firmas', 0, '/e-sign/mis-firmas', 'ri-shield-user-line');

                IF NOT EXISTS (SELECT 1 FROM [dbo].[ESIGN_MENUS] WHERE [IDMENU] = 12)
                    INSERT INTO [dbo].[ESIGN_MENUS] ([IDMENU], [IDMENUPADRE], [NOMBREMENU], [ESTADOMENU], [RUTAMENU], [ICONOMENU]) VALUES (12, NULL, 'Administracion', 1, '', 'ri-settings-3-line');

                IF NOT EXISTS (SELECT 1 FROM [dbo].[ESIGN_MENUS] WHERE [IDMENU] = 13)
                    INSERT INTO [dbo].[ESIGN_MENUS] ([IDMENU], [IDMENUPADRE], [NOMBREMENU], [ESTADOMENU], [RUTAMENU], [ICONOMENU]) VALUES (13, 12, 'Roles y permisos', 1, '/e-sign/administracion/roles', 'ri-key-line');

                IF NOT EXISTS (SELECT 1 FROM [dbo].[ESIGN_MENUS] WHERE [IDMENU] = 14)
                    INSERT INTO [dbo].[ESIGN_MENUS] ([IDMENU], [IDMENUPADRE], [NOMBREMENU], [ESTADOMENU], [RUTAMENU], [ICONOMENU]) VALUES (14, 12, 'Usuarios', 1, '/e-sign/administracion/usuarios', 'ri-user-shared-line');

                SET IDENTITY_INSERT [dbo].[ESIGN_MENUS] OFF;";
                await db.ExecuteAsync(seedMenus);

                // Asegurar que Soporte quede fuera de Configuración a nivel raíz
                await db.ExecuteAsync("UPDATE [dbo].[ESIGN_MENUS] SET [IDMENUPADRE] = NULL WHERE [IDMENU] = 7;");

                // Actualizar icono de Administración a ri-settings-3-line (igual a e-fact)
                await db.ExecuteAsync("UPDATE [dbo].[ESIGN_MENUS] SET [ICONOMENU] = 'ri-settings-3-line' WHERE [IDMENU] = 12;");

                // Asegurar que Nueva Solicitud (ID 4) y el menú principal de Mis Firmas (ID 10) estén activos
                await db.ExecuteAsync("UPDATE [dbo].[ESIGN_MENUS] SET [ESTADOMENU] = 1 WHERE [IDMENU] IN (4, 10);");

                // Restablecer mapeos para menús reactivados (4 y 10) a todos los roles
                const string restoreMapping = @"
                INSERT INTO [dbo].[ESIGN_ROL_MENU] ([IDROL], [IDMENU])
                SELECT r.[IDROL], m.[IDMENU]
                FROM [dbo].[ESIGN_ROLES] r
                CROSS JOIN [dbo].[ESIGN_MENUS] m
                WHERE m.[IDMENU] IN (4, 10)
                AND NOT EXISTS (
                    SELECT 1 FROM [dbo].[ESIGN_ROL_MENU] rm
                    WHERE rm.[IDROL] = r.[IDROL] AND rm.[IDMENU] = m.[IDMENU]
                );";
                await db.ExecuteAsync(restoreMapping);

                // Deshabilitar menú redundante Mis Firmas (ID 11) y eliminar sus mapeos
                await db.ExecuteAsync("UPDATE [dbo].[ESIGN_MENUS] SET [ESTADOMENU] = 0 WHERE [IDMENU] = 11;");
                await db.ExecuteAsync("DELETE FROM [dbo].[ESIGN_ROL_MENU] WHERE [IDMENU] = 11;");

                // Mapear automáticamente los nuevos menús a todos los roles existentes
                const string seedRolMenuMapping = @"
                -- 1. Map general menus (excluding Administration) to all roles
                INSERT INTO [dbo].[ESIGN_ROL_MENU] ([IDROL], [IDMENU])
                SELECT r.[IDROL], m.[IDMENU]
                FROM [dbo].[ESIGN_ROLES] r
                CROSS JOIN [dbo].[ESIGN_MENUS] m
                WHERE m.[IDMENU] NOT IN (12, 13, 14)
                AND NOT EXISTS (
                    SELECT 1 FROM [dbo].[ESIGN_ROL_MENU] rm
                    WHERE rm.[IDROL] = r.[IDROL] AND rm.[IDMENU] = m.[IDMENU]
                );

                -- 2. Map Administration menus only to Administrator role (IDTIPOUSUARIO = 2)
                INSERT INTO [dbo].[ESIGN_ROL_MENU] ([IDROL], [IDMENU])
                SELECT r.[IDROL], m.[IDMENU]
                FROM [dbo].[ESIGN_ROLES] r
                CROSS JOIN [dbo].[ESIGN_MENUS] m
                WHERE r.[IDTIPOUSUARIO] = 2 AND m.[IDMENU] IN (12, 13, 14)
                AND NOT EXISTS (
                    SELECT 1 FROM [dbo].[ESIGN_ROL_MENU] rm
                    WHERE rm.[IDROL] = r.[IDROL] AND rm.[IDMENU] = m.[IDMENU]
                );";
                await db.ExecuteAsync(seedRolMenuMapping);
                _schemaEnsured = true;
            }
            finally
            {
                SchemaLock.Release();
            }
        }

        private static bool CoincideNombreMenu(string? valorActual, string valorEsperado) =>
            !string.IsNullOrWhiteSpace(valorActual) &&
            CultureInfo.InvariantCulture.CompareInfo.Compare(
                valorActual.Trim(),
                valorEsperado,
                CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;

        private static bool EsModuloPrincipalConfiguracionProtegido(MenuProtegidoLookup? menu) =>
            menu is not null &&
            menu.IdMenuPadre == 0 &&
            CoincideNombreMenu(menu.NombreMenu, "Configuracion");

        private static bool EsMenuProtegido(MenuProtegidoLookup? menu) =>
            EsModuloPrincipalConfiguracionProtegido(menu);

        private static async Task<bool> EsMenuProtegido(IDbConnection db, int idMenu, IDbTransaction? trans = null)
        {
            const string sql = @"
                SELECT TOP 1
                    ISNULL(IDMENUPADRE, 0) AS IdMenuPadre,
                    NOMBREMENU AS NombreMenu,
                    RUTAMENU AS RutaMenu
                FROM ESIGN_MENUS
                WHERE IDMENU = @idMenu";

            var menu = await db.QuerySingleOrDefaultAsync<MenuProtegidoLookup>(sql, new { idMenu }, trans);
            return EsMenuProtegido(menu);
        }

        private static async Task<TipoUsuario?> ObtenerTipoUsuarioPorIdAsync(IDbConnection db, int idTipoUsuario, IDbTransaction? trans = null)
        {
            const string sql = @"
                SELECT TOP 1 IdTipoUsuario, NombreTipo, Descripcion, Estado
                FROM TIPOUSUARIO
                WHERE IdTipoUsuario = @idTipoUsuario";

            return await db.QuerySingleOrDefaultAsync<TipoUsuario>(sql, new { idTipoUsuario }, trans);
        }

        private static async Task<Rol?> ObtenerRolPorIdAsync(IDbConnection db, int idRol, IDbTransaction? trans = null)
        {
            const string sql = @"
                SELECT TOP 1 IDROL, DESCRIPCIONROL, ESTADOROL, IDTIPOUSUARIO
                FROM ESIGN_ROLES
                WHERE IDROL = @idRol";

            return await db.QuerySingleOrDefaultAsync<Rol>(sql, new { idRol }, trans);
        }

        private static async Task<Menu?> ObtenerMenuPorIdAsync(IDbConnection db, int idMenu, IDbTransaction? trans = null)
        {
            const string sql = @"
                SELECT TOP 1 IDMENU, IDMENUPADRE, NOMBREMENU, ESTADOMENU, RUTAMENU, ICONOMENU
                FROM ESIGN_MENUS
                WHERE IDMENU = @idMenu";

            return await db.QuerySingleOrDefaultAsync<Menu>(sql, new { idMenu }, trans);
        }

        private static async Task<List<Menu>> ObtenerMenusRelacionadosAsync(IDbConnection db, int idMenu, IDbTransaction? trans = null)
        {
            const string sql = @"
                SELECT IDMENU, IDMENUPADRE, NOMBREMENU, ESTADOMENU, RUTAMENU, ICONOMENU
                FROM ESIGN_MENUS
                WHERE IDMENU = @idMenu OR IDMENUPADRE = @idMenu
                ORDER BY IDMENU";

            return (await db.QueryAsync<Menu>(sql, new { idMenu }, trans)).ToList();
        }

        private static async Task<List<RolMenuSnapshot>> ObtenerMenusRolAsync(IDbConnection db, int idRol, IDbTransaction? trans = null)
        {
            const string sql = @"
                SELECT m.IDMENU AS IdMenu, m.NOMBREMENU AS NombreMenu, m.RUTAMENU AS RutaMenu
                FROM ESIGN_ROL_MENU rm
                INNER JOIN ESIGN_MENUS m ON m.IDMENU = rm.IDMENU
                WHERE rm.IDROL = @idRol
                ORDER BY m.NOMBREMENU";

            return (await db.QueryAsync<RolMenuSnapshot>(sql, new { idRol }, trans)).ToList();
        }

        private static object SnapshotTipoUsuario(TipoUsuario? tipoUsuario) => tipoUsuario == null
            ? new { }
            : new
            {
                tipoUsuario.IdTipoUsuario,
                tipoUsuario.NombreTipo,
                tipoUsuario.Descripcion,
                tipoUsuario.Estado
            };

        private static object SnapshotRol(Rol? rol) => rol == null
            ? new { }
            : new
            {
                rol.IdRol,
                rol.DescripcionRol,
                rol.EstadoRol,
                rol.IdTipoUsuario
            };

        private static object SnapshotMenu(Menu? menu) => menu == null
            ? new { }
            : new
            {
                menu.IdMenu,
                menu.IdMenuPadre,
                menu.NombreMenu,
                menu.EstadoMenu,
                menu.RutaMenu,
                menu.IconoMenu
            };

        private static object SnapshotMenus(IEnumerable<Menu> menus) =>
            menus.Select(menu => SnapshotMenu(menu)).ToList();

        private static object SnapshotMenusRol(IEnumerable<RolMenuSnapshot> menus) =>
            menus.Select(menu => new
            {
                menu.IdMenu,
                menu.NombreMenu,
                menu.RutaMenu
            }).ToList();

        private Dictionary<string, object?> CrearDetalleAuditoria(string entidad, string tabla, object llaves)
        {
            var detalle = new Dictionary<string, object?>
            {
                ["Entidad"] = entidad,
                ["Tabla"] = tabla,
                ["Llaves"] = llaves
            };

            var ruta = _auditActorResolver.ResolveRequestPath();
            if (!string.IsNullOrWhiteSpace(ruta))
            {
                detalle["Ruta"] = ruta;
            }

            var direccionIp = _auditActorResolver.ResolveRemoteIpAddress();
            if (!string.IsNullOrWhiteSpace(direccionIp))
            {
                detalle["DireccionIP"] = direccionIp;
            }

            return detalle;
        }

        private async Task RegistrarAuditoriaAsync(
            string accion,
            object? valoresPrevios,
            object? valorNuevo,
            Dictionary<string, object?> detalles)
        {
            if (!_auditService.IsEnabled)
            {
                return;
            }

            await _auditService.RegistrarAuditoriaAsync(
                ActorUserId,
                accion,
                valoresPrevios,
                valorNuevo,
                detalles);
        }

        #region Consultas

        public async Task<List<Menu>> GetMenusByRol(int idTipoUsuario)
        {
            using var db = Connection;
            const string sql = @"
                SELECT DISTINCT m.IDMENU, m.NOMBREMENU, m.RUTAMENU, m.ICONOMENU, m.IDMENUPADRE, m.ESTADOMENU, m.ORDENMENU
                FROM ESIGN_MENUS m
                INNER JOIN ESIGN_ROL_MENU rm ON m.IDMENU = rm.IDMENU
                INNER JOIN ESIGN_ROLES r ON rm.IDROL = r.IDROL
                WHERE r.IDTIPOUSUARIO = @idTipoUsuario 
                AND m.ESTADOMENU = 1 
                AND r.ESTADOROL = 1
                ORDER BY m.IDMENUPADRE ASC, m.ORDENMENU ASC, m.NOMBREMENU ASC";

            return (await db.QueryAsync<Menu>(sql, new { idTipoUsuario })).ToList();
        }

        public async Task<List<Menu>> GetAllMenus()
        {
            using var db = Connection;
            const string sql = "SELECT * FROM ESIGN_MENUS WHERE ESTADOMENU = 1 ORDER BY ISNULL(IDMENUPADRE, 0), ISNULL(ORDENMENU, 0), NOMBREMENU";
            return (await db.QueryAsync<Menu>(sql)).ToList();
        }

        public async Task<List<Menu>> GetMenusByRolId(int idRol)
        {
            using var db = Connection;
            const string sql = @"
                SELECT m.* FROM ESIGN_MENUS m
                INNER JOIN ESIGN_ROL_MENU rm ON m.IDMENU = rm.IDMENU
                WHERE rm.IDROL = @idRol AND m.ESTADOMENU = 1";

            return (await db.QueryAsync<Menu>(sql, new { idRol })).ToList();
        }

        public async Task<List<TipoUsuario>> GetTiposUsuario()
        {
            using var db = Connection;
            return (await db.QueryAsync<TipoUsuario>("SELECT * FROM TIPOUSUARIO WHERE ESTADO = 1 ORDER BY NOMBRETIPO")).ToList();
        }

        public async Task<List<Rol>> GetAllRoles()
        {
            using var db = Connection;
            const string sql = @"
                SELECT r.*, t.NOMBRETIPO 
                FROM ESIGN_ROLES r 
                INNER JOIN TIPOUSUARIO t ON r.IDTIPOUSUARIO = t.IDTIPOUSUARIO 
                WHERE r.ESTADOROL = 1 
                ORDER BY r.DESCRIPCIONROL";

            return (await db.QueryAsync<Rol>(sql)).ToList();
        }

        #endregion

        #region Comandos de Gestión

        public async Task<bool> CrearTipoYRolSimultaneo(string nombrePerfil)
        {
            using var db = Connection;
            db.Open();
            using var trans = db.BeginTransaction();
            try
            {
                const string sqlTipo = @"INSERT INTO TIPOUSUARIO (NOMBRETIPO, ESTADO) 
                                         VALUES (@nombrePerfil, 1); 
                                         SELECT CAST(SCOPE_IDENTITY() as int);";

                int nuevoIdTipo = await db.ExecuteScalarAsync<int>(sqlTipo, new { nombrePerfil }, trans);

                const string sqlRol = @"INSERT INTO ESIGN_ROLES (DESCRIPCIONROL, IDTIPOUSUARIO, ESTADOROL) 
                                        VALUES (@nombrePerfil, @nuevoIdTipo, 1);
                                        SELECT CAST(SCOPE_IDENTITY() as int);";

                int nuevoIdRol = await db.ExecuteScalarAsync<int>(sqlRol, new { nombrePerfil, nuevoIdTipo }, trans);

                var tipoCreado = await ObtenerTipoUsuarioPorIdAsync(db, nuevoIdTipo, trans);
                var rolCreado = await ObtenerRolPorIdAsync(db, nuevoIdRol, trans);

                trans.Commit();

                var detalleTipo = CrearDetalleAuditoria("TipoUsuario", "TIPOUSUARIO", new { IdTipoUsuario = nuevoIdTipo });
                detalleTipo["Origen"] = "CrearTipoYRolSimultaneo";
                await RegistrarAuditoriaAsync("CREAR", null, SnapshotTipoUsuario(tipoCreado), detalleTipo);

                var detalleRol = CrearDetalleAuditoria("Rol", "ESIGN_ROLES", new { IdRol = nuevoIdRol });
                detalleRol["Origen"] = "CrearTipoYRolSimultaneo";
                await RegistrarAuditoriaAsync("CREAR", null, SnapshotRol(rolCreado), detalleRol);

                return true;
            }
            catch
            {
                trans.Rollback();
                return false;
            }
        }

        public async Task<bool> AsignarMenusARol(int idRol, List<int> idMenus)
        {
            using var db = Connection;
            db.Open();
            using var trans = db.BeginTransaction();
            try
            {
                var rol = await ObtenerRolPorIdAsync(db, idRol, trans);
                var menusPrevios = await ObtenerMenusRolAsync(db, idRol, trans);

                await db.ExecuteAsync("DELETE FROM ESIGN_ROL_MENU WHERE IDROL = @idRol", new { idRol }, trans);

                if (idMenus != null && idMenus.Any())
                {
                    const string sqlIns = "INSERT INTO ESIGN_ROL_MENU (IDROL, IDMENU) VALUES (@idRol, @idMenu)";
                    var parametros = idMenus.Select(mId => new { idRol, idMenu = mId });
                    await db.ExecuteAsync(sqlIns, parametros, trans);
                }

                var menusNuevos = await ObtenerMenusRolAsync(db, idRol, trans);

                trans.Commit();

                var detalle = CrearDetalleAuditoria("RolMenu", "ESIGN_ROL_MENU", new { IdRol = idRol });
                detalle["Rol"] = SnapshotRol(rol);
                detalle["CantidadMenus"] = menusNuevos.Count;

                await RegistrarAuditoriaAsync(
                    "MODIFICAR",
                    SnapshotMenusRol(menusPrevios),
                    SnapshotMenusRol(menusNuevos),
                    detalle);

                return true;
            }
            catch
            {
                trans.Rollback();
                return false;
            }
        }

        public async Task<bool> UpsertRol(Rol rol)
        {
            using var db = Connection;
            var rolPrevio = rol.IdRol > 0
                ? await ObtenerRolPorIdAsync(db, rol.IdRol)
                : null;

            if (rolPrevio is not null)
            {
                const string sqlUpdate = @"
                    UPDATE ESIGN_ROLES
                    SET DESCRIPCIONROL = @DescripcionRol,
                        IDTIPOUSUARIO = @IdTipoUsuario
                    WHERE IDROL = @IdRol";

                var actualizado = await db.ExecuteAsync(sqlUpdate, rol) > 0;
                if (!actualizado)
                {
                    return false;
                }

                var rolNuevo = await ObtenerRolPorIdAsync(db, rolPrevio.IdRol);
                var detalle = CrearDetalleAuditoria("Rol", "ESIGN_ROLES", new { IdRol = rolPrevio.IdRol });
                await RegistrarAuditoriaAsync("MODIFICAR", SnapshotRol(rolPrevio), SnapshotRol(rolNuevo), detalle);
                return true;
            }

            const string sqlInsert = @"
                INSERT INTO ESIGN_ROLES (DESCRIPCIONROL, IDTIPOUSUARIO, ESTADOROL)
                VALUES (@DescripcionRol, @IdTipoUsuario, 1);
                SELECT CAST(SCOPE_IDENTITY() as int);";

            var nuevoIdRol = await db.ExecuteScalarAsync<int>(sqlInsert, new
            {
                rol.DescripcionRol,
                rol.IdTipoUsuario
            });

            var rolCreado = await ObtenerRolPorIdAsync(db, nuevoIdRol);
            var detalleCreacion = CrearDetalleAuditoria("Rol", "ESIGN_ROLES", new { IdRol = nuevoIdRol });
            await RegistrarAuditoriaAsync("CREAR", null, SnapshotRol(rolCreado), detalleCreacion);

            return true;
        }

        public async Task<bool> UpsertMenu(Menu menu)
        {
            using var db = Connection;

            if (menu.IdMenu != 0 && await EsMenuProtegido(db, menu.IdMenu))
            {
                return false;
            }

            var idPadre = menu.IdMenuPadre == 0 ? (int?)null : menu.IdMenuPadre;
            var menuPrevio = menu.IdMenu > 0
                ? await ObtenerMenuPorIdAsync(db, menu.IdMenu)
                : null;

            if (menuPrevio is not null)
            {
                const string sqlUpdate = @"
                    UPDATE ESIGN_MENUS
                    SET NOMBREMENU = @NombreMenu,
                        RUTAMENU = @RutaMenu,
                        ICONOMENU = @IconoMenu,
                        IDMENUPADRE = @idPadre
                    WHERE IDMENU = @IdMenu";

                var actualizado = await db.ExecuteAsync(sqlUpdate, new
                {
                    menu.IdMenu,
                    menu.NombreMenu,
                    menu.RutaMenu,
                    menu.IconoMenu,
                    idPadre
                }) > 0;

                if (!actualizado)
                {
                    return false;
                }

                var menuNuevo = await ObtenerMenuPorIdAsync(db, menu.IdMenu);
                var detalle = CrearDetalleAuditoria("Menu", "ESIGN_MENUS", new { IdMenu = menu.IdMenu });
                await RegistrarAuditoriaAsync("MODIFICAR", SnapshotMenu(menuPrevio), SnapshotMenu(menuNuevo), detalle);
                return true;
            }

            const string sqlInsert = @"
                INSERT INTO ESIGN_MENUS (NOMBREMENU, RUTAMENU, ICONOMENU, IDMENUPADRE, ESTADOMENU)
                VALUES (@NombreMenu, @RutaMenu, @IconoMenu, @idPadre, 1);
                SELECT CAST(SCOPE_IDENTITY() as int);";

            var nuevoIdMenu = await db.ExecuteScalarAsync<int>(sqlInsert, new
            {
                menu.NombreMenu,
                menu.RutaMenu,
                menu.IconoMenu,
                idPadre
            });

            var menuCreado = await ObtenerMenuPorIdAsync(db, nuevoIdMenu);
            var detalleCreacion = CrearDetalleAuditoria("Menu", "ESIGN_MENUS", new { IdMenu = nuevoIdMenu });
            await RegistrarAuditoriaAsync("CREAR", null, SnapshotMenu(menuCreado), detalleCreacion);

            return true;
        }

        public async Task<bool> EliminarLogicoRol(int idRol)
        {
            using var db = Connection;

            var rolPrevio = await ObtenerRolPorIdAsync(db, idRol);
            if (rolPrevio is null)
            {
                return false;
            }

            var eliminado = await db.ExecuteAsync(
                "UPDATE ESIGN_ROLES SET ESTADOROL = 0 WHERE IDROL = @idRol",
                new { idRol }) > 0;

            if (!eliminado)
            {
                return false;
            }

            var rolNuevo = await ObtenerRolPorIdAsync(db, idRol);
            var detalle = CrearDetalleAuditoria("Rol", "ESIGN_ROLES", new { IdRol = idRol });
            await RegistrarAuditoriaAsync("ELIMINAR", SnapshotRol(rolPrevio), SnapshotRol(rolNuevo), detalle);

            return true;
        }

        public async Task<bool> EliminarLogicoMenu(int idMenu)
        {
            using var db = Connection;

            if (await EsMenuProtegido(db, idMenu))
            {
                return false;
            }

            var menusPrevios = await ObtenerMenusRelacionadosAsync(db, idMenu);
            db.Open();
            using var trans = db.BeginTransaction();
            try
            {
                await db.ExecuteAsync("UPDATE ESIGN_MENUS SET ESTADOMENU = 0 WHERE IDMENU = @idMenu OR IDMENUPADRE = @idMenu", new { idMenu }, trans);

                var menusNuevos = await ObtenerMenusRelacionadosAsync(db, idMenu, trans);

                trans.Commit();

                var detalle = CrearDetalleAuditoria("Menu", "ESIGN_MENUS", new { IdMenu = idMenu });
                detalle["CantidadRegistros"] = menusNuevos.Count;

                await RegistrarAuditoriaAsync(
                    "ELIMINAR",
                    SnapshotMenus(menusPrevios),
                    SnapshotMenus(menusNuevos),
                    detalle);

                return true;
            }
            catch
            {
                trans.Rollback();
                return false;
            }
        }

        public async Task<bool> ActualizarOrdenMenus(List<Menu> menus)
        {
            if (menus == null || !menus.Any())
            {
                return false;
            }

            using var db = Connection;
            db.Open();
            using var trans = db.BeginTransaction();
            try
            {
                const string sqlUpdate = @"
                    UPDATE ESIGN_MENUS
                    SET ORDENMENU = @OrdenMenu
                    WHERE IDMENU = @IdMenu";

                foreach (var menu in menus)
                {
                    await db.ExecuteAsync(sqlUpdate, new { menu.OrdenMenu, menu.IdMenu }, trans);
                }

                trans.Commit();
                return true;
            }
            catch
            {
                trans.Rollback();
                return false;
            }
        }

        #endregion
    }
}
