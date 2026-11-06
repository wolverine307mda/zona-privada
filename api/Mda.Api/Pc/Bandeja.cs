namespace Mda.Api.Pc;

// La bandeja de entrada del PC: lo que ha ido cayendo en la carpeta «Zona privada\Bandeja» (la vigila el agente de Windows).
// Se guardan los 50 últimos en memoria: sirve para ver «qué ha llegado» y no para archivar (los archivos están en el PC); al reiniciar la API la lista empieza de cero.
public record ElementoBandeja(int Id, string Texto, DateTimeOffset Fecha);

public sealed class BandejaPc(TimeProvider reloj)
{
    public const int Maximo = 50;
    private readonly List<ElementoBandeja> _lista = [];
    private readonly Lock _cerrojo = new();
    private int _siguiente;

    public ElementoBandeja Añadir(string texto)
    {
        lock (_cerrojo)
        {
            var e = new ElementoBandeja(++_siguiente, texto, reloj.GetUtcNow());
            _lista.Insert(0, e);
            if (_lista.Count > Maximo) _lista.RemoveRange(Maximo, _lista.Count - Maximo);
            return e;
        }
    }

    public IReadOnlyList<ElementoBandeja> Lista() { lock (_cerrojo) return [.. _lista]; }

    public bool Quitar(int id) { lock (_cerrojo) return _lista.RemoveAll(x => x.Id == id) > 0; }

    public void Vaciar() { lock (_cerrojo) _lista.Clear(); }
}

public static class BandejaEndpoints
{
    public static void MapBandeja(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/pc/bandeja").RequireAuthorization();
        g.MapGet("/", (BandejaPc b) => Results.Ok(b.Lista()));
        g.MapDelete("/{id:int}", (int id, BandejaPc b) => b.Quitar(id) ? Results.NoContent() : Results.NotFound());
        g.MapDelete("/", (BandejaPc b) => { b.Vaciar(); return Results.NoContent(); });
    }
}
