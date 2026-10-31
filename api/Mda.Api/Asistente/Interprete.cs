using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Mda.Api.Agenda;
using Mda.Api.Gastos;

namespace Mda.Api.Asistente;

public record HuchaConocida(int Id, string Nombre, string Emoji);
public record AporteHucha(int Id, string Nombre, string Emoji, decimal Importe);
public enum QuePregunta { Saldo, Gastado, Ingresado, Tiempo, Agenda }
public record Pregunta(QuePregunta Que, string? Categoria, string? Concepto, DateOnly Desde, DateOnly Hasta, string Periodo);

// Lo que el asistente ha entendido de una frase. Clase: movimiento, agenda, hucha, horario, pregunta o nada.
// No guarda nada: la app lo enseña, deja retocarlo y lo guarda con los endpoints de siempre.
public record Propuesta(
    string Clase, string Resumen, MovimientoEntrada? Movimiento = null, EventoEntrada? Evento = null,
    AporteHucha? Hucha = null, Pregunta? Pregunta = null, IReadOnlyList<string>? Dudas = null, string? Respuesta = null, Horario? Horario = null);

// Lo que sabe de ti para entender mejor: la hora de casa, qué categoría sueles poner a cada concepto y tus huchas
// y tu horario de trabajo, para avisarte si una cita cae dentro
public record Contexto(DateTime Ahora, TimeZoneInfo Zona, IReadOnlyDictionary<string, string> Aprendido, IReadOnlyList<HuchaConocida> Huchas, Horario? Horario = null);

// Entiende frases sueltas en castellano, sin IA de por medio: «ayer 12 € en el Mercadona», «cobré la nómina 1.450 €»,
// «recuérdame el viernes a las 10 llamar al taller», «50 € a la hucha de Japón», «¿cuánto llevo en restaurantes?»…
public static partial class Interprete
{
    private static readonly CultureInfo Es = new("es-ES");
    private const RegexOptions O = RegexOptions.CultureInvariant;
    private const string N = @"(?<![\d.,])(\d{1,3}(?:\.\d{3})+(?:,\d{1,2})?|\d+(?:[.,]\d{1,2})?)";
    private const string Meses = "enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|setiembre|octubre|noviembre|diciembre";
    private const string Dias = "lunes|martes|miercoles|jueves|viernes|sabado|domingo";

    // ---- palabras que delatan la categoría (sin tildes; gana la coincidencia más larga) ----
    private static readonly (string Categoria, string[] Palabras)[] Pistas =
    [
        ("supermercado", ["mercadona", "carrefour", "lidl", "aldi", "dia", "alcampo", "eroski", "hipercor", "consum", "ahorramas", "froiz", "gadis", "supercor", "super", "supermercado", "la compra", "compra semanal", "fruteria", "carniceria", "panaderia", "pan"]),
        ("restaurantes", ["restaurante", "cena", "cenar", "comida", "comer", "almuerzo", "desayuno", "cafe", "cafes", "bar", "canas", "cervezas", "birras", "telepizza", "burger", "burger king", "mcdonalds", "mcdonald", "kfc", "pizza", "kebab", "glovo", "just eat", "uber eats", "sushi", "tapas", "vermut", "menu del dia", "brunch", "heladeria"]),
        ("ocio", ["cine", "concierto", "entradas", "entrada", "teatro", "fiesta", "discoteca", "copas", "steam", "playstation", "videojuego", "museo", "bolera", "escape room", "festival", "partido", "futbol", "padel", "libro", "libros", "regalo"]),
        ("ropa", ["ropa", "zara", "primark", "pull", "bershka", "stradivarius", "h&m", "zapatillas", "zapatos", "camiseta", "pantalon", "pantalones", "chaqueta", "abrigo", "sudadera", "nike", "adidas", "decathlon", "shein", "mango"]),
        ("salud", ["farmacia", "medico", "dentista", "fisio", "fisioterapeuta", "optica", "gafas", "lentillas", "gimnasio", "gym", "psicologo", "analisis", "medicinas"]),
        ("tecnologia", ["movil", "cargador", "auriculares", "portatil", "ordenador", "pc", "teclado", "raton", "monitor", "mediamarkt", "media markt", "pccomponentes", "apple store", "tablet", "consola"]),
        ("transporte", ["bus", "autobus", "metro", "taxi", "uber", "cabify", "bolt", "tren", "renfe", "ave", "avion", "vuelo", "ryanair", "vueling", "parking", "aparcamiento", "peaje", "bici", "patinete", "abono transporte", "blablacar"]),
        ("suscripciones", ["netflix", "spotify", "hbo", "max", "disney", "prime video", "amazon prime", "youtube premium", "icloud", "chatgpt", "game pass", "dazn", "movistar plus", "suscripcion", "filmin", "crunchyroll"]),
        ("gasolina", ["gasolina", "gasoil", "gasoleo", "diesel", "repsol", "cepsa", "bp", "shell", "galp", "petronor", "ballenoil", "plenoil", "repostar", "repostaje", "repostado", "lleno", "deposito"]),
        ("seguro", ["seguro", "seguro del coche", "mapfre", "mutua", "linea directa", "allianz", "axa"]),
        ("mecanico", ["taller", "mecanico", "ruedas", "neumaticos", "cambio de aceite", "aceite", "revision", "frenos", "pastillas de freno", "bateria"]),
        ("cuota-coche", ["cuota del coche", "letra del coche", "cuota coche", "plazo del coche"]),
        ("coche-extras", ["itv", "multa", "lavado", "lavar el coche", "impuesto de circulacion", "sello del coche", "dgt"]),
        ("vivienda", ["alquiler", "hipoteca", "comunidad", "ikea", "muebles", "leroy merlin", "ferreteria", "casero", "ibi"]),
        ("suministros", ["luz", "agua", "gas", "internet", "fibra", "iberdrola", "endesa", "naturgy", "holaluz", "movistar", "vodafone", "orange", "digi", "lowi", "pepephone", "factura", "telefono", "tarifa movil"]),
        ("nomina", ["nomina", "sueldo", "salario", "paga"]),
        ("ventas", ["wallapop", "vinted", "venta", "vendi", "he vendido", "milanuncios"]),
        ("extra", ["bizum", "paga extra", "horas extra", "extra", "propina", "premio", "loteria", "me regalaron", "regalo de"]),
        ("otros-ingresos", ["devolucion", "reembolso", "me devolvieron", "me han devuelto", "hacienda", "intereses"]),
    ];

    // además de las pistas, el propio nombre de cada categoría («restaurantes», «luz, agua e internet»)
    private static readonly (Regex Patron, string Categoria)[] PistasPatrones =
        Pistas.SelectMany(p => p.Palabras.Select(w => (w, p.Categoria)))
            .Concat(Categorias.Todas.SelectMany(c => new[] { (Aplanar(c.Value.Nombre), c.Key), (c.Key, c.Key) }))
            .DistinctBy(x => x.Item1)
            .Select(x => (new Regex($@"(?<![\w&]){Regex.Escape(x.Item1)}(?![\w&])", O), x.Item2)).ToArray();

