using Microsoft.EntityFrameworkCore;
using Simetric.Data;

namespace Simetric.Services;

public enum InitialSetupStep
{
    None = 0,
    Emisor = 1,
    Caja = 2
}

public sealed class EmisorOnboardingService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public EmisorOnboardingService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public event Action? Changed;

    public InitialSetupStep RequiredStep { get; private set; }
    public bool RequiresInitialSetup => RequiredStep != InitialSetupStep.None;
    public bool RequiresEmisorSetup => RequiredStep == InitialSetupStep.Emisor;
    public bool RequiresCajaSetup => RequiredStep == InitialSetupStep.Caja;
    public string? RequiredRoute => RequiredStep switch
    {
        InitialSetupStep.Emisor => "/emisor",
        InitialSetupStep.Caja => "/mi-caja",
        _ => null
    };

    public async Task<bool> UserHasActiveEmisorAsync(int userId)
    {
        if (userId <= 0)
        {
            return false;
        }

        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.Emisores
            .AsNoTracking()
            .AnyAsync(e => e.IdUsuario == userId && e.Estado == true);
    }

    public async Task<bool> UserHasActiveCajaAsync(int userId)
    {
        if (userId <= 0)
        {
            return false;
        }

        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.Caja
            .AsNoTracking()
            .AnyAsync(c => c.IdUsuario == userId && (c.Estado == true || c.Estado == null));
    }

    public async Task<InitialSetupStep> RefreshRequirementAsync(int? userId)
    {
        if (userId is not > 0)
        {
            SetRequiredStep(InitialSetupStep.None);
            return RequiredStep;
        }

        await using var context = await _dbFactory.CreateDbContextAsync();
        var usuario = await context.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.IdUsuario == userId.Value);

        var usaEmisorCompartido = usuario?.estadoAsociado == true && usuario.idJefe is > 0;
        var idUsuarioEmisor = usaEmisorCompartido
            ? usuario!.idJefe!.Value
            : userId.Value;

        var tieneEmisorActivo = await context.Emisores
            .AsNoTracking()
            .AnyAsync(e => e.IdUsuario == idUsuarioEmisor && e.Estado == true);

        if (!usaEmisorCompartido && !tieneEmisorActivo)
        {
            SetRequiredStep(InitialSetupStep.Emisor);
            return RequiredStep;
        }

        var tieneCajaActiva = await context.Caja
            .AsNoTracking()
            .AnyAsync(c => c.IdUsuario == userId.Value && (c.Estado == true || c.Estado == null));

        if (!tieneCajaActiva)
        {
            SetRequiredStep(InitialSetupStep.Caja);
            return RequiredStep;
        }

        SetRequiredStep(InitialSetupStep.None);
        return RequiredStep;
    }

    public void SetRequiredStep(InitialSetupStep requiredStep)
    {
        if (RequiredStep == requiredStep)
        {
            return;
        }

        RequiredStep = requiredStep;
        Changed?.Invoke();
    }

    public void NotifyChanged() => Changed?.Invoke();
}
