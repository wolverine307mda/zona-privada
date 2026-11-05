using Mda.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Mda.Api.Tests;

// Una base de datos en memoria para probar lógica sin levantar la API. IMPORTANTE: EF guarda el modelo de AppDbContext para todo el proceso, y el de Identity
// (passkeys incluidos) depende de las opciones de Identity del proveedor de servicios; si el primer contexto que se crea no las tiene, el modelo queda sin passkeys
// y fallan, según el orden en que corran las pruebas, las de la API. Por eso todo contexto suelto de las pruebas se crea con esto.
public static class BaseDeDatos
{
    private static readonly IServiceProvider Servicios = new ServiceCollection()
        .AddSingleton<IOptions<IdentityOptions>>(Options.Create(new IdentityOptions { Stores = { SchemaVersion = IdentitySchemaVersions.Version3 } }))
        .BuildServiceProvider();

    public static AppDbContext Nueva() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).UseApplicationServiceProvider(Servicios).Options);
}
