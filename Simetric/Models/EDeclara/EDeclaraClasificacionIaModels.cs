namespace Simetric.Models.EDeclara;

public sealed record EDeclaraClasificacionIaResultado(
    int Analizados,
    int Clasificados,
    int Pendientes,
    IReadOnlyList<string> Errores)
{
    public bool TieneErrores => Errores.Count > 0;
}