    // ---- frases que dicen qué es ----
    // ---- agenda: rangos, duración, lugar, citas médicas, horario de trabajo y preguntas ----
    private const string H1 = @"(?<h1>\d{1,2})(?:[:.h](?<m1>\d{2}))?";
    private const string H2 = @"(?<h2>\d{1,2})(?:[:.h](?<m2>\d{2}))?";
    private const string Dia = "lunes|martes|miercoles|jueves|viernes|sabado|domingo";
    [GeneratedRegex(@"\b(?:de|desde)\s+(?:las?\s+)?" + H1 + @"(?:\s*h\b)?\s+(?:a|hasta)\s+(?:las?\s+)?" + H2 + @"(?:\s*(?:h|horas)\b)?(?:\s+(?:de\s+la\s+|por\s+la\s+)?(?<p>manana|tarde|noche))?", O)] private static partial Regex ReRango();
    [GeneratedRegex(@"\bhasta\s+(?:las?\s+)" + H2 + @"(?:\s+(?:de\s+la\s+)?(?<p>manana|tarde|noche))?", O)] private static partial Regex ReHasta();
    [GeneratedRegex(@"\b(?:durante|dura|de)\s+(?<n>una?|dos|tres|cuatro|media|\d+)\s+(?<u>horas?|h|minutos?|min)\b(?<y>\s+y\s+media)?", O)] private static partial Regex ReDuracion();
    [GeneratedRegex(@"\ben\s+(?:el\s+|la\s+|mi\s+)?(?<l>(?:hospital|clinica|centro\s+de\s+salud|centro\s+medico|ambulatorio|consulta|consultorio|mutua|urgencias|farmacia|oficina|gimnasio|colegio|instituto|universidad|ayuntamiento|notaria|banco|peluqueria|taller|casa\s+de|calle|avenida|avda|plaza)\b(?:\s+(?!(?:a\s+las?|el\s+(?:" + Dia + @")|este|esta|el\s+\d|hoy|manana|pasado|de\s+la\s+(?:manana|tarde|noche)|por\s+la|y|para|con|de\s+\d)\b)[\w.\-ºª/]+){0,4})", O)] private static partial Regex ReLugar();
    [GeneratedRegex(@"\b(?:medic[oa]s?|cita\s+medica|doctor[a]?|dentista|odontolog\w*|fisio\w*|hospital|centro\s+de\s+salud|ambulatorio|analisis|analitica|vacuna\w*|dermatolog\w*|oculista|oftalmolog\w*|ginecolog\w*|pediatra|traumatolog\w*|psicolog\w*|psiquiatra|urgencias|enfermer[ao]|cardiolog\w*|radiografia|resonancia|ecografia|podolog\w*|logopeda|otorrino)\b", O)] private static partial Regex ReMedico();
    [GeneratedRegex(@"\b(?:trabajo|curro|curra\w*|trabaj\w*|mi\s+horario|horario\s+de\s+trabajo|horario\s+laboral|jornada|entro|ficho)\b", O)] private static partial Regex ReHorarioClave();
    [GeneratedRegex(@"\bentro\s+(?:a\s+)?(?:las?\s+)?" + H1 + @"\b.*?\bsalgo\s+(?:a\s+)?(?:las?\s+)?" + H2, O)] private static partial Regex ReEntroSalgo();
    [GeneratedRegex(@"\b(?:de|desde\s+el)\s+(?<a>" + Dia + @")\s+(?:a|al|hasta\s+el)\s+(?<b>" + Dia + @")\b", O)] private static partial Regex ReDiasRango();
    [GeneratedRegex(@"\b(?:hoy|pasado\s+manana|el\s+\d{1,2}\b|\d{1,2}\s+de\s+(?:" + Meses + @")|(?<!(?:la|por|de)\s)manana|el\s+(?:proximo\s+)?(?:" + Dia + @")|este\s+(?:" + Dia + @"))\b", O)] private static partial Regex ReUnDia();
    [GeneratedRegex(@"\b(?:que\s+tengo|tengo\s+algo|tengo\s+(?:alguna\s+)?cita|que\s+hay\s+(?:en\s+(?:la\s+|mi\s+)?agenda|para)|mi\s+agenda|estoy\s+libre|tengo\s+(?:el\s+dia\s+)?libre|tengo\s+hueco|que\s+me\s+toca)\b", O)] private static partial Regex RePreguntaAgenda();
    [GeneratedRegex(@"\b(?:que\s+tiempo|el\s+tiempo|tiempo\s+(?:hace|hara|va\s+a\s+hacer)|llov\w*|llueve|lluvia|temperatura|grados|hace\s+(?:frio|calor|sol|viento)|hara\s+(?:frio|calor|sol|viento|bueno|malo)|paraguas|nieva|nevara|tormenta)\b", O)] private static partial Regex ReTiempo();
    [GeneratedRegex(@"^\W*(?:cuant[oa]s?|que\s+saldo|cual\s+es\s+mi\s+saldo|mi\s+saldo|saldo\b|como\s+voy|que\s+me\s+queda|en\s+que\s+(?:he\s+)?gast)", O)] private static partial Regex RePregunta();
    [GeneratedRegex(@"\b(?:saldo|cuanto\s+(?:dinero\s+)?tengo|cuanto\s+me\s+queda|que\s+me\s+queda|dinero\s+(?:tengo|ahora)|como\s+voy)\b", O)] private static partial Regex RePreguntaSaldo();
    [GeneratedRegex(@"\b(?:ingresado|cobrado|ganado|ingresos|cobre)\b", O)] private static partial Regex RePreguntaIngresos();
    [GeneratedRegex(@"\b(?:recuerdame|recordarme|recuerda(?:me)?\s+que|recordatorio|avisame|no\s+(?:me\s+)?(?:dejes\s+)?olvid\w*|acuerdame)\b", O)] private static partial Regex ReRecordatorio();
    [GeneratedRegex(@"\b(?:cita|reunion|evento|quedada|he\s+quedado|quedo|cumple(?:anos)?|boda|entrevista|examen|vuelo\s+a|viaje\s+a|en\s+la\s+agenda|en\s+el\s+calendario)\b", O)] private static partial Regex ReEvento();
    [GeneratedRegex(@"\bhuchas?\b", O)] private static partial Regex ReHucha();
    [GeneratedRegex(@"\b(?:ahorr\w*|apart\w*|meto|meter|mete|guardo|guardar|anado|sumo)\b", O)] private static partial Regex ReMeter();
    [GeneratedRegex(@"\b(?:saco|sacar|saque|quito|quitar|quite|retiro|retirar|cojo|coger|cogi)\b", O)] private static partial Regex ReSacar();
    [GeneratedRegex(@"\b(?:cobr[eoa]\w*|me\s+(?:han\s+)?(?:pagado|pagaron|pagan|ingresad\w*|ingresaron|ingresan|devuelto|devolvieron|dado|dieron|regalado|regalaron|hicieron\s+un\s+bizum|han\s+hecho\s+un\s+bizum)|ingreso|ingresos|nomina|sueldo|salario|vend[ie]\w*|venta|bizum\s+de|reembolso|devolucion|gane)\b", O)] private static partial Regex ReIngreso();
    [GeneratedRegex(@"\b(?:gast\w*|pag\w*|compr\w*|cost\w*|apunta\w*|anota\w*|factura|cuota|suscripcion|repost\w*)\b", O)] private static partial Regex ReDinero();

    // ---- importes, litros y kilómetros ----
    [GeneratedRegex(@"(?:\bpor\s+)?(?<![\d.,])(?<e>\d+)\s*(?:€|euros?)\s+con\s+(?<c>\d{1,2})\b(?:\s*(?:centimos?|cts?))?", O)] private static partial Regex ReEurosCon();
    [GeneratedRegex(@"(?:\bpor\s+)?(?:€\s*" + N + @"|" + N + @"\s*(?:€|euros?\b|eur\b|pavos?\b|pelas\b))", O)] private static partial Regex ReImporte();
    [GeneratedRegex(@"(?:\bpor\s+)?(?<![\d.,/:])(?<!\b(?:las?|el|dia)\s+)(\d{1,3}(?:\.\d{3})+(?:,\d{1,2})?|\d+(?:[.,]\d{1,2})?)(?![\d.,:/])(?!\s*(?:de\s+(?:" + Meses + @")|h\b|horas?\b|min\b|minutos?\b|dias?\b|semanas?\b|meses\b|mes\b|anos?\b|km\b|kms\b|l\b|litros?\b|y\s+media|y\s+cuarto))", O)] private static partial Regex ReNumeroSuelto();
    [GeneratedRegex(@"(?<![\d.,])(?<n>\d+[.,]\d{2,3})\s*(?:€|euros?)?\s*(?:/\s*|(?:el|por|cada|a)\s+)(?:l|litro)\b", O)] private static partial Regex RePrecioLitro();
    [GeneratedRegex(@"(?<![\d.,])(?<n>\d+(?:[.,]\d+)?)\s*(?:l|lt|lts|litros?)\b", O)] private static partial Regex ReLitros();
    [GeneratedRegex(@"(?:\b(?:a\s+los|con|en)\s+)?(?<![\d.,])(?<n>\d{1,3}(?:\.\d{3})+|\d+)\s*(?:km|kms|kilometros?)\b", O)] private static partial Regex ReKm();

