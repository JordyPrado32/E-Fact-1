namespace Simetric.Models;

public sealed class AliadoCuentaBancaria
{
    public static readonly string[] Bancos = ["Pichincha", "Banco Guayaquil", "Banco del Pacífico", "Produbanco", "Banco Bolivariano", "Banco Internacional", "Banco del Austro", "Banco de Loja", "Otro banco o cooperativa"];
    public string Banco { get; set; } = string.Empty;
    public string OtroBanco { get; set; } = string.Empty;
    public string NombreBanco => Banco == "Otro banco o cooperativa" ? OtroBanco.Trim() : Banco;
    public string Tipo { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public string Titular { get; set; } = string.Empty;
    public bool EsValida => Bancos.Contains(Banco) && !string.IsNullOrWhiteSpace(NombreBanco) && NombreBanco.Length <= 120 && (Tipo is "Ahorros" or "Corriente") &&
        Numero.Length is >= 4 and <= 30 && Numero.All(char.IsAsciiDigit) &&
        !string.IsNullOrWhiteSpace(Titular) && Titular.Trim().Length <= 120;
}
