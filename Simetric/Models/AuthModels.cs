namespace Simetric.Auth
{
    public class LoginModel
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class LoginResult
    {
        public bool Exito { get; set; }
        public string Mensaje { get; set; } = string.Empty;
        public bool RequiereCambioClave { get; set; }
        public Simetric.Models.Usuario? Usuario { get; set; }

        public static LoginResult Fallido(string mensaje) => new() { Exito = false, Mensaje = mensaje };
        public static LoginResult Exitoso(Simetric.Models.Usuario usuario) => new() { Exito = true, Usuario = usuario };
    }
}