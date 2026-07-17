using System.ComponentModel.DataAnnotations;

namespace Simetric.ViewModels
{
    public class RegistroViewModel
    {
        [Required(ErrorMessage = "La razón social es obligatoria")]
        [MinLength(3, ErrorMessage = "El nombre de la empresa es muy corto")]
        public string NombreEmpresa { get; set; } = string.Empty;

        [Required(ErrorMessage = "La dirección es obligatoria")]
        public string DireccionEmpresa { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingrese sus nombres")]
        [RegularExpression(@"^[a-zA-ZáéíóúÁÉÍÓÚñÑ]{3,}\s+[a-zA-ZáéíóúÁÉÍÓÚñÑ]{3,}.*$",
            ErrorMessage = "Debe ingresar al menos dos nombres (mínimo 3 letras cada uno)")]
        public string Nombres { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingrese sus apellidos")]
        [RegularExpression(@"^[a-zA-ZáéíóúÁÉÍÓÚñÑ]{1,}(\s+[a-zA-ZáéíóúÁÉÍÓÚñÑ]{1,})+$",
            ErrorMessage = "Debe ingresar al menos dos apellidos")]
        public string Apellidos { get; set; } = string.Empty;

        [Required(ErrorMessage = "El celular es obligatorio")]
        [RegularExpression(@"^[0-9]{10}$", ErrorMessage = "El celular debe tener 10 dígitos numéricos")]
        public string Celular { get; set; } = string.Empty;

        [Required(ErrorMessage = "El email es obligatorio")]
        [EmailAddress(ErrorMessage = "Formato de email inválido")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "La contraseña es obligatoria")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$",
            ErrorMessage = "La contraseña debe tener al menos 8 caracteres, una mayúscula, una minúscula, un número y un carácter especial.")]
        public string Password { get; set; } = string.Empty;

        // Auxiliares
        public int IdTipoIdentificacion { get; set; }
        public string Identificacion { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = "Avatar-Boy.png";

        [Required(ErrorMessage = "Debe seleccionar el tipo de cliente")]
        public int TipoCliente { get; set; } = 1;
    }
}
