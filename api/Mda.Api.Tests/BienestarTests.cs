using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Bienestar;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mda.Api.Tests;

public class PlanTests
{
    public static IEnumerable<object[]> Perfiles()
    {
        foreach (var obj in Objetivos.Validos)
            foreach (var niv in Objetivos.Niveles)
                foreach (var eq in Objetivos.Equipos)
                    foreach (var dias in new[] { 1, 2, 3, 4, 5, 6 })
                        yield return [obj, niv, eq, dias];
    }

    [Theory]
    [MemberData(nameof(Perfiles))]
    public void Cada_combinacion_da_un_plan_valido_sin_dos_dias_seguidos_de_lo_mismo_y_con_descarga(string objetivo, string nivel, string equipo, int dias)
    {
        var plan = GeneradorPlan.Generar(new PerfilDatos(objetivo, nivel, dias, equipo, "", 45, 170, 1990));
        Assert.NotEmpty(plan.Sesiones);
        Assert.NotEmpty(plan.Resumen); Assert.Contains("no consejo médico", plan.Avisos[0]);
        Assert.All(plan.Sesiones, s => { Assert.InRange(s.DiaSemana, 1, 7); Assert.InRange(s.Semana, 1, plan.Semanas); Assert.NotEmpty(s.Ejercicios); Assert.InRange(s.Minutos, 5, 120); });
        // ni dos sesiones el mismo día
        Assert.Equal(plan.Sesiones.Count, plan.Sesiones.Select(s => (s.Semana, s.DiaSemana)).Distinct().Count());
        // sin dos días seguidos del mismo tipo (la movilidad diaria y la caminata, que son suaves, son la excepción)
        if (plan.Plantilla is not ("movilidad-diaria" or "caminata-progresiva"))
        {
            var o = plan.Sesiones.OrderBy(s => s.Semana).ThenBy(s => s.DiaSemana).ToList();
            for (var i = 1; i < o.Count; i++)
            {
                var d = (o[i].Semana - o[i - 1].Semana) * 7 + o[i].DiaSemana - o[i - 1].DiaSemana;
                if (d == 1) Assert.NotEqual(o[i - 1].Tipo, o[i].Tipo);
            }
        }
        // los ejercicios existen en la biblioteca
        Assert.All(plan.Sesiones.SelectMany(s => s.Ejercicios), e => { Assert.NotNull(Ejercicios.Buscar(e.Id)); Assert.True(e.Series >= 1); });
        // cada 4.ª semana es de descarga
        Assert.All(plan.Sesiones.Where(s => s.Semana % 4 == 0), s => Assert.True(s.Descarga));
        Assert.All(plan.Sesiones.Where(s => s.Semana % 4 != 0), s => Assert.False(s.Descarga));
    }

    [Fact]
    public void Es_determinista_el_mismo_perfil_da_el_mismo_plan()
    {
        var p = new PerfilDatos("musculo", "intermedio", 4, "gimnasio", "", 60, 175, 1988);
        Assert.Equal(JsonSerializer.Serialize(GeneradorPlan.Generar(p)), JsonSerializer.Serialize(GeneradorPlan.Generar(p)));
    }

    [Theory]
    [InlineData("fuerza", "principiante", "gimnasio", 3, "cuerpo-completo")]
    [InlineData("musculo", "intermedio", "gimnasio", 4, "torso-pierna")]
    [InlineData("musculo", "avanzado", "gimnasio", 5, "empuje-tiron-pierna")]
    [InlineData("fuerza", "intermedio", "nada", 3, "calistenia")]
    [InlineData("resistencia", "principiante", "casa", 3, "correr-0-5k")]
    [InlineData("salud", "principiante", "nada", 3, "caminata-progresiva")]
    [InlineData("movilidad", "avanzado", "gimnasio", 6, "movilidad-diaria")]
    public void Elige_la_plantilla_que_toca(string objetivo, string nivel, string equipo, int dias, string plantilla) =>
        Assert.Equal(plantilla, GeneradorPlan.Generar(new PerfilDatos(objetivo, nivel, dias, equipo, "", 45, null, null)).Plantilla);

