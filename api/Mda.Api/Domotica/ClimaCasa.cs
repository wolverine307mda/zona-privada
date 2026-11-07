using System.Globalization;
using System.Text;
using Mda.Api.Agenda;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Domotica;

// Un día de un dato numérico (temperatura, humedad…) resumido: las lecturas crudas se guardan 30 días, el resumen 400.
public class ResumenCasaDia
{
    public int EntidadId { get; set; }
    public DateOnly Dia { get; set; }
    public double Min { get; set; }
    public double Media { get; set; }
    public double Max { get; set; }
    public int Muestras { get; set; }
}

public record Muestra(DateTimeOffset Fecha, double Valor);
public record DiaResumen(DateOnly Dia, double Min, double Media, double Max, int Muestras);
public record PuntoClima(DateTimeOffset T, double V);
public record ExtremoClima(DateTimeOffset T, double V);
public record SerieClima(IReadOnlyList<PuntoClima> Puntos, double? Min, double? Media, double? Max, double? Actual, ExtremoClima? MasBajo, ExtremoClima? MasAlto);

// El clima de la casa por estancia: series de temperatura y humedad de los sensores de cada habitación, con mín/media/máx y lo más frío y lo más cálido.
public static class ClimaCasa
{
    public const int DiasResumen = 400, DiasCrudos = 30;
    public const double FrioBajo = 17, HumedadAlta = 65;     // las marcas de «hace frío» y «riesgo de moho»
    public static readonly string[] Datos = ["temperatura", "humedad"];

