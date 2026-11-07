using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Avisos;
using Mda.Api.Cocina;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Tests;

public class IngredientesTests
{
    [Theory]
    [InlineData("200 g de harina", 200, "g", "harina")]
    [InlineData("1,5 kg de patatas", 1500, "g", "patatas")]
    [InlineData("200g harina", 200, "g", "harina")]                              // pegado sin espacio también vale
    [InlineData("2 l de leche", 2000, "ml", "leche")]
    [InlineData("250 ml agua", 250, "ml", "agua")]
    [InlineData("1/2 cebolla", 0.5, "", "cebolla")]
    [InlineData("1 1/2 tazas de arroz", 1.5, "taza", "arroz")]
    [InlineData("½ cucharadita de sal", 0.5, "cucharadita", "sal")]
    [InlineData("3 dientes de ajo", 3, "diente", "ajo")]
    [InlineData("4 huevos", 4, "", "huevos")]
    [InlineData("2 cdas. de aceite", 2, "cucharada", "aceite")]
    public void Separa_cantidad_unidad_y_nombre(string texto, double cantidad, string unidad, string nombre)
    {
        var i = Ingrediente.Parsear(texto);
        Assert.Equal(cantidad, i.Cantidad); Assert.Equal(unidad, i.Unidad); Assert.Equal(nombre, i.Nombre);
    }

    [Theory] [InlineData("sal al gusto")] [InlineData("un poco de pimienta")] [InlineData("")] [InlineData("Perejil fresco")]
    public void Lo_que_no_lleva_cantidad_se_queda_como_texto(string texto)
    {
        var i = Ingrediente.Parsear(texto);
        Assert.Null(i.Cantidad); Assert.Equal(texto, i.Nombre);
    }

    [Fact]
    public void Suma_mismo_ingrediente_y_misma_unidad_con_el_factor_de_raciones_y_normaliza_kg_y_litros()
    {
        var s = Ingrediente.Sumar([
            (Ingrediente.Parsear("600 g de patatas"), 1), (Ingrediente.Parsear("1 kg patatas"), 1), (Ingrediente.Parsear("2 huevos"), 2), (Ingrediente.Parsear("3 huevos"), 1),
            (Ingrediente.Parsear("500 ml leche"), 1), (Ingrediente.Parsear("1 l de leche"), 1), (Ingrediente.Parsear("sal al gusto"), 1), (Ingrediente.Parsear("Sal al gusto"), 1)]);
        Assert.Contains(("patatas", "1,6 kg"), s);
        Assert.Contains(("huevos", "7"), s);
        Assert.Contains(("leche", "1,5 l"), s);
        Assert.Single(s, x => Ingrediente.Clave(x.Nombre) == "sal al gusto");
    }

    [Fact]
    public void Mismo_nombre_con_unidades_distintas_no_se_mezcla_y_los_nombres_se_reconocen_sin_tildes()
    {
        var s = Ingrediente.Sumar([(Ingrediente.Parsear("200 g tomate"), 1), (Ingrediente.Parsear("2 tomate"), 1), (Ingrediente.Parsear("1 Jamón"), 1), (Ingrediente.Parsear("1 jamon"), 1)]);
        Assert.Equal(3, s.Count);
        Assert.Contains(("Jamón", "2"), s);
    }

    [Theory] [InlineData(1500, "g", "1,5 kg")] [InlineData(250, "g", "250 g")] [InlineData(2500, "ml", "2,5 l")] [InlineData(0.5, "", "0,5")] [InlineData(3, "diente", "3 diente")]
    public void Escribe_las_cantidades_de_forma_natural(double c, string u, string esperado) => Assert.Equal(esperado, Ingrediente.Escribir(c, u));
}

