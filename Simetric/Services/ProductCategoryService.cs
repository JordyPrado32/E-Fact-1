using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Services
{
    public class ProductCategoryService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public ProductCategoryService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // Método privado vital para resolver la jerarquía en cada operación
        private async Task<int> GetOwnerIdAsync(AppDbContext context, int idUsuario)
        {
            var usuario = await context.Usuarios
                .Where(u => u.IdUsuario == idUsuario)
                .Select(u => new { u.IdUsuario, u.idJefe })
                .FirstOrDefaultAsync();

            if (usuario == null) throw new Exception("Usuario no encontrado en el sistema.");

            // Si tiene jefe, el dueño es el jefe. Si no, es él mismo.
            return usuario.idJefe ?? usuario.IdUsuario;
        }

        #region Producto Tipo (Categorías)

        public async Task<List<Productotipo>> GetTiposAsync(int idUsuario)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            int idOwner = await GetOwnerIdAsync(context, idUsuario);

            return await context.Productotipos
                .Where(x => x.Idusuario == idOwner && x.Estado == true)
                .OrderBy(x => x.Descripcion)
                .ToListAsync();
        }

        public async Task SaveTipoAsync(Productotipo model, int idUsuario)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            int idOwner = await GetOwnerIdAsync(context, idUsuario);
            var descripcion = NormalizarDescripcion(model.Descripcion);

            if (string.IsNullOrWhiteSpace(descripcion))
                throw new InvalidOperationException("La descripcion de la categoria es obligatoria.");

            if (model.Idtipoproducto == 0)
            {
                var existente = await context.Productotipos
                    .FirstOrDefaultAsync(x =>
                        x.Idusuario == idOwner &&
                        x.Descripcion != null &&
                        x.Descripcion.Trim().ToUpper() == descripcion.ToUpper());

                if (existente is not null)
                {
                    existente.Descripcion = descripcion;
                    existente.Perecible = model.Perecible;
                    existente.Stockminimo = model.Stockminimo;
                    existente.Stockmaximo = model.Stockmaximo;
                    existente.Estado = true;
                }
                else
                {
                    model.Descripcion = descripcion;
                    model.Estado = true;
                    model.Idusuario = idOwner;
                    context.Productotipos.Add(model);
                }
            }
            else
            {
                var actual = await context.Productotipos
                    .FirstOrDefaultAsync(x => x.Idtipoproducto == model.Idtipoproducto && x.Idusuario == idOwner);

                if (actual == null)
                    throw new InvalidOperationException("No tiene permisos para editar esta categoría.");

                var duplicado = await context.Productotipos
                    .AnyAsync(x =>
                        x.Idusuario == idOwner &&
                        x.Idtipoproducto != model.Idtipoproducto &&
                        x.Estado == true &&
                        x.Descripcion != null &&
                        x.Descripcion.Trim().ToUpper() == descripcion.ToUpper());

                if (duplicado)
                    throw new InvalidOperationException("Ya existe una categoria activa con esa descripcion.");

                actual.Descripcion = descripcion;
                actual.Perecible = model.Perecible;
                actual.Stockminimo = model.Stockminimo;
                actual.Stockmaximo = model.Stockmaximo;
                actual.Estado = true;
            }

            await context.SaveChangesAsync();
        }

        public async Task SoftDeleteTipoAsync(int id, int idUsuario)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            int idOwner = await GetOwnerIdAsync(context, idUsuario);

            var item = await context.Productotipos
                .FirstOrDefaultAsync(x => x.Idtipoproducto == id && x.Idusuario == idOwner);

            if (item == null)
                throw new InvalidOperationException("No tiene permisos para desactivar esta categoría.");

            item.Estado = false;
            await context.SaveChangesAsync();
        }

        #endregion

        #region Producto Subtipo

        public async Task<List<Productosubtipo>> GetSubtiposAsync(int idUsuario)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            int idOwner = await GetOwnerIdAsync(context, idUsuario);

            return await context.Productosubtipos
                .Where(x => x.Idusuario == idOwner && x.Estado == "A")
                .Include(x => x.IdtipoproductoNavigation)
                .OrderBy(x => x.Descripcion)
                .ToListAsync();
        }

        public async Task SaveSubtipoAsync(Productosubtipo model, int idUsuario)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            int idOwner = await GetOwnerIdAsync(context, idUsuario);
            var descripcion = NormalizarDescripcion(model.Descripcion);

            if (string.IsNullOrWhiteSpace(descripcion))
                throw new InvalidOperationException("La descripcion de la subcategoria es obligatoria.");

            var tipoOk = await context.Productotipos
                .AnyAsync(t => t.Idtipoproducto == model.Idtipoproducto &&
                               t.Idusuario == idOwner &&
                               t.Estado == true);

            if (!tipoOk)
                throw new InvalidOperationException("La categoria seleccionada no pertenece a su grupo.");

            if (model.Idsubtipo == 0)
            {
                var existente = await context.Productosubtipos
                    .FirstOrDefaultAsync(x =>
                        x.Idusuario == idOwner &&
                        x.Idtipoproducto == model.Idtipoproducto &&
                        x.Descripcion != null &&
                        x.Descripcion.Trim().ToUpper() == descripcion.ToUpper());

                if (existente is not null)
                {
                    existente.Descripcion = descripcion;
                    existente.Idtipoproducto = model.Idtipoproducto;
                    existente.Estado = "A";
                }
                else
                {
                    model.Descripcion = descripcion;
                    model.Estado = "A";
                    model.Idusuario = idOwner;
                    context.Productosubtipos.Add(model);
                }
            }
            else
            {
                var actual = await context.Productosubtipos
                    .FirstOrDefaultAsync(x => x.Idsubtipo == model.Idsubtipo && x.Idusuario == idOwner);

                if (actual == null)
                    throw new InvalidOperationException("No tiene permisos para editar esta subcategoria.");

                var duplicado = await context.Productosubtipos
                    .AnyAsync(x =>
                        x.Idusuario == idOwner &&
                        x.Idsubtipo != model.Idsubtipo &&
                        x.Idtipoproducto == model.Idtipoproducto &&
                        x.Estado == "A" &&
                        x.Descripcion != null &&
                        x.Descripcion.Trim().ToUpper() == descripcion.ToUpper());

                if (duplicado)
                    throw new InvalidOperationException("Ya existe una subcategoria activa con esa descripcion para la categoria seleccionada.");

                actual.Descripcion = descripcion;
                actual.Idtipoproducto = model.Idtipoproducto;
                actual.Estado = "A";
            }

            await context.SaveChangesAsync();
        }

        public async Task SoftDeleteSubtipoAsync(int id, int idUsuario)
        {
            using var context = await _dbFactory.CreateDbContextAsync();
            int idOwner = await GetOwnerIdAsync(context, idUsuario);

            var item = await context.Productosubtipos
                .FirstOrDefaultAsync(x => x.Idsubtipo == id && x.Idusuario == idOwner);

            if (item == null)
                throw new InvalidOperationException("No tiene permisos para desactivar esta subcategoria.");

            item.Estado = "I";
            await context.SaveChangesAsync();
        }

        #endregion

        private static string NormalizarDescripcion(string? descripcion) =>
            (descripcion ?? string.Empty).Trim();
    }
}
