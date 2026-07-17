namespace Simetric.Models.EDeclara;

public sealed class EDeclaraContribuyenteResumen
{
    public string Nombre { get; init; } = string.Empty;
    public string? Identificacion { get; init; }
    public bool Activo { get; init; }
}

public sealed class EDeclaraDeclaracionResumen
{
    public string Titulo { get; init; } = string.Empty;
    public string? Periodo { get; init; }
    public bool Activo { get; init; }
}

public sealed class EDeclaraSharedDataSnapshot
{
    public int TotalContribuyentes { get; init; }
    public int TotalDeclaraciones { get; init; }

    public int ContribuyentesActivos { get; init; }
    public int PersonasNaturales { get; init; }
    public int ConCorreo { get; init; }

    public IReadOnlyList<EDeclaraContribuyenteResumen> Contribuyentes { get; init; }
        = Array.Empty<EDeclaraContribuyenteResumen>();

    public IReadOnlyList<EDeclaraDeclaracionResumen> Declaraciones { get; init; }
        = Array.Empty<EDeclaraDeclaracionResumen>();
}
