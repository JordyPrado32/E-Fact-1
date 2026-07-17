using Microsoft.EntityFrameworkCore;

using Simetric.Models;
using Simetric.Models.EContax;
using System.Globalization;
using static Simetric.Models.Cliente;


namespace Simetric.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<ClienteCorreo> ClientesCorreos { get; set; }
        public DbSet<Auditoria> Auditorias { get; set; }
        public DbSet<BlacklistIp> BlacklistIps { get; set; }
        public DbSet<Caja> Caja { get; set; }
        public DbSet<Cliente> Clientes { get; set; }
        public DbSet<Codigosimpuesto> Codigoimpuestos { get; set; }
        public DbSet<Detallefactura> Detallefacturas { get; set; }
        public DbSet<Emisor> Emisores { get; set; }
        public DbSet<Factura> Facturas { get; set; }
        public DbSet<FormasPago> FormasPago { get; set; }
        public DbSet<AbonoMultiple> AbonoMultiples { get; set; }
        public DbSet<Abonos> Abonos { get; set; }
        public DbSet<AbonoTipo> AbonoTipos { get; set; }
        public DbSet<DetalleNotaCredito> DetallesNotaCredito { get; set; }
        public DbSet<DetalleNotaDebito> DetallesNotaDebito { get; set; }
        public DbSet<Identificacion> Identificacion { get; set; }
        public DbSet<LogIniciosSesion> LogIniciosSesiones { get; set; }
        public DbSet<Porcentajeiva> Porcentajeivas { get; set; }
        public DbSet<Producto> Productos { get; set; }
        public DbSet<Productosubtipo> Productosubtipos { get; set; }
        public DbSet<Productotipo> Productotipos { get; set; }
        public DbSet<Tipocliente> Tipoclientes { get; set; }
        public DbSet<Pais> Paises { get; set; }
        public DbSet<Provincia> Provincias { get; set; }
        public DbSet<Ciudad> Ciudades { get; set; }
        public DbSet<Retencion> Retencion { get; set; }
        public DbSet<RetencionIva> RetencionIva { get; set; }
        public DbSet<RetencionIsd> RetencionIsd { get; set; }
        public DbSet<RetencionRenta> RetencionRenta { get; set; }
        public DbSet<CompraRetValor> ComprasRetValor { get; set; }
        public DbSet<NotaCredito> NotaCreditos { get; set; }
        public DbSet<NotaDebito> NotaDebitos { get; set; }
        public DbSet<ComprasFactura> ComprasFacturas { get; set; }
        public DbSet<ComprasDetalleFac> ComprasDetalleFac { get; set; }


        public DbSet<UsuEstadoFirma> UsuEstadoFirma { get; set; }
        public DbSet<UsuSolicitudFirma> UsuSolicitudFirma { get; set; }
        public DbSet<UsuSolicitudDocumento> UsuSolicitudDocumento { get; set; }
        public DbSet<UsuSolicitudObservacion> UsuSolicitudObservacion { get; set; }
        public DbSet<UsuSolicitudEstadoHistorial> UsuSolicitudEstadoHistorial { get; set; }

        // Nota: el nombre de la propiedad no afecta el nombre de tabla, pero es confuso.
        // Si deseas, puedes renombrar a TipoIdentificaciones sin romper EF (solo cambia el nombre de la propiedad).
        public DbSet<TipoIdentificacion> TipoIdentificacion { get; set; }

        public DbSet<TipoUsuario> TipoUsuario { get; set; }
        public DbSet<TipoDocumento> TipoDocumento { get; set; }
        public DbSet<UserSession> UserSessions { get; set; }
        public DbSet<Usuario> Usuarios { get; set; }
        public DbSet<RetencionInfo> RetencionInfo { get; set; }
        public DbSet<Proveedor> Proveedores { get; set; }
        public DbSet<Transportista> Transportistas { get; set; }
        public DbSet<GuiaRemision> GuiasRemision { get; set; }  
        public DbSet<GuiaDestinatario> GuiaDestinatarios { get; set; }
        public DbSet<DetalleGuiaRemision> DetallesGuiaRemision { get; set; }
        public DbSet<ComprobanteCorreoEstado> ComprobantesCorreoEstado { get; set; }
        public DbSet<AppServicio> AppServicios { get; set; }
        public DbSet<UsuarioServicioSuscripcion> UsuarioServicioSuscripciones { get; set; }
        public DbSet<ReporteVentaBackOffice> ReporteVentasBackOffice { get; set; }
        public DbSet<VendedorBackOffice> VendedoresBackOffice { get; set; }
        public DbSet<ContribuyenteEdeclare> ContribuyentesEdeclare { get; set; }
        public DbSet<EContaxRol> EContaxRoles { get; set; }
        public DbSet<EContaxMenu> EContaxMenus { get; set; }
        public DbSet<EContaxEmpresa> EContaxEmpresas { get; set; }
        public DbSet<EContaxSucursal> EContaxSucursales { get; set; }
        public DbSet<EContaxUsuarioContexto> EContaxUsuariosContexto { get; set; }
        public DbSet<EdeclareTarjeta> EdeclareTarjetas { get; set; }
        public DbSet<EsignTarjeta> EsignTarjetas { get; set; }

        public override int SaveChanges()
        {
            NormalizarUsuarios();
            return base.SaveChanges();
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            NormalizarUsuarios();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            NormalizarUsuarios();
            return base.SaveChangesAsync(cancellationToken);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            NormalizarUsuarios();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void NormalizarUsuarios()
        {
            var cultura = CultureInfo.GetCultureInfo("es-EC");

            foreach (var entry in ChangeTracker.Entries<Usuario>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            {
                entry.Entity.Nombres = NormalizarTextoMayusculas(entry.Entity.Nombres, cultura);
                entry.Entity.Apellidos = NormalizarTextoMayusculas(entry.Entity.Apellidos, cultura);
            }
        }

        private static string NormalizarTextoMayusculas(string? value, CultureInfo culture)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var compactado = string.Join(" ", value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            return compactado.ToUpper(culture);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ComprobanteCorreoEstado>(entity =>
            {
                entity.ToTable("COMPROBANTECORREOESTADO", "dbo");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.TipoDocumento, e.DocumentoId })
                    .IsUnique();
            });

            modelBuilder.Entity<EContaxRol>(entity =>
            {
                entity.ToTable("rol", "dbo");
                entity.HasKey(e => e.IdRol);

                entity.Property(e => e.IdRol)
                    .ValueGeneratedNever()
                    .HasColumnName("id_rol");
                entity.Property(e => e.NombreRol)
                    .HasColumnName("nombre_rol")
                    .HasMaxLength(200)
                    .IsUnicode(false);
                entity.Property(e => e.PermisoRol).HasColumnName("permiso_rol");
                entity.Property(e => e.EstadoRol)
                    .HasColumnName("estado_rol")
                    .HasDefaultValue(1);
            });

            modelBuilder.Entity<EContaxMenu>(entity =>
            {
                entity.ToTable("menu", "dbo");
                entity.HasKey(e => e.IdMenu);

                entity.Property(e => e.IdMenu)
                    .ValueGeneratedNever()
                    .HasColumnName("id_menu");
                entity.Property(e => e.NombreMenu)
                    .HasColumnName("nombre_menu")
                    .HasMaxLength(200)
                    .IsUnicode(false);
                entity.Property(e => e.PerteneceMenu).HasColumnName("pertenece_menu");
                entity.Property(e => e.UrlMenu)
                    .HasColumnName("url_menu")
                    .HasMaxLength(200)
                    .IsUnicode(false);
                entity.Property(e => e.DescripcionMenu)
                    .HasColumnName("desc_menu")
                    .HasMaxLength(300)
                    .IsUnicode(false);
                entity.Property(e => e.IconoMenu)
                    .HasColumnName("icon_menu")
                    .HasMaxLength(50)
                    .IsUnicode(false);
                entity.Property(e => e.EstadoMenu)
                    .HasColumnName("estado_menu")
                    .HasDefaultValue(0);
                entity.Property(e => e.IdPadre).HasColumnName("id_padre");
                entity.Property(e => e.OrdenMenu).HasColumnName("orden_menu");
            });

            modelBuilder.Entity<EContaxEmpresa>(entity =>
            {
                entity.ToTable("EMPRESA", "dbo");
                entity.HasKey(e => e.IdEmpresa);

                entity.Property(e => e.IdEmpresa)
                    .ValueGeneratedNever()
                    .HasColumnName("idEmpresa");
                entity.Property(e => e.Nombre)
                    .HasColumnName("nombre")
                    .HasMaxLength(200);
                entity.Property(e => e.Ruc)
                    .HasColumnName("ruc")
                    .HasMaxLength(20);
                entity.Property(e => e.Estado)
                    .HasColumnName("estado")
                    .HasDefaultValue(true);
                entity.Property(e => e.FechaCreacion).HasColumnName("fechaCreacion");
                entity.Property(e => e.FechaActualizacion).HasColumnName("fechaActualizacion");
            });

            modelBuilder.Entity<EContaxSucursal>(entity =>
            {
                entity.ToTable("SUCURSAL", "dbo");
                entity.HasKey(e => new { e.IdEmpresa, e.IdSucursal });

                entity.Property(e => e.IdSucursal)
                    .ValueGeneratedNever()
                    .HasColumnName("idSucursal");
                entity.Property(e => e.IdEmpresa)
                    .ValueGeneratedNever()
                    .HasColumnName("idEmpresa");
                entity.Property(e => e.Nombre)
                    .HasColumnName("nombre")
                    .HasMaxLength(200);
                entity.Property(e => e.Codigo)
                    .HasColumnName("codigo")
                    .HasMaxLength(20);
                entity.Property(e => e.Direccion)
                    .HasColumnName("direccion")
                    .HasMaxLength(300);
                entity.Property(e => e.Estado)
                    .HasColumnName("estado")
                    .HasDefaultValue(true);
                entity.Property(e => e.FechaCreacion).HasColumnName("fechaCreacion");
                entity.Property(e => e.FechaActualizacion).HasColumnName("fechaActualizacion");

                entity.HasOne(e => e.Empresa)
                    .WithMany(e => e.Sucursales)
                    .HasForeignKey(e => e.IdEmpresa)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<EContaxUsuarioContexto>(entity =>
            {
                entity.ToTable("ECONTAX_USUARIO_CONTEXTO", "dbo");
                entity.HasKey(e => e.IdUsuario);

                entity.Property(e => e.IdUsuario).HasColumnName("id_usuario");
                entity.Property(e => e.IdEmpresa).HasColumnName("id_empresa");
                entity.Property(e => e.IdSucursal).HasColumnName("id_sucursal");
                entity.Property(e => e.Estado)
                    .HasColumnName("estado")
                    .HasDefaultValue(true);
                entity.Property(e => e.FechaCreacion).HasColumnName("fecha_creacion");
                entity.Property(e => e.FechaActualizacion).HasColumnName("fecha_actualizacion");

                entity.HasOne(e => e.Usuario)
                    .WithMany()
                    .HasForeignKey(e => e.IdUsuario)
                    .OnDelete(DeleteBehavior.NoAction);
                entity.HasOne(e => e.Empresa)
                    .WithMany()
                    .HasForeignKey(e => e.IdEmpresa)
                    .OnDelete(DeleteBehavior.NoAction);
                entity.HasOne(e => e.Sucursal)
                    .WithMany()
                    .HasForeignKey(e => new { e.IdEmpresa, e.IdSucursal })
                    .HasPrincipalKey(e => new { e.IdEmpresa, e.IdSucursal })
                    .OnDelete(DeleteBehavior.NoAction);
            });

            base.OnModelCreating(modelBuilder);
           
            // =========================
            // PRODUCTOTIPO
            // =========================
            modelBuilder.Entity<Productotipo>(entity =>
            {
                entity.ToTable("PRODUCTOTIPO", "dbo");
                entity.HasKey(e => e.Idtipoproducto);

                entity.Property(e => e.Idtipoproducto).HasColumnName("IDTIPOPRODUCTO");
                entity.Property(e => e.Descripcion).HasColumnName("DESCRIPCION");
                entity.Property(e => e.Estado).HasColumnName("ESTADO");


                entity.Property(e => e.Valoracioninventario).HasColumnName("VALORACIONINVENTARIO");
                entity.Property(e => e.Perecible).HasColumnName("PERECIBLE");
                entity.Property(e => e.Idempresa).HasColumnName("IDEMPRESA");
                entity.Property(e => e.Stockminimo).HasColumnName("STOCKMINIMO");
                entity.Property(e => e.Stockmaximo).HasColumnName("STOCKMAXIMO");

                entity.Property(e => e.Idusuario).HasColumnName("IDUSUARIO");
            });
            // Configuración específica si es necesaria
            modelBuilder.Entity<Proveedor>().ToTable("PROVEEDORES");
            modelBuilder.Entity<Productosubtipo>(entity =>
            {
                entity.ToTable("PRODUCTOSUBTIPO", "dbo");
                entity.HasKey(e => e.Idsubtipo);

                entity.Property(e => e.Idsubtipo).HasColumnName("IDSUBTIPO");
                entity.Property(e => e.Idtipoproducto).HasColumnName("IDTIPOPRODUCTO");
                entity.Property(e => e.Descripcion).HasColumnName("DESCRIPCION");
                entity.Property(e => e.Estado).HasColumnName("ESTADO");

                entity.Property(e => e.Cuentacontable).HasColumnName("CUENTACONTABLE");
                entity.Property(e => e.Idusuario).HasColumnName("IDUSUARIO");

                // ✅ RELACIÓN SIN FK SOMBRA (esta línea mata el error)
                entity.HasOne(e => e.IdtipoproductoNavigation)
                      .WithMany(t => t.Productosubtipos)
                      .HasForeignKey(e => e.Idtipoproducto)
                      .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<AbonoMultiple>(entity => {
                entity.Property(e => e.valor).HasPrecision(18, 2);
                entity.Property(e => e.saldo).HasPrecision(18, 2);
                entity.Property(e => e.saldoUtilizado).HasPrecision(18, 2);
            });

            modelBuilder.Entity<Abonos>(entity => {
                // Ya no es necesario poner .HasColumnName("base") aquí 
                // porque ya lo pusimos en el atributo [Column] del modelo.
                entity.Property(e => e.abono).HasPrecision(18, 2);
                entity.Property(e => e.BaseImporte).HasPrecision(18, 2);
                entity.Property(e => e.porcentaje).HasPrecision(18, 2);
            });
            // =========================
            // CODIGOIMPUESTO
            // =========================
            // 1. Mapeo de USU_SOLICITUD_FIRMA
            modelBuilder.Entity<UsuSolicitudFirma>(entity =>
            {
                entity.ToTable("USU_SOLICITUD_FIRMA");
                entity.HasKey(e => e.SolId);

                // Mapeo manual de columnas para asegurar los guiones bajos
                entity.Property(e => e.SolId).HasColumnName("SOL_ID");
                entity.Property(e => e.SolIdUsuarioCliente).HasColumnName("SOL_ID_USUARIO_CLIENTE");
                entity.Property(e => e.SolIdEstadoNumerica).HasColumnName("SOL_ID_ESTADO_NUMERICA");
                entity.Property(e => e.SolIdEstadoUanataca).HasColumnName("SOL_ID_ESTADO_UANATACA");
                entity.Property(e => e.SolTipoIdentificacion).HasColumnName("SOL_TIPO_IDENTIFICACION");
                entity.Property(e => e.SolIdentificacion).HasColumnName("SOL_IDENTIFICACION");
                entity.Property(e => e.SolCodigoDactilar).HasColumnName("SOL_CODIGO_DACTILAR");
                entity.Property(e => e.SolNombres).HasColumnName("SOL_NOMBRES");
                entity.Property(e => e.SolPrimerApellido).HasColumnName("SOL_PRIMER_APELLIDO");
                entity.Property(e => e.SolSegundoApellido).HasColumnName("SOL_SEGUNDO_APELLIDO");
                entity.Property(e => e.SolFechaNacimiento).HasColumnName("SOL_FECHA_NACIMIENTO");
                entity.Property(e => e.SolNacionalidad).HasColumnName("SOL_NACIONALIDAD");
                entity.Property(e => e.SolSexo).HasColumnName("SOL_SEXO");
                entity.Property(e => e.SolTelefono1).HasColumnName("SOL_TELEFONO_1");
                entity.Property(e => e.SolCorreo1).HasColumnName("SOL_CORREO_1");
                entity.Property(e => e.SolTieneRuc).HasColumnName("SOL_TIENE_RUC");
                entity.Property(e => e.SolNroRuc).HasColumnName("SOL_NRO_RUC");
                entity.Property(e => e.SolProvincia).HasColumnName("SOL_PROVINCIA");
                entity.Property(e => e.SolCanton).HasColumnName("SOL_CANTON");
                entity.Property(e => e.SolDireccion).HasColumnName("SOL_DIRECCION");
                entity.Property(e => e.SolEsMayor65).HasColumnName("SOL_ES_MAYOR_65");
                entity.Property(e => e.SolFormatoFirma).HasColumnName("SOLFORMATOFIRMA");
                entity.Property(e => e.SolVigencia).HasColumnName("SOLTIEMPOVIGENCIA");
                entity.Property(e => e.SolFechaSolicitud).HasColumnName("SOL_FECHA_SOLICITUD");
                entity.Property(e => e.SolIdUsuarioSoporte).HasColumnName("SOL_ID_USUARIO_SOPORTE");
                entity.Property(e => e.SolActivo).HasColumnName("SOL_ACTIVO");
                entity.Property(e => e.SolClaveP12).HasColumnName("SolClaveP12");
                entity.Property(e => e.SolArchivoP12).HasColumnName("SolArchivoP12");

                entity.Property(e => e.SolUanatacaUuid).HasColumnName("SOL_UANATACA_UUID");
                entity.Property(e => e.SolUanatacaStatus).HasColumnName("SOL_UANATACA_STATUS");
                entity.Property(e => e.SolUanatacaToken).HasColumnName("SOL_UANATACA_TOKEN");
                entity.Property(e => e.SolUanatacaStatusText).HasColumnName("SOL_UANATACA_STATUS_TEXT");
                entity.Property(e => e.SolUanatacaComments).HasColumnName("SOL_UANATACA_COMMENTS");
                entity.Property(e => e.SolUanatacaProductUuid).HasColumnName("SOL_UANATACA_PRODUCT_UUID");
                entity.Property(e => e.SolUanatacaStakeholderUuid).HasColumnName("SOL_UANATACA_STAKEHOLDER_UUID");
                entity.Property(e => e.SolUanatacaCreatedBy).HasColumnName("SOL_UANATACA_CREATED_BY");
                entity.Property(e => e.SolUanatacaActive).HasColumnName("SOL_UANATACA_ACTIVE");
                entity.Property(e => e.SolUanatacaCountable).HasColumnName("SOL_UANATACA_COUNTABLE");
                entity.Property(e => e.SolUanatacaRenovation).HasColumnName("SOL_UANATACA_RENOVATION");
                entity.Property(e => e.SolUanatacaOfferUuid).HasColumnName("SOL_UANATACA_OFFER_UUID");
                entity.Property(e => e.SolUanatacaHasFrontId).HasColumnName("SOL_UANATACA_HAS_FRONT_ID");
                entity.Property(e => e.SolUanatacaHasBackId).HasColumnName("SOL_UANATACA_HAS_BACK_ID");
                entity.Property(e => e.SolUanatacaHasSelfie).HasColumnName("SOL_UANATACA_HAS_SELFIE");
                entity.Property(e => e.SolUanatacaHasRucFile).HasColumnName("SOL_UANATACA_HAS_RUC_FILE");
                entity.Property(e => e.SolUanatacaHasSeniorVideo).HasColumnName("SOL_UANATACA_HAS_SENIOR_VIDEO");
                entity.Property(e => e.SolUanatacaHasAppointment).HasColumnName("SOL_UANATACA_HAS_APPOINTMENT");
                entity.Property(e => e.SolUanatacaHasAcceptance).HasColumnName("SOL_UANATACA_HAS_ACCEPTANCE");
                entity.Property(e => e.SolUanatacaHasConstitution).HasColumnName("SOL_UANATACA_HAS_CONSTITUTION");
                entity.Property(e => e.SolUanatacaHasManagerId).HasColumnName("SOL_UANATACA_HAS_MANAGER_ID");
                entity.Property(e => e.SolUanatacaHasAuthorization).HasColumnName("SOL_UANATACA_HAS_AUTHORIZATION");
                entity.Property(e => e.SolUanatacaHasAdditional).HasColumnName("SOL_UANATACA_HAS_ADDITIONAL");

                entity.Property(e => e.SolCompanyName).HasColumnName("SOL_COMPANY_NAME");
                entity.Property(e => e.SolDepartment).HasColumnName("SOL_DEPARTMENT");
                entity.Property(e => e.SolPosition).HasColumnName("SOL_POSITION");
                entity.Property(e => e.SolReason).HasColumnName("SOL_REASON");
                entity.Property(e => e.SolIdentificationTypeManager).HasColumnName("SOL_IDENTIFICATION_TYPE_MANAGER");
                entity.Property(e => e.SolIdentificationManager).HasColumnName("SOL_IDENTIFICATION_MANAGER");
                entity.Property(e => e.SolNamesManager).HasColumnName("SOL_NAMES_MANAGER");
                entity.Property(e => e.SolLastNameManager).HasColumnName("SOL_LAST_NAME_MANAGER");

                entity.HasOne(d => d.EstadoNumerica)
        .WithMany(p => p.SolicitudesNumerica) // Conecta con la colección en UsuEstadoFirma
        .HasForeignKey(d => d.SolIdEstadoNumerica)
        .OnDelete(DeleteBehavior.Restrict);

                // Configuración de la relación para Estado Uanataca
                entity.HasOne(d => d.EstadoUanataca)
                    .WithMany(p => p.SolicitudesUanataca) // Conecta con la otra colección en UsuEstadoFirma
                    .HasForeignKey(d => d.SolIdEstadoUanataca)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // 2. Mapeo de USU_SOLICITUD_DOCUMENTO
            modelBuilder.Entity<UsuSolicitudEstadoHistorial>(entity =>
            {
                entity.ToTable("USU_SOLICITUD_ESTADO_HISTORIAL");

                // Relación con Estado Anterior
                entity.HasOne(d => d.EstadoAnterior)
                    .WithMany(p => p.HistorialAnterior) // <-- Esto ahora funcionará con el modelo de arriba
                    .HasForeignKey(d => d.HisIdEstadoAnterior)
                    .OnDelete(DeleteBehavior.Restrict);

                // Relación con Estado Nuevo
                entity.HasOne(d => d.EstadoNuevo)
                    .WithMany(p => p.HistorialNuevo) // <-- Esto ahora funcionará con el modelo de arriba
                    .HasForeignKey(d => d.HisIdEstadoNuevo)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // 5. Mapeo de USU_SOLICITUD_OBSERVACION
            modelBuilder.Entity<UsuSolicitudObservacion>(entity =>
            {
                entity.ToTable("USU_SOLICITUD_OBSERVACION");
                entity.HasKey(e => e.ObsId);

                entity.Property(e => e.ObsId).HasColumnName("OBS_ID");
                entity.Property(e => e.ObsIdSolicitud).HasColumnName("OBS_ID_SOLICITUD");
                entity.Property(e => e.ObsCampoObservedo).HasColumnName("OBS_CAMPO_OBSERVADO");
                entity.Property(e => e.ObsTipo).HasColumnName("OBS_TIPO");
                entity.Property(e => e.ObsDetalle).HasColumnName("OBS_DETALLE");
                entity.Property(e => e.ObsEstado).HasColumnName("OBS_ESTADO");
                entity.Property(e => e.ObsTokenCorreccion).HasColumnName("OBS_TOKEN_CORRECCION");
                entity.Property(e => e.ObsFechaObservacion).HasColumnName("OBS_FECHA_OBSERVACION");
                entity.Property(e => e.ObsActivo).HasColumnName("OBS_ACTIVO");

                entity.HasOne<UsuSolicitudFirma>()
                    .WithMany(s => s.Observaciones)
                    .HasForeignKey(o => o.ObsIdSolicitud)
                    .OnDelete(DeleteBehavior.Restrict);
            });
            modelBuilder.Entity<Codigosimpuesto>(entity =>
            {
                entity.ToTable("CODIGOSIMPUESTOS");
                entity.HasKey(e => e.Codigo);

                entity.Property(e => e.Codigo).HasColumnName("CODIGO");
                entity.Property(e => e.Descripcion).HasColumnName("DESCRIPCION");

                // Si tu tabla tiene más campos y existen en tu modelo, agrégalos aquí.
            });
            modelBuilder.Entity<ClienteCorreo>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne<Cliente>()
                    .WithMany(c => c.ClientesCorreos)
                    .HasForeignKey(e => e.CodCliente)
                    .OnDelete(DeleteBehavior.Cascade); // Si se borra el cliente, se borran sus correos
            });
            // =========================
            // DETALLEFACTURA (REPARADO)
            // =========================
            modelBuilder.Entity<Detallefactura>(entity =>
            {
                // Nombre exacto de la tabla
                entity.ToTable("DETALLEFACTURA");

                // Llave primaria (PK) según tu imagen de SQL
                entity.HasKey(e => e.Codlinea);

                // --- REPARACIÓN DE COLUMNA FANTASMA ---
                // Esto evita el error 'Invalid column name ProductoCodigo'
                entity.Ignore("ProductoCodigo");
                entity.Ignore(e => e.ProductoCodigo);

                // Mapeo de columnas - Nombres exactos de tu Base de Datos
                entity.Property(e => e.Codlinea).HasColumnName("CODLINEA");
                entity.Property(e => e.Codfactura).HasColumnName("CODFACTURA");
                entity.Property(e => e.Codproducto).HasColumnName("CODPRODUCTO");
                entity.Property(e => e.Codprincipal).HasColumnName("CODPRINCIPAL");
                entity.Property(e => e.Codauxiliar).HasColumnName("CODAUXILIAR");
                entity.Property(e => e.Cantproducto).HasColumnName("CANTPRODUCTO").HasColumnType("decimal(18, 2)");
                entity.Property(e => e.Descripproducto).HasColumnName("DESCRIPPRODUCTO");
                entity.Property(e => e.Precioproducto).HasColumnName("PRECIOPRODUCTO").HasColumnType("decimal(18, 2)");
                entity.Property(e => e.Descuento).HasColumnName("DESCUENTO").HasColumnType("decimal(18, 2)");
                entity.Property(e => e.Valortproducto).HasColumnName("VALORTPRODUCTO").HasColumnType("decimal(18, 2)");
                entity.Property(e => e.Valoriva).HasColumnName("VALORIVA").HasColumnType("decimal(18, 2)");
                entity.Property(e => e.Valortotal).HasColumnName("VALORTOTAL").HasColumnType("decimal(18, 2)");
                entity.Property(e => e.Tarifa).HasColumnName("TARIFA");
                entity.Property(e => e.Valorice).HasColumnName("VALORICE").HasColumnType("decimal(18, 2)");
                entity.Property(e => e.Costo).HasColumnName("COSTO").HasColumnType("decimal(18, 2)");
                entity.Property(e => e.Bonificacion).HasColumnName("BONIFICACION");


                // --- CONFIGURACIÓN DE RELACIONES (NAVEGACIÓN) ---

                // Relación con FACTURA
                entity.HasOne(d => d.Factura)
                      .WithMany(p => p.Detallefacturas)
                      .HasForeignKey(d => d.Codfactura)
                      .OnDelete(DeleteBehavior.Cascade) // Si se borra la factura, se borran los detalles
                      .HasConstraintName("FK_DETALLEFACTURA_FACTURA");

                // Relación con PRODUCTO (Aquí es donde se arregla el nombre de la FK)
                entity.HasOne<Producto>()
                      .WithMany(p => p.Detallefacturas)
                      .HasForeignKey(d => d.Codproducto) // Forzamos a usar CODPRODUCTO y no 'ProductoCodigo'
                      .OnDelete(DeleteBehavior.NoAction)
                      .HasConstraintName("FK_DETALLEFACTURA_PRODUCTO");
            });

            // =========================
            // PORCENTAJEIVA
            // =========================
            modelBuilder.Entity<Porcentajeiva>(entity =>
            {
                entity.ToTable("PORCENTAJEIVA");

                // ⚠️ IMPORTANTE:
                // En tu Razor tú usas iva.Codigo / iva.Descripcion / iva.Valor
                // Así que asumo que la PK es "Codigo" (string). Si en tu modelo la PK es "Idporcentaje",
                // cambia HasKey y las columnas según corresponda.
                entity.HasKey(e => e.Codigo);

                entity.Property(e => e.Codigo).HasColumnName("CODIGO");
                entity.Property(e => e.Descripcion).HasColumnName("DESCRIPCION");
                entity.Property(e => e.Valor).HasColumnName("VALOR");
            });

            // =========================
            // PRODUCTO
            // =========================
            modelBuilder.Entity<Producto>(entity =>
            {
                entity.ToTable("PRODUCTO");
                entity.HasKey(e => e.Codigo);

                entity.Property(e => e.Codigo).HasColumnName("CODIGO");
                entity.Property(e => e.CodigoPrincipal).HasColumnName("CODIGO_PRINCIPAL");
                entity.Property(e => e.CodAuxiliar).HasColumnName("COD_AUXILIAR");
                entity.Property(e => e.Nombre).HasColumnName("NOMBRE");

                entity.Property(e => e.ValorUnitario).HasColumnName("VALOR_UNITARIO");
                entity.Property(e => e.Precio2).HasColumnName("PRECIO2");
                entity.Property(e => e.Precio3).HasColumnName("PRECIO3");

                entity.Property(e => e.TipoProducto).HasColumnName("TIPO_PRODUCTO");

                entity.Property(e => e.Codigoimpuesto).HasColumnName("CODIGOIMPUESTO");
                entity.Property(e => e.Porcentajeimpuesto).HasColumnName("PORCENTAJEIMPUESTO");

                entity.Property(e => e.Idusuario).HasColumnName("IDUSUARIO");
                entity.Property(e => e.Idempresa).HasColumnName("IDEMPRESA");
                entity.Property(e => e.Idsucursal).HasColumnName("IDSUCURSAL");

                entity.Property(e => e.Estado).HasColumnName("ESTADO");
                entity.Property(e => e.Margen).HasColumnName("MARGEN");
                entity.Property(e => e.Codigocontable).HasColumnName("CODIGOCONTABLE");
                entity.Property(e => e.Facturable).HasColumnName("FACTURABLE");

                entity.Property(e => e.Idproveedor).HasColumnName("IDPROVEEDOR");
                entity.Property(e => e.Idmarca).HasColumnName("IDMARCA");
                entity.Property(e => e.Perecible).HasColumnName("PERECIBLE");
                entity.Property(e => e.Observacion).HasColumnName("OBSERVACION");
                entity.Property(e => e.Tipocompravena).HasColumnName("TIPOCOMPRAVENA");
                entity.Property(e => e.Inventario).HasColumnName("INVENTARIO");

                entity.Property(e => e.Idsubtipo).HasColumnName("IDSUBTIPO");

                entity.HasOne(d => d.TipoProductoNavigation)
                      .WithMany(p => p.Productos)
                      .HasForeignKey(d => d.TipoProducto)
                      .OnDelete(DeleteBehavior.NoAction)
                      .HasConstraintName("FK_PRODUCTO_PRODUCTOTIPO");

                entity.HasOne(d => d.CodigoimpuestoNavigation)
                      .WithMany(p => p.Productos)
                      .HasForeignKey(d => d.Codigoimpuesto)
                      .HasConstraintName("FK_PRODUCTO_IMPUESTO");

                entity.HasOne(d => d.PorcentajeimpuestoNavigation)
                      .WithMany(p => p.Productos)
                      .HasForeignKey(d => d.Porcentajeimpuesto)
                      .HasConstraintName("FK_PRODUCTO_PORCENTAJEIVA");

                entity.HasOne(d => d.IdsubtipoNavigation)
                      .WithMany(p => p.Productos)
                      .HasForeignKey(d => d.Idsubtipo)
                      .OnDelete(DeleteBehavior.NoAction)
                      .HasConstraintName("FK_PRODUCTO_PRODUCTOSUBTIPO");
            });
            // =========================
            // CLIENTES
            // =========================
            modelBuilder.Entity<Cliente>(entity =>
            {
                entity.ToTable("CLIENTES");
                entity.HasKey(e => e.Codcliente);

                entity.Property(e => e.Codcliente).HasColumnName("CODCLIENTE");

                entity.Property(e => e.Apellidos).HasColumnName("APELLIDOS");
                entity.Property(e => e.Nombres).HasColumnName("NOMBRES");
                entity.Property(e => e.Nombrecomercial).HasColumnName("NOMBRECOMERCIAL");
                entity.Property(e => e.Nombrerazonsocial).HasColumnName("NOMBRERAZONSOCIAL");
                entity.Property(e => e.Tipoidentificacion).HasColumnName("TIPOIDENTIFICACION");
                entity.Property(e => e.Numeroidentificacion).HasColumnName("NUMEROIDENTIFICACION");
                entity.Property(e => e.Direccion).HasColumnName("DIRECCION");
                entity.Property(e => e.Telefonoconvencional).HasColumnName("TELEFONOCONVENCIONAL");
                entity.Property(e => e.Celular).HasColumnName("CELULAR");
                entity.Property(e => e.Correo).HasColumnName("CORREO");
                entity.Property(e => e.DiasCredito).HasColumnName("DIAS_CREDITO");

                entity.Property(e => e.TipoCliente).HasColumnName("TIPO_CLIENTE");
                entity.Property(e => e.Estado).HasColumnName("ESTADO");
                entity.Property(e => e.Idempresa).HasColumnName("IDEMPRESA");
                entity.Property(e => e.Idsucursal).HasColumnName("IDSUCURSAL");

                entity.Property(e => e.Observaciones).HasColumnName("OBSERVACIONES");

                // Ubicación
                entity.Property(e => e.Pais).HasColumnName("PAIS");
                entity.Property(e => e.Provincia).HasColumnName("PROVINCIA");
                entity.Property(e => e.Ciudad).HasColumnName("CIUDAD");

                entity.HasOne(d => d.PaisNavegacion)
                      .WithMany(p => p.Clientes)
                      .HasForeignKey(d => d.Pais)
                      .HasConstraintName("FK_CLIENTES_PAIS")
                      .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(d => d.ProvinciaNavegacion)
                      .WithMany(p => p.Clientes)
                      .HasForeignKey(d => d.Provincia)
                      .HasConstraintName("FK_CLIENTES_PROVINCIA")
                      .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(d => d.CiudadNavegacion)
                      .WithMany(c => c.Clientes)
                      .HasForeignKey(d => d.Ciudad)
                      .HasConstraintName("FK_CLIENTES_CIUDAD")
                      .OnDelete(DeleteBehavior.NoAction);

                // Identificación (IDE_SEC)
                entity.Property(e => e.IdeSec).HasColumnName("IDE_SEC");

                entity.HasOne(e => e.IdentificacionNavigation)
                      .WithMany(i => i.Clientes)
                      .HasForeignKey(e => e.IdeSec)
                      .HasConstraintName("FK_CLIENTES_IDENTIFICACION");

                // TipoCliente
                entity.HasOne(d => d.TipoClienteNavigation)
                      .WithMany(p => p.Clientes)
                      .HasForeignKey(d => d.TipoCliente)
                      .HasConstraintName("FK_CLIENTES_TIPOCLIENTE");

                // Usuario
                entity.Property(e => e.Usuario).HasColumnName("USUARIO");

                entity.HasOne(e => e.UsuarioNavegacion)
                      .WithMany()
                      .HasForeignKey(e => e.Usuario);
            });

            modelBuilder.Entity<ComprasFactura>(entity =>
            {
                entity.ToTable("COMPRASFACTURA");
                entity.HasKey(e => e.CodFactura);

                entity.Property(e => e.CodFactura).HasColumnName("codFactura");
                entity.Property(e => e.CodClave).HasColumnName("codClave");
                entity.Property(e => e.CodClientes).HasColumnName("codClientes");
                entity.Property(e => e.CodEmisor).HasColumnName("codEmisor");
                entity.Property(e => e.NumFactura).HasColumnName("numFactura");
                entity.Property(e => e.NumAutorizacion).HasColumnName("numAutorización");
                entity.Property(e => e.CodDocumento).HasColumnName("codDocumento");
                entity.Property(e => e.Subtotal12).HasColumnName("subtotal12").HasColumnType("decimal(18,2)");
                entity.Property(e => e.Subtotal0).HasColumnName("subtotal0").HasColumnType("decimal(18,2)");
                entity.Property(e => e.Subtotal).HasColumnName("subtotal").HasColumnType("decimal(18,2)");
                entity.Property(e => e.Descuentos).HasColumnName("descuentos").HasColumnType("decimal(18,2)");
                entity.Property(e => e.Iva).HasColumnName("iva").HasColumnType("decimal(18,2)");
                entity.Property(e => e.ValorTotal).HasColumnName("valorTotal").HasColumnType("decimal(18,2)");
                entity.Property(e => e.NoImp).HasColumnName("noImp").HasColumnType("decimal(18,2)");
                entity.Property(e => e.ExIva).HasColumnName("exIva").HasColumnType("decimal(18,2)");
                entity.Property(e => e.ValorICE).HasColumnName("valorICE").HasColumnType("decimal(18,2)");
                entity.Property(e => e.BI_IRBPNR).HasColumnName("BI_IRBPNR").HasColumnType("decimal(18,2)");
                entity.Property(e => e.ValorBI_IRBPNR).HasColumnName("valorBI_IRBPNR").HasColumnType("decimal(18,2)");
                entity.Property(e => e.Serie).HasColumnName("serie");
                entity.Property(e => e.FechaAutoSRI).HasColumnName("fechaAutoSRI");
                entity.Property(e => e.Estado).HasColumnName("estado");
                entity.Property(e => e.Autorizado).HasColumnName("autorizado");
                entity.Property(e => e.FechaRegistro).HasColumnName("fechaRegistro");

                entity.HasMany(e => e.Detalles)
                      .WithOne(d => d.CompraFactura)
                      .HasForeignKey(d => d.CodFactura)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ComprasDetalleFac>(entity =>
            {
                entity.ToTable("COMPRASDETALLEFAC");
                entity.HasKey(e => e.CodLinea);

                entity.Property(e => e.CodLinea).HasColumnName("codLinea");
                entity.Property(e => e.CodFactura).HasColumnName("codFactura");
                entity.Property(e => e.CodProducto).HasColumnName("codProducto");
                entity.Property(e => e.CodPrincipal).HasColumnName("codPrincipal");
                entity.Property(e => e.CodAuxiliar).HasColumnName("codAuxiliar");
                entity.Property(e => e.CantProducto).HasColumnName("cantProducto").HasColumnType("de cimal(18,2)");
                entity.Property(e => e.DescripProducto).HasColumnName("descripProducto");
                entity.Property(e => e.PrecioProducto).HasColumnName("precioProducto").HasColumnType("decimal(18,2)");
                entity.Property(e => e.Descuento).HasColumnName("descuento").HasColumnType("decimal(18,2)");
                entity.Property(e => e.ValorTProducto).HasColumnName("valorTProducto").HasColumnType("decimal(18,2)");
                entity.Property(e => e.ValorICE).HasColumnName("valorICE").HasColumnType("decimal(18,2)");
                entity.Property(e => e.ValorIVA).HasColumnName("valorIVA").HasColumnType("decimal(18,2)");
                entity.Property(e => e.BI_IRBPNR).HasColumnName("BI_IRBPNR").HasColumnType("decimal(18,2)");
                entity.Property(e => e.ValorBI_IRBPNR).HasColumnName("valorBI_IRBPNR").HasColumnType("decimal(18,2)");
                entity.Property(e => e.ValorTotal).HasColumnName("valorTotal").HasColumnType("decimal(18,2)");
                entity.Property(e => e.CodImp).HasColumnName("codImp");
                entity.Property(e => e.PorImp).HasColumnName("porImp");
                entity.Property(e => e.Tarifa).HasColumnName("tarifa");
            });


            modelBuilder.Entity<Tipocliente>(entity =>
            {
                entity.ToTable("TIPOCLIENTE");
                entity.HasKey(e => e.TclCodigo);

                entity.Property(e => e.TclSec).HasColumnName("TCL_SEC");
                entity.Property(e => e.TclCodigo).HasColumnName("TCL_CODIGO");
                entity.Property(e => e.TclDescripcion).HasColumnName("TCL_DESCRIPCION");
            });

            modelBuilder.Entity<Emisor>(entity =>
            {
                entity.ToTable("EMISOR", "dbo");
                entity.HasKey(e => e.Codigo).HasName("PK_EMISOR");

                entity.Property(e => e.Codigo).HasColumnName("codigo");

                entity.Property(e => e.RazonSocial).HasColumnName("razonSocial").HasMaxLength(70);
                entity.Property(e => e.Ruc).HasColumnName("RUC").HasMaxLength(15);
                entity.Property(e => e.NomComercial).HasColumnName("nomComercial").HasMaxLength(100);
                entity.Property(e => e.DirEstablecimiento).HasColumnName("dirEstablecimiento").HasMaxLength(250);
                entity.Property(e => e.CodEstablecimiento).HasColumnName("codEstablecimiento").HasMaxLength(5);
                entity.Property(e => e.Resolusion).HasColumnName("resolusion").HasMaxLength(25);
                entity.Property(e => e.ContribuyenteEspecial).HasColumnName("contribuyenteEspecial").HasMaxLength(5);
                entity.Property(e => e.CodPuntoEmision).HasColumnName("codPuntoEmision").HasMaxLength(3);

                // Cambiamos a Varchar o eliminamos el FixedLength si existiera para evitar espacios
                entity.Property(e => e.LlevaContabilidad).HasColumnName("llevaContabilidad").HasMaxLength(2).IsUnicode(false);

                entity.Property(e => e.LogoImagen).HasColumnName("logoImagen").HasColumnType("nvarchar(max)");
                entity.Property(e => e.TipoEmision).HasColumnName("tipoEmision").HasMaxLength(3);
                entity.Property(e => e.TiempoEspera).HasColumnName("tiempoEspera").HasMaxLength(3);
                entity.Property(e => e.Email).HasColumnName("EMAIL").HasMaxLength(50);
                entity.Property(e => e.Direccion).HasColumnName("DIRECCION").HasMaxLength(50);

                entity.Property(e => e.ClaveInterna).HasColumnName("CLAVE_INTERNA").HasMaxLength(25);
                entity.Property(e => e.TipoAmbiente).HasColumnName("TIPO_AMBIENTE").HasMaxLength(1);
                entity.Property(e => e.DireccionMatriz).HasColumnName("DIRECCION_MATRIZ").HasMaxLength(255);
                entity.Property(e => e.Token).HasColumnName("TOKEN").HasMaxLength(40);

                // ✅ CORRECCIÓN: Usar varchar o simplemente omitir ColumnType si la BD ya es char, 
                // pero el Trim en el Controller seguirá siendo necesario si la BD es CHAR física.
                entity.Property(e => e.Retenciones).HasColumnName("retenciones").HasColumnType("char(3)").IsUnicode(false);
                entity.Property(e => e.RetIva).HasColumnName("retIva").HasMaxLength(1).IsUnicode(false);
                entity.Property(e => e.RetFuente).HasColumnName("retFuente").HasMaxLength(1).IsUnicode(false);

                entity.Property(e => e.IdEmpresa).HasColumnName("idEmpresa");
                entity.Property(e => e.IdSucursal).HasColumnName("idSucursal");

                entity.Property(e => e.PathCertificado).HasColumnName("pathCertificado").HasMaxLength(150).IsUnicode(false);
                entity.Property(e => e.ClaveCertificado).HasColumnName("claveCertificado").HasMaxLength(50).IsUnicode(false);
                entity.Property(e => e.Telefono).HasColumnName("telefono").HasMaxLength(50).IsUnicode(false);

                // ✅ MUY IMPORTANTE: Falta el mapeo de la columna ESTADO
                // Si tu columna se llama 'estado' en la BD, agrégala aquí:
                entity.Property(e => e.Estado).HasColumnName("estado");
            });

            modelBuilder.Entity<Pais>(entity =>
            {
                entity.ToTable("PAIS");
                entity.HasKey(e => e.IdPais).HasName("PK_PAIS");

                entity.Property(e => e.IdPais).HasColumnName("idPais");
                entity.Property(e => e.Descripcion).HasColumnName("Descripcion").HasMaxLength(50);
            });

            modelBuilder.Entity<Provincia>(entity =>
            {
                entity.ToTable("PROVINCIA");
                entity.HasKey(e => e.IdProvincia).HasName("PK_PROVINCIA");

                entity.Property(e => e.IdProvincia)
                    .ValueGeneratedNever()
                    .HasColumnName("idProvincia");
                entity.Property(e => e.IdPais).HasColumnName("idPais");
                entity.Property(e => e.Descripcion).HasColumnName("descripcion").HasMaxLength(50);

                entity.HasOne(d => d.Pais)
                      .WithMany(p => p.Provincias)
                      .HasForeignKey(d => d.IdPais)
                      .HasConstraintName("FK_PROVINCIA_PAIS")
                      .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<Ciudad>(entity =>
            {
                entity.ToTable("CIUDAD");
                entity.HasKey(e => e.IdCiudad).HasName("PK_CIUDAD");

                entity.Property(e => e.IdCiudad)
                    .ValueGeneratedNever()
                    .HasColumnName("idCiudad");
                entity.Property(e => e.IdProvincia).HasColumnName("idProvincia");
                entity.Property(e => e.Descripcion).HasColumnName("descripcion").HasMaxLength(50);

                entity.HasOne(d => d.Provincia)
                      .WithMany(p => p.Ciudades)
                      .HasForeignKey(d => d.IdProvincia)
                      .HasConstraintName("FK_CIUDAD_PROVINCIA")
                      .OnDelete(DeleteBehavior.NoAction);
            });

            // ✅ RELACIONES CLIENTES -> PAIS/PROVINCIA/CIUDAD
            modelBuilder.Entity<Cliente>(entity =>
            {
                // Asegurar nombres de columnas (como en tu BD)
                entity.Property(e => e.Tipoidentificacion)
      .HasColumnName("TIPOIDENTIFICACION");

                entity.Property(e => e.Pais).HasColumnName("PAIS");
                entity.Property(e => e.Provincia).HasColumnName("PROVINCIA");
                entity.Property(e => e.Ciudad).HasColumnName("CIUDAD");

                entity.HasOne(d => d.PaisNavegacion)
       .WithMany(p => p.Clientes)
       .HasForeignKey(d => d.Pais)
       .HasConstraintName("FK_CLIENTES_PAIS")
       .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(d => d.ProvinciaNavegacion)
                      .WithMany(p => p.Clientes)
                      .HasForeignKey(d => d.Provincia)
                      .HasConstraintName("FK_CLIENTES_PROVINCIA")
                      .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(d => d.CiudadNavegacion)
                      .WithMany(c => c.Clientes)
                      .HasForeignKey(d => d.Ciudad)
                      .HasConstraintName("FK_CLIENTES_CIUDAD")
                      .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<LogIniciosSesion>(entity =>
            {
                entity.ToTable("Log_IniciosSesion");
                entity.HasKey(e => e.IdLog);

                entity.Property(e => e.IdLog).HasColumnName("IdLog");
                entity.Property(e => e.IdUsuario).HasColumnName("IdUsuario");
                entity.Property(e => e.FechaAcceso).HasColumnName("FechaAcceso");
                entity.Property(e => e.DireccionIp).HasColumnName("DireccionIP");
                entity.Property(e => e.Navegador).HasColumnName("Navegador");
                entity.Property(e => e.Exitoso).HasColumnName("Exitoso");
                entity.Property(e => e.DetalleError).HasColumnName("DetalleError");

                entity.HasOne(e => e.IdUsuarioNavigation)
                      .WithMany(u => u.LogIniciosSesions)
                      .HasForeignKey(e => e.IdUsuario);
            });

        }
    }
}
