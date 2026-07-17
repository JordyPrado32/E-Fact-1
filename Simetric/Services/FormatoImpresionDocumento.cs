namespace Simetric.Services;

public enum FormatoImpresionDocumento
{
    A4 = 0,
    Termica80mm = 1,
    Termica50mm = 2
}

public static class FormatoImpresionDocumentoExtensions
{
    public static bool EsTermico(this FormatoImpresionDocumento formato)
        => formato is FormatoImpresionDocumento.Termica80mm or FormatoImpresionDocumento.Termica50mm;

    public static float ObtenerAnchoMm(this FormatoImpresionDocumento formato)
        => formato switch
        {
            FormatoImpresionDocumento.Termica50mm => 50f,
            _ => 80f
        };

    public static string ObtenerSufijoArchivo(this FormatoImpresionDocumento formato)
        => formato switch
        {
            FormatoImpresionDocumento.Termica80mm => "_ticket_80mm",
            FormatoImpresionDocumento.Termica50mm => "_ticket_50mm",
            _ => string.Empty
        };

    public static string ObtenerEtiqueta(this FormatoImpresionDocumento formato)
        => formato switch
        {
            FormatoImpresionDocumento.Termica80mm => "Termica 80mm",
            FormatoImpresionDocumento.Termica50mm => "Termica 50mm",
            _ => "A4"
        };
}
