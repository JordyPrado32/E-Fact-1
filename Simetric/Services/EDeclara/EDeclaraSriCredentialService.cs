using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;

namespace Simetric.Services.EDeclara;

public sealed class EDeclaraSriCredentialService
{
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private readonly string _connectionString;
    private readonly IDataProtector _protector;
    private bool _schemaEnsured;

    public EDeclaraSriCredentialService(
        IConfiguration configuration,
        IDataProtectionProvider dataProtectionProvider)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'DefaultConnection'.");
        _protector = dataProtectionProvider.CreateProtector("Simetric.EDeclara.ClaveSri.v1");
    }

    public async Task<bool> TieneClaveAsync(int userId)
    {
        await EnsureSchemaAsync();
        await using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM EDECLARA_CREDENCIALES_SRI WHERE IDUSUARIO = @userId AND CLAVEPROTEGIDA <> ''",
            new { userId }) > 0;
    }

    public async Task GuardarClaveAsync(int userId, string clave)
    {
        if (userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId));

        if (string.IsNullOrWhiteSpace(clave))
            throw new ArgumentException("La clave SRI es obligatoria.", nameof(clave));

        await EnsureSchemaAsync();
        var claveProtegida = _protector.Protect(clave);

        await using var connection = new SqlConnection(_connectionString);
        const string sql = """
            UPDATE EDECLARA_CREDENCIALES_SRI
               SET CLAVEPROTEGIDA = @claveProtegida,
                   FECHAACTUALIZACION = SYSUTCDATETIME()
             WHERE IDUSUARIO = @userId;

            IF @@ROWCOUNT = 0
                INSERT INTO EDECLARA_CREDENCIALES_SRI (IDUSUARIO, CLAVEPROTEGIDA, FECHAACTUALIZACION)
                VALUES (@userId, @claveProtegida, SYSUTCDATETIME());
            """;

        await connection.ExecuteAsync(sql, new { userId, claveProtegida });
    }

    public async Task<string?> ObtenerClaveAsync(int userId)
    {
        await EnsureSchemaAsync();
        await using var connection = new SqlConnection(_connectionString);
        var claveProtegida = await connection.QuerySingleOrDefaultAsync<string>(
            "SELECT CLAVEPROTEGIDA FROM EDECLARA_CREDENCIALES_SRI WHERE IDUSUARIO = @userId",
            new { userId });

        if (string.IsNullOrWhiteSpace(claveProtegida))
            return null;

        try
        {
            return _protector.Unprotect(claveProtegida);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> TieneClaveContribuyenteAsync(int userId, int contribuyenteId)
    {
        await EnsureSchemaAsync();
        await using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM EDECLARA_CONTRIBUYENTE_CREDENCIALES_SRI WHERE IDUSUARIO = @userId AND IDCONTRIBUYENTE = @contribuyenteId AND CLAVEPROTEGIDA <> ''",
            new { userId, contribuyenteId }) > 0;
    }

    public async Task<HashSet<int>> ObtenerContribuyentesConClaveAsync(int userId)
    {
        await EnsureSchemaAsync();
        await using var connection = new SqlConnection(_connectionString);
        var ids = await connection.QueryAsync<int>(
            "SELECT IDCONTRIBUYENTE FROM EDECLARA_CONTRIBUYENTE_CREDENCIALES_SRI WHERE IDUSUARIO = @userId AND CLAVEPROTEGIDA <> ''",
            new { userId });
        return ids.ToHashSet();
    }

    public async Task GuardarClaveContribuyenteAsync(int userId, int contribuyenteId, string clave)
    {
        if (userId <= 0 || contribuyenteId <= 0)
            throw new ArgumentOutOfRangeException(nameof(contribuyenteId));
        if (string.IsNullOrWhiteSpace(clave))
            throw new ArgumentException("La clave SRI es obligatoria.", nameof(clave));

        await EnsureSchemaAsync();
        var claveProtegida = _protector.Protect(clave);
        await using var connection = new SqlConnection(_connectionString);
        const string sql = """
            UPDATE EDECLARA_CONTRIBUYENTE_CREDENCIALES_SRI
               SET CLAVEPROTEGIDA = @claveProtegida, FECHAACTUALIZACION = SYSUTCDATETIME()
             WHERE IDUSUARIO = @userId AND IDCONTRIBUYENTE = @contribuyenteId;

            IF @@ROWCOUNT = 0
                INSERT INTO EDECLARA_CONTRIBUYENTE_CREDENCIALES_SRI
                    (IDUSUARIO, IDCONTRIBUYENTE, CLAVEPROTEGIDA, FECHAACTUALIZACION)
                VALUES (@userId, @contribuyenteId, @claveProtegida, SYSUTCDATETIME());
            """;
        await connection.ExecuteAsync(sql, new { userId, contribuyenteId, claveProtegida });
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

            await using var connection = new SqlConnection(_connectionString);
            const string sql = """
                IF OBJECT_ID('dbo.EDECLARA_CREDENCIALES_SRI', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.EDECLARA_CREDENCIALES_SRI
                    (
                        IDUSUARIO INT NOT NULL CONSTRAINT PK_EDECLARA_CREDENCIALES_SRI PRIMARY KEY,
                        CLAVEPROTEGIDA NVARCHAR(MAX) NOT NULL,
                        FECHAACTUALIZACION DATETIME2 NOT NULL
                            CONSTRAINT DF_EDECLARA_CREDENCIALES_SRI_FECHA DEFAULT SYSUTCDATETIME()
                    );
                END

                IF OBJECT_ID('dbo.EDECLARA_CONTRIBUYENTE_CREDENCIALES_SRI', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.EDECLARA_CONTRIBUYENTE_CREDENCIALES_SRI
                    (
                        IDUSUARIO INT NOT NULL,
                        IDCONTRIBUYENTE INT NOT NULL,
                        CLAVEPROTEGIDA NVARCHAR(MAX) NOT NULL,
                        FECHAACTUALIZACION DATETIME2 NOT NULL
                            CONSTRAINT DF_EDECLARA_CONTRIBUYENTE_CREDENCIALES_SRI_FECHA DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT PK_EDECLARA_CONTRIBUYENTE_CREDENCIALES_SRI
                            PRIMARY KEY (IDUSUARIO, IDCONTRIBUYENTE)
                    );
                END
                """;

            await connection.ExecuteAsync(sql);
            _schemaEnsured = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }
}