public class SchemaRecipeTests
{
    private const string Pagina = """
    <html><head><script type="application/ld+json">{"@context":"https://schema.org","@graph":[{"@type":"WebSite","name":"Cocina rica"},
      {"@type":"Recipe","name":"Tortilla de patatas &amp; cebolla","recipeYield":["4","4 raciones"],"totalTime":"PT1H15M","keywords":"Clásica, Española, vegetariana",
       "recipeCategory":"Cena","recipeIngredient":["500 g de patatas","6 huevos","1/2 cebolla","Aceite de oliva","sal"],
       "recipeInstructions":[{"@type":"HowToStep","text":"Pela y corta las patatas."},{"@type":"HowToSection","name":"Cuajar","itemListElement":[{"@type":"HowToStep","text":"Mezcla con el huevo."},{"@type":"HowToStep","text":"Cuaja por los dos lados."}]}]}]}</script>
      <script type="application/ld+json">{ roto </script></head><body>x</body></html>
    """;

    [Fact]
    public void Lee_una_receta_de_un_grafo_con_secciones_ingredientes_tiempo_y_etiquetas()
    {
        var r = SchemaRecipe.Parsear(Pagina)!;
        Assert.Equal("Tortilla de patatas & cebolla", r.Nombre);
        Assert.Equal(4, r.Raciones); Assert.Equal(75, r.Minutos);
        Assert.Equal(["clásica", "española", "vegetariana", "cena"], r.Etiquetas);
        Assert.Equal(5, r.Ingredientes.Count);
        Assert.Equal((500.0, "g", "patatas"), (r.Ingredientes[0].Cantidad!.Value, r.Ingredientes[0].Unidad, r.Ingredientes[0].Nombre));
        Assert.Equal(["Pela y corta las patatas.", "Mezcla con el huevo.", "Cuaja por los dos lados."], r.Pasos);
    }

    [Fact]
    public void Aguanta_receta_suelta_tipo_en_lista_pasos_en_texto_y_sin_datos()
    {
        var p = """<script type="application/ld+json">{"@type":["Thing","Recipe"],"name":"Ensalada","recipeIngredient":["2 tomates"],"recipeInstructions":"Corta. Aliña. Sirve."}</script>""";
        var r = SchemaRecipe.Parsear(p)!;
        Assert.Equal("Ensalada", r.Nombre); Assert.Equal(4, r.Raciones); Assert.Equal(0, r.Minutos);
        Assert.Equal(["Corta.", "Aliña.", "Sirve."], r.Pasos);
        Assert.Null(SchemaRecipe.Parsear("<html>sin nada</html>")); Assert.Null(SchemaRecipe.Parsear(null)); Assert.Null(SchemaRecipe.Parsear("""<script type="application/ld+json">{"@type":"Article"}</script>"""));
        Assert.Null(SchemaRecipe.Parsear("""<script type="application/ld+json">{"@type":"Recipe","name":"  "}</script>"""));
    }

    [Theory] [InlineData("PT1H30M", 90)] [InlineData("PT45M", 45)] [InlineData("P0DT0H20M", 20)] [InlineData("PT2H", 120)]
    public void Las_duraciones_iso_pasan_a_minutos(string iso, int min) => Assert.Equal(min, SchemaRecipe.Duracion(iso));

    [Theory] [InlineData(null)] [InlineData("")] [InlineData("una hora")] [InlineData("45")]
    public void Una_duracion_rara_es_null(string? iso) => Assert.Null(SchemaRecipe.Duracion(iso));
}

