using System;
using Microsoft.Data.SqlClient;

class Program
{
    static void Main()
    {
        string connStr = "Data Source=68.178.204.190,1433;Initial Catalog=db_aab800_numerica;User ID=numericasoft;Password=numericaecuador;TrustServerCertificate=True;";
        using (SqlConnection conn = new SqlConnection(connStr))
        {
            conn.Open();

            Console.WriteLine("=== INTERNAL USERS (IdTipoUsuario = 7) ===");
            string query = "SELECT IdUsuario, Nombres, Apellidos, TipoCliente FROM Usuarios WHERE IdTipoUsuario = 7";
            using (SqlCommand cmd = new SqlCommand(query, conn))
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    Console.WriteLine($"{reader["IdUsuario"]} | {reader["Nombres"]} | {reader["Apellidos"]} | TipoCliente: {reader["TipoCliente"]}");
                }
            }
        }
    }
}
