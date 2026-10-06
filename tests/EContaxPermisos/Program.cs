using System.Text.Json;
using Simetric.Models.EContax;

static void Comprobar(bool condicion, string mensaje)
{
    if (!condicion) throw new InvalidOperationException(mensaje);
}

Comprobar(EContaxPermisos.AccionesDe(null, 42) == EContaxAccion.Todas, "Los roles existentes deben conservar sus acciones.");
Comprobar(EContaxPermisos.AccionesDe("{}", 42) == 0, "Un menú sin acciones explícitas debe quedar denegado.");
Comprobar(EContaxPermisos.AccionesDe("{\"42\":1}", 42) == EContaxAccion.Ver, "El rol de consulta solo debe permitir ver.");
foreach (var accion in new[] { EContaxAccion.Crear, EContaxAccion.Editar, EContaxAccion.Eliminar, EContaxAccion.Exportar })
{
    Comprobar(!EContaxPermisos.AccionesDe("{\"42\":1}", 42).HasFlag(accion), $"La consulta no debe conceder {accion}.");
    var json = JsonSerializer.Serialize(new Dictionary<int, EContaxAccion> { [42] = EContaxAccion.Ver | accion });
    Comprobar(EContaxPermisos.AccionesDe(json, 42) == (EContaxAccion.Ver | accion), $"Debe conservarse la acción {accion}.");
    Comprobar(EContaxPermisos.AccionesDe(json, 43) == 0, "Las acciones no deben heredarse entre menús.");
}
Comprobar(EContaxPermisos.AccionesDe("{\"42\":4}", 42) == 0, "Editar sin consultar debe quedar denegado.");
Comprobar(EContaxPermisos.AccionesDe("{\"42\":33}", 42) == 0, "Los bits desconocidos deben quedar denegados.");
Comprobar(EContaxPermisos.AccionesDe("{\"42\":-1}", 42) == 0, "Una máscara negativa debe quedar denegada.");
Comprobar(EContaxPermisos.AccionesDe("{\"42\":31}", 42) == EContaxAccion.Todas, "La máscara completa debe conservarse.");
try
{
    EContaxPermisos.AccionesDe("JSON inválido", 42);
    throw new InvalidOperationException("El JSON inválido no debe conceder permisos silenciosamente.");
}
catch (JsonException) { }
Console.WriteLine("20 comprobaciones de permisos E-Contax correctas.");
