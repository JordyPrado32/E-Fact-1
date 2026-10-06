using Simetric.Models;
using Simetric.Services;

static void Igual(decimal actual, decimal esperado)
{
    if (actual != esperado) throw new InvalidOperationException($"Esperado {esperado}; obtenido {actual}.");
}
foreach (var (vigencia, costo, ganancia) in new[]
{
    ("7 DIAS", 5.59m, 3.41m), ("30 DIAS", 6.39m, 5.61m),
    ("1 ANIO", 12.05m, 8.95m), ("2 ANIOS", 18.75m, 12.25m),
    ("3 ANIOS", 24.72m, 15.28m), ("4 ANIOS", 30.72m, 18.28m), ("5 ANIOS", 36.05m, 20.95m)
})
{
    Igual(ESignPricing.ObtenerCostoProveedor(vigencia), costo);
    Igual(AliadoComisionCalculationService.ObtenerBaseComisionable(ESignPricing.ObtenerPrecioBase(vigencia),
        [new($"Firma electronica E-Sign Archivo - {vigencia}", 1m)]), ganancia);
}
Igual(ESignPricing.ObtenerCostoProveedor("Firma electrónica 1 año"), 12.05m);
Igual(ESignPricing.ObtenerCostoProveedor("Firma 30 días"), 6.39m);
Igual(AliadoComisionCalculationService.ObtenerBaseComisionable(42m, [new("Firma 1 año", 2m)]), 17.90m);
Igual(AliadoComisionCalculationService.ObtenerBaseComisionable(121m, [new("Firma 1 año", 1m)]), 108.95m);
Igual(AliadoComisionCalculationService.ObtenerBaseComisionable(10m, [new("Firma 1 año", 1m)]), 0m);
Igual(AliadoComisionCalculationService.ObtenerBaseComisionable(100m, []), 100m);
Igual(AliadoComisionCalculationService.CalcularValor(8.95m, 10m), 0.90m);
Igual(AliadoComisionCalculationService.ObtenerBaseNeta(null, 10m, 11m), 21m);
foreach (var descripcion in new[] { "Firma sin vigencia", "Firma 6 años", "Firma 17 días" })
{
    try { ESignPricing.ObtenerCostoProveedor(descripcion); }
    catch (InvalidOperationException) { continue; }
    throw new InvalidOperationException($"No debe concederse comisión con vigencia desconocida: {descripcion}.");
}
foreach (var estado in new[] { "Generada", "Pendiente", "Aprobada" })
    if (!AliadoComisionCalculationService.PuedeRecalcular(estado, "VentaNueva", null, null))
        throw new InvalidOperationException($"Debe recalcularse una comisión {estado} sin liquidación.");
foreach (var estado in new[] { "Pagada", "Anulada", "Revertida" })
    if (AliadoComisionCalculationService.PuedeRecalcular(estado, "VentaNueva", null, null))
        throw new InvalidOperationException($"No debe recalcularse una comisión {estado}.");
if (AliadoComisionCalculationService.PuedeRecalcular("Pendiente", "VentaNueva", 1, null) ||
    AliadoComisionCalculationService.PuedeRecalcular("Pendiente", "VentaNueva", null, DateTime.Today) ||
    AliadoComisionCalculationService.PuedeRecalcular("Pendiente", "Reversion-VentaNueva", null, null))
    throw new InvalidOperationException("Las liquidaciones, pagos y reversiones deben conservarse.");
Console.WriteLine("Comprobaciones de costos, ganancia, redondeo y exclusión de pagos/liquidaciones/reversiones correctas.");
