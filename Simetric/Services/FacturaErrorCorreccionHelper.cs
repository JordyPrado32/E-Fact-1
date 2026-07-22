using System.Globalization;
using System.Text;

namespace Simetric.Services;

public static class FacturaErrorCorreccionHelper
{
    public static FacturaCorreccionDestino Clasificar(string? mensaje)
    {
        var texto = Normalizar(mensaje);
        if (string.IsNullOrWhiteSpace(texto))
            return FacturaCorreccionDestino.Ninguno;

        if (Contiene(texto, "CERTIFICADO", "FIRMA ELECTRONICA", "CLAVE DE FIRMA"))
            return FacturaCorreccionDestino.Firma;

        if (Contiene(texto,
                "NOMBRE COMERCIAL", "NOMBRECOMERCIAL", "RAZON SOCIAL EMISOR", "RAZONSOCIALEMISOR",
                "DIRECCION MATRIZ", "DIRMATRIZ", "DIRESTABLECIMIENTO", "RUC EMISOR", "RUC DEL EMISOR",
                "ESTABLECIMIENTO", "PUNTO DE EMISION", "OBLIGADO A LLEVAR CONTABILIDAD"))
            return FacturaCorreccionDestino.Emisor;

        if (Contiene(texto,
                "COMPRADOR", "CLIENTE", "IDENTIFICACION", "RAZON SOCIAL COMPRADOR",
                "RAZONSOCIALCOMPRADOR", "DIRECCIONCOMPRADOR"))
            return FacturaCorreccionDestino.Cliente;

        if (Contiene(texto, "CODIGO PRINCIPAL", "CODIGOPRINCIPAL", "PRODUCTO", "DESCRIPCION DEL ITEM"))
            return FacturaCorreccionDestino.Productos;

        if (Contiene(texto, "SECUENCIAL REGISTRADO", "CLAVE DE ACCESO REGISTRADA", "ERROR DE SECUENCIA"))
            return FacturaCorreccionDestino.ConflictoSecuencia;

        return FacturaCorreccionDestino.Factura;
    }

    public static string ObtenerRuta(FacturaCorreccionDestino destino) => destino switch
    {
        FacturaCorreccionDestino.Emisor => "/emisor",
        FacturaCorreccionDestino.Firma => "/firma",
        FacturaCorreccionDestino.Cliente => "/clientes",
        FacturaCorreccionDestino.Productos => "/productos",
        _ => string.Empty
    };

    public static string ObtenerEtiqueta(FacturaCorreccionDestino destino) => destino switch
    {
        FacturaCorreccionDestino.Emisor => "Corregir emisor",
        FacturaCorreccionDestino.Firma => "Corregir firma",
        FacturaCorreccionDestino.Cliente => "Corregir cliente",
        FacturaCorreccionDestino.Productos => "Corregir productos",
        FacturaCorreccionDestino.ConflictoSecuencia => "Revisar conflicto",
        FacturaCorreccionDestino.Factura => "Revisar datos de factura",
        _ => string.Empty
    };

    private static bool Contiene(string texto, params string[] valores) =>
        valores.Any(texto.Contains);

    private static string Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return string.Empty;

        var builder = new StringBuilder(valor.Length);
        foreach (var caracter in valor.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caracter) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToUpperInvariant(caracter));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}

public enum FacturaCorreccionDestino
{
    Ninguno,
    Emisor,
    Firma,
    Cliente,
    Productos,
    Factura,
    ConflictoSecuencia
}
