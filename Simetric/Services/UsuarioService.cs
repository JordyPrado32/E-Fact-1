using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;
using Simetric.ViewModels;
using Simetric.Components.Helpers;
using System.ComponentModel.DataAnnotations;

namespace Simetric.Services
{
    public interface IUsuarioService
    {
        Task<Usuario?> ObtenerUsuarioPorId(int id);
        Task<bool> ActualizarPerfil(int id, PerfilViewModel modelo);
        Task<bool> ActualizarAvatar(int id, string? avatarUrl);
    }

    public class UsuarioService : IUsuarioService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        public UsuarioService(IDbContextFactory<AppDbContext> contextFactory) => _contextFactory = contextFactory;

        public async Task<Usuario?> ObtenerUsuarioPorId(int id)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Usuarios.FindAsync(id);
        }

        public async Task<bool> ActualizarPerfil(int id, PerfilViewModel modelo)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            modelo.Nombres = NormalizarTexto(modelo.Nombres);
            modelo.Apellidos = NormalizarTexto(modelo.Apellidos);
            modelo.NombreEmpresa = modelo.NombreEmpresa?.Trim();
            modelo.DireccionEmpresa = modelo.DireccionEmpresa?.Trim();
            modelo.Celular = modelo.Celular?.Trim();
            modelo.Identificacion = modelo.Identificacion?.Trim();

            var contextoValidacion = new ValidationContext(modelo);
            var resultados = new List<ValidationResult>();
            if (!Validator.TryValidateObject(modelo, contextoValidacion, resultados, validateAllProperties: true))
            {
                return false;
            }

            var usuarioDb = await context.Usuarios.FindAsync(id);
            if (usuarioDb == null) return false;

            // 1. Actualizamos datos básicos
            usuarioDb.Nombres = modelo.TipoCliente == 2 ? modelo.NombreEmpresa! : modelo.Nombres;
            usuarioDb.Apellidos = modelo.TipoCliente == 2 ? string.Empty : modelo.Apellidos;
            usuarioDb.NombreEmpresa = modelo.TipoCliente == 2 ? modelo.NombreEmpresa : null;
            usuarioDb.DireccionEmpresa = modelo.DireccionEmpresa;
            usuarioDb.Celular = modelo.Celular;
            usuarioDb.Identificacion = modelo.Identificacion;
            usuarioDb.IdTipoIdentificacion = modelo.IdTipoIdentificacion;

            // CORRECCIÓN: Guardamos la ruta (string), no los bytes.
            // Esto guardará algo como "Images/Avatars/geek.png"
            usuarioDb.AvatarUrl = modelo.AvatarUrl;

            // 2. La fecha de nacimiento ahora puede actualizarse desde el perfil.
            usuarioDb.FechaNacimiento = modelo.FechaNacimiento?.Date;
            usuarioDb.TipoCliente = modelo.TipoCliente;

            // 3. Lógica de Password usando SecurityHelper
            if (!string.IsNullOrWhiteSpace(modelo.NuevaPassword))
            {
                usuarioDb.PasswordHash = SecurityHelper.HashPassword(modelo.NuevaPassword);
                usuarioDb.ClaveTemporal = false;
            }

            // 4. Persistencia
            context.Usuarios.Update(usuarioDb);
            return await context.SaveChangesAsync() > 0;
        }

        public async Task<bool> ActualizarAvatar(int id, string? avatarUrl)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var usuarioDb = await context.Usuarios.FindAsync(id);
            if (usuarioDb == null)
            {
                return false;
            }

            usuarioDb.AvatarUrl = avatarUrl;
            context.Usuarios.Update(usuarioDb);
            return await context.SaveChangesAsync() > 0;
        }

        private static string NormalizarTexto(string? texto) =>
            string.Join(
                " ",
                (texto ?? string.Empty)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
