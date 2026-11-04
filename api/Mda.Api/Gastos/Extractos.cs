using System.Globalization;
using System.Text;
using Mda.Api.Agenda;
using Mda.Api.Asistente;
using Mda.Api.Atajos;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Gastos;

public record MapeoExtracto(int Fecha, int Concepto, int? Importe, int? Cargo, int? Abono);
public record FilaExtracto(DateOnly Fecha, string Concepto, decimal Importe, string Tipo, string Categoria, bool Duplicado);
public record AnalisisExtracto(string Separador, IReadOnlyList<string> Columnas, MapeoExtracto? Mapeo, IReadOnlyList<FilaExtracto> Filas, IReadOnlyList<string> Errores);
public record AnalizarEntrada(string? Texto, MapeoExtracto? Mapeo);
public record FilaImportar(string Fecha, string Concepto, decimal Importe, string Tipo, string Categoria);
public record ImportarEntrada(List<FilaImportar>? Filas);

// Importar el extracto del banco (CSV): se detecta el separador y las columnas, se ve una vista previa con la categoría que sugieren tus reglas
// y los duplicados marcados, y solo entra lo que confirmas.
public static class Extractos
{
    public const int MaximoFilas = 2000;
    private static readonly string[] Formatos = ["dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd", "dd/MM/yy", "d/M/yy", "dd.MM.yyyy", "yyyy/MM/dd"];

    public static char Separador(string texto)
    {
        var cab = texto.Split('\n', 2)[0];
        return new[] { ';', '\t', ',' }.OrderByDescending(c => cab.Count(x => x == c)).First();
    }

