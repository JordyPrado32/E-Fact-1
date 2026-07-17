using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;
using System.Xml.Linq;

namespace Simetric.Services;

public class RetencionesService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    public RetencionesService(IDbContextFactory<AppDbContext> dbFactory) => _dbFactory = dbFactory;

    // ========================= IVA =========================
    public async Task<List<RetencionIva>> GetIvaAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.RetencionIva.OrderBy(x => x.Codigo).ToListAsync();
    }

    public async Task SaveIvaAsync(RetencionIva model, bool isEdit)
    {
        ValidarCodigoNumero(model.Codigo, model.Descripcion);

        using var db = await _dbFactory.CreateDbContextAsync();

        if (!isEdit)
        {
            if (await db.RetencionIva.AnyAsync(x => x.Codigo == model.Codigo))
                throw new Exception("Ese código ya existe.");
            db.RetencionIva.Add(model);
        }
        else db.RetencionIva.Update(model);

        await db.SaveChangesAsync();
    }

    public async Task DeleteIvaAsync(int codigo)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var item = await db.RetencionIva.FindAsync(codigo);
        if (item is null) return;
        db.RetencionIva.Remove(item);
        await db.SaveChangesAsync();
    }

    // ========================= ISD =========================
    public async Task<List<RetencionIsd>> GetIsdAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.RetencionIsd.OrderBy(x => x.Codigo).ToListAsync();
    }

    public async Task SaveIsdAsync(RetencionIsd model, bool isEdit)
    {
        ValidarCodigoNumero(model.Codigo, model.Descripcion);

        using var db = await _dbFactory.CreateDbContextAsync();

        if (!isEdit)
        {
            if (await db.RetencionIsd.AnyAsync(x => x.Codigo == model.Codigo))
                throw new Exception("Ese código ya existe.");
            db.RetencionIsd.Add(model);
        }
        else db.RetencionIsd.Update(model);

        await db.SaveChangesAsync();
    }

    public async Task DeleteIsdAsync(int codigo)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var item = await db.RetencionIsd.FindAsync(codigo);
        if (item is null) return;
        db.RetencionIsd.Remove(item);
        await db.SaveChangesAsync();
    }
