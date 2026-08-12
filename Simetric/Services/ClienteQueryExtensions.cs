using Simetric.Models;

namespace Simetric.Services;

public static class ClienteQueryExtensions
{
    public static IQueryable<Cliente> ExcluirClientesExclusivosBackOffice(this IQueryable<Cliente> query) =>
        query.Where(cliente =>
            !cliente.Facturas.Any(factura =>
                factura.CodemisorNavigation != null &&
                factura.CodemisorNavigation.EsEmisorSistema) ||
            cliente.Facturas.Any(factura =>
                factura.CodemisorNavigation == null ||
                !factura.CodemisorNavigation.EsEmisorSistema));
}
