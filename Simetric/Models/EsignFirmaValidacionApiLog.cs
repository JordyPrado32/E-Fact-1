namespace Simetric.Models;

public sealed class EsignFirmaValidacionApiLog
{
    public long Id { get; set; }
    public int IdUsuario { get; set; }
    public int CodEmisor { get; set; }
    public string? Ruc { get; set; }
    public DateTime FechaValidacion { get; set; }
    public bool EsValida { get; set; }
    public string? EstadoVigencia { get; set; }
    public string? Mensaje { get; set; }
    public string? NombreTitular { get; set; }
    public string? Identificacion { get; set; }
    public DateTime? FechaExpiracion { get; set; }
    public int? DiasRestantes { get; set; }
    public int? HttpStatusCode { get; set; }
    public bool? ApiSuccess { get; set; }
    public string? ResponseJson { get; set; }
    public string? ErrorTecnico { get; set; }
}
