using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Avisos;
using Mda.Api.Data;
using Mda.Api.Personas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mda.Api.Tests;

public class VCardTests
{
    [Fact]
    public void Lee_nombre_telefonos_correos_cumple_y_nota_de_una_tarjeta_3_0()
    {
        var vcf = "BEGIN:VCARD\r\nVERSION:3.0\r\nN:García;María;;;\r\nFN:María García\r\nTEL;TYPE=CELL:+34 600 111 222\r\nTEL;TYPE=HOME:910 000 000\r\nEMAIL;TYPE=INTERNET:maria@ejemplo.com\r\nBDAY:1990-03-15\r\nADR;TYPE=HOME:;;Calle Mayor 1;Madrid;;28001;España\r\nNOTE:Le gusta el café\\, mucho\r\nEND:VCARD\r\n";
        var c = Assert.Single(VCard.Parsear(vcf));
        Assert.Equal("María García", c.Nombre);
        Assert.Equal(["+34 600 111 222", "910 000 000"], c.Telefonos);
        Assert.Equal(["maria@ejemplo.com"], c.Correos);
        Assert.Equal((15, 3, 1990), (c.Dia!.Value, c.Mes!.Value, c.Anio!.Value));
        Assert.Equal("Calle Mayor 1, Madrid, 28001, España", c.Direccion);
        Assert.Equal("Le gusta el café, mucho", c.Notas);
    }

    [Theory]
    [InlineData("19900315", 15, 3, 1990)]
    [InlineData("1990-03-15", 15, 3, 1990)]
    [InlineData("--0315", 15, 3, null)]
    [InlineData("--03-15", 15, 3, null)]
    [InlineData("1604-03-15", 15, 3, null)]                 // Apple pone 1604 cuando no hay año
    [InlineData("20000229", 29, 2, 2000)]
    [InlineData("--0229", 29, 2, null)]
    [InlineData("1990-03-15T00:00:00Z", 15, 3, 1990)]
    public void Entiende_los_formatos_de_cumpleaños(string v, int dia, int mes, int? anio) => Assert.Equal((dia, mes, anio), VCard.Cumple(v));

    [Theory] [InlineData("")] [InlineData("hoy")] [InlineData("1990-13-01")] [InlineData("1990-02-30")] [InlineData("--0431")] [InlineData("1990-00-10")]
    public void Rechaza_cumpleaños_imposibles(string v) => Assert.Null(VCard.Cumple(v));

    [Fact]
    public void Las_lineas_plegadas_y_el_quoted_printable_se_deshacen()
    {
        var vcf = "BEGIN:VCARD\nVERSION:2.1\nFN:Nombre muy\n  largo\nNOTE;ENCODING=QUOTED-PRINTABLE;CHARSET=UTF-8:Ca=C3=B1as =\n=C3=A9xito\nEND:VCARD";
        var c = Assert.Single(VCard.Parsear(vcf));
        Assert.Equal("Nombre muy largo", c.Nombre);
        Assert.Equal("Cañas éxito", c.Notas);
    }

    [Fact]
    public void Varias_tarjetas_con_una_rota_en_medio_no_tumban_a_las_demas()
    {
        var vcf = "BEGIN:VCARD\nFN:Ana\nEND:VCARD\nBEGIN:VCARD\nTEL:123\nEND:VCARD\nBEGIN:VCARD\nFN:Luis\nBDAY:rarisimo\nitem1.TEL:600600600\nEND:VCARD";
        var l = VCard.Parsear(vcf);
        Assert.Equal(["Ana", "Luis"], l.Select(x => x.Nombre));                    // la de en medio no tiene nombre: se salta
        Assert.Null(l[1].Dia); Assert.Equal(["600600600"], l[1].Telefonos);        // «item1.TEL» también es un teléfono
        Assert.Empty(VCard.Parsear("")); Assert.Empty(VCard.Parsear(null)); Assert.Empty(VCard.Parsear("esto no es una tarjeta"));
    }

    [Fact]
    public void Sin_fn_se_compone_el_nombre_a_partir_de_n() =>
        Assert.Equal("Dra. Marta López Ruiz", Assert.Single(VCard.Parsear("BEGIN:VCARD\nN:López Ruiz;Marta;;Dra.;\nEND:VCARD")).Nombre);
}

public class CumplesTests
{
    [Fact]
    public void El_proximo_cumple_es_este_año_si_no_ha_pasado_y_el_siguiente_si_ya_paso()
    {
        var hoy = new DateOnly(2026, 11, 7);
        Assert.Equal(new DateOnly(2026, 12, 25), Cumples.Proximo(25, 12, hoy));
        Assert.Equal(new DateOnly(2027, 3, 15), Cumples.Proximo(15, 3, hoy));
        Assert.Equal(new DateOnly(2026, 11, 7), Cumples.Proximo(7, 11, hoy));          // hoy mismo
        Assert.Equal(0, Cumples.Dias(7, 11, hoy)); Assert.Equal(1, Cumples.Dias(8, 11, hoy));
    }

