using Dapper;
using Microsoft.Data.SqlClient;
using Simetric.Models.EDeclara;
using System.Data;

namespace Simetric.Services.EDeclara;

public sealed class ComisionesService
{
    private readonly string _connectionString;
    private readonly AuditService _audit;
    public ComisionesService(IConfiguration configuration, AuditService audit)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No existe DefaultConnection.");
        _audit = audit;
    }

    private IDbConnection Connection => new SqlConnection(_connectionString);

    public async Task<ComisionesResumen> ObtenerResumenAsync(int idEmpresa, DateTime? desde = null, DateTime? hasta = null)
    {
        using var db = Connection;
        const string sql = @"WITH filtrados AS (
                SELECT * FROM DECLARA_COMISION_MOVIMIENTO WHERE IdEmpresa=@idEmpresa
                AND (@desde IS NULL OR FechaGeneracion >= @desde) AND (@hasta IS NULL OR FechaGeneracion < DATEADD(day,1,@hasta))
            ), porFactura AS (
                SELECT IdFactura,MAX(CASE WHEN Estado IN ('Generada','Pendiente','Pagada') THEN ValorFacturado ELSE 0 END) Facturacion,
                    SUM(CASE WHEN Estado IN ('Generada','Pendiente','Pagada') THEN ComisionGenerada ELSE 0 END) Comision
                FROM filtrados GROUP BY IdFactura
            )
            SELECT COALESCE((SELECT SUM(ComisionGenerada) FROM filtrados WHERE Estado IN ('Generada','Pendiente','Pagada')),0) Generadas,
                COALESCE((SELECT SUM(ComisionGenerada) FROM filtrados WHERE Estado='Pendiente'),0) Pendientes,
                COALESCE((SELECT SUM(ComisionGenerada) FROM filtrados WHERE Estado='Pagada'),0) Pagadas,
                COALESCE((SELECT SUM(ComisionGenerada) FROM filtrados WHERE Estado='Revertida'),0) Revertidas,
                COALESCE((SELECT SUM(Facturacion) FROM porFactura),0) Facturacion,
                (SELECT COUNT(*) FROM DECLARA_COMISION_INVOLUCRADO WHERE IdEmpresa=@idEmpresa AND Activo=1) InvolucradosActivos,
                COALESCE((SELECT AVG(NULLIF(Comision,0)) FROM porFactura),0) PromedioPorFactura;";
        return await db.QuerySingleAsync<ComisionesResumen>(sql, new { idEmpresa, desde, hasta });
    }

    public async Task<IReadOnlyList<ComisionInvolucrado>> ListarInvolucradosAsync(int idEmpresa, string? filtro = null)
    {
        using var db = Connection;
        const string sql = @"SELECT i.Id,i.IdEmpresa,i.IdSucursal,i.Tipo,i.IdEmpleado,i.Identificacion,i.Nombre,i.Telefono,i.Correo,
            i.PorcentajePredeterminado,i.ValorPredeterminado,i.TipoCalculo,i.BaseCalculo,i.FechaVigencia,i.Activo,i.Observaciones,i.DatosBancarios,i.FechaCreacion,
            COALESCE(t.TotalGenerado,0) TotalGenerado,u.FechaGeneracion FechaUltimaComision,u.NumeroFactura NumeroUltimaFactura
            FROM DECLARA_COMISION_INVOLUCRADO i
            OUTER APPLY (
                SELECT SUM(m.ComisionGenerada) TotalGenerado
                FROM DECLARA_COMISION_MOVIMIENTO m
                WHERE m.IdEmpresa=i.IdEmpresa AND m.IdInvolucrado=i.Id AND m.Estado IN ('Generada','Pendiente','Pagada')
            ) t
            OUTER APPLY (
                SELECT TOP 1 m.FechaGeneracion,f.NumFactura NumeroFactura
                FROM DECLARA_COMISION_MOVIMIENTO m
                LEFT JOIN FACTURA f ON f.CODFACTURA=m.IdFactura
                WHERE m.IdEmpresa=i.IdEmpresa AND m.IdInvolucrado=i.Id
                ORDER BY m.FechaGeneracion DESC
            ) u
            WHERE i.IdEmpresa=@idEmpresa
            AND (@filtro IS NULL OR i.Nombre LIKE '%'+@filtro+'%' OR i.Identificacion LIKE '%'+@filtro+'%') ORDER BY i.Nombre;";
        var items = (await db.QueryAsync<ComisionInvolucrado>(sql, new { idEmpresa, filtro = string.IsNullOrWhiteSpace(filtro) ? null : filtro.Trim() })).ToList();
        foreach (var item in items) item.PorcentajePredeterminado = NormalizarPorcentaje(item.PorcentajePredeterminado);
        return items;
    }

    public async Task<int> GuardarInvolucradoAsync(ComisionInvolucrado item, int usuarioId)
    {
        if (item.IdEmpresa <= 0) throw new ArgumentException("La empresa es obligatoria.");
        if (item.TipoCalculo == TipoCalculoComision.Porcentaje && item.PorcentajePredeterminado is < 0 or > 100)
            throw new ArgumentException("El porcentaje debe estar entre 0 y 100.");
        if (item.TipoCalculo == TipoCalculoComision.ValorFijo && item.ValorPredeterminado < 0)
            throw new ArgumentException("El valor fijo no puede ser negativo.");
        if (item.TipoCalculo == TipoCalculoComision.Porcentaje && item.BaseCalculo is BaseCalculoComision.Utilidad or BaseCalculoComision.ValorFijo)
            throw new ArgumentException("El cálculo porcentual solo admite subtotal o total como base.");
        if (string.IsNullOrWhiteSpace(item.Identificacion) || string.IsNullOrWhiteSpace(item.Nombre)) throw new ArgumentException("Identificación y nombre son obligatorios.");
        item.Identificacion = item.Identificacion.Trim();
        item.Nombre = item.Nombre.Trim();
        item.Telefono = string.IsNullOrWhiteSpace(item.Telefono) ? null : item.Telefono.Trim();
        item.Correo = string.IsNullOrWhiteSpace(item.Correo) ? null : item.Correo.Trim();
        using var db = Connection;
        db.Open();
        using var tx = db.BeginTransaction();
        try
        {
            var duplicate = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM DECLARA_COMISION_INVOLUCRADO WHERE IdEmpresa=@empresa AND Identificacion=@ident AND Id<>@id", new { empresa=item.IdEmpresa, ident=item.Identificacion.Trim(), id=item.Id }, tx);
            if (duplicate > 0) throw new InvalidOperationException("Ya existe un involucrado con esa identificación.");
            var esNuevo = item.Id == 0;
            decimal? porcentajeAnterior = null;
            if (!esNuevo)
                porcentajeAnterior = await db.QuerySingleOrDefaultAsync<decimal?>("SELECT PorcentajePredeterminado FROM DECLARA_COMISION_INVOLUCRADO WHERE Id=@Id AND IdEmpresa=@IdEmpresa", new { item.Id, item.IdEmpresa }, tx);

            var parametros = new
            {
                item.Id,
                item.IdEmpresa,
                item.IdSucursal,
                Tipo = item.Tipo.ToString(),
                item.IdEmpleado,
                item.Identificacion,
                item.Nombre,
                item.Telefono,
                item.Correo,
                item.PorcentajePredeterminado,
                item.ValorPredeterminado,
                TipoCalculo = item.TipoCalculo.ToString(),
                BaseCalculo = item.BaseCalculo.ToString(),
                item.FechaVigencia,
                item.Activo,
                item.Observaciones,
                item.DatosBancarios,
                usuarioId
            };
            if (esNuevo)
                item.Id = await db.ExecuteScalarAsync<int>(@"INSERT INTO DECLARA_COMISION_INVOLUCRADO (IdEmpresa,IdSucursal,Tipo,IdEmpleado,Identificacion,Nombre,Telefono,Correo,PorcentajePredeterminado,ValorPredeterminado,TipoCalculo,BaseCalculo,FechaVigencia,Activo,Observaciones,DatosBancarios,CreadoPor)
                    VALUES (@IdEmpresa,@IdSucursal,@Tipo,@IdEmpleado,@Identificacion,@Nombre,@Telefono,@Correo,@PorcentajePredeterminado,@ValorPredeterminado,@TipoCalculo,@BaseCalculo,@FechaVigencia,@Activo,@Observaciones,@DatosBancarios,@usuarioId); SELECT CAST(SCOPE_IDENTITY() AS int);", parametros, tx);
            else
            {
                var afectados = await db.ExecuteAsync(@"UPDATE DECLARA_COMISION_INVOLUCRADO SET IdSucursal=@IdSucursal,Tipo=@Tipo,IdEmpleado=@IdEmpleado,Identificacion=@Identificacion,Nombre=@Nombre,Telefono=@Telefono,Correo=@Correo,PorcentajePredeterminado=@PorcentajePredeterminado,ValorPredeterminado=@ValorPredeterminado,TipoCalculo=@TipoCalculo,BaseCalculo=@BaseCalculo,FechaVigencia=@FechaVigencia,Activo=@Activo,Observaciones=@Observaciones,DatosBancarios=@DatosBancarios,ModificadoPor=@usuarioId,FechaModificacion=SYSUTCDATETIME() WHERE Id=@Id AND IdEmpresa=@IdEmpresa", parametros, tx);
                if (afectados == 0) throw new InvalidOperationException("El involucrado ya no existe.");
            }
            await db.ExecuteAsync("INSERT INTO DECLARA_COMISION_CONFIG_HISTORIAL (IdInvolucrado,PorcentajeAnterior,PorcentajeNuevo,UsuarioId,Fecha,Motivo) VALUES (@Id,@anterior,@nuevo,@usuarioId,SYSUTCDATETIME(),@motivo)", new { Id=item.Id, anterior=porcentajeAnterior, nuevo=item.PorcentajePredeterminado, usuarioId, motivo=esNuevo ? "Alta de configuración" : "Actualización de configuración" }, tx);
            tx.Commit();
            await _audit.RegistrarAuditoriaAsync(usuarioId, esNuevo ? "CREAR" : "MODIFICAR", null, item,
                new { Modulo = "Comisiones", Entidad = "Involucrado", Id = item.Id });
            return item.Id;
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task<IReadOnlyList<ComisionMovimiento>> ListarMovimientosAsync(
        int idEmpresa, ComisionEstado? estado = null, DateTime? desde = null, DateTime? hasta = null, string? filtro = null, int limite = 250)
    {
        using var db = Connection;
        const string sql = @"SELECT TOP (@limite) m.Id,m.IdFactura,f.NumFactura NumeroFactura,c.Nombrerazonsocial Cliente,m.IdInvolucrado,i.Nombre Involucrado,i.Tipo TipoInvolucrado,m.BaseUtilizada,m.Porcentaje,m.ValorFacturado,m.ComisionGenerada,m.Estado,m.FechaGeneracion,m.FechaPago,m.MetodoPago,m.ReferenciaPago,m.Motivo
            FROM DECLARA_COMISION_MOVIMIENTO m JOIN DECLARA_COMISION_INVOLUCRADO i ON i.Id=m.IdInvolucrado LEFT JOIN FACTURA f ON f.CODFACTURA=m.IdFactura LEFT JOIN CLIENTES c ON c.CODCLIENTE=f.CODCLIENTES
            WHERE m.IdEmpresa=@idEmpresa AND (@estado IS NULL OR m.Estado=@estado)
            AND (@desde IS NULL OR m.FechaGeneracion >= @desde)
            AND (@hasta IS NULL OR m.FechaGeneracion < DATEADD(day,1,@hasta))
            AND (@filtro IS NULL OR i.Nombre LIKE '%'+@filtro+'%' OR f.NumFactura LIKE '%'+@filtro+'%' OR c.Nombrerazonsocial LIKE '%'+@filtro+'%')
            ORDER BY m.FechaGeneracion DESC;";
        return (await db.QueryAsync<ComisionMovimiento>(sql, new
        {
            idEmpresa,
            estado = estado?.ToString(),
            desde,
            hasta,
            filtro = string.IsNullOrWhiteSpace(filtro) ? null : filtro.Trim(),
            limite = Math.Clamp(limite, 1, 500)
        })).ToList();
    }

    public async Task<ComisionConfiguracion> ObtenerConfiguracionAsync(int idEmpresa)
    {
        using var db = Connection;
        return await db.QuerySingleOrDefaultAsync<ComisionConfiguracion>(
            "SELECT TOP 1 IdEmpresa,LimitePorFactura,ReglaRedondeo,MomentoPendiente,PermitirModificarPorcentaje,RequiereAutorizacionSobreLimite FROM DECLARA_COMISION_CONFIG WHERE IdEmpresa=@idEmpresa",
            new { idEmpresa }) ?? new ComisionConfiguracion { IdEmpresa = idEmpresa };
    }

    public async Task<IReadOnlyList<ComisionHistorial>> ListarHistorialAsync(int idEmpresa, int limite = 12)
    {
        try
        {
            using var db = Connection;
            const string sql = @"SELECT TOP (@limite) h.Id,h.IdInvolucrado,i.Nombre Involucrado,h.PorcentajeAnterior,h.PorcentajeNuevo,h.Fecha,h.Motivo
                FROM DECLARA_COMISION_CONFIG_HISTORIAL h
                INNER JOIN DECLARA_COMISION_INVOLUCRADO i ON i.Id=h.IdInvolucrado
                WHERE i.IdEmpresa=@idEmpresa ORDER BY h.Fecha DESC,h.Id DESC;";
            var items = (await db.QueryAsync<ComisionHistorial>(sql, new { idEmpresa, limite = Math.Clamp(limite, 1, 100) })).ToList();
            foreach (var item in items)
            {
                if (item.PorcentajeAnterior.HasValue) item.PorcentajeAnterior = NormalizarPorcentaje(item.PorcentajeAnterior.Value);
                item.PorcentajeNuevo = NormalizarPorcentaje(item.PorcentajeNuevo);
            }
            return items;
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            return [];
        }
    }

    public async Task GuardarConfiguracionAsync(ComisionConfiguracion configuracion, int usuarioId)
    {
        if (configuracion.IdEmpresa <= 0) throw new ArgumentException("La empresa es obligatoria.");
        if (configuracion.LimitePorFactura < 0) throw new ArgumentException("El límite por factura no puede ser negativo.");
        configuracion.ReglaRedondeo = string.IsNullOrWhiteSpace(configuracion.ReglaRedondeo) ? "2 decimales" : configuracion.ReglaRedondeo.Trim();
        configuracion.MomentoPendiente = string.IsNullOrWhiteSpace(configuracion.MomentoPendiente) ? "Al autorizar" : configuracion.MomentoPendiente.Trim();

        using var db = Connection;
        await db.ExecuteAsync(@"SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            UPDATE DECLARA_COMISION_CONFIG WITH (UPDLOCK, SERIALIZABLE)
            SET LimitePorFactura=@LimitePorFactura,ReglaRedondeo=@ReglaRedondeo,
                MomentoPendiente=@MomentoPendiente,PermitirModificarPorcentaje=@PermitirModificarPorcentaje,
                RequiereAutorizacionSobreLimite=@RequiereAutorizacionSobreLimite WHERE IdEmpresa=@IdEmpresa;
            IF @@ROWCOUNT=0 INSERT INTO DECLARA_COMISION_CONFIG
                (IdEmpresa,LimitePorFactura,ReglaRedondeo,MomentoPendiente,PermitirModificarPorcentaje,RequiereAutorizacionSobreLimite)
                VALUES (@IdEmpresa,@LimitePorFactura,@ReglaRedondeo,@MomentoPendiente,@PermitirModificarPorcentaje,@RequiereAutorizacionSobreLimite);
            COMMIT TRANSACTION;", configuracion);
        await _audit.RegistrarAuditoriaAsync(usuarioId, "MODIFICAR", null, configuracion,
            new { Modulo = "Comisiones", Entidad = "Configuración" });
    }

    // Debe invocarse dentro del mismo flujo transaccional que confirma la factura.
    // La restricción única IdFactura + IdInvolucrado hace idempotentes los reintentos.
    public async Task RegistrarComisionesFacturaAsync(int idEmpresa, int idFactura, decimal subtotal, decimal total,
        IReadOnlyCollection<ComisionFacturaDetalle> detalles, int usuarioId, bool facturaEmitida)
    {
        if (!facturaEmitida || detalles.Count == 0) return;
        if (idEmpresa <= 0 || idFactura <= 0) throw new ArgumentException("La empresa y la factura son obligatorias.");
        if (detalles.Any(x => x.Porcentaje is < 0 or > 100 || x.ValorFijo < 0))
            throw new ArgumentException("La comisión contiene valores inválidos.");
        using var db = Connection; db.Open(); using var tx = db.BeginTransaction();
        try
        {
            var configuracion = await db.QuerySingleOrDefaultAsync<ComisionConfiguracion>(
                "SELECT TOP 1 IdEmpresa,LimitePorFactura,ReglaRedondeo,MomentoPendiente,PermitirModificarPorcentaje,RequiereAutorizacionSobreLimite FROM DECLARA_COMISION_CONFIG WHERE IdEmpresa=@idEmpresa",
                new { idEmpresa }, tx) ?? new ComisionConfiguracion { IdEmpresa = idEmpresa };
            var involucrados = (await db.QueryAsync<ComisionInvolucrado>("SELECT * FROM DECLARA_COMISION_INVOLUCRADO WHERE IdEmpresa=@idEmpresa AND Activo=1 AND Id IN @ids", new { idEmpresa, ids=detalles.Select(x=>x.IdInvolucrado) }, tx)).ToDictionary(x=>x.Id);
            foreach (var detalle in detalles)
            {
                if (!involucrados.TryGetValue(detalle.IdInvolucrado, out var involucrado)) throw new InvalidOperationException("El involucrado no está activo o no pertenece a la empresa.");
                if (detalle.Base == BaseCalculoComision.Utilidad)
                    throw new InvalidOperationException("La comisión sobre utilidad requiere una base de costos y todavía no puede calcularse en la factura.");
                var baseCalculo = detalle.Base == BaseCalculoComision.Total ? total : subtotal;
                var porcentaje = NormalizarPorcentaje(configuracion.PermitirModificarPorcentaje ? detalle.Porcentaje : involucrado.PorcentajePredeterminado);
                var valorFijo = configuracion.PermitirModificarPorcentaje ? detalle.ValorFijo : involucrado.ValorPredeterminado;
                var valorSinRedondear = involucrado.TipoCalculo == TipoCalculoComision.ValorFijo ? valorFijo : baseCalculo * porcentaje / 100m;
                var valor = configuracion.ReglaRedondeo.Equals("Sin decimales", StringComparison.OrdinalIgnoreCase)
                    ? Math.Round(valorSinRedondear, 0, MidpointRounding.AwayFromZero)
                    : Math.Round(valorSinRedondear, 2, MidpointRounding.AwayFromZero);
                var sobreLimite = configuracion.LimitePorFactura > 0 && valor > configuracion.LimitePorFactura;
                var estado = configuracion.MomentoPendiente.Equals("Al autorizar", StringComparison.OrdinalIgnoreCase) && !(sobreLimite && configuracion.RequiereAutorizacionSobreLimite)
                    ? ComisionEstado.Pendiente.ToString()
                    : ComisionEstado.Generada.ToString();
                await db.ExecuteAsync(@"INSERT INTO DECLARA_COMISION_MOVIMIENTO (IdEmpresa,IdFactura,IdInvolucrado,BaseUtilizada,Porcentaje,ValorFacturado,ComisionGenerada,Estado,FechaGeneracion,UsuarioResponsable,Motivo) SELECT @idEmpresa,@idFactura,@idInvolucrado,@base,@porcentaje,@valorFacturado,@valor,@estado,SYSUTCDATETIME(),@usuarioId,@motivo WHERE NOT EXISTS (SELECT 1 FROM DECLARA_COMISION_MOVIMIENTO WHERE IdFactura=@idFactura AND IdInvolucrado=@idInvolucrado)", new { idEmpresa,idFactura,detalle.IdInvolucrado, @base=detalle.Base.ToString(), porcentaje, valorFacturado=baseCalculo, valor, estado, usuarioId, motivo = sobreLimite && configuracion.RequiereAutorizacionSobreLimite ? "Supera el límite configurado; requiere autorización. " + detalle.Motivo : detalle.Motivo }, tx);
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public async Task RegistrarPagoAsync(int idEmpresa, IEnumerable<long> ids, string metodoPago, string? referencia, int usuarioId)
    {
        var movimientoIds = ids.Distinct().ToArray();
        if (movimientoIds.Length == 0) throw new ArgumentException("Selecciona al menos una comisión.");
        if (string.IsNullOrWhiteSpace(metodoPago)) throw new ArgumentException("El método de pago es obligatorio.");
        using var db = Connection;
        var elegibles = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM DECLARA_COMISION_MOVIMIENTO WHERE IdEmpresa=@idEmpresa AND Id IN @ids AND Estado='Pendiente'", new { idEmpresa, ids=movimientoIds });
        if (elegibles != movimientoIds.Length) throw new InvalidOperationException("Todas las comisiones seleccionadas deben estar pendientes.");
        var afectados = await db.ExecuteAsync("UPDATE m SET Estado='Pagada',FechaPago=SYSUTCDATETIME(),MetodoPago=@metodoPago,ReferenciaPago=@referencia,UsuarioResponsable=@usuarioId FROM DECLARA_COMISION_MOVIMIENTO m WHERE m.IdEmpresa=@idEmpresa AND m.Id IN @ids AND m.Estado='Pendiente'", new { idEmpresa, ids=movimientoIds, metodoPago=metodoPago.Trim(), referencia=referencia?.Trim(), usuarioId });
        if (afectados == 0) throw new InvalidOperationException("Las comisiones seleccionadas deben estar pendientes para registrar el pago.");
        await _audit.RegistrarAuditoriaAsync(usuarioId, "PAGAR", null, new { Ids = movimientoIds, MetodoPago = metodoPago, Referencia = referencia }, new { Modulo = "Comisiones", Entidad = "Movimiento" });
    }

    public async Task MarcarPendientesAsync(int idEmpresa, IEnumerable<long> ids, int usuarioId)
    {
        var movimientoIds = ids.Distinct().ToArray();
        if (movimientoIds.Length == 0) throw new ArgumentException("Selecciona al menos una comisión.");
        using var db = Connection;
        var elegibles = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM DECLARA_COMISION_MOVIMIENTO WHERE IdEmpresa=@idEmpresa AND Id IN @ids AND Estado='Generada'", new { idEmpresa, ids=movimientoIds });
        if (elegibles != movimientoIds.Length) throw new InvalidOperationException("Todas las comisiones seleccionadas deben estar generadas.");
        var afectados = await db.ExecuteAsync("UPDATE DECLARA_COMISION_MOVIMIENTO SET Estado='Pendiente',UsuarioResponsable=@usuarioId WHERE IdEmpresa=@idEmpresa AND Id IN @ids AND Estado='Generada'", new { idEmpresa, ids=movimientoIds, usuarioId });
        if (afectados == 0) throw new InvalidOperationException("Solo las comisiones generadas pueden pasar a pendientes.");
        await _audit.RegistrarAuditoriaAsync(usuarioId, "MARCAR_PENDIENTE", null, new { Ids = movimientoIds }, new { Modulo = "Comisiones", Entidad = "Movimiento" });
    }

    public async Task CambiarEstadoInvolucradoAsync(int idEmpresa, int id, bool activo, int usuarioId)
    {
        using var db = Connection;
        var afectados = await db.ExecuteAsync("UPDATE DECLARA_COMISION_INVOLUCRADO SET Activo=@activo,ModificadoPor=@usuarioId,FechaModificacion=SYSUTCDATETIME() WHERE Id=@id AND IdEmpresa=@idEmpresa", new { idEmpresa, id, activo, usuarioId });
        if (afectados == 0) throw new InvalidOperationException("El involucrado ya no existe.");
        await _audit.RegistrarAuditoriaAsync(usuarioId, activo ? "ACTIVAR" : "ELIMINAR_LOGICO", null, new { Id = id, Activo = activo }, new { Modulo = "Comisiones", Entidad = "Involucrado" });
    }

    public Task DesactivarInvolucradoAsync(int idEmpresa, int id, int usuarioId) => CambiarEstadoInvolucradoAsync(idEmpresa, id, false, usuarioId);

    public async Task RevertirFacturaAsync(int idEmpresa, int idFactura, string motivo, int usuarioId)
    {
        using var db=Connection; await db.ExecuteAsync("UPDATE DECLARA_COMISION_MOVIMIENTO SET Estado='Revertida',Motivo=@motivo,UsuarioResponsable=@usuarioId WHERE IdEmpresa=@idEmpresa AND IdFactura=@idFactura AND Estado NOT IN ('Revertida','Anulada')", new { idEmpresa,idFactura,motivo,usuarioId });
    }

    private static decimal NormalizarPorcentaje(decimal valor) => valor > 100m && valor <= 10000m ? valor / 1000m : valor;
}
