namespace Simetric.Components.Helpers
{
    public static class SecurityHelper
    {
        // Usa este método cuando crees un usuario nuevo o cambies la clave
        public static string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        // Se usa en el Login para comparar lo que escribe el usuario con la DB
        public static bool VerifyPassword(string password, string storedHash)
        {
            if (string.IsNullOrEmpty(storedHash)) return false;

            try
            {
                return BCrypt.Net.BCrypt.Verify(password, storedHash);
            }
            catch
            {
                // Compatibilidad con claves temporales o registros legados que quedaron en texto plano.
                return string.Equals(password, storedHash, StringComparison.Ordinal);
            }
        }
    }
}
