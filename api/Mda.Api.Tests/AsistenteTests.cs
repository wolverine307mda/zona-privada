using System.Net.Http.Json;
using System.Text.Json;
using Mda.Api.Agenda;
using Mda.Api.Asistente;
using Mda.Api.Gastos;

namespace Mda.Api.Tests;

public class AsistenteTests(ModulosFactory factory) : IClassFixture<ModulosFactory>
{
    // miércoles 28 de octubre de 2026, a mediodía
    private static readonly DateTime Ahora = new(2026, 10, 28, 12, 0, 0);
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(Ahora);
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    private static Contexto Ctx(Dictionary<string, string>? aprendido = null, params HuchaConocida[] huchas) =>
        new(Ahora, Madrid, aprendido ?? [], huchas);

    private static Propuesta E(string t, Contexto? c = null) => Interprete.Entender(t, c ?? Ctx());

    [Theory]
    [InlineData("ayer 12 € en el Mercadona", 12, "supermercado", "Mercadona", -1)]
    [InlineData("12,50€ cena con Laura", 12.5, "restaurantes", "Cena con Laura", 0)]
    [InlineData("me he gastado 30 euros en el cine anteayer", 30, "ocio", "Cine", -2)]
    [InlineData("pagué 1.200 € de alquiler", 1200, "vivienda", "Alquiler", 0)]
    [InlineData("zapatillas nike 89,99", 89.99, "ropa", "Zapatillas nike", 0)]
    [InlineData("el lunes 4 euros con 50 de café", 4.5, "restaurantes", "Café", -2)]
    [InlineData("taxi 15€ el 3", 15, "transporte", "Taxi", -25)]
    [InlineData("farmacia 8€ el 20 de septiembre", 8, "salud", "Farmacia", -38)]
    [InlineData("gasto de 20 € en algo raro", 20, "otros", "Algo raro", 0)]
    [InlineData("taxi 15,40 € al aeropuerto", 15.4, "transporte", "Taxi al aeropuerto", 0)]
    public void Entiende_gastos(string frase, double importe, string categoria, string concepto, int dias)
    {
        var p = E(frase);
        Assert.Equal("movimiento", p.Clase);
        var m = p.Movimiento!;
        Assert.Equal(TipoMovimiento.Gasto, m.Tipo);
        Assert.Equal((decimal)importe, m.Importe);
        Assert.Equal(categoria, m.Categoria);
        Assert.Equal(concepto, m.Concepto);
        Assert.Equal(Hoy.AddDays(dias), m.Fecha);
        Assert.Null(m.Repite);
    }

    [Theory]
    [InlineData("cobré la nómina 1.450 €", 1450, "nomina", "Nómina")]
    [InlineData("me han hecho un bizum de 20 € de Pablo", 20, "extra", "Bizum de Pablo")]
    [InlineData("vendí la bici en wallapop por 120 €", 120, "ventas", "Bici en wallapop")]
    [InlineData("me devolvieron 35€ de Amazon", 35, "otros-ingresos", "Amazon")]
    public void Entiende_ingresos(string frase, double importe, string categoria, string concepto)
    {
        var m = E(frase).Movimiento!;
        Assert.Equal(TipoMovimiento.Ingreso, m.Tipo);
        Assert.Equal((decimal)importe, m.Importe);
        Assert.Equal(categoria, m.Categoria);
        Assert.Equal(concepto, m.Concepto);
    }

    [Fact]
    public void Lo_que_se_repite_y_puede_variar()
    {
        var luz = E("luz unos 60 € cada mes").Movimiento!;
        Assert.Equal(Frecuencia.Mensual, luz.Repite);
        Assert.True(luz.Variable);
        Assert.Equal("suministros", luz.Categoria);
        Assert.Equal("Luz", luz.Concepto);
        var seguro = E("seguro del coche 420 € al año").Movimiento!;
        Assert.Equal(Frecuencia.Anual, seguro.Repite);
        Assert.False(seguro.Variable);
        Assert.Equal("seguro", seguro.Categoria);
        Assert.Equal(Frecuencia.Mensual, E("netflix 13,99 al mes").Movimiento!.Repite);
    }