public class UrlsRecetasTests
{
    [Theory] [InlineData("https://www.recetasgratis.net/receta-de-tortilla-12345")] [InlineData("http://cocina.example.com/a?b=c")] [InlineData("https://8.8.8.8/receta")]
    public void Admite_paginas_publicas(string url) => Assert.True(UrlsRecetas.Valida(url, out _));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("ftp://x.com/a")] [InlineData("file:///etc/passwd")] [InlineData("javascript:alert(1)")] [InlineData("https://user:clave@x.com/a")]
    [InlineData("http://localhost/x")] [InlineData("http://127.0.0.1/x")] [InlineData("http://192.168.1.5/x")] [InlineData("http://10.0.0.1/x")] [InlineData("http://172.20.0.1/x")]
    [InlineData("http://169.254.169.254/latest/meta-data")] [InlineData("http://[::1]/x")] [InlineData("http://[fd00::1]/x")] [InlineData("http://intranet/x")] [InlineData("http://mi.servidor.local/x")]
    [InlineData("https://x.com:22/a")] [InlineData("http://0.0.0.0/")] [InlineData("http://100.64.0.1/x")]
    public void Rechaza_la_red_local_otros_esquemas_y_trucos(string? url) => Assert.False(UrlsRecetas.Valida(url, out _));
}

public class MenuSemanalTests
{
    private static RecetaMenu R(int id, string nombre, string[]? tags = null, string[]? ing = null) => new(id, nombre, tags ?? [], ing ?? []);
    private static readonly DateOnly Lunes = new(2026, 11, 2);

    private static List<RecetaMenu> Catalogo() => Enumerable.Range(1, 20).Select(i => R(i, $"Receta {i}", i % 2 == 0 ? ["rápida"] : ["lenta"])).ToList();

    [Fact]
    public void Genera_14_casillas_de_la_semana_con_recetas_distintas_si_hay_de_sobra()
    {
        var m = MenuSemanal.Generar(Catalogo(), new Dictionary<int, DateOnly>(), Lunes, [], [], [], 0, 7);
        Assert.Equal(14, m.Count);
        Assert.Equal(Lunes, m[0].Fecha); Assert.Equal(Lunes.AddDays(6), m[^1].Fecha);
        Assert.Equal(14, m.Select(c => c.RecetaId).Distinct().Count());
        Assert.All(m, c => Assert.False(c.Sobras));
    }

    [Fact]
    public void Respeta_las_etiquetas_pedidas_y_no_inventa_si_no_hay()
    {
        var m = MenuSemanal.Generar(Catalogo(), new Dictionary<int, DateOnly>(), Lunes, ["rápida"], [], [], 0, 3);
        Assert.All(m, c => Assert.True(c.RecetaId % 2 == 0));
        Assert.Empty(MenuSemanal.Generar(Catalogo(), new Dictionary<int, DateOnly>(), Lunes, ["vegana"], [], [], 0, 3));
        Assert.Empty(MenuSemanal.Generar([], new Dictionary<int, DateOnly>(), Lunes, [], [], [], 0, 3));
    }

    [Fact]
    public void Las_alergias_excluyen_por_nombre_ingrediente_o_etiqueta_sin_tildes()
    {
        var rs = new List<RecetaMenu> { R(1, "Ensalada", ing: ["nueces", "lechuga"]), R(2, "Pasta"), R(3, "Crema de Almendras"), R(4, "Pollo", ["sin gluten"]), R(5, "Tarta", ing: ["Cacahuetes"]) };
        var m = MenuSemanal.Generar(rs, new Dictionary<int, DateOnly>(), Lunes, [], ["nueces", "ALMENDRA", "cacahuete"], [], 0, 1);
        Assert.All(m, c => Assert.True(c.RecetaId is 2 or 4));
        Assert.Empty(MenuSemanal.Generar([R(1, "Pastel de nueces")], new Dictionary<int, DateOnly>(), Lunes, [], ["Nueces"], [], 0, 1));
    }

    [Fact]
    public void No_repite_lo_de_los_ultimos_14_dias_y_si_no_hay_mas_usa_lo_mas_antiguo()
    {
        var rs = Enumerable.Range(1, 16).Select(i => R(i, $"R{i}")).ToList();
        var ultima = Enumerable.Range(1, 8).ToDictionary(i => i, _ => Lunes.AddDays(-3));          // 8 recetas recién hechas
        var m = MenuSemanal.Generar(rs, ultima, Lunes, [], [], [], 0, 5);
        Assert.All(m, c => Assert.True(c.RecetaId > 8, $"repitió {c.RecetaId}"));                   // las 8 restantes bastan para 14 casillas? no: se reutilizan entre ellas, nunca las recientes
    }

