using System.Globalization;

namespace Mda.Api.Bienestar;

// Bienestar: una ayuda personal para moverte más y registrar tu progreso. NO es consejo médico: ante dolor, mareos, enfermedad, lesión o embarazo, consulta a un profesional.
// Todo es determinista (reglas y plantillas, sin IA): el mismo perfil da siempre el mismo plan.
public record PerfilDatos(string Objetivo, string Nivel, int DiasSemana, string Equipo, string Limitaciones, int Minutos, int? AlturaCm, int? AnioNacimiento);
public record EjercicioPlan(string Id, string Nombre, int Series, string Reps, int DescansoSeg);
public record SesionPlan(int Semana, int DiaSemana, string Tipo, string Titulo, int Minutos, List<EjercicioPlan> Ejercicios, bool Descarga);
public record PlanGenerado(string Plantilla, string Resumen, int Semanas, List<SesionPlan> Sesiones, List<string> Avisos);
public record EjercicioInfo(string Id, string Nombre, string Musculos, string[] Pasos, string Errores, string Facil, string Dificil);

public static class Objetivos
{
    public static readonly string[] Validos = ["grasa", "fuerza", "musculo", "resistencia", "movilidad", "salud"];
    public static readonly string[] Niveles = ["principiante", "intermedio", "avanzado"];
    public static readonly string[] Equipos = ["gimnasio", "casa", "nada"];
}

