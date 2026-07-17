using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ConsultaDeRucApi.Services
{
    public class DatosPersonaSri
    {
        public string Error { get; set; } = string.Empty;
        public string contribuyenteFantasma { get; set; } = string.Empty;
        public object numeroRucFantasma { get; set; } = string.Empty;
        public string numeroRuc { get; set; } = string.Empty;
        public string razonSocial { get; set; } = string.Empty;
        public object nombreComercial { get; set; } = string.Empty;
        public string estadoPersonaNatural { get; set; } = string.Empty;
        public object estadoSociedad { get; set; } = string.Empty;
        public string claseContribuyente { get; set; } = string.Empty;
        public string obligado { get; set; } = string.Empty;
        public string actividadContribuyente { get; set; } = string.Empty;
        public Informacionfechascontribuyente informacionFechasContribuyente { get; set; } = new();
        public Persona_MiPyme Persona_MiPyme { get; set; } = new();
        public object representanteLegal { get; set; } = string.Empty;
        public object agenteRepresentante { get; set; } = string.Empty;
        public string personaSociedad { get; set; } = string.Empty;
        public string subtipoContribuyente { get; set; } = string.Empty;
        public object motivoCancelacion { get; set; } = string.Empty;
        public object motivoSuspension { get; set; } = string.Empty;
        public List<PersonaEstablecimientosRuc> personaEstablecimientos { get; set; } = new();
    }

    public class Informacionfechascontribuyente
    {
        public string fechaInicioActividades { get; set; } = string.Empty;
        public object fechaCese { get; set; } = string.Empty;
        public object fechaReinicioActividades { get; set; } = string.Empty;
        public object fechaActualizacion { get; set; } = string.Empty;
    }
    public class Persona_MiPyme
    {
        public string clasificacionMiPyme { get; set; } = string.Empty;
    }
    public class PersonaEstablecimientosRuc
    {
        public object nombreFantasiaComercial { get; set; } = string.Empty;
        public string tipoEstablecimiento { get; set; } = string.Empty;
        public string direccionCompleta { get; set; } = string.Empty;
        public string estado { get; set; } = string.Empty;
        public string numeroEstablecimiento { get; set; } = string.Empty;
    }



}




