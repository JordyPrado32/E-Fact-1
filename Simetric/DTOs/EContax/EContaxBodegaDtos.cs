namespace Simetric.DTOs.EContax;

public sealed class EContaxBodegaDto
{
    public int IdBodega { get; set; }
    public int IdEmpresa { get; set; }
    public int IdSucursal { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public string? Direccion { get; set; }
    public string? Observacion { get; set; }
    public string NombreSucursal { get; set; } = string.Empty;
    public bool Estado { get; set; }
}

public sealed class EContaxBodegaUpsertDto
{
    public int IdBodega { get; set; }
    public int IdSucursal { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public string? Direccion { get; set; }
    public string? Observacion { get; set; }
}