    // ---- cuándo ----
    [GeneratedRegex(@"\b(?:en|dentro\s+de)\s+(?<n>\d+|un|una|media|dos|tres|cuatro|cinco|diez|quince|veinte|treinta)\s+(?<u>minutos?|min|horas?|h|dias?|semanas?|mes(?:es)?)\b", O)] private static partial Regex ReDentroDe();
    [GeneratedRegex(@"\b(?:a\s+)?(?:las|la)\s+(?<h>\d{1,2})(?:(?:[:.]|h)(?<m>\d{2})|\s+y\s+(?<f>media|cuarto)|\s+menos\s+(?<menos>cuarto))?(?:\s*(?:h|horas)\b)?(?:\s+(?:de\s+la\s+|por\s+la\s+|del\s+)?(?<p>manana|tarde|noche|madrugada|mediodia))?", O)] private static partial Regex ReALas();
    [GeneratedRegex(@"(?<![\d.,])(?<h>\d{1,2})(?::(?<m>\d{2})|\s*h\b)", O)] private static partial Regex ReReloj();
    [GeneratedRegex(@"\b(?<esta>esta|por\s+la|de\s+la|a\s+la)\s+(?<p>manana|tarde|noche|madrugada)\b|\b(?:al\s+|a\s+)?(?<p>mediodia)\b", O)] private static partial Regex ReFranja();
    [GeneratedRegex(@"\bpasado\s+manana\b", O)] private static partial Regex RePasadoManana();
    [GeneratedRegex(@"\b(?:anteayer|antier|antes\s+de\s+ayer)\b", O)] private static partial Regex ReAnteayer();
    [GeneratedRegex(@"\bayer\b", O)] private static partial Regex ReAyer();
    [GeneratedRegex(@"\bhoy\b", O)] private static partial Regex ReHoy();
    [GeneratedRegex(@"\bmanana\b", O)] private static partial Regex ReManana();
    [GeneratedRegex(@"\b(?:la\s+)?(?:semana\s+que\s+viene|proxima\s+semana)\b", O)] private static partial Regex ReSemanaQueViene();
    [GeneratedRegex(@"\b(?:este|el)\s+(?:finde|fin\s+de\s+semana)\b", O)] private static partial Regex ReFinde();
    [GeneratedRegex(@"\b(?:el\s+|este\s+)?(?<antes>proximo\s+|pasado\s+)?(?<d>" + Dias + @")(?<q>\s+que\s+viene|\s+pasado|\s+proximo)?\b", O)] private static partial Regex ReDiaSemana();
    [GeneratedRegex(@"\b(?:el\s+)?(?:dia\s+)?(?<d>\d{1,2})\s+de\s+(?<mes>" + Meses + @")(?:\s+(?:de|del)\s+(?<a>\d{4}))?\b", O)] private static partial Regex ReFechaLarga();
    [GeneratedRegex(@"(?<![\d.,/])(?<d>\d{1,2})[/-](?<mes>\d{1,2})(?:[/-](?<a>\d{2,4}))?(?![\d/])", O)] private static partial Regex ReFechaCorta();
    [GeneratedRegex(@"\b(?:el\s+dia|el|dia)\s+(?<d>\d{1,2})\b(?!\s*(?:€|euros?|[.,]\d|:))", O)] private static partial Regex ReElDia();

    // ---- repetición y otros ----
    [GeneratedRegex(@"\b(?:cada\s+dia|todos\s+los\s+dias|a\s+diario|diariamente)\b", O)] private static partial Regex ReDiario();
    [GeneratedRegex(@"\b(?:todos\s+los|cada)\s+(?<d>" + Dias + @")\b", O)] private static partial Regex ReCadaDiaSemana();
    [GeneratedRegex(@"\b(?:cada\s+semana|todas\s+las\s+semanas|semanal(?:mente)?)\b", O)] private static partial Regex ReSemanal();
    [GeneratedRegex(@"\b(?:cada\s+(?:3|tres)\s+meses|trimestral(?:mente)?|al\s+trimestre)\b", O)] private static partial Regex ReTrimestral();
    [GeneratedRegex(@"\b(?:cada\s+mes|todos\s+los\s+meses|al\s+mes|mensual(?:es|mente)?|a\s+mes)\b", O)] private static partial Regex ReMensual();
    [GeneratedRegex(@"\b(?:cada\s+ano|todos\s+los\s+anos|al\s+ano|anual(?:es|mente)?)\b", O)] private static partial Regex ReAnual();
    [GeneratedRegex(@"\b(?:se\s+repite|que\s+se\s+repite|fijo)\b", O)] private static partial Regex ReSeRepite();
    [GeneratedRegex(@"\b(?:puede\s+variar|varia|variable|aprox(?:imadamente)?|mas\s+o\s+menos|unos|unas)\b", O)] private static partial Regex ReVariable();
    [GeneratedRegex(@"\s+", O)] private static partial Regex ReEspacios();
    // «por whatsapp», «avísame por correo y telegram», «por todas partes»: por dónde avisar
    private const string Canal = @"(?:whats\s?app|wh?asap|wsp|correo(?:\s+electronico)?|e-?mail|mail|telegram|(?:la\s+)?app(?:licacion)?|el\s+movil|notificaci\w+|push|todas?(?:\s+(?:partes|los\s+canales|las\s+apps|las\s+aplicaciones|los\s+sitios|los\s+medios))?|todos\s+(?:los\s+)?(?:canales|sitios|lados|medios))";
    [GeneratedRegex(@"(?:\b(?:y\s+)?(?:que\s+me\s+)?(?:avis\w*|notific\w*|mand\w*|envi\w*)\s+)?\bpor\s+(?<c>" + Canal + @")(?:\s*(?:,|\by\b|\be\b)\s*(?:por\s+)?(?<c>" + Canal + @"))*\b", O)] private static partial Regex ReCanales();

    // palabras de relleno: los verbos se quitan estén donde estén; las otras, solo en los bordes
    private static readonly HashSet<string> Verbos =
    [
        "me", "he", "hemos", "gaste", "gastado", "gastamos", "gastarme", "pague", "pagado", "pagamos", "compre", "comprado", "compramos",
        "cobre", "cobrado", "cobro", "apunta", "apuntame", "apuntar", "anota", "anotame", "anotar", "anade", "recuerdame", "recordarme",
        "recuerda", "recordatorio", "avisame", "acuerdame", "olvide", "olvides", "dejes", "pon", "ponme", "gasto", "ingreso", "ingresaron",
        "ingresado", "pagaron", "devolvieron", "devuelto", "hicieron", "hecho", "dieron", "dado", "costo", "cuesta", "han", "hucha", "huchas",
        "vendi", "vendido", "agenda", "calendario", "repostado", "repostar",
        // «nuevo gasto…», «crea un recordatorio con concepto…»: son la orden, no el título
        "nuevo", "nueva", "nuevos", "nuevas", "anadir", "anademe", "anada", "crea", "crear", "creame", "mete", "meter", "meteme",
        "registra", "registrar", "registrame", "guarda", "guardar", "guardame", "concepto", "titulo", "titulado", "titulada",
        "llamado", "llamada", "asunto", "motivo", "evento", "pago", "otro", "otra",
    ];
    private static readonly HashSet<string> Bordes =
    [
        "en", "el", "la", "los", "las", "un", "una", "unos", "unas", "de", "del", "al", "a", "por", "para", "con", "y", "que", "mi", "mis",
        "lo", "total", "se", "le", "les", "esta", "este", "hay", "tengo", "otro", "otra", "unos", "o", "sobre", "son", "es", "fue", "eran",
    ];

