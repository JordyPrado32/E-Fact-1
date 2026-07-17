using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;
using System.Text;

namespace Simetric.Services;

public sealed class VendedorBackOfficeService
{
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaEnsured;
    private const string CodigoSistema = "SISTEMA";
    private const string CodigoSecretoPrefix = "ven_";

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDataProtector _protector;

    public VendedorBackOfficeService(
        IDbContextFactory<AppDbContext> dbFactory,
        IDataProtectionProvider dataProtectionProvider)
    {
        _dbFactory = dbFactory;
        _protector = dataProtectionProvider.CreateProtector("Simetric.VendedorBackOffice.LinkRegistro.v1");
    }

    public async Task EnsureSchemaAsync()
    {
        if (_schemaEnsured)
            return;

        await SchemaLock.WaitAsync();
        try
        {
            if (_schemaEnsured)
                return;

            await using var context = await _dbFactory.CreateDbContextAsync();
            foreach (var statement in BuildEnsureSchemaStatements())
                await context.Database.ExecuteSqlRawAsync(statement);

            _schemaEnsured = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    public async Task<List<VendedorBackOffice>> ListarAsync()
    {
        await EnsureSchemaAsync();

        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.VendedoresBackOffice
            .AsNoTracking()
            .OrderByDescending(x => x.EsSistema)
            .ThenBy(x => x.Nombre)
            .ToListAsync();
    }

    public async Task<VendedorBackOffice> ObtenerSistemaAsync()
    {
        await EnsureSchemaAsync();

        await using var context = await _dbFactory.CreateDbContextAsync();
        var existente = await context.VendedoresBackOffice
            .FirstOrDefaultAsync(x => x.EsSistema || x.CodigoReferencia == CodigoSistema);

        if (existente != null)
            return existente;

        var sistema = new VendedorBackOffice
        {
            Nombre = "Sistema",
            CodigoReferencia = CodigoSistema,
            Activo = true,
            EsSistema = true,
            FechaCreacion = DateTime.Now
        };

        context.VendedoresBackOffice.Add(sistema);
        await context.SaveChangesAsync();
        return sistema;
    }

    public async Task<VendedorBackOffice> ResolverParaRegistroAsync(string? codigoReferencia)
    {
        await EnsureSchemaAsync();

        if (string.IsNullOrWhiteSpace(codigoReferencia))
            return await ObtenerSistemaAsync();

        await using var context = await _dbFactory.CreateDbContextAsync();
        var vendedor = await ResolverPorCodigoSecretoAsync(context, codigoReferencia);
        if (vendedor is null)
        {
            var codigo = NormalizarCodigo(codigoReferencia);
            vendedor = await context.VendedoresBackOffice
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Activo && x.CodigoReferencia == codigo);
        }

        return vendedor ?? await ObtenerSistemaAsync();
    }

    public async Task<(bool Success, string Message, VendedorBackOffice? Vendedor)> CrearAsync(int idUsuario, string? nombre)
    {
        await EnsureSchemaAsync();

        var nombreNormalizado = (nombre ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nombreNormalizado))
            return (false, "Ingresa un nombre para el vendedor.", null);

        await using var context = await _dbFactory.CreateDbContextAsync();

        var codigoBase = GenerarCodigoBase(nombreNormalizado);
        var codigo = codigoBase;
        var sufijo = 1;

        while (await context.VendedoresBackOffice.AnyAsync(x => x.CodigoReferencia == codigo))
        {
            sufijo++;
            codigo = $"{codigoBase}{sufijo}";
        }

        var vendedor = new VendedorBackOffice
        {
            Nombre = nombreNormalizado,
            CodigoReferencia = codigo,
            Activo = true,
            EsSistema = false,
            IdUsuarioCreacion = idUsuario > 0 ? idUsuario : null,
            FechaCreacion = DateTime.Now
        };

        context.VendedoresBackOffice.Add(vendedor);
        await context.SaveChangesAsync();

        return (true, "Vendedor creado correctamente.", vendedor);
    }

    public async Task EnsurePerfilVendedorAsync(int idUsuario, string nombreCompleto)
    {
        await ObtenerPerfilUsuarioAsync(idUsuario, nombreCompleto);
    }

