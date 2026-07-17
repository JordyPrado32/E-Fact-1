using System.ComponentModel.DataAnnotations;

namespace Simetric.Models // <--- Agrega esto para organizar tu código
{
    public class RecoverPasswordModel
    {
        [Required(ErrorMessage = "El correo electrónico es obligatorio.")]
        [EmailAddress(ErrorMessage = "Por favor, ingresa un correo electrónico válido.")]
        [StringLength(100, ErrorMessage = "El correo es demasiado largo.")]
        public string Email { get; set; } = string.Empty;
    }
}