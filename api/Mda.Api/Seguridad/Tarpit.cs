namespace Mda.Api.Seguridad;

// Tarpit: a quien pide una ruta de sonda (/.env, /wp-login.php…) se le contesta un 404 normal, pero la respuesta no termina:
// va soltando un byte cada pocos segundos hasta un minuto. Los escáneres que esperan la respuesta pierden ese tiempo.
// Para no gastar recursos propios hay un tope de conexiones simultáneas: si se llena, se contesta el 404 de golpe como siempre.
public sealed class Tarpit(TimeProvider reloj, int maximo = 20)
{
    public static readonly TimeSpan IntervaloNormal = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DuracionNormal = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _cupo = new(maximo, maximo);
    public int Activas => maximo - _cupo.CurrentCount;

    // true si ha atendido la petición (la respuesta ya está terminada); false si no había hueco y hay que contestar el 404 normal
    public async Task<bool> Gotear(HttpContext ctx, TimeSpan? intervalo = null, TimeSpan? duracion = null)
    {
        if (!await _cupo.WaitAsync(0)) return false;
        var ct = ctx.RequestAborted;
        try
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound; // idéntico a una ruta que no existe
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.StartAsync(ct);
            var fin = reloj.GetUtcNow() + (duracion ?? DuracionNormal);
            var espera = intervalo ?? IntervaloNormal;
            var byteSuelto = new byte[] { (byte)' ' };
            while (reloj.GetUtcNow() < fin && !ct.IsCancellationRequested)
            {
                await ctx.Response.Body.WriteAsync(byteSuelto, ct);
                await ctx.Response.Body.FlushAsync(ct);
                await Task.Delay(espera, reloj, ct);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or InvalidOperationException) { /* el escáner se cansó y colgó */ }
        finally { _cupo.Release(); }
        return true;
    }
}
