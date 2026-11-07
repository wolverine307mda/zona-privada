using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Domotica;

// Une los drivers con la base de datos: refresca el estado de los aparatos, guarda el histórico y reenvía las órdenes.
public sealed class ServicioCasa(IEnumerable<IDriverCasa> drivers, TimeProvider reloj, Mda.Api.Agenda.Zona zona)
{
    public static readonly TimeSpan CadaLectura = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Historia = TimeSpan.FromDays(30);

    public IDriverCasa? Driver(string id) => drivers.FirstOrDefault(d => d.Id == id);

    // Lee el aparato y apunta cada dato (y, si es un número, una lectura de histórico cada 5 minutos como mucho)
    public async Task<bool> Refrescar(AppDbContext db, Aparato a, CancellationToken ct)
    {
        if (Driver(a.Driver) is not { } d) return false;
        IReadOnlyDictionary<string, string> datos;
        try { datos = await d.Leer(a, ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
        if (datos.Count == 0) return false;

        var ahora = reloj.GetUtcNow();
        var entidades = await db.Entidades.Where(e => e.AparatoId == a.Id).ToListAsync(ct);
        foreach (var (clave, valor) in datos)
        {
            if (Catalogo.Campos(a.Tipo).All(c => c.Clave != clave)) continue;
            var e = entidades.FirstOrDefault(x => x.Clave == clave);
            if (e is null) { e = new Entidad { AparatoId = a.Id, Clave = clave }; db.Entidades.Add(e); await db.SaveChangesAsync(ct); entidades.Add(e); }
            e.Valor = valor;
            e.Actualizado = ahora;
            if (double.TryParse(valor, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n))
            {
                var ultima = await db.Lecturas.Where(l => l.EntidadId == e.Id).OrderByDescending(l => l.Fecha).Select(l => (DateTimeOffset?)l.Fecha).FirstOrDefaultAsync(ct);
                if (ultima is null || ahora - ultima >= CadaLectura) db.Lecturas.Add(new LecturaCasa { EntidadId = e.Id, Valor = n, Fecha = ahora });
            }
        }
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<(bool Ok, string Mensaje, int Codigo)> Ejecutar(AppDbContext db, Aparato a, string clave, string? bruto, CancellationToken ct)
    {
        if (Catalogo.Buscar(a.Tipo, clave) is not { } campo) return (false, $"Este aparato no tiene «{clave}».", 400);
        var (valor, error) = Catalogo.Normalizar(campo, bruto);
        if (valor is null) return (false, error ?? "Valor no válido.", 400);
        if (Driver(a.Driver) is not { } d) return (false, $"No hay conector «{a.Driver}».", 409);
        (bool Ok, string Mensaje) r;
        try { r = await d.Ejecutar(a, campo, valor, ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { return (false, "El aparato no contesta.", 502); }
        if (!r.Ok) return (false, r.Mensaje, 409);
        // se apunta ya el nuevo valor para que la pantalla no espere a la siguiente lectura
        var e = await db.Entidades.FirstOrDefaultAsync(x => x.AparatoId == a.Id && x.Clave == clave, ct);
        if (e is null) { e = new Entidad { AparatoId = a.Id, Clave = clave }; db.Entidades.Add(e); }
        e.Valor = valor;
        e.Actualizado = reloj.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return (true, r.Mensaje, 200);
    }

    // borra las lecturas de más de 30 días
    public async Task Compactar(AppDbContext db, CancellationToken ct)
    {
        // antes de borrar, el resumen por días de lo que ya se cerró (para el clima de meses atrás)
        await ClimaCasa.Resumir(db, reloj.GetUtcNow(), zona.Info, ct);
        var limite = reloj.GetUtcNow() - Historia;
        db.Lecturas.RemoveRange(await db.Lecturas.Where(l => l.Fecha < limite).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }
}

// Cada minuto refresca todos los aparatos (los de ejemplo y, más adelante, los reales)
public sealed class VigilanteCasa(IServiceScopeFactory ambitos, ServicioCasa casa, ILogger<VigilanteCasa> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var vuelta = 0;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var scope = ambitos.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    foreach (var a in await db.Aparatos.AsNoTracking().ToListAsync(ct)) await casa.Refrescar(db, a, ct);
                    if (++vuelta % 60 == 0) await casa.Compactar(db, ct);
                }
                catch (Exception e) when (!ct.IsCancellationRequested) { log.LogWarning(e, "No se pudo refrescar la casa"); }
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
            }
        }
        catch (OperationCanceledException) { /* se apaga la API */ }
    }
}
