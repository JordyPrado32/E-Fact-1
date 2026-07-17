using System.Globalization;
using System.Text;

namespace Simetric.Models;

public sealed class ProvinciaEcuador
{
    public string Nombre { get; init; } = string.Empty;
    public IReadOnlyList<string> Cantones { get; init; } = Array.Empty<string>();
}

public static class CatalogoUbicacionEcuador
{
    public static IReadOnlyList<ProvinciaEcuador> Provincias { get; } = new List<ProvinciaEcuador>
    {
        new() { Nombre = "Azuay", Cantones = new[] { "Camilo Ponce Enríquez", "Chordeleg", "Cuenca", "El Pan", "Girón", "Guachapala", "Gualaceo", "Nabón", "Oña", "Paute", "Pucará", "San Fernando", "Santa Isabel", "Sevilla de Oro", "Sígsig" } },
        new() { Nombre = "Bolívar", Cantones = new[] { "Caluma", "Chillanes", "Chimbo", "Echeandía", "Guaranda", "Las Naves", "San Miguel" } },
        new() { Nombre = "Cañar", Cantones = new[] { "Azogues", "Biblián", "Cañar", "Déleg", "El Tambo", "La Troncal", "Suscal" } },
        new() { Nombre = "Carchi", Cantones = new[] { "Bolívar", "Espejo", "Mira", "Montúfar", "San Pedro de Huaca", "Tulcán" } },
        new() { Nombre = "Chimborazo", Cantones = new[] { "Alausí", "Chambo", "Chunchi", "Colta", "Cumandá", "Guamote", "Guano", "Pallatanga", "Penipe", "Riobamba" } },
        new() { Nombre = "Cotopaxi", Cantones = new[] { "La Maná", "Latacunga", "Pangua", "Pujilí", "Salcedo", "Saquisilí", "Sigchos" } },
        new() { Nombre = "El Oro", Cantones = new[] { "Arenillas", "Atahualpa", "Balsas", "Chilla", "El Guabo", "Huaquillas", "Las Lajas", "Machala", "Marcabelí", "Pasaje", "Piñas", "Portovelo", "Santa Rosa", "Zaruma" } },
        new() { Nombre = "Esmeraldas", Cantones = new[] { "Atacames", "Eloy Alfaro", "Esmeraldas", "Muisne", "Quinindé", "Rioverde", "San Lorenzo" } },
        new() { Nombre = "Galápagos", Cantones = new[] { "Isabela", "San Cristóbal", "Santa Cruz" } },
        new() { Nombre = "Guayas", Cantones = new[] { "Alfredo Baquerizo Moreno (Juján)", "Balao", "Balzar", "Colimes", "Coronel Marcelino Maridueña", "Daule", "Durán", "El Empalme", "El Triunfo", "General Antonio Elizalde (Bucay)", "Guayaquil", "Isidro Ayora", "Lomas de Sargentillo", "Milagro", "Naranjal", "Naranjito", "Nobol", "Palestina", "Pedro Carbo", "Playas", "Salitre", "Samborondón", "Santa Lucía", "Simón Bolívar", "Yaguachi" } },
        new() { Nombre = "Imbabura", Cantones = new[] { "Antonio Ante", "Cotacachi", "Ibarra", "Otavalo", "Pimampiro", "San Miguel de Urcuquí" } },
        new() { Nombre = "Loja", Cantones = new[] { "Calvas", "Catamayo", "Celica", "Chaguarpamba", "Espíndola", "Gonzanamá", "Loja", "Macará", "Olmedo", "Paltas", "Pindal", "Puyango", "Quilanga", "Saraguro", "Sozoranga" } },
        new() { Nombre = "Los Ríos", Cantones = new[] { "Baba", "Babahoyo", "Buena Fe", "Mocache", "Montalvo", "Palenque", "Puebloviejo", "Quevedo", "Quinsaloma", "Urdaneta", "Valencia", "Ventanas", "Vinces" } },
        new() { Nombre = "Manabí", Cantones = new[] { "24 de Mayo", "Bolívar", "Chone", "El Carmen", "Flavio Alfaro", "Jama", "Jaramijó", "Jipijapa", "Junín", "Manta", "Montecristi", "Olmedo", "Paján", "Pedernales", "Pichincha", "Portoviejo", "Puerto López", "Rocafuerte", "San Vicente", "Santa Ana", "Sucre", "Tosagua" } },
        new() { Nombre = "Morona Santiago", Cantones = new[] { "Gualaquiza", "Huamboya", "Limón Indanza", "Logroño", "Morona (Macas)", "Pablo Sexto", "Palora", "San Juan Bosco", "Santiago de Méndez", "Sucúa", "Taisha", "Tiwintza" } },
        new() { Nombre = "Napo", Cantones = new[] { "Archidona", "Carlos Julio Arosemena Tola", "El Chaco", "Quijos", "Tena" } },
        new() { Nombre = "Orellana", Cantones = new[] { "Aguarico", "Francisco de Orellana", "La Joya de los Sachas", "Loreto" } },
        new() { Nombre = "Pastaza", Cantones = new[] { "Arajuno", "Mera", "Pastaza", "Santa Clara" } },
        new() { Nombre = "Pichincha", Cantones = new[] { "Cayambe", "Mejía", "Pedro Moncayo", "Pedro Vicente Maldonado", "Puerto Quito", "Quito", "Rumiñahui", "San Miguel de los Bancos" } },
        new() { Nombre = "Santa Elena", Cantones = new[] { "La Libertad", "Salinas", "Santa Elena" } },
        new() { Nombre = "Santo Domingo de los Tsáchilas", Cantones = new[] { "La Concordia", "Santo Domingo" } },
        new() { Nombre = "Sucumbíos", Cantones = new[] { "Cascales", "Cuyabeno", "Gonzalo Pizarro", "Lago Agrio", "Putumayo", "Shushufindi", "Sucumbíos" } },
        new() { Nombre = "Tungurahua", Cantones = new[] { "Ambato", "Baños de Agua Santa", "Cevallos", "Mocha", "Patate", "Pelileo", "Píllaro", "Quero", "Tisaleo" } },
        new() { Nombre = "Zamora Chinchipe", Cantones = new[] { "Centinela del Cóndor", "Chinchipe", "El Pangui", "Nangaritza", "Palanda", "Paquisha", "Yacuambi", "Yantzaza", "Zamora" } }
    };

