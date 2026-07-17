using System.Collections.Generic;

namespace Simetric.Models
{
    public class EFactPlanDto
    {
        public string Nombre { get; set; } = string.Empty;
        public int CantidadDocs { get; set; } // -1 for unlimited, 0 for custom/per document
        public decimal Precio { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public string ColorClase { get; set; } = string.Empty;
        public string Icono { get; set; } = string.Empty;
        public bool EsRecomendado { get; set; }
    }

    public static class EFactBillingConfig
    {
        public static List<EFactPlanDto> EFactPlans { get; set; } = new()
        {
            new EFactPlanDto { Nombre = "25 documentos", CantidadDocs = 25, Precio = EFactDocumentPricing.PrecioPlan25, Descripcion = "Una recarga simple para comenzar.", ColorClase = "blue", Icono = "bi-cup-hot", EsRecomendado = false },
            new EFactPlanDto { Nombre = "120 documentos", CantidadDocs = 120, Precio = EFactDocumentPricing.PrecioPlan120, Descripcion = "Equilibrio ideal para tu operacion diaria.", ColorClase = "orange", Icono = "bi-box2-heart", EsRecomendado = true },
            new EFactPlanDto { Nombre = "600 documentos", CantidadDocs = 600, Precio = EFactDocumentPricing.PrecioPlan600, Descripcion = "Mas documentos para una operacion constante.", ColorClase = "mint", Icono = "bi-boxes", EsRecomendado = false },
            new EFactPlanDto { Nombre = "Ilimitados durante 1 año", CantidadDocs = -1, Precio = EFactDocumentPricing.PrecioPlanIlimitado, Descripcion = "Emite sin descontar saldo por un año.", ColorClase = "green", Icono = "bi-stars", EsRecomendado = false },
            new EFactPlanDto { Nombre = "Por documento", CantidadDocs = 0, Precio = EFactDocumentPricing.PrecioTierHasta25, Descripcion = "Compra por documentos o por dinero.", ColorClase = "slate", Icono = "bi-file-earmark", EsRecomendado = false }
        };
    }
}