    // Minúsculas y sin tildes, letra a letra (la misma longitud, para poder recortar el texto original)
    public static string Aplanar(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(char.ToLowerInvariant(c.ToString().Normalize(NormalizationForm.FormD)[0]));
        return sb.ToString();
    }

    public static Propuesta Entender(string texto, Contexto ctx)
    {
        texto = ReEspacios().Replace(texto.Trim(), " ");
        if (texto.Length == 0) return new("nada", "Dime algo como «ayer 12 € en el Mercadona».");
        var f = new Frase(texto);
        var hoy = DateOnly.FromDateTime(ctx.Ahora);

        // «avísame por whatsapp» dice por dónde avisar, no que sea un recordatorio
        var recordatorio = ReRecordatorio().IsMatch(ReCanales().Replace(f.Plano, " "));
        // «¿qué tengo mañana?», «¿estoy libre el jueves?»
        if (!recordatorio && RePreguntaAgenda().IsMatch(f.Plano))
        {
            Hora(f, ctx);
            var dia = Fecha(f, hoy, futuro: true) ?? hoy;
            return new("pregunta", "Tu agenda", Pregunta: new(QuePregunta.Agenda, null, null, dia, dia, DiaBonito(dia, hoy)));
        }
        // «curro de 8:30 a 17:30 de lunes a viernes»
        if (!recordatorio && DeHorario(f) is { } horario) return horario;
        // «¿qué tiempo hace?», «¿va a llover mañana?», «¿hará frío el sábado?» (pero «recuérdame coger el paraguas» es un recordatorio)
        if (!recordatorio && ReTiempo().IsMatch(f.Plano) && !ReImporte().IsMatch(f.Plano))
        {
            Hora(f, ctx); // que «esta mañana» no se lea como mañana
            var dia = Fecha(f, hoy, futuro: true) ?? hoy;
            return new("pregunta", "El tiempo", Pregunta: new(QuePregunta.Tiempo, null, null, dia, dia, dia == hoy ? "hoy" : dia == hoy.AddDays(1) ? "mañana" : dia.ToString("dddd d", Es)));
        }

        if (RePregunta().IsMatch(f.Plano) || f.Plano.EndsWith('?') && f.Plano.Contains("cuant")) return Preguntar(f, ctx, hoy);

        var suelto = ReNumeroSuelto().Match(f.Plano);
        var deAgenda = ReEvento().IsMatch(f.Plano) || ReALas().IsMatch(f.Plano) || ReReloj().IsMatch(f.Plano) || ReRango().IsMatch(f.Plano) || ReMedico().IsMatch(f.Plano);
        // «médico el martes de 10 a 11:30»: con una hora o un rango y sin € ni «pagué», es una cita aunque «médico» sea de la categoría salud
        var conHora = ReRango().IsMatch(f.Plano) || (ReALas().IsMatch(f.Plano) || ReReloj().IsMatch(f.Plano)) && ReMedico().IsMatch(f.Plano);
        var dinero = !recordatorio && (ReImporte().IsMatch(f.Plano) || ReEurosCon().IsMatch(f.Plano)
            || suelto.Success && (Regex.IsMatch(suelto.Groups[1].Value, @"[.,]\d{2}$") || !deAgenda || ReDinero().IsMatch(f.Plano)
                || ReIngreso().IsMatch(f.Plano) || CategoriaDe(f.Plano, ctx) is not null && !conHora));
        var hucha = recordatorio ? null : HuchaDe(f.Plano, ctx);
        var conNumero = ReImporte().IsMatch(f.Plano) || ReNumeroSuelto().IsMatch(f.Plano);
        var esHucha = !recordatorio && conNumero && (ReHucha().IsMatch(f.Plano) || hucha is not null && (ReMeter().IsMatch(f.Plano) || ReSacar().IsMatch(f.Plano)));

        if (esHucha) return DeHucha(f, ctx, hucha);
        if (dinero) return DeMovimiento(f, ctx, hoy);
        return DeAgenda(f, ctx, hoy, recordatorio);
    }

    // ---- gastos e ingresos ----
    private static Propuesta DeMovimiento(Frase f, Contexto ctx, DateOnly hoy)
    {
        var dudas = new List<string>();
        var (frecuencia, _, _) = LeerRepeticion(f);
        var variable = f.Coger(ReVariable()) is not null;
        f.Coger(ReSeRepite());

        decimal? precioLitro = f.Coger(RePrecioLitro()) is { } pl ? Numero(pl.Groups["n"].Value) : null;
        decimal? litros = f.Coger(ReLitros()) is { } l ? Numero(l.Groups["n"].Value) : null;
        int? km = f.Coger(ReKm()) is { } k ? (int)Numero(k.Groups["n"].Value) : null;

        decimal? importe = null;
        if (f.Coger(ReEurosCon()) is { } ec) importe = int.Parse(ec.Groups["e"].Value) + int.Parse(ec.Groups["c"].Value.PadRight(2, '0')) / 100m;
        else if (f.Coger(ReImporte()) is { } im) importe = Numero(im.Groups[1].Success ? im.Groups[1].Value : im.Groups[2].Value);

        Hora(f, ctx); // la hora no pinta nada en un apunte, pero no debe quedarse en el concepto (y «esta mañana» no es mañana)
        var fecha = Fecha(f, hoy, futuro: false) ?? hoy;
        if (importe is null && f.Coger(ReNumeroSuelto()) is { } ns) importe = Numero(ns.Groups[1].Value);
        if (importe is null && litros is { } li && precioLitro is { } pr) importe = Math.Round(li * pr, 2);
        if (importe is not { } euros || euros <= 0) return new("nada", "No veo cuánto ha costado. Prueba con «12 € en el Mercadona».");

        var cat = CategoriaDe(f.Plano, ctx);
        var tipo = ReIngreso().IsMatch(f.Plano) || cat is not null && Categorias.Todas[cat].Tipo == TipoMovimiento.Ingreso
            ? TipoMovimiento.Ingreso : TipoMovimiento.Gasto;
        if (litros is not null || km is not null) { tipo = TipoMovimiento.Gasto; cat = "gasolina"; }
        if (cat is null || !Categorias.Valida(cat, tipo))
        {
            cat = tipo == TipoMovimiento.Gasto ? "otros" : "otros-ingresos";
            dudas.Add($"No sé de qué es: lo dejo en «{Categorias.Todas[cat].Nombre}». Toca la categoría para cambiarla.");
        }
        var concepto = Concepto(f.Resto());
        if (concepto.Length == 0) concepto = cat == "gasolina" ? "Repostaje" : Categorias.Todas[cat].Nombre;
        if (concepto.Length > 120) concepto = concepto[..120];
        if (cat == "gasolina" && litros is { } lt && precioLitro is null) precioLitro = Math.Round(euros / lt, 3);
        if (fecha > hoy) dudas.Add("Es de un día que aún no ha llegado: no contará en el saldo hasta entonces.");

        var m = new MovimientoEntrada(tipo, fecha, Math.Round(euros, 2), cat, concepto, null, null,
            cat == "gasolina" ? litros : null, cat == "gasolina" ? precioLitro : null, cat == "gasolina" ? km : null,
            frecuencia, frecuencia is not null && variable);
        var que = tipo == TipoMovimiento.Gasto ? "Gasto" : "Ingreso";
        var resumen = $"{que} de {Cartera.Euros(m.Importe)} · {concepto} · {Categorias.Todas[cat].Nombre} · {DiaBonito(fecha, hoy)}"
            + (frecuencia is { } fr ? $" · se repite {Cada(fr)}" : "");
        return new("movimiento", resumen, Movimiento: m, Dudas: dudas);
    }

