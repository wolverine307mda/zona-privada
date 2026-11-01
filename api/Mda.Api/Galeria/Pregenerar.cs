using System.Threading.Channels;
using Mda.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Mda.Api.Galeria;

// Genera los derivados (mini, media y grande) en segundo plano: al arrancar y cada vez que se publica o cambia una foto.
// Así ninguna visita paga los ~2 s de decodificar la foto la primera vez.
public sealed class Pregenerar(IServiceScopeFactory ambitos, Derivados derivados, ILogger<Pregenerar> log) : BackgroundService
{
    private readonly Channel<FotoPublica?> _cola = Channel.CreateUnbounded<FotoPublica?>();

    // una foto concreta; null = repasar todas las publicadas
    public void Encolar(FotoPublica? f = null) => _cola.Writer.TryWrite(f is null ? null : new FotoPublica { Id = f.Id, Raiz = f.Raiz, Ruta = f.Ruta, Hash = f.Hash });

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Encolar();
        try
        {
            await foreach (var f in _cola.Reader.ReadAllAsync(ct))
            {
                try
                {
                    var fotos = f is not null ? [f] : await Publicadas(ct);
                    foreach (var foto in fotos)
                        foreach (var tam in Derivados.Tamanos.Keys)
                            if (!derivados.Existe(foto, tam)) await derivados.Obtener(foto, tam, ct);
                }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "No se pudieron pregenerar las imágenes"); }
            }
        }
        catch (OperationCanceledException) { /* apagando */ }
    }

    private async Task<List<FotoPublica>> Publicadas(CancellationToken ct)
    {
        using var scope = ambitos.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().FotosPublicas.AsNoTracking().Where(x => x.Publicada).ToListAsync(ct);
    }
}
