namespace Mda.Api.Avisos;

// Cada clase de aviso (lo del dinero, lo del coche, la salud del PC…) con la pantalla a la que lleva al pulsarlo y los canales
// por los que sale mientras no se elija otra cosa. Las preferencias se guardan en Ajustes como «avisos:{id}» = canales separados por coma.
public record TipoAviso(string Id, string Nombre, string Detalle, string Ruta, string[] PorDefecto);

public static class TiposAviso
{
    public const string Push = "push", Telegram = "telegram", Correo = "correo", WhatsApp = "whatsapp";
    public static readonly string[] Canales = [Push, Telegram, Correo, WhatsApp];

    private static readonly string[] PushTelegram = [Push, Telegram];
    private static readonly string[] Todos3 = [Push, Telegram, Correo];

    public static readonly TipoAviso[] Todos =
    [
        new("presupuestos", "Presupuestos del mes", "Cuando una categoría llega al 80 % o al 100 %", "/gastos", Todos3),
        new("cartera", "Cartera", "Presupuesto semanal, cobros que llegan y gasolina barata", "/gastos", PushTelegram),
        new("coche", "Coche", "ITV, seguro y mantenimiento que se acercan", "/gastos/deudas", Todos3),
        new("tareas", "Tareas", "Lo pendiente y lo que vence", "/tareas", PushTelegram),
        new("habitos", "Hábitos y bienestar", "Recordatorios para apuntar un hábito o moverte", "/habitos", PushTelegram),
        new("papeles", "Papeles y garantías", "Documentos que caducan y devoluciones con plazo", "/papeles", PushTelegram),
        new("copias", "Copias de seguridad", "Cuando falta una copia o la restauración no cuadra", "/seguridad", Todos3),
        new("seguridad", "Seguridad", "Equipos nuevos, señuelos y errores del servidor", "/seguridad", PushTelegram),
        new("pc", "Tu PC", "Salud, batería, descargas y bandeja de entrada", "/pc", PushTelegram),
        new("fotografia", "Fotografía y cielo", "Luz dorada, la ISS, lluvias de estrellas", "/fotografia", PushTelegram),
        new("personas", "Personas", "Cumpleaños y fechas", "/personas", PushTelegram),
        new("cocina", "Cocina", "Lo que toca cocinar hoy", "/cocina", PushTelegram),
        new("series", "Series y películas", "Estrenos y capítulos nuevos", "/series", PushTelegram),
        new("servicios", "Webs y servicios", "Cuando algo vigilado se cae o vuelve", "/servicios", PushTelegram),
        new("automatizaciones", "Automatizaciones", "Lo que hacen tus reglas «si pasa X, haz Y»", "/automatizaciones", PushTelegram),
        new("tiempo", "El tiempo", "Lo que cambia mañana: lluvia, calor, frío", "/inicio", PushTelegram),
    ];

    public static TipoAviso? Buscar(string id) => Todos.FirstOrDefault(t => t.Id == id);

    public static string Clave(string tipo) => $"avisos:{tipo}";

    // «sin fila» = lo de siempre; una fila vacía = ese tipo de aviso no sale por ningún canal
    public static string[] Interpretar(string? valor, TipoAviso tipo) =>
        valor is null ? tipo.PorDefecto
        : valor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(c => Canales.Contains(c)).Distinct().ToArray();

    public static string Guardar(IEnumerable<string> canales) => string.Join(',', canales.Where(c => Canales.Contains(c)).Distinct());
}
