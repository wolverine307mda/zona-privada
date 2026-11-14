namespace Mda.Api.Servidor;

// Un suceso del servidor que no vive en otra tabla: aprobaciones de entrada, avisos de la guardia, runbooks, despertares…
// La línea de tiempo del servidor lo junta con los accesos y las órdenes al PC, que ya tienen su sitio.
public class EventoServidor
{
    public int Id { get; set; }
    public DateTimeOffset Hora { get; set; } = DateTimeOffset.UtcNow;
    // acceso | aprobacion | guardia | runbook | pc | alerta | sistema
    public string Tipo { get; set; } = "sistema";
    public string Titulo { get; set; } = "";
    public string Detalle { get; set; } = "";
    // info | aviso | grave
    public string Gravedad { get; set; } = "info";
}

// «Entrar desde un ordenador nuevo con el iPhone»: el ordenador pide la aprobación y enseña un código de dos cifras; el iPhone la da con Face ID
// escribiendo ese código. El ordenador guarda un secreto que solo conoce él (aquí, solo su huella) para recoger la sesión una vez aprobada.
public class AprobacionLogin
{
    public string Id { get; set; } = "";
    public string Codigo { get; set; } = "";
    public string SecretoHash { get; set; } = "";
    public DateTimeOffset Creada { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Caduca { get; set; }
    // pendiente → aprobada → usada; o denegada | caducada
    public string Estado { get; set; } = "pendiente";
    public string Ip { get; set; } = "";
    public string Agente { get; set; } = "";
    public string Lugar { get; set; } = "";
    public int Intentos { get; set; }
}

// Una secuencia de pasos con nombre («Comprobar todo», «Mantenimiento de noche»). Los pasos van en JSON (lista de PasoRunbook).
public class Runbook
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Pasos { get; set; } = "[]";
    // se ofrece como botón cuando la guardia avisa de algo
    public bool Guardia { get; set; }
    public DateTimeOffset Creado { get; set; } = DateTimeOffset.UtcNow;
}

// Una vez que se ha lanzado un runbook: qué pasos tenía, cómo fue cada uno y cómo acabó. Se ve en vivo desde la app.
public class EjecucionRunbook
{
    public int Id { get; set; }
    public int? RunbookId { get; set; }
    public string Nombre { get; set; } = "";
    // app | telegram | guardia
    public string Origen { get; set; } = "app";
    public DateTimeOffset Inicio { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? Fin { get; set; }
    // en-curso | esperando (un paso pide tu permiso) | ok | error | cancelado
    public string Estado { get; set; } = "en-curso";
    // lista de EstadoPaso en JSON
    public string Pasos { get; set; } = "[]";
}