    // ---- huchas ----
    private static Propuesta DeHucha(Frase f, Contexto ctx, HuchaConocida? h)
    {
        var dudas = new List<string>();
        if (h is null)
        {
            if (ctx.Huchas.Count == 1) h = ctx.Huchas[0];
            else return new("nada", ctx.Huchas.Count == 0
                ? "Aún no tienes huchas. Crea una en Cartera › Huchas."
                : $"¿En qué hucha? Tienes: {string.Join(", ", ctx.Huchas.Select(x => x.Nombre))}.");
        }
        decimal? importe = null;
        if (f.Coger(ReEurosCon()) is { } ec) importe = int.Parse(ec.Groups["e"].Value) + int.Parse(ec.Groups["c"].Value.PadRight(2, '0')) / 100m;
        else if (f.Coger(ReImporte()) is { } im) importe = Numero(im.Groups[1].Success ? im.Groups[1].Value : im.Groups[2].Value);
        else if (f.Coger(ReNumeroSuelto()) is { } ns) importe = Numero(ns.Groups[1].Value);
        if (importe is not { } euros || euros <= 0) return new("nada", $"¿Cuánto meto en «{h.Nombre}»?");
        var sacar = ReSacar().IsMatch(f.Plano);
        var a = new AporteHucha(h.Id, h.Nombre, h.Emoji, sacar ? -euros : euros);
        return new("hucha", sacar ? $"Sacar {Cartera.Euros(euros)} de la hucha {h.Emoji} {h.Nombre}" : $"Meter {Cartera.Euros(euros)} en la hucha {h.Emoji} {h.Nombre}",
            Hucha: a, Dudas: dudas);
    }

    // ---- agenda ----
    private static Propuesta DeAgenda(Frase f, Contexto ctx, DateOnly hoy, bool recordatorio)
    {
        var dudas = new List<string>();
        var (_, repeticion, diaFijo) = LeerRepeticion(f);
        f.Coger(ReSeRepite());
        var canales = LeerCanales(f);

        // «en el centro de salud de la calle Mayor»: el lugar va aparte y no ensucia el título
        string? lugar = null;
        if (!recordatorio && f.Coger(ReLugar()) is { } lg)
        {
            var l = f.Original.Substring(lg.Groups["l"].Index, lg.Groups["l"].Length).Trim();
            lugar = char.ToUpper(l[0], Es) + l[1..];
        }
        var medico = !recordatorio && ReMedico().IsMatch(f.Plano);

        // «de 10 a 11:30», «desde las 16 hasta las 18»
        TimeOnly? desde = null, hasta = null;
        if (f.Coger(ReRango()) is { } rg)
        {
            var franja = rg.Groups["p"].Success ? rg.Groups["p"].Value : null;
            desde = Ajustar(int.Parse(rg.Groups["h1"].Value), rg.Groups["m1"].Success ? int.Parse(rg.Groups["m1"].Value) : 0, franja, ctx);
            hasta = FinDe(int.Parse(rg.Groups["h2"].Value), rg.Groups["m2"].Success ? int.Parse(rg.Groups["m2"].Value) : 0, franja, desde);
        }

        DateTime? exacto = null;
        if (f.Coger(ReDentroDe()) is { } dd)
        {
            var n = dd.Groups["n"].Value switch { "un" or "una" => 1, "media" => 0.5, "dos" => 2, "tres" => 3, "cuatro" => 4, "cinco" => 5, "diez" => 10, "quince" => 15, "veinte" => 20, "treinta" => 30, var x => double.Parse(x, CultureInfo.InvariantCulture) };
            var u = dd.Groups["u"].Value;
            exacto = u.StartsWith("min") ? ctx.Ahora.AddMinutes(n) : u.StartsWith('h') ? ctx.Ahora.AddHours(n) : u.StartsWith("dia") ? ctx.Ahora.AddDays(n)
                : u.StartsWith("sem") ? ctx.Ahora.AddDays(7 * n) : ctx.Ahora.AddMonths((int)Math.Max(1, n));
            if (!u.StartsWith("min") && !u.StartsWith('h')) exacto = null; // «dentro de 3 días» es una fecha: la hora sale aparte
            if (exacto is null) f.Desmarcar(dd); // que lo lea Fecha
        }
        var hora = exacto is null ? desde ?? Hora(f, ctx) : null;
        // «a las 10 hasta las 12», «durante hora y media»
        if (hora is not null && hasta is null && f.Coger(ReHasta()) is { } hs)
            hasta = FinDe(int.Parse(hs.Groups["h2"].Value), hs.Groups["m2"].Success ? int.Parse(hs.Groups["m2"].Value) : 0, hs.Groups["p"].Success ? hs.Groups["p"].Value : null, hora);
        TimeSpan? duracion = null;
        if (hasta is null && f.Coger(ReDuracion()) is { } du)
        {
            var n = du.Groups["n"].Value switch { "un" or "una" => 1, "dos" => 2, "tres" => 3, "cuatro" => 4, "media" => 0.5, var x => double.Parse(x, CultureInfo.InvariantCulture) };
            if (du.Groups["y"].Success) n += 0.5;
            duracion = du.Groups["u"].Value.StartsWith('m') ? TimeSpan.FromMinutes(n) : TimeSpan.FromHours(n);
            if (duracion <= TimeSpan.Zero || duracion > TimeSpan.FromHours(24)) duracion = null;
        }
        var fecha = exacto is { } ex ? DateOnly.FromDateTime(ex) : Fecha(f, hoy, futuro: true);
        if (diaFijo is { } dsem && fecha is null) fecha = Proximo(hoy, dsem, incluirHoy: true);

        var hayCuando = exacto is not null || hora is not null || fecha is not null;
        var esEvento = !recordatorio && (ReEvento().IsMatch(f.Plano) || hayCuando);
        if (!recordatorio && !esEvento) return new("nada", "No te he entendido. Prueba con «ayer 12 € en el Mercadona» o «recuérdame mañana a las 9 llamar al taller».");

        var titulo = Concepto(f.Resto());
        if (titulo.Length == 0) return new("nada", recordatorio ? "¿Qué te recuerdo? Por ejemplo: «recuérdame mañana pagar la luz»." : "¿Cómo se llama el evento?");
        if (titulo.Length > 200) titulo = titulo[..200];

        var todoElDia = false;
        DateTime inicio;
        if (exacto is { } e) inicio = new DateTime(e.Year, e.Month, e.Day, e.Hour, e.Minute, 0);
        else if (hora is { } h)
        {
            var dia = fecha ?? hoy;
            inicio = dia.ToDateTime(h);
            if (fecha is null && inicio <= ctx.Ahora) inicio = inicio.AddDays(1); // «a las 9» cuando ya pasaron: mañana
        }
        else if (fecha is { } d)
        {
            if (recordatorio) inicio = d.ToDateTime(new TimeOnly(9, 0));
            else { inicio = d.ToDateTime(TimeOnly.MinValue); todoElDia = true; }
        }
        else
        {
            inicio = hoy.AddDays(1).ToDateTime(new TimeOnly(9, 0));
            dudas.Add("No me has dicho cuándo: lo pongo mañana a las 9.");
        }
        if (inicio < ctx.Ahora.AddMinutes(-1) && repeticion == Repeticion.No) dudas.Add("Esa hora ya ha pasado.");

        var ini = new DateTimeOffset(inicio, ctx.Zona.GetUtcOffset(inicio));
        var tipo = recordatorio ? TipoEvento.Recordatorio : TipoEvento.Evento;
        // a las citas médicas se avisa con una hora, para que dé tiempo a llegar
        var aviso = todoElDia ? -540 : recordatorio ? 0 : medico ? 60 : 30;
        DateTime? finLocal = tipo == TipoEvento.Recordatorio || todoElDia ? null
            : hasta is { } hf && inicio.Date.Add(hf.ToTimeSpan()) > inicio ? inicio.Date.Add(hf.ToTimeSpan())
            : inicio + (duracion ?? TimeSpan.FromHours(1));
        DateTimeOffset? fin = finLocal is { } fl ? new DateTimeOffset(fl, ctx.Zona.GetUtcOffset(fl)) : null;
        var ev = new EventoEntrada(tipo, titulo, null, lugar, ini, fin, todoElDia, medico ? "verde" : "",
            repeticion, null, aviso, canales.Correo, canales.WhatsApp, false, false, canales.Push, canales.Telegram);
        if (finLocal is { } fl2 && ctx.Horario is { Activo: true } hz && hz.Pisa(inicio, fl2))
            dudas.Add($"Cae en tu horario de {hz.Nombre.ToLower(Es)} ({Horario.Corta(hz.HoraEntrada)}–{Horario.Corta(hz.HoraSalida)}).");
        var cuando = todoElDia ? DiaBonito(DateOnly.FromDateTime(inicio), hoy)
            : $"{DiaBonito(DateOnly.FromDateTime(inicio), hoy)} a las {inicio:HH:mm}" + (hasta is not null || duracion is not null ? $"–{finLocal:HH:mm}" : "");
        if (lugar is not null) cuando += $" · 📍 {lugar}";
        var rep = repeticion switch
        {
            Repeticion.Diaria => " · cada día", Repeticion.Semanal => " · cada semana",
            Repeticion.Mensual => " · cada mes", Repeticion.Anual => " · cada año", _ => "",
        };
        return new("agenda", $"{(recordatorio ? "Recordatorio" : "Evento")}: {titulo} · {cuando}{rep} · 🔔 {canales.Texto}", Evento: ev, Dudas: dudas);
    }

