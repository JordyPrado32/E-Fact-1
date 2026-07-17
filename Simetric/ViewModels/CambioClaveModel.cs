using System.ComponentModel.DataAnnotations;

namespace Simetric.ViewModels
{
    public class CambioClaveModel
    {
        [Required(ErrorMessage = "El codigo de acceso es obligatorio")]
        public string ClaveActual { get; set; } = string.Empty;

        [Required(ErrorMessage = "La nueva clave es obligatoria")]
        [MinLength(10, ErrorMessage = "La clave debe tener al menos 10 caracteres")]
        [RegularExpression(
            @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{10,}$",
            ErrorMessage = "La clave debe tener: Mayúscula, Minúscula, Número y un Símbolo (ej: #@$)"
        )]
        public string NuevaClave { get; set; } = string.Empty;

        [Required(ErrorMessage = "Debe confirmar la clave")]
        [Compare("NuevaClave", ErrorMessage = "Las contraseñas no coinciden")]
        public string ConfirmarClave { get; set; } = string.Empty;
    }
}