    [Fact]
    public void La_descarga_baja_un_40_por_ciento_las_series_con_minimo_de_una()
    {
        var plan = GeneradorPlan.Generar(new PerfilDatos("fuerza", "principiante", 3, "gimnasio", "", 45, null, null));
        var normal = plan.Sesiones.First(s => s.Semana == 3 && s.Tipo == "cuerpo-a").Ejercicios.First(e => e.Id == "prensa");
        var descarga = plan.Sesiones.First(s => s.Semana == 4 && s.Tipo == "cuerpo-a").Ejercicios.First(e => e.Id == "prensa");
        Assert.Equal(3, normal.Series); Assert.Equal(2, descarga.Series);                          // 3 × 0,6 = 1,8 → 2
        Assert.All(plan.Sesiones.Where(s => s.Descarga).SelectMany(s => s.Ejercicios), e => Assert.True(e.Series >= 1));
    }

    [Fact]
    public void Las_limitaciones_la_edad_y_perder_grasa_añaden_avisos_prudentes()
    {
        var a = GeneradorPlan.Generar(new PerfilDatos("grasa", "principiante", 3, "casa", "rodilla operada", 45, null, 1950));
        Assert.Contains(a.Avisos, x => x.Contains("limitaciones")); Assert.Contains(a.Avisos, x => x.Contains("revisión médica")); Assert.Contains(a.Avisos, x => x.Contains("1 %") && x.Contains("no incluye dietas"));
        Assert.Single(GeneradorPlan.Generar(new PerfilDatos("fuerza", "intermedio", 3, "gimnasio", "", 45, null, 1995)).Avisos);
    }

    [Fact]
    public void Correr_0_a_5k_crece_semana_a_semana_y_la_caminata_llega_a_45_minutos()
    {
        Assert.Equal((1, 2, 8), GeneradorPlan.IntervalosCorrer(1)); Assert.Equal((8, 2, 3), GeneradorPlan.IntervalosCorrer(5)); Assert.Equal((35, 0, 1), GeneradorPlan.IntervalosCorrer(8));
        var trote = Enumerable.Range(1, 8).Select(w => GeneradorPlan.IntervalosCorrer(w)).Select(i => i.Trote * i.Repeticiones).ToList();
        Assert.True(trote.Zip(trote.Skip(1), (a, b) => b >= a).All(x => x));
        Assert.Equal(20, GeneradorPlan.MinutosCaminata(1)); Assert.All(Enumerable.Range(1, 8), w => Assert.InRange(GeneradorPlan.MinutosCaminata(w), 20, 45));
        Assert.True(GeneradorPlan.MinutosCaminata(7) >= GeneradorPlan.MinutosCaminata(2));
    }

    [Fact]
    public void Si_faltas_a_mas_del_40_por_ciento_el_plan_se_simplifica_y_si_no_se_deja_igual()
    {
        Assert.True(GeneradorPlan.Asistencia(6, 3).Simplificar); Assert.False(GeneradorPlan.Asistencia(6, 4).Simplificar); Assert.False(GeneradorPlan.Asistencia(0, 0).Simplificar);
        Assert.Equal(0.5, GeneradorPlan.Asistencia(6, 3).Asistencia, 2);
        var plan = GeneradorPlan.Generar(new PerfilDatos("musculo", "intermedio", 4, "gimnasio", "", 60, null, null));
        var simple = GeneradorPlan.Simplificar(plan, 3);
        Assert.Equal(plan.Sesiones.Count(s => s.Semana < 3), simple.Sesiones.Count(s => s.Semana < 3));                       // lo ya pasado no se toca
        Assert.All(Enumerable.Range(3, 6), w => Assert.Equal(3, simple.Sesiones.Count(s => s.Semana == w)));                 // de 4 días a 3
        var antes = plan.Sesiones.First(s => s.Semana == 3).Ejercicios[0]; var despues = simple.Sesiones.First(s => s.Semana == 3).Ejercicios[0];
        Assert.Equal(antes.Series - 1, despues.Series);
        Assert.Contains("sin culpa", simple.Resumen);
        // nunca baja de 2 días a la semana
        var dos = GeneradorPlan.Simplificar(GeneradorPlan.Generar(new PerfilDatos("fuerza", "principiante", 2, "gimnasio", "", 45, null, null)), 1);
        Assert.All(dos.Sesiones.GroupBy(s => s.Semana), g => Assert.Equal(2, g.Count()));
    }
}

