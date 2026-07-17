using System.ComponentModel.DataAnnotations;

namespace Simetric.Models
{
    public class UserSession
    {
        [Key]
        public int IdUsuario { get; set; }
        public string Nombre { get; set; } = "";
        public string Email { get; set; } = "";
        public string Rol { get; set; } = "";
        public string Avatar { get; set; } = "";
    }
}
