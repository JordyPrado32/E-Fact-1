using Dapper;
using Microsoft.Data.SqlClient;
using Simetric.Models;
using System.Data;
using System.Globalization;

namespace Simetric.Services.EDeclara
{
    public interface IEDeclaraMenuService
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
    }

    public class EDeclaraMenuService : IEDeclaraMenuService
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

        public EDeclaraMenuService(
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

                // 1. EDECLARA_MENUS Table
                const string sqlMenus = @"
                IF OBJECT_ID('dbo.EDECLARA_MENUS', 'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[EDECLARA_MENUS] (
                        [IDMENU] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_EDECLARA_MENUS] PRIMARY KEY,
                        [IDMENUPADRE] INT NULL,
                        [NOMBREMENU] NVARCHAR(100) NOT NULL,
                        [ESTADOMENU] BIT NULL CONSTRAINT [DF_EDECLARA_MENUS_ESTADOMENU] DEFAULT(1),
                        [RUTAMENU] NVARCHAR(200) NULL,
                        [ICONOMENU] NVARCHAR(50) NULL
                    );
                END";
                await db.ExecuteAsync(sqlMenus);

                // 2. EDECLARA_ROLES Table
                const string sqlRoles = @"
                IF OBJECT_ID('dbo.EDECLARA_ROLES', 'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[EDECLARA_ROLES] (
                        [IDROL] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_EDECLARA_ROLES] PRIMARY KEY,
                        [DESCRIPCIONROL] NVARCHAR(100) NOT NULL,
                        [ESTADOROL] BIT NULL CONSTRAINT [DF_EDECLARA_ROLES_ESTADOROL] DEFAULT(1),
                        [IDTIPOUSUARIO] INT NULL
                    );
                END";
                await db.ExecuteAsync(sqlRoles);

                // 3. EDECLARA_ROL_MENU Table
                const string sqlRolMenu = @"
                IF OBJECT_ID('dbo.EDECLARA_ROL_MENU', 'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[EDECLARA_ROL_MENU] (
                        [IDROL] INT NOT NULL,
                        [IDMENU] INT NOT NULL,
                        CONSTRAINT [PK_EDECLARA_ROL_MENU] PRIMARY KEY CLUSTERED ([IDROL] ASC, [IDMENU] ASC)
                    );
                END";
                await db.ExecuteAsync(sqlRolMenu);

                // Seed Roles
                const string seedRoles = @"
                IF NOT EXISTS (SELECT TOP 1 1 FROM [dbo].[EDECLARA_ROLES])
                BEGIN
                    SET IDENTITY_INSERT [dbo].[EDECLARA_ROLES] ON;
                    INSERT INTO [dbo].[EDECLARA_ROLES] ([IDROL], [DESCRIPCIONROL], [ESTADOROL], [IDTIPOUSUARIO])
                    SELECT [IDROL], [DESCRIPCIONROL], [ESTADOROL], [IDTIPOUSUARIO] FROM [dbo].[ROLES];
                    SET IDENTITY_INSERT [dbo].[EDECLARA_ROLES] OFF;
                END";
                await db.ExecuteAsync(seedRoles);

                // Seed Menus
                const string seedMenus = @"
                IF NOT EXISTS (SELECT TOP 1 1 FROM [dbo].[EDECLARA_MENUS])
                BEGIN
                    SET IDENTITY_INSERT [dbo].[EDECLARA_MENUS] ON;
                    
                    INSERT INTO [dbo].[EDECLARA_MENUS] ([IDMENU], [IDMENUPADRE], [NOMBREMENU], [ESTADOMENU], [RUTAMENU], [ICONOMENU]) VALUES
                    (1, NULL, 'Inicio', 1, '/e-declara', 'ri-home-4-line'),
                    (2, NULL, 'Contribuyentes', 1, '/e-declara/contribuyentes', 'ri-user-star-line'),
                    (3, NULL, 'Soporte', 1, '/e-declara/soporte', 'ri-customer-service-2-line'),
                    (4, NULL, 'Administracion', 1, '', 'ri-shield-keyhole-line'),
                    (5, NULL, 'Configuracion', 1, '', 'ri-settings-3-line');

                    INSERT INTO [dbo].[EDECLARA_MENUS] ([IDMENU], [IDMENUPADRE], [NOMBREMENU], [ESTADOMENU], [RUTAMENU], [ICONOMENU]) VALUES
                    (6, 4, 'Roles y permisos', 1, '/e-declara/administracion/roles', 'ri-key-line'),
                    (7, 5, 'Perfil', 1, '/e-declara/configuracion/perfil', 'ri-user-settings-line'),
                    (8, 5, 'Parametros', 1, '/e-declara/configuracion/parametros', 'ri-settings-5-line'),
                    (9, 5, 'Periodos', 1, '/e-declara/configuracion/periodos', 'ri-calendar-todo-line'),
                    (10, 5, 'Tipos de Declaracion', 1, '/e-declara/configuracion/tipos-declaracion', 'ri-file-list-3-line');

                    SET IDENTITY_INSERT [dbo].[EDECLARA_MENUS] OFF;
                END";
                await db.ExecuteAsync(seedMenus);

                // Seed RolMenu mappings
                const string seedRolMenuMapping = @"
                IF NOT EXISTS (SELECT TOP 1 1 FROM [dbo].[EDECLARA_ROL_MENU])
                BEGIN
                    INSERT INTO [dbo].[EDECLARA_ROL_MENU] ([IDROL], [IDMENU])
                    SELECT r.[IDROL], m.[IDMENU]
                    FROM [dbo].[EDECLARA_ROLES] r
                    CROSS JOIN [dbo].[EDECLARA_MENUS] m;
                END";
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

        private static bool EsModuloPrincipalAdministracionProtegido(MenuProtegidoLookup? menu) =>
            menu is not null &&
            menu.IdMenuPadre == 0 &&
            CoincideNombreMenu(menu.NombreMenu, "Administracion");

        private static bool EsSeccionRolesPermisosProtegida(MenuProtegidoLookup? menu) =>
            menu is not null &&
            ((!string.IsNullOrWhiteSpace(menu.RutaMenu) &&
              string.Equals(menu.RutaMenu.Trim(), "/e-declara/administracion/roles", StringComparison.OrdinalIgnoreCase)) ||
             CoincideNombreMenu(menu.NombreMenu, "Roles y permisos"));

        private static bool EsMenuProtegido(MenuProtegidoLookup? menu) =>
            EsModuloPrincipalAdministracionProtegido(menu) || EsSeccionRolesPermisosProtegida(menu);

        private static async Task<bool> EsMenuProtegido(IDbConnection db, int idMenu, IDbTransaction? trans = null)
        {
            const string sql = @"
                SELECT TOP 1
                    ISNULL(IDMENUPADRE, 0) AS IdMenuPadre,
                    NOMBREMENU AS NombreMenu,
                    RUTAMENU AS RutaMenu
                FROM EDECLARA_MENUS
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
                FROM EDECLARA_ROLES
                WHERE IDROL = @idRol";

            return await db.QuerySingleOrDefaultAsync<Rol>(sql, new { idRol }, trans);
        }

        private static async Task<Menu?> ObtenerMenuPorIdAsync(IDbConnection db, int idMenu, IDbTransaction? trans = null)
        {
            const string sql = @"
                SELECT TOP 1 IDMENU, IDMENUPADRE, NOMBREMENU, ESTADOMENU, RUTAMENU, ICONOMENU
                FROM EDECLARA_MENUS
                WHERE IDMENU = @idMenu";

            return await db.QuerySingleOrDefaultAsync<Menu>(sql, new { idMenu }, trans);
        }

        private static async Task<List<Menu>> ObtenerMenusRelacionadosAsync(IDbConnection db, int idMenu, IDbTransaction? trans = null)
        {
            const string sql = @"
                SELECT IDMENU, IDMENUPADRE, NOMBREMENU, ESTADOMENU, RUTAMENU, ICONOMENU
                FROM EDECLARA_MENUS
                WHERE IDMENU = @idMenu OR IDMENUPADRE = @idMenu
                ORDER BY IDMENU";

            return (await db.QueryAsync<Menu>(sql, new { idMenu }, trans)).ToList();
        }

        private static async Task<List<RolMenuSnapshot>> ObtenerMenusRolAsync(IDbConnection db, int idRol, IDbTransaction? trans = null)
        {
            const string sql = @"
                SELECT m.IDMENU AS IdMenu, m.NOMBREMENU AS NombreMenu, m.RUTAMENU AS RutaMenu
                FROM EDECLARA_ROL_MENU rm
                INNER JOIN EDECLARA_MENUS m ON m.IDMENU = rm.IDMENU
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
                SELECT DISTINCT m.IDMENU, m.NOMBREMENU, m.RUTAMENU, m.ICONOMENU, m.IDMENUPADRE, m.ESTADOMENU 
                FROM EDECLARA_MENUS m
                INNER JOIN EDECLARA_ROL_MENU rm ON m.IDMENU = rm.IDMENU
                INNER JOIN EDECLARA_ROLES r ON rm.IDROL = r.IDROL
                WHERE r.IDTIPOUSUARIO = @idTipoUsuario 
                AND m.ESTADOMENU = 1 
                AND r.ESTADOROL = 1
                ORDER BY m.IDMENUPADRE ASC, m.NOMBREMENU ASC";

            return (await db.QueryAsync<Menu>(sql, new { idTipoUsuario })).ToList();
        }

        public async Task<List<Menu>> GetAllMenus()
        {
            using var db = Connection;
            const string sql = "SELECT * FROM EDECLARA_MENUS WHERE ESTADOMENU = 1 ORDER BY ISNULL(IDMENUPADRE, 0), NOMBREMENU";
            return (await db.QueryAsync<Menu>(sql)).ToList();
        }

        public async Task<List<Menu>> GetMenusByRolId(int idRol)
        {
            using var db = Connection;
            const string sql = @"
                SELECT m.* FROM EDECLARA_MENUS m
                INNER JOIN EDECLARA_ROL_MENU rm ON m.IDMENU = rm.IDMENU
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
                FROM EDECLARA_ROLES r 
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

                const string sqlRol = @"INSERT INTO EDECLARA_ROLES (DESCRIPCIONROL, IDTIPOUSUARIO, ESTADOROL) 
                                        VALUES (@nombrePerfil, @nuevoIdTipo, 1);
                                        SELECT CAST(SCOPE_IDENTITY() as int);";

                int nuevoIdRol = await db.ExecuteScalarAsync<int>(sqlRol, new { nombrePerfil, nuevoIdTipo }, trans);

                var tipoCreado = await ObtenerTipoUsuarioPorIdAsync(db, nuevoIdTipo, trans);
                var rolCreado = await ObtenerRolPorIdAsync(db, nuevoIdRol, trans);

                trans.Commit();

                var detalleTipo = CrearDetalleAuditoria("TipoUsuario", "TIPOUSUARIO", new { IdTipoUsuario = nuevoIdTipo });
                detalleTipo["Origen"] = "CrearTipoYRolSimultaneo";
                await RegistrarAuditoriaAsync("CREAR", null, SnapshotTipoUsuario(tipoCreado), detalleTipo);

                var detalleRol = CrearDetalleAuditoria("Rol", "EDECLARA_ROLES", new { IdRol = nuevoIdRol });
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

                await db.ExecuteAsync("DELETE FROM EDECLARA_ROL_MENU WHERE IDROL = @idRol", new { idRol }, trans);

                if (idMenus != null && idMenus.Any())
                {
                    const string sqlIns = "INSERT INTO EDECLARA_ROL_MENU (IDROL, IDMENU) VALUES (@idRol, @idMenu)";
                    var parametros = idMenus.Select(mId => new { idRol, idMenu = mId });
                    await db.ExecuteAsync(sqlIns, parametros, trans);
                }

                var menusNuevos = await ObtenerMenusRolAsync(db, idRol, trans);

                trans.Commit();

                var detalle = CrearDetalleAuditoria("RolMenu", "EDECLARA_ROL_MENU", new { IdRol = idRol });
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
                    UPDATE EDECLARA_ROLES
                    SET DESCRIPCIONROL = @DescripcionRol,
                        IDTIPOUSUARIO = @IdTipoUsuario
                    WHERE IDROL = @IdRol";

                var actualizado = await db.ExecuteAsync(sqlUpdate, rol) > 0;
                if (!actualizado)
                {
                    return false;
                }

                var rolNuevo = await ObtenerRolPorIdAsync(db, rolPrevio.IdRol);
                var detalle = CrearDetalleAuditoria("Rol", "EDECLARA_ROLES", new { IdRol = rolPrevio.IdRol });
                await RegistrarAuditoriaAsync("MODIFICAR", SnapshotRol(rolPrevio), SnapshotRol(rolNuevo), detalle);
                return true;
            }

            const string sqlInsert = @"
                INSERT INTO EDECLARA_ROLES (DESCRIPCIONROL, IDTIPOUSUARIO, ESTADOROL)
                VALUES (@DescripcionRol, @IdTipoUsuario, 1);
                SELECT CAST(SCOPE_IDENTITY() as int);";

            var nuevoIdRol = await db.ExecuteScalarAsync<int>(sqlInsert, new
            {
                rol.DescripcionRol,
                rol.IdTipoUsuario
            });

            var rolCreado = await ObtenerRolPorIdAsync(db, nuevoIdRol);
            var detalleCreacion = CrearDetalleAuditoria("Rol", "EDECLARA_ROLES", new { IdRol = nuevoIdRol });
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
                    UPDATE EDECLARA_MENUS
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
                var detalle = CrearDetalleAuditoria("Menu", "EDECLARA_MENUS", new { IdMenu = menu.IdMenu });
                await RegistrarAuditoriaAsync("MODIFICAR", SnapshotMenu(menuPrevio), SnapshotMenu(menuNuevo), detalle);
                return true;
            }

            const string sqlInsert = @"
                INSERT INTO EDECLARA_MENUS (NOMBREMENU, RUTAMENU, ICONOMENU, IDMENUPADRE, ESTADOMENU)
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
            var detalleCreacion = CrearDetalleAuditoria("Menu", "EDECLARA_MENUS", new { IdMenu = nuevoIdMenu });
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
                "UPDATE EDECLARA_ROLES SET ESTADOROL = 0 WHERE IDROL = @idRol",
                new { idRol }) > 0;

            if (!eliminado)
            {
                return false;
            }

            var rolNuevo = await ObtenerRolPorIdAsync(db, idRol);
            var detalle = CrearDetalleAuditoria("Rol", "EDECLARA_ROLES", new { IdRol = idRol });
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
                await db.ExecuteAsync("UPDATE EDECLARA_MENUS SET ESTADOMENU = 0 WHERE IDMENU = @idMenu OR IDMENUPADRE = @idMenu", new { idMenu }, trans);

                var menusNuevos = await ObtenerMenusRelacionadosAsync(db, idMenu, trans);

                trans.Commit();

                var detalle = CrearDetalleAuditoria("Menu", "EDECLARA_MENUS", new { IdMenu = idMenu });
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

        #endregion
    }
}
