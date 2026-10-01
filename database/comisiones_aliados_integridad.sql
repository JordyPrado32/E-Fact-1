/* Integridad exclusiva del módulo de Comisiones de Aliados. */
IF OBJECT_ID('dbo.ALIADO_COMISION', 'U') IS NULL
    THROW 50002, 'Debe inicializarse primero el esquema del Portal de Aliados.', 1;
IF OBJECT_ID('dbo.ALIADO_LIQUIDACION', 'U') IS NULL
    THROW 50003, 'Debe inicializarse primero el esquema de liquidaciones de Aliados.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ALIADO_COMISION_FACTURA_ESTADO' AND object_id = OBJECT_ID('dbo.ALIADO_COMISION'))
    CREATE INDEX IX_ALIADO_COMISION_FACTURA_ESTADO
        ON dbo.ALIADO_COMISION(IdFactura, Estado)
        INCLUDE(IdVendedor, IdCliente, TipoComision, Valor, IdLiquidacion);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ALIADO_COMISION_LIQUIDACION' AND object_id = OBJECT_ID('dbo.ALIADO_COMISION'))
    CREATE INDEX IX_ALIADO_COMISION_LIQUIDACION
        ON dbo.ALIADO_COMISION(IdLiquidacion, Estado);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ALIADO_LIQUIDACION_PENDIENTE' AND object_id = OBJECT_ID('dbo.ALIADO_LIQUIDACION'))
   AND NOT EXISTS (
       SELECT 1
       FROM dbo.ALIADO_LIQUIDACION
       WHERE Estado = 'Pendiente'
       GROUP BY IdVendedor, IdCliente, Periodo
       HAVING COUNT(*) > 1
   )
    CREATE UNIQUE INDEX UX_ALIADO_LIQUIDACION_PENDIENTE
        ON dbo.ALIADO_LIQUIDACION(IdVendedor, IdCliente, Periodo)
        WHERE Estado = 'Pendiente';

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ALIADO_COMISION_ESTADO')
   AND NOT EXISTS (
       SELECT 1 FROM dbo.ALIADO_COMISION
       WHERE Estado NOT IN ('Generada','Pendiente','Aprobada','Pagada','Anulada','Revertida','AjustePendiente')
   )
    ALTER TABLE dbo.ALIADO_COMISION
        ADD CONSTRAINT CK_ALIADO_COMISION_ESTADO
        CHECK (Estado IN ('Generada','Pendiente','Aprobada','Pagada','Anulada','Revertida','AjustePendiente'));

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ALIADO_COMISION_PORCENTAJE')
   AND NOT EXISTS (
       SELECT 1 FROM dbo.ALIADO_COMISION
       WHERE Porcentaje < 0 OR Porcentaje > 100
   )
    ALTER TABLE dbo.ALIADO_COMISION
        ADD CONSTRAINT CK_ALIADO_COMISION_PORCENTAJE
        CHECK (Porcentaje BETWEEN 0 AND 100);

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ALIADO_LIQUIDACION_ESTADO')
   AND NOT EXISTS (
       SELECT 1 FROM dbo.ALIADO_LIQUIDACION
       WHERE Estado NOT IN ('Pendiente','Pagada','Anulada')
   )
    ALTER TABLE dbo.ALIADO_LIQUIDACION
        ADD CONSTRAINT CK_ALIADO_LIQUIDACION_ESTADO
        CHECK (Estado IN ('Pendiente','Pagada','Anulada'));
