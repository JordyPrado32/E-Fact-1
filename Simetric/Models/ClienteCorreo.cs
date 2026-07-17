namespace Simetric.Models
{
    public class ClienteCorreo
    {
        public int Id { get; set; }
        public int CodCliente { get; set; }
        public string Correo { get; set; } = string.Empty;
        public bool Estado { get; set; } = true;
    }
}