    // ---- preguntas ----
    private static Propuesta Preguntar(Frase f, Contexto ctx, DateOnly hoy)
    {
        if (RePreguntaSaldo().IsMatch(f.Plano) && !f.Plano.Contains("gast"))
            return new("pregunta", "Tu saldo", Pregunta: new(QuePregunta.Saldo, null, null, hoy, hoy, "ahora"));
        var que = RePreguntaIngresos().IsMatch(f.Plano) ? QuePregunta.Ingresado : QuePregunta.Gastado;
        var primeroMes = new DateOnly(hoy.Year, hoy.Month, 1);
        var lunes = hoy.AddDays(-(((int)hoy.DayOfWeek + 6) % 7));
        var p = f.Plano;
        (DateOnly Desde, DateOnly Hasta, string Texto) periodo =
            p.Contains("mes pasado") ? (primeroMes.AddMonths(-1), primeroMes.AddDays(-1), "el mes pasado")
            : p.Contains("semana pasada") ? (lunes.AddDays(-7), lunes.AddDays(-1), "la semana pasada")
            : p.Contains("semana") ? (lunes, hoy, "esta semana")
            : ReAyer().IsMatch(p) ? (hoy.AddDays(-1), hoy.AddDays(-1), "ayer")
            : ReHoy().IsMatch(p) ? (hoy, hoy, "hoy")
            : p.Contains("ano pasado") ? (new DateOnly(hoy.Year - 1, 1, 1), new DateOnly(hoy.Year - 1, 12, 31), "el año pasado")
            : Regex.IsMatch(p, @"\b(?:este\s+ano|en\s+el\s+ano|al\s+ano)\b") ? (new DateOnly(hoy.Year, 1, 1), hoy, "este año")
            : MesNombrado(p, hoy) is { } mn ? mn
            : (primeroMes, hoy, "este mes");
        var cat = CategoriaDe(p, ctx);
        // si nombras una tienda o un concepto tuyo («Mercadona», «Gimnasio Basic»), se suma por concepto, que es más fino que la categoría
        var concepto = Marcas.Where(w => Palabra(p, w))
            .Concat(ctx.Aprendido.Keys.Where(k => k.Length >= 3 && !EsPista(k) && Palabra(p, k)))
            .OrderByDescending(w => w.Length).FirstOrDefault();
        if (cat is not null && que == QuePregunta.Ingresado && Categorias.Todas[cat].Tipo != TipoMovimiento.Ingreso) cat = null;
        return new("pregunta", "Tu pregunta", Pregunta: new(que, cat, concepto, periodo.Desde, periodo.Hasta, periodo.Texto));
    }

    private static readonly HashSet<string> Marcas =
        ["mercadona", "carrefour", "lidl", "aldi", "alcampo", "eroski", "hipercor", "consum", "ahorramas", "telepizza", "burger king", "mcdonalds", "glovo", "zara", "primark", "amazon", "netflix", "spotify", "repsol", "cepsa", "ikea", "decathlon", "iberdrola", "endesa", "uber", "cabify", "renfe", "steam"];

    private static (DateOnly, DateOnly, string)? MesNombrado(string p, DateOnly hoy)
    {
        var m = Regex.Match(p, @"\b(?:en\s+|de\s+)(?<mes>" + Meses + @")\b");
        if (!m.Success) return null;
        var n = NumeroMes(m.Groups["mes"].Value);
        var anio = n > hoy.Month ? hoy.Year - 1 : hoy.Year;
        var desde = new DateOnly(anio, n, 1);
        var hasta = desde.AddMonths(1).AddDays(-1);
        return (desde, hasta < hoy ? hasta : hoy, $"en {Es.DateTimeFormat.GetMonthName(n)}");
    }

    // ---- piezas ----
    private static (Frecuencia? Dinero, Repeticion Agenda, DayOfWeek? Dia) LeerRepeticion(Frase f)
    {
        if (f.Coger(ReCadaDiaSemana()) is { } cd) return (null, Repeticion.Semanal, DiaSemana(cd.Groups["d"].Value));
        if (f.Coger(ReDiario()) is not null) return (null, Repeticion.Diaria, null);
        if (f.Coger(ReSemanal()) is not null) return (null, Repeticion.Semanal, null);
        if (f.Coger(ReTrimestral()) is not null) return (Frecuencia.Trimestral, Repeticion.Mensual, null);
        if (f.Coger(ReMensual()) is not null) return (Frecuencia.Mensual, Repeticion.Mensual, null);
        if (f.Coger(ReAnual()) is not null) return (Frecuencia.Anual, Repeticion.Anual, null);
        return (null, Repeticion.No, null);
    }

    private static TimeOnly? Hora(Frase f, Contexto ctx)
    {
        if (f.Coger(ReALas()) is { } a)
        {
            var h = int.Parse(a.Groups["h"].Value);
            var m = a.Groups["m"].Success ? int.Parse(a.Groups["m"].Value) : a.Groups["f"].Value == "media" ? 30 : a.Groups["f"].Value == "cuarto" ? 15 : 0;
            if (a.Groups["menos"].Success) { h -= 1; m = 45; }
            return Ajustar(h, m, a.Groups["p"].Success ? a.Groups["p"].Value : FranjaSuelta(f), ctx);
        }
        if (f.Coger(ReReloj()) is { } r)
            return Ajustar(int.Parse(r.Groups["h"].Value), r.Groups["m"].Success ? int.Parse(r.Groups["m"].Value) : 0, FranjaSuelta(f), ctx, ambigua: false);
        if (f.Coger(ReFranja()) is { } fr)
        {
            if (fr.Groups["esta"].Value == "esta") f.Hoy = true;
            return fr.Groups["p"].Value switch { "manana" => new(9, 0), "mediodia" => new(14, 0), "tarde" => new(17, 0), "noche" => new(21, 0), _ => new(3, 0) };
        }
        return null;
    }