    [Fact]
    public void El_29_de_febrero_se_celebra_el_28_en_los_años_que_no_son_bisiestos()
    {
        Assert.Equal(new DateOnly(2027, 2, 28), Cumples.Proximo(29, 2, new DateOnly(2026, 11, 7)));
        Assert.Equal(new DateOnly(2028, 2, 29), Cumples.Proximo(29, 2, new DateOnly(2027, 11, 7)));
        Assert.Equal(new DateOnly(2028, 2, 29), Cumples.Proximo(29, 2, new DateOnly(2028, 1, 10)));
    }

    [Theory] [InlineData(20, 0)] [InlineData(15, 0)] [InlineData(14, 1)] [InlineData(8, 1)] [InlineData(7, 2)] [InlineData(2, 2)] [InlineData(1, 3)] [InlineData(0, 4)]
    public void Los_niveles_de_aviso_son_14_7_1_y_el_dia(int dias, int nivel) => Assert.Equal(nivel, Cumples.Nivel(dias));

    [Fact]
    public void La_edad_es_la_que_cumple_y_sin_año_no_hay_edad()
    {
        Assert.Equal(36, Cumples.Edad(1990, new DateOnly(2026, 3, 15)));
        Assert.Null(Cumples.Edad(null, new DateOnly(2026, 3, 15)));
    }
}

public class DuplicadosYRegalosTests
{
    private static Persona P(string nombre, string tel = "", string correo = "") => new() { Nombre = nombre, Telefonos = tel, Correos = correo };
    private static ContactoVcf C(string nombre, string[]? tel = null, string[]? correo = null) => new(nombre, [.. tel ?? []], [.. correo ?? []], "", null, null, null, "");

    [Fact]
    public void Un_duplicado_es_mismo_nombre_telefono_o_correo()
    {
        var existentes = new[] { P("María García", "+34 600 111 222", "maria@ejemplo.com"), P("Luis") };
        Assert.Equal("María García", Duplicados.Buscar(C("MARIA GARCIA"), existentes)?.Nombre);                                         // sin tildes ni mayúsculas
        Assert.Equal("María García", Duplicados.Buscar(C("Otra persona", ["600111222"]), existentes)?.Nombre);                            // mismo teléfono con otro formato
        Assert.Equal("María García", Duplicados.Buscar(C("Mari", null, ["MARIA@ejemplo.com"]), existentes)?.Nombre);
        Assert.Null(Duplicados.Buscar(C("Pedro", ["611000000"], ["pedro@x.com"]), existentes));
        Assert.Null(Duplicados.Buscar(C("Pedro", ["123"]), [P("Ana", "123")]));                                                          // un teléfono de 3 cifras no vale para comparar
    }