    public async Task<VendedorBackOffice?> ObtenerPerfilUsuarioAsync(int idUsuario, string? nombreCompleto = null)
    {
        await EnsureSchemaAsync();
        if (idUsuario <= 0)
            return null;

        await using var context = await _dbFactory.CreateDbContextAsync();

        var codigo = $"usr_{idUsuario}";
        var nombre = string.IsNullOrWhiteSpace(nombreCompleto) ? "Vendedor" : nombreCompleto.Trim();

        var vendedor = await context.VendedoresBackOffice
            .FirstOrDefaultAsync(x => x.CodigoReferencia == codigo);

        if (vendedor is null)
        {
            vendedor = await context.VendedoresBackOffice
                .FirstOrDefaultAsync(x => x.IdUsuarioCreacion == idUsuario && x.EsSistema == false);
        }

        if (vendedor is null)
        {
            var idVendedorUsuario = await context.Usuarios
                .Where(x => x.IdUsuario == idUsuario)
                .Select(x => x.IdVendedor)
                .FirstOrDefaultAsync();

            if (idVendedorUsuario.HasValue)
            {
                vendedor = await context.VendedoresBackOffice
                    .FirstOrDefaultAsync(x => x.IdVendedor == idVendedorUsuario.Value && x.EsSistema == false);
            }
        }

        if (vendedor is null)
        {
            vendedor = new VendedorBackOffice
            {
                Nombre = nombre,
                CodigoReferencia = codigo,
                Activo = true,
                EsSistema = false,
                IdUsuarioCreacion = idUsuario,
                FechaCreacion = DateTime.Now
            };
            context.VendedoresBackOffice.Add(vendedor);
            await context.SaveChangesAsync();
            return vendedor;
        }

        var changed = false;
        if (!string.Equals(vendedor.Nombre?.Trim(), nombre, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(nombre))
        {
            vendedor.Nombre = nombre;
            changed = true;
        }

        if (!string.Equals(vendedor.CodigoReferencia, codigo, StringComparison.OrdinalIgnoreCase))
        {
            var codigoDisponible = !await context.VendedoresBackOffice
                .AnyAsync(x => x.IdVendedor != vendedor.IdVendedor && x.CodigoReferencia == codigo);

            if (codigoDisponible)
            {
                vendedor.CodigoReferencia = codigo;
                changed = true;
            }
        }

        if (vendedor.IdUsuarioCreacion != idUsuario)
        {
            vendedor.IdUsuarioCreacion = idUsuario;
            changed = true;
        }

        if (changed)
        {
            await context.SaveChangesAsync();
        }

        return vendedor;
    }

    public string ObtenerCodigoSecreto(VendedorBackOffice vendedor)
    {
        if (vendedor is null)
            return string.Empty;

        var payload = $"{vendedor.IdVendedor}|{vendedor.CodigoReferencia}";
        var protegido = _protector.Protect(payload);
        var bytes = Encoding.UTF8.GetBytes(protegido);
        return $"{CodigoSecretoPrefix}{WebEncoders.Base64UrlEncode(bytes)}";
    }

    public string ConstruirRutaRegistro(VendedorBackOffice vendedor) =>
        $"/register?vendedor={Uri.EscapeDataString(ObtenerCodigoSecreto(vendedor))}";

    private static string GenerarCodigoBase(string nombre)
    {
        var limpio = new string(nombre
            .Trim()
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

        if (string.IsNullOrWhiteSpace(limpio))
            return "VENDEDOR";

        return limpio.Length <= 18 ? limpio : limpio[..18];
    }

    private static string NormalizarCodigo(string codigo)
    {
        var builder = new StringBuilder();
        foreach (var c in codigo.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(c);
        }

        return builder.Length == 0 ? CodigoSistema : builder.ToString();
    }

    private async Task<VendedorBackOffice?> ResolverPorCodigoSecretoAsync(AppDbContext context, string codigoSecreto)
    {
        if (!TryDesprotegerCodigoSecreto(codigoSecreto, out var idVendedor, out var codigoReferencia))
            return null;

        return await context.VendedoresBackOffice
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Activo &&
                x.IdVendedor == idVendedor &&
                x.CodigoReferencia == codigoReferencia);
    }

    private bool TryDesprotegerCodigoSecreto(string codigoSecreto, out int idVendedor, out string codigoReferencia)
    {
        idVendedor = 0;
        codigoReferencia = string.Empty;

        if (string.IsNullOrWhiteSpace(codigoSecreto) ||
            !codigoSecreto.StartsWith(CodigoSecretoPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var token = codigoSecreto[CodigoSecretoPrefix.Length..].Trim();
            var bytes = WebEncoders.Base64UrlDecode(token);
            var protegido = Encoding.UTF8.GetString(bytes);
            var payload = _protector.Unprotect(protegido);
            var partes = payload.Split('|', 2, StringSplitOptions.TrimEntries);
            if (partes.Length != 2 || !int.TryParse(partes[0], out idVendedor))
                return false;

            codigoReferencia = partes[1];
            return !string.IsNullOrWhiteSpace(codigoReferencia);
        }
        catch
        {
            idVendedor = 0;
            codigoReferencia = string.Empty;
            return false;
        }
    }

    private static IEnumerable<string> BuildEnsureSchemaStatements()
    {
        yield return """
IF OBJECT_ID(N'dbo.VENDEDOR_BACKOFFICE', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[VENDEDOR_BACKOFFICE](
        [idVendedor] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [nombre] NVARCHAR(120) NOT NULL,
        [codigoReferencia] NVARCHAR(60) NOT NULL,
        [activo] BIT NOT NULL CONSTRAINT [DF_VENDEDOR_BACKOFFICE_activo] DEFAULT(1),
        [esSistema] BIT NOT NULL CONSTRAINT [DF_VENDEDOR_BACKOFFICE_esSistema] DEFAULT(0),
        [idUsuarioCreacion] INT NULL,
        [fechaCreacion] DATETIME NOT NULL CONSTRAINT [DF_VENDEDOR_BACKOFFICE_fechaCreacion] DEFAULT(GETDATE())
    );
END
""";

        yield return """
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_VENDEDOR_BACKOFFICE_codigoReferencia'
      AND object_id = OBJECT_ID(N'dbo.VENDEDOR_BACKOFFICE')
)
BEGIN
    CREATE UNIQUE INDEX [UX_VENDEDOR_BACKOFFICE_codigoReferencia]
        ON [dbo].[VENDEDOR_BACKOFFICE]([codigoReferencia]);
END
""";

        yield return """
IF COL_LENGTH('dbo.Usuarios', 'idVendedor') IS NULL
BEGIN
    ALTER TABLE [dbo].[Usuarios] ADD [idVendedor] INT NULL;
END
""";
    }
}
