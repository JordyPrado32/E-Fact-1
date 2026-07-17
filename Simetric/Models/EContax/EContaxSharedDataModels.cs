namespace Simetric.Models.EContax;

public sealed record EContaxSharedDataSnapshot(
    int OwnerUserId,
    int TotalClientes,
    int TotalProductos,
    int TotalEmisores,
    IReadOnlyList<EContaxClientSummary> Clientes,
    IReadOnlyList<EContaxProductSummary> Productos,
    IReadOnlyList<EContaxIssuerSummary> Emisores);

public sealed record EContaxClientSummary(
    int Id,
    string Nombre,
    string Identificacion,
    string Correo,
    string Telefono,
    bool Activo);

public sealed record EContaxProductSummary(
    int Codigo,
    string Nombre,
    string CodigoPrincipal,
    decimal? ValorUnitario,
    string Tipo,
    bool Activo);

public sealed record EContaxIssuerSummary(
    int Codigo,
    string Nombre,
    string Ruc,
    bool Activo);
