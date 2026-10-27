/*
    Script maestro de migración de E-Fact.

    Requiere ejecutar en SSMS con SQLCMD Mode habilitado, desde la raíz del
    repositorio. Este archivo reúne las migraciones incrementales existentes
    en database; no reemplaza un backup de datos ni incluye el esquema base
    que no está versionado en esta carpeta.
*/

:on error exit
:setvar DatabaseScriptsPath "database"

PRINT 'Inicio de migración E-Fact';

-- Catálogo compartido y módulos de aliados.
:r $(DatabaseScriptsPath)\20260930_backoffice_catalogo_productos.sql
:r $(DatabaseScriptsPath)\20261005_aliado_facturas_liquidaciones.sql
:r $(DatabaseScriptsPath)\20261005_aliados_cuenta_bancaria.sql
:r $(DatabaseScriptsPath)\20261006_liquidacion_confirmacion_cliente.sql

-- Seguridad E-Contax y ajustes dependientes.
:r $(DatabaseScriptsPath)\20261005_econtax_seguridad.sql
:r $(DatabaseScriptsPath)\20261006_econtax_acciones_suspension.sql

-- Ajustes de documentos y firmas.
:r $(DatabaseScriptsPath)\20261006_backoffice_guias_notas_debito.sql
:r $(DatabaseScriptsPath)\20261006_firmas_backoffice_sin_cuenta.sql

-- Comisiones y declaraciones.
:r $(DatabaseScriptsPath)\20261008_comisiones_vendedores.sql
:r $(DatabaseScriptsPath)\20261012_edeclara_declaraciones.sql
:r $(DatabaseScriptsPath)\comisiones_edeclara.sql
:r $(DatabaseScriptsPath)\comisiones_edeclara_integridad.sql
:r $(DatabaseScriptsPath)\comisiones_aliados_integridad.sql

-- Compras, productos por sucursal, inventario y bodegas.
:r $(DatabaseScriptsPath)\20261020_econtax_documentos_sucursal.sql
:r $(DatabaseScriptsPath)\20261020_econtax_productos_sucursales.sql
:r $(DatabaseScriptsPath)\20261020_econtax_ordenes_compra.sql
:r $(DatabaseScriptsPath)\20261020_econtax_recepciones_compra.sql
:r $(DatabaseScriptsPath)\20261021_econtax_compras_inventario_complemento.sql
:r $(DatabaseScriptsPath)\20261023_econtax_bodegas.sql

PRINT 'Migración E-Fact finalizada';
