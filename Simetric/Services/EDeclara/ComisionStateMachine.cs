using Simetric.Models.EDeclara;

namespace Simetric.Services.EDeclara;

internal static class ComisionStateMachine
{
    public static bool PuedeTransicionar(ComisionEstado origen, ComisionEstado destino)
        => (origen, destino) switch
        {
            (ComisionEstado.Generada, ComisionEstado.Pendiente) => true,
            (ComisionEstado.Pendiente, ComisionEstado.Pagada) => true,
            (ComisionEstado.Generada, ComisionEstado.Anulada) => true,
            (ComisionEstado.Pendiente, ComisionEstado.Anulada) => true,
            (ComisionEstado.Generada, ComisionEstado.Revertida) => true,
            (ComisionEstado.Pendiente, ComisionEstado.Revertida) => true,
            (ComisionEstado.Pagada, ComisionEstado.Revertida) => true,
            _ when origen == destino => true,
            _ => false
        };

    public static void Validar(string origen, ComisionEstado destino)
    {
        if (!Enum.TryParse<ComisionEstado>(origen, true, out var estado) || !PuedeTransicionar(estado, destino))
            throw new InvalidOperationException($"No se puede pasar una comisión de '{origen}' a '{destino}'.");
    }
}
