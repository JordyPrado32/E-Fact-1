public class PorcentajeIvaDto
{
    public string Codigo { get; set; } = "";
    public string? Descripcion { get; set; }
    public string? Valor { get; set; }          // texto (ej: "12%")
    public decimal? ValorCalculo { get; set; }  // ej: 0.12m
}
