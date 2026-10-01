namespace Simetric.Services;

internal enum AliadoComisionEstado
{
    Generada,
    Pendiente,
    Aprobada,
    Pagada,
    Anulada,
    Revertida,
    AjustePendiente
}

internal static class AliadoComisionStateMachine
{
    public static void Require(string origen, AliadoComisionEstado destino)
    {
        if (!Enum.TryParse<AliadoComisionEstado>(origen, true, out var estado) || !PuedeTransicionar(estado, destino))
            throw new InvalidOperationException($"No se puede pasar una comisión de '{origen}' a '{destino}'.");
    }

    private static bool PuedeTransicionar(AliadoComisionEstado origen, AliadoComisionEstado destino)
        => (origen, destino) switch
        {
            (AliadoComisionEstado.Generada, AliadoComisionEstado.Pendiente or AliadoComisionEstado.Aprobada or AliadoComisionEstado.Anulada) => true,
            (AliadoComisionEstado.Pendiente, AliadoComisionEstado.Pendiente or AliadoComisionEstado.Aprobada or AliadoComisionEstado.Anulada) => true,
            (AliadoComisionEstado.Aprobada, AliadoComisionEstado.Pagada or AliadoComisionEstado.Anulada) => true,
            (AliadoComisionEstado.Pagada, AliadoComisionEstado.Revertida or AliadoComisionEstado.AjustePendiente) => true,
            (AliadoComisionEstado.AjustePendiente, AliadoComisionEstado.Revertida) => true,
            _ when origen == destino => true,
            _ => false
        };
}