    // CSV con comillas ("a;b" dentro de un campo, "" como comilla escapada)
    public static List<List<string>> Parsear(string texto, char sep)
    {
        var filas = new List<List<string>>();
        var fila = new List<string>();
        var campo = new StringBuilder();
        var entre = false;
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (entre)
            {
                if (c == '"' && i + 1 < texto.Length && texto[i + 1] == '"') { campo.Append('"'); i++; }
                else if (c == '"') entre = false;
                else campo.Append(c);
            }
            else if (c == '"') entre = true;
            else if (c == sep) { fila.Add(campo.ToString().Trim()); campo.Clear(); }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n') i++;
                fila.Add(campo.ToString().Trim()); campo.Clear();
                if (fila.Any(x => x.Length > 0)) filas.Add(fila);
                fila = [];
            }
            else campo.Append(c);
        }
        fila.Add(campo.ToString().Trim());
        if (fila.Any(x => x.Length > 0)) filas.Add(fila);
        return filas;
    }

    // «1.234,56», «-12,50», «12.50», «1,234.56» → número (null si no lo es)
    public static decimal? Importe(string s)
    {
        s = s.Replace("€", "").Replace("EUR", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "").Replace(" ", "").Trim();
        if (s.Length == 0) return null;
        var neg = s.StartsWith('-') || (s.StartsWith('(') && s.EndsWith(')'));
        s = s.Trim('-', '+', '(', ')');
        var coma = s.LastIndexOf(','); var punto = s.LastIndexOf('.');
        if (coma >= 0 && punto >= 0) s = coma > punto ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
        else if (coma >= 0) s = s.Replace(',', '.');
        else if (punto >= 0 && System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d{1,3}(\.\d{3})+$")) s = s.Replace(".", "");
        return decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? (neg ? -v : v) : null;
    }

    public static DateOnly? Fecha(string s)
    {
        s = s.Trim();
        if (s.Length > 10 && char.IsDigit(s[0])) s = s.Split(' ', 'T')[0];
        return DateOnly.TryParseExact(s, Formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;
    }

    private static string Plano(string s) => Interprete.Aplanar(s);

    // busca en la cabecera las columnas por su nombre
    public static MapeoExtracto? Detectar(IReadOnlyList<string> cab)
    {
        int Buscar(params string[] claves) { for (var i = 0; i < cab.Count; i++) { var c = Plano(cab[i]); if (claves.Any(k => c.Contains(k))) return i; } return -1; }
        var fecha = Buscar("fecha operacion", "f. operacion", "fecha", "date");
        var concepto = Buscar("concepto", "descripcion", "detalle", "movimiento", "description", "comercio");
        var importe = Buscar("importe", "cantidad", "amount");
        var cargo = Buscar("cargo", "debe", "debit");
        var abono = Buscar("abono", "haber", "credit");
        if (fecha < 0 || concepto < 0) return null;
        if (importe >= 0) return new MapeoExtracto(fecha, concepto, importe, null, null);
        return cargo >= 0 || abono >= 0 ? new MapeoExtracto(fecha, concepto, null, cargo >= 0 ? cargo : null, abono >= 0 ? abono : null) : null;
    }

    public static (List<FilaExtracto> Filas, List<string> Errores) Leer(List<List<string>> filas, MapeoExtracto m, IReadOnlyDictionary<string, string> aprendido, Func<DateOnly, string, decimal, bool> existe)
    {
        var res = new List<FilaExtracto>(); var errores = new List<string>();
        for (var n = 1; n < filas.Count && res.Count < MaximoFilas; n++)
        {
            var f = filas[n];
            string C(int? i) => i is { } k && k >= 0 && k < f.Count ? f[k] : "";
            var fecha = Fecha(C(m.Fecha));
            var concepto = Limpiar(C(m.Concepto));
            decimal? importe = m.Importe is not null ? Importe(C(m.Importe))
                : Importe(C(m.Abono)) is { } ab && ab != 0 ? Math.Abs(ab) : Importe(C(m.Cargo)) is { } ca && ca != 0 ? -Math.Abs(ca) : null;
            if (fecha is null || importe is null || importe == 0 || concepto.Length == 0) { errores.Add($"Fila {n + 1}: no se entiende ({string.Join(" | ", f.Take(4))})"); continue; }
            var gasto = importe < 0;
            var abs = Math.Abs(importe.Value);
            var cat = gasto ? AtajosApi.Categoria(concepto, aprendido) : "otros-ingresos";
            res.Add(new FilaExtracto(fecha.Value, concepto, abs, gasto ? "Gasto" : "Ingreso", cat, existe(fecha.Value, concepto, abs)));
        }
        return (res, errores);
    }

    private static string Limpiar(string s) { var t = System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s+", " "); return t.Length > 120 ? t[..120] : t; }

    public static void MapExtractos(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/gastos/importar").RequireAuthorization();

        g.MapPost("/analizar", async (AnalizarEntrada e, AppDbContext db, TimeProvider reloj, Zona zona, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(e.Texto) || e.Texto.Length > 2_000_000) return Results.ValidationProblem(new Dictionary<string, string[]> { ["texto"] = ["El archivo está vacío o es demasiado grande."] });
            var sep = Separador(e.Texto);
            var filas = Parsear(e.Texto, sep);
            if (filas.Count < 2) return Results.ValidationProblem(new Dictionary<string, string[]> { ["texto"] = ["No veo filas de datos: ¿es un CSV con cabecera?"] });
            var mapeo = e.Mapeo ?? Detectar(filas[0]);
            if (mapeo is null) return Results.Ok(new AnalisisExtracto(sep.ToString(), filas[0], null, [], ["No he encontrado las columnas de fecha, concepto e importe: elígelas a mano."]));
            var ctx = await AsistenteEndpoints.ContextoDe(db, reloj, zona, ct);
            // duplicados: misma fecha, importe y concepto ya apuntados
            var fechas = filas.Skip(1).Select(f => mapeo.Fecha < f.Count ? Fecha(f[mapeo.Fecha]) : null).Where(x => x is not null).Select(x => x!.Value).ToList();
            var existentes = fechas.Count == 0 ? [] : (await db.Movimientos.AsNoTracking().Where(m => m.Fecha >= fechas.Min() && m.Fecha <= fechas.Max()).Select(m => new { m.Fecha, m.Concepto, m.Importe }).ToListAsync(ct))
                .Select(m => (m.Fecha, Plano(m.Concepto), m.Importe)).ToHashSet();
            var (lista, errores) = Leer(filas, mapeo, ctx.Aprendido, (f, c, i) => existentes.Contains((f, Plano(c), i)));
            return Results.Ok(new AnalisisExtracto(sep.ToString(), filas[0], mapeo, lista, errores.Take(20).ToList()));
        });

        g.MapPost("/", async (ImportarEntrada e, AppDbContext db, CancellationToken ct) =>
        {
            var filas = e.Filas ?? [];
            if (filas.Count == 0 || filas.Count > MaximoFilas) return Results.ValidationProblem(new Dictionary<string, string[]> { ["filas"] = [$"Entre 1 y {MaximoFilas} filas."] });
            var nuevos = new List<Movimiento>();
            foreach (var f in filas)
            {
                var tipo = f.Tipo == "Ingreso" ? TipoMovimiento.Ingreso : f.Tipo == "Gasto" ? TipoMovimiento.Gasto : (TipoMovimiento?)null;
                if (tipo is null || !DateOnly.TryParseExact(f.Fecha, "yyyy-MM-dd", out var fecha) || f.Importe <= 0 || f.Importe > 1_000_000 || string.IsNullOrWhiteSpace(f.Concepto) || !Categorias.Valida(f.Categoria, tipo.Value))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["filas"] = [$"Hay una fila no válida: {f.Concepto}"] });
                nuevos.Add(new Movimiento { Tipo = tipo.Value, Fecha = fecha, Importe = Math.Round(f.Importe, 2), Categoria = f.Categoria, Concepto = f.Concepto.Trim(), Notas = "Importado de extracto" });
            }
            db.Movimientos.AddRange(nuevos);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { importados = nuevos.Count });
        });
    }
}
