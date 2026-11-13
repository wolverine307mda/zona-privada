using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Mda.Api.Agenda;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Personas;

// Personas: cumpleaños, datos de contacto, gustos e ideas de regalo. Los datos de otras personas (teléfonos, correos, dirección, notas) se guardan cifrados.
// Nada se importa sin que lo marques: se puede traer un archivo de contactos (.vcf) eligiendo cuáles y qué datos de cada uno.
public class Persona
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Apodo { get; set; } = "";
    public string Relacion { get; set; } = "";
    public string Telefonos { get; set; } = "";       // separados por «;»
    public string Correos { get; set; } = "";
    public string Direccion { get; set; } = "";
    public string Notas { get; set; } = "";
    public int? CumpleDia { get; set; }
    public int? CumpleMes { get; set; }
    public int? CumpleAnio { get; set; }
    public string Gustos { get; set; } = "";          // etiquetas separadas por coma: «libros, café, montaña»
    public bool Avisar { get; set; } = true;
    public DateOnly? UltimaVez { get; set; }          // la última vez que hablasteis
    public int AvisadoAnio { get; set; }              // hasta qué aviso del cumple se ha llegado (año y nivel) para no repetir
    public int AvisadoNivel { get; set; }
}

public class RegaloPersona
{
    public int Id { get; set; }
    public int PersonaId { get; set; }
    public string Idea { get; set; } = "";
    public decimal? Precio { get; set; }
    public string Ocasion { get; set; } = "";
    public DateOnly? Fecha { get; set; }
    public string Estado { get; set; } = "idea";      // idea · comprado · regalado
    public string Sento { get; set; } = "";           // cómo sentó
}

public record PersonaEntrada(string? Nombre, string? Apodo, string? Relacion, string? Telefonos, string? Correos, string? Direccion, string? Notas, int? CumpleDia, int? CumpleMes, int? CumpleAnio,
    string? Gustos, bool? Avisar, string? UltimaVez, bool QuitarCumple = false);
public record RegaloEntrada(string? Idea, decimal? Precio, string? Ocasion, string? Fecha, string? Estado, string? Sento);
public record ContactoVcf(string Nombre, List<string> Telefonos, List<string> Correos, string Direccion, int? Dia, int? Mes, int? Anio, string Notas);
public record SeleccionImportar(int Indice, bool Traer);
public record ImportarEntrada(string? Vcf, List<int>? Indices, string? Traer);
public record PersonaAtajo(string? Nombre, int? Dia, int? Mes, int? Anio, string? Telefono, string? Correo);

