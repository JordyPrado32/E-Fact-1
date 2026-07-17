using Microsoft.EntityFrameworkCore;
using Simetric.Data;

namespace Simetric.Services;

internal static class ComprobanteCorreoDestinatariosHelper
{
    public static async Task<List<string>> ConstruirDestinatariosClienteAsync(
        AppDbContext context,
        int? usuarioId,
        int? codCliente,
        string? correoPrincipalDocumento,
        IEnumerable<string?>? correosExtra = null)
    {
        var destinatarios = new List<string>();

        if (usuarioId is > 0)
        {
            var correoUsuario = await context.Usuarios
                .AsNoTracking()
                .Where(u => u.IdUsuario == usuarioId.Value)
                .Select(u => u.Email)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(correoUsuario))
                destinatarios.Add(correoUsuario);
        }

        if (!string.IsNullOrWhiteSpace(correoPrincipalDocumento))
            destinatarios.Add(correoPrincipalDocumento);

        if (codCliente is > 0)
        {
            var cliente = await context.Clientes
                .AsNoTracking()
                .Where(c => c.Codcliente == codCliente.Value)
                .Select(c => new
                {
                    c.Correo,
                    CorreosAdicionales = context.ClientesCorreos
                        .Where(cc => cc.CodCliente == c.Codcliente && cc.Estado)
                        .OrderBy(cc => cc.Id)
                        .Select(cc => cc.Correo)
                        .ToList()
                })
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(cliente?.Correo))
                destinatarios.Add(cliente.Correo);

            if (cliente?.CorreosAdicionales != null)
                destinatarios.AddRange(cliente.CorreosAdicionales);
        }

        if (correosExtra != null)
            destinatarios.AddRange(
                correosExtra
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim()));

        return NormalizarCorreos(destinatarios);
    }

    public static List<string> NormalizarCorreos(IEnumerable<string?>? correos)
    {
        if (correos == null)
            return new List<string>();

        return correos
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
