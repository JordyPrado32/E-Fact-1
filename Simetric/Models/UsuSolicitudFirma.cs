using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;

namespace Simetric.Models
{
    [Table("USU_SOLICITUD_FIRMA")]
    public class UsuSolicitudFirma : IValidatableObject
    {
        [Key]
        [Column("SOL_ID")]
        public int SolId { get; set; }

        [Column("SOL_ID_USUARIO_CLIENTE")]
        public int SolIdUsuarioCliente { get; set; }

        [Column("SOL_ID_ESTADO_NUMERICA")]
        public int SolIdEstadoNumerica { get; set; }

        [Column("SOL_ID_ESTADO_UANATACA")]
        public int? SolIdEstadoUanataca { get; set; }

        [Required(ErrorMessage = "Seleccione el tipo de documento.")]
        [StringLength(20, ErrorMessage = "El tipo de documento no puede exceder 20 caracteres.")]
        [Column("SOL_TIPO_IDENTIFICACION")]
        public string SolTipoIdentificacion { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingrese la identificacion.")]
        [StringLength(20, ErrorMessage = "La identificacion no puede exceder 20 caracteres.")]
        [Column("SOL_IDENTIFICACION")]
        public string SolIdentificacion { get; set; } = string.Empty;

        [StringLength(10, ErrorMessage = "El codigo dactilar debe tener 10 caracteres.")]
        [Column("SOL_CODIGO_DACTILAR")]
        public string? SolCodigoDactilar { get; set; }

        [Required(ErrorMessage = "Ingrese los nombres.")]
        [StringLength(100, ErrorMessage = "Los nombres no pueden exceder 100 caracteres.")]
        [Column("SOL_NOMBRES")]
        public string SolNombres { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingrese el primer apellido.")]
        [StringLength(100, ErrorMessage = "El primer apellido no puede exceder 100 caracteres.")]
        [Column("SOL_PRIMER_APELLIDO")]
        public string SolPrimerApellido { get; set; } = string.Empty;

        [StringLength(100, ErrorMessage = "El segundo apellido no puede exceder 100 caracteres.")]
        [Column("SOL_SEGUNDO_APELLIDO")]
        public string? SolSegundoApellido { get; set; }

        [Column("SOL_FECHA_NACIMIENTO")]
        public DateTime SolFechaNacimiento { get; set; }

        [Required(ErrorMessage = "Seleccione la nacionalidad.")]
        [StringLength(100, ErrorMessage = "La nacionalidad no puede exceder 100 caracteres.")]
        [Column("SOL_NACIONALIDAD")]
        public string SolNacionalidad { get; set; } = string.Empty;

        [Required(ErrorMessage = "Seleccione el sexo.")]
        [StringLength(20, ErrorMessage = "El sexo no puede exceder 20 caracteres.")]
        [Column("SOL_SEXO")]
        public string SolSexo { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingrese el celular principal.")]
        [StringLength(20, ErrorMessage = "El celular principal no puede exceder 20 caracteres.")]
        [Column("SOL_TELEFONO_1")]
        public string SolTelefono1 { get; set; } = string.Empty;

        [StringLength(20, ErrorMessage = "El telefono secundario no puede exceder 20 caracteres.")]
        [Column("SOL_TELEFONO_2")]
        public string? SolTelefono2 { get; set; }

        [Required(ErrorMessage = "Ingrese el correo principal.")]
        [EmailAddress(ErrorMessage = "El correo principal no tiene un formato valido.")]
        [StringLength(150, ErrorMessage = "El correo principal no puede exceder 150 caracteres.")]
        [Column("SOL_CORREO_1")]
        public string SolCorreo1 { get; set; } = string.Empty;

        [StringLength(150, ErrorMessage = "El correo secundario no puede exceder 150 caracteres.")]
        [Column("SOL_CORREO_2")]
        public string? SolCorreo2 { get; set; }

        [Column("SOL_TIENE_RUC")]
        public bool SolTieneRuc { get; set; }

        [StringLength(13, ErrorMessage = "El RUC no puede exceder 13 digitos.")]
        [Column("SOL_NRO_RUC")]
        public string? SolNroRuc { get; set; }

        [Required(ErrorMessage = "Ingrese la provincia.")]
        [StringLength(100, ErrorMessage = "La provincia no puede exceder 100 caracteres.")]
        [Column("SOL_PROVINCIA")]
        public string SolProvincia { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingrese el canton.")]
        [StringLength(100, ErrorMessage = "El canton no puede exceder 100 caracteres.")]
        [Column("SOL_CANTON")]
        public string SolCanton { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingrese la direccion.")]
        [StringLength(300, ErrorMessage = "La direccion no puede exceder 300 caracteres.")]
        [Column("SOL_DIRECCION")]
        public string SolDireccion { get; set; } = string.Empty;

        [Column("SOL_REQUIERE_VALIDACION")]
        public bool SolRequiereValidacion { get; set; }

        [Column("SOL_ES_MAYOR_65")]
        public bool SolEsMayor65 { get; set; }

        [Required(ErrorMessage = "Seleccione el tipo de firma requerido.")]
        [StringLength(50, ErrorMessage = "El tipo de firma no puede exceder 50 caracteres.")]
        [Column("SOLFORMATOFIRMA")]
        public string SolFormatoFirma { get; set; } = string.Empty;

        [Required(ErrorMessage = "Seleccione la vigencia requerida.")]
        [StringLength(30, ErrorMessage = "La vigencia no puede exceder 30 caracteres.")]
        [Column("SOLTIEMPOVIGENCIA")]
        public string SolVigencia { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "La observacion general no puede exceder 500 caracteres.")]
        [Column("SOL_OBSERVACION_GENERAL")]
        public string? SolObservacionGeneral { get; set; }

        [Column("SOL_FECHA_SOLICITUD")]
        public DateTime SolFechaSolicitud { get; set; } = DateTime.Now;

        [Column("SOL_FECHA_REVISION")]
        public DateTime? SolFechaRevision { get; set; }

        [Column("SOL_FECHA_APROBACION")]
        public DateTime? SolFechaAprobacion { get; set; }

        [Column("SOL_FECHA_CANCELACION")]
        public DateTime? SolFechaCancelacion { get; set; }

        [Column("SOL_FECHA_ACTUALIZACION")]
        public DateTime? SolFechaActualizacion { get; set; }

        [Column("SOL_ID_USUARIO_SOPORTE")]
        public int? SolIdUsuarioSoporte { get; set; }

        [Required(ErrorMessage = "Seleccione si la solicitud corresponde a una persona natural o juridica.")]
        [StringLength(20, ErrorMessage = "La clasificacion no puede exceder 20 caracteres.")]
        public string SolTipoPersona { get; set; } = "NATURAL";

        [Column("SOL_ACTIVO")]
        public bool SolActivo { get; set; } = true;

        [ForeignKey("SolIdEstadoNumerica")]
        public virtual UsuEstadoFirma EstadoNumerica { get; set; } = null!;

        public virtual UsuEstadoFirma? EstadoUanataca { get; set; }
        public virtual ICollection<UsuSolicitudDocumento> Documentos { get; set; } = new List<UsuSolicitudDocumento>();
        public virtual ICollection<UsuSolicitudObservacion> Observaciones { get; set; } = new List<UsuSolicitudObservacion>();
        public virtual ICollection<UsuSolicitudEstadoHistorial> Historial { get; set; } = new List<UsuSolicitudEstadoHistorial>();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var tipoPersona = (SolTipoPersona ?? string.Empty).Trim().ToUpperInvariant();
            var tipoDocumento = (SolTipoIdentificacion ?? string.Empty).Trim().ToUpperInvariant();
            var identificacion = Regex.Replace((SolIdentificacion ?? string.Empty).Trim().ToUpperInvariant(), @"\s+", string.Empty);
            var codigoDactilar = Regex.Replace((SolCodigoDactilar ?? string.Empty).Trim().ToUpperInvariant(), @"\s+", string.Empty);
            var nombres = NormalizarEspacios(SolNombres);
            var primerApellido = NormalizarEspacios(SolPrimerApellido);
            var segundoApellido = NormalizarEspacios(SolSegundoApellido);
            var nacionalidad = NormalizarEspacios(SolNacionalidad);
            var sexo = (SolSexo ?? string.Empty).Trim().ToUpperInvariant();
            var telefonoPrincipal = SoloDigitos(SolTelefono1);
            var telefonoSecundario = SoloDigitos(SolTelefono2);
            var correoPrincipal = (SolCorreo1 ?? string.Empty).Trim().ToLowerInvariant();
            var correoSecundario = (SolCorreo2 ?? string.Empty).Trim().ToLowerInvariant();
            var requiereRuc = tipoPersona == "JURIDICA" || SolTieneRuc;
            var ruc = SoloDigitos(SolNroRuc);
            var provincia = NormalizarEspacios(SolProvincia);
            var canton = NormalizarEspacios(SolCanton);
            var direccion = NormalizarEspacios(SolDireccion);
            var formatoFirma = (SolFormatoFirma ?? string.Empty).Trim().ToUpperInvariant();
            var vigencia = (SolVigencia ?? string.Empty).Trim().ToUpperInvariant();

            if (!EsValorPermitido(tipoPersona, "NATURAL", "JURIDICA"))
            {
                yield return new ValidationResult(
                    "Seleccione si la solicitud es para persona natural o juridica.",
                    new[] { nameof(SolTipoPersona) });
            }

            if (!EsValorPermitido(tipoDocumento, "CEDULA", "PASAPORTE"))
            {
                yield return new ValidationResult(
                    "Seleccione un tipo de documento valido.",
                    new[] { nameof(SolTipoIdentificacion) });
            }

            if (tipoDocumento == "CEDULA")
            {
                if (!Regex.IsMatch(identificacion, @"^\d{10}$"))
                {
                    yield return new ValidationResult(
                        "La cedula debe tener exactamente 10 digitos.",
                        new[] { nameof(SolIdentificacion) });
                }
                else if (!EsCedulaEcuatorianaValida(identificacion))
                {
                    yield return new ValidationResult(
                        "La cedula ingresada no es valida.",
                        new[] { nameof(SolIdentificacion) });
                }

                if (string.IsNullOrWhiteSpace(codigoDactilar))
                {
                    yield return new ValidationResult(
                        "Ingrese el codigo dactilar para solicitudes con cedula.",
                        new[] { nameof(SolCodigoDactilar) });
                }
                else if (!Regex.IsMatch(codigoDactilar, @"^[A-Z]\d{4}[A-Z]\d{4}$"))
                {
                    yield return new ValidationResult(
                        "El codigo dactilar debe tener el formato A1234B5678 en mayusculas.",
                        new[] { nameof(SolCodigoDactilar) });
                }
            }
            else if (tipoDocumento == "PASAPORTE" &&
                     !Regex.IsMatch(identificacion, @"^[A-Z0-9\-]{6,20}$"))
            {
                yield return new ValidationResult(
                    "El pasaporte debe tener entre 6 y 20 caracteres alfanumericos.",
                    new[] { nameof(SolIdentificacion) });
            }

            if (!EsTextoConLetrasValido(nombres, 2, 100))
            {
                yield return new ValidationResult(
                    "Ingrese nombres validos, sin numeros ni caracteres no permitidos.",
                    new[] { nameof(SolNombres) });
            }

            if (!EsTextoConLetrasValido(primerApellido, 2, 100))
            {
                yield return new ValidationResult(
                    "Ingrese un primer apellido valido.",
                    new[] { nameof(SolPrimerApellido) });
            }

            if (!string.IsNullOrWhiteSpace(segundoApellido) &&
                !EsTextoConLetrasValido(segundoApellido, 2, 100))
            {
                yield return new ValidationResult(
                    "El segundo apellido contiene caracteres no permitidos.",
                    new[] { nameof(SolSegundoApellido) });
            }

            if (SolFechaNacimiento == default)
            {
                yield return new ValidationResult(
                    "Seleccione la fecha de nacimiento.",
                    new[] { nameof(SolFechaNacimiento) });
            }
            else if (SolFechaNacimiento.Date > DateTime.Today)
            {
                yield return new ValidationResult(
                    "La fecha de nacimiento no puede ser futura.",
                    new[] { nameof(SolFechaNacimiento) });
            }
            else if (CalcularEdad(SolFechaNacimiento) < 18)
            {
                yield return new ValidationResult(
                    "Debes ser mayor de 18 anos para solicitar la firma electronica.",
                    new[] { nameof(SolFechaNacimiento) });
            }

            if (!EsTextoConLetrasValido(nacionalidad, 2, 100))
            {
                yield return new ValidationResult(
                    "Seleccione una nacionalidad valida.",
                    new[] { nameof(SolNacionalidad) });
            }

            if (!EsValorPermitido(sexo, "M", "F"))
            {
                yield return new ValidationResult(
                    "Seleccione un sexo valido.",
                    new[] { nameof(SolSexo) });
            }

            if (!EsCelularEcuatorianoValido(telefonoPrincipal))
            {
                yield return new ValidationResult(
                    "Ingrese un celular principal valido de 10 digitos que empiece con 09.",
                    new[] { nameof(SolTelefono1) });
            }

            if (!string.IsNullOrWhiteSpace(telefonoSecundario) &&
                !EsTelefonoSecundarioValido(telefonoSecundario))
            {
                yield return new ValidationResult(
                    "El telefono secundario debe ser un celular o convencional ecuatoriano valido.",
                    new[] { nameof(SolTelefono2) });
            }

            if (!string.IsNullOrWhiteSpace(telefonoSecundario) &&
                telefonoSecundario == telefonoPrincipal)
            {
                yield return new ValidationResult(
                    "El telefono secundario debe ser diferente al celular principal.",
                    new[] { nameof(SolTelefono2) });
            }

            if (!string.IsNullOrWhiteSpace(correoSecundario) &&
                !new EmailAddressAttribute().IsValid(correoSecundario))
            {
                yield return new ValidationResult(
                    "El correo secundario no tiene un formato valido.",
                    new[] { nameof(SolCorreo2) });
            }

            if (!string.IsNullOrWhiteSpace(correoPrincipal) &&
                !string.IsNullOrWhiteSpace(correoSecundario) &&
                string.Equals(correoPrincipal, correoSecundario, StringComparison.OrdinalIgnoreCase))
            {
                yield return new ValidationResult(
                    "El correo secundario debe ser diferente al correo principal.",
                    new[] { nameof(SolCorreo2) });
            }

            if (tipoPersona == "JURIDICA" && !SolTieneRuc)
            {
                yield return new ValidationResult(
                    "Las personas juridicas deben registrar RUC.",
                    new[] { nameof(SolTipoPersona), nameof(SolNroRuc) });
            }

            if (requiereRuc)
            {
                if (string.IsNullOrWhiteSpace(ruc))
                {
                    yield return new ValidationResult(
                        "Ingrese el RUC cuando corresponda registrarlo.",
                        new[] { nameof(SolNroRuc) });
                }
                else if (!EsRucEcuatorianoValido(ruc))
                {
                    yield return new ValidationResult(
                        "El RUC debe tener 13 digitos y una estructura ecuatoriana valida.",
                        new[] { nameof(SolNroRuc) });
                }
            }

            if (!CatalogoUbicacionEcuador.ProvinciaValida(provincia))
            {
                yield return new ValidationResult(
                    "Ingrese una provincia valida.",
                    new[] { nameof(SolProvincia) });
            }

            if (!CatalogoUbicacionEcuador.CantonValido(provincia, canton))
            {
                yield return new ValidationResult(
                    "Seleccione un canton valido para la provincia elegida.",
                    new[] { nameof(SolCanton) });
            }

            if (!EsDireccionValida(direccion))
            {
                yield return new ValidationResult(
                    "Ingrese una direccion mas detallada y sin caracteres no permitidos.",
                    new[] { nameof(SolDireccion) });
            }

            if (!EsValorPermitido(formatoFirma, "ARCHIVO_P12", "NUBE"))
            {
                yield return new ValidationResult(
                    "Seleccione un tipo de firma valido.",
                    new[] { nameof(SolFormatoFirma) });
            }

            if (!EsValorPermitido(vigencia, "30 DIAS", "1 AÑO", "2 AÑOS", "3 AÑOS", "4 AÑOS"))
            {
                yield return new ValidationResult(
                    "Seleccione una vigencia valida.",
                    new[] { nameof(SolVigencia) });
            }

            if (tipoPersona == "JURIDICA")
            {
                if (string.IsNullOrWhiteSpace(SolCompanyName))
                {
                    yield return new ValidationResult(
                        "Ingrese la razon social de la empresa.",
                        new[] { nameof(SolCompanyName) });
                }

                if (string.IsNullOrWhiteSpace(SolPosition))
                {
                    yield return new ValidationResult(
                        "Ingrese el cargo en la empresa.",
                        new[] { nameof(SolPosition) });
                }

                if (string.IsNullOrWhiteSpace(SolReason))
                {
                    yield return new ValidationResult(
                        "Ingrese el motivo de la firma.",
                        new[] { nameof(SolReason) });
                }

                var tipoDocManager = (SolIdentificationTypeManager ?? string.Empty).Trim().ToUpperInvariant();
                if (!EsValorPermitido(tipoDocManager, "CEDULA", "PASAPORTE"))
                {
                    yield return new ValidationResult(
                        "Seleccione el tipo de documento del representante.",
                        new[] { nameof(SolIdentificationTypeManager) });
                }

                var idManager = Regex.Replace((SolIdentificationManager ?? string.Empty).Trim().ToUpperInvariant(), @"\s+", string.Empty);
                if (string.IsNullOrWhiteSpace(idManager))
                {
                    yield return new ValidationResult(
                        "Ingrese la identificacion del representante.",
                        new[] { nameof(SolIdentificationManager) });
                }
                else if (tipoDocManager == "CEDULA")
                {
                    if (!Regex.IsMatch(idManager, @"^\d{10}$"))
                    {
                        yield return new ValidationResult(
                            "La cedula del representante debe tener exactamente 10 digitos.",
                            new[] { nameof(SolIdentificationManager) });
                    }
                    else if (!EsCedulaEcuatorianaValida(idManager))
                    {
                        yield return new ValidationResult(
                            "La cedula del representante ingresada no es valida.",
                            new[] { nameof(SolIdentificationManager) });
                    }
                }
                else if (tipoDocManager == "PASAPORTE" && !Regex.IsMatch(idManager, @"^[A-Z0-9\-]{6,20}$"))
                {
                    yield return new ValidationResult(
                        "El pasaporte del representante debe tener entre 6 y 20 caracteres alfanumericos.",
                        new[] { nameof(SolIdentificationManager) });
                }

                if (!EsTextoConLetrasValido(NormalizarEspacios(SolNamesManager), 2, 100))
                {
                    yield return new ValidationResult(
                        "Ingrese nombres validos para el representante.",
                        new[] { nameof(SolNamesManager) });
                }

                if (!EsTextoConLetrasValido(NormalizarEspacios(SolLastNameManager), 2, 100))
                {
                    yield return new ValidationResult(
                        "Ingrese apellidos validos para el representante.",
                        new[] { nameof(SolLastNameManager) });
                }
            }
        }

        private static string NormalizarEspacios(string? valor)
            => Regex.Replace(valor ?? string.Empty, @"\s+", " ").Trim();

        private static string SoloDigitos(string? valor)
            => Regex.Replace(valor ?? string.Empty, @"\D", string.Empty);

        private static int CalcularEdad(DateTime fechaNacimiento)
        {
            var hoy = DateTime.Today;
            var edad = hoy.Year - fechaNacimiento.Year;
            if (fechaNacimiento.Date > hoy.AddYears(-edad))
            {
                edad--;
            }

            return edad;
        }

        private static bool EsValorPermitido(string valor, params string[] permitidos)
        {
            foreach (var permitido in permitidos)
            {
                if (string.Equals(permitido, valor, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EsTextoConLetrasValido(string valor, int minimo, int maximo)
        {
            if (valor.Length < minimo || valor.Length > maximo)
            {
                return false;
            }

            return Regex.IsMatch(valor, @"^[\p{L}][\p{L}'\- ]*$");
        }

        private static bool EsDireccionValida(string direccion)
        {
            if (direccion.Length < 8 || direccion.Length > 300)
            {
                return false;
            }

            return Regex.IsMatch(direccion, @"^[\p{L}\d#.,\-\/() ]+$");
        }

        private static bool EsCelularEcuatorianoValido(string telefono)
            => Regex.IsMatch(telefono, @"^09\d{8}$");

        private static bool EsTelefonoSecundarioValido(string telefono)
            => Regex.IsMatch(telefono, @"^(09\d{8}|0[2-7]\d{7})$");

        private static bool EsCedulaEcuatorianaValida(string cedula)
        {
            if (!Regex.IsMatch(cedula ?? string.Empty, @"^\d{10}$"))
            {
                return false;
            }

            var provincia = int.Parse(cedula[..2]);
            var tercerDigito = int.Parse(cedula[2].ToString());
            if (provincia < 1 || provincia > 24 || tercerDigito > 6)
            {
                return false;
            }

            var suma = 0;
            for (var i = 0; i < 9; i++)
            {
                var digito = int.Parse(cedula[i].ToString());
                if (i % 2 == 0)
                {
                    digito *= 2;
                    if (digito > 9)
                    {
                        digito -= 9;
                    }
                }

                suma += digito;
            }

            var verificadorCalculado = (10 - (suma % 10)) % 10;
            var verificador = int.Parse(cedula[9].ToString());

            return verificadorCalculado == verificador;
        }

        private static bool EsRucEcuatorianoValido(string ruc)
        {
            if (!Regex.IsMatch(ruc ?? string.Empty, @"^\d{13}$"))
            {
                return false;
            }

            var provincia = int.Parse(ruc[..2]);
            if (provincia < 1 || provincia > 24)
            {
                return false;
            }

            var tercerDigito = int.Parse(ruc[2].ToString());
            return tercerDigito switch
            {
                <= 5 => EsCedulaEcuatorianaValida(ruc[..10]) && ruc[10..] != "000",
                6 => ValidarModulo11(ruc[..8], int.Parse(ruc[8].ToString()), new[] { 3, 2, 7, 6, 5, 4, 3, 2 }) &&
                     ruc[9..] != "0000",
                9 => ValidarModulo11(ruc[..9], int.Parse(ruc[9].ToString()), new[] { 4, 3, 2, 7, 6, 5, 4, 3, 2 }) &&
                     ruc[10..] != "000",
                _ => false
            };
        }

        private static bool ValidarModulo11(string baseNumero, int digitoVerificador, int[] coeficientes)
        {
            var suma = 0;

            for (var i = 0; i < coeficientes.Length; i++)
            {
                suma += int.Parse(baseNumero[i].ToString()) * coeficientes[i];
            }

            var residuo = suma % 11;
            var verificadorCalculado = residuo == 0 ? 0 : 11 - residuo;

            return verificadorCalculado == digitoVerificador;
        }

        [Column("SOLMONTOPAGO")]
        public decimal? SolMontoPago { get; set; }

        [StringLength(100)]
        [Column("SOLIDTRANSACCIONPAGO")]
        public string? SolIdTransaccionPago { get; set; }

        [Column("SOLPAGOEXITOSO")]
        public bool? SolPagoExitoso { get; set; } = false;

        [StringLength(6)]
        [Column("SOLPINDESCARGA")]
        public string? SolPinDescarga { get; set; }

        [Column("SOLCLAVEP12")]
        public string? SolClaveP12 { get; set; }

        [Column("SOLARCHIVOP12")]
        public byte[]? SolArchivoP12 { get; set; }

        [Column("SOLFECHAPAGO")]
        public DateTime? SolFechaPago { get; set; }

        [Column("SOL_UANATACA_UUID")]
        [StringLength(50)]
        public string? SolUanatacaUuid { get; set; }

        [Column("SOL_UANATACA_STATUS")]
        [StringLength(50)]
        public string? SolUanatacaStatus { get; set; }

        [Column("SOL_UANATACA_TOKEN")]
        [StringLength(50)]
        public string? SolUanatacaToken { get; set; }

        [Column("SOL_UANATACA_STATUS_TEXT")]
        [StringLength(100)]
        public string? SolUanatacaStatusText { get; set; }

        [Column("SOL_UANATACA_COMMENTS")]
        public string? SolUanatacaComments { get; set; }

        [Column("SOL_UANATACA_PRODUCT_UUID")]
        [StringLength(50)]
        public string? SolUanatacaProductUuid { get; set; }

        [Column("SOL_UANATACA_STAKEHOLDER_UUID")]
        [StringLength(50)]
        public string? SolUanatacaStakeholderUuid { get; set; }

        [Column("SOL_UANATACA_CREATED_BY")]
        [StringLength(100)]
        public string? SolUanatacaCreatedBy { get; set; }

        [Column("SOL_UANATACA_ACTIVE")]
        public bool? SolUanatacaActive { get; set; }

        [Column("SOL_UANATACA_COUNTABLE")]
        public bool? SolUanatacaCountable { get; set; }

        [Column("SOL_UANATACA_RENOVATION")]
        public bool? SolUanatacaRenovation { get; set; }

        [Column("SOL_UANATACA_OFFER_UUID")]
        [StringLength(50)]
        public string? SolUanatacaOfferUuid { get; set; }

        [Column("SOL_UANATACA_HAS_FRONT_ID")]
        public bool? SolUanatacaHasFrontId { get; set; }

        [Column("SOL_UANATACA_HAS_BACK_ID")]
        public bool? SolUanatacaHasBackId { get; set; }

        [Column("SOL_UANATACA_HAS_SELFIE")]
        public bool? SolUanatacaHasSelfie { get; set; }

        [Column("SOL_UANATACA_HAS_RUC_FILE")]
        public bool? SolUanatacaHasRucFile { get; set; }

        [Column("SOL_UANATACA_HAS_SENIOR_VIDEO")]
        public bool? SolUanatacaHasSeniorVideo { get; set; }

        [Column("SOL_UANATACA_HAS_APPOINTMENT")]
        public bool? SolUanatacaHasAppointment { get; set; }

        [Column("SOL_UANATACA_HAS_ACCEPTANCE")]
        public bool? SolUanatacaHasAcceptance { get; set; }

        [Column("SOL_UANATACA_HAS_CONSTITUTION")]
        public bool? SolUanatacaHasConstitution { get; set; }

        [Column("SOL_UANATACA_HAS_MANAGER_ID")]
        public bool? SolUanatacaHasManagerId { get; set; }

        [Column("SOL_UANATACA_HAS_AUTHORIZATION")]
        public bool? SolUanatacaHasAuthorization { get; set; }

        [Column("SOL_UANATACA_HAS_ADDITIONAL")]
        public bool? SolUanatacaHasAdditional { get; set; }

        [Column("SOL_COMPANY_NAME")]
        [StringLength(150)]
        public string? SolCompanyName { get; set; }

        [Column("SOL_DEPARTMENT")]
        [StringLength(100)]
        public string? SolDepartment { get; set; }

        [Column("SOL_POSITION")]
        [StringLength(100)]
        public string? SolPosition { get; set; }

        [Column("SOL_REASON")]
        [StringLength(250)]
        public string? SolReason { get; set; }

        [Column("SOL_IDENTIFICATION_TYPE_MANAGER")]
        [StringLength(20)]
        public string? SolIdentificationTypeManager { get; set; }

        [Column("SOL_IDENTIFICATION_MANAGER")]
        [StringLength(20)]
        public string? SolIdentificationManager { get; set; }

        [Column("SOL_NAMES_MANAGER")]
        [StringLength(100)]
        public string? SolNamesManager { get; set; }

        [Column("SOL_LAST_NAME_MANAGER")]
        [StringLength(100)]
        public string? SolLastNameManager { get; set; }
    }
}