public static class Ejercicios
{
    private static EjercicioInfo E(string id, string n, string m, string[] pasos, string err, string facil, string dificil) => new(id, n, m, pasos, err, facil, dificil);
    public static readonly EjercicioInfo[] Todos =
    [
        E("sentadilla", "Sentadilla", "piernas, glúteos", ["Pies a la anchura de los hombros.", "Baja con la espalda recta como si te sentaras.", "Sube empujando el suelo."], "Rodillas hacia dentro; levantar los talones.", "Sentadilla a una silla", "Sentadilla con peso o búlgara"),
        E("prensa", "Prensa de piernas", "piernas, glúteos", ["Siéntate con la espalda pegada.", "Baja controlado hasta unos 90° de rodilla.", "Empuja sin bloquear las rodillas."], "Despegar la zona lumbar; bloquear las rodillas.", "Menos peso o rango corto", "Una pierna cada vez"),
        E("zancada", "Zancadas", "piernas, glúteos", ["Da un paso largo.", "Baja la rodilla de atrás hacia el suelo.", "Vuelve empujando con el pie de delante."], "Rodilla de delante pasando de la punta del pie.", "Zancada con apoyo en una pared", "Zancada con peso"),
        E("peso-muerto-rumano", "Peso muerto rumano", "isquios, glúteos, espalda baja", ["Barra o mancuernas cerca de las piernas.", "Lleva la cadera atrás con las rodillas casi rectas.", "Sube apretando los glúteos."], "Redondear la espalda.", "Sin peso, con una escoba", "Con más peso o a una pierna"),
        E("puente-gluteo", "Puente de glúteo", "glúteos, core", ["Tumbado, rodillas dobladas.", "Sube la cadera apretando los glúteos.", "Baja despacio."], "Arquear la zona lumbar.", "Rango corto", "Puente a una pierna"),
        E("press-banca", "Press de banca", "pecho, tríceps, hombro", ["Escápulas juntas y pies firmes.", "Baja la barra al pecho controlado.", "Empuja sin despegar la espalda."], "Rebotar la barra; abrir demasiado los codos.", "Mancuernas ligeras", "Más peso o pausa abajo"),
        E("flexion", "Flexiones", "pecho, tríceps, core", ["Manos algo más anchas que los hombros.", "Cuerpo recto como una tabla.", "Baja el pecho al suelo y sube."], "Caer la cadera; codos muy abiertos.", "Flexión inclinada (manos en una mesa)", "Flexión declinada (pies en una silla)"),
        E("press-hombro", "Press de hombro", "hombros, tríceps", ["De pie o sentado con el core firme.", "Empuja sobre la cabeza sin arquear la espalda.", "Baja controlado."], "Arquear la zona lumbar.", "Menos peso", "Más peso o de pie"),
        E("remo", "Remo con mancuerna o barra", "espalda, bíceps", ["Espalda recta, cadera atrás.", "Lleva el codo hacia la cadera.", "Baja despacio."], "Tirar con el brazo y no con la espalda; balancear el tronco.", "Remo con banda elástica", "Remo con más peso"),
        E("jalon", "Jalón al pecho / dominada asistida", "espalda, bíceps", ["Agarre algo más ancho que los hombros.", "Tira hacia el pecho bajando los codos.", "Sube controlado."], "Tirar del cuello hacia atrás.", "Banda elástica", "Dominada"),
        E("plancha", "Plancha", "core", ["Antebrazos en el suelo, cuerpo recto.", "Aprieta abdomen y glúteos.", "Respira con calma."], "Hundir o subir la cadera.", "Plancha con rodillas apoyadas", "Plancha con elevación de un pie"),
        E("bicho-muerto", "Bicho muerto", "core", ["Tumbado, brazos y piernas arriba.", "Baja brazo y pierna contrarios sin arquear la espalda.", "Alterna."], "Despegar la zona lumbar.", "Solo las piernas", "Con peso ligero en las manos"),
        E("curl", "Curl de bíceps", "bíceps", ["Codos pegados al cuerpo.", "Sube sin balancearte.", "Baja despacio."], "Balancear el cuerpo.", "Menos peso", "Más peso o a una mano"),
        E("fondos-silla", "Fondos en silla", "tríceps, pecho", ["Manos en el borde de una silla firme.", "Baja doblando los codos hacia atrás.", "Sube estirando."], "Hombros hacia las orejas.", "Piernas más dobladas", "Piernas estiradas o con peso"),
        E("elevacion-talones", "Elevación de talones", "gemelos", ["De pie, sube de puntillas.", "Pausa arriba.", "Baja controlado."], "Rebotar.", "Con apoyo de la mano", "A una pierna"),
        E("sentadilla-bulgara", "Sentadilla búlgara", "piernas, glúteos", ["Pie de atrás apoyado en un banco.", "Baja recto.", "Sube con la pierna de delante."], "Pie de delante demasiado cerca del banco.", "Zancada normal", "Con mancuernas"),
        E("camina", "Caminar a paso ligero", "resistencia", ["Ritmo en el que puedes hablar pero no cantar.", "Hombros sueltos, paso natural.", "Zapatillas cómodas."], "Empezar demasiado deprisa.", "Ir más despacio", "Cuestas o más ritmo"),
        E("trote", "Trote suave", "resistencia", ["Ritmo en el que puedes hablar frases cortas.", "Pasos cortos, aterriza bajo tu cuerpo.", "Para y camina si te falta el aire."], "Salir demasiado rápido.", "Alterna con caminar", "Tramos más largos"),
        E("movilidad-cadera", "Movilidad de cadera", "cadera", ["En cuclillas profundas con las manos juntas.", "Empuja las rodillas hacia fuera.", "Respira 30 segundos."], "Forzar el rango con dolor.", "Con apoyo en una pared", "Más tiempo"),
        E("movilidad-hombros", "Movilidad de hombros y espalda", "hombros, columna", ["Círculos grandes con los brazos.", "Gato-camello a cuatro apoyos.", "Rotación de tronco sentado."], "Movimientos bruscos.", "Menos recorrido", "Con una banda"),
        E("estiramiento-cuello", "Estiramiento suave de cuello", "cuello", ["Sentado y recto.", "Inclina la oreja hacia el hombro sin forzar.", "Mantén 20 segundos por lado."], "Tirar con la mano.", "Solo girar la cabeza", "Con respiración lenta"),
        E("gato-camello", "Gato–camello", "columna", ["A cuatro apoyos.", "Redondea la espalda al exhalar.", "Arquéala suave al inhalar."], "Ir rápido.", "Rango corto", "Más lento y con pausa"),
        E("sentadilla-aire", "Sentadilla sin peso", "piernas, glúteos", ["Pies a la anchura de los hombros.", "Baja con la espalda recta.", "Sube sin bloquear las rodillas."], "Talones levantados.", "A una silla", "Con pausa de 3 segundos abajo"),
    ];
    public static EjercicioInfo? Buscar(string id) => Todos.FirstOrDefault(e => e.Id == id);
}

