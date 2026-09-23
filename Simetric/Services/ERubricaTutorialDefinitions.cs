using Simetric.Tutorials;

namespace Simetric.Services;

internal static class ERubricaTutorialDefinitions
{
    public static IReadOnlyList<TutorialDefinition> Create() =>
    [
        Define("erubrica-inicio", "Inicio de E-Rúbrica", "Conoce el estado de tu firma y los accesos principales.", "/e-rubrica", "erubrica.dashboard.page",
            Step("Estado de tu firma", "Consulta la vigencia y el titular de la firma electrónica activa.", "erubrica.dashboard.status"),
            Step("Acciones principales", "Desde aquí puedes firmar un PDF, solicitar una firma, consultar documentos o validar una firma.", "erubrica.dashboard.actions"),
            Step("Indicadores", "Revisa el resumen de solicitudes, firmas y documentos del periodo.", "erubrica.dashboard.analytics"),
            Step("Accesos de documentos", "Estos indicadores abren directamente los documentos pendientes, firmados y las firmas del mes.", "erubrica.dashboard.document-stats"),
            Step("Documentos recientes", "Consulta la actividad más reciente y abre el historial completo cuando lo necesites.", "erubrica.dashboard.recent")),
        Define("erubrica-documentos", "Documentos firmados", "Consulta, filtra y abre los PDF firmados de tu cuenta.", "/e-rubrica/documentos", "erubrica.documents.page",
            Step("Historial", "Aquí se muestran los documentos firmados disponibles en tu cuenta.", "erubrica.documents.header"),
            Step("Resumen", "Revisa el total firmado, la actividad mensual y el porcentaje de documentos válidos.", "erubrica.documents.stats"),
            Step("Filtros", "Busca por nombre, fecha o estado para localizar un documento rápidamente.", "erubrica.documents.filters"),
            Step("Listado", "Abre o descarga el PDF desde las acciones de cada registro.", "erubrica.documents.table"),
            Step("Validar un documento", "Usa este acceso para revisar la firma digital de un PDF.", "erubrica.documents.validate")),
        Define("erubrica-firmar", "Firmar documentos", "Selecciona un documento pendiente y continúa al estampado de la firma.", "/e-rubrica/documentos/firmar", "erubrica.sign.page",
            Step("Documentos pendientes", "Consulta los documentos disponibles para firmar y aplica filtros si lo necesitas.", "erubrica.sign.list"),
            Step("Documento seleccionado", "Revisa el documento elegido antes de continuar con la ubicación de la firma.", "erubrica.sign.selection"),
            Step("Cargar documento", "También puedes cargar un PDF desde esta acción.", "erubrica.sign.upload")),
        Define("erubrica-estampar", "Estampar un PDF", "Carga el PDF, selecciona la ubicación y genera el documento firmado.", "/e-rubrica/documentos/estampar", "erubrica.stamp.page",
            Step("Selecciona el PDF", "Carga un documento o usa uno de los documentos guardados.", "erubrica.stamp.upload"),
            Step("Ubica la firma", "Haz clic en el PDF para definir dónde aparecerá la firma; puedes usar dos ubicaciones.", "erubrica.stamp.workspace"),
            Step("Firma el documento", "Previsualiza el resultado o estampa el PDF cuando la ubicación esté lista.", "erubrica.stamp.actions")),
        Define("erubrica-validar", "Validar una firma", "Analiza la integridad y validez de la firma digital de un PDF.", "/e-rubrica/documentos/validar-firma", "erubrica.validation.page",
            Step("Carga el PDF", "Selecciona un PDF firmado o recupera uno desde tu historial.", "erubrica.validation.upload"),
            Step("Analiza la firma", "Inicia la verificación de certificado, revocación y sello de tiempo.", "erubrica.validation.action"),
            Step("Criterios revisados", "Consulta los controles que se aplicarán durante la validación.", "erubrica.validation.criteria")),
        Define("erubrica-configurar-firma", "Configurar firma y clave", "Administra el certificado .p12 y la clave de la cuenta.", "/e-rubrica/configuracion/firma", "erubrica.config.page",
            Step("Certificado y clave", "Carga el archivo .p12 e ingresa la clave que se usará al firmar documentos.", "erubrica.config.form"),
            Step("Validación", "Comprueba el estado del certificado antes de guardar la configuración.", "erubrica.config.validation")),
        Define("erubrica-mis-firmas", "Mi firma activa", "Consulta el certificado configurado y valida otro archivo temporalmente.", "/e-rubrica/mis-firmas", "erubrica.signatures.page",
            Step("Firma activa", "Revisa titular, vigencia y demás datos del certificado activo.", "erubrica.signatures.active"),
            Step("Validación temporal", "Puedes comprobar un archivo .p12 y su clave sin reemplazar la firma activa.", "erubrica.signatures.validation")),
        Define("erubrica-plan", "Plan de E-Rúbrica", "Consulta la vigencia de tu plan y solicita una nueva firma cuando sea necesario.", "/e-rubrica/configuracion/plan", "erubrica.plan.page",
            Step("Estado del plan", "Aquí se muestra la vigencia, los días disponibles y el estado de acceso.", "erubrica.plan.details"),
            Step("Acciones", "Solicita una nueva firma o consulta tus trámites desde estos accesos.", "erubrica.plan.actions")),
        Define("erubrica-soporte", "Soporte de E-Rúbrica", "Encuentra respuestas, explora temas frecuentes y contacta al equipo de soporte.", "/e-rubrica/soporte", "erubrica.support.page",
            Step("Busca ayuda", "Escribe tu consulta para encontrar respuestas relacionadas con la firma electrónica.", "erubrica.support.search"),
            Step("Temas frecuentes", "Filtra las preguntas por categoría y abre la respuesta que necesitas.", "erubrica.support.faq"),
            Step("Canales de atención", "Contacta a soporte por WhatsApp o correo electrónico.", "erubrica.support.channels")),
        Define("erubrica-firma", "Configurar firma electrónica", "Carga y administra el certificado digital de la cuenta.", "/e-rubrica/firma", "firma.page",
            Step("Archivo de firma", "Selecciona el certificado .p12 y completa los pasos solicitados para habilitar la firma.", "firma.table")),
        Define("erubrica-emisor", "Datos del emisor", "Configura los datos legales que identifican a la cuenta que firma documentos.", "/e-rubrica/emisor", "emisor.page",
            Step("Acciones rápidas", "Crea el emisor, abre la configuración del certificado o busca un registro existente.", "erubrica.emisor.actions"),
            Step("Emisores registrados", "Consulta el listado de emisores y usa sus acciones para revisar o actualizar los datos.", "emisor.table"),
            Step("Formulario del emisor", "Completa la información legal, tributaria y de contacto antes de guardar.", "emisor.editor")),
        Define("erubrica-roles", "Roles y permisos", "Administra los perfiles y las funciones que puede utilizar cada usuario de E-Rúbrica.", "/e-rubrica/administracion/roles", "security.page",
            Step("Crear perfiles", "Crea un rol o un módulo de menú para organizar los accesos disponibles.", "security.hero"),
            Step("Seleccionar un rol", "Elige el perfil que deseas consultar o modificar.", "security.roles-panel"),
            Step("Asignar permisos", "Activa los módulos y opciones que estarán disponibles para el rol seleccionado.", "security.permissions-panel"),
            Step("Guardar accesos", "Guarda los cambios después de revisar la configuración del perfil.", "security.save-access")),
        Define("erubrica-usuarios", "Usuarios de E-Rúbrica", "Crea, consulta y administra las cuentas con acceso a E-Rúbrica.", "/e-rubrica/administracion/usuarios", "usuarios.page",
            Step("Resumen de usuarios", "Consulta cuántos usuarios están registrados, activos o bloqueados.", "usuarios.hero"),
            Step("Acciones administrativas", "Crea un nuevo usuario o administra la galería de avatares.", "usuarios.actions"),
            Step("Directorio", "Busca y revisa el rol, estado y acciones disponibles para cada usuario.", "usuarios.table")),
        Define("erubrica-perfil", "Mi perfil de E-Rúbrica", "Actualiza la información de tu cuenta y la seguridad de acceso.", "/e-rubrica/configuracion/perfil", "perfil.page",
            Step("Foto de perfil", "Selecciona el avatar que identificará tu cuenta.", "perfil.avatar"),
            Step("Información personal", "Revisa y actualiza los datos de contacto y de identificación.", "perfil.form"),
            Step("Seguridad", "Gestiona la contraseña y la información de seguridad de la cuenta.", "perfil.security"),
            Step("Guardar cambios", "Confirma las actualizaciones realizadas en el perfil.", "perfil.save")),
        Define("erubrica-solicitud", "Solicitar firma electrónica", "Completa la solicitud y adjunta los documentos necesarios para adquirir o renovar una firma.", "/solicitud/nueva", "solicitud.page",
            Step("Configuración inicial", "Define el tipo de firma, su vigencia y el tipo de solicitante.", "solicitud.step1"),
            Step("Datos del solicitante", "Completa la identificación, datos de contacto y dirección requeridos.", "solicitud.form"),
            Step("Documentos requeridos", "Adjunta los archivos solicitados antes de enviar el trámite.", "solicitud.uploads"),
            Step("Enviar solicitud", "Finaliza el proceso cuando toda la información esté validada.", "solicitud.submit")),
        Define("erubrica-mis-tramites", "Mis trámites de firma", "Consulta el estado y pago de tus solicitudes de firma electrónica.", "/solicitud/pagos", "pagos.page",
            Step("Resumen de trámites", "Revisa el total de solicitudes y los pagos pendientes o aprobados.", "pagos.header"),
            Step("Listado de solicitudes", "Consulta el estado operativo, los avisos y los valores de cada trámite.", "pagos.table"),
            Step("Nueva solicitud", "Inicia otro trámite de firma desde esta acción.", "pagos.action"))
    ];

    private static TutorialDefinition Define(string id, string title, string description, string route, string target, params TutorialStep[] steps) =>
        new()
        {
            Id = id,
            Title = title,
            Description = description,
            Route = route,
            DefaultTargetSelector = Selector(target),
            Category = "E-Rúbrica",
            Steps = steps
        };

    private static TutorialStep Step(string title, string description, string target) =>
        new()
        {
            Id = target,
            Title = title,
            Description = description,
            TargetSelector = Selector(target),
            Padding = 12,
            CardPlacement = "bottom"
        };

    private static string Selector(string target) => $"[data-tour='{target}']";
}