    // Agrupa muestras por día LOCAL (el cambio de hora no parte ni junta días mal porque se convierte cada muestra con su desfase)
    public static List<DiaResumen> Agregar(IEnumerable<Muestra> muestras, TimeZoneInfo zona) =>
        muestras.GroupBy(m => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(m.Fecha, zona).DateTime))
            .Select(g => new DiaResumen(g.Key, g.Min(m => m.Valor), g.Average(m => m.Valor), g.Max(m => m.Valor), g.Count()))
            .OrderBy(d => d.Dia).ToList();

    // Medias por tramos de tiempo («cubos»): con varios sensores de la misma habitación se promedian todas las lecturas del tramo
    public static List<PuntoClima> Cubos(IEnumerable<Muestra> muestras, TimeSpan cubo)
    {
        var tick = cubo.Ticks;
        return muestras.GroupBy(m => m.Fecha.UtcTicks / tick)
            .OrderBy(g => g.Key)
            .Select(g => new PuntoClima(new DateTimeOffset(g.Key * tick, TimeSpan.Zero), Math.Round(g.Average(m => m.Valor), 2)))
            .ToList();
    }


    public static SerieClima Serie(IReadOnlyList<Muestra> crudas, TimeSpan cubo, double? actual)
    {
        if (crudas.Count == 0) return new SerieClima([], null, null, null, actual, null, null);
        var bajo = crudas.MinBy(m => m.Valor)!; var alto = crudas.MaxBy(m => m.Valor)!;
        return new SerieClima(Cubos(crudas, cubo), Math.Round(bajo.Valor, 1), Math.Round(crudas.Average(m => m.Valor), 1), Math.Round(alto.Valor, 1), actual,
            new ExtremoClima(bajo.Fecha, bajo.Valor), new ExtremoClima(alto.Fecha, alto.Valor));
    }

    // Tramos según lo que se mira: un día con detalle, una semana por horas, un mes cada 4 horas
    public static TimeSpan CuboPara(int dias) => dias <= 2 ? TimeSpan.FromMinutes(15) : dias <= 8 ? TimeSpan.FromHours(1) : TimeSpan.FromHours(4);

    // Guarda el resumen de los días ya cerrados (hasta ayer, hora local) y poda lo de más de 400 días. Se puede repetir sin problema.
    public static async Task Resumir(AppDbContext db, DateTimeOffset ahora, TimeZoneInfo zona, CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahora, zona).DateTime);
        var inicioHoy = new DateTimeOffset(hoy.ToDateTime(TimeOnly.MinValue), zona.GetUtcOffset(hoy.ToDateTime(TimeOnly.MinValue)));
        var lecturas = await db.Lecturas.AsNoTracking().Where(l => l.Fecha < inicioHoy).Select(l => new { l.EntidadId, l.Fecha, l.Valor }).ToListAsync(ct);
        foreach (var grupo in lecturas.GroupBy(l => l.EntidadId))
        {
            var dias = Agregar(grupo.Select(l => new Muestra(l.Fecha, l.Valor)), zona);
            var existentes = await db.ResumenesCasaDia.Where(r => r.EntidadId == grupo.Key).ToDictionaryAsync(r => r.Dia, ct);
            foreach (var d in dias)
            {
                if (existentes.TryGetValue(d.Dia, out var fila))
                {
                    // un día a medias que se había resumido antes de tener todas sus lecturas (o tras recortar) no se pisa con menos datos
                    if (fila.Muestras >= d.Muestras) continue;
                    fila.Min = d.Min; fila.Media = d.Media; fila.Max = d.Max; fila.Muestras = d.Muestras;
                }
                else db.ResumenesCasaDia.Add(new ResumenCasaDia { EntidadId = grupo.Key, Dia = d.Dia, Min = d.Min, Media = d.Media, Max = d.Max, Muestras = d.Muestras });
            }
        }
        var limite = hoy.AddDays(-DiasResumen);
        db.ResumenesCasaDia.RemoveRange(await db.ResumenesCasaDia.Where(r => r.Dia < limite).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }

    public static string Csv(IEnumerable<(string Estancia, string Sensor, string Dato, DateTimeOffset T, double V)> filas, TimeZoneInfo zona)
    {
        var sb = new StringBuilder("estancia;sensor;dato;fecha;valor\n");
        static string C(string s) => s.Contains(';') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        foreach (var f in filas)
            sb.Append(C(f.Estancia)).Append(';').Append(C(f.Sensor)).Append(';').Append(f.Dato).Append(';')
              .Append(TimeZoneInfo.ConvertTime(f.T, zona).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(';')
              .Append(f.V.ToString("0.##", CultureInfo.GetCultureInfo("es-ES"))).Append('\n');
        return sb.ToString();
    }

    public static void MapClima(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/domotica/clima").RequireAuthorization();

        g.MapGet("/", async (int? dias, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var d = Math.Clamp(dias ?? 1, 1, DiasResumen);
            var datos = await Cargar(db, d, reloj, zona, ct);
            return Results.Ok(new
            {
                dias = d, frioBajo = FrioBajo, humedadAlta = HumedadAlta,
                estancias = datos.Select(e => new
                {
                    e.Id, e.Nombre, e.Icono,
                    sensores = e.Sensores.Select(s => new { s.Id, s.Nombre }),
                    temperatura = e.Series.GetValueOrDefault("temperatura"),
                    humedad = e.Series.GetValueOrDefault("humedad"),
                    // las marcas del plan: frío por debajo de 17 ° y humedad por encima del 65 %
                    hizoFrio = e.Series.GetValueOrDefault("temperatura") is { Min: { } m } && m < FrioBajo,
                    riesgoMoho = e.Series.GetValueOrDefault("humedad") is { Max: { } x } && x > HumedadAlta,
                }),
            });
        });

        g.MapGet("/csv", async (int? dias, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            var d = Math.Clamp(dias ?? 7, 1, DiasResumen);
            var datos = await Cargar(db, d, reloj, zona, ct);
            var filas = datos.SelectMany(e => e.Series.SelectMany(s => s.Value.Puntos.Select(p => (e.Nombre, string.Join(" + ", e.Sensores.Select(x => x.Nombre)), s.Key, p.T, p.V))));
            return Results.Text(Csv(filas, zona.Info), "text/csv; charset=utf-8");
        });
    }

    private record EstanciaClima(int? Id, string Nombre, string Icono, List<Aparato> Sensores, Dictionary<string, SerieClima> Series);

    private static async Task<List<EstanciaClima>> Cargar(AppDbContext db, int dias, TimeProvider reloj, Zona zona, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var estancias = await db.Estancias.AsNoTracking().OrderBy(e => e.Orden).ThenBy(e => e.Id).ToListAsync(ct);
        var aparatos = await db.Aparatos.AsNoTracking().ToListAsync(ct);
        var entidades = await db.Entidades.AsNoTracking().Where(e => Datos.Contains(e.Clave)).ToListAsync(ct);
        var porEntidad = entidades.ToDictionary(e => e.Id);
        var desde = ahora.AddDays(-dias);
        var cubo = CuboPara(dias);
        var usarCrudas = dias <= DiasCrudos;

        // las muestras de todos los sensores que importan, una sola consulta
        var ids = entidades.Select(e => e.Id).ToList();
        var muestras = new Dictionary<int, List<Muestra>>();
        if (usarCrudas)
        {
            foreach (var l in await db.Lecturas.AsNoTracking().Where(l => ids.Contains(l.EntidadId) && l.Fecha >= desde).OrderBy(l => l.Fecha).Select(l => new { l.EntidadId, l.Fecha, l.Valor }).ToListAsync(ct))
                (muestras.TryGetValue(l.EntidadId, out var m) ? m : muestras[l.EntidadId] = []).Add(new Muestra(l.Fecha, l.Valor));
        }
        else
        {
            // más de un mes: se usa el resumen diario (la media a las 12:00 locales de cada día; mín y máx se conservan como extremos)
            var desdeDia = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(desde, zona.Info).DateTime);
            foreach (var r in await db.ResumenesCasaDia.AsNoTracking().Where(r => ids.Contains(r.EntidadId) && r.Dia >= desdeDia).OrderBy(r => r.Dia).ToListAsync(ct))
            {
                var medio = r.Dia.ToDateTime(new TimeOnly(12, 0));
                var f = new DateTimeOffset(medio, zona.Info.GetUtcOffset(medio));
                var l = muestras.TryGetValue(r.EntidadId, out var m) ? m : muestras[r.EntidadId] = [];
                l.Add(new Muestra(f.AddHours(-1), r.Min)); l.Add(new Muestra(f, r.Media)); l.Add(new Muestra(f.AddHours(1), r.Max));
            }
            cubo = TimeSpan.FromHours(24);
        }

        var salida = new List<EstanciaClima>();
        var grupos = estancias.Select(e => (e.Id as int?, e.Nombre, e.Icono)).Append((null, "Sin asignar", "📍"));
        foreach (var (eid, nombre, icono) in grupos)
        {
            var delGrupo = aparatos.Where(a => a.EstanciaId == eid).ToList();
            var series = new Dictionary<string, SerieClima>();
            var conDatos = new List<Aparato>();
            foreach (var dato in Datos)
            {
                var ents = entidades.Where(e => e.Clave == dato && delGrupo.Any(a => a.Id == e.AparatoId)).ToList();
                if (ents.Count == 0) continue;
                var todas = ents.SelectMany(e => muestras.GetValueOrDefault(e.Id) ?? []).OrderBy(m => m.Fecha).ToList();
                double? actual = ents.Select(e => EvaluadorReglas.Numero(e.Valor)).Where(v => v is not null).Select(v => v!.Value).DefaultIfEmpty().Average() is var media && ents.Any(e => EvaluadorReglas.Numero(e.Valor) is not null) ? Math.Round(media, 1) : null;
                series[dato] = Serie(todas, cubo, actual);
                foreach (var a in delGrupo.Where(a => ents.Any(e => e.AparatoId == a.Id)).Where(a => !conDatos.Contains(a))) conDatos.Add(a);
            }
            if (series.Count > 0) salida.Add(new EstanciaClima(eid, nombre, icono, conDatos, series));
        }
        return salida;
    }
}
