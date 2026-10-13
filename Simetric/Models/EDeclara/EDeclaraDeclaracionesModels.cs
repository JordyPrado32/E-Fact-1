namespace Simetric.Models.EDeclara;

public sealed class EDeclaraDeclaracionAnalisis
{
    public int Anio { get; init; }
    public int Periodo { get; init; }
    public int TotalDocumentos { get; init; }
    public int TotalFacturas { get; init; }
    public int TotalNotasCredito { get; init; }
    public int TotalRetenciones { get; init; }
    public int PendientesClasificacion { get; init; }
    public decimal ComprasBase { get; init; }
    public decimal ComprasIva { get; init; }
    public decimal NotasCreditoBase { get; init; }
    public decimal NotasCreditoIva { get; init; }
    public decimal RetencionesIva { get; init; }
    public decimal VentasBase { get; init; }
    public decimal VentasIva { get; init; }
    public decimal NotasCreditoVentasBase { get; init; }
    public decimal NotasCreditoVentasIva { get; init; }
    public IReadOnlyList<EDeclaraCasillaAnalisis> Casillas { get; init; } = Array.Empty<EDeclaraCasillaAnalisis>();
    public IReadOnlyList<EDeclaraDeclaracionValidacion> Validaciones { get; init; } = Array.Empty<EDeclaraDeclaracionValidacion>();
    public decimal BaseNeta => decimal.Round(ComprasBase - NotasCreditoBase, 2);
    public decimal IvaNeto => decimal.Round(ComprasIva - NotasCreditoIva, 2);
    public decimal IvaGenerado => decimal.Round(VentasIva - NotasCreditoVentasIva, 2);
    public decimal IvaAntesRetenciones => decimal.Round(IvaGenerado - IvaNeto, 2);
    public decimal IvaDespuesRetenciones => decimal.Round(IvaAntesRetenciones - RetencionesIva, 2);
    public bool TieneBloqueos => Validaciones.Any(x => !x.EsValida && x.EsBloqueante);
    public bool PuedeGuardarBorrador => TotalDocumentos > 0 && !TieneBloqueos;
}

public sealed class EDeclaraCasillaAnalisis
{
    public string Grupo { get; init; } = string.Empty;
    public string Casilla { get; init; } = string.Empty;
    public decimal Base { get; init; }
    public decimal Iva { get; init; }
    public int Documentos { get; init; }
}

public sealed class EDeclaraDeclaracionValidacion
{
    public string Codigo { get; init; } = string.Empty;
    public string Mensaje { get; init; } = string.Empty;
    public bool EsValida { get; init; }
    public bool EsBloqueante { get; init; }
}
