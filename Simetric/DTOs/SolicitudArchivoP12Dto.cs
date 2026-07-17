namespace Simetric.DTOs;

public sealed class SolicitudArchivoP12Dto
{
    public byte[] Contenido { get; set; } = Array.Empty<byte>();
    public string NombreArchivo { get; set; } = "firma.p12";
}