    public static IReadOnlyList<string> ObtenerCantones(string? provincia)
    {
        return TryObtenerProvincia(provincia, out var provinciaEncontrada)
            ? provinciaEncontrada.Cantones
            : Array.Empty<string>();
    }

    public static bool ProvinciaValida(string? provincia)
        => TryObtenerProvincia(provincia, out _);

    public static bool CantonValido(string? provincia, string? canton)
    {
        if (!TryObtenerProvincia(provincia, out var provinciaEncontrada))
        {
            return false;
        }

        var claveCanton = NormalizarClave(canton);
        return provinciaEncontrada.Cantones.Any(item => NormalizarClave(item) == claveCanton);
    }

    public static string? ObtenerProvinciaCanonical(string? provincia)
    {
        return TryObtenerProvincia(provincia, out var provinciaEncontrada)
            ? provinciaEncontrada.Nombre
            : null;
    }

    public static string? ObtenerCantonCanonical(string? provincia, string? canton)
    {
        if (!TryObtenerProvincia(provincia, out var provinciaEncontrada))
        {
            return null;
        }

        var claveCanton = NormalizarClave(canton);
        return provinciaEncontrada.Cantones.FirstOrDefault(item => NormalizarClave(item) == claveCanton);
    }

    private static bool TryObtenerProvincia(string? provincia, out ProvinciaEcuador provinciaEncontrada)
    {
        var claveProvincia = NormalizarClave(provincia);
        provinciaEncontrada = Provincias.FirstOrDefault(item => NormalizarClave(item.Nombre) == claveProvincia)
            ?? new ProvinciaEcuador();

        return !string.IsNullOrWhiteSpace(provinciaEncontrada.Nombre);
    }

    private static string NormalizarClave(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return string.Empty;
        }

        var normalizado = valor.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalizado.Length);

        foreach (var caracter in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caracter) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToUpperInvariant(caracter));
            }
        }

        return builder.ToString();
    }
}