public static class GeneradorPlan
{
    public const int SemanasEntreno = 8;

    // Pura: el plan de las próximas semanas según el perfil. Sin dos sesiones del mismo tipo en días seguidos, descarga cada 4.ª semana (−40 % de series).
    public static PlanGenerado Generar(PerfilDatos p)
    {
        var avisos = new List<string> { "Esto es una ayuda personal, no consejo médico: si sientes dolor, mareos o tienes una enfermedad, lesión o embarazo, consúltalo con un profesional antes de empezar." };
        var dias = Math.Clamp(p.DiasSemana, 1, 6);
        var limitado = !string.IsNullOrWhiteSpace(p.Limitaciones);
        if (limitado) avisos.Add("Has anotado limitaciones o lesiones: el plan es general y no está adaptado a ellas. Habla con un profesional antes de seguir los ejercicios.");
        if (p.AnioNacimiento is { } a && DateTime.UtcNow.Year - a >= 65) avisos.Add("A partir de cierta edad conviene una revisión médica antes de empezar y subir la carga muy poco a poco.");
        if (p.Objetivo == "grasa") avisos.Add("Perder grasa de forma sana lleva su tiempo: lo razonable es perder como mucho entre el 0,5 % y el 1 % del peso por semana. Este plan no incluye dietas.");

        string plantilla; string resumen; int semanas = SemanasEntreno;
        List<SesionPlan> s;
        if (p.Objetivo == "movilidad") { plantilla = "movilidad-diaria"; resumen = "10 minutos de movilidad cada día, ideal si trabajas sentado."; s = Movilidad(); semanas = 4; }
        else if (p.Objetivo == "resistencia" || (p.Objetivo is "grasa" or "salud" && p.Nivel == "principiante" && p.Equipo == "nada"))
        {
            if (p.Nivel == "principiante" && p.Objetivo == "resistencia") { plantilla = "correr-0-5k"; resumen = "De caminar a correr 5 km en 8 semanas, con intervalos que van creciendo."; s = Correr(dias); }
            else { plantilla = "caminata-progresiva"; resumen = "Caminata progresiva de 20 a 45 minutos: la forma más suave de empezar."; s = Caminata(dias); }
        }
        else if (p.Equipo == "nada") { plantilla = "calistenia"; resumen = $"Calistenia sin material, {Math.Min(dias, 4)} días, con progresiones."; s = Calistenia(Math.Min(dias, 4), p.Minutos); }
        else if (p.Nivel == "principiante" || dias <= 3) { plantilla = "cuerpo-completo"; resumen = $"Cuerpo completo, {Math.Min(dias, 3)} días: lo básico bien hecho."; s = CuerpoCompleto(Math.Min(dias, 3), p.Equipo == "gimnasio"); }
        else if (dias == 4) { plantilla = "torso-pierna"; resumen = "Torso y pierna alternados, 4 días."; s = TorsoPierna(p.Equipo == "gimnasio"); }
        else { plantilla = "empuje-tiron-pierna"; resumen = $"Empuje, tirón y pierna, {dias} días."; s = EmpujeTironPierna(dias, p.Equipo == "gimnasio"); }

        // descarga: cada 4.ª semana, un 40 % menos de series (mínimo 1)
        s = s.Select(x => x.Semana % 4 == 0 && x.Ejercicios.Count > 0 ? x with { Descarga = true, Ejercicios = x.Ejercicios.Select(e => e with { Series = Math.Max(1, (int)Math.Round(e.Series * 0.6)) }).ToList() } : x).ToList();
        return new PlanGenerado(plantilla, resumen, semanas, s.OrderBy(x => x.Semana).ThenBy(x => x.DiaSemana).ToList(), avisos);
    }

    // los días de la semana (1 = lunes … 7 = domingo) para n sesiones: repartidos con descanso
    public static int[] Dias(int n) => n switch { 1 => [3], 2 => [2, 5], 3 => [1, 3, 5], 4 => [1, 2, 4, 5], 5 => [1, 2, 4, 5, 6], _ => [1, 2, 3, 5, 6, 7] };

    private static EjercicioPlan X(string id, int series, string reps, int descanso = 90) => new(id, Ejercicios.Buscar(id)?.Nombre ?? id, series, reps, descanso);

