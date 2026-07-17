using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;
using System.Globalization;
using System.Text;

namespace Simetric.Services;

public static class PaisCatalogoService
{
    public static IReadOnlyList<string> Paises { get; } = new[]
    {
        "Afganistan",
        "Albania",
        "Alemania",
        "Andorra",
        "Angola",
        "Antigua y Barbuda",
        "Arabia Saudita",
        "Argelia",
        "Argentina",
        "Armenia",
        "Australia",
        "Austria",
        "Azerbaiyan",
        "Bahamas",
        "Banglades",
        "Barbados",
        "Barein",
        "Belgica",
        "Belice",
        "Benin",
        "Bielorrusia",
        "Birmania",
        "Bolivia",
        "Bosnia y Herzegovina",
        "Botsuana",
        "Brasil",
        "Brunei",
        "Bulgaria",
        "Burkina Faso",
        "Burundi",
        "Butan",
        "Cabo Verde",
        "Camboya",
        "Camerun",
        "Canada",
        "Catar",
        "Chad",
        "Chile",
        "China",
        "Chipre",
        "Colombia",
        "Comoras",
        "Corea del Norte",
        "Corea del Sur",
        "Costa de Marfil",
        "Costa Rica",
        "Croacia",
        "Cuba",
        "Dinamarca",
        "Dominica",
        "Ecuador",
        "Egipto",
        "El Salvador",
        "Emiratos Arabes Unidos",
        "Eritrea",
        "Eslovaquia",
        "Eslovenia",
        "Espana",
        "Estados Unidos",
        "Estonia",
        "Esuatini",
        "Etiopia",
        "Filipinas",
        "Finlandia",
        "Fiyi",
        "Francia",
        "Gabon",
        "Gambia",
        "Georgia",
        "Ghana",
        "Granada",
        "Grecia",
        "Guatemala",
        "Guinea",
        "Guinea Ecuatorial",
        "Guinea Bissau",
        "Guyana",
        "Haiti",
        "Honduras",
        "Hungria",
        "India",
        "Indonesia",
        "Irak",
        "Iran",
        "Irlanda",
        "Islandia",
        "Islas Marshall",
        "Islas Salomon",
        "Israel",
        "Italia",
        "Jamaica",
        "Japon",
        "Jordania",
        "Kazajistan",
        "Kenia",
        "Kirguistan",
        "Kiribati",
        "Kuwait",
        "Laos",
        "Lesoto",
        "Letonia",
        "Libano",
        "Liberia",
        "Libia",
        "Liechtenstein",
        "Lituania",
        "Luxemburgo",
        "Macedonia del Norte",
        "Madagascar",
        "Malasia",
        "Malaui",
        "Maldivas",
        "Mali",
        "Malta",
        "Marruecos",
        "Mauricio",
        "Mauritania",
        "Mexico",
        "Micronesia",
        "Moldavia",
        "Monaco",
        "Mongolia",
        "Montenegro",
        "Mozambique",
        "Namibia",
        "Nauru",
        "Nepal",
        "Nicaragua",
        "Niger",
        "Nigeria",
        "Noruega",
        "Nueva Zelanda",
        "Oman",
        "Paises Bajos",
        "Pakistan",
        "Palaos",
        "Panama",
        "Papua Nueva Guinea",
        "Paraguay",
        "Peru",
        "Polonia",
        "Portugal",
        "Reino Unido",
        "Republica Centroafricana",
        "Republica Checa",
        "Republica del Congo",
        "Republica Democratica del Congo",
        "Republica Dominicana",
        "Ruanda",
        "Rumania",
        "Rusia",
        "Samoa",
        "San Cristobal y Nieves",
        "San Marino",
        "San Vicente y las Granadinas",
        "Santa Lucia",
        "Santo Tome y Principe",
        "Senegal",
        "Serbia",
        "Seychelles",
        "Sierra Leona",
        "Singapur",
        "Siria",
        "Somalia",
        "Sri Lanka",
        "Sudafrica",
        "Sudan",
        "Sudan del Sur",
        "Suecia",
        "Suiza",
        "Surinam",
        "Tailandia",
        "Tanzania",
        "Tayikistan",
        "Timor Oriental",
        "Togo",
        "Tonga",
        "Trinidad y Tobago",
        "Tunez",
        "Turkmenistan",
        "Turquia",
        "Tuvalu",
        "Ucrania",
        "Uganda",
        "Uruguay",
        "Uzbekistan",
        "Vanuatu",
        "Vaticano",
        "Venezuela",
        "Vietnam",
        "Yemen",
        "Yibuti",
        "Zambia",
        "Zimbabue"
    };

    public static async Task AsegurarCatalogoAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var existentes = await db.Paises
            .AsNoTracking()
            .Select(x => new { x.IdPais, Descripcion = x.Descripcion ?? string.Empty })
            .ToListAsync(cancellationToken);

        var existentesNormalizados = existentes
            .Where(x => !string.IsNullOrWhiteSpace(x.Descripcion))
            .Select(x => Normalizar(x.Descripcion))
            .ToHashSet(StringComparer.Ordinal);

        var nextId = existentes.Count == 0
            ? 1
            : existentes.Max(x => x.IdPais) + 1;

        var faltantes = Paises
            .Where(x => !existentesNormalizados.Contains(Normalizar(x)))
            .Select(x => new Pais
            {
                IdPais = nextId++,
                Descripcion = x
            })
            .ToList();

        if (faltantes.Count == 0)
            return;

        db.Paises.AddRange(faltantes);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return string.Empty;

        var normalized = valor.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