    [Fact]
    public void Un_repostaje_con_litros_y_kilometros()
    {
        var m = E("gasolina 50 € 32,5 litros a los 123.456 km").Movimiento!;
        Assert.Equal("gasolina", m.Categoria);
        Assert.Equal(50m, m.Importe);
        Assert.Equal(32.5m, m.Litros);
        Assert.Equal(123456, m.Kilometros);
        Assert.Equal(1.538m, m.PrecioLitro);
        Assert.Equal("Gasolina", m.Concepto);
    }

    [Fact]
    public void Aprende_la_categoria_que_tu_sueles_poner()
    {
        var c = Ctx(new() { ["la tasca de pepe"] = "restaurantes", ["basic fit"] = "salud" });
        Assert.Equal("restaurantes", E("22 € la tasca de pepe", c).Movimiento!.Categoria);
        Assert.Equal("salud", E("basic fit 29,99 al mes", c).Movimiento!.Categoria);
    }

    [Theory]
    [InlineData("recuérdame mañana a las 9 llamar al taller", "Llamar al taller", 2026, 10, 29, 9, 0)]
    [InlineData("recuérdame el viernes a las 5 de la tarde recoger el paquete", "Recoger el paquete", 2026, 10, 30, 17, 0)]
    [InlineData("recuérdame pagar 30 € a Juan el 2 de noviembre", "Pagar 30 € a Juan", 2026, 11, 2, 9, 0)]
    [InlineData("avísame a las 18:30 de sacar la basura", "Sacar la basura", 2026, 10, 28, 18, 30)]
    [InlineData("recuérdame a las 10 y media regar las plantas", "Regar las plantas", 2026, 10, 29, 10, 30)]
    [InlineData("recuérdame esta noche tomar la pastilla", "Tomar la pastilla", 2026, 10, 28, 21, 0)]
    [InlineData("recuérdame el lunes por la mañana renovar el DNI", "Renovar el DNI", 2026, 11, 2, 9, 0)]
    public void Entiende_recordatorios(string frase, string titulo, int a, int m, int d, int h, int min)
    {
        var p = E(frase);
        Assert.Equal("agenda", p.Clase);
        var e = p.Evento!;
        Assert.Equal(TipoEvento.Recordatorio, e.Tipo);
        Assert.Equal(titulo, e.Titulo);
        Assert.Equal(new DateTime(a, m, d, h, min, 0), TimeZoneInfo.ConvertTime(e.Inicio, Madrid).DateTime);
        Assert.True(e.PorPush);
        Assert.True(e.PorCorreo);
        Assert.Equal(0, e.AvisoMinutos);
    }

    [Fact]
    public void Recordatorio_en_un_rato()
    {
        var e = E("recuérdame en 20 minutos sacar la pizza del horno").Evento!;
        Assert.Equal("Sacar la pizza del horno", e.Titulo);
        Assert.Equal(Ahora.AddMinutes(20), TimeZoneInfo.ConvertTime(e.Inicio, Madrid).DateTime);
        var sin = E("recuérdame comprar pan");
        Assert.Equal(new DateTime(2026, 10, 29, 9, 0, 0), TimeZoneInfo.ConvertTime(sin.Evento!.Inicio, Madrid).DateTime);
        Assert.NotEmpty(sin.Dudas!);
    }

    [Fact]
    public void Entiende_eventos_con_hora_todo_el_dia_y_repetidos()
    {
        var dentista = E("dentista el jueves a las 16:00").Evento!;
        Assert.Equal(TipoEvento.Evento, dentista.Tipo);
        Assert.Equal("Dentista", dentista.Titulo);
        Assert.Equal(new DateTime(2026, 10, 29, 16, 0, 0), TimeZoneInfo.ConvertTime(dentista.Inicio, Madrid).DateTime);
        Assert.Equal(dentista.Inicio.AddHours(1), dentista.Fin);
        Assert.Equal(30, dentista.AvisoMinutos);

        var cumple = E("cumple de Ana el 14 de febrero").Evento!;
        Assert.True(cumple.TodoElDia);
        Assert.Equal("Cumple de Ana", cumple.Titulo);
        Assert.Equal(new DateTime(2027, 2, 14), TimeZoneInfo.ConvertTime(cumple.Inicio, Madrid).DateTime);

        var gym = E("gimnasio todos los lunes a las 7 de la tarde").Evento!;
        Assert.Equal(Repeticion.Semanal, gym.Repetir);
        Assert.Equal(new DateTime(2026, 11, 2, 19, 0, 0), TimeZoneInfo.ConvertTime(gym.Inicio, Madrid).DateTime);
        Assert.Equal("Gimnasio", gym.Titulo);
    }