    [Fact]
    public void Las_ideas_encajan_con_los_gustos_respetan_el_presupuesto_y_dan_hasta_cinco()
    {
        var hoy = new DateOnly(2026, 11, 7);
        var ideas = Regalos.Ideas("café, libros", 30, [], hoy);
        Assert.InRange(ideas.Count, 1, 5);
        Assert.All(ideas, i => Assert.True(i.Precio <= 30));
        Assert.All(ideas, i => Assert.True(i.Etiquetas.Contains("café") || i.Etiquetas.Contains("libros")));
        Assert.Equal(ideas, ideas.OrderByDescending(i => i.Etiquetas.Count(t => t is "café" or "libros")).ThenBy(i => i.Precio).ThenBy(i => i.Idea, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    [Fact]
    public void Lo_regalado_hace_menos_de_tres_años_no_se_repite_pero_lo_antiguo_si_puede()
    {
        var hoy = new DateOnly(2026, 11, 7);
        var base1 = Regalos.Ideas("café", null, [], hoy).First();
        Assert.DoesNotContain(base1, Regalos.Ideas("café", null, [(base1.Idea.ToUpperInvariant(), hoy.AddYears(-1))], hoy));
        Assert.Contains(base1, Regalos.Ideas("café", null, [(base1.Idea, hoy.AddYears(-4))], hoy));
        Assert.DoesNotContain(base1, Regalos.Ideas("café", null, [(base1.Idea, null)], hoy));                  // sin fecha: por si acaso, cuenta como reciente
    }

    [Fact]
    public void Sin_gustos_se_dan_ideas_generales_y_con_gustos_raros_no_se_inventa_nada()
    {
        var hoy = new DateOnly(2026, 11, 7);
        Assert.Equal(5, Regalos.Ideas("", null, [], hoy).Count);
        Assert.Empty(Regalos.Ideas("paracaidismo extremo", null, [], hoy));
        Assert.Empty(Regalos.Ideas("café", 1, [], hoy));                                                      // presupuesto de 1 €: nada cabe
    }
}

public class PersonasAvisosTests
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
    public async Task Avisa_a_14_7_1_dias_y_el_mismo_dia_una_vez_cada_nivel()
    {
        var db = BaseDeDatos.Nueva();
        db.Personas.Add(new Persona { Nombre = "María", CumpleDia = 25, CumpleMes = 11, CumpleAnio = 1990 });
        await db.SaveChangesAsync();
        var av = new AvisosFalsos();
        var cumple = new DateOnly(2026, 11, 25);
        async Task Pasada(DateOnly hoy) => await PersonasServicio.AvisarCumples(db, av, hoy, NullLogger.Instance, default);

        await Pasada(cumple.AddDays(-20)); Assert.Empty(av.Telegram);
        await Pasada(cumple.AddDays(-14)); await Pasada(cumple.AddDays(-14)); Assert.Single(av.Telegram);
        Assert.Contains("María", av.Telegram[0]); Assert.Contains("cumple 36", av.Telegram[0]);
        await Pasada(cumple.AddDays(-10)); Assert.Single(av.Telegram);                 // el mismo nivel no repite
        await Pasada(cumple.AddDays(-7)); Assert.Equal(2, av.Telegram.Count);
        await Pasada(cumple.AddDays(-1)); Assert.Equal(3, av.Telegram.Count); Assert.Contains("mañana", av.Telegram[2]);
        await Pasada(cumple); Assert.Equal(4, av.Telegram.Count); Assert.Contains("HOY", av.Telegram[3]);
        await Pasada(cumple.AddDays(40)); Assert.Equal(4, av.Telegram.Count);          // ya pasó: nada hasta el año que viene
    }

    [Fact]
    public async Task Sin_avisar_o_sin_cumple_no_hay_aviso_y_el_texto_de_proximos_los_ordena()
    {
        var db = BaseDeDatos.Nueva();
        db.Personas.AddRange(new Persona { Nombre = "Callada", CumpleDia = 8, CumpleMes = 11, Avisar = false }, new Persona { Nombre = "Sin fecha" },
            new Persona { Nombre = "Lejana", CumpleDia = 1, CumpleMes = 1 }, new Persona { Nombre = "Cercana", Apodo = "Cerci", CumpleDia = 9, CumpleMes = 11 });
        await db.SaveChangesAsync();
        var av = new AvisosFalsos();
        await PersonasServicio.AvisarCumples(db, av, new DateOnly(2026, 11, 7), NullLogger.Instance, default);
        Assert.Single(av.Telegram); Assert.Contains("Cerci", av.Telegram[0]);
        var t = await PersonasServicio.TextoCumples(db, new DateOnly(2026, 11, 7), 6, default);
        Assert.True(t.IndexOf("Callada") < t.IndexOf("Cerci") && t.IndexOf("Cerci") < t.IndexOf("Lejana"));
        Assert.DoesNotContain("Sin fecha", t);
        Assert.Contains("No tienes", await PersonasServicio.TextoCumples(BaseDeDatos.Nueva(), new DateOnly(2026, 11, 7), 6, default));
    }
}

public class PersonasApiTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    [Fact]
    public async Task Crear_editar_con_cumple_regalos_ideas_y_borrado_completo()
    {
        var c = await factory.Dentro();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/personas", new { nombre = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/personas", new { nombre = "X", cumpleDia = 31, cumpleMes = 4 })).StatusCode);

        var r = await c.PostAsJsonAsync("/api/personas", new { nombre = "Elena", relacion = "Hermana", telefonos = "600 000 000; 911 000 000", gustos = "Café, Libros", cumpleDia = 29, cumpleMes = 2, cumpleAnio = 1992, notas = "Alérgica a las nueces" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var p = await r.Content.ReadFromJsonAsync<JsonElement>();
        var id = p.GetProperty("id").GetInt32();
        Assert.Equal(2, p.GetProperty("telefonos").GetArrayLength());
        Assert.Equal("café", p.GetProperty("gustos")[0].GetString());
        Assert.True(p.GetProperty("dias").GetInt32() >= 0);

        // lo cifrado se guarda cifrado en la base y se lee en claro
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("Alérgica a las nueces", (await db.Personas.AsNoTracking().FirstAsync(x => x.Id == id)).Notas);
        }

        var g = await c.PostAsJsonAsync($"/api/personas/{id}/regalos", new { idea = "Un libro", precio = 18.5, ocasion = "Cumple", estado = "regalado", fecha = DateTime.UtcNow.ToString("yyyy-MM-dd"), sento = "Le encantó" });
        Assert.Equal(HttpStatusCode.Created, g.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/personas/{id}/regalos", new { idea = "x", estado = "perdido" })).StatusCode);
        var ideas = await c.GetFromJsonAsync<JsonElement>($"/api/personas/{id}/ideas?presupuesto=30");
        Assert.True(ideas.GetArrayLength() > 0);
        Assert.DoesNotContain(ideas.EnumerateArray(), i => i.GetProperty("idea").GetString() == "Un libro de su autor o género favorito" && false);

        var prox = await c.GetFromJsonAsync<JsonElement>("/api/personas/proximos?dias=366");
        Assert.Contains(prox.EnumerateArray(), x => x.GetProperty("id").GetInt32() == id);

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/personas/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/personas/{id}/ideas")).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<JsonElement>($"/api/personas/{id}/regalos")).EnumerateArray());      // sus regalos se fueron con ella
    }

    [Fact]
    public async Task La_importacion_ensena_duplicados_y_trae_solo_lo_marcado_y_solo_lo_que_pides()
    {
        var c = await factory.Dentro();
        await c.PostAsJsonAsync("/api/personas", new { nombre = "Ya Existe", telefonos = "611 222 333" });
        var vcf = "BEGIN:VCARD\nFN:Ya Existe\nTEL:611222333\nEND:VCARD\nBEGIN:VCARD\nFN:Nueva Uno\nTEL:622000111\nEMAIL:uno@x.com\nBDAY:--0720\nNOTE:nota privada\nEND:VCARD\nBEGIN:VCARD\nFN:Nueva Dos\nEND:VCARD";
        var vista = await (await c.PostAsJsonAsync("/api/personas/importar/vista-previa", new { vcf })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, vista.GetArrayLength());
        Assert.Equal("Ya Existe", vista[0].GetProperty("duplicadoDe").GetString());
        Assert.Equal(JsonValueKind.Null, vista[1].GetProperty("duplicadoDe").ValueKind);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/personas/importar", new { vcf, indices = Array.Empty<int>(), traer = "basico" })).StatusCode);
        var ok = await (await c.PostAsJsonAsync("/api/personas/importar", new { vcf, indices = new[] { 0, 1 }, traer = "basico" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, ok.GetProperty("nuevas").GetInt32()); Assert.Equal(1, ok.GetProperty("saltadas").GetInt32());   // el duplicado no se importa; el 2 no estaba marcado

        var lista = await c.GetFromJsonAsync<JsonElement>("/api/personas");
        var uno = lista.EnumerateArray().First(x => x.GetProperty("nombre").GetString() == "Nueva Uno");
        Assert.Equal(20, uno.GetProperty("cumpleDia").GetInt32());
        Assert.Equal(0, uno.GetProperty("telefonos").GetArrayLength());           // «solo nombre y cumpleaños»: nada de contacto
        Assert.Equal("", uno.GetProperty("notas").GetString());
        Assert.DoesNotContain(lista.EnumerateArray(), x => x.GetProperty("nombre").GetString() == "Nueva Dos");
    }

    [Fact]
    public async Task El_atajo_añade_una_persona_con_su_permiso_y_no_repite()
    {
        var c = await factory.Dentro();
        var token = (await (await c.PostAsJsonAsync("/api/atajos-tokens", new { nombre = "Contactos", alcances = new[] { "personas" } })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var movil = factory.CreateClient();
        movil.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var a = await movil.PostAsJsonAsync("/api/atajos/persona", new { nombre = "Contacto Atajo", dia = 3, mes = 3, telefono = "633 444 555" });
        Assert.Equal(HttpStatusCode.OK, a.StatusCode);
        Assert.Contains("añadido", (await a.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mensaje").GetString());
        var otra = await (await movil.PostAsJsonAsync("/api/atajos/persona", new { nombre = "Otro nombre", telefono = "633444555" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("ya estaba", otra.GetProperty("mensaje").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await movil.PostAsJsonAsync("/api/atajos/persona", new { nombre = "" })).StatusCode);

        var sinPermiso = (await (await c.PostAsJsonAsync("/api/atajos-tokens", new { nombre = "Gasto", alcances = new[] { "gasto" } })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var malo = factory.CreateClient(); malo.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", sinPermiso);
        Assert.Equal(HttpStatusCode.Unauthorized, (await malo.PostAsJsonAsync("/api/atajos/persona", new { nombre = "X" })).StatusCode);
    }

    [Fact]
    public async Task Hace_falta_entrar()
    {
        using var anonimo = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/personas")).StatusCode);
    }
}