    [Fact]
    public void Los_dias_de_sobras_ponen_la_cena_de_la_vispera_en_la_comida()
    {
        var m = MenuSemanal.Generar(Catalogo(), new Dictionary<int, DateOnly>(), Lunes, [], [], [], 2, 11);
        foreach (var d in MenuSemanal.SobrasDias(2))
        {
            var comida = m.Single(c => c.Fecha == Lunes.AddDays(d) && c.Momento == "comida");
            var cenaAntes = m.Single(c => c.Fecha == Lunes.AddDays(d - 1) && c.Momento == "cena");
            Assert.True(comida.Sobras); Assert.Equal(cenaAntes.RecetaId, comida.RecetaId);
        }
        Assert.Equal(2, m.Count(c => c.Sobras));
        Assert.Equal(3, MenuSemanal.SobrasDias(3).Count); Assert.Empty(MenuSemanal.SobrasDias(0));
    }

    [Fact]
    public void Con_la_misma_semilla_sale_lo_mismo_y_los_gustos_pasan_delante()
    {
        var a = MenuSemanal.Generar(Catalogo(), new Dictionary<int, DateOnly>(), Lunes, [], [], [], 0, 42);
        var b = MenuSemanal.Generar(Catalogo(), new Dictionary<int, DateOnly>(), Lunes, [], [], [], 0, 42);
        Assert.Equal(a, b);
        var conGusto = MenuSemanal.Generar(Catalogo(), new Dictionary<int, DateOnly>(), Lunes, [], [], ["rápida"], 0, 42);
        Assert.All(conGusto.Take(10), c => Assert.True(c.RecetaId % 2 == 0));                          // hay 10 «rápidas»: las 10 primeras casillas son de esas
    }

    [Fact]
    public void El_aviso_de_hoy_lista_comida_y_cena_y_marca_las_sobras()
    {
        Assert.Null(AvisoCocina.Texto([]));
        Assert.Equal("🍽️ Hoy toca — comida: Lentejas (sobras); cena: Tortilla", AvisoCocina.Texto([("cena", "Tortilla", false), ("comida", "Lentejas", true)]));
    }
}

public class CocinaAvisosTests
{
    private sealed class AvisosFalsos : IAvisos
    {
        public readonly List<string> Telegram = [];
        public Canales Disponibles() => throw new NotImplementedException();
        public Task<bool> PorCorreo(string asunto, string texto, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PorWhatsApp(string texto, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PorTelegram(string texto, CancellationToken ct = default) { Telegram.Add(texto); return Task.FromResult(true); }
    }

    [Fact]
    public async Task Avisa_una_vez_al_dia_a_partir_de_la_hora_elegida_y_solo_si_hay_algo()
    {
        var db = BaseDeDatos.Nueva();
        var hoy = new DateTime(2026, 11, 7, 7, 30, 0);
        var r = new Receta { Nombre = "Lentejas" }; db.Recetas.Add(r); await db.SaveChangesAsync();
        db.MenuDias.AddRange(new MenuDia { Fecha = DateOnly.FromDateTime(hoy), Momento = "comida", RecetaId = r.Id }, new MenuDia { Fecha = DateOnly.FromDateTime(hoy), Momento = "cena", Texto = "Fuera de casa" });
        await db.SaveChangesAsync();
        var av = new AvisosFalsos();

        await CocinaServicio.AvisarHoy(db, av, hoy.AddHours(2), default);                              // sin hora elegida: nada
        Assert.Empty(av.Telegram);
        await CocinaServicio.GuardarAjustes(db, new AjustesCocina([], "09:00"), default);
        await CocinaServicio.AvisarHoy(db, av, hoy, default);                                          // son las 7:30: aún no
        Assert.Empty(av.Telegram);
        await CocinaServicio.AvisarHoy(db, av, hoy.AddHours(2), default);                              // 9:30: toca
        await CocinaServicio.AvisarHoy(db, av, hoy.AddHours(3), default);                              // y no repite
        Assert.Single(av.Telegram);
        Assert.Contains("Lentejas", av.Telegram[0]); Assert.Contains("Fuera de casa", av.Telegram[0]);
    }
}

public class CocinaApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    private static async Task<int> Receta(HttpClient c, string nombre, object[] ingredientes, string etiquetas = "")
    {
        var r = await c.PostAsJsonAsync("/api/cocina/recetas", new { nombre, raciones = 2, minutos = 30, etiquetas, pasos = "Cortar\nCocinar", ingredientes });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Receta_menu_generado_y_lista_de_la_compra_sin_repetir_lo_pendiente()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/cocina/recetas", new { nombre = "" })).StatusCode);
        var a = await Receta(c, "Pasta con tomate", [new { nombre = "pasta", cantidad = 200.0, unidad = "g" }, new { nombre = "tomate", cantidad = 400.0, unidad = "g" }], "rápida");
        var b = await Receta(c, "Tortilla", [new { nombre = "huevos", cantidad = 4.0, unidad = "" }, new { nombre = "tomate", cantidad = 100.0, unidad = "g" }], "rápida");

