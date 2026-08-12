namespace Simetric.Services;

public static class BackOfficePermissionHelper
{
    public const int SuperAdministradorRoleId = 2;
    public const int BackOfficeRoleId = 7;
    public const int AdministradorBackOfficeTipoCliente = 1;
    public const int CobranzasBackOfficeTipoCliente = 3;

    public static bool PuedeAprobarTransferencias(int? idTipoUsuario, int? tipoCliente) =>
        idTipoUsuario == SuperAdministradorRoleId ||
        (idTipoUsuario == BackOfficeRoleId &&
            (tipoCliente == AdministradorBackOfficeTipoCliente ||
             tipoCliente == CobranzasBackOfficeTipoCliente));
}