    private static string? FranjaSuelta(Frase f) => f.Coger(ReFranja()) is { } fr ? fr.Groups["p"].Value : null;

    // la hora de fin de un rango: «de 10 a 1» acaba a las 13, «de 4 a 6» (ya de tarde) a las 18
    private static TimeOnly? FinDe(int h, int m, string? franja, TimeOnly? desde)
    {
        if (h > 23 || m > 59) return null;
        if (franja is "tarde" or "noche" && h < 12) h += 12;
        if (desde is { } d && h < 12 && new TimeOnly(h, m) <= d && h + 12 < 24 && new TimeOnly(h + 12, m) > d) h += 12;
        return new TimeOnly(h, m);
    }

    private static readonly string[] NumDia = ["", "lunes", "martes", "miercoles", "jueves", "viernes", "sabado", "domingo"];

    // «curro de 8:30 a 17:30», «trabajo de lunes a jueves de 9 a 18», «entro a las 7 y salgo a las 15», «los sábados trabajo de 10 a 14»
    // Si dice un día concreto («mañana curro de 8 a 15»), no es el horario: es un evento de ese día.
    private static Propuesta? DeHorario(Frase f)
    {
        var p = f.Plano;
        if (!ReHorarioClave().IsMatch(p) || ReEvento().IsMatch(p) || ReUnDia().IsMatch(p)) return null;
        var m = ReEntroSalgo().Match(p);
        if (!m.Success) m = ReRango().Match(p);
        if (!m.Success) return null;
        int h1 = int.Parse(m.Groups["h1"].Value), m1 = m.Groups["m1"].Success ? int.Parse(m.Groups["m1"].Value) : 0;
        int h2 = int.Parse(m.Groups["h2"].Value), m2 = m.Groups["m2"].Success ? int.Parse(m.Groups["m2"].Value) : 0;
        if (m.Groups["p"].Success && m.Groups["p"].Value is "tarde" or "noche" && h2 < 12) h2 += 12;
        if (h2 * 60 + m2 <= h1 * 60 + m1 && h2 < 12) h2 += 12; // «de 8 a 3» es hasta las 15
        if (h1 > 23 || h2 > 23 || m1 > 59 || m2 > 59 || h2 * 60 + m2 <= h1 * 60 + m1) return null;

        int[] dias;
        if (ReDiasRango().Match(p) is { Success: true } r)
        {
            int a = Array.IndexOf(NumDia, r.Groups["a"].Value), b = Array.IndexOf(NumDia, r.Groups["b"].Value);
            dias = a <= b ? Enumerable.Range(a, b - a + 1).ToArray() : Enumerable.Range(a, 8 - a).Concat(Enumerable.Range(1, b)).ToArray();
        }
        else if (Regex.IsMatch(p, @"\b(?:todos\s+los\s+dias|cada\s+dia|a\s+diario)\b")) dias = [1, 2, 3, 4, 5, 6, 7];
        else if (Regex.IsMatch(p, @"\bfines?\s+de\s+semana\b")) dias = [6, 7];
        else
        {
            dias = Regex.Matches(p, @"\b(?<d>lunes|martes|miercoles|jueves|viernes|sabado|domingo)s?\b")
                .Select(x => Array.IndexOf(NumDia, x.Groups["d"].Value)).Distinct().Order().ToArray();
            if (dias.Length == 0) dias = [1, 2, 3, 4, 5]; // sin días: entre semana
        }
        var h = new Horario(true, "Trabajo", dias, $"{h1:D2}:{m1:D2}", $"{h2:D2}:{m2:D2}");
        return new("horario", $"Tu horario de trabajo: {h.Texto()}", Horario: h);
    }

    private static TimeOnly? Ajustar(int h, int m, string? franja, Contexto ctx, bool ambigua = true)
    {
        if (h > 23 || m > 59) return null;
        if (franja is "tarde" or "noche" && h < 12) h += 12;
        else if (franja is "mediodia" && h < 6) h += 12;
        else if (franja is null && ambigua && h is >= 1 and <= 7) h += 12; // «a las 5» casi siempre es por la tarde
        if (h == 24) h = 0;
        return new TimeOnly(h, m);
    }

    private static DateOnly? Fecha(Frase f, DateOnly hoy, bool futuro)
    {
        if (f.Coger(RePasadoManana()) is not null) return hoy.AddDays(2);
        if (f.Coger(ReAnteayer()) is not null) return hoy.AddDays(-2);
        if (f.Coger(ReAyer()) is not null) return hoy.AddDays(-1);
        if (f.Coger(ReHoy()) is not null || f.Hoy) return hoy;
        if (f.Coger(ReManana()) is not null) return hoy.AddDays(1);
        if (f.Coger(ReDentroDe()) is { } dd)
        {
            var n = dd.Groups["n"].Value switch { "un" or "una" => 1, "dos" => 2, "tres" => 3, "cuatro" => 4, "cinco" => 5, "diez" => 10, "quince" => 15, "veinte" => 20, "treinta" => 30, var x => int.TryParse(x, out var v) ? v : 1 };
            var u = dd.Groups["u"].Value;
            return u.StartsWith("dia") ? hoy.AddDays(n) : u.StartsWith("sem") ? hoy.AddDays(7 * n) : hoy.AddMonths(n);
        }
        if (f.Coger(ReSemanaQueViene()) is not null) return Proximo(hoy, DayOfWeek.Monday, incluirHoy: false);
        if (f.Coger(ReFinde()) is not null) return futuro ? Proximo(hoy, DayOfWeek.Saturday, incluirHoy: true) : Anterior(hoy, DayOfWeek.Saturday);
        if (f.Coger(ReFechaLarga()) is { } fl)
            return Resolver(int.Parse(fl.Groups["d"].Value), NumeroMes(fl.Groups["mes"].Value), fl.Groups["a"].Success ? int.Parse(fl.Groups["a"].Value) : null, hoy, futuro);
        if (f.Coger(ReFechaCorta()) is { } fc && int.Parse(fc.Groups["mes"].Value) is >= 1 and <= 12 and var mc)
        {
            int? a = fc.Groups["a"].Success ? int.Parse(fc.Groups["a"].Value) : null;
            if (a is < 100) a += 2000;
            return Resolver(int.Parse(fc.Groups["d"].Value), mc, a, hoy, futuro);
        }
        if (f.Coger(ReDiaSemana()) is { } ds)
        {
            var d = DiaSemana(ds.Groups["d"].Value);
            var pasado = ds.Groups["antes"].Value.StartsWith("pasado") || ds.Groups["q"].Value.Contains("pasado");
            var siguiente = ds.Groups["antes"].Value.StartsWith("proximo") || ds.Groups["q"].Value.Contains("viene") || ds.Groups["q"].Value.Contains("proximo");
            if (pasado) return Anterior(hoy.AddDays(-1), d);
            if (siguiente || futuro) return Proximo(hoy, d, incluirHoy: !siguiente);
            return Anterior(hoy, d);
        }
        if (f.Coger(ReElDia()) is { } ed && int.Parse(ed.Groups["d"].Value) is >= 1 and <= 31 and var dia)
        {
            var m = new DateOnly(hoy.Year, hoy.Month, 1);
            if (futuro && dia < hoy.Day) m = m.AddMonths(1);
            if (!futuro && dia > hoy.Day) m = m.AddMonths(-1);
            return new DateOnly(m.Year, m.Month, Math.Min(dia, DateTime.DaysInMonth(m.Year, m.Month)));
        }
        return null;
    }

