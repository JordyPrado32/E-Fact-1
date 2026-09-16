namespace Simetric.DTOs;

public sealed class LiquidacionRetencionRequestDto
{
    public List<CompraRetValorDto> Retenciones { get; set; } = new();
    public string? CorreoPrincipal { get; set; }
    public List<FacturaCorreoDestinoDto> Correos { get; set; } = new();
}