public class ProgresionYSeguimientoTests
{
    [Fact]
    public void Sube_la_carga_solo_si_las_dos_ultimas_sesiones_llegan_al_maximo_de_repeticiones()
    {
        IReadOnlyList<IReadOnlyList<int>> buenas = [[12, 12, 12], [12, 12, 12]];
        Assert.Equal(51.5, Progresion.SiguientePeso(50, 12, buenas));                                      // +2,5 % = 1,25 kg → 51,25 → redondeado a medio kilo: 51,5
    }

    [Theory]
    [InlineData(50, 51.5)] [InlineData(100, 102.5)] [InlineData(20, 21.0)] [InlineData(0, 0.0)]
    public void La_subida_es_de_un_2_5_por_ciento_con_minimo_de_un_kilo_y_redondeo_a_medio_kilo(double peso, double esperado)
    {
        IReadOnlyList<IReadOnlyList<int>> buenas = [[10, 10], [10, 10]];
        Assert.Equal(esperado, Progresion.SiguientePeso(peso, 10, buenas));
    }

    [Fact]
    public void Con_una_sesion_floja_o_con_pocos_datos_no_sube()
    {
        Assert.Equal(50, Progresion.SiguientePeso(50, 12, [[12, 12, 12], [12, 11, 12]]));
        Assert.Equal(50, Progresion.SiguientePeso(50, 12, [[12, 12, 12]]));
        Assert.Equal(50, Progresion.SiguientePeso(50, 12, [[12, 12], []]));
        Assert.Equal(51.5, Progresion.SiguientePeso(50, 12, [[8, 8], [12, 12], [12, 12]]));                // cuentan solo las dos últimas: una mala antes no impide subir
        Assert.Equal(50, Progresion.SiguientePeso(50, 12, [[12, 12], [12, 12], [8, 8]]));                  // una mala al final sí
    }

    private static PuntoPeso P(int dia, double v) => new(new DateOnly(2026, 11, 1).AddDays(dia), v);

    [Fact]
    public void La_media_movil_de_7_dias_suaviza_el_dato_suelto()
    {
        var m = SeguimientoPeso.MediaMovil([P(0, 80), P(1, 80), P(2, 82), P(3, 80), P(10, 79)]);
        Assert.Equal(80, m[0].Media); Assert.Equal(80, m[1].Media);
        Assert.Equal(80.67, m[2].Media, 2);
        Assert.Equal(79, m[4].Media);                                              // los de hace más de 7 días ya no cuentan
        Assert.Empty(SeguimientoPeso.MediaMovil([]));
        Assert.Equal(75, SeguimientoPeso.MediaMovil([P(5, 75)])[0].Media);
    }

    [Fact]
    public void La_tendencia_semanal_es_la_pendiente_y_pide_al_menos_tres_pesadas_en_dos_semanas()
    {
        var hoy = new DateOnly(2026, 11, 28);
        Assert.Equal(-0.5, SeguimientoPeso.TendenciaSemanal([P(0, 82), P(7, 81.5), P(14, 81), P(21, 80.5)], hoy)!.Value, 2);
        Assert.Null(SeguimientoPeso.TendenciaSemanal([P(0, 82), P(3, 81.5)], hoy));
        Assert.Null(SeguimientoPeso.TendenciaSemanal([P(20, 82), P(21, 81.5), P(22, 81)], hoy));       // tres pesadas pero en tres días: no es tendencia
        Assert.Null(SeguimientoPeso.TendenciaSemanal([], hoy));
    }

