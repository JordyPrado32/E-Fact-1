namespace Simetric.Modules.AsistenteIAFacturacion.DTOs;

public sealed class OpenAIDiagnosticsDto
{
    public bool ApiKeyConfigured { get; set; }
    public string ApiKeySource { get; set; } = "none";
    public string Model { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
}