// ---- vCard 3.0 / 4.0 ----
public static class VCard
{
    // Pura: líneas plegadas (las que siguen empiezan por espacio o tabulador), QUOTED-PRINTABLE, escapes y varios teléfonos; una tarjeta mala no tumba el resto
    public static List<ContactoVcf> Parsear(string? texto)
    {
        var l = new List<ContactoVcf>();
        if (string.IsNullOrWhiteSpace(texto)) return l;
        var lineas = Desplegar(texto);
        string? fn = null, n = null, adr = "", nota = ""; var tel = new List<string>(); var mail = new List<string>(); int? d = null, m = null, a = null; var dentro = false;
        foreach (var bruta in lineas)
        {
            var linea = bruta.TrimEnd('\r');
            if (linea.Equals("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase)) { dentro = true; fn = n = null; adr = nota = ""; tel = []; mail = []; d = m = a = null; continue; }
            if (linea.Equals("END:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                if (dentro)
                {
                    var nombre = (fn ?? n ?? "").Trim();
                    if (nombre.Length > 0) l.Add(new ContactoVcf(nombre, tel.Distinct().ToList(), mail.Distinct().ToList(), adr, d, m, a, nota));
                }
                dentro = false; continue;
            }
            if (!dentro) continue;
            var dos = linea.IndexOf(':');
            if (dos <= 0) continue;
            var clave = linea[..dos]; var valor = linea[(dos + 1)..];
            var partes = clave.Split(';'); var nombreProp = partes[0].Split('.').Last().ToUpperInvariant();       // «item1.TEL» → TEL
            var qp = partes.Skip(1).Any(p => p.Equals("ENCODING=QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase) || p.Equals("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase));
            if (qp) valor = DecodificarQp(valor);
            switch (nombreProp)
            {
                case "FN": fn = Desescapar(valor); break;
                case "N": { var p = valor.Split(';'); n = string.Join(' ', new[] { Get(p, 3), Get(p, 1), Get(p, 2), Get(p, 0) }.Where(x => x.Length > 0)).Trim(); break; }
                case "TEL": if (valor.Trim().Length > 0) tel.Add(valor.Trim()); break;
                case "EMAIL": if (valor.Contains('@')) mail.Add(valor.Trim()); break;
                case "ADR": adr = string.Join(", ", valor.Split(';').Select(x => Desescapar(x).Trim()).Where(x => x.Length > 0)); break;
                case "NOTE": nota = Desescapar(valor); break;
                case "BDAY": if (Cumple(valor) is { } c) (d, m, a) = c; break;
            }
        }
        return l;
    }

    private static string Get(string[] p, int i) => i < p.Length ? Desescapar(p[i]).Trim() : "";

    private static List<string> Desplegar(string t)
    {
        var l = new List<string>();
        var qpAbierto = false;
        foreach (var linea in t.Replace("\r\n", "\n").Split('\n'))
        {
            if (l.Count > 0 && (linea.StartsWith(' ') || linea.StartsWith('\t'))) { l[^1] += linea[1..]; continue; }
            // QUOTED-PRINTABLE: «=» al final de la línea la une con la siguiente
            if (qpAbierto && l.Count > 0) { l[^1] = l[^1][..^1] + linea; qpAbierto = l[^1].EndsWith('='); continue; }
            l.Add(linea);
            qpAbierto = linea.EndsWith('=') && linea.Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase);
        }
        return l;
    }

    private static string DecodificarQp(string v)
    {
        var bytes = new List<byte>();
        for (var i = 0; i < v.Length; i++)
        {
            if (v[i] == '=' && i + 2 < v.Length + 0 && Uri.IsHexDigit(v[i + 1]) && Uri.IsHexDigit(v[i + 2])) { bytes.Add(Convert.ToByte(v.Substring(i + 1, 2), 16)); i += 2; }
            else bytes.AddRange(Encoding.UTF8.GetBytes(v[i].ToString()));
        }
        return Encoding.UTF8.GetString([.. bytes]);
    }

    private static string Desescapar(string v) => v.Replace("\\n", "\n").Replace("\\N", "\n").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");

    // «19900315», «1990-03-15», «--0315», «--03-15»; el año 1604 (que Apple pone cuando no hay año) y los años absurdos se ignoran
    public static (int? Dia, int? Mes, int? Anio)? Cumple(string v)
    {
        var s = v.Trim();
        var mt = Regex.Match(s, @"^(?:(\d{4})-?|--)(\d{2})-?(\d{2})(?:T.*)?$");
        if (!mt.Success) return null;
        var mes = int.Parse(mt.Groups[2].Value, CultureInfo.InvariantCulture); var dia = int.Parse(mt.Groups[3].Value, CultureInfo.InvariantCulture);
        if (mes is < 1 or > 12 || dia < 1 || dia > DateTime.DaysInMonth(2024, mes)) return null;       // 2024 es bisiesto: el 29 de febrero vale
        int? anio = mt.Groups[1].Success ? int.Parse(mt.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        if (anio is < 1900 or > 2100) anio = null;
        return (dia, mes, anio);
    }
}

// ---- cumpleaños ----
public static class Cumples
{
    // 29 de febrero en un año que no es bisiesto se celebra el 28
    public static DateOnly EnAnio(int dia, int mes, int anio) => new(anio, mes, Math.Min(dia, DateTime.DaysInMonth(anio, mes)));

    public static DateOnly Proximo(int dia, int mes, DateOnly hoy)
    {
        var este = EnAnio(dia, mes, hoy.Year);
        return este >= hoy ? este : EnAnio(dia, mes, hoy.Year + 1);
    }

    public static int Dias(int dia, int mes, DateOnly hoy) => Proximo(dia, mes, hoy).DayNumber - hoy.DayNumber;
    public static int? Edad(int? anioNacimiento, DateOnly cumple) => anioNacimiento is { } a ? cumple.Year - a : null;

    // 0 = todavía no toca avisar; 1 = quedan 14 días o menos, 2 = 7 o menos, 3 = mañana, 4 = hoy
    public static int Nivel(int dias) => dias == 0 ? 4 : dias <= 1 ? 3 : dias <= 7 ? 2 : dias <= 14 ? 1 : 0;

    public static string Cuando(int dias) => dias == 0 ? "es HOY" : dias == 1 ? "es mañana" : $"es en {dias} días";
}

// ---- duplicados al importar ----
public static class Duplicados
{
    private static string Digitos(string t) { var d = new string(t.Where(char.IsDigit).ToArray()); return d.Length > 9 ? d[^9..] : d; }
    private static string Nombre(string t) => string.Concat(t.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant().Trim();

    // ¿Ya existe esa persona? Mismo teléfono (por sus últimos 9 dígitos), mismo correo o mismo nombre sin tildes ni mayúsculas.
    public static Persona? Buscar(ContactoVcf c, IEnumerable<Persona> existentes)
    {
        var tel = c.Telefonos.Select(Digitos).Where(x => x.Length >= 6).ToHashSet();
        var mail = c.Correos.Select(x => x.ToLowerInvariant().Trim()).ToHashSet();
        var nombre = Nombre(c.Nombre);
        foreach (var p in existentes)
        {
            if (Nombre(p.Nombre) == nombre) return p;
            if (p.Telefonos.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(Digitos).Any(tel.Contains)) return p;
            if (p.Correos.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => x.ToLowerInvariant().Trim()).Any(mail.Contains)) return p;
        }
        return null;
    }
}

// ---- ideas de regalo, por reglas (sin IA): gustos × presupuesto × lo que ya se regaló ----
public record IdeaRegalo(string Idea, string[] Etiquetas, decimal Precio);
public static class Regalos
{
    public static readonly IdeaRegalo[] Catalogo =
    [
        new("Un libro de su autor o género favorito", ["libros", "lectura", "novela"], 18), new("Una suscripción a una revista", ["libros", "lectura", "ciencia"], 40),
        new("Un set de café de especialidad", ["café", "cocina"], 25), new("Una cafetera de émbolo o moka", ["café"], 30), new("Una caja de tés", ["té", "cocina"], 20),
        new("Una cena en un restaurante que quiera probar", ["comida", "cocina", "restaurantes"], 60), new("Una clase de cocina", ["cocina", "comida"], 45),
        new("Unas botas o calcetines de montaña", ["montaña", "senderismo", "deporte"], 40), new("Una mochila ligera de día", ["montaña", "viajes", "senderismo"], 55),
        new("Una entrada para un concierto", ["música", "conciertos"], 50), new("Un vinilo", ["música", "vinilos"], 28), new("Unos auriculares", ["música", "tecnología"], 80),
        new("Un juego de mesa", ["juegos", "amigos", "familia"], 30), new("Un videojuego", ["videojuegos", "juegos", "tecnología"], 50),
        new("Una planta de interior con su maceta", ["plantas", "casa", "jardín"], 22), new("Un kit para cultivar hierbas aromáticas", ["plantas", "cocina", "jardín"], 18),
        new("Una manta suave", ["casa", "hogar", "descanso"], 35), new("Una vela aromática", ["casa", "hogar", "relax"], 15),
        new("Un cuaderno de calidad y un buen bolígrafo", ["escribir", "dibujo", "arte", "estudio"], 24), new("Una caja de acuarelas o rotuladores", ["dibujo", "arte", "manualidades"], 30),
        new("Una banda elástica y una esterilla", ["deporte", "yoga", "gimnasio"], 28), new("Una botella térmica", ["deporte", "viajes", "montaña", "café"], 25),
        new("Una escapada de un fin de semana", ["viajes", "pareja", "relax"], 150), new("Una guía de viaje de su próximo destino", ["viajes", "libros"], 20),
        new("Una entrada de cine o teatro", ["cine", "teatro", "cultura"], 24), new("Una película o serie en edición especial", ["cine", "series"], 20),
        new("Un álbum de fotos con vuestros recuerdos", ["familia", "amigos", "pareja", "recuerdos"], 25), new("Una taza personalizada", ["café", "té", "familia"], 12),
        new("Un detalle hecho a mano", ["manualidades", "familia", "amigos"], 10), new("Un juguete educativo", ["niños", "juegos"], 25),
    ];

    // Pura: hasta `cuantas` ideas, de más a menos encajada con sus gustos. Cada etiqueta coincidente suma; si hay presupuesto, solo ideas que quepan.
    // Una idea que ya se le regaló en los últimos 3 años no se repite (se compara por el texto de la idea, sin mayúsculas).
    public static List<IdeaRegalo> Ideas(string gustos, decimal? presupuesto, IEnumerable<(string Idea, DateOnly? Fecha)> regalados, DateOnly hoy, int cuantas = 5)
    {
        var tags = gustos.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(x => x.ToLowerInvariant()).ToHashSet();
        var recientes = regalados.Where(r => r.Fecha is null || r.Fecha >= hoy.AddYears(-3)).Select(r => r.Idea.Trim().ToLowerInvariant()).ToHashSet();
        return Catalogo.Where(i => !recientes.Contains(i.Idea.ToLowerInvariant()) && (presupuesto is null || i.Precio <= presupuesto))
            .Select(i => (i, puntos: i.Etiquetas.Count(tags.Contains)))
            .Where(x => tags.Count == 0 || x.puntos > 0)
            .OrderByDescending(x => x.puntos).ThenBy(x => x.i.Precio).ThenBy(x => x.i.Idea, StringComparer.CurrentCultureIgnoreCase)
            .Take(cuantas).Select(x => x.i).ToList();
    }
}

public static class PersonasServicio
{
    public const int Maximo = 500;

    public static object Dto(Persona p, DateOnly hoy)
    {
        DateOnly? prox = p.CumpleDia is { } d && p.CumpleMes is { } m ? Cumples.Proximo(d, m, hoy) : null;
        return new
        {
            p.Id, p.Nombre, p.Apodo, p.Relacion, telefonos = Lista(p.Telefonos), correos = Lista(p.Correos), p.Direccion, p.Notas, p.CumpleDia, p.CumpleMes, p.CumpleAnio, gustos = Lista(p.Gustos, ','), p.Avisar,
            ultimaVez = p.UltimaVez?.ToString("yyyy-MM-dd"), proximo = prox?.ToString("yyyy-MM-dd"), dias = prox is { } x ? x.DayNumber - hoy.DayNumber : (int?)null,
            edad = prox is { } y ? Cumples.Edad(p.CumpleAnio, y) : null,
        };
    }

    private static string[] Lista(string v, char sep = ';') => v.Split(sep, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    // «a; b\nc» → «a;b;c» (teléfonos y correos) o «a, b» → «a,b» (gustos): sin vacíos ni repetidos
    private static string Une(string? v, char sep = ';') => string.Join(sep, (v ?? "").Split(sep == ';' ? new[] { ';', '\n' } : new[] { ',', ';' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase));

    public static Dictionary<string, string[]> Validar(PersonaEntrada e, bool nueva)
    {
        var mal = new Dictionary<string, string[]>();
        if (nueva || e.Nombre is not null) { if (string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Trim().Length > 80) mal["nombre"] = ["Ponle un nombre (máximo 80 letras)."]; }
        if ((e.CumpleDia is not null) != (e.CumpleMes is not null) && !e.QuitarCumple && (nueva || (e.CumpleDia is not null && e.CumpleMes is null)))
            mal["cumple"] = ["El cumpleaños necesita día y mes."];
        if (e.CumpleMes is { } m && (m is < 1 or > 12)) mal["cumpleMes"] = ["El mes va de 1 a 12."];
        else if (e.CumpleDia is { } d && (d < 1 || d > DateTime.DaysInMonth(2024, e.CumpleMes ?? 1))) mal["cumpleDia"] = ["Ese día no existe en ese mes."];
        if (e.CumpleAnio is { } a && (a < 1900 || a > DateTime.UtcNow.Year)) mal["cumpleAnio"] = ["Un año razonable, o déjalo en blanco."];
        if (e.UltimaVez is { Length: > 0 } u && !DateOnly.TryParseExact(u, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) mal["ultimaVez"] = ["Fecha no válida."];
        if (e.Notas is { Length: > 1000 }) mal["notas"] = ["Las notas llegan a 1000 letras."];
        return mal;
    }

    public static void Copiar(PersonaEntrada e, Persona p)
    {
        if (e.Nombre is not null) p.Nombre = e.Nombre.Trim();
        if (e.Apodo is not null) p.Apodo = e.Apodo.Trim();
        if (e.Relacion is not null) p.Relacion = e.Relacion.Trim();
        if (e.Telefonos is not null) p.Telefonos = Une(e.Telefonos);
        if (e.Correos is not null) p.Correos = Une(e.Correos);
        if (e.Direccion is not null) p.Direccion = e.Direccion.Trim();
        if (e.Notas is not null) p.Notas = e.Notas.Trim();
        if (e.QuitarCumple) { p.CumpleDia = p.CumpleMes = p.CumpleAnio = null; }
        else { if (e.CumpleDia is { } d) p.CumpleDia = d; if (e.CumpleMes is { } m) p.CumpleMes = m; if (e.CumpleAnio is { } a) p.CumpleAnio = a; }
        if (e.Gustos is not null) p.Gustos = Une(e.Gustos, ',').ToLowerInvariant();
        if (e.Avisar is { } av) p.Avisar = av;
        if (e.UltimaVez is not null) p.UltimaVez = e.UltimaVez.Length == 0 ? null : DateOnly.ParseExact(e.UltimaVez, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (e.CumpleDia is not null || e.CumpleMes is not null || e.QuitarCumple) { p.AvisadoAnio = 0; p.AvisadoNivel = 0; }
    }

    // Una vez al día en la ronda del vigilante: avisa a 14, 7 y 1 días y el mismo día, sin repetir cada nivel
    public static async Task AvisarCumples(AppDbContext db, IAvisos avisos, DateOnly hoy, ILogger log, CancellationToken ct)
    {
        var lista = await db.Personas.Where(p => p.Avisar && p.CumpleDia != null && p.CumpleMes != null).ToListAsync(ct);
        var textos = new List<string>();
        foreach (var p in lista)
        {
            var prox = Cumples.Proximo(p.CumpleDia!.Value, p.CumpleMes!.Value, hoy);
            var dias = prox.DayNumber - hoy.DayNumber;
            var nivel = Cumples.Nivel(dias);
            if (nivel == 0) continue;
            if (p.AvisadoAnio == prox.Year && p.AvisadoNivel >= nivel) continue;
            p.AvisadoAnio = prox.Year; p.AvisadoNivel = nivel;
            var edad = Cumples.Edad(p.CumpleAnio, prox);
            var nombre = p.Apodo.Length > 0 ? p.Apodo : p.Nombre;
            textos.Add($"🎂 El cumpleaños de {nombre} {Cumples.Cuando(dias)} ({prox:dd/MM}){(edad is { } e ? $": cumple {e}" : "")}.");
        }
        if (textos.Count == 0) return;
        await db.SaveChangesAsync(ct);
        foreach (var t in textos)
        {
            try { await avisos.Avisar("personas", "🎂 Cumpleaños", t, textoTelegram: t, ct: ct); }
            catch (Exception e) { log.LogWarning(e, "No se pudo avisar de un cumpleaños"); }
        }
    }

    public static async Task<string> TextoCumples(AppDbContext db, DateOnly hoy, int cuantos, CancellationToken ct)
    {
        var l = (await db.Personas.AsNoTracking().Where(p => p.CumpleDia != null && p.CumpleMes != null).ToListAsync(ct))
            .Select(p => (p, dias: Cumples.Dias(p.CumpleDia!.Value, p.CumpleMes!.Value, hoy))).OrderBy(x => x.dias).Take(cuantos).ToList();
        if (l.Count == 0) return "No tienes cumpleaños apuntados todavía.";
        return "🎂 Próximos cumpleaños:\n" + string.Join("\n", l.Select(x => $"• {(x.p.Apodo.Length > 0 ? x.p.Apodo : x.p.Nombre)}: {Cumples.Proximo(x.p.CumpleDia!.Value, x.p.CumpleMes!.Value, hoy):dd/MM} ({(x.dias == 0 ? "hoy" : x.dias == 1 ? "mañana" : $"en {x.dias} días")})"));
    }
}

public static class PersonasEndpoints
{
    public static void MapPersonas(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/personas").RequireAuthorization();
        static DateOnly Hoy(TimeProvider r, Zona z) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.GetUtcNow(), z.Info).Date);

        g.MapGet("/", async (AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var hoy = Hoy(reloj, zona);
            var lista = await db.Personas.AsNoTracking().ToListAsync(ct);
            return Results.Ok(lista.OrderBy(p => p.CumpleDia is null ? int.MaxValue : Cumples.Dias(p.CumpleDia.Value, p.CumpleMes!.Value, hoy)).ThenBy(p => p.Nombre).Select(p => PersonasServicio.Dto(p, hoy)));
        });

        g.MapGet("/proximos", async (int? dias, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var hoy = Hoy(reloj, zona); var tope = Math.Clamp(dias ?? 30, 1, 366);
            var l = (await db.Personas.AsNoTracking().Where(p => p.CumpleDia != null && p.CumpleMes != null).ToListAsync(ct))
                .Select(p => (p, dias: Cumples.Dias(p.CumpleDia!.Value, p.CumpleMes!.Value, hoy))).Where(x => x.dias <= tope).OrderBy(x => x.dias)
                .Select(x => PersonasServicio.Dto(x.p, hoy)).ToList();
            return Results.Ok(l);
        });

        g.MapPost("/", async (PersonaEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (PersonasServicio.Validar(e, true) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            if (await db.Personas.CountAsync(ct) >= PersonasServicio.Maximo) return Results.ValidationProblem(new Dictionary<string, string[]> { ["nombre"] = [$"Como mucho {PersonasServicio.Maximo} personas."] });
            var p = new Persona(); PersonasServicio.Copiar(e, p);
            db.Personas.Add(p);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/personas/{p.Id}", PersonasServicio.Dto(p, Hoy(reloj, zona)));
        });

        g.MapPut("/{id:int}", async (int id, PersonaEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (PersonasServicio.Validar(e, false) is { Count: > 0 } mal) return Results.ValidationProblem(mal);
            var p = await db.Personas.FindAsync([id], ct);
            if (p is null) return Results.NotFound();
            PersonasServicio.Copiar(e, p);
            await db.SaveChangesAsync(ct);
            return Results.Ok(PersonasServicio.Dto(p, Hoy(reloj, zona)));
        });

        // borrar a una persona borra también sus regalos (y todo lo que se sabía de ella)
        g.MapDelete("/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var p = await db.Personas.FindAsync([id], ct);
            if (p is null) return Results.NotFound();
            db.Regalos.RemoveRange(await db.Regalos.Where(r => r.PersonaId == id).ToListAsync(ct));
            db.Personas.Remove(p);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ---- regalos ----
        g.MapGet("/{id:int}/regalos", async (int id, AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Regalos.AsNoTracking().Where(r => r.PersonaId == id).OrderByDescending(r => r.Id).ToListAsync(ct)));

        g.MapPost("/{id:int}/regalos", async (int id, RegaloEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Personas.AnyAsync(p => p.Id == id, ct)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(e.Idea) || e.Idea.Trim().Length > 120) return Results.ValidationProblem(new Dictionary<string, string[]> { ["idea"] = ["Ponle una idea (máximo 120 letras)."] });
            if (e.Estado is not null && e.Estado is not ("idea" or "comprado" or "regalado")) return Results.ValidationProblem(new Dictionary<string, string[]> { ["estado"] = ["idea, comprado o regalado."] });
            var r = new RegaloPersona { PersonaId = id, Idea = e.Idea.Trim(), Precio = e.Precio, Ocasion = (e.Ocasion ?? "").Trim(), Estado = e.Estado ?? "idea", Sento = (e.Sento ?? "").Trim(),
                Fecha = DateOnly.TryParseExact(e.Fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null };
            db.Regalos.Add(r);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/personas/{id}/regalos/{r.Id}", r);
        });

        g.MapPut("/regalos/{rid:int}", async (int rid, RegaloEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            var r = await db.Regalos.FindAsync([rid], ct);
            if (r is null) return Results.NotFound();
            if (e.Estado is not null && e.Estado is not ("idea" or "comprado" or "regalado")) return Results.ValidationProblem(new Dictionary<string, string[]> { ["estado"] = ["idea, comprado o regalado."] });
            if (e.Idea is { Length: > 0 }) r.Idea = e.Idea.Trim();
            if (e.Precio is not null) r.Precio = e.Precio;
            if (e.Ocasion is not null) r.Ocasion = e.Ocasion.Trim();
            if (e.Estado is not null) r.Estado = e.Estado;
            if (e.Sento is not null) r.Sento = e.Sento.Trim();
            if (e.Fecha is not null) r.Fecha = DateOnly.TryParseExact(e.Fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;
            await db.SaveChangesAsync(ct);
            return Results.Ok(r);
        });

        g.MapDelete("/regalos/{rid:int}", async (int rid, AppDbContext db, CancellationToken ct) =>
        {
            var r = await db.Regalos.FindAsync([rid], ct);
            if (r is null) return Results.NotFound();
            db.Regalos.Remove(r);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapGet("/{id:int}/ideas", async (int id, decimal? presupuesto, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var p = await db.Personas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return Results.NotFound();
            var hechos = (await db.Regalos.AsNoTracking().Where(r => r.PersonaId == id && r.Estado != "idea").ToListAsync(ct)).Select(r => (r.Idea, r.Fecha));
            return Results.Ok(Regalos.Ideas(p.Gustos, presupuesto, hechos, Hoy(reloj, zona)));
        });

        // ---- importar contactos de un archivo .vcf: primero la vista previa, después lo que marques ----
        g.MapPost("/importar/vista-previa", async (ImportarEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            var contactos = VCard.Parsear(e.Vcf);
            var existentes = await db.Personas.AsNoTracking().ToListAsync(ct);
            return Results.Ok(contactos.Select((c, i) => new
            {
                indice = i, c.Nombre, c.Telefonos, c.Correos, c.Direccion, cumple = c.Dia is { } d && c.Mes is { } m ? $"{d:00}/{m:00}{(c.Anio is { } a ? "/" + a : "")}" : null, c.Notas,
                duplicadoDe = Duplicados.Buscar(c, existentes)?.Nombre,
            }));
        });

        g.MapPost("/importar", async (ImportarEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            var contactos = VCard.Parsear(e.Vcf);
            var elegidos = (e.Indices ?? []).Distinct().Where(i => i >= 0 && i < contactos.Count).ToList();
            if (elegidos.Count == 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["indices"] = ["Marca al menos un contacto."] });
            var completo = e.Traer == "completo";
            var existentes = await db.Personas.ToListAsync(ct);
            if (existentes.Count + elegidos.Count > PersonasServicio.Maximo) return Results.ValidationProblem(new Dictionary<string, string[]> { ["indices"] = [$"Como mucho {PersonasServicio.Maximo} personas."] });
            int nuevas = 0, saltadas = 0;
            foreach (var i in elegidos)
            {
                var c = contactos[i];
                if (Duplicados.Buscar(c, existentes) is not null) { saltadas++; continue; }
                var p = new Persona { Nombre = c.Nombre[..Math.Min(c.Nombre.Length, 80)], CumpleDia = c.Dia, CumpleMes = c.Mes, CumpleAnio = c.Anio };
                if (completo) { p.Telefonos = string.Join(';', c.Telefonos); p.Correos = string.Join(';', c.Correos); p.Direccion = c.Direccion; p.Notas = c.Notas.Length > 1000 ? c.Notas[..1000] : c.Notas; }
                db.Personas.Add(p); existentes.Add(p); nuevas++;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { nuevas, saltadas });
        });
    }
}