    [Fact]
    public void Avisa_con_prudencia_si_se_pierde_mas_de_un_uno_por_ciento_a_la_semana()
    {
        Assert.NotNull(SeguimientoPeso.AvisoRitmo(-1.2, 80)); Assert.Contains("profesional", SeguimientoPeso.AvisoRitmo(-1.2, 80)!);
        Assert.Null(SeguimientoPeso.AvisoRitmo(-0.5, 80)); Assert.Null(SeguimientoPeso.AvisoRitmo(0.8, 80)); Assert.Null(SeguimientoPeso.AvisoRitmo(null, 80)); Assert.Null(SeguimientoPeso.AvisoRitmo(-5, 0));
    }

    [Fact]
    public void El_resumen_semanal_se_lee_en_castellano()
    {
        Assert.Equal("Esta semana: 3/3 entrenos, 7 h 12 min de sueño de media, 6,5 vasos de agua al día, 8.200 pasos al día.", ResumenBienestar.Texto(3, 3, 7.2, 6.5, 8200));
        Assert.Equal("Esta semana: 1 entreno.", ResumenBienestar.Texto(1, 0, null, null, null));
        Assert.Equal("Esta semana: 0/2 entrenos, 8 h 00 min de sueño de media.", ResumenBienestar.Texto(0, 2, 7.999, null, null));
    }

    [Fact]
    public void Las_fechas_del_plan_cuadran_con_la_semana_y_el_dia()
    {
        var lunes = new DateOnly(2026, 11, 2);
        Assert.Equal(lunes, BienestarServicio.Lunes(new DateOnly(2026, 11, 8))); Assert.Equal(lunes, BienestarServicio.Lunes(lunes));
        Assert.Equal(1, BienestarServicio.SemanaDelPlan(lunes, new DateOnly(2026, 11, 8))); Assert.Equal(2, BienestarServicio.SemanaDelPlan(lunes, new DateOnly(2026, 11, 9)));
        Assert.Equal(1, BienestarServicio.DiaSemana(lunes)); Assert.Equal(7, BienestarServicio.DiaSemana(new DateOnly(2026, 11, 8)));
        Assert.Equal(0, BienestarServicio.SemanaDelPlan(lunes, new DateOnly(2026, 10, 30)));              // antes del plan
    }
}

