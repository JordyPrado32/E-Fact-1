namespace Simetric.Modules.AsistenteIAFacturacion.Config;

public sealed class OpenAISettings
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gpt-4.1-mini";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
}