    private static DateOnly? Resolver(int dia, int mes, int? anio, DateOnly hoy, bool futuro)
    {
        var a = anio ?? hoy.Year;
        if (dia < 1 || dia > DateTime.DaysInMonth(a, mes)) return null;
        var d = new DateOnly(a, mes, dia);
        if (anio is null && futuro && d < hoy) d = d.AddYears(1);
        if (anio is null && !futuro && d > hoy && hoy.DayNumber - d.AddYears(-1).DayNumber < d.DayNumber - hoy.DayNumber) d = d.AddYears(-1);
        return d;
    }

    private static DateOnly Proximo(DateOnly desde, DayOfWeek d, bool incluirHoy)
    {
        var n = ((int)d - (int)desde.DayOfWeek + 7) % 7;
        if (n == 0 && !incluirHoy) n = 7;
        return desde.AddDays(n);
    }

    private static DateOnly Anterior(DateOnly desde, DayOfWeek d) => desde.AddDays(-(((int)desde.DayOfWeek - (int)d + 7) % 7));

    private static DayOfWeek DiaSemana(string d) => d switch
    {
        "lunes" => DayOfWeek.Monday, "martes" => DayOfWeek.Tuesday, "miercoles" => DayOfWeek.Wednesday, "jueves" => DayOfWeek.Thursday,
        "viernes" => DayOfWeek.Friday, "sabado" => DayOfWeek.Saturday, _ => DayOfWeek.Sunday,
    };

    private static readonly string[] NombresMes = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];
    private static int NumeroMes(string m) => m == "setiembre" ? 9 : Array.IndexOf(NombresMes, m) + 1;
    private static bool Palabra(string plano, string w) => Regex.IsMatch(plano, $@"(?<![\w&]){Regex.Escape(w)}(?![\w&])", O);
    private static bool EsPista(string w) => Pistas.Any(x => x.Palabras.Contains(w)) || Categorias.Todas.Values.Any(c => Aplanar(c.Nombre) == w);

    // «1.450,50» · «12,5» · «12.50» · «1.450»
    private static decimal Numero(string s)
    {
        if (s.Contains(',')) s = s.Replace(".", "").Replace(',', '.');
        else if (Regex.IsMatch(s, @"^\d{1,3}(?:\.\d{3})+$")) s = s.Replace(".", "");
        return decimal.Parse(s, CultureInfo.InvariantCulture);
    }

    // La categoría: primero lo que tú sueles poner a ese concepto; si no, por las palabras que salen
    public static string? CategoriaDe(string plano, Contexto ctx)
    {
        var aprendido = ctx.Aprendido.Where(k => k.Key.Length >= 3 && Regex.IsMatch(plano, $@"(?<![\w&]){Regex.Escape(k.Key)}(?![\w&])"))
            .OrderByDescending(k => k.Key.Length).Select(k => k.Value).FirstOrDefault();
        if (aprendido is not null && Categorias.Todas.ContainsKey(aprendido)) return aprendido;
        string? mejor = null;
        var largo = 0;
        foreach (var (patron, cat) in PistasPatrones)
            if (patron.Match(plano) is { Success: true } m && m.Length > largo) { mejor = cat; largo = m.Length; }
        return mejor;
    }

    private static HuchaConocida? HuchaDe(string plano, Contexto ctx)
    {
        HuchaConocida? mejor = null;
        var puntos = 0;
        foreach (var h in ctx.Huchas)
        {
            var p = Aplanar(h.Nombre).Split([' ', ',', '.', '-'], StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 3 && !Bordes.Contains(w)).Count(w => Regex.IsMatch(plano, $@"(?<!\w){Regex.Escape(w)}(?!\w)"));
            if (p > puntos) { mejor = h; puntos = p; }
        }
        return mejor;
    }

    // Lo que queda de la frase, sin verbos de relleno ni artículos sueltos en los bordes, con la primera en mayúscula
    // Por dónde avisar. Si no se dice nada, solo la notificación de la app; «por todas» = todos los canales.
    private readonly record struct Canales(bool Push, bool Correo, bool WhatsApp, bool Telegram)
    {
        public string Texto => (Push, Correo, WhatsApp, Telegram) switch
        {
            (true, true, true, true) => "por todas partes",
            _ => string.Join(", ", new[] { Push ? "la app" : null, Correo ? "correo" : null, WhatsApp ? "WhatsApp" : null, Telegram ? "Telegram" : null }.OfType<string>()),
        };
    }

    private static Canales LeerCanales(Frase f)
    {
        if (f.Coger(ReCanales()) is not { } m) return new(true, false, false, false);
        bool push = false, correo = false, whats = false, tg = false;
        foreach (Capture c in m.Groups["c"].Captures)
        {
            var v = c.Value;
            if (v.StartsWith("tod")) return new(true, true, true, true);
            if (v.Contains("whats") || v.Contains("asap") || v == "wsp") whats = true;
            else if (v.Contains("correo") || v.Contains("mail")) correo = true;
            else if (v.Contains("telegram")) tg = true;
            else push = true;
        }
        return new(push, correo, whats, tg);
    }

    private static string Concepto(string resto)
    {
        var palabras = ReEspacios().Split(resto.Trim(" ,.;:!¡?¿-".ToCharArray())).Select(w => w.Trim(",.;:!¡?¿\"'".ToCharArray())).Where(w => w.Length > 0).ToList();
        palabras.RemoveAll(w => Verbos.Contains(Aplanar(w)));
        // «bizum de [20 €] de Pablo»: al quitar el importe quedan dos «de» seguidos
        for (var i = palabras.Count - 1; i > 0; i--)
            if (Bordes.Contains(Aplanar(palabras[i])) && Aplanar(palabras[i]) == Aplanar(palabras[i - 1])) palabras.RemoveAt(i);
        while (palabras.Count > 0 && Bordes.Contains(Aplanar(palabras[0]))) palabras.RemoveAt(0);
        while (palabras.Count > 0 && Bordes.Contains(Aplanar(palabras[^1]))) palabras.RemoveAt(palabras.Count - 1);
        var t = string.Join(' ', palabras);
        return t.Length == 0 ? t : char.ToUpper(t[0], Es) + t[1..];
    }

    private static string DiaBonito(DateOnly d, DateOnly hoy)
    {
        var n = d.DayNumber - hoy.DayNumber;
        return n switch
        {
            0 => "hoy", -1 => "ayer", -2 => "anteayer", 1 => "mañana", 2 => "pasado mañana",
            _ => d.ToString(d.Year == hoy.Year ? "dddd d 'de' MMMM" : "d 'de' MMMM 'de' yyyy", Es),
        };
    }

    private static string Cada(Frecuencia f) => f switch { Frecuencia.Trimestral => "cada 3 meses", Frecuencia.Anual => "cada año", _ => "cada mes" };

    // La frase con lo que ya se ha entendido tachado, para que lo que sobre sea el concepto o el título
    private sealed class Frase(string original)
    {
        public string Original { get; } = original;
        public string Plano { get; } = Aplanar(original);
        public bool Hoy { get; set; }
        private readonly bool[] usado = new bool[original.Length];

        public Match? Coger(Regex r)
        {
            foreach (Match m in r.Matches(Plano))
            {
                if (m.Length == 0 || Enumerable.Range(m.Index, m.Length).Any(i => usado[i])) continue;
                for (var i = m.Index; i < m.Index + m.Length; i++) usado[i] = true;
                return m;
            }
            return null;
        }

        public void Desmarcar(Match m) { for (var i = m.Index; i < m.Index + m.Length; i++) usado[i] = false; }

        public string Resto()
        {
            var sb = new StringBuilder(Original.Length);
            for (var i = 0; i < Original.Length; i++) sb.Append(usado[i] ? ' ' : Original[i]);
            return sb.ToString();
        }
    }
}