public class BienestarApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Cuestionario_plan_hoy_entrenos_agua_medidas_y_borrado_completo()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/bienestar/perfil", new { objetivo = "volar", nivel = "principiante", diasSemana = 3, equipo = "casa", limitaciones = "", minutos = 45 })).StatusCode);
        var r = await c.PutAsJsonAsync("/api/bienestar/perfil", new { objetivo = "fuerza", nivel = "principiante", diasSemana = 3, equipo = "gimnasio", limitaciones = "", minutos = 45, alturaCm = 172, anioNacimiento = 1992 });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("cuerpo-completo", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("plantilla").GetString());

        var plan = await c.GetFromJsonAsync<JsonElement>("/api/bienestar/plan");
        Assert.Equal(8, plan.GetProperty("plan").GetProperty("semanas").GetInt32());

        // lo de hoy: agua con un toque, entreno y peso
        Assert.Equal(1, (await (await c.PutAsJsonAsync("/api/bienestar/dia", new { sumarAgua = 1 })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("agua").GetInt32());
        Assert.Equal(3, (await (await c.PutAsJsonAsync("/api/bienestar/dia", new { sumarAgua = 2, sueño = 7.5, pasos = 8000 })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("agua").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/bienestar/dia", new { agua = 99 })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/bienestar/entrenos", new { tipo = "pierna", minutos = 50, esfuerzo = 7, series = new[] { new { ejercicio = "sentadilla", serie = 1, reps = 10, peso = 60.0 } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/bienestar/entrenos", new { esfuerzo = 11 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/bienestar/medidas", new { peso = 81.4, cintura = 90.0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/bienestar/medidas", new { peso = 5.0 })).StatusCode);

        var hoy = await c.GetFromJsonAsync<JsonElement>("/api/bienestar/hoy");
        Assert.True(hoy.GetProperty("tienePlan").GetBoolean()); Assert.True(hoy.GetProperty("entrenadoHoy").GetBoolean());
        Assert.Equal(3, hoy.GetProperty("dia").GetProperty("agua").GetInt32());
        Assert.Contains("no consejo médico", hoy.GetProperty("aviso").GetString());
        Assert.Matches(@"\d/3 entrenos", hoy.GetProperty("semana").GetString());
        var med = await c.GetFromJsonAsync<JsonElement>("/api/bienestar/medidas");
        Assert.Equal(81.4, med.GetProperty("peso")[0].GetProperty("media").GetDouble());

        // se guarda cifrado en la base
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.All(await db.Entrenos.AsNoTracking().ToListAsync(), e => Assert.True(Mda.Api.Seguridad.Boveda.EstaCifrado(e.Datos)));
            Assert.All(await db.Medidas.AsNoTracking().ToListAsync(), e => Assert.True(Mda.Api.Seguridad.Boveda.EstaCifrado(e.Datos)));
            Assert.All(await db.PerfilesBienestar.AsNoTracking().ToListAsync(), e => Assert.True(Mda.Api.Seguridad.Boveda.EstaCifrado(e.Datos)));
        }

        var asis = await c.GetFromJsonAsync<JsonElement>("/api/bienestar/asistencia");
        Assert.True(asis.GetProperty("hayPlan").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/api/bienestar/plan/simplificar", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/bienestar/todo")).StatusCode);
        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/bienestar/hoy")).GetProperty("tienePerfil").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, await Estado(c, "/api/bienestar/plan"));
    }

    private static async Task<HttpStatusCode> Estado(HttpClient c, string url) => (await c.GetAsync(url)).StatusCode;

    [Fact]
    public async Task El_atajo_de_salud_guarda_pasos_peso_sueño_y_entrenos_sin_duplicarlos()
    {
        var c = await factory.Dentro();
        var token = (await (await c.PostAsJsonAsync("/api/atajos-tokens", new { nombre = "Salud", alcances = new[] { "salud" } })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var movil = factory.CreateClient();
        movil.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var cuerpo = new { peso = 80.2, sueño = 7.0, pasos = 9500, entrenos = new[] { new { tipo = "carrera", minutos = 30 } } };
        Assert.Equal(HttpStatusCode.OK, (await movil.PostAsJsonAsync("/api/atajos/salud", cuerpo)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await movil.PostAsJsonAsync("/api/atajos/salud", cuerpo)).StatusCode);                   // repetirlo no duplica el entreno
        Assert.Equal(HttpStatusCode.BadRequest, (await movil.PostAsJsonAsync("/api/atajos/salud", new { peso = 1.0 })).StatusCode);
        var lista = await c.GetFromJsonAsync<JsonElement>("/api/bienestar/entrenos");
        Assert.Equal(1, lista.EnumerateArray().Count(e => e.GetProperty("datos").GetProperty("tipo").GetString() == "carrera"));
        var dia = (await c.GetFromJsonAsync<JsonElement>("/api/bienestar/hoy")).GetProperty("dia");
        Assert.Equal(9500, dia.GetProperty("pasos").GetInt32()); Assert.Equal(7.0, dia.GetProperty("sueño").GetDouble());

        var sin = (await (await c.PostAsJsonAsync("/api/atajos-tokens", new { nombre = "Gasto", alcances = new[] { "gasto" } })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var malo = factory.CreateClient(); malo.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", sin);
        Assert.Equal(HttpStatusCode.Unauthorized, (await malo.PostAsJsonAsync("/api/atajos/salud", cuerpo)).StatusCode);
    }

    [Fact]
    public async Task La_biblioteca_de_ejercicios_esta_completa_y_hace_falta_entrar()
    {
        var c = await factory.Dentro();
        var l = await c.GetFromJsonAsync<JsonElement>("/api/bienestar/ejercicios");
        Assert.True(l.GetArrayLength() >= 20);
        Assert.All(l.EnumerateArray(), e => { Assert.NotEqual("", e.GetProperty("nombre").GetString()); Assert.True(e.GetProperty("pasos").GetArrayLength() >= 3); Assert.NotEqual("", e.GetProperty("errores").GetString()); });
        using var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/bienestar/hoy")).StatusCode);
    }
}
