/* Integridad adicional del módulo E-Declara. No modifica tablas de Aliados. */
IF OBJECT_ID('dbo.DECLARA_COMISION_MOVIMIENTO','U') IS NULL
    THROW 50001, 'Debe ejecutarse primero database/comisiones_edeclara.sql.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UX_DECLARA_COMISION_MOV_IDEMPOTENCY'
      AND object_id = OBJECT_ID('dbo.DECLARA_COMISION_MOVIMIENTO')
)
    CREATE UNIQUE INDEX UX_DECLARA_COMISION_MOV_IDEMPOTENCY
        ON dbo.DECLARA_COMISION_MOVIMIENTO(IdempotencyKey)
        WHERE IdempotencyKey IS NOT NULL;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_DECLARA_COMISION_MOV_FACTURA_INVOLUCRADO'
      AND object_id = OBJECT_ID('dbo.DECLARA_COMISION_MOVIMIENTO')
)
    CREATE INDEX IX_DECLARA_COMISION_MOV_FACTURA_INVOLUCRADO
        ON dbo.DECLARA_COMISION_MOVIMIENTO(IdEmpresa, IdFactura, IdInvolucrado);

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DECLARA_COMISION_MOV_ESTADO')
   AND NOT EXISTS (
       SELECT 1 FROM dbo.DECLARA_COMISION_MOVIMIENTO
       WHERE Estado NOT IN ('Proyectada','Generada','Pendiente','Pagada','Revertida','Anulada')
   )
    ALTER TABLE dbo.DECLARA_COMISION_MOVIMIENTO
        ADD CONSTRAINT CK_DECLARA_COMISION_MOV_ESTADO
        CHECK (Estado IN ('Proyectada','Generada','Pendiente','Pagada','Revertida','Anulada'));

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DECLARA_COMISION_MOV_PORCENTAJE')
   AND NOT EXISTS (
       SELECT 1 FROM dbo.DECLARA_COMISION_MOVIMIENTO
       WHERE Porcentaje < 0 OR Porcentaje > 100
   )
    ALTER TABLE dbo.DECLARA_COMISION_MOVIMIENTO
        ADD CONSTRAINT CK_DECLARA_COMISION_MOV_PORCENTAJE
        CHECK (Porcentaje BETWEEN 0 AND 100);

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DECLARA_COMISION_MOV_VALOR')
   AND NOT EXISTS (
       SELECT 1 FROM dbo.DECLARA_COMISION_MOVIMIENTO
       WHERE ValorFacturado < 0 OR ComisionGenerada < 0
   )
    ALTER TABLE dbo.DECLARA_COMISION_MOVIMIENTO
        ADD CONSTRAINT CK_DECLARA_COMISION_MOV_VALOR
        CHECK (ValorFacturado >= 0 AND ComisionGenerada >= 0);