    [Fact]
    public void Huchas_meter_y_sacar()
    {
        var c = Ctx(null, new HuchaConocida(1, "Viaje a Japón", "✈️"), new HuchaConocida(2, "Colchón", "🛟"));
        var a = E("50 € a la hucha de Japón", c);
        Assert.Equal("hucha", a.Clase);
        Assert.Equal(1, a.Hucha!.Id);
        Assert.Equal(50m, a.Hucha.Importe);
        Assert.Equal(-20m, E("saco 20 euros del colchón", c).Hucha!.Importe);
        Assert.Equal(2, E("aparto 100 para el colchón", c).Hucha!.Id);
        Assert.Equal("nada", E("mete 10 en la hucha", c).Clase); // con dos huchas, pregunta cuál
        // gastar en algo que se llama como una hucha no es meter en la hucha
        Assert.Equal("movimiento", E("gasté 40 € en el tren a Japón", c).Clase);
    }

    [Theory]
    [InlineData("¿cuánto llevo gastado en restaurantes?", QuePregunta.Gastado, "restaurantes", null, "este mes")]
    [InlineData("cuánto he gastado el mes pasado", QuePregunta.Gastado, null, null, "el mes pasado")]
    [InlineData("cuanto he gastado en mercadona esta semana", QuePregunta.Gastado, "supermercado", "mercadona", "esta semana")]
    [InlineData("¿cuánto tengo?", QuePregunta.Saldo, null, null, "ahora")]
    [InlineData("cuánto he cobrado este año", QuePregunta.Ingresado, null, null, "este año")]
    public void Entiende_preguntas(string frase, QuePregunta que, string? categoria, string? concepto, string periodo)
    {
        var p = E(frase);
        Assert.Equal("pregunta", p.Clase);
        Assert.Equal(que, p.Pregunta!.Que);
        Assert.Equal(categoria, p.Pregunta.Categoria);
        Assert.Equal(concepto, p.Pregunta.Concepto);
        Assert.Equal(periodo, p.Pregunta.Periodo);
    }

    [Theory]
    [InlineData("¿qué tiempo hace?", 0)]
    [InlineData("¿va a llover mañana?", 1)]
    [InlineData("hará frío el sábado", 3)]
    [InlineData("cuántos grados hace esta mañana", 0)]
    public void Entiende_preguntas_del_tiempo(string frase, int dias)
    {
        var p = E(frase);
        Assert.Equal("pregunta", p.Clase);
        Assert.Equal(QuePregunta.Tiempo, p.Pregunta!.Que);
        Assert.Equal(Hoy.AddDays(dias), p.Pregunta.Desde);
        // pero coger el paraguas es algo que recordar
        Assert.Equal("agenda", E("recuérdame mañana coger el paraguas").Clase);
    }

    [Theory]
    [InlineData("hola")]
    [InlineData("")]
    [InlineData("mercadona")]
    public void Lo_que_no_entiende_lo_dice(string frase) => Assert.Equal("nada", E(frase).Clase);

    [Fact]
    public async Task El_endpoint_aprende_de_tus_apuntes_y_contesta_preguntas()
    {
        var c = await factory.Dentro();
        var hoy = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        await c.PostAsJsonAsync("/api/gastos/movimientos", new { tipo = "Gasto", fecha = hoy, importe = 18, categoria = "ocio", concepto = "Bolos Kiko" });
        var p = await (await c.PostAsJsonAsync("/api/asistente/entender", new { texto = "bolos kiko 25 €" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("movimiento", p.GetProperty("clase").GetString());
        Assert.Equal("ocio", p.GetProperty("movimiento").GetProperty("categoria").GetString());

        var r = await (await c.PostAsJsonAsync("/api/asistente/entender", new { texto = "¿cuánto llevo en bolos kiko?" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pregunta", r.GetProperty("clase").GetString());
        Assert.Contains("18,00 €", r.GetProperty("respuesta").GetString());
        Assert.Contains("Bolos Kiko", r.GetProperty("respuesta").GetString());
    }
}