    private static List<SesionPlan> Repetir(int semanas, Func<int, List<SesionPlan>> semana) => Enumerable.Range(1, semanas).SelectMany(semana).ToList();

    private static List<SesionPlan> CuerpoCompleto(int dias, bool gym)
    {
        var d = Dias(dias);
        return Repetir(SemanasEntreno, w => d.Select((dia, i) => i % 2 == 0
            ? new SesionPlan(w, dia, "cuerpo-a", "Cuerpo completo A", 45, [X(gym ? "prensa" : "sentadilla", 3, w < 5 ? "8-12" : "6-10"), X(gym ? "press-banca" : "flexion", 3, "8-12"), X(gym ? "remo" : "remo", 3, "8-12"), X("plancha", 2, "20-40 s", 60)], false)
            : new SesionPlan(w, dia, "cuerpo-b", "Cuerpo completo B", 45, [X("peso-muerto-rumano", 3, "8-12"), X("press-hombro", 3, "8-12"), X(gym ? "jalon" : "remo", 3, "8-12"), X("bicho-muerto", 2, "8 por lado", 60)], false)).ToList());
    }

    private static List<SesionPlan> TorsoPierna(bool gym)
    {
        var d = Dias(4);
        return Repetir(SemanasEntreno, w => d.Select((dia, i) => i % 2 == 0
            ? new SesionPlan(w, dia, "torso", "Torso", 55, [X(gym ? "press-banca" : "flexion", 4, "6-10"), X(gym ? "jalon" : "remo", 4, "6-10"), X("press-hombro", 3, "8-12"), X("curl", 3, "10-12", 60), X("fondos-silla", 3, "10-12", 60)], false)
            : new SesionPlan(w, dia, "pierna", "Pierna", 55, [X(gym ? "prensa" : "sentadilla", 4, "6-10"), X("peso-muerto-rumano", 3, "8-10"), X("zancada", 3, "10 por pierna"), X("elevacion-talones", 3, "12-15", 60), X("plancha", 3, "30 s", 60)], false)).ToList());
    }

    private static List<SesionPlan> EmpujeTironPierna(int dias, bool gym)
    {
        var d = Dias(dias);
        string[] tipos = ["empuje", "tiron", "pierna", "empuje", "tiron", "pierna"];
        return Repetir(SemanasEntreno, w => d.Select((dia, i) => tipos[i % 3] switch
        {
            "empuje" => new SesionPlan(w, dia, "empuje", "Empuje", 55, [X(gym ? "press-banca" : "flexion", 4, "6-10"), X("press-hombro", 3, "8-12"), X("fondos-silla", 3, "10-12", 60), X("plancha", 3, "30 s", 60)], false),
            "tiron" => new SesionPlan(w, dia, "tiron", "Tirón", 55, [X(gym ? "jalon" : "remo", 4, "6-10"), X("remo", 3, "8-12"), X("curl", 3, "10-12", 60), X("bicho-muerto", 3, "10 por lado", 60)], false),
            _ => new SesionPlan(w, dia, "pierna", "Pierna", 55, [X(gym ? "prensa" : "sentadilla", 4, "6-10"), X("peso-muerto-rumano", 3, "8-10"), X("sentadilla-bulgara", 3, "8 por pierna"), X("elevacion-talones", 3, "12-15", 60)], false),
        }).ToList());
    }

    private static List<SesionPlan> Calistenia(int dias, int minutos)
    {
        var d = Dias(dias); var min = Math.Clamp(minutos, 20, 60);
        // progresiones por semana: flexión inclinada → normal → declinada; sentadilla → búlgara
        return Repetir(SemanasEntreno, w =>
        {
            var empuje = w <= 2 ? "fondos-silla" : "flexion"; var pierna = w <= 4 ? "sentadilla-aire" : "sentadilla-bulgara";
            return d.Select((dia, i) => new SesionPlan(w, dia, i % 2 == 0 ? "calistenia-a" : "calistenia-b", i % 2 == 0 ? "Calistenia A" : "Calistenia B", min,
                i % 2 == 0 ? [X(pierna, 3, "10-15", 60), X(empuje, 3, w < 3 ? "6-10" : "8-12", 60), X("puente-gluteo", 3, "12-15", 45), X("plancha", 3, "20-40 s", 45)]
                           : [X("zancada", 3, "10 por pierna", 60), X("remo", 3, "10-12", 60), X("bicho-muerto", 3, "10 por lado", 45), X("elevacion-talones", 3, "15", 45)], false)).ToList();
        });
    }

