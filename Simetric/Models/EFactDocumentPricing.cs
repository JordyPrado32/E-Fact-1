namespace Simetric.Models
{
    public static class EFactDocumentPricing
    {
        public const decimal IvaRate = 0.15m;
        public const decimal MontoMinimoPersonalizado = 5.00m;
        public const decimal MontoMaximoPersonalizado = 1000m;
        public const decimal ToleranciaRedondeoMontoEntero = 0.10m;
        public const int MaxDocumentosCalculados = int.MaxValue;

        public const decimal PrecioTierHasta25 = 0.46m;
        public static readonly decimal PrecioTierHasta48 = 17.66m / 48m;
        public static readonly decimal PrecioTierHasta120 = 31.74m / 120m;
        public static readonly decimal PrecioTierHasta240 = 44.16m / 240m;
        public static readonly decimal PrecioTierHasta600 = 69.00m / 600m;
        public const decimal PrecioRecargaAlta = 0.06m;

        public const decimal TopeMontoTier25 = 11.50m;
        public const decimal TopeMontoTier48 = 17.66m;
        public const decimal TopeMontoTier120 = 31.74m;
        public const decimal TopeMontoTier240 = 44.16m;
        public const decimal MontoMinimoRecargaAlta = 65.00m;

        public const decimal PrecioPlan25 = 11.50m;
        public const decimal PrecioPlan48 = 17.66m;
        public const decimal PrecioPlan120 = 31.74m;
        public const decimal PrecioPlan240 = 44.16m;
        public const decimal PrecioPlan600 = 69.00m;
        public const decimal PrecioPlanIlimitado = 90.00m;

        public static readonly int DocumentosMinimosPersonalizados =
            CalcularDocumentosPorMonto(MontoMinimoPersonalizado, PrecioTierHasta25);

        public static decimal CalcularTotalPorCantidad(int documentos)
        {
            if (documentos <= 0)
                return 0m;

            var total = decimal.Round(
                documentos * ObtenerPrecioPorDocumentoSegunCantidad(documentos),
                2,
                MidpointRounding.AwayFromZero);
            var totalEntero = decimal.Round(total, 0, MidpointRounding.AwayFromZero);

            return Math.Abs(total - totalEntero) <= ToleranciaRedondeoMontoEntero
                ? totalEntero
                : total;
        }

        public static decimal ObtenerPrecioPorDocumentoSegunCantidad(int documentos)
        {
            if (documentos <= 25)
                return PrecioTierHasta25;
            if (documentos <= 48)
                return PrecioTierHasta48;
            if (documentos <= 120)
                return PrecioTierHasta120;
            if (documentos <= 240)
                return PrecioTierHasta240;
            if (documentos <= 600)
                return PrecioTierHasta600;

            return PrecioRecargaAlta;
        }

        public static decimal ObtenerPrecioPorDocumentoSegunMonto(decimal monto)
        {
            if (monto >= MontoMinimoRecargaAlta)
                return PrecioRecargaAlta;
            if (monto <= TopeMontoTier25)
                return PrecioTierHasta25;
            if (monto <= TopeMontoTier48)
                return PrecioTierHasta48;
            if (monto <= TopeMontoTier120)
                return PrecioTierHasta120;
            if (monto <= TopeMontoTier240)
                return PrecioTierHasta240;

            return PrecioTierHasta600;
        }

        public static int CalcularDocumentosPorMonto(decimal monto, decimal precioDocActual)
        {
            if (monto <= 0m || precioDocActual <= 0m)
                return 0;

            var documentos = decimal.Round(monto / precioDocActual, 0, MidpointRounding.AwayFromZero);
            if (documentos <= 0m)
                return 0;

            if (documentos >= MaxDocumentosCalculados)
                return MaxDocumentosCalculados;

            return decimal.ToInt32(documentos);
        }

        public static decimal CalcularSubtotalDesdeTotal(decimal total)
        {
            return total <= 0m
                ? 0m
                : decimal.Round(total / (1m + IvaRate), 2, MidpointRounding.AwayFromZero);
        }

        public static decimal CalcularIvaDesdeTotal(decimal total)
        {
            return total <= 0m
                ? 0m
                : decimal.Round(total - CalcularSubtotalDesdeTotal(total), 2, MidpointRounding.AwayFromZero);
        }

        public static decimal EstimarMontoEfact(string? plan, int documentos)
        {
            var texto = plan ?? string.Empty;

            if (texto.Contains("ilimit", StringComparison.OrdinalIgnoreCase))
                return PrecioPlanIlimitado;
            if (texto.Contains("600", StringComparison.OrdinalIgnoreCase) || documentos >= 600)
                return PrecioPlan600;
            if (texto.Contains("240", StringComparison.OrdinalIgnoreCase) || documentos >= 240)
                return PrecioPlan240;
            if (texto.Contains("120", StringComparison.OrdinalIgnoreCase) || documentos >= 120)
                return PrecioPlan120;
            if (texto.Contains("48", StringComparison.OrdinalIgnoreCase) || documentos >= 48)
                return PrecioPlan48;
            if (texto.Contains("25", StringComparison.OrdinalIgnoreCase) || documentos >= 25)
                return PrecioPlan25;

            return documentos > 0 ? CalcularTotalPorCantidad(documentos) : PrecioPlan25;
        }
    }
}
