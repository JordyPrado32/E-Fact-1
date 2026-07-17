using System.Security.Cryptography;

namespace Simetric.Components.Helpers
{
    public static class RecoveryCodeHelper
    {
        public const int LongitudCodigo = 8;
        public const int MinutosExpiracionPorDefecto = 60;

        public static string GenerarCodigoNumerico(int longitud = LongitudCodigo)
        {
            if (longitud <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(longitud), "La longitud del codigo debe ser mayor a cero.");
            }

            var bytes = new byte[longitud];
            var resultado = new char[longitud];

            while (true)
            {
                RandomNumberGenerator.Fill(bytes);

                var todosCeros = true;
                for (var i = 0; i < longitud; i++)
                {
                    var digito = bytes[i] % 10;
                    resultado[i] = (char)('0' + digito);

                    if (digito != 0)
                    {
                        todosCeros = false;
                    }
                }

                if (!todosCeros)
                {
                    return new string(resultado);
                }
            }
        }

        public static bool EsCodigoValido(string? codigo, int longitud = LongitudCodigo)
        {
            if (string.IsNullOrWhiteSpace(codigo) || codigo.Length != longitud)
            {
                return false;
            }

            var tieneDigitoNoCero = false;
            foreach (var caracter in codigo)
            {
                if (!char.IsDigit(caracter))
                {
                    return false;
                }

                if (caracter != '0')
                {
                    tieneDigitoNoCero = true;
                }
            }

            return tieneDigitoNoCero;
        }
    }
}