// ========================= RENTA =========================
public async Task<List<RetencionRenta>> GetRentaAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.RetencionRenta.OrderBy(x => x.Codigo).ToListAsync();
    }

    public async Task SaveRentaAsync(RetencionRenta model, bool isEdit)
    {
        // Codigo en BD = varchar(50) -> validar como texto
        ValidarCodigoTexto(model.Codigo, model.Descripcion);

        using var db = await _dbFactory.CreateDbContextAsync();

        if (!isEdit)
        {
            if (await db.RetencionRenta.AnyAsync(x => x.Codigo == model.Codigo))
                throw new Exception("Ese código ya existe.");

            model.Estado ??= true;
            db.RetencionRenta.Add(model);
        }
        else db.RetencionRenta.Update(model);

        await db.SaveChangesAsync();
    }

    public async Task DeleteRentaAsync(string codigo)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var item = await db.RetencionRenta.FindAsync(codigo);
        if (item is null) return;

        db.RetencionRenta.Remove(item);
        await db.SaveChangesAsync();
    }

    // ========================= COMPRASRETVALOR =========================
    public async Task<List<CompraRetValor>> GetComprasRetValorAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.ComprasRetValor.OrderByDescending(x => x.Sec).ToListAsync();
    }

    public async Task SaveComprasRetValorAsync(CompraRetValor model, bool isEdit)
    {
        if (string.IsNullOrWhiteSpace(model.Tipo))
            throw new Exception("Tipo es obligatorio (IVA / ISD / RENTA).");

        model.Tipo = model.Tipo.Trim().ToUpperInvariant();

        if (model.Tipo is not ("IVA" or "ISD" or "RENTA"))
            throw new Exception("Tipo inválido. Use: IVA, ISD o RENTA.");

        if (model.IdRet is null)
            throw new Exception("IdRet es obligatorio.");

        using var db = await _dbFactory.CreateDbContextAsync();

        // Valida que exista el idRet según el tipo
        await ValidarIdRetAsync(db, model.Tipo, model.IdRet.Value);

        if (!isEdit) db.ComprasRetValor.Add(model);
        else db.ComprasRetValor.Update(model);

        await db.SaveChangesAsync();
    }

    public async Task DeleteComprasRetValorAsync(int sec)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var item = await db.ComprasRetValor.FindAsync(sec);
        if (item is null) return;
        db.ComprasRetValor.Remove(item);
        await db.SaveChangesAsync();
    }

    // ========================= VALIDACIONES =========================

    // Para IVA/ISD (codigo = int)
    private static void ValidarCodigoNumero(int codigo, string? descripcion)
    {
        if (codigo <= 0) throw new Exception("El código es obligatorio.");
        if (string.IsNullOrWhiteSpace(descripcion)) throw new Exception("La descripción es obligatoria.");
    }

    // Para RENTA (codigo = varchar)
    private static void ValidarCodigoTexto(string? codigo, string? descripcion)
    {
        if (string.IsNullOrWhiteSpace(codigo)) throw new Exception("El código es obligatorio.");
        if (string.IsNullOrWhiteSpace(descripcion)) throw new Exception("La descripción es obligatoria.");
    }

    private static async Task ValidarIdRetAsync(AppDbContext db, string tipo, int idRet)
    {
        var ok = tipo switch
        {
            "IVA" => await db.RetencionIva.AnyAsync(x => x.Codigo == idRet),
            "ISD" => await db.RetencionIsd.AnyAsync(x => x.Codigo == idRet),

            // OJO: RENTA.codigo es varchar(50), por eso comparamos con ToString()
            "RENTA" => await db.RetencionRenta.AnyAsync(x => x.Codigo == idRet.ToString()),

            _ => false
        };

        if (!ok) throw new Exception("La retención indicada (IdRet) no existe para el tipo seleccionado.");
    }
    public class RetencionLookupDto
    {
        public int Codigo { get; set; }
        public string Descripcion { get; set; } = "";
        public decimal? Valor { get; set; }
    }

    public async Task<RetencionLookupDto?> BuscarRetencionAsync(string? tipo, int? codigo)
    {
        if (string.IsNullOrWhiteSpace(tipo) || codigo == null)
            return null;

        tipo = tipo.Trim().ToUpperInvariant();

        using var db = await _dbFactory.CreateDbContextAsync();

        if (tipo == "IVA")
        {
            return await db.RetencionIva
                .Where(x => x.Codigo == codigo.Value)
                .Select(x => new RetencionLookupDto
                {
                    Codigo = x.Codigo,
                    Descripcion = x.Descripcion ?? "",
                    Valor = x.Valor
                })
                .FirstOrDefaultAsync();
        }

        if (tipo == "RENTA")
        {
            string codTexto = codigo.Value.ToString();

            return await db.RetencionRenta
                .Where(x => x.Codigo == codTexto)
                .Select(x => new RetencionLookupDto
                {
                    Codigo = codigo.Value,
                    Descripcion = x.Descripcion ?? "",
                    Valor = x.ValorFinal ?? x.Valor
                })
                .FirstOrDefaultAsync();
        }

        return null;
    }
    // Dentro de RetencionesService.cs
    public async Task<List<RetencionBusquedaDto>> ListarPorTipoAsync(string tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo)) return new List<RetencionBusquedaDto>();

        tipo = tipo.Trim().ToUpperInvariant();
        using var db = await _dbFactory.CreateDbContextAsync();

        if (tipo == "IVA")
        {
            return await db.RetencionIva
                .OrderBy(x => x.Codigo)
                .Select(x => new RetencionBusquedaDto
                {
                    // Convertimos el Codigo (int) a string para que coincida con el DTO
                    Id = x.Codigo.ToString(),
                    Descripcion = x.Descripcion ?? "",
                    Valor = x.Valor
                })
                .ToListAsync();
        }

        if (tipo == "RENTA")
        {
            var data = await db.RetencionRenta
                .Where(x => x.Estado == true)
                .OrderBy(x => x.Codigo)
                .ToListAsync();

            return data.Select(x => new RetencionBusquedaDto
            {
                Id = x.Codigo, // Ahora se asigna directamente el varchar
                Descripcion = x.Descripcion ?? "",
                Valor = x.ValorFinal ?? x.Valor
            }).ToList();
        }

        

        return new List<RetencionBusquedaDto>();
    }
    
    public class RetencionBusquedaDto
    {
        public string Id { get; set; } = string.Empty;
        public string Descripcion { get; set; } = "";
        public decimal? Valor { get; set; }
    }
}
