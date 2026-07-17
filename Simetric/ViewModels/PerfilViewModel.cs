using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Simetric.ViewModels
{
    public class PerfilViewModel : IValidatableObject
    {
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(60, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 60 caracteres.")]
        [SoloLetrasYEspacios(ErrorMessage = "El nombre solo puede contener letras y espacios.")]
        public string Nombres { get; set; } = string.Empty;

        [StringLength(60, ErrorMessage = "El apellido no puede superar los 60 caracteres.")]
        [SoloLetrasYEspacios(ErrorMessage = "El apellido solo puede contener letras y espacios.")]
        public string Apellidos { get; set; } = string.Empty;

        [StringLength(150, ErrorMessage = "La razon social no puede superar los 150 caracteres.")]
        public string? NombreEmpresa { get; set; }

        [Required(ErrorMessage = "El correo electrónico es obligatorio.")]
        [EmailAddress(ErrorMessage = "El correo electrónico no es válido.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "La fecha de nacimiento es obligatoria.")]
        [FechaNacimientoValida]
        public DateTime? FechaNacimiento { get; set; }

        public string? AvatarUrl { get; set; }

        [StringLength(100, MinimumLength = 8, ErrorMessage = "La contraseña debe tener entre 8 y 100 caracteres.")]
        [DataType(DataType.Password)]
        public string? NuevaPassword { get; set; }

        [Compare("NuevaPassword", ErrorMessage = "Las contraseñas no coinciden.")]
        [DataType(DataType.Password)]
        public string? ConfirmarPassword { get; set; }

        public byte[]? FotoByte { get; set; }

        public string? Identificacion { get; set; }

        public int? TipoCliente { get; set; }

        public int? IdTipoIdentificacion { get; set; }

        [StringLength(200, ErrorMessage = "La direccion no puede superar los 200 caracteres.")]
        public string? DireccionEmpresa { get; set; }

        [RegularExpression(@"^09\d{8}$", ErrorMessage = "El celular debe tener 10 digitos y empezar con 09.")]
        public string? Celular { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (TipoCliente == 2)
            {
                if (string.IsNullOrWhiteSpace(NombreEmpresa))
                    yield return new ValidationResult("La razon social es obligatoria.", new[] { nameof(NombreEmpresa) });
            }
            else if (string.IsNullOrWhiteSpace(Apellidos) || Apellidos.Trim().Length < 2)
            {
                yield return new ValidationResult("El apellido debe tener al menos 2 caracteres.", new[] { nameof(Apellidos) });
            }

            if (TipoCliente is null or 0)
                yield return new ValidationResult("El tipo de cliente es obligatorio.", new[] { nameof(TipoCliente) });
            if (IdTipoIdentificacion is null or 0)
                yield return new ValidationResult("El tipo de identificacion es obligatorio.", new[] { nameof(IdTipoIdentificacion) });
            if (string.IsNullOrWhiteSpace(Identificacion))
                yield return new ValidationResult("La identificacion es obligatoria.", new[] { nameof(Identificacion) });
            if (string.IsNullOrWhiteSpace(DireccionEmpresa))
                yield return new ValidationResult("La direccion es obligatoria.", new[] { nameof(DireccionEmpresa) });
            if (string.IsNullOrWhiteSpace(Celular))
                yield return new ValidationResult("El celular es obligatorio.", new[] { nameof(Celular) });
        }
    }

    public sealed class SoloLetrasYEspaciosAttribute : ValidationAttribute
    {
        private static readonly Regex Patron = new(
            @"^[A-Za-zÁÉÍÓÚáéíóúÑñÜü\s]+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is null)
            {
                return ValidationResult.Success;
            }

            var texto = value.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(texto))
            {
                return ValidationResult.Success;
            }

            return Patron.IsMatch(texto)
                ? ValidationResult.Success
                : new ValidationResult(ErrorMessage ?? "Este campo solo puede contener letras y espacios.");
        }
    }

    public sealed class FechaNacimientoValidaAttribute : ValidationAttribute
    {
        private static readonly DateTime FechaMinimaPermitida = new(1920, 1, 1);

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is null)
            {
                return ValidationResult.Success;
            }

            if (value is not DateTime fechaNacimiento)
            {
                return new ValidationResult("La fecha ingresada no es válida.");
            }

            var fecha = fechaNacimiento.Date;
            var hoy = DateTime.Today;

            if (fecha > hoy)
            {
                return new ValidationResult("La fecha de nacimiento no puede ser futura.");
            }

            if (fecha < FechaMinimaPermitida)
            {
                return new ValidationResult("La fecha de nacimiento no puede ser anterior al 01/01/1920.");
            }

            if (fecha > hoy.AddYears(-18))
            {
                return new ValidationResult("Debes ser mayor de 18 años.");
            }

            return ValidationResult.Success;
        }
    }
}