        var lunes = "2026-11-02";
        var gen = await c.PostAsJsonAsync("/api/cocina/menu/generar", new { desde = lunes, etiquetas = new[] { "rápida" } });
        Assert.Equal(HttpStatusCode.OK, gen.StatusCode);
        Assert.Equal(14, (await gen.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("puestas").GetInt32());
        var menu = await c.GetFromJsonAsync<JsonElement>($"/api/cocina/menu?desde={lunes}");
        Assert.Equal(14, menu.GetProperty("celdas").GetArrayLength());

        // cambiar una casilla a mano y quitarla
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/cocina/menu/{lunes}/cena", new { texto = "Cena fuera" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/cocina/menu/{lunes}/desayuno", new { texto = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/cocina/menu/{lunes}/cena", new { recetaId = 99999 })).StatusCode);

        var compra = await (await c.PostAsync($"/api/cocina/menu/compra?desde={lunes}", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(compra.GetProperty("añadidos").GetInt32() >= 3);
        var otra = await (await c.PostAsync($"/api/cocina/menu/compra?desde={lunes}", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, otra.GetProperty("añadidos").GetInt32());                                      // ya está todo pendiente: no se repite
        Assert.True(otra.GetProperty("yaEstaban").GetInt32() >= 3);

        // una receta borrada no deja el menú cojo: pasa a texto
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/cocina/recetas/{a}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/cocina/recetas/{b}")).StatusCode);
        var tras = await c.GetFromJsonAsync<JsonElement>($"/api/cocina/menu?desde={lunes}");
        Assert.Contains(tras.GetProperty("celdas").EnumerateArray(), x => x.GetProperty("texto").GetString() == "Pasta con tomate");
    }

    [Fact]
    public async Task Las_alergias_se_guardan_y_no_se_genera_si_nada_cumple_y_desde_web_rechaza_la_red_local()
    {
        var c = await factory.Dentro();
        var a = await (await c.PutAsJsonAsync("/api/cocina/ajustes", new { alergias = new[] { " Nueces ", "nueces", "" }, horaAviso = "09:30" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, a.GetProperty("alergias").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/cocina/ajustes", new { horaAviso = "25:00" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/cocina/menu/generar", new { desde = "2026-12-07", etiquetas = new[] { "inexistente" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/cocina/recetas/desde-web", new { url = "http://192.168.1.10/receta" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/cocina/recetas/desde-web", new { url = "file:///etc/passwd" })).StatusCode);
    }

    [Fact]
    public async Task Hace_falta_entrar()
    {
        using var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/cocina/recetas")).StatusCode);
    }
}
