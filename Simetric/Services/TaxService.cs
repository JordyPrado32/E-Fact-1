using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Services
{
    public class TaxService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public TaxService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        #region Codigos de Impuesto

        public async Task<List<Codigosimpuesto>> GetCodigosImpuestosAsync(string searchText = "", int skip = 0, int take = 10, bool includeInactivos = false)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            var query = context.Codigoimpuestos.AsQueryable();

            if (!includeInactivos)
                query = query.Where(x => x.Estado == "A");

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                query = query.Where(x =>
                    x.Codigo.Contains(searchText) ||
                    (x.Descripcion ?? "").Contains(searchText));
            }

            return await query
                .OrderBy(x => x.Codigo)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<int> GetTotalCodigosCountAsync(string searchText = "", bool includeInactivos = false)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            var query = context.Codigoimpuestos.AsQueryable();

            if (!includeInactivos)
                query = query.Where(x => x.Estado == "A");

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                query = query.Where(x =>
                    x.Codigo.Contains(searchText) ||
                    (x.Descripcion ?? "").Contains(searchText));
            }

            return await query.CountAsync();
        }

        public async Task<bool> ExisteCodigoImpuestoAsync(string codigo)
        {
            using var context = await _dbFactory.CreateDbContextAsync();

            codigo = (codigo ?? string.Empty).Trim().ToUpper();

            return await context.Codigoimpuestos
                .AnyAsync(x => x.Codigo.ToUpper() == codigo);
        }

        public async Task SaveCodigoAsync(Codigosimpuesto model)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            context.Codigoimpuestos.Add(model);
            await context.SaveChangesAsync();
        }

        public async Task UpdateCodigoAsync(Codigosimpuesto model)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            context.Codigoimpuestos.Update(model);
            await context.SaveChangesAsync();
        }

        public async Task SoftDeleteCodigoImpuestoAsync(string codigo)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            var item = await context.Codigoimpuestos.FindAsync(codigo);

            if (item != null)
            {
                item.Estado = "I";
                await context.SaveChangesAsync();
            }
        }

        #endregion

        #region Porcentaje IVA

        public async Task<List<Porcentajeiva>> GetPorcentajesIvaAsync(string searchText = "", int skip = 0, int take = 10, bool includeInactivos = false)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            var query = context.Porcentajeivas.AsQueryable();

            if (!includeInactivos)
                query = query.Where(x => x.Estado == "A");

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                query = query.Where(x =>
                    x.Codigo.Contains(searchText) ||
                    (x.Descripcion ?? "").Contains(searchText) ||
                    (x.Valor ?? "").Contains(searchText));
            }

            return await query
                .OrderBy(x => x.ValorCalculo)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task<int> GetTotalIvaCountAsync(string searchText = "", bool includeInactivos = false)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            var query = context.Porcentajeivas.AsQueryable();

            if (!includeInactivos)
                query = query.Where(x => x.Estado == "A");

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                query = query.Where(x =>
                    x.Codigo.Contains(searchText) ||
                    (x.Descripcion ?? "").Contains(searchText) ||
                    (x.Valor ?? "").Contains(searchText));
            }

            return await query.CountAsync();
        }

        public async Task<bool> ExistePorcentajeIvaAsync(string codigo)
        {
            using var context = await _dbFactory.CreateDbContextAsync();

            codigo = (codigo ?? string.Empty).Trim();

            return await context.Porcentajeivas
                .AnyAsync(x => x.Codigo == codigo);
        }

        public async Task SaveIvaAsync(Porcentajeiva model)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            context.Porcentajeivas.Add(model);
            await context.SaveChangesAsync();
        }

        public async Task UpdateIvaAsync(Porcentajeiva model)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            context.Porcentajeivas.Update(model);
            await context.SaveChangesAsync();
        }

        public async Task SoftDeletePorcentajeIvaAsync(string codigo)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            var item = await context.Porcentajeivas.FindAsync(codigo);

            if (item != null)
            {
                item.Estado = "I";
                await context.SaveChangesAsync();
            }
        }

        #endregion
    }
}