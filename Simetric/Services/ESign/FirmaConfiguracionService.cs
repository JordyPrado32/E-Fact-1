using Dapper;
using Microsoft.Data.SqlClient;
using Simetric.Services;

namespace Simetric.Services.ESign;

public sealed class FirmaConfiguracionService
{
    private readonly string _connectionString;
    private readonly EmisorCertificadoProtector _certificadoProtector;
    private readonly FirmaPathResolver _firmaPathResolver;
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaEnsured;

    public FirmaConfiguracionService(
        IConfiguration configuration,
        EmisorCertificadoProtector certificadoProtector,
        FirmaPathResolver firmaPathResolver)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new ArgumentNullException("La cadena de conexión no existe.");
        _certificadoProtector = certificadoProtector;
        _firmaPathResolver = firmaPathResolver;
    }

    public async Task<FirmaConfiguracionCuenta?> ObtenerAsync(int usuarioId)
    {
        await EnsureSchemaAsync();
        var cuentaId = await ObtenerIdCuentaAsync(usuarioId);
        if (cuentaId is null)
            return null;

        using var connection = new SqlConnection(_connectionString);
        var configuracion = await connection.QueryFirstOrDefaultAsync<ConfiguracionPersistida>(
            """
            SELECT PATH_CERTIFICADO AS PathCertificado,
                   CASE WHEN CLAVE_CERTIFICADO IS NULL OR LTRIM(RTRIM(CLAVE_CERTIFICADO)) = '' THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS TieneClave
            FROM dbo.ESIGN_FIRMA_CONFIGURACION
            WHERE ID_USUARIO = @CuentaId;
            """,
            new { CuentaId = cuentaId });

        if (configuracion is not null)
            return new(configuracion.PathCertificado, configuracion.TieneClave, true);

        var legado = await connection.QueryFirstOrDefaultAsync<ConfiguracionPersistida>(
            """
            SELECT TOP 1 pathCertificado AS PathCertificado,
                   CASE WHEN claveCertificado IS NULL OR LTRIM(RTRIM(claveCertificado)) = '' THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS TieneClave
            FROM EMISOR
            WHERE id_usuario = @CuentaId AND ESTADO = 1 AND es_emisor_sistema = 0
            ORDER BY codigo DESC;
            """,
            new { CuentaId = cuentaId });

        return legado is null ? null : new(legado.PathCertificado, legado.TieneClave, false);
    }

    public async Task<FirmaCredencialesCuenta?> ObtenerCredencialesAsync(int usuarioId)
    {
        await EnsureSchemaAsync();
        var cuentaId = await ObtenerIdCuentaAsync(usuarioId);
        if (cuentaId is null)
            return null;

        using var connection = new SqlConnection(_connectionString);
        var configuracion = await connection.QueryFirstOrDefaultAsync<CredencialesPersistidas>(
            """
            SELECT PATH_CERTIFICADO AS PathCertificado, CLAVE_CERTIFICADO AS ClaveCertificado
            FROM dbo.ESIGN_FIRMA_CONFIGURACION
            WHERE ID_USUARIO = @CuentaId;
            """,
            new { CuentaId = cuentaId });

        if (configuracion is null)
        {
            configuracion = await connection.QueryFirstOrDefaultAsync<CredencialesPersistidas>(
                """
                SELECT TOP 1 pathCertificado AS PathCertificado, claveCertificado AS ClaveCertificado
                FROM EMISOR
                WHERE id_usuario = @CuentaId AND ESTADO = 1 AND es_emisor_sistema = 0
                ORDER BY codigo DESC;
                """,
                new { CuentaId = cuentaId });
        }

        if (configuracion is null || string.IsNullOrWhiteSpace(configuracion.PathCertificado))
            return null;

        return new(
            configuracion.PathCertificado,
            _certificadoProtector.DesprotegerClave(configuracion.ClaveCertificado));
    }

    public async Task GuardarAsync(int usuarioId, string pathCertificado, string? claveCertificado)
    {
        if (string.IsNullOrWhiteSpace(pathCertificado))
            throw new ArgumentException("El archivo .p12 es requerido.", nameof(pathCertificado));

        var rutaNormalizada = FirmaPathResolver.NormalizarRutaRelativa(pathCertificado);
        if (string.IsNullOrWhiteSpace(rutaNormalizada) ||
            _firmaPathResolver.ResolverRutaExistente(rutaNormalizada) is null)
        {
            throw new FileNotFoundException("No se encontró el archivo .p12 subido.", pathCertificado);
        }

        pathCertificado = rutaNormalizada;

        await EnsureSchemaAsync();
        var cuentaId = await ObtenerIdCuentaAsync(usuarioId)
            ?? throw new InvalidOperationException("No se encontró la cuenta del usuario.");

        using var connection = new SqlConnection(_connectionString);
        var existente = await connection.QueryFirstOrDefaultAsync<CredencialesPersistidas>(
            """
            SELECT PATH_CERTIFICADO AS PathCertificado, CLAVE_CERTIFICADO AS ClaveCertificado
            FROM dbo.ESIGN_FIRMA_CONFIGURACION
            WHERE ID_USUARIO = @CuentaId;
            """,
            new { CuentaId = cuentaId });

        var clavePersistida = existente?.ClaveCertificado;
        if (string.IsNullOrWhiteSpace(claveCertificado) && string.IsNullOrWhiteSpace(clavePersistida))
        {
            var legado = await connection.QueryFirstOrDefaultAsync<CredencialesPersistidas>(
                """
                SELECT TOP 1 claveCertificado AS ClaveCertificado
                FROM EMISOR
                WHERE id_usuario = @CuentaId AND ESTADO = 1 AND es_emisor_sistema = 0
                ORDER BY codigo DESC;
                """,
                new { CuentaId = cuentaId });
            clavePersistida = legado?.ClaveCertificado;
        }

        if (!string.IsNullOrWhiteSpace(claveCertificado))
            clavePersistida = _certificadoProtector.ProtegerClave(claveCertificado);

        if (string.IsNullOrWhiteSpace(clavePersistida))
            throw new ArgumentException("La clave del archivo .p12 es requerida.", nameof(claveCertificado));

        if (existente is null)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO dbo.ESIGN_FIRMA_CONFIGURACION (ID_USUARIO, PATH_CERTIFICADO, CLAVE_CERTIFICADO)
                VALUES (@CuentaId, @PathCertificado, @ClaveCertificado);
                """,
                new { CuentaId = cuentaId, PathCertificado = pathCertificado.Trim(), ClaveCertificado = clavePersistida });
        }
        else
        {
            await connection.ExecuteAsync(
                """
                UPDATE dbo.ESIGN_FIRMA_CONFIGURACION
                SET PATH_CERTIFICADO = @PathCertificado,
                    CLAVE_CERTIFICADO = @ClaveCertificado,
                    FECHA_ACTUALIZACION = SYSUTCDATETIME()
                WHERE ID_USUARIO = @CuentaId;
                """,
                new { CuentaId = cuentaId, PathCertificado = pathCertificado.Trim(), ClaveCertificado = clavePersistida });
        }
    }

    private async Task<int?> ObtenerIdCuentaAsync(int usuarioId)
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<int?>(
            """
            SELECT CASE WHEN estadoAsociado = 1 AND idJefe IS NOT NULL THEN idJefe ELSE [IdUsuario] END
            FROM dbo.Usuarios
            WHERE [IdUsuario] = @UsuarioId;
            """,
            new { UsuarioId = usuarioId });
    }

    private async Task EnsureSchemaAsync()
    {
        if (_schemaEnsured)
            return;

        await SchemaLock.WaitAsync();
        try
        {
            if (_schemaEnsured)
                return;

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                """
                IF OBJECT_ID('dbo.ESIGN_FIRMA_CONFIGURACION', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.ESIGN_FIRMA_CONFIGURACION
                    (
                        ID_CONFIGURACION INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ESIGN_FIRMA_CONFIGURACION PRIMARY KEY,
                        ID_USUARIO INT NOT NULL,
                        PATH_CERTIFICADO NVARCHAR(250) NOT NULL,
                        CLAVE_CERTIFICADO NVARCHAR(MAX) NOT NULL,
                        FECHA_ACTUALIZACION DATETIME2 NOT NULL CONSTRAINT DF_ESIGN_FIRMA_CONFIGURACION_FECHA DEFAULT SYSUTCDATETIME()
                    );
                END;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ESIGN_FIRMA_CONFIGURACION_USUARIO' AND object_id = OBJECT_ID(N'dbo.ESIGN_FIRMA_CONFIGURACION'))
                    CREATE UNIQUE INDEX UX_ESIGN_FIRMA_CONFIGURACION_USUARIO ON dbo.ESIGN_FIRMA_CONFIGURACION (ID_USUARIO);
                """);
            _schemaEnsured = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    private sealed class ConfiguracionPersistida
    {
        public string? PathCertificado { get; set; }
        public bool TieneClave { get; set; }
    }

    private sealed class CredencialesPersistidas
    {
        public string? PathCertificado { get; set; }
        public string? ClaveCertificado { get; set; }
    }
}

public sealed record FirmaConfiguracionCuenta(
    string? PathCertificado,
    bool TieneClaveCertificado,
    bool EsConfiguracionCuenta);

public sealed record FirmaCredencialesCuenta(
    string PathCertificado,
    string? ClaveCertificado);