    // 0 → 5 km en 8 semanas: tramos de trote (min) y caminata (min) que se repiten
    public static (int Trote, int Camina, int Repeticiones) IntervalosCorrer(int semana) => semana switch
    {
        1 => (1, 2, 8), 2 => (2, 2, 6), 3 => (3, 2, 5), 4 => (5, 2, 4), 5 => (8, 2, 3), 6 => (10, 1, 3), 7 => (30, 0, 1), _ => (35, 0, 1),
    };

    private static List<SesionPlan> Correr(int dias)
    {
        var d = Dias(Math.Clamp(dias, 2, 3));      // correr: siempre con un día de descanso entre sesiones
        return Repetir(SemanasEntreno, w =>
        {
            var (t, c, r) = IntervalosCorrer(w);
            var minutos = t * r + c * r + 10;
            return d.Select(dia => new SesionPlan(w, dia, "correr", $"Trote {t}' / camina {c}' × {r}", minutos,
                [new EjercicioPlan("camina", "Caminar 5 minutos para entrar en calor", 1, "5 min", 0), new EjercicioPlan("trote", $"Trote suave {t} min y camina {c} min", r, $"{t}' + {c}'", 0), new EjercicioPlan("camina", "Caminar 5 minutos para enfriar", 1, "5 min", 0)], false)).ToList();
        });
    }

    // 20 → 45 minutos en 8 semanas
    public static int MinutosCaminata(int semana) => Math.Min(45, 20 + (semana - 1) * 4 - (semana >= 8 ? 3 : 0));

    private static List<SesionPlan> Caminata(int dias)
    {
        var d = Dias(Math.Clamp(dias, 3, 5));
        return Repetir(SemanasEntreno, w => d.Select(dia => new SesionPlan(w, dia, "caminata", $"Caminata de {MinutosCaminata(w)} minutos", MinutosCaminata(w), [new EjercicioPlan("camina", "Caminar a paso ligero", 1, $"{MinutosCaminata(w)} min", 0)], false)).ToList());
    }

    private static List<SesionPlan> Movilidad() =>
        Repetir(4, w => Enumerable.Range(1, 7).Select(dia => new SesionPlan(w, dia, "movilidad", "Movilidad de 10 minutos", 10,
            [X("gato-camello", 1, "10 repeticiones", 0), X("movilidad-hombros", 1, "1 minuto", 0), X("movilidad-cadera", 1, "1 minuto", 0), X("estiramiento-cuello", 1, "20 s por lado", 0)], false)).ToList());

    // Pura: si en las dos últimas semanas has hecho menos del 60 % de las sesiones previstas, el plan se simplifica (un día menos, mínimo 2, y una serie menos)
    // en vez de acumular culpa; con buena asistencia no se toca. Devuelve (hay que simplificar, asistencia 0–1).
    public static (bool Simplificar, double Asistencia) Asistencia(int previstas, int hechas) => previstas <= 0 ? (false, 1) : ((double)hechas / previstas < 0.6, Math.Min(1.0, (double)hechas / previstas));

    public static PlanGenerado Simplificar(PlanGenerado plan, int desdeSemana)
    {
        var l = new List<SesionPlan>();
        foreach (var g in plan.Sesiones.GroupBy(x => x.Semana))
        {
            if (g.Key < desdeSemana) { l.AddRange(g); continue; }
            var sesiones = g.OrderBy(x => x.DiaSemana).ToList();
            var quedan = Math.Max(2, sesiones.Count - 1);
            var elegidas = sesiones.Count <= 2 ? sesiones : sesiones.Where((_, i) => i < quedan).ToList();
            l.AddRange(elegidas.Select(s => s with { Ejercicios = s.Ejercicios.Select(e => e with { Series = Math.Max(1, e.Series - 1) }).ToList(), Minutos = (int)(s.Minutos * 0.8) }));
        }
        return plan with { Sesiones = l, Resumen = plan.Resumen + " (simplificado: un día menos por semana, sin culpa)." };
    }
}

