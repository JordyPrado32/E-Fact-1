using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Simetric.Services;

public sealed class SqlAuditService
{
    private readonly string? _connectionString;

    public SqlAuditService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection");
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_connectionString);

    public async Task RegistrarAuditoriaAsync(
        int? idUsuario,
        string accion,
        object? valoresPreviosObj,
        object? valorNuevoObj,
        object? detallesObj,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return;
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandTimeout = 3;
        command.CommandText = """
            INSERT INTO [dbo].[Auditoria]
                ([IdUsuario], [Fecha], [Accion], [ValoresPrevios], [ValorNuevo], [Detalles])
            VALUES
                (@IdUsuario, @Fecha, @Accion, @ValoresPrevios, @ValorNuevo, @Detalles);
            """;

        command.Parameters.Add(new SqlParameter("@IdUsuario", SqlDbType.Int)
        {
            Value = idUsuario.HasValue ? idUsuario.Value : DBNull.Value
        });
        command.Parameters.Add(new SqlParameter("@Fecha", SqlDbType.DateTime)
        {
            Value = DateTime.Now
        });
        command.Parameters.Add(new SqlParameter("@Accion", SqlDbType.NVarChar, 255)
        {
            Value = accion
        });
        command.Parameters.Add(new SqlParameter("@ValoresPrevios", SqlDbType.NVarChar, -1)
        {
            Value = SerializeForStorage(valoresPreviosObj)
        });
        command.Parameters.Add(new SqlParameter("@ValorNuevo", SqlDbType.NVarChar, -1)
        {
            Value = SerializeForStorage(valorNuevoObj)
        });
        command.Parameters.Add(new SqlParameter("@Detalles", SqlDbType.NVarChar, -1)
        {
            Value = SerializeForStorage(detallesObj)
        });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static object SerializeForStorage(object? value)
    {
        return value == null
            ? DBNull.Value
            : JsonSerializer.Serialize(value);
    }
}