public static class Progresion
{
    // Pura: sube la carga cuando se hacen TODAS las repeticiones del máximo del rango en las dos últimas sesiones; si no, se queda igual. Sube un 2,5 % (mínimo 1 kg, redondeado a 0,5 kg).
    public static double SiguientePeso(double pesoActual, int repsMaximas, IReadOnlyList<IReadOnlyList<int>> ultimasSesiones)
    {
        if (pesoActual <= 0 || ultimasSesiones.Count < 2) return pesoActual;
        var dos = ultimasSesiones.TakeLast(2).ToList();
        if (!dos.All(sesion => sesion.Count > 0 && sesion.All(r => r >= repsMaximas))) return pesoActual;
        var subida = Math.Max(1.0, pesoActual * 0.025);
        return Math.Round((pesoActual + subida) * 2, MidpointRounding.AwayFromZero) / 2;
    }
}

public record PuntoPeso(DateOnly Fecha, double Valor);
public record PuntoSuave(DateOnly Fecha, double Valor, double Media);

public static class SeguimientoPeso
{
    // Pura: media móvil de 7 días (los puntos de los 7 días hasta cada fecha, ambos incluidos), para no obsesionarse con el dato del día
    public static List<PuntoSuave> MediaMovil(IEnumerable<PuntoPeso> puntos, int dias = 7)
    {
        var o = puntos.OrderBy(p => p.Fecha).ToList();
        return o.Select(p => new PuntoSuave(p.Fecha, p.Valor, Math.Round(o.Where(q => q.Fecha <= p.Fecha && q.Fecha > p.Fecha.AddDays(-dias)).Average(q => q.Valor), 2))).ToList();
    }

    // Pura: kg por semana (regresión lineal de los últimos 28 días con al menos 3 pesadas en 2 semanas distintas); null si no hay datos para decir nada
    public static double? TendenciaSemanal(IEnumerable<PuntoPeso> puntos, DateOnly hoy)
    {
        var p = puntos.Where(x => x.Fecha > hoy.AddDays(-28) && x.Fecha <= hoy).OrderBy(x => x.Fecha).ToList();
        if (p.Count < 3 || (p[^1].Fecha.DayNumber - p[0].Fecha.DayNumber) < 14) return null;
        var xs = p.Select(x => (double)(x.Fecha.DayNumber - p[0].Fecha.DayNumber)).ToList(); var ys = p.Select(x => x.Valor).ToList();
        var mx = xs.Average(); var my = ys.Average();
        var den = xs.Sum(x => (x - mx) * (x - mx));
        return den == 0 ? null : Math.Round(xs.Zip(ys, (x, y) => (x - mx) * (y - my)).Sum() / den * 7, 2);
    }

    // Pura: un aviso prudente si se pierde más de lo razonable (más del 1 % del peso a la semana)
    public static string? AvisoRitmo(double? kgSemana, double pesoActual) =>
        kgSemana is { } k && pesoActual > 0 && -k > pesoActual * 0.01 ? "Estás perdiendo peso más rápido de lo que suele ser sano (más del 1 % a la semana). Conviene ir más despacio: come con calma y, si te preocupa, consúltalo con un profesional." : null;
}

public static class ResumenBienestar
{
    // Pura: «esta semana: 3/3 entrenos, 7 h 12 min de sueño de media…»
    public static string Texto(int hechos, int previstos, double? sueñoMedioHoras, double? aguaMedia, int? pasosMedia)
    {
        var partes = new List<string> { previstos > 0 ? $"{hechos}/{previstos} entrenos" : $"{hechos} {(hechos == 1 ? "entreno" : "entrenos")}" };
        if (sueñoMedioHoras is { } s) { var h = (int)s; var m = (int)Math.Round((s - h) * 60); if (m == 60) { h++; m = 0; } partes.Add($"{h} h {m:00} min de sueño de media"); }
        if (aguaMedia is { } a) partes.Add($"{a.ToString("0.#", CultureInfo.GetCultureInfo("es-ES"))} vasos de agua al día");
        if (pasosMedia is { } p) partes.Add($"{p:N0} pasos al día".Replace(',', '.'));
        return "Esta semana: " + string.Join(", ", partes) + ".";
    }
}
